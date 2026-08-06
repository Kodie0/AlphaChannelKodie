using AlphaChannel.Contracts;
using AlphaChannel.Server.Auth;

namespace AlphaChannel.Server.Social;

internal static class FriendEndpoints
{
    public static void MapFriendEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/").AddEndpointFilter<AccountAuthFilter>().AddEndpointFilter<LalafellGateFilter>();

        // The only account lookup endpoint anywhere - exact-handle match only, no search/browse/
        // autocomplete. See AccountAuthFilter's AddEndpointFilter above: this still requires being
        // signed in yourself, it's just not restricted to already-friends the way /friends is.
        group.MapGet("/accounts/by-handle/{handle}", async (string handle, HttpContext context, FriendService friends, CancellationToken ct) =>
        {
            var account = await friends.FindAccountByHandleAsync(handle, context.GetAccount().Id, ct);
            return account is null
                ? Results.NotFound()
                : Results.Ok(new AccountSummaryDto(account.Id.ToString(), account.Handle, account.DisplayName));
        });

        group.MapGet("/friends", async (HttpContext context, FriendService friends, CancellationToken ct) =>
            Results.Ok(await friends.GetFriendsAsync(context.GetAccount().Id, ct)));

        group.MapGet("/friends/requests", async (HttpContext context, FriendService friends, CancellationToken ct) =>
            Results.Ok(await friends.GetRequestsAsync(context.GetAccount().Id, ct)));

        group.MapPost("/friends/requests", async (SendFriendRequestRequest request, HttpContext context, FriendService friends, CancellationToken ct) =>
        {
            var result = await friends.SendRequestAsync(context.GetAccount().Id, request.Handle, ct);
            return result switch
            {
                SendFriendRequestResult.Sent => Results.Created(),
                SendFriendRequestResult.NotFound => Results.NotFound(),
                _ => Results.Conflict(),
            };
        });

        group.MapPost("/friends/requests/{id:guid}/accept", async (Guid id, HttpContext context, FriendService friends, CancellationToken ct) =>
            await friends.AcceptRequestAsync(id, context.GetAccount().Id, ct) ? Results.Ok() : Results.NotFound());

        group.MapPost("/friends/requests/{id:guid}/decline", async (Guid id, HttpContext context, FriendService friends, CancellationToken ct) =>
            await friends.DeclineRequestAsync(id, context.GetAccount().Id, ct) ? Results.NoContent() : Results.NotFound());

        group.MapDelete("/friends/{accountId:guid}", async (Guid accountId, HttpContext context, FriendService friends, CancellationToken ct) =>
        {
            await friends.RemoveFriendAsync(context.GetAccount().Id, accountId, ct);
            return Results.NoContent();
        });

        group.MapGet("/blocks", async (HttpContext context, FriendService friends, CancellationToken ct) =>
            Results.Ok(await friends.GetBlocksAsync(context.GetAccount().Id, ct)));

        group.MapPost("/blocks/{accountId:guid}", async (Guid accountId, HttpContext context, FriendService friends, CancellationToken ct) =>
        {
            await friends.BlockAsync(context.GetAccount().Id, accountId, ct);
            return Results.NoContent();
        });

        group.MapDelete("/blocks/{accountId:guid}", async (Guid accountId, HttpContext context, FriendService friends, CancellationToken ct) =>
        {
            await friends.UnblockAsync(context.GetAccount().Id, accountId, ct);
            return Results.NoContent();
        });
    }
}
