using AlphaChannel.Contracts;
using AlphaChannel.Plugin.Auth;
using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// "Go Live" self-hosted streaming - OBS pushes RTMP to our own MediaMTX ingest, friends watch the
// resulting HLS stream through the exact same "play a URL" primitive (queue.PlayNow) the existing
// YouTube/Twitch flow already uses. See Server/Live/LiveService.cs for the server half and why the
// stream key format keeps the secret out of the public HLS URL.
internal sealed partial class MainWindow
{
    private bool liveStatusDirty = true;
    private bool liveStatusLoading;
    private LiveStatusDto? liveStatus;
    private bool streamKeyRevealed;
    private bool keyRotating;
    private string? keyError;
    private bool keyRegenerateConfirmPending;

    private bool friendsLiveDirty = true;
    private LiveFriendDto[] friendsLive = [];

    private void DrawGoLive()
    {
        if (CurrentSession is not { } session)
        {
            DrawPlainEmpty("OBS ingest + stream keys live here after you sign in.", "Open Settings",
                () => currentPage = HomePage.Settings);
            return;
        }

        if (liveStatusDirty && !liveStatusLoading)
        {
            RefreshLiveStatus(session);
        }

        if (friendsLiveDirty)
        {
            RefreshFriendsLive(session.Token);
        }

        DrawStage("##goliveStatus", () =>
        {
            if (liveStatus is not { } status)
            {
                ImGui.TextColored(MutedText, liveStatusLoading ? "Loading…" : "Couldn't load your status.");
                return;
            }

            ImGui.TextColored(status.IsLive ? Good : MutedText, status.IsLive ? "LIVE" : "OFFLINE");
            ImGui.SetWindowFontScale(1.2f);
            ImGui.TextUnformatted(status.IsLive ? "You're broadcasting" : "Not streaming right now");
            ImGui.SetWindowFontScale(1f);
            ImGui.TextColored(MutedText, "OBS → our ingest → friends open the HLS like any other URL.");
        });

        if (liveStatus is not { } live)
        {
            return;
        }

        ImGui.TextUnformatted("OBS setup");
        ImGui.TextColored(MutedText, "Stream → Custom service");
        ImGui.Spacing();

        ImGui.TextColored(MutedText, "Server");
        var rtmpServer = BuildRtmpServer();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##rtmpServer", ref rtmpServer, 256, ImGuiInputTextFlags.ReadOnly);
        if (ImGui.SmallButton("Copy server"))
        {
            ImGui.SetClipboardText(rtmpServer);
        }

        ImGui.Spacing();
        ImGui.TextColored(MutedText, "Stream key");

        var cachedKey = Plugin.Cfg.StreamKeys.GetValueOrDefault(session.AccountId);
        if (cachedKey is null)
        {
            ImGui.TextColored(MutedText, live.HasKey
                ? "Key generated on another install — Regenerate to get a copy here."
                : "No key yet — hit Generate below.");
        }
        else
        {
            var displayKey = streamKeyRevealed ? cachedKey : new string('•', Math.Min(cachedKey.Length, 32));
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("##streamKey", ref displayKey, 256, ImGuiInputTextFlags.ReadOnly);

            if (ImGui.SmallButton(streamKeyRevealed ? "Hide" : "Reveal"))
            {
                streamKeyRevealed = !streamKeyRevealed;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Copy key"))
            {
                ImGui.SetClipboardText(cachedKey);
            }

            ImGui.SameLine();
        }

        using (ImRaii.Disabled(keyRotating))
        {
            if (ImGui.SmallButton(cachedKey is null ? "Generate" : "Regenerate"))
            {
                if (cachedKey is null)
                {
                    RotateStreamKey(session);
                }
                else
                {
                    keyRegenerateConfirmPending = true;
                }
            }
        }

        if (keyRegenerateConfirmPending)
        {
            ImGui.TextColored(Danger, "This disconnects any OBS session using the old key. Continue?");
            if (ImGui.SmallButton("Yes, regenerate"))
            {
                keyRegenerateConfirmPending = false;
                RotateStreamKey(session);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel"))
            {
                keyRegenerateConfirmPending = false;
            }
        }

        if (keyError is { Length: > 0 } error)
        {
            ImGui.TextColored(Danger, error);
        }

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextUnformatted($"Friends live ({friendsLive.Length})");
        ImGui.Spacing();
        if (friendsLive.Length == 0)
        {
            DrawPlainEmpty("Nobody you know is live.");
            return;
        }

        foreach (var friend in friendsLive)
        {
            ImGui.PushID(friend.AccountId);
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(Good, FontAwesomeIcon.Circle.ToIconString());
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(friend.DisplayName);
            ImGui.SameLine();
            if (ImGui.SmallButton("Watch"))
            {
                queue.PlayNow(new VideoQueueEntry(friend.HlsUrl, $"{friend.DisplayName}'s stream", "Live", null, null));
                currentPage = HomePage.Player;
            }

            ImGui.PopID();
        }
    }

    private string BuildRtmpServer()
    {
        var host = new Uri(Plugin.Cfg.RelayServerUrl).Host;
        return $"rtmp://{host}:1935/live";
    }

    private void RotateStreamKey(CharacterSession session)
    {
        keyRotating = true;
        keyError = null;
        var token = session.Token;
        var accountId = session.AccountId;
        _ = Task.Run(async () =>
        {
            var key = await liveClient.RotateKeyAsync(token);
            keyRotating = false;
            if (key is null)
            {
                keyError = "Couldn't generate a stream key.";
                return;
            }

            Plugin.Cfg.StreamKeys[accountId] = key;
            Plugin.Cfg.Save();
            streamKeyRevealed = true;
            liveStatusDirty = true;
        });
    }

    private void RefreshLiveStatus(CharacterSession session)
    {
        liveStatusDirty = false;
        liveStatusLoading = true;
        var token = session.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                liveStatus = await liveClient.GetMyStatusAsync(token);
            }
            finally
            {
                liveStatusLoading = false;
            }
        });
    }

    private void RefreshFriendsLive(string bearerToken)
    {
        friendsLiveDirty = false;
        _ = Task.Run(async () => friendsLive = await liveClient.GetFriendsLiveAsync(bearerToken));
    }
}
