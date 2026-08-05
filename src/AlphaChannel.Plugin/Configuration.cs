using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AlphaChannel.Plugin;

[Serializable]
internal sealed class ScreenPositionPreset
{
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Scale { get; set; } = 1.0f;
}

// Ported from Aetherphone's Configuration.cs's own VideoQueueRecord - kept as its own DTO rather
// than serializing VideoQueueEntry directly so this doesn't couple to AetherStreamQueue's
// internals (it also carries a Guid Id and mutable fields that don't belong in a saved record).
[Serializable]
internal sealed class VideoQueueRecord
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public double? DurationSeconds { get; set; }
    public string? ThumbnailUrl { get; set; }
}

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Self-asserted identity sent as the relay's bearer token - see the auth note in the plan this
    // was built from. Generated once on first run and persisted; there is no real account system
    // behind it, the relay just trusts whatever UserId shows up.
    public string UserId { get; set; } = Guid.NewGuid().ToString("N");
    public string RelayServerUrl { get; set; } = "https://alphachannel.duckdns.org";

    // Keyed by IClientState.LocalContentId - the display name a player picked is tied to the FFXIV
    // character they were playing when they picked it, not to the Windows/plugin install, so an alt
    // gets its own prompt instead of inheriting the main character's name.
    public Dictionary<ulong, string> CharacterDisplayNames { get; set; } = new();

    public List<VideoQueueRecord> VideoQueue { get; set; } = new();
    public List<ScreenPositionPreset> ScreenPresets { get; set; } = new();

    public Vector3 ScreenPosition { get; set; }
    public float ScreenYaw { get; set; }
    public float ScreenScale { get; set; } = 1f;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
