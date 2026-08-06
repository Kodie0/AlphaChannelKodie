using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlphaChannel.Contracts;

namespace AlphaChannel.Plugin.Auth;

internal sealed class PluginHubClient(Configuration configuration)
{
    private HttpClient Http(string bearerToken)
    {
        var http = new HttpClient { BaseAddress = new Uri(configuration.RelayServerUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return http;
    }

    internal async Task<bool> SyncAsync(string bearerToken, InstalledPluginDto[] plugins)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.PutAsJsonAsync("/me/plugins", new SyncInstalledPluginsRequest(plugins)).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[PluginHub] sync failed: {exception.Message}");
            return false;
        }
    }

    // Null means "not friends" (server returns 404) vs. an empty array meaning "friends, but
    // nothing installed" - see PluginHubService.GetFriendPluginsAsync's own doc comment.
    internal async Task<InstalledPluginDto[]?> GetFriendPluginsAsync(string bearerToken, string accountId)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.GetAsync($"/friends/{accountId}/plugins").ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<InstalledPluginDto[]>().ConfigureAwait(false)
                : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[PluginHub] fetch failed: {exception.Message}");
            return null;
        }
    }
}
