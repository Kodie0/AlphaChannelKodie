namespace AlphaChannel.Server.Twitch;

// Flat env vars (TWITCH_CLIENT_ID/TWITCH_CLIENT_SECRET), same pattern as XivAuthOptions - from
// registering an app at https://dev.twitch.tv/console/apps. App-only credentials (client_credentials
// grant, see TwitchHelixClient) - no per-user Twitch login needed since this only ever reads public
// trending data.
internal sealed class TwitchOptions
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
}
