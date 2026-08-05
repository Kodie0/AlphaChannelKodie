using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AetherChannel.Contracts;

namespace AetherChannel.Plugin;

// Rewrite, not a port, of Aetherphone's WatchAlongSession networking half - that one is built
// directly on AethernetSession/CallHub (account auth, a websocket shared with phone calls). This
// talks to AetherChannel.Server's dedicated /rt endpoint instead, with the same stream.* message
// shape (see AetherChannel.Contracts) but none of Aethernet's account/relationship machinery -
// the relay auto-accepts joins for v1 (see the plan's auth note: UserId is self-asserted, not a
// verified identity).
internal enum StreamMode
{
    None,
    Hosting,
    Viewing,
}

internal sealed class StreamClient : IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly Configuration configuration;
    private readonly Func<string?> displayNameProvider;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private ClientWebSocket? socket;
    private Task? runTask;

    internal StreamMode Mode { get; private set; } = StreamMode.None;
    internal string? HostId { get; private set; }
    internal ParticipantInfo[] Roster { get; private set; } = [];
    internal bool IsConnected => socket?.State == WebSocketState.Open;

    internal event Action<StreamControl>? OnState;
    internal event Action? OnJoined;
    internal event Action<string?>? OnDeclined;
    internal event Action? OnEnded;

    // Fired when an admin reset this user's name server-side - the plugin should prompt for a new
    // one again, same as the first-connect flow.
    internal event Action? OnRenameRequired;

    internal StreamClient(Configuration configuration, Func<string?> displayNameProvider)
    {
        this.configuration = configuration;
        this.displayNameProvider = displayNameProvider;
    }

    internal void Start()
    {
        runTask = Task.Run(() => RunAsync(lifetime.Token));
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Authorization", $"Bearer {configuration.UserId}");
                await ws.ConnectAsync(BuildUri(configuration.RelayServerUrl), token).ConfigureAwait(false);
                socket = ws;
                AepLog.Info("[Stream] connected");
                if (displayNameProvider() is { Length: > 0 } name)
                {
                    await SendHelloAsync(name).ConfigureAwait(false);
                }

                await ReceiveLoopAsync(ws, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AepLog.Warning($"[Stream] connection error: {exception.Message}");
            }
            finally
            {
                socket = null;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            await Task.Delay(ReconnectDelay, token).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            using var stream = new MemoryStream();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);
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
                AepLog.Warning($"[Stream] malformed message: {exception.Message}");
                continue;
            }

            if (message is not null)
            {
                Dispatch(message);
            }
        }
    }

    private void Dispatch(StreamControl message)
    {
        switch (message.Type)
        {
            case SignalType.StreamState:
                OnState?.Invoke(message);
                break;
            case SignalType.StreamJoined:
                Mode = StreamMode.Viewing;
                OnJoined?.Invoke();
                break;
            case SignalType.StreamDeclined:
                Mode = StreamMode.None;
                OnDeclined?.Invoke(message.Reason);
                break;
            case SignalType.StreamRoster:
                Roster = message.Participants ?? [];
                break;
            case SignalType.StreamEnded:
                Mode = StreamMode.None;
                HostId = null;
                OnEnded?.Invoke();
                break;

            case SignalType.StreamRenameRequired:
                OnRenameRequired?.Invoke();
                break;
        }
    }

    internal Task SendHelloAsync(string displayName) =>
        SendAsync(new StreamControl { Type = SignalType.StreamHello, DisplayName = displayName });

    internal Task PublishStateAsync(string url, double positionSeconds, bool paused, Vector3? screenPosition,
        float? screenYaw, float? screenScale)
    {
        Mode = StreamMode.Hosting;
        return SendAsync(new StreamControl
        {
            Type = SignalType.StreamState,
            HostId = configuration.UserId,
            Url = url,
            PositionSeconds = positionSeconds,
            Paused = paused,
            ScreenX = screenPosition?.X,
            ScreenY = screenPosition?.Y,
            ScreenZ = screenPosition?.Z,
            ScreenYaw = screenYaw,
            ScreenScale = screenScale,
        });
    }

    internal Task JoinAsync(string hostId)
    {
        HostId = hostId;
        return SendAsync(new StreamControl { Type = SignalType.StreamJoin, HostId = hostId });
    }

    internal async Task LeaveAsync()
    {
        if (Mode == StreamMode.None)
        {
            return;
        }

        await SendAsync(new StreamControl { Type = SignalType.StreamLeave, HostId = HostId }).ConfigureAwait(false);
        Mode = StreamMode.None;
        HostId = null;
        Roster = [];
    }

    private async Task SendAsync(StreamControl message)
    {
        var ws = socket;
        if (ws is not { State: WebSocketState.Open })
        {
            return;
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        await sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await ws.SendAsync(json, WebSocketMessageType.Text, true, lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Stream] send failed: {exception.Message}");
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static Uri BuildUri(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "wss://" + trimmed["https://".Length..];
        }
        else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "ws://" + trimmed["http://".Length..];
        }

        return new Uri(trimmed + "/rt");
    }

    public void Dispose()
    {
        lifetime.Cancel();
        socket?.Dispose();
        lifetime.Dispose();
        sendLock.Dispose();
    }
}
