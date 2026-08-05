using System.Net.WebSockets;
using System.Text.Json;
using AlphaChannel.Contracts;

namespace AlphaChannel.Server;

// One instance handles one socket's whole lifetime. hostingRoomId/viewingHostId are locals, not
// fields, so this class itself is stateless and safe to register as a DI singleton - see the plan's
// v1 auth note: userId here is just whatever the client's Authorization: Bearer header claims, no
// verification against a real identity.
internal sealed class ConnectionHandler(RoomManager rooms, UserDirectory directory, ILogger<ConnectionHandler> logger)
{
    public async Task RunAsync(WebSocket socket, string userId, CancellationToken token)
    {
        string? hostingRoomId = null;
        string? viewingHostId = null;
        directory.Connected(userId, socket);

        try
        {
            var buffer = new byte[16 * 1024];
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                stream.Position = 0;
                StreamControl? message;
                try
                {
                    message = JsonSerializer.Deserialize<StreamControl>(stream);
                }
                catch (JsonException exception)
                {
                    logger.LogWarning("malformed message from {UserId}: {Message}", userId, exception.Message);
                    continue;
                }

                if (message is null)
                {
                    continue;
                }

                switch (message.Type)
                {
                    case SignalType.StreamHello when message.DisplayName is { Length: > 0 } name:
                        directory.SetDisplayName(userId, name);
                        break;

                    case SignalType.StreamState:
                        hostingRoomId = userId;
                        var room = rooms.GetOrCreateRoom(userId);
                        room.LastState = message with { HostId = userId };
                        await BroadcastAsync(room, room.LastState, token).ConfigureAwait(false);
                        break;

                    // message.HostId carries the host's typed display name here, not their real
                    // UserId - players never see or type each other's UserId, see UserDirectory.
                    case SignalType.StreamJoin when message.HostId is { Length: > 0 } hostName:
                        if (!directory.TryResolveUserId(hostName, out var resolvedHostId))
                        {
                            await SendAsync(socket, new StreamControl { Type = SignalType.StreamDeclined, Reason = "Host not found." },
                                token).ConfigureAwait(false);
                            break;
                        }

                        var target = rooms.GetOrCreateRoom(resolvedHostId);
                        target.Viewers[userId] = socket;
                        viewingHostId = resolvedHostId;
                        await SendAsync(socket, new StreamControl { Type = SignalType.StreamJoined, HostId = resolvedHostId }, token)
                            .ConfigureAwait(false);
                        if (target.LastState is { } cached)
                        {
                            await SendAsync(socket, cached, token).ConfigureAwait(false);
                        }

                        await BroadcastRosterAsync(target, token).ConfigureAwait(false);
                        break;

                    case SignalType.StreamLeave:
                        if (viewingHostId is { } leaveHostId && rooms.GetRoom(leaveHostId) is { } leaveRoom)
                        {
                            leaveRoom.Viewers.TryRemove(userId, out _);
                            viewingHostId = null;
                            await BroadcastRosterAsync(leaveRoom, token).ConfigureAwait(false);
                        }

                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException exception)
        {
            logger.LogInformation("socket closed for {UserId}: {Message}", userId, exception.Message);
        }
        finally
        {
            directory.Disconnected(userId);

            if (hostingRoomId is not null && rooms.GetRoom(hostingRoomId) is { } ownRoom)
            {
                rooms.RemoveRoom(hostingRoomId);
                await BroadcastAsync(ownRoom, new StreamControl { Type = SignalType.StreamEnded, HostId = hostingRoomId },
                    CancellationToken.None).ConfigureAwait(false);
            }

            if (viewingHostId is not null && rooms.GetRoom(viewingHostId) is { } viewedRoom)
            {
                viewedRoom.Viewers.TryRemove(userId, out _);
                await BroadcastRosterAsync(viewedRoom, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async Task BroadcastAsync(Room room, StreamControl message, CancellationToken token)
    {
        foreach (var viewer in room.Viewers.Values)
        {
            await SendAsync(viewer, message, token).ConfigureAwait(false);
        }
    }

    private async Task BroadcastRosterAsync(Room room, CancellationToken token)
    {
        var roster = new StreamControl
        {
            Type = SignalType.StreamRoster,
            HostId = room.HostUserId,
            Participants = room.Viewers.Keys.Select(id => new ParticipantInfo(id, directory.DisplayNameOrFallback(id))).ToArray(),
        };
        foreach (var viewer in room.Viewers.Values)
        {
            await SendAsync(viewer, roster, token).ConfigureAwait(false);
        }

        // The host isn't in room.Viewers (they're not watching themselves) - push it to them
        // separately so they can see who's actually tuned in.
        if (directory.TryGetSocket(room.HostUserId, out var hostSocket) && hostSocket is not null)
        {
            await SendAsync(hostSocket, roster, token).ConfigureAwait(false);
        }
    }

    private static async Task SendAsync(WebSocket socket, StreamControl message, CancellationToken token)
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
