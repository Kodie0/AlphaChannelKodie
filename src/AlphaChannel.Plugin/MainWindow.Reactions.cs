using Dalamud.Bindings.ImGui;

namespace AlphaChannel.Plugin;

// Scoped down from "floating emoji over the in-world screen" to floating-in-the-GUI-window for
// time - a full 3D billboard/particle system through ScreenPainter would be a much bigger lift
// than a night-of-features batch justifies. Still delivers the actual feature (send/see quick
// reactions during a watch-along), just rendered in the plugin window instead of the game world.
internal sealed partial class MainWindow
{
    private static readonly string[] ReactionGlyphs = ["\U0001F44D", "\U0001F602", "❤️", "\U0001F62E", "\U0001F389"];
    private static readonly TimeSpan ReactionLifetime = TimeSpan.FromSeconds(3);

    private readonly List<(string Glyph, string SenderName, DateTime ExpiresAt)> activeReactions = new();

    private void DrawReactions()
    {
        while (stream.IncomingReactions.TryDequeue(out var incoming))
        {
            var senderName = stream.Roster.FirstOrDefault(p => p.UserId == incoming.SenderUserId)?.DisplayName
                ?? "Someone";
            activeReactions.Add((incoming.Glyph, senderName, DateTime.UtcNow + ReactionLifetime));
        }

        activeReactions.RemoveAll(reaction => reaction.ExpiresAt <= DateTime.UtcNow);

        ImGui.Text("Reactions");
        for (var index = 0; index < ReactionGlyphs.Length; index++)
        {
            if (index > 0)
            {
                ImGui.SameLine();
            }

            if (ImGui.Button(ReactionGlyphs[index]) && stream.Mode != StreamMode.None)
            {
                _ = stream.SendReactionAsync(ReactionGlyphs[index]);
            }
        }

        if (activeReactions.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        for (var index = 0; index < activeReactions.Count; index++)
        {
            var (glyph, senderName, expiresAt) = activeReactions[index];
            var remaining = (expiresAt - now).TotalSeconds / ReactionLifetime.TotalSeconds;
            ImGui.TextColored(new Vector4(1f, 1f, 1f, (float)Math.Clamp(remaining, 0, 1)), $"{glyph} {senderName}");
        }
    }
}
