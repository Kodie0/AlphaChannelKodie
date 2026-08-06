using AlphaChannel.Contracts;
using AlphaChannel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Social;

internal enum CreateConversationResult
{
    Ok,
    NotFriends,
    Blocked,
    InvalidMembers,
}

// Server-blind relay for E2E DMs and group chats - every method here only ever touches ciphertext/
// nonce/tag/ConversationId/AccountId, never plaintext or a key. See DmMessage's doc comment for the
// crypto scheme (static-static ECDH + sender-side fan-out for groups) and KeyEndpoints for the two
// public-key endpoints this depends on.
internal sealed class DmService(IDbContextFactory<AlphaChannelDbContext> dbFactory, UserDirectory directory)
{
    private const int MessagePageLimit = 50;

    // Requires an existing accepted friendship with every member being added - simplest, safest
    // default given there's no separate spam/rate-limit system beyond friends+blocks. A single
    // member reuses (or creates) the one 1:1 conversation with that pair, same "start or resume"
    // behavior as before; two or more members always creates a brand-new group - unlike a 1:1,
    // there's no natural "the" group for a given member set, so no dedup.
    public async Task<(CreateConversationResult Result, Guid? ConversationId)> CreateConversationAsync(
        Guid callerId, string[] memberAccountIds, string? name, CancellationToken cancellationToken)
    {
        var memberIds = memberAccountIds.Distinct().Where(id => id != callerId.ToString())
            .Select(id => Guid.TryParse(id, out var g) ? g : (Guid?)null).ToList();
        if (memberIds.Count == 0 || memberIds.Any(id => id is null))
        {
            return (CreateConversationResult.InvalidMembers, null);
        }

        var members = memberIds.Select(id => id!.Value).ToList();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        foreach (var memberId in members)
        {
            var isFriend = await db.Friendships.AnyAsync(f =>
                f.Status == FriendshipStatus.Accepted &&
                ((f.RequesterAccountId == callerId && f.AddresseeAccountId == memberId) ||
                 (f.RequesterAccountId == memberId && f.AddresseeAccountId == callerId)), cancellationToken);
            if (!isFriend)
            {
                return (CreateConversationResult.NotFriends, null);
            }

            if (await IsBlockedEitherWayAsync(db, callerId, memberId, cancellationToken))
            {
                return (CreateConversationResult.Blocked, null);
            }
        }

        var isGroup = members.Count > 1;
        if (!isGroup)
        {
            var otherId = members[0];
            var myConversationIds = await db.ConversationMembers.Where(m => m.AccountId == callerId)
                .Select(m => m.ConversationId).ToListAsync(cancellationToken);
            var sharedConversationIds = await db.ConversationMembers
                .Where(m => m.AccountId == otherId && myConversationIds.Contains(m.ConversationId))
                .Select(m => m.ConversationId).ToListAsync(cancellationToken);

            var existingId = await db.Conversations
                .Where(c => sharedConversationIds.Contains(c.Id) && !c.IsGroup)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingId is { } found)
            {
                return (CreateConversationResult.Ok, found);
            }
        }

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            IsGroup = isGroup,
            Name = isGroup ? (string.IsNullOrWhiteSpace(name) ? "Group chat" : name.Trim()[..Math.Min(name.Trim().Length, 48)]) : null,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Conversations.Add(conversation);

        var now = DateTime.UtcNow;
        db.ConversationMembers.Add(new ConversationMember { Id = Guid.NewGuid(), ConversationId = conversation.Id, AccountId = callerId, JoinedAtUtc = now });
        foreach (var memberId in members)
        {
            db.ConversationMembers.Add(new ConversationMember { Id = Guid.NewGuid(), ConversationId = conversation.Id, AccountId = memberId, JoinedAtUtc = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return (CreateConversationResult.Ok, conversation.Id);
    }

    public async Task<List<ConversationSummaryDto>> GetConversationsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var myMemberships = await db.ConversationMembers.Where(m => m.AccountId == accountId).ToListAsync(cancellationToken);
        var conversationIds = myMemberships.Select(m => m.ConversationId).ToList();
        var conversations = await db.Conversations.Where(c => conversationIds.Contains(c.Id)).ToListAsync(cancellationToken);

        var otherMembers = await db.ConversationMembers
            .Where(m => conversationIds.Contains(m.ConversationId) && m.AccountId != accountId)
            .ToListAsync(cancellationToken);
        var otherAccountIds = otherMembers.Select(m => m.AccountId).Distinct().ToList();
        var accounts = (await db.Accounts.Where(a => otherAccountIds.Contains(a.Id)).ToListAsync(cancellationToken)).ToDictionary(a => a.Id);

        var myCursorByConversation = myMemberships.ToDictionary(m => m.ConversationId, m => m.LastReadAtUtc ?? DateTime.MinValue);

        var result = new List<ConversationSummaryDto>();
        foreach (var conversation in conversations)
        {
            var members = otherMembers
                .Where(m => m.ConversationId == conversation.Id && accounts.ContainsKey(m.AccountId))
                .Select(m => new ConversationMemberDto(m.AccountId.ToString(), accounts[m.AccountId].Handle, accounts[m.AccountId].DisplayName))
                .ToArray();
            if (members.Length == 0)
            {
                continue;
            }

            var lastMessageAt = await db.DmMessages
                .Where(m => m.ConversationId == conversation.Id)
                .OrderByDescending(m => m.SentAtUtc)
                .Select(m => (DateTime?)m.SentAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var myCursor = myCursorByConversation[conversation.Id];
            var unread = await db.DmMessages
                .Where(m => m.ConversationId == conversation.Id && m.RecipientAccountId == accountId && m.SentAtUtc > myCursor)
                .Select(m => m.GroupId).Distinct().CountAsync(cancellationToken);

            result.Add(new ConversationSummaryDto(
                conversation.Id.ToString(), conversation.IsGroup, conversation.Name, members,
                lastMessageAt is { } sent ? ToUnixSeconds(sent) : null, unread));
        }

        return result.OrderByDescending(c => c.LastMessageAtUnix ?? 0).ToList();
    }

    public async Task<MessagePage?> GetMessagesAsync(Guid conversationId, Guid viewerId, long? beforeUnix, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var membership = await db.ConversationMembers.FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.AccountId == viewerId, cancellationToken);
        if (membership is null)
        {
            return null;
        }

        var conversation = await db.Conversations.FirstAsync(c => c.Id == conversationId, cancellationToken);

        var relevantQuery = db.DmMessages.Where(m =>
            m.ConversationId == conversationId && (m.RecipientAccountId == viewerId || m.SenderAccountId == viewerId));
        if (beforeUnix is { } before)
        {
            var beforeDate = DateTimeOffset.FromUnixTimeSeconds(before).UtcDateTime;
            relevantQuery = relevantQuery.Where(m => m.SentAtUtc < beforeDate);
        }

        // Paginate by distinct logical message (GroupId), not physical row - a group message the
        // viewer sent has one physical row per other recipient, all sharing a GroupId and an
        // identical SentAtUtc (see DmMessage's doc comment).
        var page = await relevantQuery
            .GroupBy(m => m.GroupId)
            .Select(g => new { GroupId = g.Key, SentAtUtc = g.Max(m => m.SentAtUtc) })
            .OrderByDescending(g => g.SentAtUtc)
            .Take(MessagePageLimit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > MessagePageLimit;
        page = page.Take(MessagePageLimit).ToList();
        var pageGroupIds = page.Select(g => g.GroupId).ToHashSet();

        var rows = await relevantQuery.Where(m => pageGroupIds.Contains(m.GroupId)).ToListAsync(cancellationToken);

        // The viewer's own addressed copy if they're a recipient; otherwise (they're the sender)
        // the deterministic-lowest-RecipientAccountId row, so this always agrees with whichever row
        // SendMessageAsync returned them at send time (see that method's own comment).
        var representative = rows.GroupBy(m => m.GroupId)
            .Select(g => g.FirstOrDefault(m => m.RecipientAccountId == viewerId) ?? g.OrderBy(m => m.RecipientAccountId).First())
            .OrderByDescending(m => m.SentAtUtc)
            .ToList();

        DateTime? otherMemberLastRead = null;
        if (!conversation.IsGroup)
        {
            var otherMember = await db.ConversationMembers.FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.AccountId != viewerId, cancellationToken);
            otherMemberLastRead = otherMember?.LastReadAtUtc;
        }

        var items = representative.Select(m => ToDto(m, viewerId, conversation.IsGroup, otherMemberLastRead)).ToArray();
        var nextCursor = hasMore && representative.Count > 0 ? ToUnixSeconds(representative[^1].SentAtUtc).ToString() : null;
        return new MessagePage(items, nextCursor);
    }

    public async Task<MessageDto?> SendMessageAsync(Guid conversationId, Guid senderId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        if (request.Envelopes.Length == 0)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var allMemberIds = await db.ConversationMembers.Where(m => m.ConversationId == conversationId).Select(m => m.AccountId).ToListAsync(cancellationToken);
        if (!allMemberIds.Contains(senderId))
        {
            return null;
        }

        var expectedRecipients = allMemberIds.Where(id => id != senderId).ToHashSet();
        var envelopeRecipients = new HashSet<Guid>();
        foreach (var envelope in request.Envelopes)
        {
            if (!Guid.TryParse(envelope.RecipientAccountId, out var recipientId))
            {
                return null;
            }

            envelopeRecipients.Add(recipientId);
        }

        // Exactly one envelope per other member, no more, no less - a partial fan-out would leave
        // some members permanently unable to decrypt this message.
        if (!envelopeRecipients.SetEquals(expectedRecipients))
        {
            return null;
        }

        foreach (var recipientId in expectedRecipients)
        {
            if (await IsBlockedEitherWayAsync(db, senderId, recipientId, cancellationToken))
            {
                return null;
            }
        }

        var groupId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var rows = request.Envelopes.Select(e => new DmMessage
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            ConversationId = conversationId,
            SenderAccountId = senderId,
            RecipientAccountId = Guid.Parse(e.RecipientAccountId),
            Ciphertext = Convert.FromBase64String(e.Ciphertext),
            Nonce = Convert.FromBase64String(e.Nonce),
            Tag = Convert.FromBase64String(e.Tag),
            CommitmentTag = Convert.FromBase64String(e.CommitmentTag),
            SentAtUtc = now,
        }).ToList();
        db.DmMessages.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (directory.TryGetSocket(row.RecipientAccountId.ToString(), out var socket) && socket is not null)
            {
                await SocketSend.SendAsync(socket, new SocialControl
                {
                    Type = SocialSignalType.DmMessage,
                    AccountId = senderId.ToString(),
                    ConversationId = conversationId.ToString(),
                    MessageId = row.Id.ToString(),
                    Ciphertext = Convert.ToBase64String(row.Ciphertext),
                    Nonce = Convert.ToBase64String(row.Nonce),
                    Tag = Convert.ToBase64String(row.Tag),
                    CommitmentTag = Convert.ToBase64String(row.CommitmentTag),
                    TimestampUnix = ToUnixSeconds(row.SentAtUtc),
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        // Deterministic-lowest-RecipientAccountId, matching GetMessagesAsync's own representative
        // pick for a message the viewer sent - same Id shows up on reload, not an arbitrary one.
        var representative = rows.OrderBy(m => m.RecipientAccountId).First();
        return ToDto(representative, senderId, conversation.IsGroup, otherMemberLastReadAtUtc: null);
    }

    public async Task MarkReadAsync(Guid conversationId, Guid callerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var membership = await db.ConversationMembers.FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.AccountId == callerId, cancellationToken);
        if (membership is null)
        {
            return;
        }

        membership.LastReadAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    // ReadAtUnix is only meaningful for a message the viewer themselves sent, in a 1:1 conversation
    // (a group has no single "the other member" whose read cursor would apply) - see MessageDto's
    // own doc comment.
    private static MessageDto ToDto(DmMessage message, Guid viewerId, bool isGroup, DateTime? otherMemberLastReadAtUtc)
    {
        long? readAtUnix = null;
        if (!isGroup && message.SenderAccountId == viewerId && otherMemberLastReadAtUtc is { } read && read >= message.SentAtUtc)
        {
            readAtUnix = ToUnixSeconds(read);
        }

        return new MessageDto(
            message.Id.ToString(), message.GroupId.ToString(), message.SenderAccountId.ToString(), message.RecipientAccountId.ToString(),
            Convert.ToBase64String(message.Ciphertext), Convert.ToBase64String(message.Nonce), Convert.ToBase64String(message.Tag),
            ToUnixSeconds(message.SentAtUtc), readAtUnix);
    }

    private static Task<bool> IsBlockedEitherWayAsync(AlphaChannelDbContext db, Guid a, Guid b, CancellationToken cancellationToken) =>
        db.Blocks.AnyAsync(x => (x.BlockerAccountId == a && x.BlockedAccountId == b) || (x.BlockerAccountId == b && x.BlockedAccountId == a), cancellationToken);

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
