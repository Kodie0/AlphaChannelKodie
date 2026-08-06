using System.Collections.Concurrent;

namespace AlphaChannel.Server.Auth;

// Tracks in-flight sign-in attempts between /auth/xivauth/start and /auth/xivauth/poll. In-memory
// only, like Room/UserDirectory - a flow is a few minutes of state at most, losing it on a server
// restart just means the player has to restart the sign-in prompt, no durable data at risk.
internal sealed class XivAuthFlowStore
{
    private readonly ConcurrentDictionary<string, PendingFlow> flows = new();

    public string Begin(string deviceCode, DateTime expiresAtUtc, bool isLalafell, Guid? linkToAccountId)
    {
        var flowId = Guid.NewGuid().ToString("N");
        flows[flowId] = new PendingFlow(deviceCode, expiresAtUtc, isLalafell, linkToAccountId);
        return flowId;
    }

    public bool TryGet(string flowId, out PendingFlow flow) => flows.TryGetValue(flowId, out flow!);

    public void Remove(string flowId) => flows.TryRemove(flowId, out _);

    // Cheap opportunistic sweep, called from the poll endpoint - this is low-traffic enough
    // (a handful of concurrent sign-ins at most) that a dedicated timer would be overkill.
    public void SweepExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (id, flow) in flows)
        {
            if (flow.ExpiresAtUtc < now)
            {
                flows.TryRemove(id, out _);
            }
        }
    }

    internal sealed record PendingFlow(string DeviceCode, DateTime ExpiresAtUtc, bool IsLalafell, Guid? LinkToAccountId);
}
