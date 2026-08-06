using AlphaChannel.Plugin.Video;
using AlphaChannel.Contracts;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Launchpad Home matching the mockup: welcome row, Live Now hero, Continue Watching,
// YouTube / Twitch / Watch Together tiles, Watch Together banner. Real destinations only.
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

    // Set by Home tiles so Player opens on YouTube or Twitch search.
    private string? pendingSearchTab;

    private void DrawHome()
    {
        DrawHomeIdentityRow();
        ImGui.Spacing();
        ImGui.Spacing();
        DrawNowHero();
        ImGui.Spacing();
        ImGui.Spacing();
        DrawContinueWatchingRail();
        ImGui.Spacing();
        ImGui.Spacing();
        DrawTileGrid();
        ImGui.Spacing();
        ImGui.Spacing();
        DrawWatchTogetherBanner();
        ImGui.Spacing();
        ImGui.Spacing();
        DrawFriendsOnlineStrip();
    }

    // Avatar + welcome + rename + Add Friend on the left, connection/clock on the right.
    private void DrawHomeIdentityRow()
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();
        var avatarOrigin = ImGui.GetCursorScreenPos();
        const float avatarSize = 48f;

        drawList.AddRectFilled(avatarOrigin, avatarOrigin + new Vector2(avatarSize, avatarSize),
            ImGui.GetColorU32(Accent), 14f);

        var initial = CurrentDisplayName is { Length: > 0 } name ? name[..1].ToUpperInvariant() : "?";
        var initialSize = ImGui.CalcTextSize(initial);
        drawList.AddText(avatarOrigin + new Vector2(avatarSize, avatarSize) / 2f - initialSize / 2f,
            ImGui.GetColorU32(Vector4.One), initial);

        var dotCenter = avatarOrigin + new Vector2(avatarSize - 3f, avatarSize - 3f);
        drawList.AddCircleFilled(dotCenter, 7f, ImGui.GetColorU32(WindowBg));
        drawList.AddCircleFilled(dotCenter, 5f, ImGui.GetColorU32(stream.IsConnected ? Good : MutedText));

        ImGui.Dummy(new Vector2(avatarSize, avatarSize));
        ImGui.SameLine(0, 14);

        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(1, 2));
        ImGui.TextColored(MutedText, "Welcome back");
        ImGui.SetWindowFontScale(1.25f);
        ImGui.TextUnformatted(CurrentDisplayName ?? "...");
        ImGui.SetWindowFontScale(1f);
        ImGui.EndGroup();

        ImGui.SameLine(0, 8);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 18f);
        if (IconButton(FontAwesomeIcon.Pen))
        {
            requestRename();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Change your display name");
        }

        ImGui.SameLine(0, 16);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10f);
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.12f))
                   .Push(ImGuiCol.ButtonHovered, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.22f))
                   .Push(ImGuiCol.ButtonActive, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.30f))
                   .Push(ImGuiCol.Text, AccentHover)
                   .Push(ImGuiCol.Border, Accent))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 1f))
        {
            if (ImGui.Button("+ Add Friend", new Vector2(110, 28)))
            {
                currentPage = HomePage.Friends;
            }
        }

        var clockText = DateTime.Now.ToString("h:mm tt");
        var clockWidth = ImGui.CalcTextSize(clockText).X + 36f;
        ImGui.SameLine(MathF.Max(avail - clockWidth, 280f));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 12f);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(stream.IsConnected ? Good : MutedText, FontAwesomeIcon.Wifi.ToIconString());
        }

        ImGui.SameLine(0, 8);
        ImGui.TextUnformatted(clockText);
    }

    private float nowHeroHeight = 200f;

    // One hero for "what's happening" - live session controls when active, host+join when idle.
    // Height is measured each frame so Join / Play aren't clipped (fixed 168px was cutting them off).
    private void DrawNowHero()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var isLive = stream.Mode != StreamMode.None;

        DrawGlowRect(width, nowHeroHeight, 18f);

        using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBg))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(22, 18)))
        using (var card = ImRaii.Child("##nowHero", new Vector2(width, nowHeroHeight), false))
        {
            if (!card)
            {
                return;
            }

            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(isLive ? Good : Accent, FontAwesomeIcon.Circle.ToIconString());
            }

            ImGui.SameLine();
            ImGui.TextColored(isLive ? Good : MutedText, isLive ? "LIVE NOW" : "READY TO WATCH");

            if (isLive)
            {
                DrawNowHeroLive();
            }
            else
            {
                DrawNowHeroIdle();
            }

            // Grow if content needs more room; shrink slowly so the glow stays matched.
            var needed = ImGui.GetCursorPosY() + 8f;
            if (needed > nowHeroHeight + 1f || needed < nowHeroHeight - 24f)
            {
                nowHeroHeight = MathF.Max(needed, 160f);
            }
        }
    }

    private void DrawNowHeroLive()
    {
        var roomName = stream.Mode switch
        {
            StreamMode.Hosting => $"{CurrentDisplayName ?? "Your"}'s stream",
            StreamMode.Viewing => joinedHostDisplayName is { } host ? $"{host}'s stream" : "A friend's stream",
            _ => "Stream active",
        };

        ImGui.SetWindowFontScale(1.35f);
        ImGui.TextUnformatted(roomName);
        ImGui.SetWindowFontScale(1f);
        ImGui.TextColored(MutedText, $"{stream.Roster.Length} watching");
        ImGui.Spacing();
        DrawAvatarStack(stream.Roster, maxShown: 8);
        ImGui.Spacing();

        if (stream.Mode == StreamMode.Viewing)
        {
            if (ImGui.Button("Leave", new Vector2(120, 34)))
            {
                _ = stream.LeaveAsync();
            }

            ImGui.SameLine();
            if (ImGui.Button("Open Watch-along", new Vector2(160, 34)))
            {
                currentPage = HomePage.WatchAlong;
            }
        }
        else
        {
            if (ImGui.Button("Manage roster", new Vector2(140, 34)))
            {
                currentPage = HomePage.WatchAlong;
            }

            ImGui.SameLine();
            if (ImGui.Button("Open Player", new Vector2(120, 34)))
            {
                currentPage = HomePage.Player;
            }
        }

        if (joinError is { } error)
        {
            ImGui.TextColored(Danger, error);
        }
    }

    private void DrawNowHeroIdle()
    {
        var hasMedia = queue.Current is not null;

        ImGui.SetWindowFontScale(1.35f);
        ImGui.TextUnformatted(hasMedia ? "Share what you're watching" : "Watch something together");
        ImGui.SetWindowFontScale(1f);
        ImGui.TextColored(MutedText, hasMedia
            ? "You're already playing — invite friends into the room."
            : "Play a video first, then friends can join you.");
        ImGui.Spacing();

        var avail = ImGui.GetContentRegionAvail().X;
        const float gap = 16f;
        var half = (avail - gap) / 2f;

        ImGui.BeginGroup();
        using (ImRaii.PushColor(ImGuiCol.Button, Gold)
                   .Push(ImGuiCol.ButtonHovered, GoldHover)
                   .Push(ImGuiCol.ButtonActive, Gold)
                   .Push(ImGuiCol.Text, new Vector4(0.12f, 0.09f, 0.02f, 1f)))
        {
            if (ImGui.Button(hasMedia ? "Invite friends" : "Play a video", new Vector2(half, 36)))
            {
                currentPage = hasMedia ? HomePage.WatchAlong : HomePage.Player;
            }
        }

        ImGui.TextColored(MutedText, hasMedia ? "Open watch-along" : "Opens the Player");
        ImGui.EndGroup();

        ImGui.SameLine(0, gap);

        ImGui.BeginGroup();
        ImGui.SetNextItemWidth(half - 100f);
        ImGui.InputTextWithHint("##homeJoinHost", "Friend's name", ref joinHostNameInput, 32);
        ImGui.SameLine();
        if (ImGui.Button("Join", new Vector2(88, 0)))
        {
            DoJoin(joinHostNameInput);
        }

        ImGui.TextColored(MutedText, "Join their room");
        ImGui.EndGroup();

        if (joinError is { } error)
        {
            ImGui.TextColored(Danger, error);
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
            using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBg))
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(18, 14)))
            using (var empty = ImRaii.Child("##continueEmpty", new Vector2(-1, 56), false, ImGuiWindowFlags.NoScrollbar))
            {
                if (!empty)
                {
                    return;
                }

                ImGui.TextColored(MutedText, "Nothing playing yet.");
                ImGui.SameLine();
                if (ImGui.SmallButton("Open Player"))
                {
                    currentPage = HomePage.Player;
                }
            }

            return;
        }

        const float cardWidth = 220f;
        const float cardHeight = 190f;

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
                ImGui.SameLine(0, 16);
            }

            DrawWatchCard(items[index], cardWidth, cardHeight, isCurrent: index == 0 && queue.Current is not null);
        }
    }

    private void DrawWatchCard(VideoQueueEntry entry, float width, float height, bool isCurrent)
    {
        using var bg = ImRaii.PushColor(ImGuiCol.ChildBg, isCurrent ? CardBgHover : CardBg);
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var card = ImRaii.Child($"##cw{entry.Id}", new Vector2(width, height), false, ImGuiWindowFlags.NoScrollbar);
        if (!card)
        {
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        const float posterHeight = 104f;
        var posterMax = origin + new Vector2(width, posterHeight);

        drawList.AddRectFilled(origin, posterMax, ImGui.GetColorU32(PosterColor(entry.Source)), 18f,
            ImDrawFlags.RoundCornersTop);

        var thumbnail = thumbnails.Get(entry.ThumbnailUrl);
        if (thumbnail is not null)
        {
            drawList.AddImageRounded(thumbnail.Handle, origin, posterMax, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One), 18f, ImDrawFlags.RoundCornersTop);
        }

        drawList.AddRectFilledMultiColor(origin + new Vector2(0, posterHeight * 0.45f), posterMax,
            0, 0, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)));

        DrawSourcePill(origin + new Vector2(10, posterHeight - 30f), entry.Source);

        ImGui.SetCursorScreenPos(origin + new Vector2(14, posterHeight + 12));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - 28);
        ImGui.TextUnformatted(entry.Title);
        ImGui.PopTextWrapPos();

        if (!isCurrent)
        {
            ImGui.SetCursorScreenPos(origin + new Vector2(14, height - 24));
            ImGui.TextColored(MutedText, string.IsNullOrEmpty(entry.Source) ? "Video" : entry.Source);
        }
        else
        {
            var (position, duration, _) = video.GetProgress();
            var fraction = duration > 0 ? Math.Clamp(position / duration, 0f, 1f) : 0f;

            ImGui.SetCursorScreenPos(origin + new Vector2(14, height - 34));
            DrawProgressBar(width - 28f, 5f, fraction);

            ImGui.SetCursorScreenPos(origin + new Vector2(14, height - 22));
            ImGui.TextColored(MutedText, duration > 0 ? $"{(int)(fraction * 100)}% watched" : "Playing");
        }

        ImGui.SetCursorPos(Vector2.Zero);
        if (ImGui.InvisibleButton($"##cwClick{entry.Id}", new Vector2(width, height)))
        {
            if (!isCurrent)
            {
                queue.PlayNow(entry);
            }

            currentPage = HomePage.Player;
        }
    }

    private static void DrawSourcePill(Vector2 topLeft, string source)
    {
        var drawList = ImGui.GetWindowDrawList();
        var label = string.IsNullOrEmpty(source) ? "Video" : source;
        var textSize = ImGui.CalcTextSize(label);
        var width = textSize.X + 26f;
        const float height = 22f;

        drawList.AddRectFilled(topLeft, topLeft + new Vector2(width, height),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.5f)), height / 2f);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var icon = FontAwesomeIcon.Play.ToIconString();
            drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize() * 0.8f, topLeft + new Vector2(8, 5),
                ImGui.GetColorU32(Vector4.One), icon);
        }

        drawList.AddText(topLeft + new Vector2(22, 4), ImGui.GetColorU32(Vector4.One), label);
    }

    private void DrawTileGrid()
    {
        var avail = ImGui.GetContentRegionAvail().X;
        const float gap = 12f;
        var tileWidth = (avail - gap * 2) / 3f;
        const float tileHeight = 86f;

        if (DrawTile(tileWidth, tileHeight, FontAwesomeIcon.Play, new Vector4(0.95f, 0.25f, 0.25f, 1f),
                "YouTube", "Search and play any video"))
        {
            pendingSearchTab = "YouTube";
            currentPage = HomePage.Player;
        }

        ImGui.SameLine(0, gap);
        if (DrawTile(tileWidth, tileHeight, FontAwesomeIcon.BroadcastTower, new Vector4(0.62f, 0.35f, 0.95f, 1f),
                "Twitch", "Check if a channel is live"))
        {
            pendingSearchTab = "Twitch";
            currentPage = HomePage.Player;
        }

        ImGui.SameLine(0, gap);
        if (DrawTile(tileWidth, tileHeight, FontAwesomeIcon.Users, Accent,
                "Watch Together", "Host or join a synced room"))
        {
            currentPage = HomePage.WatchAlong;
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

        // Chevron hint like the mockup tiles.
        var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var chevronWidth = ImGui.CalcTextSize(chevron).X;
            var left = ImGui.GetContentRegionAvail().X;
            if (left > chevronWidth)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + left - chevronWidth);
            }

            ImGui.TextColored(MutedText, chevron);
        }

        ImGui.SetCursorPos(Vector2.Zero);
        var clicked = ImGui.InvisibleButton($"##tileClick{title}", new Vector2(width, height));
        if (ImGui.IsItemHovered())
        {
            ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)), 12f);
        }

        return clicked;
    }

    // Gold-outline Create Room banner matching the mockup Watch Together strip.
    private void DrawWatchTogetherBanner()
    {
        var width = ImGui.GetContentRegionAvail().X;
        const float height = 72f;
        var origin = ImGui.GetCursorScreenPos();
        var parentDrawList = ImGui.GetWindowDrawList();

        using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBg))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(18, 14)))
        using (var card = ImRaii.Child("##watchTogetherBanner", new Vector2(width, height), false, ImGuiWindowFlags.NoScrollbar))
        {
            if (card)
            {
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextColored(Accent, FontAwesomeIcon.Users.ToIconString());
                }

                ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.TextUnformatted("Watch Together");
                ImGui.TextColored(MutedText, stream.Mode == StreamMode.Hosting
                    ? "You're hosting — invite friends from Watch-along."
                    : queue.Current is not null
                        ? "Playing something — friends can join your room."
                        : "Play something to start hosting, or join a friend.");
                ImGui.EndGroup();

                ImGui.SameLine(MathF.Max(width - 150f, 200f));
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 8f);

                if (stream.Mode == StreamMode.Hosting)
                {
                    if (ImGui.Button("Manage roster", new Vector2(130, 34)))
                    {
                        currentPage = HomePage.WatchAlong;
                    }
                }
                else
                {
                    using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f))
                               .Push(ImGuiCol.ButtonHovered, new Vector4(Gold.X, Gold.Y, Gold.Z, 0.18f))
                               .Push(ImGuiCol.ButtonActive, new Vector4(Gold.X, Gold.Y, Gold.Z, 0.28f))
                               .Push(ImGuiCol.Text, Gold)
                               .Push(ImGuiCol.Border, Gold))
                    using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 1.5f))
                    {
                        if (ImGui.Button("Create Room", new Vector2(130, 34)))
                        {
                            currentPage = queue.Current is not null ? HomePage.WatchAlong : HomePage.Player;
                        }
                    }
                }
            }
        }

        parentDrawList.AddRect(origin, origin + new Vector2(width, height),
            ImGui.GetColorU32(new Vector4(Gold.X, Gold.Y, Gold.Z, 0.65f)), 14f, ImDrawFlags.None, 1.5f);
    }

    private static Vector4 PosterColor(string source)
    {
        var lower = source.ToLowerInvariant();
        if (lower.Contains("twitch"))
        {
            return Hex(0x2B6EA8);
        }

        if (lower.Contains("youtube"))
        {
            return Hex(0x5B3BD6);
        }

        return Hex(0x6F2F8F);
    }

    // Compact horizontal strip - presence without eating the whole page.
    private void DrawFriendsOnlineStrip()
    {
        var online = friends.Where(f => f.Online).Take(8).ToArray();

        using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBg))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(18, 14)))
        using (var strip = ImRaii.Child("##friendsOnlineStrip", new Vector2(-1, 72), false, ImGuiWindowFlags.NoScrollbar))
        {
            if (!strip)
            {
                return;
            }

            ImGui.TextColored(MutedText, online.Length > 0
                ? $"FRIENDS ONLINE · {online.Length}"
                : "FRIENDS ONLINE");

            if (usersOnlineCount > 0 || stream.IsConnected)
            {
                var usersLabel = usersOnlineCount == 1 ? "USERS ONLINE · 1" : $"USERS ONLINE · {usersOnlineCount}";
                var usersWidth = ImGui.CalcTextSize(usersLabel).X;
                ImGui.SameLine();
                ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX() + 12f,
                    ImGui.GetWindowContentRegionMax().X - usersWidth));
                ImGui.TextColored(MutedText, usersLabel);
            }

            ImGui.Spacing();

            if (online.Length == 0)
            {
                ImGui.TextColored(MutedText, CurrentSession is null
                    ? "Sign in to see friends."
                    : "Nobody's online right now.");
                ImGui.SameLine();
                if (ImGui.SmallButton("Friends"))
                {
                    currentPage = HomePage.Friends;
                }

                return;
            }

            foreach (var friend in online)
            {
                ImGui.PushID(friend.AccountId);
                DrawAvatarChip(friend.AvatarIcon, friend.AvatarColorHex, 28);
                ImGui.SameLine(0, 8);
                ImGui.TextUnformatted(friend.DisplayName);
                ImGui.SameLine(0, 18);
                ImGui.PopID();
            }
        }
    }

    // Shared with DrawActivity so the two feeds can't drift.
    private static string ActivityLabel(ActivityEventDto item) => item.Type switch
    {
        "StartedWatching" => $"{item.ActorDisplayName} started watching",
        "JoinedWatchAlong" => item.Metadata is { Length: > 0 }
            ? $"{item.ActorDisplayName} joined {item.Metadata}'s watch-along"
            : $"{item.ActorDisplayName} joined a watch-along",
        "FriendAccepted" => $"{item.ActorDisplayName} accepted a friend request",
        "PostLiked" => $"{item.ActorDisplayName} liked your post",
        "PostReplied" => $"{item.ActorDisplayName} replied to your post",
        "Mentioned" => $"{item.ActorDisplayName} mentioned you",
        "VenueSaved" => item.Metadata is { Length: > 0 }
            ? $"{item.ActorDisplayName} saved a venue: {item.Metadata}"
            : $"{item.ActorDisplayName} saved a venue",
        "WentLive" => $"{item.ActorDisplayName} went live",
        _ => $"{item.ActorDisplayName}: {item.Type}",
    };

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
            drawList.AddRectFilledMultiColor(min, min + new Vector2(fillWidth, height),
                ImGui.GetColorU32(Accent), ImGui.GetColorU32(BlueGlow),
                ImGui.GetColorU32(BlueGlow), ImGui.GetColorU32(Accent));
        }

        ImGui.Dummy(new Vector2(width, height));
    }
}
