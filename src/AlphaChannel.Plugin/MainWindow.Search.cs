using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AlphaChannel.Plugin;

internal sealed partial class MainWindow
{
    private readonly VideoUrlResolver searchResolver = new();
    private string searchQuery = string.Empty;

    // Written from SearchAsync's continuation, which resumes on an arbitrary thread pool thread
    // (not the main thread Draw() runs on) - same reasoning as Plugin.cs's pendingRemoteState.
    private volatile bool isSearching;
    private volatile List<VideoSearchEntry>? searchResults;

    private void DrawSearch()
    {
        ImGui.Text("Search YouTube");
        ImGui.SetNextItemWidth(-40f);
        ImGui.InputTextWithHint("##search", "Search query", ref searchQuery, 200);
        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Search) && searchQuery.Length > 0 && !isSearching)
        {
            isSearching = true;
            _ = RunSearchAsync(searchQuery);
        }

        if (isSearching)
        {
            ImGui.TextDisabled("Searching...");
        }

        if (searchResults is not { } results)
        {
            return;
        }

        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            ImGui.PushID(index);

            var thumbnail = thumbnails.Get(result.ThumbnailUrl);
            if (thumbnail is not null)
            {
                var width = QueueThumbnailHeight * thumbnail.Width / MathF.Max(thumbnail.Height, 1);
                ImGui.Image(thumbnail.Handle, new Vector2(width, QueueThumbnailHeight));
                ImGui.SameLine();
            }

            ImGui.BeginGroup();
            ImGui.TextWrapped(result.Title);
            var meta = result.Duration is { } duration
                ? $"{result.ChannelName} - {FormatTime((float)duration.TotalSeconds)}"
                : result.ChannelName;
            ImGui.TextDisabled(meta);
            ImGui.EndGroup();

            ImGui.SameLine();
            if (ImGui.SmallButton("Play now"))
            {
                queue.PlayNow(new VideoQueueEntry(result.Url, result.Title, result.ChannelName, result.Duration,
                    result.ThumbnailUrl));
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Add"))
            {
                queue.Add(new VideoQueueEntry(result.Url, result.Title, result.ChannelName, result.Duration,
                    result.ThumbnailUrl));
            }

            ImGui.PopID();
        }
    }

    private async Task RunSearchAsync(string query)
    {
        searchResults = await searchResolver.SearchAsync(query, 8, CancellationToken.None).ConfigureAwait(false);
        isSearching = false;
    }
}
