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
    private static Vector4 FrameBg => Colors.FrameBg;
    private static Vector4 FrameBgHover => Colors.FrameBgHover;
    private static Vector4 Danger => Colors.Danger;
    private static Vector4 Good => Colors.Good;
    private static Vector4 WindowBg => Colors.WindowBg;
    private static Vector4 SidebarBg => Colors.SidebarBg;
    private static Vector4 CardBg => Colors.CardBg;
    private static Vector4 CardBgHover => Colors.CardBgHover;
    private static Vector4 MutedText => Colors.MutedText;
    private static readonly Vector4 BorderSubtle = new(1f, 1f, 1f, 0.06f);

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

    // "Smaller" than the original fullscreen 1920x1080 lock - still roomy enough for the sidebar +
    // 3-column feature grid, but no longer takes over the whole display.
    private static readonly Vector2 WindowSize = new(1280, 800);
    // Compact capsule chrome while tucked away - wide enough for brand + expand + close.
    private static readonly Vector2 MinimizedSize = new(276, 40);
    private const int PositionPinFrames = 3;
    private bool windowMinimized;
    private Vector2? maximizedPosition;
    private Vector2? minimizedPosition;
    private Vector2? pendingPosition;
    private int pendingFrames;

    private HomePage currentPage = HomePage.Home;
    private string joinHostNameInput = string.Empty;
    private string? joinError;

    // Always-expanded labeled sidebar matching the mockup - clear icon+label rows, ~240px.
    private const float SidebarWidth = 240f;

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
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;
        SizeCondition = ImGuiCond.Always;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = MinimizedSize,
            MaximumSize = WindowSize,
        };

        stream.OnJoined += () => joinError = null;
        stream.OnDeclined += reason => joinError = string.IsNullOrEmpty(reason) ? "Could not find that host." : reason;
        stream.OnEnded += () => joinedHostDisplayName = null;

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
        Size = windowMinimized ? MinimizedSize : WindowSize;
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
            Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;
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
        Colors = ThemeCatalog.Get(Plugin.Cfg.UiTheme);
        using var theme = new ThemeScope();
        CaptureCurrentPosition();

        if (windowMinimized)
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero))
            {
                DrawMinimizedBar();
            }

            return;
        }

        DrawNamePrompt();
        DrawSignInModal();
        DrawProfilePopup();
        DrawGlowBorder();

        // Explicit pixel sizes - size.y=0 was collapsing the sidebar to content-height in this
        // Dalamud/ImGui build, which stacked Home under the brand and hid the nav rows. Master
        // used the same Child+SameLine pattern; locking both panes to avail.Y keeps the left
        // column as a real nav rail matching the mockup.
        var avail = ImGui.GetContentRegionAvail();
        var sidebarSize = new Vector2(SidebarWidth, avail.Y);
        var contentSize = new Vector2(MathF.Max(avail.X - SidebarWidth, 0f), avail.Y);

        using (ImRaii.PushColor(ImGuiCol.ChildBg, SidebarBg))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(14, 16)))
        using (var sidebar = ImRaii.Child("##sidebar", sidebarSize, false))
        {
            if (sidebar)
            {
                DrawSidebar();
            }
        }

        ImGui.SameLine(0, 0);

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(24, 20)))
        using (var content = ImRaii.Child("##content", contentSize, false))
        {
            if (content)
            {
                DrawWindowControlsStrip();
                DrawContent();
            }
        }
    }

    // No title bar means no native minimize/close chrome - these two replace it. Minimize collapses
    // the window down to MinimizedSize (see PreDraw) rather than just hiding content at full size,
    // so it actually reads as "tucked out of the way" instead of an empty box; close just does what
    // /achannel already does (IsOpen = false).
    private void DrawWindowControlsStrip()
    {
        const float stripHeight = 36f;
        const float buttonSize = 32f;
        const float gap = 8f;

        var stripStart = ImGui.GetCursorScreenPos();
        var fullWidth = ImGui.GetContentRegionAvail().X;

        var closeOrigin = stripStart + new Vector2(fullWidth - buttonSize, (stripHeight - buttonSize) / 2f);
        if (DrawWindowControlButton("##windowClose", closeOrigin, buttonSize, FontAwesomeIcon.Times, Danger))
        {
            CloseUi();
        }

        var minimizeOrigin = closeOrigin - new Vector2(buttonSize + gap, 0);
        if (DrawWindowControlButton("##windowMinimize", minimizeOrigin, buttonSize, FontAwesomeIcon.WindowMinimize, MutedText))
        {
            SetMinimized(true);
        }

        // Actually consumes the vertical space (rather than just visually occupying it), so
        // whatever child opens next genuinely starts below this strip instead of overlapping it.
        ImGui.SetCursorScreenPos(stripStart);
        ImGui.Dummy(new Vector2(fullWidth, stripHeight));
    }

    private static bool DrawWindowControlButton(string id, Vector2 origin, float size, FontAwesomeIcon icon, Vector4 hoverColor)
    {
        ImGui.SetCursorScreenPos(origin);
        ImGui.PushID(id);
        var clicked = ImGui.InvisibleButton("##ctl", new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(origin, origin + new Vector2(size, size),
            ImGui.GetColorU32(hovered ? CardBgHover : new Vector4(1f, 1f, 1f, 0.04f)), 10f);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var text = icon.ToIconString();
            var textSize = ImGui.CalcTextSize(text);
            drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize() * 0.85f,
                origin + new Vector2(size, size) / 2f - textSize * 0.425f,
                ImGui.GetColorU32(hovered ? hoverColor : MutedText), text);
        }

        ImGui.PopID();
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

        // Soft drop + capsule body + hairline accent edge.
        drawList.AddRectFilled(
            origin + new Vector2(0f, 1.5f),
            origin + size + new Vector2(0f, 1.5f),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.4f)),
            rounding);
        drawList.AddRectFilled(
            origin,
            origin + size,
            ImGui.GetColorU32(new Vector4(SidebarBg.X, SidebarBg.Y, SidebarBg.Z, 0.96f)),
            rounding);
        drawList.AddRect(
            origin,
            origin + size,
            ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.42f)),
            rounding,
            ImDrawFlags.None,
            1.15f);

        // Accent orb instead of the chunky TV tile.
        var orbCenter = origin + new Vector2(18f, size.Y * 0.5f);
        drawList.AddCircleFilled(
            orbCenter,
            8f,
            ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.22f)));
        drawList.AddCircleFilled(orbCenter, 4.5f, ImGui.GetColorU32(Accent));

        const string label = "AlphaChannel";
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
        switch (currentPage)
        {
            case HomePage.Home:
                DrawHome();
                break;
            case HomePage.Player:
                PageTitle("Player", "Put a video on your screen.");
                DrawPlayback();
                ImGui.Spacing();
                SectionHeader("Find a video");
                DrawSearch();
                ImGui.Spacing();
                SectionHeader("Queue");
                DrawQueue();
                break;
            case HomePage.Screen:
                PageTitle("Screen", "Move and resize the picture in the world.");
                DrawScreenControls();
                break;
            case HomePage.WatchAlong:
                PageTitle("Watch-along", "Watch the same thing with friends.");
                DrawWatchAlong();
                ImGui.Spacing();
                DrawReactions();
                break;
            case HomePage.Friends:
                PageTitle("Friends", "Your people — online status and invites.");
                DrawFriends();
                break;
            case HomePage.Messages:
                PageTitle("Alpha Chat", "Private messages between friends.");
                DrawMessages();
                break;
            case HomePage.Activity:
                PageTitle("Activity", "Recent things friends did.");
                DrawActivity();
                break;
            case HomePage.Apps:
                PageTitle("Apps", "Extra tools that live alongside the channel.");
                DrawApps();
                break;
            case HomePage.Tweeter:
                PageTitleBack("Tweeter", "Short posts from people you follow.", HomePage.Apps);
                DrawTweeter();
                break;
            case HomePage.PluginHub:
                PageTitle("Plugin Hub", "What plugins friends have enabled.");
                myPluginsDirty = true;
                DrawPluginHub();
                break;
            case HomePage.Venues:
                PageTitle("Venues", "Saved screen spots you can share.");
                DrawVenues();
                break;
            case HomePage.GoLive:
                PageTitle("Go Live", "Stream from OBS for friends to watch.");
                DrawGoLive();
                break;
            case HomePage.Settings:
                PageTitle("Settings", "Account, look, and server.");
                DrawSettings();
                break;
        }
    }

    // Magenta→cyan dual-tone ring matching the mockup's neon edge. ImGui has no blur, so layered
    // translucent rects stand in for the soft glow.
    private void DrawGlowBorder()
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();

        for (var layer = 3; layer >= 1; layer--)
        {
            var inset = layer * 3f;
            var alpha = 0.04f + (4 - layer) * 0.045f;
            var t = layer / 3f;
            var glow = new Vector4(
                MagentaGlow.X + (BlueGlow.X - MagentaGlow.X) * (1f - t),
                MagentaGlow.Y + (BlueGlow.Y - MagentaGlow.Y) * (1f - t),
                MagentaGlow.Z + (BlueGlow.Z - MagentaGlow.Z) * (1f - t),
                alpha);
            drawList.AddRect(min - new Vector2(inset, inset), max + new Vector2(inset, inset),
                ImGui.GetColorU32(glow), 14f + inset, ImDrawFlags.None, 2f);
        }

        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.85f)), 12f,
            ImDrawFlags.None, 1.5f);
        drawList.AddRect(min + new Vector2(1.5f, 1.5f), max - new Vector2(1.5f, 1.5f),
            ImGui.GetColorU32(new Vector4(BlueGlow.X, BlueGlow.Y, BlueGlow.Z, 0.35f)), 11f,
            ImDrawFlags.None, 1f);
    }

    private void DrawSidebar()
    {
        // Brand block matching mockup: TV mark + wordmark + tagline.
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(Accent, FontAwesomeIcon.Tv.ToIconString());
        }

        ImGui.SameLine();
        ImGui.TextUnformatted("Alpha Channel");
        ImGui.TextColored(MutedText, "Cast. Watch. Together.");

        ImGui.Spacing();
        ImGui.Dummy(new Vector2(0, 2));
        var hair = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(hair, hair + new Vector2(ImGui.GetContentRegionAvail().X, 1f),
            ImGui.GetColorU32(BorderSubtle));
        ImGui.Dummy(new Vector2(0, 10));

        if (CurrentSession is { } sidebarSession && friendsDirty && !friendsLoading)
        {
            RefreshFriends(sidebarSession.Token);
        }

        if (CurrentSession is { } pluginSyncSession && lastPluginSyncToken != pluginSyncSession.Token)
        {
            lastPluginSyncToken = pluginSyncSession.Token;
            SyncMyPlugins(pluginSyncSession.Token);
        }

        if (CurrentSession is { } dmSidebarSession && conversationsDirty && !conversationsLoading)
        {
            RefreshConversations(dmSidebarSession.Token);
        }

        if (CurrentSession is { } activitySidebarSession && activityUnreadDirty)
        {
            activityUnreadDirty = false;
            var token = activitySidebarSession.Token;
            _ = Task.Run(async () => activityUnreadCount = await activityClient.GetUnreadCountAsync(token));
        }

        DrawNavGroup("WATCH");
        DrawNavItem(HomePage.Home, FontAwesomeIcon.Home, "Home");
        DrawNavItem(HomePage.Player, FontAwesomeIcon.Play, "Player");
        DrawNavItem(HomePage.Screen, FontAwesomeIcon.Desktop, "Screen");
        DrawNavItem(HomePage.WatchAlong, FontAwesomeIcon.Users, "Watch-along");

        DrawNavGroup("SOCIAL");
        DrawNavItem(HomePage.Friends, FontAwesomeIcon.UserFriends, "Friends", friendRequests.Incoming.Length);
        DrawNavItem(HomePage.Messages, FontAwesomeIcon.Comment, "Alpha Chat", conversations.Sum(c => c.UnreadCount) + unreadWhisperKeys.Count);
        DrawNavItem(HomePage.Activity, FontAwesomeIcon.Bell, "Activity", activityUnreadCount);

        DrawNavGroup("MORE");
        var appsActive = currentPage is HomePage.Apps or HomePage.Tweeter;
        DrawNavItem(HomePage.Apps, FontAwesomeIcon.ThLarge, "Apps", forceActive: appsActive);
        DrawNavItem(HomePage.PluginHub, FontAwesomeIcon.PuzzlePiece, "Plugin Hub");
        DrawNavItem(HomePage.Venues, FontAwesomeIcon.MapMarkerAlt, "Venues");
        DrawNavItem(HomePage.GoLive, FontAwesomeIcon.SatelliteDish, "Go Live");
        DrawNavItem(HomePage.Settings, FontAwesomeIcon.Cog, "Settings");

        var bottomBlockHeight = 100f;
        var targetY = ImGui.GetWindowHeight() - bottomBlockHeight;
        if (targetY > ImGui.GetCursorPosY())
        {
            ImGui.SetCursorPosY(targetY);
        }

        var footHair = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(footHair, footHair + new Vector2(ImGui.GetContentRegionAvail().X, 1f),
            ImGui.GetColorU32(BorderSubtle));
        ImGui.Dummy(new Vector2(0, 10));
        DrawWatchingStat();
        ImGui.Spacing();
        DrawDonateLink();
        ImGui.Spacing();
        DrawVersionFooter();
    }

    private static void DrawNavGroup(string label)
    {
        ImGui.Spacing();
        ImGui.TextColored(MutedText, label);
        ImGui.Dummy(new Vector2(0, 2));
    }

    // Proven click-region technique from origin/master: InvisibleButton + draw-list icon/label.
    // Active row = rounded purple pill matching the mockup Home highlight.
    // forceActive keeps Apps highlighted while you're inside Tweeter.
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
            if (page == HomePage.Messages)
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
            ImGui.TextColored(MutedText, "watching now");
        }
        else
        {
            ImGui.TextUnformatted($"{onlineFriends}");
            ImGui.SameLine();
            ImGui.TextColored(MutedText, onlineFriends == 1 ? "friend online" : "friends online");
        }

        // Global connected clients, pinned to the right of the friends/watching line.
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

    // Ko-fi brand pink on a solid fill so the label stays readable against every UiTheme.
    private static readonly Vector4 KofiPink = new(0.98f, 0.29f, 0.55f, 1f);
    private static readonly Vector4 KofiPinkHover = new(1f, 0.40f, 0.62f, 1f);
    private static readonly Vector4 KofiPinkActive = new(0.85f, 0.20f, 0.45f, 1f);

    private void DrawDonateLink()
    {
        using (ImRaii.PushColor(ImGuiCol.Button, KofiPink)
                   .Push(ImGuiCol.ButtonHovered, KofiPinkHover)
                   .Push(ImGuiCol.ButtonActive, KofiPinkActive)
                   .Push(ImGuiCol.Text, Vector4.One))
        {
            if (ImGui.Button("Donate on Ko-fi", new Vector2(-1, 30)))
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
    }

    private static string? cachedVersionText;

    private static void DrawVersionFooter()
    {
        cachedVersionText ??= typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "dev";
        ImGui.TextColored(MutedText, $"AlphaChannel v{cachedVersionText}");
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

        ImGui.Dummy(new Vector2(0, 6));
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddRectFilled(origin, origin + new Vector2(width, 1f),
            ImGui.GetColorU32(BorderSubtle));
        ImGui.Dummy(new Vector2(width, 14f));
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
        using (var card = ImRaii.Child(id, new Vector2(-1, 1), false, ImGuiWindowFlags.AlwaysAutoResize))
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
        using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(CardBg.X, CardBg.Y, CardBg.Z, MathF.Min(CardBg.W + 0.08f, 1f))))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(20, 18)))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 14f))
        using (var stage = ImRaii.Child(id, new Vector2(-1, 1), false, ImGuiWindowFlags.AlwaysAutoResize))
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

    private void DrawWatchAlong()
    {
        if (CurrentSession is null)
        {
            DrawPlainEmpty("Host or join a synced room after you sign in.", "Open Settings",
                () => currentPage = HomePage.Settings);
            return;
        }

        switch (stream.Mode)
        {
            case StreamMode.Hosting:
                DrawStage("##watchHosting", () =>
                {
                    ImGui.TextColored(Good, "HOSTING");
                    ImGui.SetWindowFontScale(1.2f);
                    ImGui.TextUnformatted($"{CurrentDisplayName ?? "Your"} room");
                    ImGui.SetWindowFontScale(1f);
                    ImGui.TextColored(MutedText, $"{stream.Roster.Length} watching · playback stays locked to you");
                    ImGui.Spacing();

                    var isPrivate = stream.IsPrivate;
                    if (ImGui.Checkbox("Private (hide from friends' presence)", ref isPrivate))
                    {
                        stream.IsPrivate = isPrivate;
                    }

                    if (ImGui.Button("Copy party invite", new Vector2(-1, 32)))
                    {
                        ImGui.SetClipboardText(
                            $"Come watch with me! Right-click my character and choose \"Join Stream\" " +
                            $"(or open AlphaChannel and join \"{CurrentDisplayName}\").");
                    }
                });
                DrawRoster($"Watching ({stream.Roster.Length})", allowPromote: true);
                break;

            case StreamMode.Viewing:
                DrawStage("##watchViewing", () =>
                {
                    ImGui.TextColored(Good, "IN ROOM");
                    ImGui.SetWindowFontScale(1.2f);
                    ImGui.TextUnformatted(joinedHostDisplayName is { } host ? $"{host}'s room" : "A friend's room");
                    ImGui.SetWindowFontScale(1f);
                    ImGui.TextColored(MutedText, $"{stream.Roster.Length} also here");
                    ImGui.Spacing();
                    if (ImGui.Button("Leave room", new Vector2(-1, 32)))
                    {
                        _ = stream.LeaveAsync();
                    }
                });
                DrawRoster($"Also here ({stream.Roster.Length})", allowPromote: false);
                break;

            default:
                DrawStage("##watchIdle", () =>
                {
                    var hasMedia = queue.Current is not null;
                    ImGui.TextColored(MutedText, "HOW IT WORKS");
                    ImGui.Spacing();
                    ImGui.BulletText("You play a video in Player (hosting starts automatically).");
                    ImGui.BulletText("Friends join with your AlphaChannel name.");
                    ImGui.BulletText("Everyone stays in sync — pause, seek, and the screen.");
                    ImGui.Spacing();

                    if (hasMedia)
                    {
                        ImGui.TextColored(Good, "You're playing something — friends can join you now.");
                        ImGui.Spacing();
                        if (ImGui.Button("Copy invite text", new Vector2(-1, 34)))
                        {
                            ImGui.SetClipboardText(
                                $"Come watch with me! Right-click my character and choose \"Join Stream\" " +
                                $"(or open AlphaChannel and join \"{CurrentDisplayName}\").");
                        }
                    }
                    else
                    {
                        using (ImRaii.PushColor(ImGuiCol.Button, Gold)
                                   .Push(ImGuiCol.ButtonHovered, GoldHover)
                                   .Push(ImGuiCol.ButtonActive, Gold)
                                   .Push(ImGuiCol.Text, new Vector4(0.12f, 0.09f, 0.02f, 1f)))
                        {
                            if (ImGui.Button("1. Open Player and pick a video", new Vector2(-1, 34)))
                            {
                                currentPage = HomePage.Player;
                            }
                        }
                    }

                    ImGui.Spacing();
                    ImGui.TextUnformatted(hasMedia ? "Or join someone else" : "2. Or join a friend");
                    ImGui.SetNextItemWidth(-100f);
                    ImGui.InputTextWithHint("##hostName", "Their name", ref joinHostNameInput, 32);
                    ImGui.SameLine();
                    if (ImGui.Button("Join", new Vector2(88, 0)))
                    {
                        DoJoin(joinHostNameInput);
                    }

                    if (joinError is { } error)
                    {
                        ImGui.TextColored(Danger, error);
                    }
                });
                break;
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
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 12f);
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 12f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 16f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 16f);
        }

        public void Dispose()
        {
            ImGui.PopStyleVar(StyleCount);
            ImGui.PopStyleColor(ColorCount);
        }
    }
}
