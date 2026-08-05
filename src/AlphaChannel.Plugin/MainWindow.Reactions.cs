using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Scoped down from "floating emoji over the in-world screen" to floating-in-the-GUI-window for
// time - a full 3D billboard/particle system through ScreenPainter would be a much bigger lift
// than a night-of-features batch justifies. Still delivers the actual feature (send/see quick
// reactions during a watch-along), just rendered in the plugin window instead of the game world.
// Uses FontAwesome glyphs (via IconButton/UiBuilder.IconFont), not raw emoji Unicode - Dalamud's
// default UI font doesn't have emoji glyphs loaded, so those rendered as nothing at all.
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

    private static readonly TimeSpan ReactionLifetime = TimeSpan.FromSeconds(2.5);
    private const float StageHeight = 90f;

    private readonly List<ActiveReaction> activeReactions = new();
    private readonly Random reactionRandom = new();

    private void DrawReactions()
    {
        while (stream.IncomingReactions.TryDequeue(out var incoming))
        {
            activeReactions.Add(new ActiveReaction(incoming.Glyph, DateTime.UtcNow,
                (float)(reactionRandom.NextDouble() * 50 - 25)));
        }

        activeReactions.RemoveAll(reaction => DateTime.UtcNow - reaction.SpawnedAt >= ReactionLifetime);

        ImGui.Text("Reactions");
        for (var index = 0; index < ReactionIcons.Length; index++)
        {
            if (index > 0)
            {
                ImGui.SameLine();
            }

            if (IconButton(ReactionIcons[index]) && stream.Mode != StreamMode.None)
            {
                _ = stream.SendReactionAsync(ReactionIcons[index].ToIconString());
            }
        }

        // Reserve a fixed "stage" area for reactions to rise through, Facebook-Live-style - each
        // one spawns at the bottom, drifts up and fades out over its lifetime, with a bit of
        // random horizontal jitter so a burst of the same reaction doesn't stack in one column.
        ImGui.Dummy(new Vector2(-1f, StageHeight));
        if (activeReactions.Count == 0)
        {
            return;
        }

        var stageMin = ImGui.GetItemRectMin();
        var stageMax = ImGui.GetItemRectMax();
        var centerX = (stageMin.X + stageMax.X) / 2f;
        var drawList = ImGui.GetWindowDrawList();
        var now = DateTime.UtcNow;

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            foreach (var reaction in activeReactions)
            {
                var progress = Math.Clamp((float)(now - reaction.SpawnedAt).TotalSeconds /
                    (float)ReactionLifetime.TotalSeconds, 0f, 1f);
                var position = new Vector2(centerX + reaction.XJitter, stageMax.Y - progress * StageHeight);
                var color = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f - progress));
                drawList.AddText(position, color, reaction.Glyph);
            }
        }
    }

    private readonly record struct ActiveReaction(string Glyph, DateTime SpawnedAt, float XJitter);
}
