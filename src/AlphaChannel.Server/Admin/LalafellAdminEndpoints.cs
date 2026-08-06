using AlphaChannel.Server.Social;

namespace AlphaChannel.Server.Admin;

internal sealed record HideLalafellSettingRequest(bool HideLalafellFromNonLalafell);

internal static class LalafellAdminEndpoints
{
    public static void MapLalafellAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin").AddEndpointFilter<AdminTokenFilter>();

        group.MapGet("/lalafell/pending", async (LalafellReviewService review, CancellationToken ct) =>
            Results.Ok(await review.GetPendingAsync(ct)));

        group.MapPost("/lalafell/{accountId:guid}/approve", async (Guid accountId, LalafellReviewService review, CancellationToken ct) =>
            await review.ApproveAsync(accountId, ct) ? Results.Ok() : Results.NotFound());

        group.MapPost("/lalafell/{accountId:guid}/deny", async (Guid accountId, LalafellReviewService review, CancellationToken ct) =>
            await review.DenyAsync(accountId, ct) ? Results.Ok() : Results.NotFound());

        group.MapGet("/settings", async (LalafellReviewService review, CancellationToken ct) =>
            Results.Ok(new HideLalafellSettingRequest(await review.GetHideFromNonLalafellAsync(ct))));

        group.MapPost("/settings", async (HideLalafellSettingRequest request, LalafellReviewService review, CancellationToken ct) =>
        {
            await review.SetHideFromNonLalafellAsync(request.HideLalafellFromNonLalafell, ct);
            return Results.Ok();
        });
    }
}
