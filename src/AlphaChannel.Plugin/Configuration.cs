using AlphaChannel.Plugin.Auth;
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

    public string RelayServerUrl { get; set; } = "https://alphachannel.duckdns.org";

    // Keyed by IClientState.LocalContentId, same idiom as CharacterDisplayNames below - a
    // character's sign-in is tied to the FFXIV character, not the plugin install. Two entries can
    // point at the same AccountId once linked (see Auth/AuthClient.cs). Only Watch-along, Friends,
    // Messages, and Activity require an entry here for the current character; Player/Screen/
    // Settings keep working with none, same zero-friction default as before accounts existed.
    public Dictionary<ulong, CharacterSession> CharacterSessions { get; set; } = new();

    // Keyed by AccountId (not LocalContentId) - DM identity belongs to the account, not whichever
    // character happens to be signed in as it. Base64 PKCS8 private key, DPAPI-protected on Windows
    // where available (see Crypto/KeyVault.cs) - the key never leaves this machine either way, this
    // is defense-in-depth against other local processes, not network protection.
    public Dictionary<string, string> DmPrivateKeys { get; set; } = new();

    public int Volume { get; set; } = 100;
    public bool Muted { get; set; }

    // Path to a cookies.txt file the player exported themselves from their own logged-in browser
    // session - never anything we generate or transmit, just a local file path handed to yt-dlp so
    // age-restricted videos can play. Opt-in and empty by default.
    public string? YouTubeCookiesPath { get; set; }

    // Alternative to YouTubeCookiesPath - reads cookies directly from a local Firefox profile
    // instead of a manually-exported file. Best-effort: depends on yt-dlp being able to locate and
    // read that profile from inside this process (see MpvRenderer's own note on the caveats).
    public bool UseFirefoxCookies { get; set; }

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
