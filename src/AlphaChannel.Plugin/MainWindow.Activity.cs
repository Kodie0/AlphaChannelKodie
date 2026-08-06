using AlphaChannel.Contracts;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Activity Channel: a friends-only feed of "X started watching", "X joined a watch-along", "X
// accepted your friend request" - refreshed on open and again whenever StreamClient's
// OnActivityNew ping fires (see AlphaChannel.Contracts.SocialSignalType, the feed itself is always
// fetched via REST, the socket only ever says "something changed, refetch").
internal sealed partial class MainWindow
{
    private bool activityDirty = true;
    private bool activityLoading;
    private ActivityEventDto[] activityItems = [];
    private string? activityNextCursor;

    private void DrawActivity()
    {
        if (CurrentSession is not { } session)
        {
            ImGui.TextColored(MutedText, "Sign in to use Activity.");
            if (ImGui.Button("Go to Settings"))
            {
                currentPage = HomePage.Settings;
            }

            return;
        }

        if (activityDirty && !activityLoading)
        {
            RefreshActivity(session.Token, reset: true);
        }

        if (activityItems.Length == 0)
        {
            ImGui.TextDisabled(activityLoading ? "Loading..." : "Nothing yet - friend activity shows up here.");
            return;
        }

        foreach (var item in activityItems)
        {
            var label = item.Type switch
            {
                "StartedWatching" => $"@{item.ActorHandle} started watching",
                "JoinedWatchAlong" => item.Metadata is { Length: > 0 }
                    ? $"@{item.ActorHandle} joined {item.Metadata}'s watch-along"
                    : $"@{item.ActorHandle} joined a watch-along",
                "FriendAccepted" => $"@{item.ActorHandle} accepted a friend request",
                _ => $"@{item.ActorHandle}: {item.Type}",
            };
            ImGui.BulletText(label);
        }

        if (activityNextCursor is { Length: > 0 } cursor)
        {
            ImGui.Spacing();
            using (ImRaii.Disabled(activityLoading))
            {
                if (ImGui.Button("Load older"))
                {
                    RefreshActivity(session.Token, reset: false, before: long.Parse(cursor));
                }
            }
        }
    }

    private void RefreshActivity(string bearerToken, bool reset, long? before = null)
    {
        activityDirty = false;
        activityLoading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await activityClient.GetFeedAsync(bearerToken, before);
                if (page is null)
                {
                    return;
                }

                activityItems = reset ? page.Items : [.. activityItems, .. page.Items];
                activityNextCursor = page.NextCursor;

                if (page.Items.Length > 0)
                {
                    await activityClient.MarkReadAsync(bearerToken, page.Items[0].CreatedAtUnix);
                }
            }
            finally
            {
                activityLoading = false;
            }
        });
    }
}
