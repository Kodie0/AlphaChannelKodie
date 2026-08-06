using AlphaChannel.Contracts;
using AlphaChannel.Server.Auth;

namespace AlphaChannel.Server.Social;

internal static class DmEndpoints
{
    public static void MapDmEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/dm").AddEndpointFilter<AccountAuthFilter>().AddEndpointFilter<LalafellGateFilter>();

        group.MapPost("/conversations/{accountId:guid}", async (Guid accountId, HttpContext context, DmService dm, CancellationToken ct) =>
        {
            var (result, conversationId) = await dm.StartConversationAsync(context.GetAccount().Id, accountId, ct);
            return result switch
            {
                StartConversationResult.Ok => Results.Ok(new { conversationId = conversationId!.Value.ToString() }),
                StartConversationResult.NotFriends => Results.Json(new { reason = "not_friends" }, statusCode: StatusCodes.Status403Forbidden),
                _ => Results.NotFound(),
            };
        });

        group.MapGet("/conversations", async (HttpContext context, DmService dm, CancellationToken ct) =>
            Results.Ok(await dm.GetConversationsAsync(context.GetAccount().Id, ct)));

        group.MapGet("/conversations/{id:guid}/messages", async (Guid id, long? before, HttpContext context, DmService dm, CancellationToken ct) =>
        {
            var page = await dm.GetMessagesAsync(id, context.GetAccount().Id, before, ct);
            return page is null ? Results.NotFound() : Results.Ok(page);
        });

        group.MapPost("/conversations/{id:guid}/messages", async (Guid id, SendMessageRequest request, HttpContext context, DmService dm, CancellationToken ct) =>
        {
            var message = await dm.SendMessageAsync(id, context.GetAccount().Id, request, ct);
            return message is null ? Results.NotFound() : Results.Ok(message);
        });

        group.MapPost("/conversations/{id:guid}/read", async (Guid id, HttpContext context, DmService dm, CancellationToken ct) =>
        {
            await dm.MarkReadAsync(id, context.GetAccount().Id, ct);
            return Results.NoContent();
        });
    }
}
