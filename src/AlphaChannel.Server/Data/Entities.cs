namespace AlphaChannel.Server.Data;

// The durable identity behind a connection. Public-facing (Handle/DisplayName) - the verified
// FFXIV character that proved this account belongs to a real person lives on AccountCharacter,
// deliberately kept out of this type so nothing that touches Account by itself can leak it.
internal sealed class Account
{
    public Guid Id { get; set; }

    // Chosen at signup, exact-match lookup only (no browse/search/autocomplete anywhere) - this is
    // the one thing other players are allowed to find you by. Never derived from the real character
    // name, so knowing someone's FFXIV character doesn't hand you their AlphaChannel identity.
    public required string Handle { get; set; }

    public required string DisplayName { get; set; }

    // A second, regenerable way to be added as a friend that doesn't require picking a public
    // handle at all - share it out of band (Discord, party chat) and it's spent/rotated after use.
    public required string InviteCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public bool IsBanned { get; set; }
    public string? BanReason { get; set; }
    public DateTime? BannedAtUtc { get; set; }
    public DateTime? BannedUntilUtc { get; set; } // null while IsBanned means permanent

    // X25519 public key uploaded once the plugin generates a local keypair (see DM design) - used
    // by other accounts to derive a shared secret for encrypting messages to this account.
    public byte[]? DmPublicKey { get; set; }

    // Read client-side from the live character model at sign-in (see Plugin.cs's ReadIsLalafell)
    // and OR'd in whenever a Lalafell character gets linked to this account later. Gates social
    // features via LalafellSocialStatus - see LalafellReviewService for the approve/deny flow and
    // ServerSettings.HideLalafellFromNonLalafell for the separate visibility toggle.
    public bool IsLalafell { get; set; }
    public LalafellSocialStatus LalafellSocialStatus { get; set; } = LalafellSocialStatus.NotApplicable;

    // Asked once at account creation (also editable later from Settings) - comma-separated race
    // names, purely a self-report, not itself used for any gating decision.
    public string? SelfReportedRaces { get; set; }

    // Best-effort corroboration: LodestoneRaceChecker looks the character up independently and
    // flags a contradiction with IsLalafell for admin attention. Never blocks anything by itself -
    // see LodestoneRaceChecker's own header comment for why.
    public bool LodestoneRaceMismatch { get; set; }
    public DateTime? LodestoneCheckedAtUtc { get; set; }

    // Per-account preference, asked at account creation and editable later from Settings - default
    // true (see it by default; this is an opt-out, not an opt-in, so a player who never answers the
    // question isn't silently cut off from anything). Filters Lalafell-flagged accounts out of only
    // the social surfaces (friends/activity/etc) for THIS viewer - never affects watch-along, which
    // isn't a "social app" in this sense. ServerSettings.HideLalafellFromNonLalafell is a separate
    // admin-wide override that forces the hidden behavior for everyone regardless of this value.
    public bool WantsToSeeLalafellContent { get; set; } = true;
}

internal enum LalafellSocialStatus
{
    NotApplicable, // IsLalafell is false - this account was never gated in the first place
    Pending,
    Approved,
    Denied,
}

// Single-row table (Id is always fixed at 1) for server-wide toggles an admin can flip without a
// redeploy - see LalafellReviewService and the admin UI page.
internal sealed class ServerSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool HideLalafellFromNonLalafell { get; set; }
}

// The verified FFXIV character(s) behind an account. Kept in its own table specifically so no API
// response has to touch it to answer ordinary questions ("what's my friend's handle") - only the
// auth flow and ban-evasion checks ever query this table.
internal sealed class AccountCharacter
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string CharacterName { get; set; }
    public required string World { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime LinkedAtUtc { get; set; }
}

// Bearer tokens are never stored raw - only a SHA-256 hash, so a database dump doesn't hand out
// live credentials. /rt and every authenticated endpoint hash the incoming token and look it up here.
internal sealed class AuthToken
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

internal enum FriendshipStatus
{
    Pending,
    Accepted,
    Declined,
}

internal sealed class Friendship
{
    public Guid Id { get; set; }
    public Guid RequesterAccountId { get; set; }
    public Guid AddresseeAccountId { get; set; }
    public FriendshipStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
}

// Independent of Friendship - you can block someone you were never friends with. Blocking removes
// any existing friendship and prevents new friend requests and DMs in both directions.
internal sealed class Block
{
    public Guid Id { get; set; }
    public Guid BlockerAccountId { get; set; }
    public Guid BlockedAccountId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// One row per pair of accounts. AccountAId/AccountBId are stored in a canonical (lower Guid first)
// order so there's exactly one conversation per pair regardless of who messaged first.
internal sealed class DmConversation
{
    public Guid Id { get; set; }
    public Guid AccountAId { get; set; }
    public Guid AccountBId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// Ciphertext + nonce + AES-GCM tag only - the server never sees plaintext or the encryption key.
// Static-static ECDH between the two participants' long-term Account.DmPublicKey values derives
// the same AES-256-GCM key on both ends with nothing further to store/wrap server-side - see
// AlphaChannel.Plugin/Crypto's DmCipher for the client-side half of this.
//
// CommitmentTag is HMAC-SHA256(frankingKey, plaintext), computed and sent by the sender alongside
// the ciphertext at send time. The frankingKey itself is embedded in the encrypted payload and
// never touches the server - but if the recipient (or sender) later reports this message, their
// client can voluntarily reveal the plaintext + frankingKey, and the server can recompute the HMAC
// and compare it to this stored tag to confirm the reveal is genuine, without ever having been
// able to decrypt the message on its own. See Report.FrankingVerified.
internal sealed class DmMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderAccountId { get; set; }
    public required byte[] Ciphertext { get; set; }
    public required byte[] Nonce { get; set; }
    public required byte[] Tag { get; set; }
    public required byte[] CommitmentTag { get; set; }
    public DateTime SentAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

internal enum ActivityEventType
{
    StartedWatching,
    JoinedWatchAlong,
    FriendAccepted,
}

// Friends-only by construction - the feed endpoint only ever queries events belonging to the
// caller's accepted friends (plus their own), there is no public/global feed query anywhere.
internal sealed class ActivityEvent
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public ActivityEventType Type { get; set; }
    public string? Metadata { get; set; } // small JSON blob, e.g. { "title": "..." }
    public DateTime CreatedAtUtc { get; set; }
}

internal enum ReportStatus
{
    Open,
    Reviewed,
    ActionTaken,
    Dismissed,
}

// Tweeter: short text posts + one-directional follows, separate from Friendship (which is mutual
// and gates DMs/presence). No replies/media in v1 - see TweeterService's own header comment.
internal sealed class Post
{
    public Guid Id { get; set; }
    public Guid AuthorAccountId { get; set; }
    public required string Body { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class PostLike
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid AccountId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// One-directional, unlike Friendship - anyone can follow anyone (subject to the same
// LalafellVisibility/block checks as everything else social).
internal sealed class Follow
{
    public Guid Id { get; set; }
    public Guid FollowerAccountId { get; set; }
    public Guid FolloweeAccountId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// One row per account - POST /activity/read moves LastReadAtUtc forward. Separate from ActivityEvent
// itself since one event row is visible in many different friends' feeds at once, so "read" can't
// live on the event - it has to be a per-viewer cursor.
internal sealed class ActivityReadMarker
{
    public Guid AccountId { get; set; }
    public DateTime LastReadAtUtc { get; set; }
}

internal sealed class Report
{
    public Guid Id { get; set; }
    public Guid ReporterAccountId { get; set; }
    public Guid ReportedAccountId { get; set; }
    public Guid? ReportedMessageId { get; set; }
    public Guid? ReportedPostId { get; set; }
    public required string Reason { get; set; }
    public string? Details { get; set; }
    public ReportStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }

    // Only ever populated for a DM-message report - the reporter's client voluntarily decrypted
    // and revealed this. FrankingVerified records whether it checked out against DmMessage's stored
    // CommitmentTag at the moment the report was filed - see DmMessage's own doc comment.
    public string? RevealedBody { get; set; }
    public string? FrankingKeyBase64 { get; set; }
    public bool? FrankingVerified { get; set; }
}
