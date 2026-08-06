using AlphaChannel.Contracts;
using AlphaChannel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Social;

// "What plugins does my friend run?" - auto-detected client-side (IDalamudPluginInterface.
// InstalledPlugins), never manually curated. Friends-only, same posture as the rest of the social
// surface (DmService.StartConversationAsync's own comment on why an accepted friendship is the
// bar here rather than a separate permission).
internal sealed class PluginHubService(IDbContextFactory<AlphaChannelDbContext> dbFactory)
{
    // Wholesale replace rather than diffing add/remove - an installed-plugin list is small (tens of
    // rows) and changes rarely, so "delete everything for this account, insert the current set" is
    // simpler and self-healing (a client that failed to report a removal one time doesn't leave a
    // permanently stale row behind).
    public async Task SyncAsync(Guid accountId, InstalledPluginDto[] plugins, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var existing = db.InstalledPlugins.Where(p => p.AccountId == accountId);
        db.InstalledPlugins.RemoveRange(existing);

        var now = DateTime.UtcNow;
        db.InstalledPlugins.AddRange(plugins.Select(p => new InstalledPlugin
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            InternalName = p.InternalName,
            Name = p.Name,
            Version = p.Version,
            UpdatedAtUtc = now,
        }));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Null distinguishes "not friends" (caller shouldn't see anything, not even an empty list -
    // same "don't distinguish missing from denied" reasoning as FriendService.
    // FindAccountByDisplayNameAsync) from "friends but nothing installed" (empty array).
    public async Task<List<InstalledPluginDto>?> GetFriendPluginsAsync(Guid callerId, Guid friendAccountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var isFriend = await db.Friendships.AnyAsync(f =>
            f.Status == FriendshipStatus.Accepted &&
            ((f.RequesterAccountId == callerId && f.AddresseeAccountId == friendAccountId) ||
             (f.RequesterAccountId == friendAccountId && f.AddresseeAccountId == callerId)), cancellationToken);
        if (!isFriend)
        {
            return null;
        }

        return await db.InstalledPlugins
            .Where(p => p.AccountId == friendAccountId)
            .OrderBy(p => p.Name)
            .Select(p => new InstalledPluginDto(p.InternalName, p.Name, p.Version))
            .ToListAsync(cancellationToken);
    }
}
