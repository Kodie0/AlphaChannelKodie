namespace AlphaChannel.Contracts;

public static class TweeterLimits
{
    public const int MaxPostLength = 280;
}

public sealed record CreatePostRequest(string Body);

public sealed record PostDto(
    string Id,
    string AuthorAccountId,
    string AuthorHandle,
    string AuthorDisplayName,
    string Body,
    long CreatedAtUnix,
    int LikeCount,
    bool LikedByMe);

public sealed record TimelinePage(PostDto[] Items, string? NextCursor);

public sealed record FollowSummaryDto(string AccountId, string Handle, string DisplayName);
