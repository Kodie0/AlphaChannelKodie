namespace AlphaChannel.Contracts;

public sealed record AccountSummaryDto(string Id, string Handle, string DisplayName);

// Online/WatchingLabel are computed live from server-side connection/room state, not stored -
// WatchingLabel stays null until PresenceService fills it in (see task 6).
public sealed record FriendDto(string AccountId, string Handle, string DisplayName, bool Online, string? WatchingLabel);

public sealed record FriendRequestDto(string Id, string OtherAccountId, string OtherHandle, string OtherDisplayName, long CreatedAtUnix);

public sealed record FriendRequestsPage(FriendRequestDto[] Incoming, FriendRequestDto[] Outgoing);

public sealed record SendFriendRequestRequest(string Handle);
