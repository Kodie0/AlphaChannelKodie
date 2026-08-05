using Dalamud.Bindings.ImGui;

namespace AlphaChannel.Plugin;

internal sealed partial class MainWindow
{
    private void DrawSettings()
    {
        SectionHeader("Age-restricted video settings");
        DrawCookiesSettings();
    }
}
