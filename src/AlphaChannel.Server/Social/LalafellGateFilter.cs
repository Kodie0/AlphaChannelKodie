using AlphaChannel.Server.Auth;
using AlphaChannel.Server.Data;

namespace AlphaChannel.Server.Social;

// Applied after AccountAuthFilter on every social route group - blocks a Pending/Denied Lalafell
// account from using friends/DMs/activity at all, with a machine-readable reason the plugin can
// show a clear message for. This is the "ask to be added to the social apps" gate; the separate
// per-viewer HideLalafellFromNonLalafell/WantsToSeeLalafellContent visibility filter (see
// LalafellVisibility) is unrelated and only affects what an already-allowed account sees of others.
internal sealed class LalafellGateFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var account = context.HttpContext.GetAccount();
        switch (account.LalafellSocialStatus)
        {
            case LalafellSocialStatus.Pending:
                return Results.Json(new { reason = "lalafell_pending" }, statusCode: StatusCodes.Status403Forbidden);
            case LalafellSocialStatus.Denied:
                return Results.Json(new { reason = "lalafell_denied" }, statusCode: StatusCodes.Status403Forbidden);
            default:
                return await next(context);
        }
    }
}
