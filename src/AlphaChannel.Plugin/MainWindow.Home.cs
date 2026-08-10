using AlphaChannel.Contracts;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Welcome Home — mockup layout with only real capabilities (no fake browse/retro/voice).
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

    // Player source tabs: Home CTAs set this before navigating to Player.
    private int playerSourceTab;
    private string friendSearch = string.Empty;
    private ISharedImmediateTexture? addFriendImage;
    private readonly Dictionary<string, ISharedImmediateTexture?> capabilityImages = new();

    private ISharedImmediateTexture? GetCapabilityImage(string fileName)
{
    if (capabilityImages.TryGetValue(fileName, out var cached))
    {
        return cached;
    }

    var path = Path.Combine(
        Plugin.PluginInterface.AssemblyLocation.DirectoryName!,
        "Assets",
        fileName);

    ISharedImmediateTexture? image = null;

    if (File.Exists(path))
    {
        image = Plugin.TextureProvider.GetFromFile(path);
    }

    capabilityImages[fileName] = image;
    return image;
}

    private void DrawHome()
    {
        if (Plugin.Cfg.ShowHomeHeroImage)
        {
            EnsureHomeHeroLoaded();
        }

        if (addFriendImage is null)
        {
            var path = Path.Combine(
                Plugin.PluginInterface.AssemblyLocation.DirectoryName!,
                "Assets",
                "addfriends.png");

            if (File.Exists(path))
            {
                addFriendImage = Plugin.TextureProvider.GetFromFile(path);
            }
        }

        // Fit the welcome stack into the visible content pane — no page scrollbar (Settings only).
        var avail = ImGui.GetContentRegionAvail();
        const float sectionGap = 16f;
        const float footerReserve = 36f;
        var workHeight = MathF.Max(280f, avail.Y - footerReserve);

        DrawHomeHeroBackground(workHeight * 0.38f);
        DrawHomeHero(workHeight * 0.38f);
        ImGui.Dummy(new Vector2(0, 15));
        DrawHomeCapabilities();
        ImGui.Dummy(new Vector2(0, 12));
        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + ImGui.GetContentRegionAvail().X);
        ImGui.PopTextWrapPos();
    }


    private void DrawHomeHero(float maxHeroHeight = 220f)
    {
        var showArt = Plugin.Cfg.ShowHomeHeroImage;
        var avail = ImGui.GetContentRegionAvail().X;
        const float gap = 20f;
        var textWidth = showArt ? MathF.Min(avail * 0.48f, 420f) : avail;
        var artWidth = MathF.Max(avail - textWidth - gap, 220f);

        ImGui.BeginGroup();
        ImGui.SetWindowFontScale(1.75f);
        ImGui.TextUnformatted("Welcome to ");
        ImGui.SameLine(0, 0);
        ImGui.TextColored(Accent, "Alpha Channel");
        ImGui.SetWindowFontScale(1f);

        ImGui.Dummy(new Vector2(0, 2));

        ImGui.SetWindowFontScale(1.15f);
        ImGui.TextColored(MutedText, "Cast. Watch. Together.");
        ImGui.SetWindowFontScale(1f);

        ImGui.Dummy(new Vector2(0, 6));

        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + textWidth);
        ImGui.TextWrapped(
            "Bring your favourite videos into Eorzea. Create watch parties, " +
            "share screens, and enjoy moments together with friends wherever you are.");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0, 18));

        var inviteHeight = 170f;
        var inviteWidth = ImGui.GetContentRegionAvail().X * 0.82f;

        var inviteOrigin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(
            inviteOrigin,
            inviteOrigin + new Vector2(inviteWidth, inviteHeight),
            ImGui.GetColorU32(new Vector4(CardBg.X, CardBg.Y, CardBg.Z, 0.45f)),
            14f);

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(16, 14)))
        using (var invite = ImRaii.Child(
    "##inviteFriends",
    new Vector2(inviteWidth, inviteHeight),
    false,
    ImGuiWindowFlags.NoBackground))
        {
            if (invite)
            {
                var imageSize = new Vector2(56, 56);
                var imageOffset = new Vector2(12, 12);

                var addFriendWrap = addFriendImage?.GetWrapOrDefault();

                if (addFriendWrap is not null)
                {
                    var imagePos = ImGui.GetCursorScreenPos() + imageOffset;

                    ImGui.GetWindowDrawList().AddImageRounded(
                        addFriendWrap.Handle,
                        imagePos,
                        imagePos + imageSize,
                        Vector2.Zero,
                        Vector2.One,
                        ImGui.GetColorU32(Vector4.One),
                        12f);
                }

                ImGui.Dummy(imageSize + imageOffset);
                ImGui.SameLine(0, 18);

                ImGui.BeginGroup();

                ImGui.Dummy(new Vector2(0, 6));

                ImGui.SetWindowFontScale(1.3f);
                ImGui.TextUnformatted("Invite your friends to watch with you!");
                ImGui.SetWindowFontScale(1f);

                ImGui.TextColored(
    MutedText,
    "Add your friends to host watch parties, share virtual screens,\nand watch together in sync across Eorzea.");

                ImGui.Dummy(new Vector2(0, 2));

                ImGui.SetNextItemWidth(inviteWidth - 250);

                ImGui.InputTextWithHint(
                    "##friendName",
                    "Enter a friend's name...",
                    ref friendSearch,
                    64);

                ImGui.SameLine();

                using (ImRaii.PushColor(ImGuiCol.Button, Accent)
                           .Push(ImGuiCol.ButtonHovered, AccentHover)
                           .Push(ImGuiCol.ButtonActive, AccentActive)
                           .Push(ImGuiCol.Text, Vector4.One))
                {
                    using (ImRaii.PushColor(ImGuiCol.Button, Accent)
                               .Push(ImGuiCol.ButtonHovered, AccentHover)
                               .Push(ImGuiCol.ButtonActive, AccentActive)
                               .Push(ImGuiCol.Text, Vector4.One))
                    {
                        if (ImGui.Button("##addFriend", new Vector2(120, 34)))
                        {
                            // Add friend action later
                        }

                        var buttonMin = ImGui.GetItemRectMin();
                        var buttonSize = ImGui.GetItemRectSize();

                        using (ImRaii.PushFont(UiBuilder.IconFont))
                        {
                            var icon = FontAwesomeIcon.UserPlus.ToIconString();
                            var iconSize = ImGui.CalcTextSize(icon);

                            ImGui.GetWindowDrawList().AddText(
                                buttonMin + new Vector2(14, (buttonSize.Y - iconSize.Y) * 0.5f),
                                ImGui.GetColorU32(Vector4.One),
                                icon);
                        }

                        ImGui.GetWindowDrawList().AddText(
    buttonMin + new Vector2(36, 7),
    ImGui.GetColorU32(Vector4.One),
    "Add Friend");

                        ImGui.Dummy(new Vector2(0, 12));
                    }
                }

                ImGui.EndGroup();
            }
        }

        ImGui.EndGroup();
        var textHeight = ImGui.GetItemRectSize().Y;

        // Hero image is now drawn as a background layer.
    }

    private void DrawHomeHeroBackground(float height)
    {
        if (!Plugin.Cfg.ShowHomeHeroImage || homeHero is not { } texture)
        {
            return;
        }

        var drawList = ImGui.GetWindowDrawList();

        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        var width = MathF.Min(avail.X * 0.55f, 520f);
        var size = new Vector2(width, height);

        var position = origin + new Vector2(avail.X - width, 0);

        var (uv0, uv1) = CoverUvs(texture.Width, texture.Height, width, height);

        drawList.AddImageRounded(
            texture.Handle,
            position,
            position + size,
            uv0,
            uv1,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.45f)),
            14f);
    }
    private void DrawHomeHeroArt(float width, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var size = new Vector2(width, height);

        if (homeHero is { } texture)
        {
            var (uv0, uv1) = CoverUvs(texture.Width, texture.Height, width, height);
            drawList.AddImageRounded(texture.Handle, origin, origin + size, uv0, uv1,
                ImGui.GetColorU32(Vector4.One), 14f);
            drawList.AddRect(origin, origin + size,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), 14f, ImDrawFlags.None, 1f);
            ImGui.Dummy(size);
            return;
        }

        // Gradient fallback while the asset loads (or if it's missing).
        drawList.AddRectFilledMultiColor(origin, origin + size,
            ImGui.GetColorU32(new Vector4(0.12f, 0.08f, 0.22f, 1f)),
            ImGui.GetColorU32(new Vector4(0.25f, 0.10f, 0.28f, 1f)),
            ImGui.GetColorU32(new Vector4(0.08f, 0.14f, 0.32f, 1f)),
            ImGui.GetColorU32(new Vector4(0.05f, 0.08f, 0.18f, 1f)));
        drawList.AddRect(origin, origin + size, ImGui.GetColorU32(BorderSubtle), 14f);
        ImGui.Dummy(size);
    }

    // UV crop so the image fills the box (cover) without stretching.
    private static (Vector2 Uv0, Vector2 Uv1) CoverUvs(float texW, float texH, float boxW, float boxH)
    {
        if (texW <= 0 || texH <= 0 || boxW <= 0 || boxH <= 0)
        {
            return (Vector2.Zero, Vector2.One);
        }

        var texAspect = texW / texH;
        var boxAspect = boxW / boxH;
        if (texAspect > boxAspect)
        {
            var visible = boxAspect / texAspect;
            var pad = (1f - visible) * 0.5f;
            return (new Vector2(pad, 0f), new Vector2(1f - pad, 1f));
        }

        var visibleV = texAspect / boxAspect;
        var padV = (1f - visibleV) * 0.5f;
        return (new Vector2(0f, padV), new Vector2(1f, 1f - padV));
    }

    private void DrawHomeCapabilities()
    {
        var sectionTitle = "What do you want to do?";
        var titleSize = ImGui.CalcTextSize(sectionTitle);
        var lineY = ImGui.GetCursorScreenPos().Y + titleSize.Y * 0.5f;
        var availWidth = ImGui.GetContentRegionAvail().X;

        var drawList = ImGui.GetWindowDrawList();
        var lineColor = ImGui.GetColorU32(BorderSubtle);

        drawList.AddLine(
            new Vector2(ImGui.GetCursorScreenPos().X, lineY),
            new Vector2(
                ImGui.GetCursorScreenPos().X + (availWidth - titleSize.X) * 0.5f - 12,
                lineY),
            lineColor,
            1f);

        drawList.AddLine(
            new Vector2(
                ImGui.GetCursorScreenPos().X + (availWidth + titleSize.X) * 0.5f + 12,
                lineY),
            new Vector2(
                ImGui.GetCursorScreenPos().X + availWidth,
                lineY),
            lineColor,
            1f);

        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() + (availWidth - titleSize.X) * 0.5f);

        ImGui.TextColored(Accent, sectionTitle);

        ImGui.SetCursorPosX(ImGui.GetStyle().WindowPadding.X);

        ImGui.Dummy(new Vector2(0, 2));

        var avail = ImGui.GetContentRegionAvail().X;

        const float gap = 12f;
        var cardWidth = (avail - gap * 2) / 3f;

        const float cardHeight = 128f;
        const float iconSize = 72f;
        const float titleY = 36f;
        const float bodyY = 60f;
        const float gapAfterTitle = 6f;

        DrawCapabilityCard(
            cardWidth, cardHeight, iconSize, titleY, bodyY, gapAfterTitle,
            FontAwesomeIcon.SignInAlt,
            Hex(0xEF4444),
            "watch-videos.png",
"Watch Videos",
"Watch YouTube, Twitch, or any video link.",
"Start watching →",
() => currentPage = HomePage.Player);

        ImGui.SameLine(0, gap);

        DrawCapabilityCard(
            cardWidth, cardHeight, iconSize, titleY, bodyY, gapAfterTitle,
            FontAwesomeIcon.PlusSquare,
Hex(0xF59E0B),
"create-room.png",
"Create Room",
"Host your own room and invite friends.",
"Create your room →",
() => currentPage = HomePage.Player);

        ImGui.SameLine(0, gap);

        DrawCapabilityCard(
            cardWidth, cardHeight, iconSize, titleY, bodyY, gapAfterTitle,
            FontAwesomeIcon.SignInAlt,
Hex(0xEC4899),
"join-room.png",
"Join Room",
"Enter a friend's room and start watching.",
"Join a room →",
() => currentPage = HomePage.Player);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 1);

        DrawCapabilityCard(
            cardWidth, cardHeight, iconSize, titleY, bodyY, gapAfterTitle,
            FontAwesomeIcon.Desktop,
Hex(0x8B5CF6),
"place-screen.png",
"Place a Screen",
"Move and resize your virtual screen.",
"Manage screen →",
() => currentPage = HomePage.Screen);

        ImGui.SameLine(0, gap);

        DrawCapabilityCard(
            cardWidth, cardHeight, iconSize, titleY, bodyY, gapAfterTitle,
            FontAwesomeIcon.UserFriends,
Hex(0x34D399),
"friends-list.png",
"Add Friends",
"Manage your friends and see who's online.",
"Friends List →",
() => currentPage = HomePage.Friends);

        ImGui.SameLine(0, gap);

        DrawCapabilityCard(
            cardWidth, cardHeight, iconSize, titleY, bodyY, gapAfterTitle,
            FontAwesomeIcon.ThLarge,
Hex(0x38BDF8),
"browse-apps.png",
"Browse Apps",
"Open chat, Hub, Tweeter, and more.",
"App Store →",
() => currentPage = HomePage.Apps);
    }

    // Fixed-size tile: background + hit target only claim layout; copy is DrawList-wrapped inside.
    private void DrawCapabilityCard(float width, float height, float iconSize, float titleY,
    float bodyY, float gapAfterTitle, FontAwesomeIcon icon, Vector4 color,
        string imageName, string title, string body, string actionText, Action onClick)
    {
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, height);

        if (ImGui.InvisibleButton($"##capHit{title}", size))
        {
            onClick();
        }

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(CardBg), 14f);

        if (ImGui.IsItemHovered())
        {
            drawList.AddRectFilled(origin, origin + size,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)), 14f);
            drawList.AddRect(origin, origin + size,
                ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.55f)), 14f,
                ImDrawFlags.None, 1.5f);
        }

        var discOrigin = origin + new Vector2(
            12f,
            12f);


        var image = GetCapabilityImage(imageName);

        var imageWrap = image?.GetWrapOrDefault();

        if (imageWrap is not null)
        {
            var imageSize = 48f;

            drawList.AddImageRounded(
                imageWrap.Handle,
                discOrigin,
                discOrigin + new Vector2(imageSize, imageSize),
                Vector2.Zero,
                Vector2.One,
                ImGui.GetColorU32(Vector4.One),
                12f);
        }

        var wrapWidth = MathF.Max(40f, width - (12f + 48f + 20f));

        var lineH = ImGui.GetTextLineHeight();

        var textPos = origin + new Vector2(
            12f + 48f + 16f,
            18f);
        var titleBottom = DrawWrappedLines(drawList, textPos, wrapWidth, lineH, 2,
            ImGui.GetColorU32(Vector4.One), title);
        var bodyBottom = DrawWrappedLines(
            drawList,
            new Vector2(textPos.X, titleBottom + gapAfterTitle),
            wrapWidth,
            lineH,
            3,
            ImGui.GetColorU32(MutedText),
            body);

        drawList.AddText(
            new Vector2(origin.X + 16, origin.Y + height - 24),
            ImGui.GetColorU32(color),
            actionText);
    }

    // Word-wrap into at most maxLines; returns Y just below the last drawn line.
    private static float DrawWrappedLines(ImDrawListPtr drawList, Vector2 pos, float wrapWidth,
        float lineHeight, int maxLines, uint color, string text)
    {
        var y = pos.Y;
        var linesDrawn = 0;
        var line = string.Empty;

        void Emit(string value)
        {
            if (linesDrawn >= maxLines || value.Length == 0)
            {
                return;
            }

            drawList.AddText(new Vector2(pos.X, y), color, value);
            y += lineHeight;
            linesDrawn++;
        }

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (linesDrawn >= maxLines)
            {
                break;
            }

            var test = line.Length == 0 ? word : line + " " + word;
            if (ImGui.CalcTextSize(test).X <= wrapWidth)
            {
                line = test;
                continue;
            }

            if (line.Length == 0)
            {
                Emit(word);
                continue;
            }

            Emit(line);
            line = word;
        }

        Emit(line);
        return y;
    }

    private void DrawHomeHowItWorks()
    {
        ImGui.TextUnformatted("How it works");
        ImGui.Dummy(new Vector2(0, 10));

        var avail = ImGui.GetContentRegionAvail().X;
        const float gap = 12f;
        var stepWidth = (avail - gap * 2) / 3f;

        DrawHowStep(stepWidth, 1, Accent, FontAwesomeIcon.UserPlus, "Invite Friends",
            "Add people, then host or join from Player.",
            () => currentPage = CurrentSession is null ? HomePage.Settings : HomePage.Friends);
        ImGui.SameLine(0, gap);
        DrawHowStep(stepWidth, 2, Hex(0xA78BFA), FontAwesomeIcon.Play, "Pick Something",
            "Paste a link or search YouTube / Twitch.",
            () =>
            {
                playerSourceTab = 0;
                currentPage = HomePage.Player;
            });
        ImGui.SameLine(0, gap);
        DrawHowStep(stepWidth, 3, Hex(0x34D399), FontAwesomeIcon.Heart, "Enjoy Together",
            "Everyone stays in sync on the screen.",
            () => currentPage = HomePage.Player);
    }

    private void DrawHowStep(float width, int number, Vector4 color, FontAwesomeIcon icon,
        string title, string body, Action onClick)
    {
        const float pad = 12f;
        const float badge = 24f;
        const float badgeGap = 10f;
        const float titleGap = 4f;

        // Full inner width for wrapped body — no side column stealing space.
        var wrapWidth = MathF.Max(40f, width - (pad * 2f));
        var titleWrap = MathF.Max(40f, wrapWidth - badge - badgeGap);
        var titleSize = ImGui.CalcTextSize(title, false, titleWrap);
        var bodySize = ImGui.CalcTextSize(body, false, wrapWidth);
        var headerH = MathF.Max(badge, titleSize.Y);
        var height = pad + headerH + titleGap + bodySize.Y + pad;

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var size = new Vector2(width, height);

        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(CardBg), 14f);

        var badgeCenter = origin + new Vector2(pad + badge * 0.5f, pad + headerH * 0.5f);
        drawList.AddCircleFilled(badgeCenter, badge * 0.5f, ImGui.GetColorU32(color));
        var num = number.ToString();
        var numSize = ImGui.CalcTextSize(num);
        drawList.AddText(badgeCenter - numSize * 0.5f, ImGui.GetColorU32(Vector4.One), num);

        // Title to the right of the badge; body on the next row across the full card width.
        // PushTextWrapPos is window-local X (not screen).
        var titlePos = origin + new Vector2(pad + badge + badgeGap, pad + (headerH - titleSize.Y) * 0.5f);
        ImGui.SetCursorScreenPos(titlePos);
        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + titleWrap);
        ImGui.TextUnformatted(title);
        ImGui.PopTextWrapPos();

        var bodyPos = origin + new Vector2(pad, pad + headerH + titleGap);
        ImGui.SetCursorScreenPos(bodyPos);
        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + wrapWidth);
        ImGui.TextColored(MutedText, body);
        ImGui.PopTextWrapPos();

        // Soft icon accent in the top-right corner (doesn't fight title layout).
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var glyph = icon.ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(
                origin + new Vector2(width - pad - glyphSize.X, pad),
                ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.35f)),
                glyph);
        }

        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton($"##howHit{number}", size))
        {
            onClick();
        }

        if (ImGui.IsItemHovered())
        {
            drawList.AddRect(origin, origin + size,
                ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.45f)), 14f,
                ImDrawFlags.None, 1.5f);
        }
    }

    private static void DrawAvatarStack(ParticipantInfo[] participants, int maxShown)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        const float radius = 12f;
        const float overlap = 16f;
        var shown = Math.Min(participants.Length, maxShown);
        for (var index = 0; index < shown; index++)
        {
            var center = origin + new Vector2(radius + index * overlap, radius);
            drawList.AddCircleFilled(center, radius + 1.5f, ImGui.GetColorU32(WindowBg));
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(AvatarPalette[index % AvatarPalette.Length]));
        }

        ImGui.Dummy(new Vector2(radius * 2 + Math.Max(0, shown - 1) * overlap, radius * 2));
        if (participants.Length > maxShown)
        {
            ImGui.SameLine();
            ImGui.TextColored(MutedText, $"+{participants.Length - maxShown}");
        }
    }

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
}
