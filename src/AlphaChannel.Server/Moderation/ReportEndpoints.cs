using AlphaChannel.Contracts;
using AlphaChannel.Server.Auth;

namespace AlphaChannel.Server.Moderation;

internal static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapPost("/reports", async (SubmitReportRequest request, HttpContext context, ReportService reports, CancellationToken ct) =>
        {
            Guid? targetAccountId = Guid.TryParse(request.TargetAccountId, out var accId) ? accId : null;
            Guid? targetMessageId = Guid.TryParse(request.TargetMessageId, out var msgId) ? msgId : null;

            var id = await reports.SubmitAsync(context.GetAccount().Id, request.Category, request.Note,
                targetAccountId, targetMessageId, request.RevealedBody, request.FrankingKeyBase64, ct);
            return Results.Created($"/reports/{id}", new { id = id.ToString() });
        }).AddEndpointFilter<AccountAuthFilter>();
    }
}
