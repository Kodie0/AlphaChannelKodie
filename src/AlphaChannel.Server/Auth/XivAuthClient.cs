using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AlphaChannel.Server.Auth;

internal sealed record XivAuthDeviceStart(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    int ExpiresInSeconds,
    int IntervalSeconds);

internal enum XivAuthPollOutcome
{
    Pending,
    SlowDown,
    Success,
    Denied,
    Expired,
    Error,
}

internal sealed record XivAuthPollResult(
    XivAuthPollOutcome Outcome,
    string? CharacterName = null,
    string? World = null,
    string? ErrorMessage = null);

internal interface IXivAuthClient
{
    Task<XivAuthDeviceStart> StartDeviceFlowAsync(CancellationToken cancellationToken);
    Task<XivAuthPollResult> PollAsync(string deviceCode, CancellationToken cancellationToken);
}

// Device Authorization Flow against XIVAuth, per their "Authenticating to the API" docs:
// - POST {DeviceAuthorizationEndpoint} (/oauth/authorize_device) with client_id + scopes (plural)
//   starts the flow. /oauth/device is a *different* endpoint - the browser page a human visits,
//   never POSTed to by this client directly.
// - POST {TokenEndpoint} (/oauth/token) with grant_type=urn:ietf:params:oauth:grant-type:device_code
//   redeems the device_code - their own curl example sends client_id only, no client_secret, for
//   this specific grant type.
// - The resulting access_token is opaque, NOT a JWT with character claims embedded - character
//   info comes from a separate authenticated GET {CharactersEndpoint} (/characters) call. With the
//   single "character" scope (not "character:all"), that always returns exactly one entry.
internal sealed class XivAuthClient(HttpClient httpClient, XivAuthOptions opts) : IXivAuthClient
{
    public async Task<XivAuthDeviceStart> StartDeviceFlowAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsync(
            opts.DeviceAuthorizationEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = opts.ClientId,
                ["client_secret"] = opts.ClientSecret,
                // XIVAuth's own docs say "scopes" (plural) for this endpoint, but their actual
                // server expects "scope" (singular) - matches RFC 8628's real field name,
                // confirmed empirically since the documented plural form gets invalid_scope back.
                ["scope"] = opts.Scope,
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"XIVAuth device authorization request failed: {(int)response.StatusCode} {response.StatusCode} - {errorBody}");
        }

        var body = await response.Content.ReadFromJsonAsync<DeviceAuthorizationResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("XIVAuth device authorization response was empty.");

        return new XivAuthDeviceStart(
            body.DeviceCode,
            body.UserCode,
            body.VerificationUri,
            body.VerificationUriComplete,
            body.ExpiresIn,
            body.Interval);
    }

    public async Task<XivAuthPollResult> PollAsync(string deviceCode, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsync(
            opts.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode,
                ["client_id"] = opts.ClientId,
                // Their docs' curl example omits this for the device_code grant, but this app is
                // a Confidential Client - same lesson as StartDeviceFlowAsync, confirmed empirically.
                ["client_secret"] = opts.ClientSecret,
            }),
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
            if (token?.AccessToken is null)
            {
                return new XivAuthPollResult(XivAuthPollOutcome.Error, ErrorMessage: "Missing access_token.");
            }

            return await FetchCharacterAsync(token.AccessToken, cancellationToken);
        }

        var error = await response.Content.ReadFromJsonAsync<DeviceErrorResponse>(cancellationToken: cancellationToken);
        return (error?.Error) switch
        {
            "authorization_pending" => new XivAuthPollResult(XivAuthPollOutcome.Pending),
            "slow_down" => new XivAuthPollResult(XivAuthPollOutcome.SlowDown),
            "access_denied" => new XivAuthPollResult(XivAuthPollOutcome.Denied),
            "expired_token" => new XivAuthPollResult(XivAuthPollOutcome.Expired),
            _ => new XivAuthPollResult(XivAuthPollOutcome.Error, ErrorMessage: error?.Error ?? "unknown_error"),
        };
    }

    // Field names (name/world/persistentKey) are best-guess pending a real response to confirm
    // against - PropertyNameCaseInsensitive plus a couple of fallback names per field so a close
    // guess still works. persistent_key is the one field name XIVAuth's docs actually confirm.
    private async Task<XivAuthPollResult> FetchCharacterAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, opts.CharactersEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        // Without this, XIVAuth serves an HTML page instead of JSON for this route - same content
        // negotiation quirk confirmed on /oauth/device during earlier debugging.
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new XivAuthPollResult(XivAuthPollOutcome.Error,
                ErrorMessage: $"GET /characters failed: {(int)response.StatusCode} - {body}");
        }

        List<CharacterResponse>? characters;
        try
        {
            characters = System.Text.Json.JsonSerializer.Deserialize<List<CharacterResponse>>(
                body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException exception)
        {
            // Never crash the whole poll on an unexpected response shape - surface it as a normal
            // error result instead, so a bad/changed API response can't strand a device_code the
            // way the earlier unhandled-exception version did (see the coordinator's note on why).
            return new XivAuthPollResult(XivAuthPollOutcome.Error,
                ErrorMessage: $"GET /characters returned unparseable body: {exception.Message} | body: {body[..Math.Min(body.Length, 300)]}");
        }

        var character = characters?.FirstOrDefault();
        if (character is null || character.Name is null || character.World is null)
        {
            // Logged server-side (not just returned to the caller) so a future API shape change
            // is diagnosable from the server logs directly, without depending on what the plugin's
            // UI can display/wrap.
            Console.Error.WriteLine($"[XivAuth] GET /characters raw body: {body}");
            return new XivAuthPollResult(XivAuthPollOutcome.Error,
                ErrorMessage: "GET /characters returned no usable character.");
        }

        return new XivAuthPollResult(XivAuthPollOutcome.Success, character.Name, character.World);
    }

    private sealed class DeviceAuthorizationResponse
    {
        [JsonPropertyName("device_code")] public required string DeviceCode { get; set; }
        [JsonPropertyName("user_code")] public required string UserCode { get; set; }
        [JsonPropertyName("verification_uri")] public required string VerificationUri { get; set; }
        [JsonPropertyName("verification_uri_complete")] public string? VerificationUriComplete { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; } = 5;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    }

    private sealed class DeviceErrorResponse
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    // Confirmed against a real GET /characters response during live testing:
    // {"persistent_key":"...","lodestone_id":"...","name":"...","home_world":"...","data_center":"...","avatar_url":"..."}
    private sealed class CharacterResponse
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("home_world")] public string? World { get; set; }
        [JsonPropertyName("persistent_key")] public string? PersistentKey { get; set; }
        [JsonPropertyName("lodestone_id")] public string? LodestoneId { get; set; }
    }
}
