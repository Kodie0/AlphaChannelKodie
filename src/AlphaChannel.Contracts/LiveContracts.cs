namespace AlphaChannel.Contracts;

public sealed record LiveStatusDto(bool HasKey, bool IsLive, string? HlsUrl);

public sealed record RotateStreamKeyResponse(string StreamKey);

public sealed record LiveFriendDto(string AccountId, string DisplayName, string HlsUrl, long StartedAtUnix);
