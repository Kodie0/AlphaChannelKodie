using AlphaChannel.Contracts;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Friends Channel: friend list (with live online status), incoming/outgoing requests, add-by-
// handle. REST-backed (FriendsClient) with live refresh triggered by StreamClient's
// OnFriendRequestReceived/OnFriendAccepted/OnFriendRemoved pushes (wired in MainWindow's
// constructor) rather than polling - see AlphaChannel.Contracts.SocialSignalType's own note on why
// those pushes exist.
internal sealed partial class MainWindow
{
    private bool friendsDirty = true;
    private bool friendsLoading;
    private FriendDto[] friends = [];
    private FriendRequestsPage friendRequests = new([], []);
    private string addHandleInput = string.Empty;
    private string? friendsError;
    private AccountSummaryDto[] blockedAccounts = [];

    private void DrawFriends()
    {
        if (CurrentSession is not { } session)
        {
            ImGui.TextColored(MutedText, "Sign in to use Friends.");
            if (ImGui.Button("Go to Settings"))
            {
                currentPage = HomePage.Settings;
            }

            return;
        }

        if (friendsDirty && !friendsLoading)
        {
            RefreshFriends(session.Token);
        }

        if (friendsClient.LastAccessDeniedReason is { } deniedReason)
        {
            ImGui.TextColored(Danger, deniedReason switch
            {
                "lalafell_pending" => "Your account is pending review before Lalafell accounts can use Friends. Check back soon.",
                "lalafell_denied" => "Social features aren't available for this account.",
                _ => "Friends isn't available for this account right now.",
            });
            return;
        }

        SectionHeader("Add a friend");
        ImGui.SetNextItemWidth(-100f);
        ImGui.InputTextWithHint("##addHandle", "Their handle", ref addHandleInput, 32);
        ImGui.SameLine();
        if (ImGui.Button("Send request") && addHandleInput.Trim().Length > 0)
        {
            var handle = addHandleInput.Trim();
            var token = session.Token;
            _ = Task.Run(async () =>
            {
                var ok = await friendsClient.SendRequestAsync(token, handle);
                friendsError = ok ? null : "Couldn't send that request - check the handle, or you may already be friends.";
                friendsDirty = true;
            });
            addHandleInput = string.Empty;
        }

        if (friendsError is { Length: > 0 } error)
        {
            ImGui.TextColored(Danger, error);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (friendRequests.Incoming.Length > 0)
        {
            SectionHeader($"Requests ({friendRequests.Incoming.Length})");
            foreach (var request in friendRequests.Incoming)
            {
                ImGui.PushID(request.Id);
                ImGui.Text($"@{request.OtherHandle}");
                ImGui.SameLine();
                if (ImGui.SmallButton("Accept"))
                {
                    var token = session.Token;
                    _ = Task.Run(async () => { await friendsClient.AcceptRequestAsync(token, request.Id); friendsDirty = true; });
                }

                ImGui.SameLine();
                if (ImGui.SmallButton("Decline"))
                {
                    var token = session.Token;
                    _ = Task.Run(async () => { await friendsClient.DeclineRequestAsync(token, request.Id); friendsDirty = true; });
                }

                ImGui.PopID();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        if (friendRequests.Outgoing.Length > 0)
        {
            ImGui.TextDisabled("Waiting on:");
            foreach (var request in friendRequests.Outgoing)
            {
                ImGui.BulletText($"@{request.OtherHandle}");
            }

            ImGui.Spacing();
        }

        SectionHeader($"Friends ({friends.Length})");
        if (friends.Length == 0)
        {
            ImGui.TextDisabled("No friends yet - add one by handle above.");
            return;
        }

        foreach (var friend in friends)
        {
            ImGui.PushID(friend.AccountId);
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(friend.Online ? Good : MutedText, FontAwesomeIcon.Circle.ToIconString());
            }

            ImGui.SameLine();
            ImGui.Text($"@{friend.Handle}");
            if (friend.WatchingLabel is { Length: > 0 } watching)
            {
                ImGui.SameLine();
                ImGui.TextColored(MutedText, watching);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Message"))
            {
                StartOrOpenConversation(session, friend.AccountId, friend.Handle);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                var token = session.Token;
                var accountId = friend.AccountId;
                _ = Task.Run(async () => { await friendsClient.RemoveFriendAsync(token, accountId); friendsDirty = true; });
            }

            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Danger))
            {
                if (ImGui.SmallButton("Block"))
                {
                    var token = session.Token;
                    var accountId = friend.AccountId;
                    _ = Task.Run(async () => { await friendsClient.BlockAsync(token, accountId); friendsDirty = true; });
                }
            }

            ImGui.PopID();
        }

        if (blockedAccounts.Length > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            SectionHeader($"Blocked ({blockedAccounts.Length})");
            foreach (var blocked in blockedAccounts)
            {
                ImGui.PushID(blocked.Id);
                ImGui.Text($"@{blocked.Handle}");
                ImGui.SameLine();
                if (ImGui.SmallButton("Unblock"))
                {
                    var token = session.Token;
                    var accountId = blocked.Id;
                    _ = Task.Run(async () => { await friendsClient.UnblockAsync(token, accountId); friendsDirty = true; });
                }

                ImGui.PopID();
            }
        }
    }

    // Fired from StreamClient's receive loop (a background thread) - updates `friends` in place
    // rather than setting friendsDirty, since a full REST round-trip for a single online/watching
    // change would be wasteful and would visibly lag behind the push. Same unsynchronized
    // cross-thread field access already used throughout this plugin (e.g. StreamClient.Roster).
    private void ApplyPresenceUpdate(SocialControl update)
    {
        if (update.AccountId is not { Length: > 0 } accountId)
        {
            return;
        }

        for (var index = 0; index < friends.Length; index++)
        {
            if (friends[index].AccountId != accountId)
            {
                continue;
            }

            friends[index] = friends[index] with { Online = update.Online ?? false, WatchingLabel = update.WatchingLabel };
            break;
        }
    }

    private void RefreshFriends(string bearerToken)
    {
        friendsDirty = false;
        friendsLoading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var friendsTask = friendsClient.GetFriendsAsync(bearerToken);
                var requestsTask = friendsClient.GetRequestsAsync(bearerToken);
                var blocksTask = friendsClient.GetBlocksAsync(bearerToken);
                await Task.WhenAll(friendsTask, requestsTask, blocksTask);

                friends = await friendsTask ?? [];
                friendRequests = await requestsTask ?? new FriendRequestsPage([], []);
                blockedAccounts = await blocksTask ?? [];
            }
            finally
            {
                friendsLoading = false;
            }
        });
    }
}
