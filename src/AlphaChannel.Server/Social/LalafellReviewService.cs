using AlphaChannel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Social;

internal sealed record LalafellPendingDto(
    string AccountId,
    string Handle,
    string DisplayName,
    string CharacterName,
    string World,
    bool LodestoneRaceMismatch,
    string? SelfReportedRaces,
    long RequestedAtUnix);

// Admin-only surface (see Admin/AdminTokenFilter and the /admin/ui page). GetPendingAsync is the
// one place in the whole server that returns a verified character name/world to anyone other than
// the owning account itself - a deliberate, narrow exception for the human review this feature
// exists to support, not a leak.
internal sealed class LalafellReviewService(IDbContextFactory<AlphaChannelDbContext> dbFactory)
{
    public async Task<List<LalafellPendingDto>> GetPendingAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var pending = await db.Accounts
            .Where(a => a.LalafellSocialStatus == LalafellSocialStatus.Pending)
            .ToListAsync(cancellationToken);

        var accountIds = pending.Select(a => a.Id).ToList();
        var primaryCharacterByAccount = (await db.AccountCharacters
                .Where(c => accountIds.Contains(c.AccountId))
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.AccountId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.IsPrimary).First());

        return pending.Select(a =>
        {
            var character = primaryCharacterByAccount.GetValueOrDefault(a.Id);
            return new LalafellPendingDto(
                a.Id.ToString(),
                a.Handle,
                a.DisplayName,
                character?.CharacterName ?? "(unknown)",
                character?.World ?? "(unknown)",
                a.LodestoneRaceMismatch,
                a.SelfReportedRaces,
                ToUnixSeconds(a.CreatedAtUtc));
        }).ToList();
    }

    public Task<bool> ApproveAsync(Guid accountId, CancellationToken cancellationToken) =>
        SetStatusAsync(accountId, LalafellSocialStatus.Approved, cancellationToken);

    public Task<bool> DenyAsync(Guid accountId, CancellationToken cancellationToken) =>
        SetStatusAsync(accountId, LalafellSocialStatus.Denied, cancellationToken);

    public async Task<bool> GetHideFromNonLalafellAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == ServerSettings.SingletonId, cancellationToken);
        return settings?.HideLalafellFromNonLalafell ?? false;
    }

    public async Task SetHideFromNonLalafellAsync(bool enabled, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == ServerSettings.SingletonId, cancellationToken);
        if (settings is null)
        {
            settings = new ServerSettings { Id = ServerSettings.SingletonId };
            db.Settings.Add(settings);
        }

        settings.HideLalafellFromNonLalafell = enabled;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> SetStatusAsync(Guid accountId, LalafellSocialStatus status, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        account.LalafellSocialStatus = status;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
