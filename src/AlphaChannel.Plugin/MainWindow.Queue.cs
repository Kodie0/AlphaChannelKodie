using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AlphaChannel.Plugin;

internal sealed partial class MainWindow
{
    private const float QueueThumbnailHeight = 32f;

    private void DrawQueue()
    {
        ImGui.Text("Queue");
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

            ImGui.PopID();
        }
    }
}
