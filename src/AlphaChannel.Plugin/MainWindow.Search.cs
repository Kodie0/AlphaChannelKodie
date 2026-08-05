using System.Diagnostics;
using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AlphaChannel.Plugin;

internal sealed partial class MainWindow
{
    private readonly VideoUrlResolver searchResolver = new();
    private readonly TwitchChannelChecker twitchChecker = new();
    private string searchQuery = string.Empty;
    private string cookiesPathInput = Plugin.Cfg.YouTubeCookiesPath ?? string.Empty;
    private string? cookiesSearchError;

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
            ImGui.Spacing();
            DrawYouTubeSearch();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Twitch"))
        {
            ImGui.Spacing();
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

        // Tucked away below the search box, collapsed by default - this is a one-time setup step
        // for a minority of videos, not the primary thing this tab is for, and shouldn't compete
        // with the search box for attention every time the tab opens.
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Age-restricted video settings"))
        {
            ImGui.Indent();
            DrawCookiesSettings();
            ImGui.Unindent();
        }

        if (searchResults is not { } results || results.Count == 0)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        SectionHeader($"Results ({results.Count})");

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

            if (index < results.Count - 1)
            {
                ImGui.Separator();
            }

            ImGui.PopID();
        }
    }

    private async Task RunSearchAsync(string query)
    {
        searchResults = await searchResolver.SearchAsync(query, 8, CancellationToken.None).ConfigureAwait(false);
        isSearching = false;
    }

    // Opt-in workaround for age-restricted videos, which yt-dlp otherwise refuses outright. Only
    // ever stores/uses a file path the player supplies themselves - see Configuration's own note
    // on why this isn't something the plugin generates or transmits.
    private void DrawCookiesSettings()
    {
        ImGui.TextWrapped("Age-restricted videos need a YouTube login (cookies.txt).");
        if (ImGui.Button("Open YouTube to sign in"))
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.youtube.com") { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                AepLog.Warning($"[YouTube] Failed to open browser: {exception.Message}");
            }
        }

        ImGui.Spacing();

        var useFirefox = Plugin.Cfg.UseFirefoxCookies;
        if (ImGui.Checkbox("Read cookies from Firefox automatically", ref useFirefox))
        {
            Plugin.Cfg.UseFirefoxCookies = useFirefox;
            Plugin.Cfg.Save();
            video.UseFirefoxCookies = useFirefox;
        }

        ImGui.TextDisabled("Best-effort - needs an actual logged-in Firefox session.");
        ImGui.TextDisabled("Falls back to the path below if it can't find one.");

        ImGui.Spacing();
        ImGui.SetNextItemWidth(-70f);
        ImGui.InputTextWithHint("##cookiesPath", "Path to cookies.txt", ref cookiesPathInput, 260);
        ImGui.SameLine();
        if (ImGui.Button("Save##cookies"))
        {
            var path = string.IsNullOrWhiteSpace(cookiesPathInput) ? null : cookiesPathInput.Trim();
            Plugin.Cfg.YouTubeCookiesPath = path;
            Plugin.Cfg.Save();
            video.CookiesPath = path;
        }

        if (ImGui.SmallButton("Find in Downloads"))
        {
            cookiesSearchError = null;
            var found = FindCookiesFileInDownloads();
            if (found is not null)
            {
                cookiesPathInput = found;
            }
            else
            {
                cookiesSearchError = "No cookies file found in Downloads - export one first (see above).";
            }
        }

        if (cookiesSearchError is { } searchError)
        {
            ImGui.TextColored(Danger, searchError);
        }

        if (!string.IsNullOrEmpty(Plugin.Cfg.YouTubeCookiesPath))
        {
            var exists = File.Exists(Plugin.Cfg.YouTubeCookiesPath);
            ImGui.TextColored(exists ? Good : Danger, exists ? "Cookies file found." : "File not found at that path.");
        }
    }

    // Browser cookie-export extensions default to saving into Downloads - this saves typing the
    // full path out by hand. Picks whichever matching file was modified most recently, in case
    // there are several from past exports.
    private static string? FindCookiesFileInDownloads()
    {
        try
        {
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloads))
            {
                return null;
            }

            return Directory.GetFiles(downloads, "*cookies*.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[YouTube] Failed to search Downloads for a cookies file: {exception.Message}");
            return null;
        }
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

        ImGui.TextDisabled("Not a search - checks whether one named channel is live right now.");

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

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

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
