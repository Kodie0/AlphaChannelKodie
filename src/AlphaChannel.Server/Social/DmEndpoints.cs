using AlphaChannel.Contracts;
using AlphaChannel.Server.Auth;

namespace AlphaChannel.Server.Social;

internal static class DmEndpoints
{
    public static void MapDmEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/dm").AddEndpointFilter<AccountAuthFilter>().AddEndpointFilter<LalafellGateFilter>();

        group.MapPost("/conversations", async (CreateConversationRequest request, HttpContext context, DmService dm, CancellationToken ct) =>
        {
            var (result, conversationId) = await dm.CreateConversationAsync(context.GetAccount().Id, request.MemberAccountIds, request.Name, ct);
            return result switch
            {
                CreateConversationResult.Ok => Results.Ok(new { conversationId = conversationId!.Value.ToString() }),
                CreateConversationResult.NotFriends => Results.Json(new { reason = "not_friends" }, statusCode: StatusCodes.Status403Forbidden),
                CreateConversationResult.Blocked => Results.Json(new { reason = "blocked" }, statusCode: StatusCodes.Status403Forbidden),
                _ => Results.BadRequest(),
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
