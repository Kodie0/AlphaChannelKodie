using AlphaChannel.Server.Auth;
using AlphaChannel.Server.Data;

namespace AlphaChannel.Server.Social;

// Applied after AccountAuthFilter on every social route group - blocks a Pending/Denied Lalafell
// account from using friends/DMs/activity at all, with a machine-readable reason the plugin can
// show a clear message for. This is the "ask to be added to the social apps" gate; the separate
// per-viewer HideLalafellFromNonLalafell/WantsToSeeLalafellContent visibility filter (see
// LalafellVisibility) is unrelated and only affects what an already-allowed account sees of others.
//
// Temporarily disabled: the Discord review pipeline (DISCORD_LALAFELL_WEBHOOK_URL) isn't wired up
// yet, so accounts land in Pending with no way for anyone to notice and approve them, which just
// locks testers out. Flagging/status tracking still happens (IsLalafell, LalafellSocialStatus,
// /admin/lalafell endpoints) so nothing needs backfilling once the review flow is finished - this
// just stops it from gating anyone in the meantime.
internal sealed class LalafellGateFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        await next(context);
}
