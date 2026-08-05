using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

internal sealed partial class MainWindow
{
    private const float QueueThumbnailHeight = 32f;

    private void DrawQueue()
    {
        if (queue.Entries.Count == 0)
        {
            ImGui.TextDisabled("Nothing queued.");
            return;
        }

        // Bordered, height-capped region instead of letting a long queue push the whole window
        // taller indefinitely - scrolls internally past ~4-5 entries.
        using var child = ImRaii.Child("##queueList", new Vector2(-1, 160), true);
        if (!child)
        {
            return;
        }

        for (var index = 0; index < queue.Entries.Count; index++)
        {
            var entry = queue.Entries[index];
            ImGui.PushID(index);

            var thumbnail = thumbnails.Get(entry.ThumbnailUrl);
            if (thumbnail is not null)
            {
                var width = QueueThumbnailHeight * thumbnail.Width / MathF.Max(thumbnail.Height, 1);
                ImGui.Image(thumbnail.Handle, new Vector2(width, QueueThumbnailHeight));
                ImGui.SameLine();
            }

            ImGui.BeginGroup();
            ImGui.TextWrapped(entry.Title);
            if (entry.Duration is { } duration)
            {
                ImGui.TextDisabled(FormatTime((float)duration.TotalSeconds));
            }

            ImGui.EndGroup();

            ImGui.SameLine();
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

            if (index < queue.Entries.Count - 1)
            {
                ImGui.Separator();
            }

            ImGui.PopID();
        }
    }
}
