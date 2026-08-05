using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AlphaChannel.Plugin;

internal sealed partial class MainWindow
{
    private readonly VideoUrlResolver searchResolver = new();
    private readonly TwitchChannelChecker twitchChecker = new();
    private string searchQuery = string.Empty;

    // Written from RunSearchAsync's continuation, which resumes on an arbitrary thread pool thread
    // (not the main thread Draw() runs on) - same reasoning as Plugin.cs's pendingRemoteState.
    private volatile bool isSearching;
    private volatile List<VideoSearchEntry>? searchResults;

    private string twitchChannelInput = string.Empty;
    private volatile bool isCheckingTwitch;
    private volatile TwitchStreamInfo? twitchResult;
    private volatile string? twitchError;

    private void DrawSearch()
    {
        if (!ImGui.BeginTabBar("##searchTabs"))
        {
            return;
        }

        if (ImGui.BeginTabItem("YouTube"))
        {
            DrawYouTubeSearch();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Twitch"))
        {
            DrawTwitchCheck();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawYouTubeSearch()
    {
        ImGui.SetNextItemWidth(-40f);
        var submitted = ImGui.InputTextWithHint("##search", "Search query", ref searchQuery, 200,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        var clicked = IconButton(FontAwesomeIcon.Search);
        if ((submitted || clicked) && searchQuery.Length > 0 && !isSearching)
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

    // Not a real search - see TwitchChannelChecker's own comment on why. Just checks whether one
    // named channel is currently live.
    private void DrawTwitchCheck()
    {
        ImGui.SetNextItemWidth(-70f);
        var submitted = ImGui.InputTextWithHint("##twitchChannel", "Twitch channel name", ref twitchChannelInput, 64,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        var clicked = ImGui.Button("Check");
        if ((submitted || clicked) && twitchChannelInput.Length > 0 && !isCheckingTwitch)
        {
            isCheckingTwitch = true;
            twitchResult = null;
            twitchError = null;
            _ = RunTwitchCheckAsync(twitchChannelInput.Trim());
        }

        if (isCheckingTwitch)
        {
            ImGui.TextDisabled("Checking...");
        }

        if (twitchError is { } error)
        {
            ImGui.TextColored(Danger, error);
        }

        if (twitchResult is not { } stream)
        {
            return;
        }

        var thumbnail = thumbnails.Get(stream.ThumbnailUrl);
        if (thumbnail is not null)
        {
            var width = QueueThumbnailHeight * thumbnail.Width / MathF.Max(thumbnail.Height, 1);
            ImGui.Image(thumbnail.Handle, new Vector2(width, QueueThumbnailHeight));
            ImGui.SameLine();
        }

        ImGui.BeginGroup();
        ImGui.TextWrapped(stream.Title);
        ImGui.TextDisabled($"{stream.ChannelName} - live now");
        ImGui.EndGroup();

        ImGui.SameLine();
        if (ImGui.SmallButton("Play now"))
        {
            queue.PlayNow(new VideoQueueEntry(stream.Url, stream.Title, stream.ChannelName, null, stream.ThumbnailUrl));
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Add"))
        {
            queue.Add(new VideoQueueEntry(stream.Url, stream.Title, stream.ChannelName, null, stream.ThumbnailUrl));
        }
    }

    private async Task RunTwitchCheckAsync(string channelName)
    {
        var ytdlpPath = screenController.Engine.Resources.GetLocationYTDLP();
        if (ytdlpPath is null)
        {
            twitchError = "yt-dlp isn't downloaded yet - try again in a moment.";
            isCheckingTwitch = false;
            return;
        }

        var (stream, error) = await twitchChecker.CheckLiveAsync(ytdlpPath, channelName, CancellationToken.None)
            .ConfigureAwait(false);
        twitchResult = stream;
        twitchError = error;
        isCheckingTwitch = false;
    }
}
