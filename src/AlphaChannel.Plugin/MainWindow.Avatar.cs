using System.Globalization;
using AlphaChannel.Contracts;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Avatar rendering + the curated icon/color picker - shared by Settings' profile editor and every
// place an avatar chip shows up (Friends list, Alpha Chat, Tweeter, the profile popup). Deliberately
// a closed set of FontAwesomeIcon glyphs + a small color palette, not free text or an uploaded
// image - see Account.AvatarIcon's server-side doc comment for why.
internal sealed partial class MainWindow
{
    private static readonly string[] AvatarIcons =
    [
        "Cat", "Dog", "Dragon", "Star", "Heart", "Gamepad", "Music", "Camera",
        "Ghost", "Crown", "Fish", "Feather", "Moon", "Sun", "Bolt", "Fire",
        "Leaf", "Snowflake", "Skull", "Gem", "Anchor", "Rocket", "Robot", "Paw",
        "Bug", "Frog", "Hippo", "Otter", "Spider", "Dove", "Crow", "Horse",
        "Dice", "Magic", "Bell", "Trophy",
    ];

    private static readonly string[] AvatarColors =
    [
        "#9966FA", "#FF6B6B", "#4ECDC4", "#FFD93D", "#6BCB77", "#4D96FF",
        "#FF922B", "#F783AC", "#A0A0A0", "#00C2A8",
    ];

    private static Vector4 ParseAvatarColor(string hex)
    {
        var trimmed = hex.TrimStart('#');
        if (trimmed.Length != 6 || !uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return new Vector4(0.6f, 0.4f, 1f, 1f);
        }

        return new Vector4(((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f, 1f);
    }

    // Draws a filled circle + centered glyph at the current cursor position via the draw list (not
    // child-window layout), then reserves layout space with Dummy so SameLine/etc. after this call
    // behave normally - same "capture position, draw via draw list, Dummy to reserve" idiom as
    // DrawGlowBorder's simpler cousin.
    private static void DrawAvatarChip(string? iconName, string colorHex, float diameter)
    {
        var topLeft = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var center = topLeft + new Vector2(diameter / 2, diameter / 2);
        drawList.AddCircleFilled(center, diameter / 2, ImGui.GetColorU32(ParseAvatarColor(colorHex)));

        if (iconName is { Length: > 0 } && Enum.TryParse<FontAwesomeIcon>(iconName, out var icon))
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var glyph = icon.ToIconString();
                var textSize = ImGui.CalcTextSize(glyph);
                drawList.AddText(center - textSize / 2, ImGui.GetColorU32(Vector4.One), glyph);
            }
        }

        ImGui.Dummy(new Vector2(diameter, diameter));
    }

    // Wraps into rows of 9 rather than relying on ImGui's automatic wrapping (which needs per-item
    // width math anyway) - simpler to just count and force a newline.
    private static bool DrawIconPicker(ref string? selectedIcon)
    {
        var changed = false;
        for (var index = 0; index < AvatarIcons.Length; index++)
        {
            var icon = AvatarIcons[index];
            if (index % 9 != 0)
            {
                ImGui.SameLine();
            }

            var isSelected = selectedIcon == icon;
            using (ImRaii.PushColor(ImGuiCol.Button, isSelected ? AccentActive : FrameBg))
            {
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    if (Enum.TryParse<FontAwesomeIcon>(icon, out var faIcon) && ImGui.Button(faIcon.ToIconString(), new Vector2(28, 28)))
                    {
                        selectedIcon = icon;
                        changed = true;
                    }
                }
            }
        }

        return changed;
    }

    private static bool DrawColorPicker(ref string selectedColor)
    {
        var changed = false;
        for (var index = 0; index < AvatarColors.Length; index++)
        {
            var color = AvatarColors[index];
            if (index % 9 != 0)
            {
                ImGui.SameLine();
            }

            using (ImRaii.PushColor(ImGuiCol.Button, ParseAvatarColor(color)))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, ParseAvatarColor(color)))
            using (ImRaii.PushColor(ImGuiCol.ButtonActive, ParseAvatarColor(color)))
            {
                var label = selectedColor == color ? FontAwesomeIcon.Check.ToIconString() : " ";
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    if (ImGui.Button(label + "##" + color, new Vector2(28, 28)))
                    {
                        selectedColor = color;
                        changed = true;
                    }
                }
            }
        }

        return changed;
    }
}
