using AlphaChannel.Contracts;
using AlphaChannel.Plugin.Auth;
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
    // Live global count from presence.onlineCount — not friends, every AlphaChannel /rt client.
    private int usersOnlineCount;
    private FriendRequestsPage friendRequests = new([], []);
    private string? friendsError;
    private AccountSummaryDto[] blockedAccounts = [];
    private string inviteCodeInput = string.Empty;
    private bool inviteCodeRedeeming;
    private string? inviteCodeError;

    // Live search-as-you-type, replacing a type-the-full-name-then-Send box. friendSearchGeneration
    // discards a stale response that lands after a newer keystroke already fired a fresher search -
    // same race the old exact-search box never had to worry about since it only ever fired once per
    // button click.
    private string friendSearchInput = string.Empty;
    private string friendSearchQuery = string.Empty;
    private long friendSearchGeneration;
    private bool friendSearchLoading;
    private FriendSearchResultDto[] friendSearchResults = [];
    private readonly HashSet<string> friendSearchSendingIds = [];

    // Called from Plugin.cs's right-click "Add Friend" context-menu handler - surfaces the result
    // the same way the in-page "Add a friend" flow does (friendsError + a refreshed request list),
    // and jumps straight to Friends so the outcome is actually visible instead of silent.
    internal void HandleAddFriendByCharacterResult(bool ok, string characterName)
    {
        friendsDirty = true;
        friendsError = ok ? null : $"Couldn't add {characterName} - they may not have AlphaChannel yet.";
        currentPage = HomePage.Friends;
        IsOpen = true;
    }

    private void DrawFriends()
    {
        if (CurrentSession is not { } session)
        {
            DrawPlainEmpty("Sign in to see your friends.", "Open Settings",
                () => currentPage = HomePage.Settings);
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

        // Same trap the Settings page warns about, surfaced again here since this is where it
        // actually bites - typing a friend's real character name into this box will never match
        // anything (see FriendService.FindAccountByDisplayNameAsync), and someone whose OWN name
        // is still their random handle is equally unfindable to their friend right now.
        if (session.DisplayName == session.Handle)
        {
            ImGui.TextColored(Danger, "Pick a display name in Settings so friends can find you.");
            if (ImGui.SmallButton("Open Settings"))
            {
                currentPage = HomePage.Settings;
            }

            ImGui.Spacing();
        }

        if (friendRequests.Incoming.Length > 0)
        {
            ImGui.TextColored(Accent, $"Friend requests ({friendRequests.Incoming.Length})");
            ImGui.Spacing();
            foreach (var request in friendRequests.Incoming)
            {
                ImGui.PushID(request.Id);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(request.OtherDisplayName);
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
            ImGui.Spacing();
        }

        if (friendRequests.Outgoing.Length > 0)
        {
            ImGui.TextColored(MutedText, "Waiting for them to accept:");
            foreach (var request in friendRequests.Outgoing)
            {
                ImGui.BulletText(request.OtherDisplayName);
            }

            ImGui.Spacing();
        }

        ImGui.TextUnformatted($"Your friends ({friends.Length})");
        ImGui.Spacing();
        if (friends.Length == 0)
        {
            ImGui.TextColored(MutedText, friendsLoading
                ? "Loading…"
                : "No friends yet — add someone below.");
            ImGui.Spacing();
        }
        else
        {
            foreach (var friend in friends)
            {
                ImGui.PushID(friend.AccountId);
                DrawAvatarChip(friend.AvatarIcon, friend.AvatarColorHex, 20);
                ImGui.SameLine();
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextColored(friend.Online ? Good : MutedText, FontAwesomeIcon.Circle.ToIconString());
                }

                ImGui.SameLine();
                if (ImGui.SmallButton(friend.DisplayName))
                {
                    OpenProfilePopup(session, friend.AccountId, friend.DisplayName);
                }

                if (friend.StatusMessage is { Length: > 0 } status)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(MutedText, status);
                }
                else if (friend.WatchingLabel is { Length: > 0 } watching)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(MutedText, watching);
                }

                ImGui.SameLine();
                if (ImGui.SmallButton("Message"))
                {
                    StartOrOpenConversation(session, friend.AccountId, friend.DisplayName);
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
        }

        ImGui.Spacing();
        var hairline = ImGui.GetCursorScreenPos();
        var hairWidth = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddRectFilled(hairline, hairline + new Vector2(hairWidth, 1f),
            ImGui.GetColorU32(BorderSubtle));
        ImGui.Dummy(new Vector2(hairWidth, 12f));

        ImGui.TextUnformatted("Add a friend");
        ImGui.Spacing();

        ImGui.TextColored(MutedText, "Have an invite code?");
        ImGui.SetNextItemWidth(-100f);
        ImGui.InputTextWithHint("##inviteCode", "Paste invite code", ref inviteCodeInput, 16);
        ImGui.SameLine();
        using (ImRaii.Disabled(inviteCodeRedeeming || inviteCodeInput.Trim().Length == 0))
        {
            if (ImGui.Button("Redeem"))
            {
                inviteCodeRedeeming = true;
                inviteCodeError = null;
                var code = inviteCodeInput.Trim();
                var token = session.Token;
                _ = Task.Run(async () =>
                {
                    var ok = await friendsClient.RedeemInviteCodeAsync(token, code);
                    inviteCodeRedeeming = false;
                    inviteCodeError = ok ? null : "Couldn't redeem that code - it may be wrong, expired, or already used.";
                    if (ok)
                    {
                        inviteCodeInput = string.Empty;
                        friendsDirty = true;
                    }
                });
            }
        }

        if (inviteCodeError is { Length: > 0 } codeError)
        {
            ImGui.TextColored(Danger, codeError);
        }

        ImGui.Spacing();
        ImGui.TextColored(MutedText, "Or search by their AlphaChannel name (not character name)");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputTextWithHint("##friendSearch", "Type a name…", ref friendSearchInput, DisplayNameRules.MaxLength))
        {
            RequestFriendSearch(session, friendSearchInput);
        }

        DrawFriendSearchResults(session);

        if (friendsError is { Length: > 0 } error)
        {
            ImGui.TextColored(Danger, error);
        }

        if (blockedAccounts.Length > 0)
        {
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.TextUnformatted($"Blocked ({blockedAccounts.Length})");
            ImGui.Spacing();
            foreach (var blocked in blockedAccounts)
            {
                ImGui.PushID(blocked.Id);
                ImGui.BulletText(blocked.DisplayName);
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

    private void RequestFriendSearch(CharacterSession session, string query)
    {
        var trimmed = query.Trim();
        if (string.Equals(trimmed, friendSearchQuery, StringComparison.Ordinal))
        {
            return;
        }

        friendSearchQuery = trimmed;
        var ticket = Interlocked.Increment(ref friendSearchGeneration);
        if (trimmed.Length < DisplayNameRules.MinLength)
        {
            friendSearchResults = [];
            friendSearchLoading = false;
            return;
        }

        friendSearchLoading = true;
        var token = session.Token;
        _ = Task.Run(async () =>
        {
            var results = await friendsClient.SearchAsync(token, trimmed);
            if (Interlocked.Read(ref friendSearchGeneration) != ticket)
            {
                return;
            }

            friendSearchResults = results ?? [];
            friendSearchLoading = false;
        });
    }

    private void DrawFriendSearchResults(CharacterSession session)
    {
        if (friendSearchLoading)
        {
            ImGui.TextColored(MutedText, "Searching...");
            return;
        }

        if (friendSearchQuery.Length >= DisplayNameRules.MinLength && friendSearchResults.Length == 0)
        {
            ImGui.TextColored(MutedText, "No one found with that name.");
            return;
        }

        foreach (var result in friendSearchResults)
        {
            ImGui.PushID(result.AccountId);
            DrawAvatarChip(result.AvatarIcon, result.AvatarColorHex, 20);
            ImGui.SameLine();
            ImGui.Text(result.DisplayName);
            ImGui.SameLine();
            DrawFriendSearchAction(session, result);
            ImGui.PopID();
        }
    }

    private void DrawFriendSearchAction(CharacterSession session, FriendSearchResultDto result)
    {
        switch (result.Relation)
        {
            case FriendSearchRelation.Friends:
                ImGui.TextColored(MutedText, "Already friends");
                return;
            case FriendSearchRelation.Pending:
                ImGui.TextColored(MutedText, "Request pending");
                return;
        }

        using (ImRaii.Disabled(friendSearchSendingIds.Contains(result.AccountId)))
        {
            if (!ImGui.SmallButton("Add"))
            {
                return;
            }

            friendSearchSendingIds.Add(result.AccountId);
            var token = session.Token;
            var accountId = result.AccountId;
            var displayName = result.DisplayName;
            _ = Task.Run(async () =>
            {
                var ok = await friendsClient.SendRequestAsync(token, displayName);
                friendSearchSendingIds.Remove(accountId);
                friendsError = ok ? null : "Couldn't send that request - you may already be friends.";
                if (!ok)
                {
                    return;
                }

                friendsDirty = true;
                for (var index = 0; index < friendSearchResults.Length; index++)
                {
                    if (friendSearchResults[index].AccountId == accountId)
                    {
                        friendSearchResults[index] = friendSearchResults[index] with { Relation = FriendSearchRelation.Pending };
                    }
                }
            });
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
