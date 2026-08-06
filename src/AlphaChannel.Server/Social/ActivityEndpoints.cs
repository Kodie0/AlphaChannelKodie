using AlphaChannel.Contracts;
using AlphaChannel.Server.Auth;

namespace AlphaChannel.Server.Social;

internal static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/activity").AddEndpointFilter<AccountAuthFilter>().AddEndpointFilter<LalafellGateFilter>();

        group.MapGet("/", async (long? before, HttpContext context, ActivityService activity, CancellationToken ct) =>
            Results.Ok(await activity.GetFeedAsync(context.GetAccount().Id, before, ct)));

        group.MapPost("/read", async (MarkActivityReadRequest request, HttpContext context, ActivityService activity, CancellationToken ct) =>
        {
            await activity.MarkReadAsync(context.GetAccount().Id, request.UpToUnix, ct);
            return Results.NoContent();
        });
    }
}
