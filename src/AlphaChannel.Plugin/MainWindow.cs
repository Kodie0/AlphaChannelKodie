using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AlphaChannel.Plugin;

// Deliberately plain ImGui, not a port of Aetherphone's phone-styled Casting/Player/Queue tabs -
// see the plan's note on why: that UI kit (Typography/Squircle/theme tokens) is a large surface
// area purely for visual polish a functional single-window tool doesn't need for v1.
internal sealed class MainWindow : Window, IDisposable
{
    private readonly ScreenController screenController;
    private readonly VideoPlayer video;
    private readonly AetherStreamQueue queue;
    private readonly StreamClient stream;
    private readonly Configuration configuration;
    private string urlInput = string.Empty;
    private string joinHostIdInput = string.Empty;

    private bool namePromptPending;
    private bool namePromptActive;
    private string namePromptInput = string.Empty;
    private Action<string>? onNameConfirmed;

    internal bool IsNamePromptActive => namePromptActive;

    internal MainWindow(ScreenController screenController, VideoPlayer video, AetherStreamQueue queue,
        StreamClient stream, Configuration configuration) : base("AlphaChannel###AlphaChannelMain")
    {
        this.screenController = screenController;
        this.video = video;
        this.queue = queue;
        this.stream = stream;
        this.configuration = configuration;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 420),
            MaximumSize = new Vector2(2000, 2000),
        };
    }

    // Called from Plugin.cs once per character that hasn't picked a name yet, or after an admin
    // reset - suggested is pre-filled (their real character name) so confirming needs no typing.
    internal void RequestNamePrompt(string suggested, Action<string> onConfirmed)
    {
        if (namePromptActive)
        {
            return;
        }

        namePromptInput = suggested;
        onNameConfirmed = onConfirmed;
        namePromptActive = true;
        namePromptPending = true;
        IsOpen = true;
    }

    public override void Draw()
    {
        DrawNamePrompt();
        DrawConnectionStatus();
        ImGui.Separator();
        DrawScreenControls();
        ImGui.Separator();
        DrawPlayback();
        ImGui.Separator();
        DrawQueue();
        ImGui.Separator();
        DrawWatchAlong();
    }

    private void DrawNamePrompt()
    {
        if (namePromptPending)
        {
            ImGui.OpenPopup("Choose your name");
            namePromptPending = false;
        }

        ImGui.SetNextWindowSize(new Vector2(320, 0));
        if (ImGui.BeginPopupModal("Choose your name", ImGuiWindowFlags.NoResize))
        {
            ImGui.TextWrapped("Pick the name other viewers will see for you.");
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("##displayName", ref namePromptInput, 32);
            if (ImGui.Button("Confirm") && namePromptInput.Trim().Length > 0)
            {
                onNameConfirmed?.Invoke(namePromptInput.Trim());
                onNameConfirmed = null;
                namePromptActive = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawConnectionStatus()
    {
        // The relay address is fixed (Configuration's default) and deliberately not exposed here -
        // players just connect, they don't need to know or be able to point it elsewhere.
        ImGui.TextColored(stream.IsConnected ? new Vector4(0.3f, 0.9f, 0.4f, 1f) : new Vector4(0.9f, 0.4f, 0.3f, 1f),
            stream.IsConnected ? "Connected" : "Not connected");
    }

    private void DrawScreenControls()
    {
        var engine = screenController.Engine;
        ImGui.Text("Screen");
        if (ImGui.Button("Recenter"))
        {
            engine.RecenterScreen();
        }

        var position = engine.ScreenPosition;
        var yaw = engine.ScreenYaw;
        var scale = engine.ScreenScale;
        var changed = false;
        changed |= ImGui.DragFloat3("Position", ref position, 0.05f);
        changed |= ImGui.SliderAngle("Yaw", ref yaw);
        changed |= ImGui.SliderFloat("Scale", ref scale, VideoEngine.MinScreenScale, VideoEngine.MaxScreenScale);
        if (changed)
        {
            engine.SetScreenTransform(position, yaw, scale);
        }
    }

    private void DrawPlayback()
    {
        ImGui.Text("Playback");
        ImGui.SetNextItemWidth(-70f);
        ImGui.InputTextWithHint("##url", "Video URL", ref urlInput, 2000);
        ImGui.SameLine();
        if (ImGui.Button("Paste"))
        {
            var clipboard = ImGui.GetClipboardText();
            if (!string.IsNullOrWhiteSpace(clipboard))
            {
                urlInput = clipboard.Trim();
            }
        }

        if (ImGui.Button("Play now") && urlInput.Length > 0)
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

        ImGui.SameLine();
        if (ImGui.Button("Skip"))
        {
            queue.Advance();
        }

        ImGui.SameLine();
        var isPaused = video.GetProgress().Paused;
        if (ImGui.Button(isPaused ? "Resume" : "Pause"))
        {
            video.Pause(!isPaused);
        }

        if (video.LastError is { } error)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.35f, 0.35f, 1f), error);
        }

        if (queue.Current is { } current)
        {
            ImGui.TextWrapped($"Now playing: {current.Title}");
        }
    }

    private void DrawQueue()
    {
        ImGui.Text("Queue");
        for (var index = 0; index < queue.Entries.Count; index++)
        {
            var entry = queue.Entries[index];
            ImGui.PushID(index);
            ImGui.TextWrapped(entry.Title);
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                queue.Remove(entry);
            }

            ImGui.PopID();
        }
    }

    private void DrawWatchAlong()
    {
        ImGui.Text("Watch-along");
        ImGui.TextDisabled($"Your ID: {configuration.UserId}");
        ImGui.SetNextItemWidth(-100f);
        ImGui.InputTextWithHint("##hostId", "Host's ID", ref joinHostIdInput, 64);
        ImGui.SameLine();
        if (ImGui.Button("Join") && joinHostIdInput.Length > 0)
        {
            _ = stream.JoinAsync(joinHostIdInput.Trim());
        }

        ImGui.SameLine();
        if (ImGui.Button("Leave"))
        {
            _ = stream.LeaveAsync();
        }

        ImGui.Text($"Mode: {stream.Mode}");
        for (var index = 0; index < stream.Roster.Length; index++)
        {
            ImGui.BulletText(stream.Roster[index].DisplayName);
        }
    }

    public void Dispose()
    {
    }
}
