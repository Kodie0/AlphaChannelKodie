using AlphaChannel.Server.Auth;

namespace AlphaChannel.Server.Twitch;

internal static class TwitchEndpoints
{
    public static void MapTwitchEndpoints(this WebApplication app)
    {
        // Discovery content, not the social surface - no LalafellGateFilter, same reasoning
        // watch-along already uses (this endpoint doesn't touch anyone's account beyond who's
        // asking). Returns [] rather than an error if TWITCH_CLIENT_ID/SECRET were never
        // configured (TwitchHelixClient.IsConfigured), so the client's Trending tab just stays
        // empty instead of erroring.
        app.MapGet("/twitch/trending", (TwitchTrendingService trending) =>
            Results.Ok(trending.Current)).AddEndpointFilter<AccountAuthFilter>();
    }
}
