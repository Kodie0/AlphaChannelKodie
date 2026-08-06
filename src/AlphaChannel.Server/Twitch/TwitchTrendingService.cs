using AlphaChannel.Contracts;

namespace AlphaChannel.Server.Twitch;

// Holds the last-fetched trending list so GET /twitch/trending only ever reads a cache, never calls
// Helix per-request - TwitchTrendingRefreshService is the only writer, on its own timer. A plain
// volatile field (not a lock) is enough since the whole list is swapped atomically, never mutated
// in place - the same "single pointer swap" reasoning Plugin.cs's pendingRemoteState field uses.
internal sealed class TwitchTrendingService
{
    private volatile TwitchStreamDto[] current = [];

    internal IReadOnlyList<TwitchStreamDto> Current => current;

    internal void Update(TwitchStreamDto[] streams) => current = streams;
}
