using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Settings is a preferences sheet — stacked labeled sections with hairlines, not the same
// CardBg tiles used on Home/Player. Identity: configure the plugin, don't browse content.
internal sealed partial class MainWindow
{
    private const string ProductionServerUrl = "https://alphachannel.duckdns.org";
    private const string DevServerUrl = "http://194.113.211.29:5001";

    private string serverUrlInput = string.Empty;
    private bool serverUrlSynced;

    private void DrawSettings()
    {
        SettingsSection("Appearance", "Chrome colors for this window.");
        DrawThemeSettings();
        SettingsHairline();

        SettingsSection("Account", "Sign-in, display name, invite code.");
        DrawAccountSettings();
        SettingsHairline();

        SettingsSection("Whispers", "Native /tell history on this machine.");
        DrawWhisperSettings();
        SettingsHairline();

        SettingsSection("Age-restricted video", "Optional cookies for yt-dlp.");
        DrawCookiesSettings();
        SettingsHairline();

        SettingsSection("Server", "Advanced — prod vs isolated dev stack.");
        DrawServerSettings();
    }

    private static void SettingsSection(string title, string blurb)
    {
        ImGui.TextUnformatted(title);
        ImGui.TextColored(MutedText, blurb);
        ImGui.Spacing();
    }

    private static void SettingsHairline()
    {
        ImGui.Spacing();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddRectFilled(origin, origin + new Vector2(width, 1f),
            ImGui.GetColorU32(BorderSubtle));
        ImGui.Dummy(new Vector2(width, 14f));
    }

    private void DrawThemeSettings()
    {
        DrawThemeOption(UiTheme.Purple, Hex(0x8B5CF6));
        ImGui.SameLine(0, 10);
        DrawThemeOption(UiTheme.Gold, Hex(0xD4AF37));
        ImGui.SameLine(0, 10);
        DrawThemeOption(UiTheme.Green, Hex(0x34D399));
        ImGui.SameLine(0, 10);
        DrawThemeOption(UiTheme.Red, Hex(0xE11D48));
    }

    private void DrawThemeOption(UiTheme theme, Vector4 swatch)
    {
        var selected = Plugin.Cfg.UiTheme == theme;
        var label = ThemeCatalog.Label(theme);
        var size = new Vector2(88, 36);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        ImGui.PushID((int)theme);
        var clicked = ImGui.InvisibleButton("##theme", size);
        var hovered = ImGui.IsItemHovered();
        ImGui.PopID();

        drawList.AddRectFilled(origin, origin + size,
            ImGui.GetColorU32(selected ? new Vector4(swatch.X, swatch.Y, swatch.Z, 0.28f)
                : hovered ? CardBgHover : CardBg), 10f);
        if (selected)
        {
            drawList.AddRect(origin, origin + size, ImGui.GetColorU32(swatch), 10f, ImDrawFlags.None, 1.5f);
        }

        drawList.AddCircleFilled(origin + new Vector2(18, size.Y / 2), 7f, ImGui.GetColorU32(swatch));
        drawList.AddText(origin + new Vector2(32, size.Y / 2 - 7), ImGui.GetColorU32(Vector4.One), label);

        if (clicked && !selected)
        {
            Plugin.Cfg.UiTheme = theme;
            Plugin.Cfg.Save();
            Colors = ThemeCatalog.Get(theme);
        }
    }

    private void DrawWhisperSettings()
    {
        var archive = Plugin.Cfg.ArchiveWhispersToDisk;
        if (ImGui.Checkbox("Save /tell history to disk", ref archive))
        {
            Plugin.Cfg.ArchiveWhispersToDisk = archive;
            Plugin.Cfg.Save();
        }

        ImGui.TextColored(MutedText,
            "Per-character archive under the plugin config folder. Off keeps Whispers session-only.");
    }

    // Lets a dev-build plugin point at the isolated dev server (own DB, no real accounts) instead of
    // prod, so server-side changes can be tried end-to-end before the same build goes live - see
    // docker-compose.yml's alphachannel-server-dev for the other half of this. Signing in again is
    // required after switching since prod/dev accounts live in separate databases.
    private void DrawServerSettings()
    {
        if (!serverUrlSynced)
        {
            serverUrlInput = Plugin.Cfg.RelayServerUrl;
            serverUrlSynced = true;
        }

        ImGui.TextColored(MutedText, "Switching requires signing in again.");
        ImGui.SetNextItemWidth(320f);
        ImGui.InputText("##serverUrl", ref serverUrlInput, 128);
        ImGui.SameLine();
        using (ImRaii.Disabled(serverUrlInput.Trim() == Plugin.Cfg.RelayServerUrl))
        {
            if (ImGui.SmallButton("Save"))
            {
                Plugin.Cfg.RelayServerUrl = serverUrlInput.Trim();
                Plugin.Cfg.Save();
            }
        }

        if (ImGui.SmallButton("Use production"))
        {
            serverUrlInput = ProductionServerUrl;
            Plugin.Cfg.RelayServerUrl = ProductionServerUrl;
            Plugin.Cfg.Save();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Use dev"))
        {
            serverUrlInput = DevServerUrl;
            Plugin.Cfg.RelayServerUrl = DevServerUrl;
            Plugin.Cfg.Save();
        }

        ImGui.TextColored(MutedText, $"Currently: {Plugin.Cfg.RelayServerUrl}");
    }
}
