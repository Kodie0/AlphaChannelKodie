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
            Results.Ok(AccountService.ToSummary(context.GetAccount()))).AddEndpointFilter<AccountAuthFilter>();

        app.MapPost("/me/onboarding", async (OnboardingRequest request, HttpContext context, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.UpdateOnboardingAsync(context.GetAccount().Id, request.Races, request.WantsToSeeLalafellContent, ct);
            return Results.NoContent();
        }).AddEndpointFilter<AccountAuthFilter>();

        app.MapPatch("/me", async (UpdateProfileRequest request, HttpContext context, AccountService accounts, CancellationToken ct) =>
        {
            var outcome = await accounts.UpdateProfileAsync(context.GetAccount().Id, request, ct);
            return outcome.Result switch
            {
                UpdateProfileResult.Updated => Results.Ok(outcome.Account),
                UpdateProfileResult.NameTaken => Results.Json(new { reason = "name_taken" }, statusCode: StatusCodes.Status409Conflict),
                UpdateProfileResult.InvalidFormat => Results.Json(new { reason = "invalid_format" }, statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.NotFound(),
            };
        }).AddEndpointFilter<AccountAuthFilter>();

        // Custom profile picture — multipart field "file", max 1 MB, png/jpg/webp. Replaces any
        // previous upload for this account. Icon/color chips remain the fallback while clients load.
        app.MapPost("/me/avatar", async (HttpContext context, AccountService accounts, AvatarStorage storage, CancellationToken ct) =>
        {
            var account = context.GetAccount();
            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new { reason = "expected_multipart" });
            }

            var form = await context.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { reason = "missing_file" });
            }

            if (file.Length > AvatarStorage.MaxBytes)
            {
                return Results.Json(new { reason = "too_large", maxBytes = AvatarStorage.MaxBytes },
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            await using var upload = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await upload.CopyToAsync(buffer, ct);
            if (buffer.Length > AvatarStorage.MaxBytes)
            {
                return Results.Json(new { reason = "too_large", maxBytes = AvatarStorage.MaxBytes },
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            var bytes = buffer.ToArray();
            var extension = AvatarStorage.DetectExtension(bytes.AsSpan(0, Math.Min(16, bytes.Length)), file.FileName);
            if (extension is null)
            {
                return Results.Json(new { reason = "unsupported_type" }, statusCode: StatusCodes.Status415UnsupportedMediaType);
            }

            var fileName = storage.BuildFileName(account.Id, extension);
            foreach (var staleExt in new[] { ".png", ".jpg", ".jpeg", ".webp" })
            {
                storage.DeleteIfExists(storage.BuildFileName(account.Id, staleExt));
            }

            await File.WriteAllBytesAsync(storage.GetFullPath(fileName), bytes, ct);

            var outcome = await accounts.SetAvatarImageAsync(account.Id, fileName, ct);
            return outcome.Result == UpdateProfileResult.Updated
                ? Results.Ok(outcome.Account)
                : Results.NotFound();
        }).AddEndpointFilter<AccountAuthFilter>().DisableAntiforgery();

        app.MapDelete("/me/avatar", async (HttpContext context, AccountService accounts, AvatarStorage storage, CancellationToken ct) =>
        {
            var outcome = await accounts.ClearAvatarImageAsync(context.GetAccount().Id, storage, ct);
            return outcome.Result == UpdateProfileResult.Updated
                ? Results.Ok(outcome.Account)
                : Results.NotFound();
        }).AddEndpointFilter<AccountAuthFilter>();

        // Public read — friends lists and profile popups need this without an extra auth hop.
        // Filenames are account-scoped GUIDs, so guessing others' avatars is impractical at scale.
        app.MapGet("/avatars/{fileName}", (string fileName, AvatarStorage storage) =>
        {
            if (!AvatarStorage.IsSafeFileName(fileName))
            {
                return Results.NotFound();
            }

            var path = storage.GetFullPath(fileName);
            return File.Exists(path)
                ? Results.File(path, AvatarStorage.ContentTypeFor(fileName), enableRangeProcessing: false)
                : Results.NotFound();
        });

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
                return new AuthPollResponse(AuthPollStatus.Success, token, AccountService.ToSummary(account), null, isNew);

            default:
                flows.Remove(flowId);
                return new AuthPollResponse(AuthPollStatus.Error, null, null, "Unexpected sign-in state.");
        }
    }
}
