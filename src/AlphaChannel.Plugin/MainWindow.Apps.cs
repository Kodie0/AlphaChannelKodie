using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Apps launcher under More — companion apps as clickable tiles (Tweeter today).
// Alpha Chat stays in Social. Hit-testing matches Home tiles: InvisibleButton inside the card.
internal sealed partial class MainWindow
{
    private void DrawApps()
    {
        ImGui.TextColored(MutedText, "Tap an app to open it.");
        ImGui.Spacing();
        ImGui.Spacing();

        var avail = ImGui.GetContentRegionAvail().X;
        const float gap = 14f;
        var tileWidth = MathF.Min(300f, (avail - gap) / 2f);
        const float tileHeight = 120f;

        if (DrawAppTile(tileWidth, tileHeight, FontAwesomeIcon.Feather, Hex(0x38BDF8),
                "Tweeter", "Short posts for people you follow."))
        {
            currentPage = HomePage.Tweeter;
        }

        ImGui.SameLine(0, gap);
        DrawAppTile(tileWidth, tileHeight, FontAwesomeIcon.PuzzlePiece, Accent,
            "Coming soon", "More Alpha apps will land here.", enabled: false);
    }

    // Returns true when the tile is clicked (enabled tiles only).
    private bool DrawAppTile(
        float width,
        float height,
        FontAwesomeIcon icon,
        Vector4 iconColor,
        string title,
        string subtitle,
        bool enabled = true)
    {
        ImGui.PushID(title);
        var clicked = false;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBg))
        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(18, 16)))
        using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 16f))
        using (var card = ImRaii.Child("##appCard", new Vector2(width, height), false, ImGuiWindowFlags.NoScrollbar))
        {
            if (card)
            {
                var drawList = ImGui.GetWindowDrawList();
                var iconOrigin = ImGui.GetCursorScreenPos();
                const float disc = 44f;

                drawList.AddRectFilled(iconOrigin, iconOrigin + new Vector2(disc, disc),
                    ImGui.GetColorU32(new Vector4(iconColor.X, iconColor.Y, iconColor.Z, enabled ? 0.24f : 0.12f)),
                    14f);

                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    var glyph = icon.ToIconString();
                    var glyphSize = ImGui.CalcTextSize(glyph);
                    drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize() * 1.05f,
                        iconOrigin + new Vector2(disc, disc) / 2f - glyphSize / 2f,
                        ImGui.GetColorU32(enabled ? iconColor : MutedText), glyph);
                }

                ImGui.Dummy(new Vector2(disc, disc));
                ImGui.SameLine(0, 14);

                ImGui.BeginGroup();
                ImGui.Dummy(new Vector2(0, 6));
                ImGui.TextUnformatted(title);
                ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + width - disc - 56f);
                ImGui.TextColored(enabled ? MutedText : new Vector4(MutedText.X, MutedText.Y, MutedText.Z, 0.5f),
                    subtitle);
                ImGui.PopTextWrapPos();
                ImGui.EndGroup();

                if (enabled)
                {
                    var chevron = FontAwesomeIcon.ChevronRight.ToIconString();
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        var chevronW = ImGui.CalcTextSize(chevron).X;
                        ImGui.SetCursorPos(new Vector2(width - chevronW - 22f, (height - ImGui.GetFontSize()) / 2f));
                        ImGui.TextColored(MutedText, chevron);
                    }
                }

                // Whole-card click target drawn last so it sits on top of text/icons.
                ImGui.SetCursorPos(Vector2.Zero);
                clicked = enabled && ImGui.InvisibleButton("##launch", new Vector2(width, height));
                if (enabled && ImGui.IsItemHovered())
                {
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), 16f);
                    drawList.AddRect(min, max,
                        ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.55f)), 16f,
                        ImDrawFlags.None, 1.5f);
                }
            }
        }

        ImGui.PopID();
        return clicked;
    }
}
