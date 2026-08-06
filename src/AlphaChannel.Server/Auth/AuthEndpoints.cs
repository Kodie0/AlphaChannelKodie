using AlphaChannel.Contracts;

namespace AlphaChannel.Server.Auth;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        // Fresh sign-in (or re-adding a previously-linked character on a new install) - anonymous,
        // creates or resolves an Account once the XIVAuth device flow completes.
        app.MapPost("/auth/xivauth/start", async (AuthStartRequest request, IXivAuthClient xivAuth, XivAuthFlowStore flows, CancellationToken ct) =>
            await StartFlowAsync(request, xivAuth, flows, linkToAccountId: null, ct));

        app.MapPost("/auth/xivauth/poll", async (AuthPollRequest request, IXivAuthClient xivAuth, XivAuthFlowStore flows, AccountService accounts, CancellationToken ct) =>
            Results.Ok(await PollAsync(request.FlowId, xivAuth, flows, accounts, ct)));

        // Linking an additional character onto the caller's already-signed-in account - Bearer-
        // authed so the target account comes from the caller's own token, never a request field.
        var linkGroup = app.MapGroup("/auth/xivauth/link").AddEndpointFilter<AccountAuthFilter>();

        linkGroup.MapPost("/start", async (AuthStartRequest request, HttpContext context, IXivAuthClient xivAuth, XivAuthFlowStore flows, CancellationToken ct) =>
            await StartFlowAsync(request, xivAuth, flows, linkToAccountId: context.GetAccount().Id, ct));

        linkGroup.MapPost("/poll", async (AuthPollRequest request, IXivAuthClient xivAuth, XivAuthFlowStore flows, AccountService accounts, CancellationToken ct) =>
            Results.Ok(await PollAsync(request.FlowId, xivAuth, flows, accounts, ct)));

        app.MapPost("/auth/token/revoke", async (HttpContext context, AccountService accounts, CancellationToken ct) =>
        {
            var auth = context.Request.Headers.Authorization.ToString();
            var token = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth["Bearer ".Length..] : null;
            if (!string.IsNullOrWhiteSpace(token))
            {
                await accounts.RevokeTokenAsync(token, ct);
            }

            return Results.NoContent();
        });

        app.MapGet("/me", (HttpContext context) =>
        {
            var account = context.GetAccount();
            return Results.Ok(new AccountSummary(account.Id.ToString(), account.Handle, account.DisplayName, account.InviteCode));
        }).AddEndpointFilter<AccountAuthFilter>();

        app.MapPost("/me/onboarding", async (OnboardingRequest request, HttpContext context, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.UpdateOnboardingAsync(context.GetAccount().Id, request.Races, request.WantsToSeeLalafellContent, ct);
            return Results.NoContent();
        }).AddEndpointFilter<AccountAuthFilter>();

        // The one endpoint anywhere that returns a real character name/world - and only ever the
        // caller's own linked characters (see LinkedCharacterDto's doc comment).
        app.MapGet("/me/characters", async (HttpContext context, AccountService accounts, CancellationToken ct) =>
            Results.Ok(await accounts.GetLinkedCharactersAsync(context.GetAccount().Id, ct))).AddEndpointFilter<AccountAuthFilter>();
    }

    private static async Task<IResult> StartFlowAsync(
        AuthStartRequest request, IXivAuthClient xivAuth, XivAuthFlowStore flows, Guid? linkToAccountId, CancellationToken ct)
    {
        try
        {
            var start = await xivAuth.StartDeviceFlowAsync(ct);
            var flowId = flows.Begin(start.DeviceCode, DateTime.UtcNow.AddSeconds(start.ExpiresInSeconds), request.IsLalafell, linkToAccountId);
            return Results.Ok(new AuthStartResponse(flowId, start.UserCode, start.VerificationUri, start.VerificationUriComplete, start.IntervalSeconds, start.ExpiresInSeconds));
        }
        catch (Exception exception)
        {
            // Surfaced in the response (not just server logs) so failures here are diagnosable
            // without SSHing in - this call is entirely to XIVAuth, not user input, so there's no
            // injection/leak concern in echoing it back.
            return Results.Json(new { error = "xivauth_unreachable", message = exception.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<AuthPollResponse> PollAsync(
        string flowId, IXivAuthClient xivAuth, XivAuthFlowStore flows, AccountService accounts, CancellationToken ct)
    {
        flows.SweepExpired();
        if (!flows.TryGet(flowId, out var flow))
        {
            return new AuthPollResponse(AuthPollStatus.Expired, null, null, "Sign-in flow not found or expired.");
        }

        try
        {
            return await PollInnerAsync(flowId, flow, xivAuth, flows, accounts, ct);
        }
        catch (Exception exception)
        {
            // A device_code can only ever be redeemed once - if anything throws here after
            // XIVAuth already accepted the token exchange (e.g. an unexpected /characters
            // response shape), the flow MUST still be removed, or a retry reuses the already-spent
            // device_code and gets a confusing invalid_grant instead of the real error. Learned
            // this the hard way - see XivAuthClient.FetchCharacterAsync's own hardening too.
            flows.Remove(flowId);
            return new AuthPollResponse(AuthPollStatus.Error, null, null, $"Unexpected error: {exception.Message}");
        }
    }

    private static async Task<AuthPollResponse> PollInnerAsync(
        string flowId, XivAuthFlowStore.PendingFlow flow, IXivAuthClient xivAuth, XivAuthFlowStore flows, AccountService accounts, CancellationToken ct)
    {
        var result = await xivAuth.PollAsync(flow.DeviceCode, ct);
        switch (result.Outcome)
        {
            case XivAuthPollOutcome.Pending:
            case XivAuthPollOutcome.SlowDown:
                return new AuthPollResponse(AuthPollStatus.Pending, null, null, null);

            case XivAuthPollOutcome.Denied:
                flows.Remove(flowId);
                return new AuthPollResponse(AuthPollStatus.Denied, null, null, "Sign-in was declined.");

            case XivAuthPollOutcome.Expired:
                flows.Remove(flowId);
                return new AuthPollResponse(AuthPollStatus.Expired, null, null, "Sign-in code expired.");

            case XivAuthPollOutcome.Error:
                flows.Remove(flowId);
                return new AuthPollResponse(AuthPollStatus.Error, null, null, result.ErrorMessage ?? "XIVAuth sign-in failed.");

            case XivAuthPollOutcome.Success:
                flows.Remove(flowId);
                var (account, isNew) = await accounts.FindOrCreateAccountForCharacterAsync(
                    result.CharacterName!, result.World!, flow.IsLalafell, flow.LinkToAccountId, ct);

                if (account.IsBanned && (account.BannedUntilUtc is null || account.BannedUntilUtc > DateTime.UtcNow))
                {
                    return new AuthPollResponse(AuthPollStatus.Banned, null, null, account.BanReason ?? "This account is suspended.");
                }

                var token = await accounts.IssueTokenAsync(account.Id, ct);
                var summary = new AccountSummary(account.Id.ToString(), account.Handle, account.DisplayName, account.InviteCode);
                return new AuthPollResponse(AuthPollStatus.Success, token, summary, null, isNew);

            default:
                flows.Remove(flowId);
                return new AuthPollResponse(AuthPollStatus.Error, null, null, "Unexpected sign-in state.");
        }
    }
}
