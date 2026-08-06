using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlphaChannel.Contracts;

namespace AlphaChannel.Plugin.Auth;

internal sealed class ActivityClient(Configuration configuration)
{
    internal async Task<ActivityPage?> GetFeedAsync(string bearerToken, long? before)
    {
        using var http = new HttpClient { BaseAddress = new Uri(configuration.RelayServerUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            var query = before is { } cursor ? $"/activity?before={cursor}" : "/activity";
            var response = await http.GetAsync(query).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<ActivityPage>().ConfigureAwait(false)
                : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Activity] fetch failed: {exception.Message}");
            return null;
        }
    }

    internal async Task MarkReadAsync(string bearerToken, long upToUnix)
    {
        using var http = new HttpClient { BaseAddress = new Uri(configuration.RelayServerUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            await http.PostAsJsonAsync("/activity/read", new MarkActivityReadRequest(upToUnix)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Activity] mark-read failed: {exception.Message}");
        }
    }
}
