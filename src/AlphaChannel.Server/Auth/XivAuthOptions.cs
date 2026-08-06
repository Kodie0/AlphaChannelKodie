namespace AlphaChannel.Server.Auth;

// Bound from flat XIVAUTH_* env vars (see .env.example). Confirmed against XIVAuth's real
// "Authenticating to the API" + "API Version 1" docs:
// - Device Code Request URL: /oauth/authorize_device (NOT /oauth/device - that's the browser
//   verification page a human visits, never POSTed to directly by a client)
// - Token Request URL: /oauth/token (shared by every grant type)
// - Characters API: GET /characters - the access token is opaque, NOT a JWT with claims baked in;
//   character name/world/persistent_key come from this separate authenticated call, not by
//   decoding the token itself.
internal sealed class XivAuthOptions
{
    public string DeviceAuthorizationEndpoint { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
    public string CharactersEndpoint { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    // Single "character" scope, not "character:all" - AlphaChannel signs in one character per
    // flow, so GET /characters is guaranteed exactly one entry back (confirmed by their docs).
    public string Scope { get; set; } = "character";
}
