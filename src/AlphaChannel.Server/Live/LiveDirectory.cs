using System.Collections.Concurrent;

namespace AlphaChannel.Server.Live;

// Tiny in-memory mirror of "who's currently live," kept in sync by LiveService whenever a
// LiveSession opens/closes - lets PresenceLabels.WatchingLabel stay synchronous and DB-free, same
// as everything else it already checks (RoomManager/UserDirectory are both in-memory too). The
// LiveSessions table stays the durable source of truth; this is purely a fast-path cache for the
// hot "what is this account doing right now" query that runs on every friends-list fetch and every
// presence push.
internal sealed class LiveDirectory
{
    private readonly ConcurrentDictionary<string, bool> live = new();

    public void SetLive(string accountId) => live[accountId] = true;

    public void SetOffline(string accountId) => live.TryRemove(accountId, out _);

    public bool IsLive(string accountId) => live.ContainsKey(accountId);
}
