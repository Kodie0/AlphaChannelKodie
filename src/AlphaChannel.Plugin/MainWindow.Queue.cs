using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

internal sealed partial class MainWindow
{
    private const float QueueThumbnailHeight = 40f;

    private void DrawQueue()
    {
        if (queue.Entries.Count == 0)
        {
            DrawPlainEmpty("Nothing queued — add from Discover or paste a URL above.");
            return;
        }

        using var child = ImRaii.Child("##queueList", new Vector2(-1, 200), false,
            ImGuiWindowFlags.NoScrollbar);
        if (!child)
        {
            return;
        }

        for (var index = 0; index < queue.Entries.Count; index++)
        {
            var entry = queue.Entries[index];
            ImGui.PushID(index);

            using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBg))
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(12, 10)))
            using (var row = ImRaii.Child($"##qrow{entry.Id}", new Vector2(-1, 56), false,
                       PaddedChild | ImGuiWindowFlags.NoScrollbar))
            {
                if (row)
                {
                    var thumbnail = thumbnails.Get(entry.ThumbnailUrl);
                    if (thumbnail is not null)
                    {
                        var width = QueueThumbnailHeight * thumbnail.Width / MathF.Max(thumbnail.Height, 1);
                        ImGui.Image(thumbnail.Handle, new Vector2(width, QueueThumbnailHeight));
                        ImGui.SameLine();
                    }

                    ImGui.BeginGroup();
                    ImGui.TextUnformatted(entry.Title);
                    if (entry.Duration is { } duration)
                    {
                        ImGui.TextColored(MutedText, FormatTime((float)duration.TotalSeconds));
                    }
                    else if (!string.IsNullOrEmpty(entry.Source))
                    {
                        ImGui.TextColored(MutedText, entry.Source);
                    }

                    ImGui.EndGroup();

                    ImGui.SameLine(ImGui.GetWindowWidth() - 110);
                    if (IconButton(FontAwesomeIcon.ChevronUp) && index > 0)
                    {
                        queue.Reorder(index, index - 1);
                    }

                    ImGui.SameLine();
                    if (IconButton(FontAwesomeIcon.ChevronDown) && index < queue.Entries.Count - 1)
                    {
                        queue.Reorder(index, index + 1);
                    }

                    ImGui.SameLine();
                    if (IconButton(FontAwesomeIcon.Trash))
                    {
                        queue.Remove(entry);
                    }
                }
            }

            ImGui.Spacing();
            ImGui.PopID();
        }
    }
}
