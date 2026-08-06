using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AlphaChannel.Plugin;

// Just the send buttons - the actual reactions now render on the in-world screen itself
// (Plugin.cs's UpdateReactions/VideoEngine.SetReactions/ScreenPainter's ReactionsPS), not floating
// in this GUI window. Only one place can drain stream.IncomingReactions (it's a ConcurrentQueue,
// not a broadcast), and Plugin.cs is it.
internal sealed partial class MainWindow
{
    private static readonly FontAwesomeIcon[] ReactionIcons =
    [
        FontAwesomeIcon.ThumbsUp,
        FontAwesomeIcon.Laugh,
        FontAwesomeIcon.Heart,
        FontAwesomeIcon.Surprise,
        FontAwesomeIcon.Star,
    ];

    private void DrawReactions()
    {
        ImGui.TextUnformatted("Reactions");
        ImGui.TextColored(MutedText, "They land on the in-world screen, not in this window.");
        ImGui.Spacing();

        if (stream.Mode == StreamMode.None)
        {
            ImGui.TextColored(MutedText, "Join or host a room first.");
            return;
        }

        for (var index = 0; index < ReactionIcons.Length; index++)
        {
            if (index > 0)
            {
                ImGui.SameLine(0, 10);
            }

            if (IconButton(ReactionIcons[index]))
            {
                _ = stream.SendReactionAsync(ReactionIcons[index].ToIconString());
            }
        }
    }
}
