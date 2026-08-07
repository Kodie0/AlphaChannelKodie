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
        SettingsSection("Account", "Sign-in, display name, and invite code.");
        DrawAccountSettings();
        SettingsHairline();

        SettingsSection("Appearance", "Colors and window chrome.");
        ImGui.TextColored(MutedText, "Accent");
        ImGui.Spacing();
        DrawThemeSettings();
        ImGui.Dummy(new Vector2(0, 12));
        ImGui.TextColored(MutedText, "Background");
        ImGui.Spacing();
        DrawBackgroundSettings();
        ImGui.Dummy(new Vector2(0, 10));
        DrawCustomBackgroundSettings();
        ImGui.Dummy(new Vector2(0, 10));
        ImGui.TextColored(MutedText, "Home illustration");
        ImGui.Spacing();
        DrawHomeHeroSettings();
        SettingsHairline();

        SettingsSection("Whispers", "Native /tell history on this machine.");
        DrawWhisperSettings();

        // Hidden from players — enable ShowServerStackSwitcher in the plugin config JSON to show.
        if (Plugin.Cfg.ShowServerStackSwitcher)
        {
            SettingsHairline();
            SettingsSection("Advanced", "Prod vs isolated dev relay.");
            DrawServerSettings();
        }
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

    private void DrawBackgroundSettings()
    {
        DrawBackgroundOption(UiBackground.Theme);
        ImGui.SameLine(0, 8);
        DrawBackgroundOption(UiBackground.Midnight);
        ImGui.SameLine(0, 8);
        DrawBackgroundOption(UiBackground.Void);
        ImGui.SameLine(0, 8);
        DrawBackgroundOption(UiBackground.Slate);
        ImGui.SameLine(0, 8);
        DrawBackgroundOption(UiBackground.Warm);
        ImGui.SameLine(0, 8);
        DrawBackgroundOption(UiBackground.Carbon);
        ImGui.SameLine(0, 8);
        DrawBackgroundOption(UiBackground.Custom);
    }

    private void DrawCustomBackgroundSettings()
    {
        if (!customBackgroundPathSynced)
        {
            customBackgroundPathInput = Plugin.Cfg.CustomBackgroundPath ?? string.Empty;
            customBackgroundPathSynced = true;
        }

        ImGui.TextColored(MutedText, "Your image");
        ImGui.TextColored(new Vector4(MutedText.X, MutedText.Y, MutedText.Z, 0.85f),
            "Paste a path to a png/jpg/webp, or grab the newest image from Downloads.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-70f);
        ImGui.InputTextWithHint("##customBgPath", "/path/to/image.png", ref customBackgroundPathInput, 512);
        ImGui.SameLine();
        if (ImGui.Button("Apply##customBg"))
        {
            TryApplyCustomBackgroundFromPath(customBackgroundPathInput);
        }

        if (ImGui.SmallButton("Find in Downloads##customBg"))
        {
            var found = FindImageInDownloads();
            if (found is null)
            {
                customBackgroundError = "No image found in Downloads.";
            }
            else
            {
                customBackgroundPathInput = found;
                TryApplyCustomBackgroundFromPath(found);
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrEmpty(Plugin.Cfg.CustomBackgroundPath)))
        {
            if (ImGui.SmallButton("Clear##customBg"))
            {
                ClearCustomBackground();
            }
        }

        if (Plugin.Cfg.UiBackground == UiBackground.Custom ||
            !string.IsNullOrEmpty(Plugin.Cfg.CustomBackgroundPath))
        {
            ImGui.Spacing();
            var dim = Plugin.Cfg.CustomBackgroundDim;
            ImGui.SetNextItemWidth(220f);
            if (ImGui.SliderFloat("Dim overlay##customBgDim", ref dim, 0f, 0.85f, "%.2f"))
            {
                Plugin.Cfg.CustomBackgroundDim = dim;
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                Plugin.Cfg.Save();
            }

            ImGui.SameLine();
            ImGui.TextColored(MutedText, "Higher = darker panels over the photo");
        }

        if (customBackgroundError is { } error)
        {
            ImGui.TextColored(Danger, error);
        }
        else if (Plugin.Cfg.UiBackground == UiBackground.Custom && customBackground is not null)
        {
            ImGui.TextColored(Good, "Custom background active.");
        }
        else if (!string.IsNullOrEmpty(Plugin.Cfg.CustomBackgroundPath) &&
                 Plugin.Cfg.UiBackground != UiBackground.Custom)
        {
            ImGui.TextColored(MutedText, "Image saved — pick Custom above to use it.");
        }
    }

    private void DrawHomeHeroSettings()
    {
        if (!customHomeHeroPathSynced)
        {
            customHomeHeroPathInput = Plugin.Cfg.CustomHomeHeroPath ?? string.Empty;
            customHomeHeroPathSynced = true;
        }

        var showHero = Plugin.Cfg.ShowHomeHeroImage;
        if (ImGui.Checkbox("Show Home welcome illustration", ref showHero))
        {
            Plugin.Cfg.ShowHomeHeroImage = showHero;
            Plugin.Cfg.Save();
        }

        ImGui.TextColored(MutedText, "Picture next to Welcome on Home. Use the default art or your own.");
        ImGui.Spacing();

        using (ImRaii.Disabled(!Plugin.Cfg.ShowHomeHeroImage))
        {
            ImGui.SetNextItemWidth(-70f);
            ImGui.InputTextWithHint("##customHomeHeroPath", "/path/to/image.png", ref customHomeHeroPathInput,
                512);
            ImGui.SameLine();
            if (ImGui.Button("Apply##homeHero"))
            {
                TryApplyCustomHomeHeroFromPath(customHomeHeroPathInput);
            }

            if (ImGui.SmallButton("Find in Downloads##homeHero"))
            {
                var found = FindImageInDownloads();
                if (found is null)
                {
                    customHomeHeroError = "No image found in Downloads.";
                }
                else
                {
                    customHomeHeroPathInput = found;
                    TryApplyCustomHomeHeroFromPath(found);
                }
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(string.IsNullOrEmpty(Plugin.Cfg.CustomHomeHeroPath)))
            {
                if (ImGui.SmallButton("Use default##homeHero"))
                {
                    ClearCustomHomeHero();
                }
            }
        }

        if (customHomeHeroError is { } error)
        {
            ImGui.TextColored(Danger, error);
        }
        else if (!string.IsNullOrEmpty(Plugin.Cfg.CustomHomeHeroPath) && Plugin.Cfg.ShowHomeHeroImage)
        {
            ImGui.TextColored(Good, "Using your Home illustration.");
        }
    }

    private void DrawBackgroundOption(UiBackground background)
    {
        var selected = Plugin.Cfg.UiBackground == background;
        var label = ThemeCatalog.Label(background);
        var swatch = background == UiBackground.Theme
            ? ThemeCatalog.Get(Plugin.Cfg.UiTheme).WindowBg
            : ThemeCatalog.Swatch(background);
        var size = new Vector2(92, 36);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        ImGui.PushID((int)background + 100);
        var clicked = ImGui.InvisibleButton("##bg", size);
        var hovered = ImGui.IsItemHovered();
        ImGui.PopID();

        drawList.AddRectFilled(origin, origin + size,
            ImGui.GetColorU32(selected ? new Vector4(Accent.X, Accent.Y, Accent.Z, 0.22f)
                : hovered ? CardBgHover : CardBg), 10f);
        if (selected)
        {
            drawList.AddRect(origin, origin + size, ImGui.GetColorU32(Accent), 10f, ImDrawFlags.None, 1.5f);
        }

        drawList.AddCircleFilled(origin + new Vector2(18, size.Y / 2), 7f, ImGui.GetColorU32(swatch));
        drawList.AddCircle(origin + new Vector2(18, size.Y / 2), 7f,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.25f)), 0, 1f);
        drawList.AddText(origin + new Vector2(32, size.Y / 2 - 7), ImGui.GetColorU32(Vector4.One), label);

        if (clicked && !selected)
        {
            if (background == UiBackground.Custom && string.IsNullOrWhiteSpace(Plugin.Cfg.CustomBackgroundPath))
            {
                customBackgroundError = "Apply an image below first.";
                return;
            }

            Plugin.Cfg.UiBackground = background;
            Plugin.Cfg.Save();
            Colors = ThemeCatalog.Get(Plugin.Cfg.UiTheme, background);
            if (background == UiBackground.Custom)
            {
                customBackgroundLoadStarted = false;
                EnsureCustomBackgroundLoaded();
            }
        }
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
            Colors = ThemeCatalog.Get(theme, Plugin.Cfg.UiBackground);
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
