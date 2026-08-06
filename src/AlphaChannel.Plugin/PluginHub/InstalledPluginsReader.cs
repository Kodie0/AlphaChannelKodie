using AlphaChannel.Contracts;
using Dalamud.Plugin;

namespace AlphaChannel.Plugin.PluginHub;

// Reads Plugin.PluginInterface.InstalledPlugins (IEnumerable<IExposedPlugin>) into the DTO synced
// to the server - see PluginHubService for the friends-only visibility this feeds. Only plugins
// that are currently enabled (IsLoaded) — installed-but-disabled is not a signal friends care about.
internal static class InstalledPluginsReader
{
    // Own internal name (AlphaChannel.Plugin.json) - excluded so every AlphaChannel user doesn't
    // trivially show up on their own friends' list running "AlphaChannel."
    private const string SelfInternalName = "AlphaChannel.Plugin";

    internal static InstalledPluginDto[] ReadCurrent() =>
        Plugin.PluginInterface.InstalledPlugins
            .Where(p => p.IsLoaded)
            .Where(p => p.InternalName != SelfInternalName)
            // Decommissioned/banned entries are stale registry noise; IsDev is almost always a local
            // test build with a throwaway name, not something friends would recognize.
            .Where(p => !p.IsDecommissioned && !p.IsBanned && !p.IsDev)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new InstalledPluginDto(p.InternalName, p.Name, p.Version?.ToString() ?? "0.0.0.0"))
            .ToArray();

    // Plugins we last toggled "on" via the hub — used so a second chip click can close again.
    // OpenMainUi/OpenConfigUi is what Dalamud fires for the plugin list "Open" button; well-behaved
    // plugins treat that as a toggle (IsOpen = !IsOpen), so invoking it again closes them.
    private static readonly HashSet<string> OpenedByHub = new(StringComparer.Ordinal);

    internal static bool IsOpenedByHub(string internalName) => OpenedByHub.Contains(internalName);

    // Opens (or closes, on a second click) the plugin's main window when available, otherwise its
    // config UI — same fallback Aetherphone's PluginCatalog uses for shortcut "open plugin" steps.
    internal static bool TryToggle(string internalName)
    {
        var plugin = FindLoaded(internalName);
        if (plugin is null)
        {
            return false;
        }

        try
        {
            if (plugin.HasMainUi)
            {
                plugin.OpenMainUi();
                ToggleHubOpenState(internalName);
                return true;
            }

            if (plugin.HasConfigUi)
            {
                plugin.OpenConfigUi();
                ToggleHubOpenState(internalName);
                return true;
            }
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[PluginHub] toggle {internalName} failed: {exception.Message}");
        }

        return false;
    }

    private static void ToggleHubOpenState(string internalName)
    {
        if (!OpenedByHub.Add(internalName))
        {
            OpenedByHub.Remove(internalName);
        }
    }

    internal static bool CanOpen(string internalName)
    {
        var plugin = FindLoaded(internalName);
        return plugin is not null && (plugin.HasMainUi || plugin.HasConfigUi);
    }

    private static IExposedPlugin? FindLoaded(string internalName) =>
        Plugin.PluginInterface.InstalledPlugins.FirstOrDefault(p =>
            p.IsLoaded && string.Equals(p.InternalName, internalName, StringComparison.Ordinal));
}
