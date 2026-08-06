using AlphaChannel.Contracts;
using AlphaChannel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Social;

// Twitter-style posts/likes/follows - separate from Friendship (mutual, gates DMs/presence).
// Follow is one-directional like Twitter's, subject to the same LalafellVisibility/block checks as
// everything else social. No replies or media in v1 - kept to the smallest thing that's
// recognizably "Tweeter" (post, like, follow, timeline), same "as simple as correctly does the
// job" posture as the rest of this backend.
internal sealed class TweeterService(IDbContextFactory<AlphaChannelDbContext> dbFactory, ActivityService activity)
{
    private const int TimelineLimit = 30;

    public async Task<PostDto?> CreatePostAsync(Guid authorId, string body, string? parentPostId, string? imageUrl, CancellationToken cancellationToken)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0 || trimmed.Length > TweeterLimits.MaxPostLength)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        Post? parent = null;
        if (parentPostId is { Length: > 0 } && Guid.TryParse(parentPostId, out var parentGuid))
        {
            parent = await db.Posts.FirstOrDefaultAsync(p => p.Id == parentGuid, cancellationToken);
        }

        var post = new Post
        {
            Id = Guid.NewGuid(),
            AuthorAccountId = authorId,
            Body = trimmed,
            CreatedAtUtc = DateTime.UtcNow,
            ParentPostId = parent?.Id,
            ImageUrl = NormalizeImageUrl(imageUrl),
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync(cancellationToken);

        // Notify whoever's being replied to, same "target regardless of friendship" reasoning as
        // PostLiked in LikeAsync - a reply can come from any follower, not just a friend.
        if (parent is not null && parent.AuthorAccountId != authorId)
        {
            await activity.RecordAsync(authorId, ActivityEventType.PostReplied, post.Id.ToString(), cancellationToken, parent.AuthorAccountId);
        }

        return (await HydrateAsync(db, [post], authorId, cancellationToken)).FirstOrDefault();
    }

    public async Task<PostDto?> RepostAsync(Guid callerId, Guid postId, string? quoteBody, CancellationToken cancellationToken)
    {
        var trimmedQuote = (quoteBody ?? string.Empty).Trim();
        if (trimmedQuote.Length > TweeterLimits.MaxPostLength)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var original = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        var post = new Post
        {
            Id = Guid.NewGuid(),
            AuthorAccountId = callerId,
            Body = trimmedQuote,
            CreatedAtUtc = DateTime.UtcNow,
            RepostOfPostId = original.Id,
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync(cancellationToken);

        return (await HydrateAsync(db, [post], callerId, cancellationToken)).FirstOrDefault();
    }

    public async Task<TimelinePage> GetRepliesAsync(Guid postId, Guid viewerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var posts = await db.Posts.Where(p => p.ParentPostId == postId).OrderBy(p => p.CreatedAtUtc).ToListAsync(cancellationToken);
        var items = await HydrateAsync(db, posts, viewerId, cancellationToken);
        return new TimelinePage(items, null);
    }

    // Scoped to the same "self + who I follow" set as GetTimelineAsync - deliberately not a global
    // search, consistent with there being no public/browse surface anywhere else in this backend
    // (see FriendService.FindAccountByDisplayNameAsync's own doc comment on that posture).
    public async Task<TimelinePage> SearchByHashtagAsync(Guid viewerId, string hashtag, CancellationToken cancellationToken)
    {
        var normalized = "#" + hashtag.TrimStart('#').Trim().ToLowerInvariant();
        if (normalized.Length <= 1)
        {
            return new TimelinePage([], null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var followingIds = await db.Follows.Where(f => f.FollowerAccountId == viewerId)
            .Select(f => f.FolloweeAccountId).ToListAsync(cancellationToken);
        followingIds.Add(viewerId);

        var posts = await db.Posts
            .Where(p => followingIds.Contains(p.AuthorAccountId) && p.Body.ToLower().Contains(normalized))
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(TimelineLimit)
            .ToListAsync(cancellationToken);

        var items = await HydrateAsync(db, posts, viewerId, cancellationToken);
        return new TimelinePage(items, null);
    }

    private static string? NormalizeImageUrl(string? imageUrl)
    {
        var trimmed = imageUrl?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? trimmed[..Math.Min(trimmed.Length, 500)]
            : null;
    }

    public async Task<bool> DeletePostAsync(Guid postId, Guid callerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId && p.AuthorAccountId == callerId, cancellationToken);
        if (post is null)
        {
            return false;
        }

        var likes = await db.PostLikes.Where(l => l.PostId == postId).ToListAsync(cancellationToken);
        db.PostLikes.RemoveRange(likes);
        db.Posts.Remove(post);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TimelinePage> GetTimelineAsync(Guid viewerId, long? beforeUnix, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var followingIds = await db.Follows.Where(f => f.FollowerAccountId == viewerId)
            .Select(f => f.FolloweeAccountId).ToListAsync(cancellationToken);
        followingIds.Add(viewerId);

        var query = db.Posts.Where(p => followingIds.Contains(p.AuthorAccountId));
        if (beforeUnix is { } before)
        {
            var beforeDate = DateTimeOffset.FromUnixTimeSeconds(before).UtcDateTime;
            query = query.Where(p => p.CreatedAtUtc < beforeDate);
        }

        var posts = await query.OrderByDescending(p => p.CreatedAtUtc).Take(TimelineLimit + 1).ToListAsync(cancellationToken);
        var hasMore = posts.Count > TimelineLimit;
        posts = posts.Take(TimelineLimit).ToList();

        var items = await HydrateAsync(db, posts, viewerId, cancellationToken);
        var nextCursor = hasMore && posts.Count > 0 ? ToUnixSeconds(posts[^1].CreatedAtUtc).ToString() : null;
        return new TimelinePage(items, nextCursor);
    }

    public async Task<TimelinePage> GetAccountPostsAsync(Guid targetAccountId, Guid viewerId, long? beforeUnix, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var viewer = await db.Accounts.FirstAsync(a => a.Id == viewerId, cancellationToken);
        var target = await db.Accounts.FirstOrDefaultAsync(a => a.Id == targetAccountId, cancellationToken);
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == ServerSettings.SingletonId, cancellationToken) ?? new ServerSettings();
        if (target is null || LalafellVisibility.IsHiddenFrom(viewer, target, settings) ||
            await IsBlockedEitherWayAsync(db, viewerId, targetAccountId, cancellationToken))
        {
            return new TimelinePage([], null);
        }

        var query = db.Posts.Where(p => p.AuthorAccountId == targetAccountId);
        if (beforeUnix is { } before)
        {
            var beforeDate = DateTimeOffset.FromUnixTimeSeconds(before).UtcDateTime;
            query = query.Where(p => p.CreatedAtUtc < beforeDate);
        }

        var posts = await query.OrderByDescending(p => p.CreatedAtUtc).Take(TimelineLimit + 1).ToListAsync(cancellationToken);
        var hasMore = posts.Count > TimelineLimit;
        posts = posts.Take(TimelineLimit).ToList();

        var items = await HydrateAsync(db, posts, viewerId, cancellationToken);
        var nextCursor = hasMore && posts.Count > 0 ? ToUnixSeconds(posts[^1].CreatedAtUtc).ToString() : null;
        return new TimelinePage(items, nextCursor);
    }

    public async Task LikeAsync(Guid postId, Guid callerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var exists = await db.PostLikes.AnyAsync(l => l.PostId == postId && l.AccountId == callerId, cancellationToken);
        if (exists)
        {
            return;
        }

        db.PostLikes.Add(new PostLike { Id = Guid.NewGuid(), PostId = postId, AccountId = callerId, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(cancellationToken);

        // Notify the author (if it's not their own post) regardless of whether they're actually
        // friends with the liker - a like can come from anyone following them, and ActivityEvent.
        // TargetAccountId is exactly the "notify this specific account either way" mechanism (see
        // ActivityService's own doc comment on why).
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, cancellationToken);
        if (post is not null && post.AuthorAccountId != callerId)
        {
            await activity.RecordAsync(callerId, ActivityEventType.PostLiked, postId.ToString(), cancellationToken, post.AuthorAccountId);
        }
    }

    public async Task UnlikeAsync(Guid postId, Guid callerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var like = await db.PostLikes.FirstOrDefaultAsync(l => l.PostId == postId && l.AccountId == callerId, cancellationToken);
        if (like is not null)
        {
            db.PostLikes.Remove(like);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> FollowAsync(Guid callerId, Guid targetId, CancellationToken cancellationToken)
    {
        if (callerId == targetId)
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var target = await db.Accounts.FirstOrDefaultAsync(a => a.Id == targetId, cancellationToken);
        if (target is null || await IsBlockedEitherWayAsync(db, callerId, targetId, cancellationToken))
        {
            return false;
        }

        var caller = await db.Accounts.FirstAsync(a => a.Id == callerId, cancellationToken);
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.Id == ServerSettings.SingletonId, cancellationToken) ?? new ServerSettings();
        if (LalafellVisibility.IsHiddenFrom(caller, target, settings))
        {
            return false;
        }

        var exists = await db.Follows.AnyAsync(f => f.FollowerAccountId == callerId && f.FolloweeAccountId == targetId, cancellationToken);
        if (!exists)
        {
            db.Follows.Add(new Follow { Id = Guid.NewGuid(), FollowerAccountId = callerId, FolloweeAccountId = targetId, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task UnfollowAsync(Guid callerId, Guid targetId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var follow = await db.Follows.FirstOrDefaultAsync(f => f.FollowerAccountId == callerId && f.FolloweeAccountId == targetId, cancellationToken);
        if (follow is not null)
        {
            db.Follows.Remove(follow);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<FollowSummaryDto>> GetFollowingAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.Follows.Where(f => f.FollowerAccountId == accountId).Select(f => f.FolloweeAccountId).ToListAsync(cancellationToken);
        return await db.Accounts.Where(a => ids.Contains(a.Id))
            .Select(a => new FollowSummaryDto(a.Id.ToString(), a.Handle, a.DisplayName)).ToListAsync(cancellationToken);
    }

    public async Task<List<FollowSummaryDto>> GetFollowersAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.Follows.Where(f => f.FolloweeAccountId == accountId).Select(f => f.FollowerAccountId).ToListAsync(cancellationToken);
        return await db.Accounts.Where(a => ids.Contains(a.Id))
            .Select(a => new FollowSummaryDto(a.Id.ToString(), a.Handle, a.DisplayName)).ToListAsync(cancellationToken);
    }

    private static async Task<PostDto[]> HydrateAsync(AlphaChannelDbContext db, List<Post> posts, Guid viewerId, CancellationToken cancellationToken)
    {
        if (posts.Count == 0)
        {
            return [];
        }

        var authorIds = posts.Select(p => p.AuthorAccountId).Distinct().ToList();
        var authors = (await db.Accounts.Where(a => authorIds.Contains(a.Id)).ToListAsync(cancellationToken)).ToDictionary(a => a.Id);

        var postIds = posts.Select(p => p.Id).ToList();
        var likeCounts = (await db.PostLikes.Where(l => postIds.Contains(l.PostId))
            .GroupBy(l => l.PostId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            .ToDictionary(x => x.Key, x => x.Count);
        var myLikes = (await db.PostLikes.Where(l => postIds.Contains(l.PostId) && l.AccountId == viewerId)
            .Select(l => l.PostId).ToListAsync(cancellationToken)).ToHashSet();

        var replyCounts = (await db.Posts.Where(p => p.ParentPostId != null && postIds.Contains(p.ParentPostId!.Value))
            .GroupBy(p => p.ParentPostId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            .ToDictionary(x => x.Key, x => x.Count);

        // One extra round trip to resolve what's being reposted (post + its author), rather than
        // denormalizing the quoted content onto the repost row - see Post.RepostOfPostId's doc
        // comment on why that's the deliberate tradeoff (a deleted original just shows as such).
        var repostOfIds = posts.Where(p => p.RepostOfPostId is not null).Select(p => p.RepostOfPostId!.Value).Distinct().ToList();
        var repostOfPosts = repostOfIds.Count == 0
            ? []
            : await db.Posts.Where(p => repostOfIds.Contains(p.Id)).ToListAsync(cancellationToken);
        var repostOfAuthorIds = repostOfPosts.Select(p => p.AuthorAccountId).Distinct().ToList();
        var repostOfAuthors = repostOfAuthorIds.Count == 0
            ? new Dictionary<Guid, Account>()
            : (await db.Accounts.Where(a => repostOfAuthorIds.Contains(a.Id)).ToListAsync(cancellationToken)).ToDictionary(a => a.Id);
        var repostOfById = repostOfPosts.ToDictionary(p => p.Id);

        return posts
            .Where(p => authors.ContainsKey(p.AuthorAccountId))
            .Select(p =>
            {
                Post? repostOf = p.RepostOfPostId is { } repostId ? repostOfById.GetValueOrDefault(repostId) : null;
                Account? repostOfAuthor = repostOf is not null ? repostOfAuthors.GetValueOrDefault(repostOf.AuthorAccountId) : null;
                return ToDto(p, authors[p.AuthorAccountId], likeCounts.GetValueOrDefault(p.Id), myLikes.Contains(p.Id),
                    replyCounts.GetValueOrDefault(p.Id), repostOf, repostOfAuthor);
            })
            .ToArray();
    }

    private static PostDto ToDto(Post post, Account author, int likeCount, bool likedByMe, int replyCount, Post? repostOf, Account? repostOfAuthor) => new(
        post.Id.ToString(), author.Id.ToString(), author.Handle, author.DisplayName,
        post.Body, ToUnixSeconds(post.CreatedAtUtc), likeCount, likedByMe,
        post.ParentPostId?.ToString(), replyCount, post.ImageUrl,
        post.RepostOfPostId?.ToString(), repostOfAuthor?.DisplayName, repostOf?.Body, repostOf?.ImageUrl);

    private static Task<bool> IsBlockedEitherWayAsync(AlphaChannelDbContext db, Guid a, Guid b, CancellationToken cancellationToken) =>
        db.Blocks.AnyAsync(x => (x.BlockerAccountId == a && x.BlockedAccountId == b) || (x.BlockerAccountId == b && x.BlockedAccountId == a), cancellationToken);

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
