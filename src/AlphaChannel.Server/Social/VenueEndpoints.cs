using AlphaChannel.Contracts;
using AlphaChannel.Server.Auth;

namespace AlphaChannel.Server.Social;

internal static class VenueEndpoints
{
    public static void MapVenueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/").AddEndpointFilter<AccountAuthFilter>().AddEndpointFilter<LalafellGateFilter>();

        group.MapPost("/venues", async (CreateVenueRequest request, HttpContext context, VenueService venues, CancellationToken ct) =>
        {
            var created = await venues.CreateAsync(context.GetAccount().Id, request, ct);
            return created is null ? Results.BadRequest() : Results.Ok(created);
        });

        group.MapGet("/venues/mine", async (HttpContext context, VenueService venues, CancellationToken ct) =>
            Results.Ok(await venues.GetMineAsync(context.GetAccount().Id, ct)));

        group.MapDelete("/venues/{id:guid}", async (Guid id, HttpContext context, VenueService venues, CancellationToken ct) =>
            await venues.DeleteAsync(context.GetAccount().Id, id, ct) ? Results.NoContent() : Results.NotFound());

        group.MapGet("/friends/{accountId:guid}/venues", async (Guid accountId, HttpContext context, VenueService venues, CancellationToken ct) =>
        {
            var list = await venues.GetFriendVenuesAsync(context.GetAccount().Id, accountId, ct);
            return list is null ? Results.NotFound() : Results.Ok(list);
        });
    }
}
