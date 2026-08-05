namespace AetherChannel.Contracts;

// Ported from Aetherphone's Core/Telephony/Contracts/Signals.cs - the stream.* slice only. That
// file multiplexes call.*/chat.*/velvet.*/etc signals onto the same socket because Aetherphone is
// a whole phone; AetherChannel only ever had streaming, so there is nothing else to multiplex.
public static class SignalType
{
    public const string StreamState = "stream.state";
    public const string StreamJoin = "stream.join";
    public const string StreamLeave = "stream.leave";
    public const string StreamJoined = "stream.joined";
    public const string StreamDeclined = "stream.declined";
    public const string StreamRoster = "stream.roster";
    public const string StreamLeft = "stream.left";
    public const string StreamEnded = "stream.ended";

    // Sent by the client right after connecting, carrying its current DisplayName so the server can
    // show real names in rosters instead of raw UserId GUIDs.
    public const string StreamHello = "stream.hello";

    // Server -> client push telling this client its name was cleared by an admin reset and it needs
    // to prompt the player for a new one - see AetherChannel.Server's /admin/reset-username.
    public const string StreamRenameRequired = "stream.renameRequired";
}

// Same flat envelope shape as Aetherphone's CallControl, trimmed to only the fields the stream.*
// signals actually use - nulls are omitted on the wire, unknown fields are ignored, so this stays
// wire-compatible with a server that happens to also speak the fuller Aetherphone dialect.
public sealed record StreamControl
{
    public string Type { get; init; } = string.Empty;
    public string? HostId { get; init; }
    public string? UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? Reason { get; init; }
    public ParticipantInfo[]? Participants { get; init; }

    public string? Url { get; init; }
    public double? PositionSeconds { get; init; }
    public bool? Paused { get; init; }

    // The host's world-anchored screen transform (VideoEngine.ScreenPosition/ScreenYaw/ScreenScale).
    // Omitted entirely if the host's screen isn't active. Every viewer's client applies this to its
    // own local ScreenPainter - there is no shared/networked 3D object.
    public float? ScreenX { get; init; }
    public float? ScreenY { get; init; }
    public float? ScreenZ { get; init; }
    public float? ScreenYaw { get; init; }
    public float? ScreenScale { get; init; }
}

public sealed record ParticipantInfo(string UserId, string DisplayName);
