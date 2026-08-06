using AlphaChannel.Contracts;
using AlphaChannel.Server.Auth;

namespace AlphaChannel.Server.Social;

internal static class PluginHubEndpoints
{
    public static void MapPluginHubEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/").AddEndpointFilter<AccountAuthFilter>().AddEndpointFilter<LalafellGateFilter>();

        group.MapPut("/me/plugins", async (SyncInstalledPluginsRequest request, HttpContext context, PluginHubService hub, CancellationToken ct) =>
        {
            await hub.SyncAsync(context.GetAccount().Id, request.Plugins, ct);
            return Results.NoContent();
        });

        group.MapGet("/friends/{accountId:guid}/plugins", async (Guid accountId, HttpContext context, PluginHubService hub, CancellationToken ct) =>
        {
            var plugins = await hub.GetFriendPluginsAsync(context.GetAccount().Id, accountId, ct);
            return plugins is null ? Results.NotFound() : Results.Ok(plugins);
        });
    }
}
