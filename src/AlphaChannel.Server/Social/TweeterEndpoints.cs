using AlphaChannel.Contracts;
using AlphaChannel.Server.Auth;

namespace AlphaChannel.Server.Social;

internal static class TweeterEndpoints
{
    public static void MapTweeterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/").AddEndpointFilter<AccountAuthFilter>().AddEndpointFilter<LalafellGateFilter>();

        group.MapPost("/posts", async (CreatePostRequest request, HttpContext context, TweeterService tweeter, CancellationToken ct) =>
        {
            var post = await tweeter.CreatePostAsync(context.GetAccount().Id, request.Body, ct);
            return post is null ? Results.BadRequest() : Results.Created($"/posts/{post.Id}", post);
        });

        group.MapDelete("/posts/{id:guid}", async (Guid id, HttpContext context, TweeterService tweeter, CancellationToken ct) =>
            await tweeter.DeletePostAsync(id, context.GetAccount().Id, ct) ? Results.NoContent() : Results.NotFound());

        group.MapGet("/timeline", async (long? before, HttpContext context, TweeterService tweeter, CancellationToken ct) =>
            Results.Ok(await tweeter.GetTimelineAsync(context.GetAccount().Id, before, ct)));

        group.MapGet("/accounts/{accountId:guid}/posts", async (Guid accountId, long? before, HttpContext context, TweeterService tweeter, CancellationToken ct) =>
            Results.Ok(await tweeter.GetAccountPostsAsync(accountId, context.GetAccount().Id, before, ct)));

        group.MapPost("/posts/{id:guid}/like", async (Guid id, HttpContext context, TweeterService tweeter, CancellationToken ct) =>
        {
            await tweeter.LikeAsync(id, context.GetAccount().Id, ct);
            return Results.NoContent();
        });

        group.MapDelete("/posts/{id:guid}/like", async (Guid id, HttpContext context, TweeterService tweeter, CancellationToken ct) =>
        {
            await tweeter.UnlikeAsync(id, context.GetAccount().Id, ct);
            return Results.NoContent();
        });

        group.MapPost("/follows/{accountId:guid}", async (Guid accountId, HttpContext context, TweeterService tweeter, CancellationToken ct) =>
            await tweeter.FollowAsync(context.GetAccount().Id, accountId, ct) ? Results.NoContent() : Results.BadRequest());

        group.MapDelete("/follows/{accountId:guid}", async (Guid accountId, HttpContext context, TweeterService tweeter, CancellationToken ct) =>
        {
            await tweeter.UnfollowAsync(context.GetAccount().Id, accountId, ct);
            return Results.NoContent();
        });

        group.MapGet("/follows/following", async (HttpContext context, TweeterService tweeter, CancellationToken ct) =>
            Results.Ok(await tweeter.GetFollowingAsync(context.GetAccount().Id, ct)));

        group.MapGet("/follows/followers", async (HttpContext context, TweeterService tweeter, CancellationToken ct) =>
            Results.Ok(await tweeter.GetFollowersAsync(context.GetAccount().Id, ct)));
    }
}
