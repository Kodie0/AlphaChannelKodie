using System.Collections.Concurrent;
using System.Net.WebSockets;
using AlphaChannel.Contracts;

namespace AlphaChannel.Server;

// A room is keyed by its host's UserId and exists only while that host is connected - created on
// the host's first stream.state, torn down when the host's socket closes. No persistence: this is
// the in-memory-only v1 the plan calls for, no database.
internal sealed class Room
{
    public required string HostUserId { get; init; }
    public StreamControl? LastState { get; set; }
    public ConcurrentDictionary<string, WebSocket> Viewers { get; } = new();
}

internal sealed class RoomManager
{
    private readonly ConcurrentDictionary<string, Room> rooms = new();

    public Room GetOrCreateRoom(string hostUserId) =>
        rooms.GetOrAdd(hostUserId, id => new Room { HostUserId = id });

    public Room? GetRoom(string hostUserId) =>
        rooms.GetValueOrDefault(hostUserId);

    public void RemoveRoom(string hostUserId) => rooms.TryRemove(hostUserId, out _);
}
