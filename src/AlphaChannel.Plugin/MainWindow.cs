using System.Diagnostics;
using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace AlphaChannel.Plugin;

// Split into partials by concern (MainWindow.Playback.cs, .Queue.cs, .Search.cs, .Screen.cs) -
// this file just has the window skeleton, the name prompt, connection status, watch-along, and
// the shared theme. A lightweight custom theme (this file's ThemeScope), not a port of
// Aetherphone's Typography/Squircle kit - that's still too much surface area for this tool, but a
// bare-default-gray ImGui window doesn't read as a real player either.
internal sealed partial class MainWindow : Window, IDisposable
{
    private static readonly Vector4 Accent = new(0.42f, 0.45f, 0.95f, 1f);
    private static readonly Vector4 AccentHover = new(0.52f, 0.56f, 1.00f, 1f);
    private static readonly Vector4 AccentActive = new(0.32f, 0.35f, 0.82f, 1f);
    private static readonly Vector4 FrameBg = new(0.14f, 0.14f, 0.19f, 1f);
    private static readonly Vector4 FrameBgHover = new(0.20f, 0.20f, 0.28f, 1f);
    private static readonly Vector4 Danger = new(0.95f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 Good = new(0.3f, 0.9f, 0.4f, 1f);

    private readonly ScreenController screenController;
    private readonly VideoPlayer video;
    private readonly AetherStreamQueue queue;
    private readonly StreamClient stream;
    private readonly ThumbnailCache thumbnails = new();
    private readonly Action requestRename;
    private string joinHostNameInput = string.Empty;
    private string? joinError;

    private bool namePromptPending;
    private bool namePromptActive;
    private string namePromptInput = string.Empty;
    private Action<string>? onNameConfirmed;

    internal bool IsNamePromptActive => namePromptActive;

    // Updated every tick from Plugin.cs (cheap dictionary lookup there) - shown here instead of the
    // raw UserId so players never need to read each other an opaque GUID to join a stream.
    internal string? CurrentDisplayName { get; set; }

    internal MainWindow(ScreenController screenController, VideoPlayer video, AetherStreamQueue queue,
        StreamClient stream, Action requestRename) : base("AlphaChannel###AlphaChannelMain")
    {
        this.screenController = screenController;
        this.video = video;
        this.queue = queue;
        this.stream = stream;
        this.requestRename = requestRename;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 460),
            MaximumSize = new Vector2(2000, 2000),
        };

        stream.OnJoined += () => joinError = null;
        stream.OnDeclined += reason => joinError = string.IsNullOrEmpty(reason) ? "Could not find that host." : reason;
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
        using var theme = new ThemeScope();

        DrawNamePrompt();
        DrawConnectionStatus();
        ImGui.Separator();
        DrawScreenControls();
        ImGui.Separator();
        DrawPlayback();
        ImGui.Separator();
        DrawQueue();
        ImGui.Separator();
        DrawSearch();
        ImGui.Separator();
        DrawWatchAlong();
        ImGui.Separator();
        DrawReactions();
        ImGui.Separator();
        DrawDonateButton();
    }

    private static readonly Vector4 KofiPink = new(0.98f, 0.29f, 0.55f, 1f);
    private static readonly Vector4 KofiPinkHover = new(1.00f, 0.42f, 0.65f, 1f);
    private static readonly Vector4 KofiPinkActive = new(0.85f, 0.20f, 0.45f, 1f);

    private void DrawDonateButton()
    {
        // Pinned to the bottom of the window's visible area (when there's room left), not just
        // wherever it happens to fall after the last section - kept visually separate from the
        // rest of the controls above rather than reading as part of Reactions.
        var targetY = ImGui.GetWindowSize().Y - ImGui.GetFrameHeightWithSpacing() - ImGui.GetStyle().WindowPadding.Y;
        if (targetY > ImGui.GetCursorPosY())
        {
            ImGui.SetCursorPosY(targetY);
        }

        using var color = ImRaii.PushColor(ImGuiCol.Button, KofiPink)
            .Push(ImGuiCol.ButtonHovered, KofiPinkHover)
            .Push(ImGuiCol.ButtonActive, KofiPinkActive);

        if (ImGui.Button("Like what we've built? Donate on Ko-fi"))
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://ko-fi.com/alphachannel") { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                AepLog.Warning($"[Donate] Failed to open browser: {exception.Message}");
            }
        }
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
        ImGui.TextColored(stream.IsConnected ? Good : Danger, stream.IsConnected ? "Connected" : "Not connected");
    }

    private void DrawWatchAlong()
    {
        ImGui.Text("Watch-along");
        ImGui.TextDisabled($"Your name: {CurrentDisplayName ?? "..."}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Rename"))
        {
            requestRename();
        }

        switch (stream.Mode)
        {
            case StreamMode.Hosting:
                if (ImGui.Button("Copy party invite"))
                {
                    ImGui.SetClipboardText(
                        $"Come watch with me! Right-click my character and choose \"Join Stream\" " +
                        $"(or open AlphaChannel and join \"{CurrentDisplayName}\").");
                }

                DrawRoster($"Watching ({stream.Roster.Length})", allowPromote: true);
                break;

            case StreamMode.Viewing:
                if (ImGui.Button("Leave"))
                {
                    _ = stream.LeaveAsync();
                }

                DrawRoster($"Also watching ({stream.Roster.Length})", allowPromote: false);
                break;

            default:
                ImGui.SetNextItemWidth(-100f);
                ImGui.InputTextWithHint("##hostName", "Host's name", ref joinHostNameInput, 32);
                ImGui.SameLine();
                if (ImGui.Button("Join") && joinHostNameInput.Length > 0)
                {
                    queue.Clear();
                    _ = stream.JoinAsync(joinHostNameInput.Trim());
                }

                if (joinError is { } error)
                {
                    ImGui.TextColored(Danger, error);
                }

                break;
        }
    }

    private void DrawRoster(string label, bool allowPromote)
    {
        ImGui.Text(label);
        if (stream.Roster.Length == 0)
        {
            ImGui.TextDisabled("Nobody yet.");
            return;
        }

        for (var index = 0; index < stream.Roster.Length; index++)
        {
            var participant = stream.Roster[index];
            ImGui.BulletText(participant.DisplayName);
            if (!allowPromote)
            {
                continue;
            }

            ImGui.SameLine();
            ImGui.PushID(index);
            if (ImGui.SmallButton("Make host"))
            {
                _ = stream.TransferHostAsync(participant.UserId);
            }

            ImGui.PopID();
        }
    }

    public void Dispose()
    {
        thumbnails.Dispose();
    }

    // Shared by every partial that wants a play/pause/skip/volume-style glyph button instead of a
    // text label - Dalamud bundles FontAwesome already, no extra font asset needed.
    private static bool IconButton(FontAwesomeIcon icon)
    {
        using var iconFont = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.Button(icon.ToIconString());
    }

    private readonly struct ThemeScope : IDisposable
    {
        private const int ColorCount = 7;
        private const int StyleCount = 2;

        public ThemeScope()
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AccentHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, AccentActive);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, FrameBg);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, FrameBgHover);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, Accent);
            ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, AccentActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);
        }

        public void Dispose()
        {
            ImGui.PopStyleVar(StyleCount);
            ImGui.PopStyleColor(ColorCount);
        }
    }
}
