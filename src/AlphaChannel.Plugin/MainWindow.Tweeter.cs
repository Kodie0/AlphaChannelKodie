using AlphaChannel.Contracts;
using AlphaChannel.Plugin.Auth;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Tweeter Channel: short posts, likes, replies, reposts, one-directional follows (separate from
// Friends). Timeline is "accounts you follow + yourself" only - no public/global feed, and hashtag
// search is scoped to that same set (see TweeterService.SearchByHashtagAsync) - consistent with the
// rest of this backend's no-discovery-beyond-handle posture.
internal sealed partial class MainWindow
{
    private bool timelineDirty = true;
    private bool timelineLoading;
    private PostDto[] timelinePosts = [];
    private string? timelineNextCursor;
    private string postComposerInput = string.Empty;
    private string postImageUrlInput = string.Empty;
    private string followHandleInput = string.Empty;
    private string? tweeterError;

    private string searchHashtagInput = string.Empty;
    private bool searchActive;
    private bool searchLoading;
    private PostDto[] hashtagSearchResults = [];

    private readonly HashSet<string> expandedReplies = [];
    private readonly Dictionary<string, string> replyComposerInputs = [];
    private readonly Dictionary<string, PostDto[]> repliesByPostId = [];
    private readonly HashSet<string> repliesLoading = [];

    private void DrawTweeter()
    {
        if (CurrentSession is not { } session)
        {
            DrawPlainEmpty("Tweeter is for people you follow — sign in first.", "Open Settings",
                () => currentPage = HomePage.Settings);
            return;
        }

        if (timelineDirty && !timelineLoading)
        {
            RefreshTimeline(session.Token, reset: true);
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextMultiline("##composer", ref postComposerInput, TweeterLimits.MaxPostLength, new Vector2(-1, 60));
        ImGui.TextColored(MutedText, $"{postComposerInput.Length}/{TweeterLimits.MaxPostLength}");

        ImGui.SetNextItemWidth(300f);
        ImGui.InputTextWithHint("##postImageUrl", "Image URL (optional)", ref postImageUrlInput, 500);
        ImGui.SameLine();
        using (ImRaii.Disabled(postComposerInput.Trim().Length == 0))
        {
            if (ImGui.Button("Post"))
            {
                var body = postComposerInput.Trim();
                var imageUrl = postImageUrlInput.Trim();
                var token = session.Token;
                _ = Task.Run(async () =>
                {
                    var post = await tweeterClient.CreatePostAsync(token, body, imageUrl: imageUrl.Length > 0 ? imageUrl : null);
                    if (post is not null)
                    {
                        timelinePosts = [post, .. timelinePosts];
                    }
                });
                postComposerInput = string.Empty;
                postImageUrlInput = string.Empty;
            }
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(-100f);
        ImGui.InputTextWithHint("##followHandle", "Follow by name", ref followHandleInput, 32);
        ImGui.SameLine();
        if (ImGui.Button("Follow") && followHandleInput.Trim().Length > 0)
        {
            var name = followHandleInput.Trim();
            var token = session.Token;
            _ = Task.Run(async () =>
            {
                var account = await friendsClient.FindByDisplayNameAsync(token, name);
                tweeterError = account is null ? "Couldn't find that name." : null;
                if (account is not null)
                {
                    await tweeterClient.FollowAsync(token, account.Id);
                }
            });
            followHandleInput = string.Empty;
        }

        if (tweeterError is { Length: > 0 } error)
        {
            ImGui.TextColored(Danger, error);
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(-100f);
        ImGui.InputTextWithHint("##searchHashtag", "Search #hashtag (you + who you follow)", ref searchHashtagInput, 48);
        ImGui.SameLine();
        if (ImGui.Button("Search") && searchHashtagInput.Trim().Length > 0)
        {
            RunHashtagSearch(session.Token);
        }

        if (searchActive)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
            {
                searchActive = false;
                hashtagSearchResults = [];
                searchHashtagInput = string.Empty;
            }
        }

        ImGui.Spacing();
        ImGui.Spacing();

        var displayedPosts = searchActive ? hashtagSearchResults : timelinePosts;
        if (displayedPosts.Length == 0)
        {
            var emptyMessage = searchActive
                ? (searchLoading ? "Searching..." : "No posts found.")
                : (timelineLoading ? "Loading..." : "Nothing here yet - follow someone or post something.");
            DrawPlainEmpty(emptyMessage);
            return;
        }

        foreach (var post in displayedPosts)
        {
            DrawPost(session, post);
        }

        if (!searchActive && timelineNextCursor is { Length: > 0 } cursor)
        {
            using (ImRaii.Disabled(timelineLoading))
            {
                if (ImGui.Button("Load older"))
                {
                    RefreshTimeline(session.Token, reset: false, before: long.Parse(cursor));
                }
            }
        }
    }

    private void DrawPost(CharacterSession session, PostDto post)
    {
        ImGui.PushID(post.Id);
        if (ImGui.SmallButton(post.AuthorDisplayName))
        {
            OpenProfilePopup(session, post.AuthorAccountId, post.AuthorDisplayName);
        }

        if (post.Body.Length > 0)
        {
            ImGui.TextWrapped(post.Body);
        }

        DrawPostImage(post.ImageUrl);

        // A repost: show what's being reposted in a nested, visually distinct box - RepostOfPostId
        // is set but RepostOfAuthorDisplayName can still be null if the original was deleted (see
        // TweeterService.HydrateAsync's own doc comment on why that's left dangling rather than
        // cascaded).
        if (post.RepostOfPostId is { Length: > 0 })
        {
            using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBg))
            using (var repostBox = ImRaii.Child("##repost", new Vector2(-1, 0), true,
                       ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar))
            {
                if (repostBox)
                {
                    if (post.RepostOfAuthorDisplayName is { Length: > 0 } originalAuthor)
                    {
                        ImGui.TextColored(MutedText, $"Reposted from {originalAuthor}");
                        if (post.RepostOfBody is { Length: > 0 } originalBody)
                        {
                            ImGui.TextWrapped(originalBody);
                        }

                        DrawPostImage(post.RepostOfImageUrl);
                    }
                    else
                    {
                        ImGui.TextColored(MutedText, "(original post deleted)");
                    }
                }
            }
        }

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(post.LikedByMe ? Danger : MutedText, FontAwesomeIcon.Heart.ToIconString());
        }

        if (ImGui.IsItemClicked())
        {
            var token = session.Token;
            var postId = post.Id;
            var wasLiked = post.LikedByMe;
            _ = Task.Run(() => wasLiked ? tweeterClient.UnlikeAsync(token, postId) : tweeterClient.LikeAsync(token, postId));
            UpdateLikeLocally(postId, !wasLiked);
        }

        ImGui.SameLine();
        ImGui.TextColored(MutedText, post.LikeCount.ToString());

        ImGui.SameLine();
        if (ImGui.SmallButton("Repost"))
        {
            var token = session.Token;
            var postId = post.Id;
            _ = Task.Run(() => tweeterClient.RepostAsync(token, postId));
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(post.ReplyCount > 0 ? $"Replies ({post.ReplyCount})" : "Reply"))
        {
            ToggleReplies(session, post.Id);
        }

        if (post.AuthorAccountId == session.AccountId)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Delete"))
            {
                var token = session.Token;
                var postId = post.Id;
                _ = Task.Run(async () => { await tweeterClient.DeletePostAsync(token, postId); timelineDirty = true; });
                timelinePosts = timelinePosts.Where(p => p.Id != post.Id).ToArray();
            }
        }

        if (expandedReplies.Contains(post.Id))
        {
            DrawReplies(session, post.Id);
        }

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.PopID();
    }

    private void DrawPostImage(string? imageUrl)
    {
        if (imageUrl is not { Length: > 0 })
        {
            return;
        }

        var thumbnail = thumbnails.Get(imageUrl);
        if (thumbnail is not null)
        {
            var width = Math.Min(280f, thumbnail.Width);
            var height = width * thumbnail.Height / thumbnail.Width;
            ImGui.Image(thumbnail.Handle, new Vector2(width, height));
        }
    }

    private void DrawReplies(CharacterSession session, string postId)
    {
        using (ImRaii.PushIndent())
        {
            ImGui.Spacing();

            var replyInput = replyComposerInputs.GetValueOrDefault(postId, string.Empty);
            ImGui.SetNextItemWidth(-70f);
            if (ImGui.InputTextWithHint("##reply" + postId, "Write a reply...", ref replyInput, TweeterLimits.MaxPostLength))
            {
                replyComposerInputs[postId] = replyInput;
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(replyInput.Trim().Length == 0))
            {
                if (ImGui.SmallButton("Send"))
                {
                    var token = session.Token;
                    var body = replyInput.Trim();
                    _ = Task.Run(async () =>
                    {
                        var reply = await tweeterClient.CreatePostAsync(token, body, parentPostId: postId);
                        if (reply is not null)
                        {
                            repliesByPostId[postId] = [.. repliesByPostId.GetValueOrDefault(postId, []), reply];
                        }
                    });
                    replyComposerInputs[postId] = string.Empty;
                }
            }

            if (repliesLoading.Contains(postId))
            {
                ImGui.TextDisabled("Loading replies...");
            }
            else if (repliesByPostId.TryGetValue(postId, out var replies))
            {
                foreach (var reply in replies)
                {
                    ImGui.PushID(reply.Id);
                    ImGui.TextColored(MutedText, reply.AuthorDisplayName);
                    ImGui.SameLine();
                    ImGui.TextWrapped(reply.Body);
                    ImGui.PopID();
                }
            }

            ImGui.Spacing();
        }
    }

    private void ToggleReplies(CharacterSession session, string postId)
    {
        if (!expandedReplies.Add(postId))
        {
            expandedReplies.Remove(postId);
            return;
        }

        repliesLoading.Add(postId);
        var token = session.Token;
        _ = Task.Run(async () =>
        {
            var page = await tweeterClient.GetRepliesAsync(token, postId);
            repliesByPostId[postId] = page?.Items ?? [];
            repliesLoading.Remove(postId);
        });
    }

    private void RunHashtagSearch(string bearerToken)
    {
        searchActive = true;
        searchLoading = true;
        var tag = searchHashtagInput.Trim();
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await tweeterClient.SearchByHashtagAsync(bearerToken, tag);
                hashtagSearchResults = page?.Items ?? [];
            }
            finally
            {
                searchLoading = false;
            }
        });
    }

    private void UpdateLikeLocally(string postId, bool liked)
    {
        for (var index = 0; index < timelinePosts.Length; index++)
        {
            if (timelinePosts[index].Id != postId)
            {
                continue;
            }

            var post = timelinePosts[index];
            timelinePosts[index] = post with { LikedByMe = liked, LikeCount = post.LikeCount + (liked ? 1 : -1) };
            break;
        }
    }

    private void RefreshTimeline(string bearerToken, bool reset, long? before = null)
    {
        timelineDirty = false;
        timelineLoading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await tweeterClient.GetTimelineAsync(bearerToken, before);
                if (page is null)
                {
                    return;
                }

                timelinePosts = reset ? page.Items : [.. timelinePosts, .. page.Items];
                timelineNextCursor = page.NextCursor;
            }
            finally
            {
                timelineLoading = false;
            }
        });
    }
}
