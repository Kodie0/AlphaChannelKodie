using System.Diagnostics;
using AlphaChannel.Plugin.Auth;
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
    // Active palette for this frame - set at the top of Draw() from Cfg.UiTheme so every partial
    // (and ThemeScope) reads the same colors without threading a palette through each helper.
    // Mockup default is Purple (deep navy + violet accent + magenta/cyan glow).
    private static ThemeColors Colors = ThemeCatalog.Get(UiTheme.Purple);

    private static Vector4 Accent => Colors.Accent;
    private static Vector4 AccentHover => Colors.AccentHover;
    private static Vector4 AccentActive => Colors.AccentActive;
    private static Vector4 BlueGlow => Colors.BlueGlow;
    private static Vector4 MagentaGlow => Colors.MagentaGlow;
    private static Vector4 Gold => Colors.Gold;
    private static Vector4 GoldHover => Colors.GoldHover;
    private static Vector4 FrameBg => FadeForCustomBg(Colors.FrameBg, 0.30f);
    private static Vector4 FrameBgHover => FadeForCustomBg(Colors.FrameBgHover, 0.38f);
    private static Vector4 Danger => Colors.Danger;
    private static Vector4 Good => Colors.Good;
    // Custom wallpaper mode: panels are ~75% see-through so the image reads through.
    private static Vector4 WindowBg => FadeForCustomBg(Colors.WindowBg, 0.22f);
    private static Vector4 SidebarBg => FadeForCustomBg(Colors.SidebarBg, 0.28f);
    private static Vector4 CardBg => FadeForCustomBg(Colors.CardBg, 0.25f);
    private static Vector4 CardBgHover => FadeForCustomBg(Colors.CardBgHover, 0.35f);
    private static Vector4 MutedText => Colors.MutedText;
    private static readonly Vector4 BorderSubtle = new(1f, 1f, 1f, 0.06f);

    // Set each frame in Draw() when a custom background texture is actually showing.
    private static bool customBackgroundActive;

    private static Vector4 FadeForCustomBg(Vector4 color, float alpha) =>
        customBackgroundActive ? new Vector4(color.X, color.Y, color.Z, alpha) : color;

    private static Vector4 Hex(int rgb) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        1f);

    private enum HomePage
    {
        Home,
        Player,
        Screen,
        WatchAlong,
        Friends,
        Messages,
        Activity,
        Tweeter,
        Apps,
        PluginHub,
        Venues,
        GoLive,
        Settings,
    }

    private readonly ScreenController screenController;
    private readonly VideoPlayer video;
    private readonly AetherStreamQueue queue;
    private readonly StreamClient stream;
    private readonly ThumbnailCache thumbnails = new();
    private readonly Action requestRename;
    private readonly SignInFlow signInFlow;
    private readonly AuthClient authClient;
    private readonly FriendsClient friendsClient;
    private readonly ActivityClient activityClient;
    private readonly DmClient dmClient;
    private readonly ReportClient reportClient;
    private readonly TweeterClient tweeterClient;
    private readonly PluginHubClient pluginHubClient;
    private readonly VenuesClient venuesClient;
    private readonly LiveClient liveClient;
    private readonly TwitchClient twitchClient;
    private readonly Crypto.KeyVault keyVault;
    private readonly Whispers.WhisperMirror whisperMirror;

    // Called whenever sign-in/link/sign-out changes what CharacterSession belongs to the currently-
    // played character - the callback (Plugin.cs) is what actually writes Cfg.CharacterSessions and
    // saves, same split as requestRename above (MainWindow owns the UI, Plugin.cs owns persistence).
    private readonly Action<CharacterSession?> onSessionChanged;

    // Room for left nav + center + optional right rail + bottom media bar (mockup chrome).
    private static readonly Vector2 WindowSize = new(1400, 860);
    // Compact capsule chrome while tucked away - wide enough for brand + expand + close.
    private static readonly Vector2 MinimizedSize = new(276, 40);
    // Wider capsule when "Watching First Last" is showing (viewer-only join).
    private static readonly Vector2 MinimizedViewerSize = new(340, 40);
    private const int PositionPinFrames = 3;
    private bool windowMinimized;
    // True after /achannel watch or context-menu Join Stream: stay minimized; screen still
    // draws via ScreenPainter + /rt sync. Requires AlphaChannel on both sides — not Lightless.
    private bool viewerMode;
    // Set when NearbyAutoWatch started the join — range leave only applies to these sessions.
    private bool proximityJoined;
    private Vector2? maximizedPosition;
    private Vector2? minimizedPosition;
    private Vector2? pendingPosition;
    private int pendingFrames;

    private HomePage currentPage = HomePage.Home;
    private string joinHostNameInput = string.Empty;
    private string? joinError;

    private const float SidebarWidth = 200f;
    private const float RightRailWidth = 260f;
    private const float BottomBarHeight = 104f;

    // Borderless Child windows ignore WindowPadding in this ImGui build unless AlwaysUseWindowPadding
    // is set. NoScrollbar keeps chrome panes clean (navbar / cards / rails).
    private const ImGuiWindowFlags PaddedChild = ImGuiWindowFlags.AlwaysUseWindowPadding
        | ImGuiWindowFlags.NoScrollbar;

    private const ImGuiWindowFlags NavPaneFlags = PaddedChild | ImGuiWindowFlags.NoScrollWithMouse;

    // Not from StreamClient - see the comment where it's set (DoJoin) for why: HostId gets
    // overwritten with the host's real UserId once StreamJoined arrives, so this is the only
    // place the friendly name a viewer actually typed survives for display.
    private string? joinedHostDisplayName;

    private bool namePromptPending;
    private bool namePromptActive;
    private string namePromptInput = string.Empty;
    private Action<string>? onNameConfirmed;

    internal bool IsNamePromptActive => namePromptActive;

    // Updated every tick from Plugin.cs (cheap dictionary lookup there) - shown here instead of the
    // raw UserId so players never need to read each other an opaque GUID to join a stream.
    internal string? CurrentDisplayName { get; set; }

    // Also updated every tick from Plugin.cs, same reasoning as CurrentDisplayName - the signed-in
    // account (if any) for whichever character is currently being played, and the live character
    // name/world to sign in with if there isn't one yet.
    internal CharacterSession? CurrentSession { get; set; }
    internal string? CurrentCharacterName { get; set; }
    internal string? CurrentWorldName { get; set; }
    internal bool CurrentIsLalafell { get; set; }

    internal MainWindow(ScreenController screenController, VideoPlayer video, AetherStreamQueue queue,
        StreamClient stream, Action requestRename, AuthClient authClient, SignInFlow signInFlow,
        FriendsClient friendsClient, ActivityClient activityClient, DmClient dmClient, ReportClient reportClient,
        TweeterClient tweeterClient, PluginHubClient pluginHubClient, VenuesClient venuesClient, LiveClient liveClient,
        TwitchClient twitchClient, Crypto.KeyVault keyVault, Whispers.WhisperMirror whisperMirror,
        Action<CharacterSession?> onSessionChanged)
        : base("AlphaChannel###AlphaChannelMain")
    {
        this.screenController = screenController;
        this.video = video;
        this.queue = queue;
        this.stream = stream;
        this.requestRename = requestRename;
        this.authClient = authClient;
        this.signInFlow = signInFlow;
        this.friendsClient = friendsClient;
        this.activityClient = activityClient;
        this.dmClient = dmClient;
        this.reportClient = reportClient;
        this.tweeterClient = tweeterClient;
        this.pluginHubClient = pluginHubClient;
        this.venuesClient = venuesClient;
        this.liveClient = liveClient;
        this.twitchClient = twitchClient;
        this.keyVault = keyVault;
        this.whisperMirror = whisperMirror;
        this.onSessionChanged = onSessionChanged;

        whisperMirror.OnWhisperMessage += ApplyIncomingWhisper;

        stream.OnFriendRequestReceived += _ => friendsDirty = true;
        stream.OnFriendAccepted += _ => friendsDirty = true;
        stream.OnFriendRemoved += _ => friendsDirty = true;
        stream.OnPresenceUpdate += ApplyPresenceUpdate;
        stream.OnOnlineCount += count => usersOnlineCount = count;
        stream.OnActivityNew += _ => { activityDirty = true; activityUnreadDirty = true; };
        stream.OnDmMessage += ApplyIncomingDm;

        // Fixed size, no title bar/resize handles - reads as a real console/TV dashboard rather
        // than a floating dev-tool window. Actual size is set every frame in PreDraw (below), since
        // it toggles between WindowSize and MinimizedSize - SizeConstraints just has to be loose
        // enough to allow both (NoResize already blocks the player from dragging it anywhere else).
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
                | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        SizeCondition = ImGuiCond.Always;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = MinimizedSize,
            MaximumSize = WindowSize,
        };

        stream.OnJoined += () => joinError = null;
        stream.OnDeclined += reason =>
        {
            joinError = string.IsNullOrEmpty(reason) ? "Could not find that host." : reason;
            if (proximityJoined)
            {
                proximityJoined = false;
                joinedHostDisplayName = null;
                viewerMode = false;
            }
        };
        stream.OnEnded += () =>
        {
            joinedHostDisplayName = null;
            viewerMode = false;
            proximityJoined = false;
        };

        maximizedPosition = Plugin.Cfg.MaximizedPosition;
        minimizedPosition = Plugin.Cfg.MinimizedPosition;
    }

    // /achannel and Dalamud's OpenMainUi both land here so a second activation always closes,
    // including when the window is sitting in its minimized capsule.
    internal void OpenUi()
    {
        SetMinimized(false);
        RequestPosition(maximizedPosition);
        IsOpen = true;
    }

    // Full-window join (Home / Party "Join" field). Prefer OpenViewerAndJoin for quick watch.
    internal void OpenPlayerAndJoin(string hostDisplayName)
    {
        proximityJoined = false;
        viewerMode = false;
        currentPage = HomePage.Player;
        playerSourceTab = 0;
        OpenUi();
        DoJoin(hostDisplayName);
    }

    // Viewer-only: AlphaChannel required. Capsule UI + ScreenPainter; sync is still /rt URL/position
    // (ApplyRemoteState) — no Penumbra texture pipe, so Lightless alone cannot show the screen.
    // fromProximity: NearbyAutoWatch owns leave-on-range; manual /watch keeps the session until Leave.
    internal void OpenViewerAndJoin(string hostDisplayName, bool fromProximity = false)
    {
        proximityJoined = fromProximity;
        viewerMode = true;
        currentPage = HomePage.Player;
        playerSourceTab = 0;
        SetMinimized(true);
        RequestPosition(minimizedPosition);
        IsOpen = true;
        DoJoin(hostDisplayName);
    }

    // Silent proximity probe — join without opening chrome until ShowProximityViewer (URL confirmed).
    // Does not clear the local queue (DoJoin does); wiping playback was resetting hosts' screens.
    internal void BeginProximityJoin(string hostDisplayName)
    {
        if (hostDisplayName.Length == 0)
        {
            return;
        }

        proximityJoined = true;
        viewerMode = true;
        // Do not touch playerSourceTab / queue — probes must not yank the YouTube search box.
        joinedHostDisplayName = hostDisplayName.Trim();
        _ = stream.JoinAsync(hostDisplayName.Trim());
    }

    // True when this client is driving its own screen/queue (hosting or solo play) — auto-watch
    // must not join/clear over the top of that.
    internal bool HasLocalPlayback =>
        stream.Mode == StreamMode.Hosting
        || queue.Current is not null
        || screenController.Engine.IsActive;

    internal void ShowProximityViewer()
    {
        if (!proximityJoined)
        {
            return;
        }

        SetMinimized(true);
        RequestPosition(minimizedPosition);
        IsOpen = true;
    }

    internal void LeaveStream()
    {
        viewerMode = false;
        proximityJoined = false;
        joinedHostDisplayName = null;
        _ = stream.LeaveAsync();
    }

    internal string? JoinedHostDisplayName => joinedHostDisplayName;
    internal bool ProximityJoined => proximityJoined;

    internal void ClearProximityJoin() => proximityJoined = false;

    internal void CloseUi()
    {
        PersistPositions();
        windowMinimized = false;
        IsOpen = false;
    }

    // Writes remembered placements when they changed — called on close and plugin unload.
    internal void PersistPositions()
    {
        if (Plugin.Cfg.MaximizedPosition == maximizedPosition &&
            Plugin.Cfg.MinimizedPosition == minimizedPosition)
        {
            return;
        }

        Plugin.Cfg.MaximizedPosition = maximizedPosition;
        Plugin.Cfg.MinimizedPosition = minimizedPosition;
        Plugin.Cfg.Save();
    }

    public override void OnClose() => PersistPositions();

    private void SetMinimized(bool minimized)
    {
        if (windowMinimized == minimized)
        {
            return;
        }

        windowMinimized = minimized;
        RequestPosition(minimized ? minimizedPosition : maximizedPosition);
    }

    private void RequestPosition(Vector2? target)
    {
        if (target is not { } position)
        {
            return;
        }

        pendingPosition = position;
        pendingFrames = PositionPinFrames;
    }

    private void CaptureCurrentPosition()
    {
        var pos = ImGui.GetWindowPos();
        if (windowMinimized)
        {
            minimizedPosition = pos;
        }
        else
        {
            maximizedPosition = pos;
        }
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

    // Window.Size is only read once Begin() runs, which happens before Draw() - setting it from
    // inside Draw() would lag a frame behind a minimize/restore click, so it's set here instead
    // (Dalamud calls PreDraw before Begin every frame). Flags also flip here so the minimized
    // capsule can draw its own chrome (NoBackground) without NoMove blocking drag.
    public override void PreDraw()
    {
        Size = windowMinimized
            ? (viewerMode ? MinimizedViewerSize : MinimizedSize)
            : WindowSize;
        if (windowMinimized)
        {
            Flags = ImGuiWindowFlags.NoTitleBar
                    | ImGuiWindowFlags.NoResize
                    | ImGuiWindowFlags.NoCollapse
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoBackground;
        }
        else
        {
            // Outer window never scrolls — Settings scrolls inside ##content instead.
            Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
                    | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        }

        // Pin for a few frames after minimize/restore/reopen so the size swap doesn't leave the
        // window at the wrong corner; then clear Position so the player can drag freely again.
        if (pendingFrames > 0 && pendingPosition is { } target)
        {
            Position = target;
            PositionCondition = ImGuiCond.Always;
            pendingFrames--;
        }
        else
        {
            Position = null;
        }
    }

    public override void Draw()
    {
        Colors = ThemeCatalog.Get(Plugin.Cfg.UiTheme, Plugin.Cfg.UiBackground);
        EnsureCustomBackgroundLoaded();
        customBackgroundActive = Plugin.Cfg.UiBackground == UiBackground.Custom && customBackground is not null;
        using var theme = new ThemeScope();
        CaptureCurrentPosition();

        if (windowMinimized)
        {
            customBackgroundActive = false;
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero))
            {
                DrawMinimizedBar();
            }

            return;
        }

        DrawCustomBackgroundLayer();
        DrawNamePrompt();
        DrawSignInModal();
        DrawProfilePopup();
        DrawGlowBorder();

        var avail = ImGui.GetContentRegionAvail();
        var topHeight = MathF.Max(avail.Y - BottomBarHeight, 120f);
        var showRightRail = currentPage == HomePage.Home;
        var rightWidth = showRightRail ? RightRailWidth : 0f;
        var centerWidth = MathF.Max(avail.X - SidebarWidth - rightWidth, 0f);

        using (ImRaii.PushColor(ImGuiCol.ChildBg, SidebarBg))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(14, 16)))
        using (var sidebar = ImRaii.Child("##sidebar", new Vector2(SidebarWidth, topHeight), false, NavPaneFlags))
        {
            if (sidebar)
            {
                DrawSidebar();
            }
        }

        ImGui.SameLine(0, 0);

        // Settings keeps a scrollbar so the long preferences sheet stays usable; every other page
        // hides chrome scrollbars (navbar / home / player / etc.).
        var contentFlags = currentPage == HomePage.Settings
            ? ImGuiWindowFlags.AlwaysUseWindowPadding
            : PaddedChild | ImGuiWindowFlags.NoScrollWithMouse;

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(24, 18)))
        using (var content = ImRaii.Child("##content", new Vector2(centerWidth, topHeight), false, contentFlags))
        {
            if (content)
            {
                DrawContent();
            }
        }

        if (showRightRail)
        {
            ImGui.SameLine(0, 0);
            using (ImRaii.PushColor(ImGuiCol.ChildBg, SidebarBg))
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(16, 18)))
            using (var rail = ImRaii.Child("##rightRail", new Vector2(RightRailWidth, topHeight), false, NavPaneFlags))
            {
                if (rail)
                {
                    DrawHomeRightRail();
                }
            }
        }

        using (ImRaii.PushColor(ImGuiCol.ChildBg, SidebarBg))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(24, 14)))
        using (var bottom = ImRaii.Child("##bottomBar", new Vector2(avail.X, BottomBarHeight), false,
                   NavPaneFlags))
        {
            if (bottom)
            {
                DrawBottomBar();
            }
        }

        // Overlay last — its own ImGui window so clicks aren't eaten by the content/rail children.
        DrawWindowControlsStrip();
    }

    // No title bar means no native minimize/close chrome - these two replace it. Minimize collapses
    // the window down to MinimizedSize (see PreDraw) rather than just hiding content at full size,
    // so it actually reads as "tucked out of the way" instead of an empty box; close just does what
    // /achannel already does (IsOpen = false).
    //
    // Floated in a tiny sibling window just above the neon glow so (1) it sits outside the main
    // chrome and (2) hit-testing works — parent InvisibleButtons under child panes never receive
    // clicks even when painted with the foreground draw list.
    // Chrome is outline-only (no solid fill) so it doesn't read as a double-stacked pill.
    private void DrawWindowControlsStrip()
    {
        const float buttonSize = 26f;
        const float gap = 8f;
        const float pad = 2f;
        const float glowClearance = 20f;

        var mainPos = ImGui.GetWindowPos();
        var mainSize = ImGui.GetWindowSize();
        var stripW = pad * 2 + buttonSize * 2 + gap;
        var stripH = pad * 2 + buttonSize;
        var stripPos = new Vector2(
            mainPos.X + mainSize.X - stripW - 10f,
            mainPos.Y - stripH - glowClearance);

        ImGui.SetNextWindowPos(stripPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(stripW, stripH), ImGuiCond.Always);

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad))
                   .Push(ImGuiStyleVar.ItemSpacing, new Vector2(gap, 0f)))
        {
            const ImGuiWindowFlags flags =
                ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.NoResize
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoCollapse
                | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoNav
                | ImGuiWindowFlags.NoDocking
                | ImGuiWindowFlags.NoBackground;

            if (!ImGui.Begin("##alphaWindowControls", flags))
            {
                ImGui.End();
                return;
            }

            if (DrawWindowControlButton("##ctlMin", FontAwesomeIcon.WindowMinimize, buttonSize))
            {
                SetMinimized(true);
            }

            ImGui.SameLine(0, gap);
            if (DrawWindowControlButton("##ctlClose", FontAwesomeIcon.Times, buttonSize))
            {
                CloseUi();
            }

            ImGui.End();
        }
    }

    // Invisible hit target + theme-glow outline (Accent / MagentaGlow from the picked theme).
    private static bool DrawWindowControlButton(string id, FontAwesomeIcon icon, float size)
    {
        var origin = ImGui.GetCursorScreenPos();
        ImGui.PushID(id);
        var clicked = ImGui.InvisibleButton("##hit", new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        ImGui.PopID();

        var drawList = ImGui.GetWindowDrawList();
        // Idle: soft MagentaGlow (same family as the outer halo). Hover: Accent rim strength.
        var outline = hovered
            ? new Vector4(Accent.X, Accent.Y, Accent.Z, 0.95f)
            : new Vector4(MagentaGlow.X, MagentaGlow.Y, MagentaGlow.Z, 0.55f);
        drawList.AddRect(origin, origin + new Vector2(size, size), ImGui.GetColorU32(outline), 8f,
            ImDrawFlags.None, hovered ? 1.6f : 1.25f);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var glyph = icon.ToIconString();
            var textSize = ImGui.CalcTextSize(glyph);
            var glyphColor = hovered
                ? AccentHover
                : new Vector4(Accent.X, Accent.Y, Accent.Z, 0.70f);
            drawList.AddText(origin + new Vector2(size, size) / 2f - textSize / 2f,
                ImGui.GetColorU32(glyphColor), glyph);
        }

        return clicked;
    }

    // Collapsed capsule - brand mark + expand control. Drag the bar to reposition; expand restores
    // via the chevron or a double-click (single-click-anywhere restore was blocking window moves).
    private void DrawMinimizedBar()
    {
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        var rounding = size.Y * 0.5f;

        // Body only on the window list (clipped). Glow/rim go through DrawGlowBorder on the
        // foreground list so the halo isn't cut off into a hard red stroke.
        drawList.AddRectFilled(
            origin,
            origin + size,
            ImGui.GetColorU32(new Vector4(SidebarBg.X, SidebarBg.Y, SidebarBg.Z, 0.96f)),
            rounding);

        DrawGlowBorder(rounding);

        // Accent orb instead of the chunky TV tile.
        var orbCenter = origin + new Vector2(18f, size.Y * 0.5f);
        drawList.AddCircleFilled(
            orbCenter,
            8f,
            ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.22f)));
        drawList.AddCircleFilled(orbCenter, 4.5f, ImGui.GetColorU32(Accent));

        var label = viewerMode && joinedHostDisplayName is { Length: > 0 } host
            ? $"Watching {host}"
            : "AlphaChannel";
        if (label.Length > 28)
        {
            label = label[..25] + "…";
        }

        var labelSize = ImGui.CalcTextSize(label);
        drawList.AddText(
            origin + new Vector2(32f, (size.Y - labelSize.Y) * 0.5f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.92f)),
            label);

        const float chipSize = 24f;
        const float chipGap = 4f;
        var closeOrigin = origin + new Vector2(size.X - 8f - chipSize, (size.Y - chipSize) * 0.5f);
        var restoreOrigin = closeOrigin - new Vector2(chipSize + chipGap, 0f);
        var restoreClicked = DrawMinimizedRoundButton(
            "##windowRestore", restoreOrigin, chipSize, FontAwesomeIcon.ChevronUp, Accent);
        var closeClicked = DrawMinimizedRoundButton(
            "##windowCloseMini", closeOrigin, chipSize, FontAwesomeIcon.Times, Danger);

        // Drag region covers everything except the expand/close chips so NoTitleBar still moves.
        var dragWidth = MathF.Max(size.X - (chipSize * 2f) - chipGap - 12f, 0f);
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##minimizedDrag", new Vector2(dragWidth, size.Y));
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
        }

        if (closeClicked)
        {
            CloseUi();
        }
        else if (restoreClicked || (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)))
        {
            SetMinimized(false);
        }
    }

    private bool DrawMinimizedRoundButton(
        string id, Vector2 origin, float size, FontAwesomeIcon icon, Vector4 hoverColor)
    {
        ImGui.SetCursorScreenPos(origin);
        ImGui.PushID(id);
        var clicked = ImGui.InvisibleButton("##ctl", new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();

        var fill = hovered
            ? new Vector4(hoverColor.X, hoverColor.Y, hoverColor.Z, 0.28f)
            : new Vector4(1f, 1f, 1f, 0.06f);
        drawList.AddCircleFilled(origin + new Vector2(size, size) * 0.5f, size * 0.5f, ImGui.GetColorU32(fill));

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var text = icon.ToIconString();
            var textSize = ImGui.CalcTextSize(text);
            drawList.AddText(
                UiBuilder.IconFont,
                ImGui.GetFontSize() * 0.78f,
                origin + new Vector2(size, size) * 0.5f - textSize * 0.39f,
                ImGui.GetColorU32(hovered ? hoverColor : new Vector4(1f, 1f, 1f, 0.78f)),
                text);
        }

        ImGui.PopID();
        return clicked;
    }

    private void DrawContent()
    {
        // Still parked from launch cut — bounce home if somehow selected.
        if (currentPage is HomePage.WatchAlong or HomePage.Activity
            or HomePage.Venues or HomePage.GoLive)
        {
            currentPage = HomePage.Home;
        }

        switch (currentPage)
        {
            case HomePage.Home:
                DrawHome();
                break;
            case HomePage.Player:
                PageTitle("Player", "Play something, then watch together.");
                DrawPlayerPage();
                break;
            case HomePage.Screen:
                PageTitle("Screen", "Place the picture in the world.");
                DrawScreenControls();
                break;
            case HomePage.Friends:
                PageTitle("Friends", "People you can invite and join.");
                DrawFriends();
                break;
            case HomePage.Apps:
                PageTitle("Apps", "Extra tools that live alongside the channel.");
                DrawApps();
                break;
            case HomePage.Messages:
                PageTitleBack("Alpha Chat", "Private messages between friends.", HomePage.Apps);
                DrawMessages();
                break;
            case HomePage.PluginHub:
                PageTitleBack("Plugin Hub", "What plugins friends have enabled.", HomePage.Apps);
                myPluginsDirty = true;
                DrawPluginHub();
                break;
            case HomePage.Tweeter:
                PageTitleBack("Tweeter", "Short posts from people you follow.", HomePage.Apps);
                DrawTweeter();
                break;
            case HomePage.Settings:
                PageTitle("Settings", "Account, look, and whispers.");
                DrawSettings();
                break;
        }
    }

    // Soft neon halo around the window. Must use the foreground draw list — the window draw list
    // clips to the window rect, which cuts off any outward glow and leaves a hard rim.
    // roundingOverride: capsule uses half-height; full window uses the default 16.
    private void DrawGlowBorder(float rounding = 16f)
    {
        var drawList = ImGui.GetForegroundDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();

        // Outer falloff (largest → smallest). Wider + softer so it reads as glow, not a stroke.
        for (var layer = 7; layer >= 1; layer--)
        {
            var outset = layer * 2.25f;
            var alpha = 0.028f + (8 - layer) * 0.018f;
            var t = layer / 7f;
            var glow = new Vector4(
                MagentaGlow.X + (BlueGlow.X - MagentaGlow.X) * (1f - t),
                MagentaGlow.Y + (BlueGlow.Y - MagentaGlow.Y) * (1f - t),
                MagentaGlow.Z + (BlueGlow.Z - MagentaGlow.Z) * (1f - t),
                alpha);
            drawList.AddRect(
                min - new Vector2(outset, outset),
                max + new Vector2(outset, outset),
                ImGui.GetColorU32(glow),
                rounding + outset * 0.55f,
                ImDrawFlags.None,
                2.2f + layer * 0.35f);
        }

        // Accent rim sitting on the window edge.
        drawList.AddRect(min + new Vector2(0.5f, 0.5f), max - new Vector2(0.5f, 0.5f),
            ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.95f)), rounding,
            ImDrawFlags.None, 1.6f);

        // Cool inner hairline for depth (skip on tiny capsules — reads as a double stroke).
        if (rounding < max.Y * 0.45f)
        {
            drawList.AddRect(min + new Vector2(2.5f, 2.5f), max - new Vector2(2.5f, 2.5f),
                ImGui.GetColorU32(new Vector4(BlueGlow.X, BlueGlow.Y, BlueGlow.Z, 0.28f)),
                MathF.Max(4f, rounding - 2f), ImDrawFlags.None, 1f);
        }
    }

    private void DrawSidebar()
    {
        // Compact brand: accent mark + wordmark (tagline lives on Home).
        var brandOrigin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        const float mark = 28f;
        drawList.AddRectFilled(brandOrigin, brandOrigin + new Vector2(mark, mark),
            ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.22f)), 8f);
        drawList.AddText(brandOrigin + new Vector2(8, 5), ImGui.GetColorU32(Accent), "A");
        ImGui.Dummy(new Vector2(mark, mark));
        ImGui.SameLine(0, 10);
        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(0, 4));
        ImGui.TextUnformatted("ALPHA CHANNEL");
        ImGui.EndGroup();

        ImGui.Dummy(new Vector2(0, 14));

        if (CurrentSession is { } sidebarSession && friendsDirty && !friendsLoading)
        {
            RefreshFriends(sidebarSession.Token);
        }

        DrawNavItem(HomePage.Home, FontAwesomeIcon.Home, "Home");
        DrawNavItem(HomePage.Player, FontAwesomeIcon.Play, "Player");
        DrawNavItem(HomePage.Screen, FontAwesomeIcon.Desktop, "Screen");
        DrawNavItem(HomePage.Friends, FontAwesomeIcon.UserFriends, "Friends", friendRequests.Incoming.Length);
        var appsActive = currentPage is HomePage.Apps or HomePage.Messages or HomePage.PluginHub
            or HomePage.Tweeter;
        var appsBadge = conversations.Sum(c => c.UnreadCount) + unreadWhisperKeys.Count;
        DrawNavItem(HomePage.Apps, FontAwesomeIcon.ThLarge, "Apps", appsBadge, forceActive: appsActive);
        DrawNavItem(HomePage.Settings, FontAwesomeIcon.Cog, "Settings");

        if (CurrentSession is { } dmSidebarSession
            && currentPage is HomePage.Messages or HomePage.Apps
            && conversationsDirty && !conversationsLoading)
        {
            RefreshConversations(dmSidebarSession.Token);
        }

        // Footer pinned above the content-region bottom. Theme ItemSpacing was eating the
        // version line (Dummy gap + spacing pushed it under the clip), so zero it here and
        // keep a little explicit slack under the version.
        // Rotate the ask ↔ "Donate on Ko-fi" every 30s; height fits the taller copy so the
        // footer doesn't jump when the label flips.
        var donateLabel = DonateLabels[((int)(ImGui.GetTime() / DonateRotateSeconds)) % DonateLabels.Length];
        var footerWidth = MathF.Max(40f, ImGui.GetContentRegionAvail().X);
        var wrapWidth = MathF.Max(40f, footerWidth - 16f);
        var donateH = 40f;
        foreach (var candidate in DonateLabels)
        {
            donateH = MathF.Max(donateH, ImGui.CalcTextSize(candidate, false, wrapWidth).Y + 14f);
        }

        const float footerGap = 8f;
        const float bottomSlack = 10f;
        var versionH = ImGui.GetTextLineHeightWithSpacing();
        var footerH = donateH + footerGap + versionH + bottomSlack;

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            var footerStartY = ImGui.GetWindowContentRegionMax().Y - footerH;
            if (footerStartY > ImGui.GetCursorPosY())
            {
                ImGui.SetCursorPosY(footerStartY);
            }

            DrawDonateLink(donateLabel, donateH);
            ImGui.Dummy(new Vector2(0, footerGap));
            DrawVersionFooter();
        }
    }

    private static void DrawNavGroup(string label)
    {
        ImGui.Spacing();
        ImGui.TextColored(MutedText, label);
        ImGui.Dummy(new Vector2(0, 2));
    }

    // forceActive keeps Apps highlighted while you're inside an app (Chat / Hub / Tweeter).
    private void DrawNavItem(HomePage page, FontAwesomeIcon icon, string label, int badgeCount = 0,
        bool forceActive = false)
    {
        var active = forceActive || currentPage == page;
        ImGui.PushID((int)page);

        var rowStart = ImGui.GetCursorScreenPos();
        var rowSize = new Vector2(ImGui.GetContentRegionAvail().X, 34f);
        var drawList = ImGui.GetWindowDrawList();

        var clicked = ImGui.InvisibleButton("##navrow", rowSize);
        var hovered = ImGui.IsItemHovered();

        if (active)
        {
            drawList.AddRectFilled(rowStart, rowStart + rowSize, ImGui.GetColorU32(Accent), 10f);
        }
        else if (hovered)
        {
            drawList.AddRectFilled(rowStart, rowStart + rowSize, ImGui.GetColorU32(CardBgHover), 10f);
        }

        var textColor = active ? Vector4.One : MutedText;
        drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize(), rowStart + new Vector2(12, 9),
            ImGui.GetColorU32(textColor), icon.ToIconString());
        drawList.AddText(rowStart + new Vector2(38, 9), ImGui.GetColorU32(textColor), label);

        if (badgeCount > 0)
        {
            var badgeText = badgeCount > 9 ? "9+" : badgeCount.ToString();
            var badgeCenter = rowStart + new Vector2(rowSize.X - 14, rowSize.Y / 2);
            drawList.AddCircleFilled(badgeCenter, 8f, ImGui.GetColorU32(active ? Vector4.One : Danger));
            var textSize = ImGui.CalcTextSize(badgeText);
            drawList.AddText(badgeCenter - textSize / 2,
                ImGui.GetColorU32(active ? Accent : Vector4.One), badgeText);
        }

        ImGui.PopID();

        if (clicked)
        {
            currentPage = page;
            if (page == HomePage.Apps)
            {
                conversationsDirty = true;
            }
        }
    }

    private void DrawWatchingStat()
    {
        var onlineFriends = friends.Count(f => f.Online);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(onlineFriends > 0 || stream.IsConnected ? Good : MutedText,
                FontAwesomeIcon.Circle.ToIconString());
        }

        ImGui.SameLine();
        if (stream.Mode != StreamMode.None)
        {
            ImGui.TextUnformatted($"{stream.Roster.Length}");
            ImGui.SameLine();
            ImGui.TextColored(MutedText, "in party");
        }
        else
        {
            ImGui.TextUnformatted($"{onlineFriends}");
            ImGui.SameLine();
            ImGui.TextColored(MutedText, onlineFriends == 1 ? "friend online" : "friends online");
        }

        if (usersOnlineCount > 0 || stream.IsConnected)
        {
            var label = usersOnlineCount == 1 ? "1 user" : $"{usersOnlineCount} users";
            var labelWidth = ImGui.CalcTextSize(label).X;
            var right = ImGui.GetWindowContentRegionMax().X;
            ImGui.SameLine();
            ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX() + 8f, right - labelWidth));
            ImGui.TextColored(MutedText, label);
        }
    }

    // Ko-fi brand pink — left-nav footer above the version. Alternates ask ↔ CTA every 30s.
    private static readonly Vector4 KofiPink = new(0.98f, 0.29f, 0.55f, 1f);
    private static readonly Vector4 KofiPinkHover = new(1f, 0.40f, 0.62f, 1f);
    private static readonly Vector4 KofiPinkActive = new(0.85f, 0.20f, 0.45f, 1f);
    private static readonly string[] DonateLabels =
    [
        "Hey, like what you see?\nConsider supporting us",
        "Donate on Ko-fi",
    ];
    private const double DonateRotateSeconds = 30;

    private void DrawDonateLink(string label, float height)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, height);

        using (ImRaii.PushColor(ImGuiCol.Button, KofiPink)
                   .Push(ImGuiCol.ButtonHovered, KofiPinkHover)
                   .Push(ImGuiCol.ButtonActive, KofiPinkActive)
                   .Push(ImGuiCol.Text, Vector4.One))
        {
            if (ImGui.Button("##kofiDonate", size))
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

        // Centered wrapped copy on top of the solid pink hit target (Button labels don't wrap).
        var wrapWidth = MathF.Max(40f, width - 16f);
        var textSize = ImGui.CalcTextSize(label, false, wrapWidth);
        var textPos = origin + new Vector2((width - textSize.X) * 0.5f, (height - textSize.Y) * 0.5f);
        ImGui.GetWindowDrawList().AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize(),
            textPos,
            ImGui.GetColorU32(Vector4.One),
            label,
            wrapWidth);
    }

    private static string? cachedVersionText;

    private static void DrawVersionFooter()
    {
        cachedVersionText ??= typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "dev";
        var text = $"AlphaChannel v{cachedVersionText}";
        var textWidth = ImGui.CalcTextSize(text).X;
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (avail - textWidth) * 0.5f));
        ImGui.TextColored(MutedText, text);
    }

    // Every non-Home page starts with back + title + a one-line purpose so each Channel reads as
    // its own place, not a clone of every other tab with a different header string.
    private void PageTitle(string text, string purpose) => PageTitleBack(text, purpose, HomePage.Home);

    private void PageTitleBack(string text, string purpose, HomePage backPage)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.12f))
                   .Push(ImGuiCol.ButtonHovered, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.22f))
                   .Push(ImGuiCol.ButtonActive, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.30f))
                   .Push(ImGuiCol.Text, AccentHover))
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button($"{FontAwesomeIcon.ArrowLeft.ToIconString()}##backPage", new Vector2(34, 30)))
                {
                    currentPage = backPage;
                }
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(backPage == HomePage.Home ? "Back to Home" : "Back to Apps");
        }

        ImGui.SameLine(0, 12);
        ImGui.BeginGroup();
        ImGui.SetWindowFontScale(1.35f);
        ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1f);
        ImGui.TextColored(MutedText, purpose);
        ImGui.EndGroup();

        ImGui.Dummy(new Vector2(0, 8));
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddRectFilled(origin, origin + new Vector2(width, 1f),
            ImGui.GetColorU32(BorderSubtle));
        ImGui.Dummy(new Vector2(width, 18f));
    }

    // Consistent accent-colored sub-headers within a page — same weight on every Channel.
    private static void SectionHeader(string text)
    {
        ImGui.TextColored(Accent, text);
        ImGui.Dummy(new Vector2(0, 4));
    }

    // Soft panel for content that needs grouping. Height must be >0 — Child size.y=0 means
    // "fill remaining host height" in ImGui, which swallowed the Player search section below.
    private static void DrawCard(string id, Action draw)
    {
        using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBg))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(16, 14)))
        using (var card = ImRaii.Child(id, new Vector2(-1, 1), false,
                   PaddedChild | ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (card)
            {
                draw();
            }
        }

        ImGui.Spacing();
    }

    // Tall accent-edged panel for the "main thing" on media/live pages (now playing, room status).
    private static void DrawStage(string id, Action draw)
    {
        var origin = ImGui.GetCursorScreenPos();
        using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBgHover))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(20, 18)))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 14f))
        using (var stage = ImRaii.Child(id, new Vector2(-1, 1), false,
                   PaddedChild | ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (stage)
            {
                draw();
            }
        }

        var end = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddRectFilled(origin, new Vector2(origin.X + 3f, end.Y),
            ImGui.GetColorU32(Accent), 2f);
        ImGui.Spacing();
        ImGui.Spacing();
    }

    // Activity feed row: left rail + text, no card chrome.
    private static void DrawTimelineRow(string id, string text, bool unread = false)
    {
        ImGui.PushID(id);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var wrapWidth = MathF.Max(ImGui.GetContentRegionAvail().X - 28f, 40f);
        var textHeight = ImGui.CalcTextSize(text, false, wrapWidth).Y;
        var height = MathF.Max(textHeight + 12f, 28f);

        drawList.AddLine(origin + new Vector2(7, 0), origin + new Vector2(7, height),
            ImGui.GetColorU32(BorderSubtle), 1.5f);
        drawList.AddCircleFilled(origin + new Vector2(7, 12), unread ? 4.5f : 3.5f,
            ImGui.GetColorU32(unread ? Accent : MutedText));

        ImGui.SetCursorScreenPos(origin + new Vector2(22, 4));
        ImGui.PushTextWrapPos(origin.X + 22f + wrapWidth);
        ImGui.TextWrapped(text);
        ImGui.PopTextWrapPos();

        var afterY = ImGui.GetCursorScreenPos().Y;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, MathF.Max(afterY, origin.Y + height) + 2f));
        ImGui.PopID();
    }

    private static void DrawPlainEmpty(string message, string? buttonLabel = null, Action? onClick = null)
    {
        ImGui.Dummy(new Vector2(0, 8));
        ImGui.TextColored(MutedText, message);
        if (buttonLabel is not null && onClick is not null)
        {
            ImGui.Spacing();
            if (ImGui.Button(buttonLabel, new Vector2(160, 30)))
            {
                onClick();
            }
        }

        ImGui.Dummy(new Vector2(0, 8));
    }

    private static void DrawEmptyCard(string id, string message, string? buttonLabel = null, Action? onClick = null)
    {
        DrawCard(id, () =>
        {
            ImGui.TextColored(MutedText, message);
            if (buttonLabel is null || onClick is null)
            {
                return;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton(buttonLabel))
            {
                onClick();
            }
        });
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

    private void DrawRoster(string label, bool allowPromote)
    {
        ImGui.TextUnformatted(label);
        ImGui.Spacing();
        if (stream.Roster.Length == 0)
        {
            DrawPlainEmpty("Waiting for people…");
            return;
        }

        DrawAvatarStack(stream.Roster, maxShown: 12);
        ImGui.Spacing();
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

        ImGui.Spacing();
    }

    public void Dispose()
    {
        PersistPositions();
        thumbnails.Dispose();
        homeHero?.Dispose();
        homeHero = null;
        customBackground?.Dispose();
        customBackground = null;
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
        private const int StyleCount = 7;

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
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 12f);
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 12f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 16f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 16f);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(12, 10));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, 8));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(8, 6));
        }

        public void Dispose()
        {
            ImGui.PopStyleVar(StyleCount);
            ImGui.PopStyleColor(ColorCount);
        }
    }
}
