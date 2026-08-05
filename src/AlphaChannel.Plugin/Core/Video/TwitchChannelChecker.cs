using System.Diagnostics;
using System.Text.Json;

namespace AlphaChannel.Plugin.Video;

internal sealed record TwitchStreamInfo(string Title, string Url, string ChannelName, string? ThumbnailUrl);

// Not a search - Twitch has no keyword-search extractor in yt-dlp the way YouTube does, and a real
// browsable search needs Twitch's own Helix API (an app registration + Client ID/Secret, deferred
// per the user's own choice). This just asks yt-dlp "is this specific channel live right now",
// reusing the exact same yt-dlp binary/pipeline already downloaded for YouTube playback - no new
// dependency, no subprocess-spawning pattern that hasn't already been proven to work under Wine
// (mpv's own ytdl_hook already spawns this same binary internally for every YouTube play).
internal sealed class TwitchChannelChecker
{
    public async Task<(TwitchStreamInfo? Stream, string? Error)> CheckLiveAsync(string ytdlpPath, string channelName,
        CancellationToken token)
    {
        var url = $"https://www.twitch.tv/{channelName}";
        var startInfo = new ProcessStartInfo(ytdlpPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-j");
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add(url);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (null, "Could not start yt-dlp.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
            var stderrTask = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                var offline = stderr.Contains("offline", StringComparison.OrdinalIgnoreCase);
                return (null, offline ? $"{channelName} is not live right now." : "Could not find that channel.");
            }

            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            var title = root.TryGetProperty("title", out var titleProperty) ? titleProperty.GetString() ?? channelName : channelName;
            var thumbnail = root.TryGetProperty("thumbnail", out var thumbnailProperty) ? thumbnailProperty.GetString() : null;
            return (new TwitchStreamInfo(title, url, channelName, thumbnail), null);
        }
        catch (OperationCanceledException)
        {
            return (null, null);
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Twitch] Check failed for {channelName}: {exception.Message}");
            return (null, "Something went wrong checking that channel.");
        }
    }
}
