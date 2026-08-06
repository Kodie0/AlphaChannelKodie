namespace AlphaChannel.Contracts;

// REST contracts for the XIVAuth device-flow sign-in, mirrored from Aetherphone's
// /auth/xivauth/start + /auth/xivauth/poll shape. AlphaChannel's server is the actual OAuth client
// registered with XIVAuth (client_id/secret) - the plugin never talks to XIVAuth directly, it just
// opens a browser to VerificationUri and polls this server, same reasoning Aetherphone has: a
// Dalamud plugin can't receive an OAuth redirect callback.

// Fresh sign-in uses POST /auth/xivauth/start (anonymous). Linking an additional character to an
// already-signed-in account uses the separate POST /auth/xivauth/link/start (Bearer-authed) -
// which account to link into comes from the Authorization header there, never from a client-
// supplied field, so a forged request body can't link a character onto someone else's account.
// IsLalafell is read client-side from the live character model (not verified server-side beyond
// trusting the plugin) - see AlphaChannel.Server's Lalafell review flow for what it gates.
public sealed record AuthStartRequest(string CharacterName, string World, bool IsLalafell = false);

public sealed record AuthStartResponse(
    string FlowId,
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    int IntervalSeconds,
    int ExpiresInSeconds);

public sealed record AuthPollRequest(string FlowId);

public enum AuthPollStatus
{
    Pending,       // normal "still waiting on the user" state during polling
    Success,
    Denied,
    Expired,
    Banned,
    Error,
}

public sealed record AuthPollResponse(
    AuthPollStatus Status,
    string? Token,
    AccountSummary? Account,
    string? ErrorMessage,
    bool IsNewAccount = false);

// Deliberately excludes the verified character name/world - see AlphaChannel.Server.Data.Account's
// doc comment. Callers only ever see Handle/DisplayName for themselves and everyone else.
public sealed record AccountSummary(string AccountId, string Handle, string DisplayName, string InviteCode);

// The one deliberate exception to "real character name/world is never returned to a client" - only
// ever the caller's own linked characters, via GET /me/characters, never anyone else's.
public sealed record LinkedCharacterDto(string CharacterName, string World, bool IsPrimary);

// Asked once at account creation (IsNewAccount on the poll response), also editable later from
// Settings. Races is a free-form self-report, not used for any gating decision by itself.
public sealed record OnboardingRequest(string[] Races, bool WantsToSeeLalafellContent);
