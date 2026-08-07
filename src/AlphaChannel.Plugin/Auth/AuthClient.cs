using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlphaChannel.Contracts;
using AlphaChannel.Plugin;

namespace AlphaChannel.Plugin.Auth;

internal sealed record UpdateDisplayNameOutcome(AccountSummary? Account, bool NameTaken, bool InvalidFormat);

// Thin REST wrapper around AlphaChannel.Server's /auth/* + /me endpoints. Separate from
// StreamClient (which only ever speaks the /rt websocket) since sign-in is plain request/response,
// not a persistent connection - same split Aetherphone draws between HttpService and RealtimeConnection.
internal sealed class AuthClient(Configuration configuration)
{
    private HttpClient Http => new() { BaseAddress = new Uri(configuration.RelayServerUrl) };

    // Used to pick up server-side changes the client didn't itself just cause - currently only the
    // invite code, which rotates whenever someone else redeems it (see FriendService.
    // RedeemInviteCodeAsync), so the Settings page's copy of it can go stale.
    internal async Task<AccountSummary?> GetMeAsync(string bearerToken)
    {
        using var http = Http;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            var response = await http.GetAsync("/me").ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<AccountSummary>().ConfigureAwait(false)
                : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Auth] fetching /me failed: {exception.Message}");
            return null;
        }
    }

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

    internal Task<UpdateDisplayNameOutcome> UpdateDisplayNameAsync(string bearerToken, string displayName) =>
        UpdateProfileAsync(bearerToken, new UpdateProfileRequest(displayName, null, null, null, null));

    internal async Task<UpdateDisplayNameOutcome> UpdateProfileAsync(string bearerToken, UpdateProfileRequest request)
    {
        using var http = Http;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            var response = await http.PatchAsJsonAsync("/me", request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new UpdateDisplayNameOutcome(await response.Content.ReadFromJsonAsync<AccountSummary>().ConfigureAwait(false), false, false);
            }

            return new UpdateDisplayNameOutcome(null, response.StatusCode == System.Net.HttpStatusCode.Conflict,
                response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity);
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Auth] display name update failed: {exception.Message}");
            return new UpdateDisplayNameOutcome(null, false, false);
        }
    }

    internal async Task<AccountSummary?> UploadAvatarAsync(string bearerToken, string filePath)
    {
        using var http = Http;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            await using var stream = File.OpenRead(filePath);
            using var content = new MultipartFormDataContent();
            var part = new StreamContent(stream);
            part.Headers.ContentType = new MediaTypeHeaderValue(GuessImageMime(filePath));
            content.Add(part, "file", Path.GetFileName(filePath));

            var response = await http.PostAsync("/me/avatar", content).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<AccountSummary>().ConfigureAwait(false)
                : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Auth] avatar upload failed: {exception.Message}");
            return null;
        }
    }

    internal async Task<AccountSummary?> ClearAvatarAsync(string bearerToken)
    {
        using var http = Http;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            var response = await http.DeleteAsync("/me/avatar").ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<AccountSummary>().ConfigureAwait(false)
                : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Auth] avatar clear failed: {exception.Message}");
            return null;
        }
    }

    private static string GuessImageMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };

    // Null covers both "not found" and "not viewable" (not friends) - server returns 404 either
    // way, see FriendService.GetProfileAsync's own doc comment on why those stay indistinguishable.
    internal async Task<AccountProfileDto?> GetProfileAsync(string bearerToken, string accountId)
    {
        using var http = Http;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        try
        {
            var response = await http.GetAsync($"/accounts/{accountId}/profile").ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<AccountProfileDto>().ConfigureAwait(false)
                : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Auth] fetching profile failed: {exception.Message}");
            return null;
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
