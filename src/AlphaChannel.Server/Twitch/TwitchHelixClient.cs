using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AlphaChannel.Contracts;

namespace AlphaChannel.Server.Twitch;

// Twitch's official Helix API - real trending data via the actual supported endpoint, not scraping.
// App-only auth (client_credentials grant) since this only ever reads public data, no per-user
// Twitch login involved anywhere in this app.
internal sealed class TwitchHelixClient(HttpClient http, TwitchOptions options)
{
    private string? cachedAppToken;
    private DateTime tokenExpiresAtUtc = DateTime.MinValue;

    internal bool IsConfigured => options.ClientId.Length > 0 && options.ClientSecret.Length > 0;

    internal async Task<List<TwitchStreamDto>> GetTopStreamsAsync(int count, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var token = await EnsureAppTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.twitch.tv/helix/streams?first={count}");
        request.Headers.Add("Client-Id", options.ClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var payload = await response.Content.ReadFromJsonAsync<HelixStreamsResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return payload?.Data.Select(ToDto).ToList() ?? [];
    }

    private async Task<string?> EnsureAppTokenAsync(CancellationToken cancellationToken)
    {
        if (cachedAppToken is not null && DateTime.UtcNow < tokenExpiresAtUtc)
        {
            return cachedAppToken;
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["grant_type"] = "client_credentials",
        };

        var response = await http.PostAsync("https://id.twitch.tv/oauth2/token", new FormUrlEncodedContent(form), cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var token = await response.Content.ReadFromJsonAsync<HelixTokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return null;
        }

        cachedAppToken = token.AccessToken;
        // A minute of slack so a request never starts with a token that's about to expire mid-flight.
        tokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, token.ExpiresIn - 60));
        return cachedAppToken;
    }

    // Twitch's thumbnail_url is a template with literal "{width}x{height}" placeholders, not a
    // ready-to-use URL - a fixed size is fine here, nothing in this app does responsive images.
    private static TwitchStreamDto ToDto(HelixStream s) => new(
        s.UserName, s.Title, s.GameName, s.ViewerCount,
        s.ThumbnailUrl.Replace("{width}", "440").Replace("{height}", "248"),
        $"https://twitch.tv/{s.UserLogin}");

    private sealed record HelixTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record HelixStreamsResponse([property: JsonPropertyName("data")] List<HelixStream> Data);

    private sealed record HelixStream(
        [property: JsonPropertyName("user_login")] string UserLogin,
        [property: JsonPropertyName("user_name")] string UserName,
        [property: JsonPropertyName("game_name")] string GameName,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("viewer_count")] int ViewerCount,
        [property: JsonPropertyName("thumbnail_url")] string ThumbnailUrl);
}
