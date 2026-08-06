using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlphaChannel.Contracts;

namespace AlphaChannel.Plugin.Auth;

internal sealed class TwitchClient(Configuration configuration)
{
    internal async Task<TwitchStreamDto[]> GetTrendingAsync(string bearerToken)
    {
        using var http = new HttpClient { BaseAddress = new Uri(configuration.RelayServerUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            var response = await http.GetAsync("/twitch/trending").ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<TwitchStreamDto[]>().ConfigureAwait(false) ?? []
                : [];
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Twitch] trending fetch failed: {exception.Message}");
            return [];
        }
    }
}
