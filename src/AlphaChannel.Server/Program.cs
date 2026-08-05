using System.Net.WebSockets;
using System.Text.Json;
using AlphaChannel.Contracts;
using AlphaChannel.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddSingleton<UserDirectory>();
builder.Services.AddSingleton<ConnectionHandler>();

var app = builder.Build();
app.UseWebSockets();

// Shared secret for the admin reset endpoint below - set ADMIN_TOKEN in .env. If it's never
// configured the endpoint just always 503s rather than silently accepting no token.
var adminToken = builder.Configuration["ADMIN_TOKEN"];

app.MapGet("/", () => "AlphaChannel relay is running.");

app.Map("/rt", async (HttpContext context, ConnectionHandler handler) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var auth = context.Request.Headers.Authorization.ToString();
    var userId = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth["Bearer ".Length..] : null;
    if (string.IsNullOrWhiteSpace(userId))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await handler.RunAsync(socket, userId, context.RequestAborted);
});

// Operator-only: clears a player's display name and, if they're currently connected, tells their
// client to prompt them for a new one immediately. Not exposed in the plugin UI - this is meant to
// be called by whoever runs the relay (e.g. via curl) if someone picks an abusive name.
app.MapPost("/admin/reset-username/{userId}", async (string userId, HttpContext context, UserDirectory directory) =>
{
    if (string.IsNullOrEmpty(adminToken) || context.Request.Headers["X-Admin-Token"] != adminToken)
    {
        return Results.Unauthorized();
    }

    if (directory.TryResetDisplayName(userId, out var socket) && socket is { State: WebSocketState.Open })
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new StreamControl { Type = SignalType.StreamRenameRequired });
        await socket.SendAsync(json, WebSocketMessageType.Text, true, context.RequestAborted);
    }

    return Results.Ok();
});

app.Run();
