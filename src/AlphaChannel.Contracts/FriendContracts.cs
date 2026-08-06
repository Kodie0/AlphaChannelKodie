namespace AlphaChannel.Contracts;

public sealed record AccountSummaryDto(string Id, string Handle, string DisplayName);

// Online/WatchingLabel are computed live from server-side connection/room state, not stored -
// WatchingLabel stays null until PresenceService fills it in (see task 6). AvatarIcon/AvatarColorHex/
// StatusMessage are included inline (not just on the dedicated profile endpoint) so the friends list
// itself can render an avatar chip and status without an extra round-trip per friend.
public sealed record FriendDto(
    string AccountId, string Handle, string DisplayName, bool Online, string? WatchingLabel,
    string? AvatarIcon, string AvatarColorHex, string? StatusMessage);

public sealed record FriendRequestDto(string Id, string OtherAccountId, string OtherHandle, string OtherDisplayName, long CreatedAtUnix);

public sealed record FriendRequestsPage(FriendRequestDto[] Incoming, FriendRequestDto[] Outgoing);

// Looks up by the recipient's chosen DisplayName (case-insensitive) - see FriendService.
// SendRequestAsync. Named DisplayName rather than Handle since that's what it actually is now.
public sealed record SendFriendRequestRequest(string DisplayName);

// Right-click "Add Friend" in-game - looks up by real FFXIV character identity instead of a chosen
// name, see FriendService.SendRequestByCharacterAsync.
public sealed record SendFriendRequestByCharacterRequest(string CharacterName, string World);

// The "share out of band" path - see FriendService.RedeemInviteCodeAsync for why this skips
// straight to an accepted friendship rather than a pending request.
public sealed record RedeemInviteCodeRequest(string InviteCode);

public enum FriendSearchRelation
{
    None,
    Pending,
    Friends,
}

// Live search-as-you-type results for GET /friends/search - see FriendService.
// SearchByDisplayNamePrefixAsync. Relation lets the client gray out/relabel a row instead of
// blindly offering "Add" on someone already pending or already a friend.
public sealed record FriendSearchResultDto(
    string AccountId, string DisplayName, string? AvatarIcon, string AvatarColorHex, FriendSearchRelation Relation);
