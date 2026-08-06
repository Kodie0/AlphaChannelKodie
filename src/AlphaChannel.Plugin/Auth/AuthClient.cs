using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlphaChannel.Contracts;
using AlphaChannel.Plugin;

namespace AlphaChannel.Plugin.Auth;

// Thin REST wrapper around AlphaChannel.Server's /auth/* + /me endpoints. Separate from
// StreamClient (which only ever speaks the /rt websocket) since sign-in is plain request/response,
// not a persistent connection - same split Aetherphone draws between HttpService and RealtimeConnection.
internal sealed class AuthClient(Configuration configuration)
{
    private HttpClient Http => new() { BaseAddress = new Uri(configuration.RelayServerUrl) };

    internal Task<AuthStartResponse?> StartAsync(string characterName, string world, bool isLalafell) =>
        PostAsync<AuthStartResponse>("/auth/xivauth/start", new AuthStartRequest(characterName, world, isLalafell));

    internal Task<AuthPollResponse?> PollAsync(string flowId) =>
        PostAsync<AuthPollResponse>("/auth/xivauth/poll", new AuthPollRequest(flowId));

    internal Task<AuthStartResponse?> StartLinkAsync(string bearerToken, string characterName, string world, bool isLalafell) =>
        PostAsync<AuthStartResponse>("/auth/xivauth/link/start", new AuthStartRequest(characterName, world, isLalafell), bearerToken);

    internal Task<AuthPollResponse?> PollLinkAsync(string bearerToken, string flowId) =>
        PostAsync<AuthPollResponse>("/auth/xivauth/link/poll", new AuthPollRequest(flowId), bearerToken);

    internal async Task<bool> SubmitOnboardingAsync(string bearerToken, string[] races, bool wantsToSeeLalafellContent)
    {
        using var http = Http;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            var response = await http.PostAsJsonAsync("/me/onboarding", new OnboardingRequest(races, wantsToSeeLalafellContent)).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Auth] onboarding submit failed: {exception.Message}");
            return false;
        }
    }

    internal async Task<LinkedCharacterDto[]?> GetMyCharactersAsync(string bearerToken)
    {
        using var http = Http;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            var response = await http.GetAsync("/me/characters").ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<LinkedCharacterDto[]>().ConfigureAwait(false)
                : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Auth] fetching linked characters failed: {exception.Message}");
            return null;
        }
    }

    internal async Task RevokeAsync(string bearerToken)
    {
        using var http = Http;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            await http.PostAsync("/auth/token/revoke", null).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Auth] revoke failed: {exception.Message}");
        }
    }

    private async Task<T?> PostAsync<T>(string path, object body, string? bearerToken = null)
    {
        using var http = Http;
        if (bearerToken is not null)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        try
        {
            var response = await http.PostAsJsonAsync(path, body).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<T>().ConfigureAwait(false)
                : default;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Auth] request to {path} failed: {exception.Message}");
            return default;
        }
    }
}
