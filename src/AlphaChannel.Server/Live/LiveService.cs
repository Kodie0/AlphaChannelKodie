using System.Security.Cryptography;
using System.Text;
using AlphaChannel.Contracts;
using AlphaChannel.Server.Data;
using AlphaChannel.Server.Social;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Live;

// Backs "Go Live" self-hosted streaming. MediaMTX (RTMP-in/HLS-out, see docker-compose.yml) calls
// back into the media-only endpoint group (LiveEndpoints) to authenticate publishes and track
// live/offline state - MediaMTX's own publisher state is the source of truth for "live" (see
// LiveSession's doc comment), not a client-invoked start/stop button, since OBS can crash or lose
// connection without the plugin ever hearing about it.
//
// Publish path/query split: the RTMP path is just "live/{accountId}" - stable, and becomes the
// public HLS playback path too, since MediaMTX ties publish and read to the same path string
// (confirmed against its own docs, no path-aliasing feature to lean on instead). The secret travels
// as a query string ("?key={secret}") that only ever reaches the publish-auth webhook, never the
// HLS URL viewers use. Getting this split wrong would leak a streamer's own stream key to anyone
// who watches - MediaMTX's authHTTPAddress payload carries "path" and "query" as separate fields
// specifically to make this split possible.
internal sealed class LiveService(
    IDbContextFactory<AlphaChannelDbContext> dbFactory, ActivityService activity,
    PresenceService presence, UserDirectory directory, LiveDirectory liveDirectory, IConfiguration configuration)
{
    public async Task<string> RotateKeyAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var secret = GenerateSecret();
        var hash = Hash(secret);

        var existing = await db.StreamKeys.FirstOrDefaultAsync(k => k.AccountId == accountId, cancellationToken);
        if (existing is null)
        {
            db.StreamKeys.Add(new StreamKey { Id = Guid.NewGuid(), AccountId = accountId, KeyHash = hash, CreatedAtUtc = DateTime.UtcNow });
        }
        else
        {
            existing.KeyHash = hash;
            existing.RotatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Regenerating invalidates the old key immediately (any OBS session pushing with it starts
        // failing AuthenticatePublishAsync on its next reconnect) - that's a deliberate security
        // feature, not just recovery-path friction.
        return $"{accountId}?key={secret}";
    }

    public async Task<LiveStatusDto> GetMyStatusAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var hasKey = await db.StreamKeys.AnyAsync(k => k.AccountId == accountId, cancellationToken);
        var isLive = await db.LiveSessions.AnyAsync(s => s.AccountId == accountId && s.EndedAtUtc == null, cancellationToken);
        return new LiveStatusDto(hasKey, isLive, isLive ? BuildHlsUrl(accountId) : null);
    }

    public async Task<List<LiveFriendDto>> GetFriendsLiveAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var friendIds = await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted && (f.RequesterAccountId == accountId || f.AddresseeAccountId == accountId))
            .Select(f => f.RequesterAccountId == accountId ? f.AddresseeAccountId : f.RequesterAccountId)
            .ToListAsync(cancellationToken);
        if (friendIds.Count == 0)
        {
            return [];
        }

        var sessions = await db.LiveSessions
            .Where(s => s.EndedAtUtc == null && friendIds.Contains(s.AccountId))
            .ToListAsync(cancellationToken);
        if (sessions.Count == 0)
        {
            return [];
        }

        var liveIds = sessions.Select(s => s.AccountId).ToList();
        var accounts = (await db.Accounts.Where(a => liveIds.Contains(a.Id)).ToListAsync(cancellationToken)).ToDictionary(a => a.Id);
        var sessionByAccount = sessions.ToDictionary(s => s.AccountId);

        return liveIds
            .Where(accounts.ContainsKey)
            .Select(id => new LiveFriendDto(
                id.ToString(), accounts[id].DisplayName, BuildHlsUrl(id), ToUnixSeconds(sessionByAccount[id].StartedAtUtc)))
            .ToList();
    }

    // Only ever called for a "publish" action - LiveEndpoints short-circuits reads/playback to
    // always-allow before this is reached (confirmed decision: unauthenticated once you have the
    // link, same trust model as every other shareable URL in this app).
    public async Task<bool> AuthenticatePublishAsync(string path, string? query, CancellationToken cancellationToken)
    {
        if (!TryParseAccountId(path, out var accountId) || query is null)
        {
            return false;
        }

        var secret = ParseKeyFromQuery(query);
        if (secret is null)
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var key = await db.StreamKeys.FirstOrDefaultAsync(k => k.AccountId == accountId, cancellationToken);
        return key is not null && key.KeyHash == Hash(secret);
    }

    public async Task MarkLiveAsync(string path, CancellationToken cancellationToken)
    {
        if (!TryParseAccountId(path, out var accountId))
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var alreadyLive = await db.LiveSessions.AnyAsync(s => s.AccountId == accountId && s.EndedAtUtc == null, cancellationToken);
        if (alreadyLive)
        {
            liveDirectory.SetLive(accountId.ToString());
            return;
        }

        db.LiveSessions.Add(new LiveSession { Id = Guid.NewGuid(), AccountId = accountId, StartedAtUtc = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent /ready webhook already inserted the open session (unique filtered index on
            // AccountId WHERE EndedAtUtc IS NULL). Warm the cache and skip the duplicate fanout.
            liveDirectory.SetLive(accountId.ToString());
            return;
        }

        liveDirectory.SetLive(accountId.ToString());

        // Friends-fanout only (no TargetAccountId) - "went live" is "something a friend did," shown
        // in friends' Activity feeds the same way StartedWatching/VenueSaved already are.
        await activity.RecordAsync(accountId, ActivityEventType.WentLive, null, cancellationToken);
        await PushPresenceAsync(accountId, cancellationToken);
    }

    public async Task MarkOfflineAsync(string path, CancellationToken cancellationToken)
    {
        if (!TryParseAccountId(path, out var accountId))
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var sessions = await db.LiveSessions
            .Where(s => s.AccountId == accountId && s.EndedAtUtc == null)
            .ToListAsync(cancellationToken);
        if (sessions.Count == 0)
        {
            liveDirectory.SetOffline(accountId.ToString());
            return;
        }

        var endedAt = DateTime.UtcNow;
        foreach (var session in sessions)
        {
            session.EndedAtUtc = endedAt;
        }

        await db.SaveChangesAsync(cancellationToken);

        liveDirectory.SetOffline(accountId.ToString());
        await PushPresenceAsync(accountId, cancellationToken);
    }

    // Reuses PresenceService's existing push (the same one connect/disconnect/watch-along already
    // drive) rather than inventing a new signal type - it recomputes PresenceLabels.WatchingLabel
    // (now live-aware via LiveDirectory) and pushes to friends only if the label actually changed.
    // Live publishers may only have OBS on MediaMTX (no /rt socket) — treat "currently live" as
    // online so friends still get WatchingLabel = "Live now" instead of Online=false with no label.
    private async Task PushPresenceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var idString = accountId.ToString();
        var online = liveDirectory.IsLive(idString) || directory.TryGetSocket(idString, out _);
        await presence.NotifyAsync(idString, online, cancellationToken);
    }

    private string BuildHlsUrl(Guid accountId)
    {
        // docker-compose sets CDN_HOSTNAME="" until a pull zone exists — empty string is not null,
        // so coalesce with IsNullOrWhiteSpace or friends get https:///live/... m3u8 links.
        var cdnHost = configuration["CDN_HOSTNAME"];
        if (string.IsNullOrWhiteSpace(cdnHost))
        {
            cdnHost = configuration["RELAY_DOMAIN"];
        }

        return $"https://{cdnHost}/live/{accountId}/index.m3u8";
    }

    private static bool TryParseAccountId(string path, out Guid accountId)
    {
        var segment = path.Split('/').LastOrDefault() ?? string.Empty;
        return Guid.TryParse(segment, out accountId);
    }

    private static string? ParseKeyFromQuery(string query)
    {
        if (query.Length == 0)
        {
            return null;
        }

        var parsed = QueryHelpers.ParseQuery(query.TrimStart('?'));
        return parsed.TryGetValue("key", out var values) ? values.ToString() : null;
    }

    private static string GenerateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string Hash(string value) => Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(value)));

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
