using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace AlphaChannel.Server;

// In-memory only, like Room - resets on server restart, which just means everyone gets re-prompted
// for a name, no data loss that matters. Tracks every connected user's chosen name (for roster
// display) and their live socket (so an admin reset can push stream.renameRequired immediately
// instead of waiting for them to reconnect).
internal sealed class UserDirectory
{
    private readonly ConcurrentDictionary<string, string> displayNames = new();
    private readonly ConcurrentDictionary<string, WebSocket> sockets = new();

    public void Connected(string userId, WebSocket socket) => sockets[userId] = socket;

    public void Disconnected(string userId) => sockets.TryRemove(userId, out _);

    public void SetDisplayName(string userId, string displayName) => displayNames[userId] = displayName;

    public string DisplayNameOrFallback(string userId) => displayNames.GetValueOrDefault(userId, userId);

    // Returns the socket to push stream.renameRequired to, if the user is currently connected.
    public bool TryResetDisplayName(string userId, out WebSocket? socket)
    {
        displayNames.TryRemove(userId, out _);
        return sockets.TryGetValue(userId, out socket);
    }
}
