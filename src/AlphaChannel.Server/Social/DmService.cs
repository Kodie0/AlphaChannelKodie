using AlphaChannel.Contracts;
using AlphaChannel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Social;

internal enum StartConversationResult
{
    Ok,
    NotFriends,
    NotFound,
}

// Server-blind relay for E2E DMs - every method here only ever touches ciphertext/nonce/tag/
// ConversationId/AccountId, never plaintext or a key. See DmMessage's doc comment for the crypto
// scheme (static-static ECDH, no server-side key material at all) and KeyEndpoints for the two
// public-key endpoints this depends on.
internal sealed class DmService(IDbContextFactory<AlphaChannelDbContext> dbFactory, UserDirectory directory)
{
    // DMs require an existing accepted friendship - simplest, safest default given there's no
    // separate spam/rate-limit system beyond friends+blocks in v1.
    public async Task<(StartConversationResult Result, Guid? ConversationId)> StartConversationAsync(
        Guid callerId, Guid otherId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var isFriend = await db.Friendships.AnyAsync(f =>
            f.Status == FriendshipStatus.Accepted &&
            ((f.RequesterAccountId == callerId && f.AddresseeAccountId == otherId) ||
             (f.RequesterAccountId == otherId && f.AddresseeAccountId == callerId)), cancellationToken);
        if (!isFriend)
        {
            return (StartConversationResult.NotFriends, null);
        }

        var (lowId, highId) = CanonicalPair(callerId, otherId);
        var conversation = await db.DmConversations.FirstOrDefaultAsync(
            c => c.AccountAId == lowId && c.AccountBId == highId, cancellationToken);

        if (conversation is not null)
        {
            return (StartConversationResult.Ok, conversation.Id);
        }

        conversation = new DmConversation { Id = Guid.NewGuid(), AccountAId = lowId, AccountBId = highId, CreatedAtUtc = DateTime.UtcNow };
        db.DmConversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return (StartConversationResult.Ok, conversation.Id);
    }

    public async Task<List<ConversationSummaryDto>> GetConversationsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var conversations = await db.DmConversations
            .Where(c => c.AccountAId == accountId || c.AccountBId == accountId)
            .ToListAsync(cancellationToken);

        var otherIds = conversations.Select(c => c.AccountAId == accountId ? c.AccountBId : c.AccountAId).ToList();
        var others = (await db.Accounts.Where(a => otherIds.Contains(a.Id)).ToListAsync(cancellationToken)).ToDictionary(a => a.Id);

        var result = new List<ConversationSummaryDto>();
        foreach (var conversation in conversations)
        {
            var otherId = conversation.AccountAId == accountId ? conversation.AccountBId : conversation.AccountAId;
            if (!others.TryGetValue(otherId, out var other))
            {
                continue;
            }

            var lastMessageAt = await db.DmMessages
                .Where(m => m.ConversationId == conversation.Id)
                .OrderByDescending(m => m.SentAtUtc)
                .Select(m => (DateTime?)m.SentAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var unread = await db.DmMessages.CountAsync(
                m => m.ConversationId == conversation.Id && m.SenderAccountId != accountId && m.ReadAtUtc == null, cancellationToken);

            result.Add(new ConversationSummaryDto(
                conversation.Id.ToString(), other.Id.ToString(), other.Handle, other.DisplayName,
                lastMessageAt is { } sent ? ToUnixSeconds(sent) : null, unread));
        }

        return result.OrderByDescending(c => c.LastMessageAtUnix ?? 0).ToList();
    }

    public async Task<MessagePage?> GetMessagesAsync(Guid conversationId, Guid callerId, long? beforeUnix, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var conversation = await db.DmConversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null || (conversation.AccountAId != callerId && conversation.AccountBId != callerId))
        {
            return null;
        }

        const int limit = 50;
        var query = db.DmMessages.Where(m => m.ConversationId == conversationId);
        if (beforeUnix is { } before)
        {
            var beforeDate = DateTimeOffset.FromUnixTimeSeconds(before).UtcDateTime;
            query = query.Where(m => m.SentAtUtc < beforeDate);
        }

        var messages = await query.OrderByDescending(m => m.SentAtUtc).Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = messages.Count > limit;
        messages = messages.Take(limit).ToList();

        var items = messages.Select(ToDto).ToArray();
        var nextCursor = hasMore && messages.Count > 0 ? ToUnixSeconds(messages[^1].SentAtUtc).ToString() : null;
        return new MessagePage(items, nextCursor);
    }

    public async Task<MessageDto?> SendMessageAsync(
        Guid conversationId, Guid senderId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var conversation = await db.DmConversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null || (conversation.AccountAId != senderId && conversation.AccountBId != senderId))
        {
            return null;
        }

        var recipientId = conversation.AccountAId == senderId ? conversation.AccountBId : conversation.AccountAId;

        var blocked = await db.Blocks.AnyAsync(b =>
            (b.BlockerAccountId == senderId && b.BlockedAccountId == recipientId) ||
            (b.BlockerAccountId == recipientId && b.BlockedAccountId == senderId), cancellationToken);
        if (blocked)
        {
            return null;
        }

        var message = new DmMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderAccountId = senderId,
            Ciphertext = Convert.FromBase64String(request.Ciphertext),
            Nonce = Convert.FromBase64String(request.Nonce),
            Tag = Convert.FromBase64String(request.Tag),
            CommitmentTag = Convert.FromBase64String(request.CommitmentTag),
            SentAtUtc = DateTime.UtcNow,
        };
        db.DmMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        var dto = ToDto(message);

        // Real-time push of the actual ciphertext to the recipient's live socket if they're
        // connected - AlphaChannel already tracks a per-account socket in UserDirectory for
        // watch-along, so pushing real content directly is simpler than Aetherphone's ping-then-
        // REST-refetch pattern (which exists there to work around a separate call-only socket).
        if (directory.TryGetSocket(recipientId.ToString(), out var socket) && socket is not null)
        {
            await SocketSend.SendAsync(socket, new SocialControl
            {
                Type = SocialSignalType.DmMessage,
                AccountId = senderId.ToString(),
                ConversationId = conversationId.ToString(),
                MessageId = message.Id.ToString(),
                Ciphertext = request.Ciphertext,
                Nonce = request.Nonce,
                Tag = request.Tag,
                CommitmentTag = request.CommitmentTag,
                TimestampUnix = ToUnixSeconds(message.SentAtUtc),
            }, cancellationToken).ConfigureAwait(false);
        }

        return dto;
    }

    public async Task MarkReadAsync(Guid conversationId, Guid callerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var unread = await db.DmMessages
            .Where(m => m.ConversationId == conversationId && m.SenderAccountId != callerId && m.ReadAtUtc == null)
            .ToListAsync(cancellationToken);

        if (unread.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var message in unread)
        {
            message.ReadAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static MessageDto ToDto(DmMessage message) => new(
        message.Id.ToString(),
        message.SenderAccountId.ToString(),
        Convert.ToBase64String(message.Ciphertext),
        Convert.ToBase64String(message.Nonce),
        Convert.ToBase64String(message.Tag),
        ToUnixSeconds(message.SentAtUtc),
        message.ReadAtUtc is { } read ? ToUnixSeconds(read) : null);

    private static (Guid Low, Guid High) CanonicalPair(Guid a, Guid b) =>
        a.CompareTo(b) <= 0 ? (a, b) : (b, a);

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
