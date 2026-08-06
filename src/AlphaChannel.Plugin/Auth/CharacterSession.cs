namespace AlphaChannel.Plugin.Auth;

// One of these per FFXIV character that's been signed in on this install, keyed by LocalContentId
// in Configuration.CharacterSessions - same idiom as CharacterDisplayNames. Multiple characters can
// point at the same AccountId once linked (see AuthClient's link flow), which is what makes
// multi-character linking "just work" everywhere downstream that keys off AccountId.
[Serializable]
internal sealed class CharacterSession
{
    public string AccountId { get; set; } = "";
    public string Token { get; set; } = "";
    public string Handle { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
