using AlphaChannel.Contracts;
using AlphaChannel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Social;

internal enum SendFriendRequestResult
{
    Sent,
    NotFound,
    AlreadyFriends,
    AlreadyPending,
}

// IDbContextFactory rather than a plain DbContext, matching AccountService's reasoning - this
// service pushes over sockets via UserDirectory too, and staying consistent means it's safe to
// call from a future singleton (e.g. PresenceService) without a rewrite.
internal sealed class FriendService(
    IDbContextFactory<AlphaChannelDbContext> dbFactory, UserDirectory directory, RoomManager rooms, ActivityService activity)
{
    // Deliberately returns the same "not found" for a genuinely-missing handle, a blocked account,
    // and (per the Lalafell visibility preference) a Lalafell account this caller has opted not to
    // see - none of those should be distinguishable from probing handles.
    public async Task<Account?> FindAccountByHandleAsync(string handle, Guid callerAccountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Handle == handle, cancellationToken);
        if (account is null)
        {
            return null;
        }

        if (await IsBlockedEitherWayAsync(db, callerAccountId, account.Id, cancellationToken))
        {
            return null;
        }

        var caller = await db.Accounts.FirstAsync(a => a.Id == callerAccountId, cancellationToken);
        var settings = await GetSettingsAsync(db, cancellationToken);
        return LalafellVisibility.IsHiddenFrom(caller, account, settings) ? null : account;
    }

    public async Task<List<FriendDto>> GetFriendsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var friendships = await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted && (f.RequesterAccountId == accountId || f.AddresseeAccountId == accountId))
            .ToListAsync(cancellationToken);

        var friendIds = friendships
            .Select(f => f.RequesterAccountId == accountId ? f.AddresseeAccountId : f.RequesterAccountId)
            .ToHashSet();

        var caller = await db.Accounts.FirstAsync(a => a.Id == accountId, cancellationToken);
        var settings = await GetSettingsAsync(db, cancellationToken);
        var accounts = await db.Accounts.Where(a => friendIds.Contains(a.Id)).ToListAsync(cancellationToken);

        return accounts
            .Where(a => !LalafellVisibility.IsHiddenFrom(caller, a, settings))
            .Select(a => new FriendDto(a.Id.ToString(), a.Handle, a.DisplayName,
                directory.TryGetSocket(a.Id.ToString(), out _),
                PresenceLabels.WatchingLabel(a.Id.ToString(), rooms, directory)))
            .ToList();
    }

    public async Task<FriendRequestsPage> GetRequestsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var incoming = await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Pending && f.AddresseeAccountId == accountId)
            .ToListAsync(cancellationToken);
        var outgoing = await db.Friendships
            .Where(f => f.Status == FriendshipStatus.Pending && f.RequesterAccountId == accountId)
            .ToListAsync(cancellationToken);

        var otherIds = incoming.Select(f => f.RequesterAccountId)
            .Concat(outgoing.Select(f => f.AddresseeAccountId))
            .Distinct()
            .ToList();
        var accounts = (await db.Accounts.Where(a => otherIds.Contains(a.Id)).ToListAsync(cancellationToken))
            .ToDictionary(a => a.Id);

        FriendRequestDto? MapIncoming(Friendship f) => accounts.TryGetValue(f.RequesterAccountId, out var other)
            ? new FriendRequestDto(f.Id.ToString(), other.Id.ToString(), other.Handle, other.DisplayName, ToUnixSeconds(f.CreatedAtUtc))
            : null;

        FriendRequestDto? MapOutgoing(Friendship f) => accounts.TryGetValue(f.AddresseeAccountId, out var other)
            ? new FriendRequestDto(f.Id.ToString(), other.Id.ToString(), other.Handle, other.DisplayName, ToUnixSeconds(f.CreatedAtUtc))
            : null;

        return new FriendRequestsPage(
            incoming.Select(MapIncoming).OfType<FriendRequestDto>().ToArray(),
            outgoing.Select(MapOutgoing).OfType<FriendRequestDto>().ToArray());
    }

    public async Task<SendFriendRequestResult> SendRequestAsync(Guid requesterId, string recipientHandle, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var recipient = await db.Accounts.FirstOrDefaultAsync(a => a.Handle == recipientHandle, cancellationToken);
        if (recipient is null || recipient.Id == requesterId)
        {
            return SendFriendRequestResult.NotFound;
        }

        if (await IsBlockedEitherWayAsync(db, requesterId, recipient.Id, cancellationToken))
        {
            return SendFriendRequestResult.NotFound;
        }

        var requesterAccount = await db.Accounts.FirstAsync(a => a.Id == requesterId, cancellationToken);
        var visibilitySettings = await GetSettingsAsync(db, cancellationToken);
        if (LalafellVisibility.IsHiddenFrom(requesterAccount, recipient, visibilitySettings))
        {
            return SendFriendRequestResult.NotFound;
        }

        var existing = await db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterAccountId == requesterId && f.AddresseeAccountId == recipient.Id) ||
            (f.RequesterAccountId == recipient.Id && f.AddresseeAccountId == requesterId), cancellationToken);

        if (existing is not null)
        {
            return existing.Status == FriendshipStatus.Accepted ? SendFriendRequestResult.AlreadyFriends : SendFriendRequestResult.AlreadyPending;
        }

        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterAccountId = requesterId,
            AddresseeAccountId = recipient.Id,
            Status = FriendshipStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Friendships.Add(friendship);
        await db.SaveChangesAsync(cancellationToken);

        await PushAsync(recipient.Id, new SocialControl
        {
            Type = SocialSignalType.FriendRequestReceived,
            AccountId = requesterId.ToString(),
            DisplayName = requesterAccount.DisplayName,
            RequestId = friendship.Id.ToString(),
        }, cancellationToken);

        return SendFriendRequestResult.Sent;
    }

    public async Task<bool> AcceptRequestAsync(Guid requestId, Guid callerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var request = await db.Friendships.FirstOrDefaultAsync(
            f => f.Id == requestId && f.AddresseeAccountId == callerId && f.Status == FriendshipStatus.Pending, cancellationToken);
        if (request is null)
        {
            return false;
        }

        request.Status = FriendshipStatus.Accepted;
        request.RespondedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var accepter = await db.Accounts.FirstAsync(a => a.Id == callerId, cancellationToken);
        await PushAsync(request.RequesterAccountId, new SocialControl
        {
            Type = SocialSignalType.FriendAccepted,
            AccountId = callerId.ToString(),
            DisplayName = accepter.DisplayName,
            RequestId = request.Id.ToString(),
        }, cancellationToken);

        await activity.RecordAsync(callerId, ActivityEventType.FriendAccepted, null, cancellationToken);
        await activity.RecordAsync(request.RequesterAccountId, ActivityEventType.FriendAccepted, null, cancellationToken);

        return true;
    }

    public async Task<bool> DeclineRequestAsync(Guid requestId, Guid callerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var request = await db.Friendships.FirstOrDefaultAsync(
            f => f.Id == requestId && f.AddresseeAccountId == callerId && f.Status == FriendshipStatus.Pending, cancellationToken);
        if (request is null)
        {
            return false;
        }

        db.Friendships.Remove(request);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RemoveFriendAsync(Guid callerId, Guid otherId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var friendship = await db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterAccountId == callerId && f.AddresseeAccountId == otherId) ||
            (f.RequesterAccountId == otherId && f.AddresseeAccountId == callerId), cancellationToken);
        if (friendship is null)
        {
            return;
        }

        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync(cancellationToken);

        await PushAsync(otherId, new SocialControl { Type = SocialSignalType.FriendRemoved, AccountId = callerId.ToString() }, cancellationToken);
    }

    public async Task<List<AccountSummaryDto>> GetBlocksAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var blockedIds = await db.Blocks.Where(b => b.BlockerAccountId == accountId).Select(b => b.BlockedAccountId).ToListAsync(cancellationToken);
        var blocked = await db.Accounts.Where(a => blockedIds.Contains(a.Id)).ToListAsync(cancellationToken);
        return blocked.Select(a => new AccountSummaryDto(a.Id.ToString(), a.Handle, a.DisplayName)).ToList();
    }

    // Hides bidirectionally and completely: removes any existing friendship/pending request in
    // both directions (so FindAccountByHandleAsync/SendRequestAsync's IsBlockedEitherWayAsync
    // checks take effect immediately, not just for brand-new interactions), and DM history is
    // preserved rather than deleted - only new sends are rejected (see DmService.SendMessageAsync).
    public async Task BlockAsync(Guid callerId, Guid targetId, CancellationToken cancellationToken)
    {
        if (callerId == targetId)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var alreadyBlocked = await db.Blocks.AnyAsync(b => b.BlockerAccountId == callerId && b.BlockedAccountId == targetId, cancellationToken);
        if (!alreadyBlocked)
        {
            db.Blocks.Add(new Block { Id = Guid.NewGuid(), BlockerAccountId = callerId, BlockedAccountId = targetId, CreatedAtUtc = DateTime.UtcNow });
        }

        var friendship = await db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterAccountId == callerId && f.AddresseeAccountId == targetId) ||
            (f.RequesterAccountId == targetId && f.AddresseeAccountId == callerId), cancellationToken);
        if (friendship is not null)
        {
            db.Friendships.Remove(friendship);
        }

        // Also severs Tweeter follows both ways - blocking should mean "no contact at all", not
        // just "no friendship."
        var follows = await db.Follows.Where(f =>
            (f.FollowerAccountId == callerId && f.FolloweeAccountId == targetId) ||
            (f.FollowerAccountId == targetId && f.FolloweeAccountId == callerId)).ToListAsync(cancellationToken);
        db.Follows.RemoveRange(follows);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UnblockAsync(Guid callerId, Guid targetId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var block = await db.Blocks.FirstOrDefaultAsync(b => b.BlockerAccountId == callerId && b.BlockedAccountId == targetId, cancellationToken);
        if (block is null)
        {
            return;
        }

        db.Blocks.Remove(block);
        await db.SaveChangesAsync(cancellationToken);
    }

    // The singleton settings row is seeded via migration (see AlphaChannelDbContext.OnModelCreating)
    // so this should always find it, but falls back to defaults rather than throwing if it's ever
    // missing - a missing settings row shouldn't be able to break every friend-related endpoint.
    private static async Task<ServerSettings> GetSettingsAsync(AlphaChannelDbContext db, CancellationToken cancellationToken) =>
        await db.Settings.FirstOrDefaultAsync(s => s.Id == ServerSettings.SingletonId, cancellationToken) ?? new ServerSettings();

    private static Task<bool> IsBlockedEitherWayAsync(AlphaChannelDbContext db, Guid a, Guid b, CancellationToken cancellationToken) =>
        db.Blocks.AnyAsync(x =>
            (x.BlockerAccountId == a && x.BlockedAccountId == b) ||
            (x.BlockerAccountId == b && x.BlockedAccountId == a), cancellationToken);

    private async Task PushAsync(Guid toAccountId, SocialControl message, CancellationToken cancellationToken)
    {
        if (directory.TryGetSocket(toAccountId.ToString(), out var socket) && socket is not null)
        {
            await SocketSend.SendAsync(socket, message, cancellationToken).ConfigureAwait(false);
        }
    }

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
