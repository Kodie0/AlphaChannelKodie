using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Watch party lives on Player: host/join/roster + ephemeral room chat (stream.chat).
internal sealed partial class MainWindow
{
    private readonly List<(string Name, string Text)> partyChatLines = [];
    private string partyChatInput = string.Empty;
    private bool partyChatStickToBottom = true;

    private void DrainPartyChat()
    {
        while (stream.IncomingChat.TryDequeue(out var line))
        {
            partyChatLines.Add(line);
            if (partyChatLines.Count > 200)
            {
                partyChatLines.RemoveRange(0, partyChatLines.Count - 200);
            }

            partyChatStickToBottom = true;
        }

        if (stream.Mode == StreamMode.None && partyChatLines.Count > 0)
        {
            partyChatLines.Clear();
        }
    }

    private void DrawPartyPanel()
    {
        DrainPartyChat();
        SectionHeader("Watch party");

        if (CurrentSession is null)
        {
            ImGui.TextColored(MutedText, "Sign in under Settings to host or join a synced room.");
            if (ImGui.SmallButton("Open Settings"))
            {
                currentPage = HomePage.Settings;
            }

            return;
        }

        switch (stream.Mode)
        {
            case StreamMode.Hosting:
                DrawStage("##partyHosting", () =>
                {
                    ImGui.TextColored(Good, "HOSTING");
                    ImGui.TextUnformatted($"{CurrentDisplayName ?? "Your"} room");
                    ImGui.TextColored(MutedText, $"{stream.Roster.Length} watching · playback stays locked to you");
                    ImGui.Spacing();
                    var isPrivate = stream.IsPrivate;
                    if (ImGui.Checkbox("Private (hide from friends' presence)", ref isPrivate))
                    {
                        stream.IsPrivate = isPrivate;
                    }

                    if (ImGui.Button("Copy party invite", new Vector2(-1, 30)))
                    {
                        ImGui.SetClipboardText(
                            $"Come watch with me! Right-click my character and choose \"Join Stream\" " +
                            $"(or open AlphaChannel → Player and join \"{CurrentDisplayName}\").");
                    }
                });
                DrawRoster($"Watching ({stream.Roster.Length})", allowPromote: true);
                DrawReactions();
                ImGui.Spacing();
                DrawPartyChat();
                break;

            case StreamMode.Viewing:
                DrawStage("##partyViewing", () =>
                {
                    ImGui.TextColored(Good, "IN ROOM");
                    ImGui.TextUnformatted(joinedHostDisplayName is { } host ? $"{host}'s room" : "A friend's room");
                    ImGui.TextColored(MutedText, $"{stream.Roster.Length} also here");
                    ImGui.Spacing();
                    if (ImGui.Button("Leave room", new Vector2(-1, 30)))
                    {
                        LeaveStream();
                        partyChatLines.Clear();
                    }
                });
                DrawRoster($"Also here ({stream.Roster.Length})", allowPromote: false);
                DrawReactions();
                ImGui.Spacing();
                DrawPartyChat();
                break;

            default:
                ImGui.TextColored(MutedText,
                    "Play a video above and friends can join you automatically. Or join someone:");
                ImGui.Spacing();
                ImGui.SetNextItemWidth(-100f);
                if (playerFocusJoin)
                {
                    ImGui.SetKeyboardFocusHere();
                    playerFocusJoin = false;
                }

                ImGui.InputTextWithHint("##hostName", "Their AlphaChannel name", ref joinHostNameInput, 32);
                ImGui.SameLine();
                if (ImGui.Button("Join", new Vector2(88, 0)))
                {
                    DoJoin(joinHostNameInput);
                }

                if (joinError is { } error)
                {
                    ImGui.TextColored(Danger, error);
                }

                break;
        }
    }

    private void DrawPartyChat()
    {
        ImGui.TextUnformatted("Party chat");
        ImGui.TextColored(MutedText, "Only people in this room see these messages.");
        ImGui.Spacing();

        var height = MathF.Min(180f, MathF.Max(100f, ImGui.GetContentRegionAvail().Y * 0.35f));
        using (var child = ImRaii.Child("##partyChatLog", new Vector2(-1, height), true,
                   ImGuiWindowFlags.NoScrollbar))
        {
            if (child)
            {
                if (partyChatLines.Count == 0)
                {
                    ImGui.TextColored(MutedText, "Say something…");
                }
                else
                {
                    foreach (var (name, text) in partyChatLines)
                    {
                        ImGui.TextColored(Accent, name);
                        ImGui.SameLine();
                        ImGui.TextWrapped(text);
                    }
                }

                if (partyChatStickToBottom)
                {
                    ImGui.SetScrollHereY(1f);
                    partyChatStickToBottom = false;
                }
            }
        }

        ImGui.SetNextItemWidth(-70f);
        var sent = ImGui.InputTextWithHint("##partyChatInput", "Message…", ref partyChatInput, 280,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.Button("Send") || sent) && partyChatInput.Trim().Length > 0)
        {
            var text = partyChatInput.Trim();
            partyChatInput = string.Empty;
            _ = stream.SendChatAsync(text);
            partyChatStickToBottom = true;
        }
    }
}
