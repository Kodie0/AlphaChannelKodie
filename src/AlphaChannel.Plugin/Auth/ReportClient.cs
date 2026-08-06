using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlphaChannel.Contracts;

namespace AlphaChannel.Plugin.Auth;

internal sealed class ReportClient(Configuration configuration)
{
    internal async Task<bool> SubmitAsync(
        string bearerToken, string category, string? note, string? targetAccountId, string? targetMessageId,
        string? revealedBody, string? frankingKeyBase64)
    {
        using var http = new HttpClient { BaseAddress = new Uri(configuration.RelayServerUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            var response = await http.PostAsJsonAsync("/reports",
                new SubmitReportRequest(category, note, targetAccountId, targetMessageId, revealedBody, frankingKeyBase64)).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Report] submit failed: {exception.Message}");
            return false;
        }
    }
}
