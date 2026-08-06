using System.Net.WebSockets;
using System.Text.Json;

namespace AlphaChannel.Server;

// Generic replacement for ConnectionHandler's old private SendAsync(WebSocket, StreamControl, ...)
// - reused by both ConnectionHandler (StreamControl) and the Social/* services (SocialControl) via
// UserDirectory.TryGetSocket, so there's exactly one place that knows how to write a JSON frame to
// a socket instead of two copies drifting apart.
internal static class SocketSend
{
    public static async Task SendAsync<T>(WebSocket socket, T message, CancellationToken token)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        try
        {
            await socket.SendAsync(json, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
        }
    }
}
