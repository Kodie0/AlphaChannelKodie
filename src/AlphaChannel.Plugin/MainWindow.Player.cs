using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Player is the single watch surface: source switcher, quiet empty deck, queue, and watch party.
internal sealed partial class MainWindow
{
    private void DrawPlayerPage()
    {
        DrawPlayerSourceTabs();
        ImGui.Spacing();
        ImGui.Spacing();

        DrawPlayback();

        switch (playerSourceTab)
        {
            case 0:
                DrawLinkSource();
                break;
            case 1:
                DrawYouTubeSearch();
                break;
            case 2:
                DrawTwitchCheck();
                break;
            case 3:
                DrawGoLive();
                break;
        }

        ImGui.Spacing();
        SectionHeader("Queue");
        DrawQueue();

        ImGui.Spacing();
        ImGui.Spacing();
        DrawPartyPanel();
    }

    private void DrawPlayerSourceTabs()
    {
        DrawPlayerSourceTab("Link", 0);
        ImGui.SameLine();
        DrawPlayerSourceTab("YouTube", 1);
        ImGui.SameLine();
        DrawPlayerSourceTab("Twitch", 2);
        ImGui.SameLine();
        DrawPlayerSourceTab("Go Live", 3);
    }

    private void DrawPlayerSourceTab(string label, int tab)
    {
        var selected = playerSourceTab == tab;
        using (ImRaii.PushColor(ImGuiCol.Button, selected ? Accent : CardBg)
                   .Push(ImGuiCol.ButtonHovered, selected ? AccentHover : CardBgHover)
                   .Push(ImGuiCol.ButtonActive, selected ? AccentActive : CardBgHover)
                   .Push(ImGuiCol.Text, selected ? Vector4.One : MutedText))
        {
            if (ImGui.Button(label, new Vector2(100, 30)))
            {
                playerSourceTab = tab;
            }
        }
    }

    private void DrawLinkSource()
    {
        ImGui.TextColored(MutedText, "Paste a YouTube, Twitch, or direct video URL.");
        ImGui.SetNextItemWidth(-70f);
        var submittedUrl = ImGui.InputTextWithHint("##url", "https://…", ref urlInput, 2000,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if (ImGui.Button("Paste"))
        {
            var clipboard = ImGui.GetClipboardText();
            if (!string.IsNullOrWhiteSpace(clipboard))
            {
                urlInput = clipboard.Trim();
            }
        }

        var playNowClicked = ImGui.Button("Play now", new Vector2(120, 30));
        if ((submittedUrl || playNowClicked) && urlInput.Length > 0)
        {
            queue.PlayNow(new Video.VideoQueueEntry(urlInput, urlInput, string.Empty, null, null));
            urlInput = string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.Button("Add to queue", new Vector2(120, 30)) && urlInput.Length > 0)
        {
            queue.Add(new Video.VideoQueueEntry(urlInput, urlInput, string.Empty, null, null));
            urlInput = string.Empty;
        }
    }
}
