using Dalamud.Bindings.ImGui;

namespace AlphaChannel.Plugin;

internal sealed partial class MainWindow
{
    private void DrawSettings()
    {
        SectionHeader("Account");
        DrawAccountSettings();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        SectionHeader("Age-restricted video settings");
        DrawCookiesSettings();
    }
}
