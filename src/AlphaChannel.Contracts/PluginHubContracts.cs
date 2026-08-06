namespace AlphaChannel.Contracts;

// One entry per installed plugin, as reported by the client's own IDalamudPluginInterface -
// nothing here is verified server-side beyond trusting the plugin, same posture as IsLalafell in
// AuthStartRequest.
public sealed record InstalledPluginDto(string InternalName, string Name, string Version);

// Wholesale replace, not a diff - see PluginHubService.SyncAsync for why.
public sealed record SyncInstalledPluginsRequest(InstalledPluginDto[] Plugins);
