using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlphaChannel.Contracts;

namespace AlphaChannel.Plugin.Auth;

internal sealed class VenuesClient(Configuration configuration)
{
    private HttpClient Http(string bearerToken)
    {
        var http = new HttpClient { BaseAddress = new Uri(configuration.RelayServerUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return http;
    }

    internal async Task<VenueDto?> CreateAsync(string bearerToken, CreateVenueRequest request)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.PostAsJsonAsync("/venues", request).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<VenueDto>().ConfigureAwait(false) : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Venues] create failed: {exception.Message}");
            return null;
        }
    }

    internal Task<VenueDto[]?> GetMineAsync(string bearerToken) => GetAsync<VenueDto[]>(bearerToken, "/venues/mine");

    // Null means "not friends" (server 404s), distinct from an empty array meaning "friends, but no
    // venues saved" - see VenueService.GetFriendVenuesAsync's own doc comment.
    internal Task<VenueDto[]?> GetFriendVenuesAsync(string bearerToken, string accountId) =>
        GetAsync<VenueDto[]>(bearerToken, $"/friends/{accountId}/venues");

    internal async Task<bool> DeleteAsync(string bearerToken, string venueId)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.DeleteAsync($"/venues/{venueId}").ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Venues] delete failed: {exception.Message}");
            return false;
        }
    }

    private async Task<T?> GetAsync<T>(string bearerToken, string path)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.GetAsync(path).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>().ConfigureAwait(false) : default;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Venues] request to {path} failed: {exception.Message}");
            return default;
        }
    }
}
