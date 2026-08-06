using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlphaChannel.Contracts;

namespace AlphaChannel.Plugin.Auth;

internal sealed class LiveClient(Configuration configuration)
{
    private HttpClient Http(string bearerToken)
    {
        var http = new HttpClient { BaseAddress = new Uri(configuration.RelayServerUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return http;
    }

    // Regenerating instantly invalidates any previous key - an OBS session still pushing with the
    // old one starts failing its next publish-auth check (see Server/Live/LiveService.cs).
    internal async Task<string?> RotateKeyAsync(string bearerToken)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.PostAsync("/live/key/rotate", null).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<RotateStreamKeyResponse>().ConfigureAwait(false);
            return result?.StreamKey;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Live] key rotate failed: {exception.Message}");
            return null;
        }
    }

    internal Task<LiveStatusDto?> GetMyStatusAsync(string bearerToken) => GetAsync<LiveStatusDto>(bearerToken, "/live/mine");

    internal async Task<LiveFriendDto[]> GetFriendsLiveAsync(string bearerToken) =>
        await GetAsync<LiveFriendDto[]>(bearerToken, "/live/friends").ConfigureAwait(false) ?? [];

    private async Task<T?> GetAsync<T>(string bearerToken, string path)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.GetAsync(path).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<T>().ConfigureAwait(false)
                : default;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Live] request to {path} failed: {exception.Message}");
            return default;
        }
    }
}
