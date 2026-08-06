using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlphaChannel.Contracts;

namespace AlphaChannel.Plugin.Auth;

internal sealed class DmClient(Configuration configuration)
{
    private HttpClient Http(string bearerToken)
    {
        var http = new HttpClient { BaseAddress = new Uri(configuration.RelayServerUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return http;
    }

    internal async Task<string?> StartConversationAsync(string bearerToken, string otherAccountId)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.PostAsync($"/dm/conversations/{otherAccountId}", null).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            return doc.RootElement.TryGetProperty("conversationId", out var idEl) ? idEl.GetString() : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Dm] start conversation failed: {exception.Message}");
            return null;
        }
    }

    internal Task<ConversationSummaryDto[]?> GetConversationsAsync(string bearerToken) =>
        GetAsync<ConversationSummaryDto[]>(bearerToken, "/dm/conversations");

    internal Task<MessagePage?> GetMessagesAsync(string bearerToken, string conversationId, long? before) =>
        GetAsync<MessagePage>(bearerToken, before is { } cursor
            ? $"/dm/conversations/{conversationId}/messages?before={cursor}"
            : $"/dm/conversations/{conversationId}/messages");

    internal async Task<MessageDto?> SendMessageAsync(string bearerToken, string conversationId, SendMessageRequest request)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.PostAsJsonAsync($"/dm/conversations/{conversationId}/messages", request).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<MessageDto>().ConfigureAwait(false)
                : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Dm] send failed: {exception.Message}");
            return null;
        }
    }

    internal async Task MarkReadAsync(string bearerToken, string conversationId)
    {
        using var http = Http(bearerToken);
        try
        {
            await http.PostAsync($"/dm/conversations/{conversationId}/read", null).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Dm] mark-read failed: {exception.Message}");
        }
    }

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
            AepLog.Warning($"[Dm] request to {path} failed: {exception.Message}");
            return default;
        }
    }
}
