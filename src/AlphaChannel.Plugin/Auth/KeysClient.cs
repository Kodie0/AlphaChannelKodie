using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlphaChannel.Contracts;

namespace AlphaChannel.Plugin.Auth;

internal sealed class KeysClient(Configuration configuration)
{
    internal async Task<bool> UploadPublicKeyAsync(string bearerToken, string publicKeyBase64)
    {
        using var http = new HttpClient { BaseAddress = new Uri(configuration.RelayServerUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            var response = await http.PutAsJsonAsync("/keys/me", new UploadPublicKeyRequest(publicKeyBase64)).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Keys] upload failed: {exception.Message}");
            return false;
        }
    }

    internal async Task<string?> GetPublicKeyAsync(string bearerToken, string accountId)
    {
        using var http = new HttpClient { BaseAddress = new Uri(configuration.RelayServerUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            var response = await http.GetAsync($"/keys/users/{accountId}").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<PublicKeyDto>().ConfigureAwait(false);
            return dto?.PublicKeyBase64;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Keys] fetch failed: {exception.Message}");
            return null;
        }
    }
}
