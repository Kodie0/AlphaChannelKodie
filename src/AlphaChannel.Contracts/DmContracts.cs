namespace AlphaChannel.Contracts;

// The server relays these fields blind - Ciphertext/Nonce/Tag/CommitmentTag are all base64, and
// only the two conversation participants' clients (via static-static ECDH on their
// Account.DmPublicKey values) can ever derive the key that makes Ciphertext meaningful.
public sealed record SendMessageRequest(string Ciphertext, string Nonce, string Tag, string CommitmentTag);

public sealed record MessageDto(
    string Id,
    string SenderAccountId,
    string Ciphertext,
    string Nonce,
    string Tag,
    long SentAtUnix,
    long? ReadAtUnix);

public sealed record MessagePage(MessageDto[] Items, string? NextCursor);

// No last-message preview - the server never has plaintext to preview.
public sealed record ConversationSummaryDto(
    string ConversationId,
    string OtherAccountId,
    string OtherHandle,
    string OtherDisplayName,
    long? LastMessageAtUnix,
    int UnreadCount);

public sealed record UploadPublicKeyRequest(string PublicKeyBase64);

public sealed record PublicKeyDto(string PublicKeyBase64);
