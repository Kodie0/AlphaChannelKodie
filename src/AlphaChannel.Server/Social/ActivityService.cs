using AlphaChannel.Contracts;
using AlphaChannel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Social;

// Friends-only by construction, not by a privacy check that could be misconfigured - GetFeedAsync
// only ever queries events belonging to the viewer or their accepted friends, there is no
// public/global feed query anywhere in this file.
internal sealed class ActivityService(IDbContextFactory<AlphaChannelDbContext> dbFactory, UserDirectory directory)
{
    private const int DefaultLimit = 30;

    public async Task RecordAsync(
        Guid actorAccountId, ActivityEventType type, string? metadata, CancellationToken cancellationToken,
        Guid? targetAccountId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.ActivityEvents.Add(new ActivityEvent
        {
            Id = Guid.NewGuid(),
            AccountId = actorAccountId,
            Type = type,
            Metadata = metadata,
            CreatedAtUtc = DateTime.UtcNow,
            TargetAccountId = targetAccountId,
        });
        await db.SaveChangesAsync(cancellationToken);

        var friendIds = await FriendIdsAsync(db, actorAccountId, cancellationToken);
        var pushTo = new HashSet<Guid>(friendIds);
        if (targetAccountId is { } target)
        {
            pushTo.Add(target);
        }

        foreach (var recipientId in pushTo)
        {
            if (directory.TryGetSocket(recipientId.ToString(), out var socket) && socket is not null)
            {
                await SocketSend.SendAsync(socket, new SocialControl
                {
                    Type = SocialSignalType.ActivityNew,
                    AccountId = actorAccountId.ToString(),
                    ActivityType = type.ToString(),
                }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<ActivityPage> GetFeedAsync(Guid viewerAccountId, long? beforeUnix, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var visibleActorIds = await FriendIdsAsync(db, viewerAccountId, cancellationToken);
        visibleActorIds.Add(viewerAccountId);

        var query = db.ActivityEvents.Where(e => visibleActorIds.Contains(e.AccountId) || e.TargetAccountId == viewerAccountId);
        if (beforeUnix is { } before)
        {
            var beforeDate = DateTimeOffset.FromUnixTimeSeconds(before).UtcDateTime;
            query = query.Where(e => e.CreatedAtUtc < beforeDate);
        }

        var events = await query.OrderByDescending(e => e.CreatedAtUtc).Take(DefaultLimit + 1).ToListAsync(cancellationToken);
        var hasMore = events.Count > DefaultLimit;
        events = events.Take(DefaultLimit).ToList();

        var viewer = await db.Accounts.FirstAsync(a => a.Id == viewerAccountId, cancellationToken);
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == ServerSettings.SingletonId, cancellationToken) ?? new ServerSettings();

        var actorIds = events.Select(e => e.AccountId).Distinct().ToList();
        var actors = (await db.Accounts.Where(a => actorIds.Contains(a.Id)).ToListAsync(cancellationToken)).ToDictionary(a => a.Id);

        var items = events
            .Where(e => actors.TryGetValue(e.AccountId, out var actor) && !LalafellVisibility.IsHiddenFrom(viewer, actor, settings))
            .Select(e =>
            {
                var actor = actors[e.AccountId];
                return new ActivityEventDto(e.Id.ToString(), actor.Id.ToString(), actor.Handle, actor.DisplayName,
                    e.Type.ToString(), e.Metadata, ToUnixSeconds(e.CreatedAtUtc));
            })
            .ToArray();

        var nextCursor = hasMore && events.Count > 0 ? ToUnixSeconds(events[^1].CreatedAtUtc).ToString() : null;
        return new ActivityPage(items, nextCursor);
    }

    // Powers the sidebar badge - a plain count rather than reusing GetFeedAsync's page, since the
    // badge just needs a number and doesn't want the actor-lookup/Lalafell-visibility join overhead
    // of building full ActivityEventDtos for events that'll never be displayed as such.
    public async Task<int> GetUnreadCountAsync(Guid viewerAccountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var visibleActorIds = await FriendIdsAsync(db, viewerAccountId, cancellationToken);
        visibleActorIds.Add(viewerAccountId);

        var marker = await db.ActivityReadMarkers.FirstOrDefaultAsync(m => m.AccountId == viewerAccountId, cancellationToken);
        var since = marker?.LastReadAtUtc ?? DateTime.MinValue;

        return await db.ActivityEvents.CountAsync(e =>
            (visibleActorIds.Contains(e.AccountId) || e.TargetAccountId == viewerAccountId) && e.CreatedAtUtc > since,
            cancellationToken);
    }

    public async Task MarkReadAsync(Guid accountId, long upToUnix, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var upToDate = DateTimeOffset.FromUnixTimeSeconds(upToUnix).UtcDateTime;
        var marker = await db.ActivityReadMarkers.FirstOrDefaultAsync(m => m.AccountId == accountId, cancellationToken);
        if (marker is null)
        {
            db.ActivityReadMarkers.Add(new ActivityReadMarker { AccountId = accountId, LastReadAtUtc = upToDate });
        }
        else if (upToDate > marker.LastReadAtUtc)
        {
            marker.LastReadAtUtc = upToDate;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<List<Guid>> FriendIdsAsync(AlphaChannelDbContext db, Guid accountId, CancellationToken cancellationToken) =>
        await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted && (f.RequesterAccountId == accountId || f.AddresseeAccountId == accountId))
            .Select(f => f.RequesterAccountId == accountId ? f.AddresseeAccountId : f.RequesterAccountId)
            .ToListAsync(cancellationToken);

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
