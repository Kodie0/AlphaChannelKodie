using AlphaChannel.Contracts;
using AlphaChannel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Social;

// Twitter-style posts/likes/follows - separate from Friendship (mutual, gates DMs/presence).
// Follow is one-directional like Twitter's, subject to the same LalafellVisibility/block checks as
// everything else social. No replies or media in v1 - kept to the smallest thing that's
// recognizably "Tweeter" (post, like, follow, timeline), same "as simple as correctly does the
// job" posture as the rest of this backend.
internal sealed class TweeterService(IDbContextFactory<AlphaChannelDbContext> dbFactory)
{
    private const int TimelineLimit = 30;

    public async Task<PostDto?> CreatePostAsync(Guid authorId, string body, CancellationToken cancellationToken)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0 || trimmed.Length > TweeterLimits.MaxPostLength)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var author = await db.Accounts.FirstAsync(a => a.Id == authorId, cancellationToken);

        var post = new Post { Id = Guid.NewGuid(), AuthorAccountId = authorId, Body = trimmed, CreatedAtUtc = DateTime.UtcNow };
        db.Posts.Add(post);
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(post, author, likeCount: 0, likedByMe: false);
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
        if (!exists)
        {
            db.PostLikes.Add(new PostLike { Id = Guid.NewGuid(), PostId = postId, AccountId = callerId, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
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

        return posts
            .Where(p => authors.ContainsKey(p.AuthorAccountId))
            .Select(p => ToDto(p, authors[p.AuthorAccountId], likeCounts.GetValueOrDefault(p.Id), myLikes.Contains(p.Id)))
            .ToArray();
    }

    private static PostDto ToDto(Post post, Account author, int likeCount, bool likedByMe) => new(
        post.Id.ToString(), author.Id.ToString(), author.Handle, author.DisplayName,
        post.Body, ToUnixSeconds(post.CreatedAtUtc), likeCount, likedByMe);

    private static Task<bool> IsBlockedEitherWayAsync(AlphaChannelDbContext db, Guid a, Guid b, CancellationToken cancellationToken) =>
        db.Blocks.AnyAsync(x => (x.BlockerAccountId == a && x.BlockedAccountId == b) || (x.BlockerAccountId == b && x.BlockedAccountId == a), cancellationToken);

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
