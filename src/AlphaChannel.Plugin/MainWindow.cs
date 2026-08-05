using System.Diagnostics;
using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace AlphaChannel.Plugin;

// Split into partials by concern (MainWindow.Home.cs, .Playback.cs, .Queue.cs, .Search.cs,
// .Screen.cs, .Settings.cs, .Reactions.cs) - this file has the window skeleton: the sidebar nav,
// the theme/palette, the name prompt, and watch-along/roster (shared between the Home dashboard's
// Live Now card and the dedicated Watch-along page). Smart-TV-dashboard look (dark background,
// purple neon glow border, sidebar nav, rounded cards) built with plain ImGui style pushes plus
// hand-drawn ImDrawList primitives (MainWindow.Home.cs) where ImGui has no built-in equivalent -
// not a port of Aetherphone's Typography/Squircle kit, still too much surface area for this tool.
internal sealed partial class MainWindow : Window, IDisposable
{
    private static readonly Vector4 Accent = new(0.58f, 0.40f, 0.98f, 1f);
    private static readonly Vector4 AccentHover = new(0.66f, 0.50f, 1.00f, 1f);
    private static readonly Vector4 AccentActive = new(0.46f, 0.30f, 0.85f, 1f);
    private static readonly Vector4 FrameBg = new(0.13f, 0.12f, 0.19f, 1f);
    private static readonly Vector4 FrameBgHover = new(0.18f, 0.16f, 0.26f, 1f);
    private static readonly Vector4 Danger = new(0.95f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 Good = new(0.30f, 0.90f, 0.45f, 1f);
    private static readonly Vector4 WindowBg = new(0.045f, 0.04f, 0.075f, 1f);
    private static readonly Vector4 SidebarBg = new(0.035f, 0.03f, 0.06f, 1f);
    private static readonly Vector4 CardBg = new(0.10f, 0.09f, 0.16f, 1f);
    private static readonly Vector4 CardBgHover = new(0.14f, 0.12f, 0.21f, 1f);
    private static readonly Vector4 MutedText = new(0.60f, 0.60f, 0.68f, 1f);

    private enum HomePage
    {
        Home,
        Player,
        Screen,
        WatchAlong,
        Settings,
    }

    private readonly ScreenController screenController;
    private readonly VideoPlayer video;
    private readonly AetherStreamQueue queue;
    private readonly StreamClient stream;
    private readonly ThumbnailCache thumbnails = new();
    private readonly Action requestRename;
    private HomePage currentPage = HomePage.Home;
    private string joinHostNameInput = string.Empty;
    private string? joinError;

    // Not from StreamClient - see the comment where it's set (DrawWatchAlong's Join handler) for
    // why: HostId gets overwritten with the host's real UserId once StreamJoined arrives, so this
    // is the only place the friendly name a viewer actually typed survives for display.
    private string? joinedHostDisplayName;

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
            MinimumSize = new Vector2(720, 480),
            MaximumSize = new Vector2(2400, 1600),
        };

        stream.OnJoined += () => joinError = null;
        stream.OnDeclined += reason => joinError = string.IsNullOrEmpty(reason) ? "Could not find that host." : reason;
        stream.OnEnded += () => joinedHostDisplayName = null;
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
        DrawGlowBorder();

        // ChildBg/WindowPadding are captured by ImGui at the moment BeginChild runs, so these have
        // to be pushed before opening each child, not from inside DrawSidebar/DrawHome etc. -
        // pushing them after the fact would only affect further-nested children, not the one
        // already open.
        using (ImRaii.PushColor(ImGuiCol.ChildBg, SidebarBg))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(14, 18)))
        using (var sidebar = ImRaii.Child("##sidebar", new Vector2(200, 0), false))
        {
            if (sidebar)
            {
                DrawSidebar();
            }
        }

        ImGui.SameLine(0, 0);

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(24, 20)))
        using (var content = ImRaii.Child("##content", Vector2.Zero, false))
        {
            if (content)
            {
                DrawContent();
            }
        }
    }

    private void DrawContent()
    {
        switch (currentPage)
        {
            case HomePage.Home:
                DrawHome();
                break;
            case HomePage.Player:
                PageTitle("Player");
                DrawPlayback();
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                SectionHeader("Discover");
                DrawSearch();
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                SectionHeader("Queue");
                DrawQueue();
                break;
            case HomePage.Screen:
                PageTitle("Screen");
                DrawScreenControls();
                break;
            case HomePage.WatchAlong:
                PageTitle("Watch-along");
                DrawWatchAlong();
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                DrawReactions();
                break;
            case HomePage.Settings:
                PageTitle("Settings");
                DrawSettings();
                break;
        }
    }

    // Layered translucent rounded rects around the whole window, growing outward with falling
    // alpha - ImGui has no native blur, so this is what stands in for the mockup's soft neon glow
    // border. Drawn against the outer window's own draw list, before the sidebar/content children
    // are opened, so it sits behind everything else this frame paints.
    private void DrawGlowBorder()
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();

        for (var layer = 3; layer >= 1; layer--)
        {
            var inset = layer * 3f;
            var alpha = 0.05f + (4 - layer) * 0.05f;
            var color = ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, alpha));
            drawList.AddRect(min - new Vector2(inset, inset), max + new Vector2(inset, inset), color,
                14f + inset, ImDrawFlags.None, 2f);
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.9f)), 12f,
            ImDrawFlags.None, 1.5f);
    }

    private void DrawSidebar()
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(Accent, FontAwesomeIcon.Tv.ToIconString());
        }

        ImGui.SameLine();
        ImGui.Text("AlphaChannel");
        ImGui.TextColored(MutedText, "Cast. Watch. Together.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawNavItem(HomePage.Home, FontAwesomeIcon.Home, "Home");
        DrawNavItem(HomePage.Player, FontAwesomeIcon.Play, "Player");
        DrawNavItem(HomePage.Screen, FontAwesomeIcon.Desktop, "Screen");
        DrawNavItem(HomePage.WatchAlong, FontAwesomeIcon.Users, "Watch-along");
        DrawNavItem(HomePage.Settings, FontAwesomeIcon.Cog, "Settings");

        // Pinned near the bottom rather than wherever nav happens to end - the watching-count
        // card and the Ko-fi link are meant to always be visible, not scroll away with more nav
        // items later.
        var bottomBlockHeight = 96f;
        var targetY = ImGui.GetWindowHeight() - bottomBlockHeight;
        if (targetY > ImGui.GetCursorPosY())
        {
            ImGui.SetCursorPosY(targetY);
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawWatchingStat();
        ImGui.Spacing();
        DrawDonateLink();
    }

    // InvisibleButton, not Selectable+PushColor(Header) - the same proven click-region technique
    // MainWindow.Home.cs's DrawTile already uses successfully. Selectable's "selected" background
    // draws through ImGuiCol.Header, and something about combining that with the PushColor
    // condition parameter and a same-frame draw-list text overlay was making these rows register
    // visually but not actually respond to clicks in-game.
    private void DrawNavItem(HomePage page, FontAwesomeIcon icon, string label)
    {
        var active = currentPage == page;
        ImGui.PushID((int)page);

        var rowStart = ImGui.GetCursorScreenPos();
        var rowSize = new Vector2(ImGui.GetContentRegionAvail().X, 34);
        var drawList = ImGui.GetWindowDrawList();

        var clicked = ImGui.InvisibleButton("##navrow", rowSize);
        var hovered = ImGui.IsItemHovered();

        if (active)
        {
            drawList.AddRectFilled(rowStart, rowStart + rowSize, ImGui.GetColorU32(Accent), 8f);
        }
        else if (hovered)
        {
            drawList.AddRectFilled(rowStart, rowStart + rowSize, ImGui.GetColorU32(CardBgHover), 8f);
        }

        // AddText takes the font pointer directly - no need to push it onto the ImGui font stack
        // first, unlike every other icon usage in this file (IconButton etc.) that draws through
        // normal ImGui widgets instead of the draw list.
        var textColor = active ? Vector4.One : MutedText;
        drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), rowStart + new Vector2(12, 9),
            ImGui.GetColorU32(textColor), icon.ToIconString());
        drawList.AddText(rowStart + new Vector2(38, 9), ImGui.GetColorU32(textColor), label);

        ImGui.PopID();

        if (clicked)
        {
            currentPage = page;
        }
    }

    private void DrawWatchingStat()
    {
        var watching = stream.Mode == StreamMode.None ? 0 : stream.Roster.Length;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(stream.IsConnected ? Good : Danger, FontAwesomeIcon.Circle.ToIconString());
        }

        ImGui.SameLine();
        ImGui.Text(watching.ToString());
        ImGui.SameLine();
        ImGui.TextColored(MutedText, stream.Mode == StreamMode.None ? "not watching" : "watching");
    }

    private static readonly Vector4 KofiPink = new(0.98f, 0.29f, 0.55f, 1f);

    private void DrawDonateLink()
    {
        using var color = ImRaii.PushColor(ImGuiCol.Text, KofiPink);
        if (ImGui.SmallButton("Donate on Ko-fi"))
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

    // Every non-Home page starts with one of these instead of the old tab label - the sidebar nav
    // no longer shows a title once a page is selected, so the content area needs its own. Scaled
    // via SetWindowFontScale rather than a second font asset - Dalamud only guarantees the one
    // default proportional font plus IconFont are loaded.
    private static void PageTitle(string text)
    {
        ImGui.SetWindowFontScale(1.25f);
        ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1f);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    // Consistent accent-colored sub-headers within a page - plain white ImGui.Text for every
    // section read as everything being the same visual weight, which was a good chunk of why the
    // window felt "hammered on" rather than laid out.
    private static void SectionHeader(string text)
    {
        ImGui.TextColored(Accent, text);
        ImGui.Spacing();
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

    private void DrawWatchAlong()
    {
        switch (stream.Mode)
        {
            case StreamMode.Hosting:
                SectionHeader("You're hosting");
                if (ImGui.Button("Copy party invite"))
                {
                    ImGui.SetClipboardText(
                        $"Come watch with me! Right-click my character and choose \"Join Stream\" " +
                        $"(or open AlphaChannel and join \"{CurrentDisplayName}\").");
                }

                ImGui.Spacing();
                DrawRoster($"Watching ({stream.Roster.Length})", allowPromote: true);
                break;

            case StreamMode.Viewing:
                SectionHeader("You're watching along");
                if (ImGui.Button("Leave"))
                {
                    _ = stream.LeaveAsync();
                }

                ImGui.Spacing();
                DrawRoster($"Also watching ({stream.Roster.Length})", allowPromote: false);
                break;

            default:
                SectionHeader("Join a stream");
                ImGui.SetNextItemWidth(-100f);
                ImGui.InputTextWithHint("##hostName", "Host's name", ref joinHostNameInput, 32);
                ImGui.SameLine();
                if (ImGui.Button("Join"))
                {
                    DoJoin(joinHostNameInput);
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
        ImGui.TextDisabled(label);
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
        private const int ColorCount = 10;
        private const int StyleCount = 4;

        public ThemeScope()
        {
            ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBg);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, WindowBg);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, CardBg);
            ImGui.PushStyleColor(ImGuiCol.Button, Accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AccentHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, AccentActive);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, FrameBg);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, FrameBgHover);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, Accent);
            ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, AccentActive);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 12f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f);
        }

        public void Dispose()
        {
            ImGui.PopStyleVar(StyleCount);
            ImGui.PopStyleColor(ColorCount);
        }
    }
}
