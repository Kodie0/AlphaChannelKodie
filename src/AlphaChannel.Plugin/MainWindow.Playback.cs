using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AlphaChannel.Plugin;

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

        // Now-playing status first - the thing you actually glance at, above the controls that
        // change it.
        if (queue.Current is { } current)
        {
            ImGui.TextWrapped(current.Title);
        }
        else
        {
            ImGui.TextDisabled("Nothing playing.");
        }

        if (!seekDragging)
        {
            seekPreview = position;
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.SliderFloat("##seek", ref seekPreview, 0f, MathF.Max(duration, 0.01f), "");
        seekDragging = ImGui.IsItemActive();
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            video.Seek(seekPreview);
        }

        ImGui.TextDisabled($"{FormatTime(position)} / {FormatTime(duration)}");

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
            // Deliberately queue.Clear(), not video.Pause/Stop directly - Clear() is the one path
            // that actually deactivates the in-world screen (makes it disappear) rather than just
            // freezing it on the last frame, which is what a bare Stop leaves behind (see
            // AetherStreamQueue.Advance's own comment on why that's the deliberate idle behavior).
            queue.Clear();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        SectionHeader("Play something");

        ImGui.SetNextItemWidth(-70f);
        var submittedUrl = ImGui.InputTextWithHint("##url", "Video URL", ref urlInput, 2000,
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

        var playNowClicked = ImGui.Button("Play now");
        if ((submittedUrl || playNowClicked) && urlInput.Length > 0)
        {
            queue.PlayNow(new VideoQueueEntry(urlInput, urlInput, string.Empty, null, null));
            urlInput = string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.Button("Add to queue") && urlInput.Length > 0)
        {
            queue.Add(new VideoQueueEntry(urlInput, urlInput, string.Empty, null, null));
            urlInput = string.Empty;
        }
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
