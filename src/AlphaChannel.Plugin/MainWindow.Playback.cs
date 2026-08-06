using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Player is the transport deck — stage for what's on screen, then a flat URL strip and lists.
// Not a stack of identical cards; the stage is the identity.
internal sealed partial class MainWindow
{
    private string urlInput = string.Empty;

    // The seek bar tracks live playback position every frame except while the user is actively
    // dragging it - see the Draw body below for why: mpv keeps advancing position during a drag,
    // and resetting the slider's value from that every frame would fight the user's own drag
    // input, snapping back to "now" instead of following the mouse.
    private float seekPreview;
    private bool seekDragging;

    private void DrawPlayback()
    {
        var (position, duration, isPaused) = video.GetProgress();

        DrawStage("##nowPlaying", () =>
        {
            if (queue.Current is { } current)
            {
                ImGui.TextColored(Accent, "NOW PLAYING");
                ImGui.SetWindowFontScale(1.25f);
                ImGui.TextWrapped(current.Title);
                ImGui.SetWindowFontScale(1f);
            }
            else
            {
                ImGui.TextColored(MutedText, "Nothing playing");
                ImGui.TextWrapped("Paste a YouTube/Twitch link below, or search under Find a video.");
            }

            if (!seekDragging)
            {
                seekPreview = position;
            }

            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1f);
            ImGui.SliderFloat("##seek", ref seekPreview, 0f, MathF.Max(duration, 0.01f), "");
            seekDragging = ImGui.IsItemActive();
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                video.Seek(seekPreview);
            }

            var (streamWidth, streamHeight) = video.GetResolution();
            var timeText = $"{FormatTime(position)} / {FormatTime(duration)}";
            ImGui.TextColored(MutedText, streamWidth > 0 && streamHeight > 0
                ? $"{timeText}  ·  {streamWidth}x{streamHeight}"
                : timeText);

            if (video.LastError is { } error)
            {
                ImGui.TextColored(Danger, error);
            }

            ImGui.Spacing();

            if (IconButton(isPaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause))
            {
                video.Pause(!isPaused);
            }

            ImGui.SameLine();
            if (IconButton(FontAwesomeIcon.Forward))
            {
                queue.Advance();
            }

            ImGui.SameLine();
            DrawVolumeControl();

            ImGui.SameLine();
            if (IconButton(FontAwesomeIcon.PowerOff))
            {
                queue.Clear();
            }
        });

        ImGui.TextColored(MutedText, "Paste a link");
        ImGui.SetNextItemWidth(-70f);
        var submittedUrl = ImGui.InputTextWithHint("##url", "https://…", ref urlInput, 2000,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if (ImGui.Button("Paste"))
        {
            var clipboard = ImGui.GetClipboardText();
            if (!string.IsNullOrWhiteSpace(clipboard))
            {
                urlInput = clipboard.Trim();
            }
        }

        var playNowClicked = ImGui.Button("Play now", new Vector2(120, 30));
        if ((submittedUrl || playNowClicked) && urlInput.Length > 0)
        {
            queue.PlayNow(new VideoQueueEntry(urlInput, urlInput, string.Empty, null, null));
            urlInput = string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.Button("Add to queue", new Vector2(120, 30)) && urlInput.Length > 0)
        {
            queue.Add(new VideoQueueEntry(urlInput, urlInput, string.Empty, null, null));
            urlInput = string.Empty;
        }

        ImGui.Spacing();
    }

    private void DrawVolumeControl()
    {
        if (IconButton(Plugin.Cfg.Muted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp))
        {
            Plugin.Cfg.Muted = !Plugin.Cfg.Muted;
            video.SetVolume(Plugin.Cfg.Muted ? 0 : Plugin.Cfg.Volume);
            Plugin.Cfg.Save();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        var volume = Plugin.Cfg.Volume;
        if (ImGui.SliderInt("##volume", ref volume, 0, 100))
        {
            Plugin.Cfg.Volume = volume;
            video.SetVolume(Plugin.Cfg.Muted ? 0 : volume);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Plugin.Cfg.Save();
        }
    }

    private static string FormatTime(float totalSeconds)
    {
        if (totalSeconds < 0 || float.IsNaN(totalSeconds) || float.IsInfinity(totalSeconds))
        {
            totalSeconds = 0;
        }

        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.Hours > 0 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }
}
