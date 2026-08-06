using AlphaChannel.Contracts;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Tweeter Channel: short posts, likes, one-directional follows (separate from Friends). Timeline is
// "accounts you follow + yourself" only - no public/global feed, consistent with the rest of this
// backend's no-discovery-beyond-handle posture.
internal sealed partial class MainWindow
{
    private bool timelineDirty = true;
    private bool timelineLoading;
    private PostDto[] timelinePosts = [];
    private string? timelineNextCursor;
    private string postComposerInput = string.Empty;
    private string followHandleInput = string.Empty;
    private string? tweeterError;

    private void DrawTweeter()
    {
        if (CurrentSession is not { } session)
        {
            ImGui.TextColored(MutedText, "Sign in to use Tweeter.");
            if (ImGui.Button("Go to Settings"))
            {
                currentPage = HomePage.Settings;
            }

            return;
        }

        if (timelineDirty && !timelineLoading)
        {
            RefreshTimeline(session.Token, reset: true);
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextMultiline("##composer", ref postComposerInput, TweeterLimits.MaxPostLength, new Vector2(-1, 60));
        ImGui.TextColored(MutedText, $"{postComposerInput.Length}/{TweeterLimits.MaxPostLength}");
        ImGui.SameLine();
        using (ImRaii.Disabled(postComposerInput.Trim().Length == 0))
        {
            if (ImGui.Button("Post"))
            {
                var body = postComposerInput.Trim();
                var token = session.Token;
                _ = Task.Run(async () =>
                {
                    var post = await tweeterClient.CreatePostAsync(token, body);
                    if (post is not null)
                    {
                        timelinePosts = [post, .. timelinePosts];
                    }
                });
                postComposerInput = string.Empty;
            }
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(-100f);
        ImGui.InputTextWithHint("##followHandle", "Follow by handle", ref followHandleInput, 32);
        ImGui.SameLine();
        if (ImGui.Button("Follow") && followHandleInput.Trim().Length > 0)
        {
            var handle = followHandleInput.Trim();
            var token = session.Token;
            _ = Task.Run(async () =>
            {
                var account = await friendsClient.FindByHandleAsync(token, handle);
                tweeterError = account is null ? "Couldn't find that handle." : null;
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
        ImGui.Separator();
        ImGui.Spacing();

        if (timelinePosts.Length == 0)
        {
            ImGui.TextDisabled(timelineLoading ? "Loading..." : "Nothing here yet - follow someone or post something.");
            return;
        }

        foreach (var post in timelinePosts)
        {
            ImGui.PushID(post.Id);
            ImGui.TextColored(Accent, $"@{post.AuthorHandle}");
            ImGui.TextWrapped(post.Body);

            using (ImRaii.PushFont(Dalamud.Interface.UiBuilder.IconFont))
            {
                ImGui.TextColored(post.LikedByMe ? Danger : MutedText, Dalamud.Interface.FontAwesomeIcon.Heart.ToIconString());
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

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.PopID();
        }

        if (timelineNextCursor is { Length: > 0 } cursor)
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
