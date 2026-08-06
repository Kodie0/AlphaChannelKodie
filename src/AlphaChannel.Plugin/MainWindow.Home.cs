using AlphaChannel.Plugin.Video;
using AlphaChannel.Contracts;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// The smart-TV-dashboard landing page: welcome header, a "Live Now" status card, a horizontal
// "Continue Watching" rail built from the real video queue, three shortcut tiles, and a Watch
// Together banner. All of it re-presents state that already exists elsewhere in the window
// (StreamClient, AetherStreamQueue, VideoPlayer) - nothing here owns new state of its own besides
// which sidebar page a click should jump to. Built with hand-drawn ImDrawList primitives
// (DrawGlowRect, DrawAvatarStack, DrawProgressBar, DrawTile) since ImGui has no native rounded
// card/avatar-stack/progress-bar widgets.
internal sealed partial class MainWindow
{
    private static readonly Vector4[] AvatarPalette =
    [
        new(0.55f, 0.35f, 0.95f, 1f),
        new(0.95f, 0.45f, 0.55f, 1f),
        new(0.35f, 0.65f, 0.95f, 1f),
        new(0.95f, 0.70f, 0.30f, 1f),
        new(0.40f, 0.85f, 0.65f, 1f),
    ];

    private void DrawHome()
    {
        DrawWelcomeHeader();
        ImGui.Spacing();
        ImGui.Spacing();
        DrawLiveNowCard();
        ImGui.Spacing();
        ImGui.Spacing();
        DrawContinueWatchingRail();
        ImGui.Spacing();
        ImGui.Spacing();
        DrawTileGrid();
        ImGui.Spacing();
        ImGui.Spacing();
        DrawWatchTogetherCta();
    }

    private void DrawWelcomeHeader()
    {
        ImGui.TextColored(MutedText, "Welcome back,");
        ImGui.SetWindowFontScale(1.5f);
        ImGui.TextUnformatted(CurrentDisplayName ?? "...");
        ImGui.SetWindowFontScale(1f);
        ImGui.SameLine(0, 10);
        if (IconButton(FontAwesomeIcon.Pen))
        {
            requestRename();
        }

        ImGui.SameLine(0, 16);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(stream.IsConnected ? Good : Danger, FontAwesomeIcon.Wifi.ToIconString());
        }

        ImGui.SameLine();
        ImGui.TextColored(MutedText, stream.IsConnected ? "Connected" : "Not connected");
    }

    // Build order for this whole redesign started here on purpose - it exercises every custom
    // primitive at once (rounded card, layered glow, avatar stack, quick-join) before the rest of
    // the layout gets built on top of the same techniques.
    private void DrawLiveNowCard()
    {
        var width = MathF.Min(380f, ImGui.GetContentRegionAvail().X);
        const float height = 156f;
        var isLive = stream.Mode != StreamMode.None;

        DrawGlowRect(width, height, 14f);

        using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBg))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(16, 14)))
        using (var card = ImRaii.Child("##liveNow", new Vector2(width, height), false, ImGuiWindowFlags.NoScrollbar))
        {
            if (!card)
            {
                return;
            }

            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(isLive ? Good : MutedText, FontAwesomeIcon.Circle.ToIconString());
            }

            ImGui.SameLine();
            ImGui.TextColored(isLive ? Good : MutedText, isLive ? "LIVE NOW" : "NOT WATCHING");

            var roomName = stream.Mode switch
            {
                StreamMode.Hosting => $"{CurrentDisplayName ?? "Your"}'s stream",
                StreamMode.Viewing => joinedHostDisplayName is { } host ? $"{host}'s stream" : "A friend's stream",
                _ => "No stream active",
            };

            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(Vector4.One, FontAwesomeIcon.Desktop.ToIconString());
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(roomName);

            ImGui.Spacing();

            if (isLive)
            {
                ImGui.TextColored(MutedText, $"{stream.Roster.Length} watching");
                ImGui.Spacing();
                DrawAvatarStack(stream.Roster, maxShown: 6);
                ImGui.Spacing();

                if (stream.Mode == StreamMode.Viewing)
                {
                    if (ImGui.Button("Leave"))
                    {
                        _ = stream.LeaveAsync();
                    }
                }
                else if (ImGui.Button("Manage roster"))
                {
                    currentPage = HomePage.WatchAlong;
                }
            }
            else
            {
                ImGui.TextColored(MutedText, "Join a friend's stream by name:");
                ImGui.Spacing();
                ImGui.SetNextItemWidth(160);
                ImGui.InputTextWithHint("##homeJoinHost", "Host's name", ref joinHostNameInput, 32);
                ImGui.SameLine();
                if (ImGui.Button("Join Channel"))
                {
                    DoJoin(joinHostNameInput);
                }

                if (joinError is { } error)
                {
                    ImGui.TextColored(Danger, error);
                }
            }
        }
    }

    private void DrawContinueWatchingRail()
    {
        SectionHeader("Continue Watching");

        var items = new List<VideoQueueEntry>();
        if (queue.Current is { } current)
        {
            items.Add(current);
        }

        items.AddRange(queue.Entries);

        if (items.Count == 0)
        {
            ImGui.TextColored(MutedText, "Nothing queued yet - add something from the Player page.");
            return;
        }

        const float cardWidth = 160f;
        const float cardHeight = 132f;

        using var rail = ImRaii.Child("##continueWatching", new Vector2(-1, cardHeight + 14), false,
            ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!rail)
        {
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (index > 0)
            {
                ImGui.SameLine();
            }

            DrawContinueWatchingCard(items[index], cardWidth, cardHeight, isCurrent: index == 0 && queue.Current is not null);
        }
    }

    private void DrawContinueWatchingCard(VideoQueueEntry entry, float width, float height, bool isCurrent)
    {
        using var bg = ImRaii.PushColor(ImGuiCol.ChildBg, isCurrent ? CardBgHover : CardBg);
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        using var card = ImRaii.Child($"##cw{entry.Id}", new Vector2(width, height), false, ImGuiWindowFlags.NoScrollbar);
        if (!card)
        {
            return;
        }

        const float thumbHeight = 70f;
        var thumbWidth = width - 16f;
        var thumbnail = thumbnails.Get(entry.ThumbnailUrl);
        var thumbTopLeft = ImGui.GetCursorScreenPos();

        ImGui.GetWindowDrawList().AddRectFilled(thumbTopLeft, thumbTopLeft + new Vector2(thumbWidth, thumbHeight),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)), 6f);

        if (thumbnail is not null)
        {
            ImGui.Image(thumbnail.Handle, new Vector2(thumbWidth, thumbHeight));
        }
        else
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var icon = FontAwesomeIcon.Play.ToIconString();
                var iconSize = ImGui.CalcTextSize(icon);
                ImGui.GetWindowDrawList().AddText(UiBuilder.IconFont, ImGui.GetFontSize(),
                    thumbTopLeft + new Vector2(thumbWidth, thumbHeight) / 2f - iconSize / 2f,
                    ImGui.GetColorU32(MutedText), icon);
            }

            ImGui.SetCursorScreenPos(thumbTopLeft + new Vector2(0, thumbHeight + 4));
        }

        ImGui.TextWrapped(entry.Title);
        ImGui.TextColored(MutedText, string.IsNullOrEmpty(entry.Source) ? "Video" : entry.Source);

        if (!isCurrent)
        {
            return;
        }

        var (position, duration, _) = video.GetProgress();
        if (duration > 0)
        {
            DrawProgressBar(width - 16f, 4f, Math.Clamp(position / duration, 0f, 1f));
        }
    }

    private void DrawTileGrid()
    {
        var avail = ImGui.GetContentRegionAvail().X;
        const float gap = 12f;
        var tileWidth = (avail - gap * 2) / 3f;
        const float tileHeight = 78f;

        if (DrawTile(tileWidth, tileHeight, FontAwesomeIcon.Play, new Vector4(0.95f, 0.25f, 0.25f, 1f),
                "YouTube", "Search and play any video"))
        {
            currentPage = HomePage.Player;
        }

        ImGui.SameLine(0, gap);
        if (DrawTile(tileWidth, tileHeight, FontAwesomeIcon.BroadcastTower, new Vector4(0.62f, 0.35f, 0.95f, 1f),
                "Twitch", "Check if a channel is live"))
        {
            currentPage = HomePage.Player;
        }

        ImGui.SameLine(0, gap);
        if (DrawTile(tileWidth, tileHeight, FontAwesomeIcon.ExpandArrowsAlt, Accent,
                "Screen", "Reposition or resize the screen"))
        {
            currentPage = HomePage.Screen;
        }
    }

    private static bool DrawTile(float width, float height, FontAwesomeIcon icon, Vector4 iconColor, string title,
        string subtitle)
    {
        using var bg = ImRaii.PushColor(ImGuiCol.ChildBg, CardBg);
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(14, 12));
        using var card = ImRaii.Child($"##tile{title}", new Vector2(width, height), false, ImGuiWindowFlags.NoScrollbar);
        if (!card)
        {
            return false;
        }

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(iconColor, icon.ToIconString());
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(title);
        ImGui.TextColored(MutedText, subtitle);
        ImGui.Spacing();

        // Right-aligned against however much content-region width is actually left in this card,
        // not the card's raw outer width - that would overshoot past the padding on narrow tiles.
        var chevronText = FontAwesomeIcon.ChevronRight.ToIconString();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var chevronWidth = ImGui.CalcTextSize(chevronText).X;
            var avail = ImGui.GetContentRegionAvail().X;
            if (avail > chevronWidth)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - chevronWidth);
            }

            ImGui.TextColored(MutedText, chevronText);
        }

        // Whole-card click target, drawn last (on top) so it catches clicks over the text/icon
        // too, not just whatever empty space is left below them.
        ImGui.SetCursorPos(Vector2.Zero);
        var clicked = ImGui.InvisibleButton($"##tileClick{title}", new Vector2(width, height));
        if (ImGui.IsItemHovered())
        {
            ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)), 10f);
        }

        return clicked;
    }

    private void DrawWatchTogetherCta()
    {
        var width = ImGui.GetContentRegionAvail().X;
        const float height = 64f;

        using var bg = ImRaii.PushColor(ImGuiCol.ChildBg, CardBg);
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(16, 12));
        using var card = ImRaii.Child("##watchTogetherCta", new Vector2(width, height), false, ImGuiWindowFlags.NoScrollbar);
        if (!card)
        {
            return;
        }

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(Accent, FontAwesomeIcon.Users.ToIconString());
        }

        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted("Watch Together");
        ImGui.TextColored(MutedText, stream.Mode == StreamMode.Hosting
            ? "You're hosting - invite friends from the Watch-along page."
            : "Play something to start hosting, or join a friend above.");
        ImGui.EndGroup();

        ImGui.SameLine(MathF.Max(width - 150f, 0f));
        if (stream.Mode == StreamMode.Hosting)
        {
            if (ImGui.Button("Manage roster", new Vector2(130, 0)))
            {
                currentPage = HomePage.WatchAlong;
            }
        }
        else if (ImGui.Button("Create Room", new Vector2(130, 0)))
        {
            currentPage = HomePage.Player;
        }
    }

    // Shared by the Home quick-join box and the dedicated Watch-along page's join field - keeps
    // the queue.Clear()/joinedHostDisplayName/JoinAsync sequence in one place instead of two
    // copies that could drift.
    private void DoJoin(string hostName)
    {
        if (hostName.Length == 0)
        {
            return;
        }

        queue.Clear();
        joinedHostDisplayName = hostName.Trim();
        _ = stream.JoinAsync(hostName.Trim());
    }

    // Layered translucent filled rounded rects behind a card, growing outward with falling alpha
    // - the same glow technique as the window border (MainWindow.cs's DrawGlowBorder) but filled
    // rather than outlined, so it reads as a soft bleed behind the card rather than a ring around
    // it. Must be called with the cursor at the exact position the card itself will open at.
    private static void DrawGlowRect(float width, float height, float radius)
    {
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();

        for (var layer = 3; layer >= 1; layer--)
        {
            var inset = layer * 4f;
            var alpha = 0.06f + (4 - layer) * 0.05f;
            var color = ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, alpha));
            drawList.AddRectFilled(min - new Vector2(inset, inset), max + new Vector2(inset, inset), color,
                radius + inset);
        }
    }

    // No avatar images exist anywhere in this plugin (ParticipantInfo is just UserId+DisplayName)
    // - these are real participants, just rendered as colored initials instead of photos.
    private static void DrawAvatarStack(ParticipantInfo[] participants, int maxShown)
    {
        if (participants.Length == 0)
        {
            ImGui.TextColored(MutedText, "Nobody yet.");
            return;
        }

        const float radius = 12f;
        const float overlap = 8f;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos() + new Vector2(radius, radius);
        var shown = Math.Min(participants.Length, maxShown);

        for (var index = 0; index < shown; index++)
        {
            var center = origin + new Vector2(index * (radius * 2 - overlap), 0);
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(AvatarPalette[index % AvatarPalette.Length]));
            var initial = participants[index].DisplayName.Length > 0
                ? participants[index].DisplayName[..1].ToUpperInvariant()
                : "?";
            var textSize = ImGui.CalcTextSize(initial);
            drawList.AddText(center - textSize / 2f, ImGui.GetColorU32(Vector4.One), initial);
        }

        var overflow = participants.Length - shown;
        var slots = shown;
        if (overflow > 0)
        {
            var center = origin + new Vector2(shown * (radius * 2 - overlap), 0);
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(CardBgHover));
            var text = $"+{overflow}";
            var textSize = ImGui.CalcTextSize(text);
            drawList.AddText(center - textSize / 2f, ImGui.GetColorU32(MutedText), text);
            slots++;
        }

        ImGui.Dummy(new Vector2(slots * (radius * 2 - overlap) + overlap, radius * 2));
    }

    private static void DrawProgressBar(float width, float height, float fraction)
    {
        var min = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, min + new Vector2(width, height), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f)),
            height / 2f);

        var fillWidth = width * Math.Clamp(fraction, 0f, 1f);
        if (fillWidth > 0.5f)
        {
            drawList.AddRectFilled(min, min + new Vector2(fillWidth, height), ImGui.GetColorU32(Accent), height / 2f);
        }

        ImGui.Dummy(new Vector2(width, height));
    }
}
