using System.Collections.Concurrent;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AlphaChannel.Plugin.Whispers;

internal sealed record WhisperMessage(string CorrespondentKey, string CorrespondentDisplay, bool Mine, string Text, DateTime AtUtc);

// Mirrors native /tell messages into the plugin - deliberately separate from Alpha Chat's
// account/E2E system, since the other party isn't necessarily running AlphaChannel at all.
// History persists per character via WhisperArchive when Configuration.ArchiveWhispersToDisk
// is on (Linkpearl-style), same capture/send path as before.
//
// Read side: IChatGui.ChatMessage. Send side: UIModule.ProcessChatBoxEntry.
internal sealed unsafe class WhisperMirror : IDisposable
{
    internal const int MaxMessageLength = 400;

    internal event Action<WhisperMessage>? OnWhisperMessage;

    private readonly Configuration configuration;
    private readonly WhisperArchive archive;
    private readonly ConcurrentDictionary<string, List<WhisperMessage>> byCorrespondent = new();
    private readonly ConcurrentDictionary<string, string> displayNameByKey = new();
    private readonly ConcurrentDictionary<string, string> worldByKey = new();
    private readonly ConcurrentDictionary<string, DateTime> lastActivityByKey = new();
    private ulong activeContentId;

    internal WhisperMirror(Configuration configuration, string configDirectory)
    {
        this.configuration = configuration;
        archive = new WhisperArchive(configDirectory);
        Plugin.ChatGui.ChatMessage += OnChatMessage;
    }

    internal void SetCharacter(ulong contentId)
    {
        if (contentId == activeContentId)
        {
            return;
        }

        activeContentId = contentId;
        byCorrespondent.Clear();
        displayNameByKey.Clear();
        worldByKey.Clear();
        lastActivityByKey.Clear();
        archive.SetCharacter(contentId);

        if (contentId == 0 || !configuration.ArchiveWhispersToDisk)
        {
            return;
        }

        foreach (var archived in archive.LoadAll())
        {
            displayNameByKey[archived.SendTarget] = archived.Contact;
            var atIndex = archived.SendTarget.LastIndexOf('@');
            if (atIndex > 0 && atIndex < archived.SendTarget.Length - 1)
            {
                worldByKey[archived.SendTarget] = archived.SendTarget[(atIndex + 1)..];
            }

            var list = byCorrespondent.GetOrAdd(archived.SendTarget, _ => []);
            lock (list)
            {
                list.Clear();
                list.AddRange(archived.Lines);
            }

            if (archived.Lines.Count > 0)
            {
                lastActivityByKey[archived.SendTarget] = archived.Lines[^1].AtUtc;
            }
        }
    }

    internal IReadOnlyList<WhisperMessage> GetMessages(string correspondentKey)
    {
        if (!byCorrespondent.TryGetValue(correspondentKey, out var list))
        {
            return [];
        }

        lock (list)
        {
            return list.ToArray();
        }
    }

    // MRU — most recent tell activity first.
    internal string[] GetCorrespondentKeys() =>
        byCorrespondent.Keys
            .OrderByDescending(k => lastActivityByKey.GetValueOrDefault(k, DateTime.MinValue))
            .ThenBy(k => k, StringComparer.Ordinal)
            .ToArray();

    internal string GetDisplayName(string correspondentKey) => displayNameByKey.GetValueOrDefault(correspondentKey, correspondentKey);

    internal DateTime GetLastActivity(string correspondentKey) =>
        lastActivityByKey.GetValueOrDefault(correspondentKey, DateTime.MinValue);

    // False when a World was never captured for this correspondent - same-world tells sometimes
    // render without the PlayerPayload a cross-world one needs, and replying with just a bare name
    // risks hitting a same-named character on the wrong world. The thread view disables Send in
    // that case rather than guess.
    internal bool CanReply(string correspondentKey) => worldByKey.ContainsKey(correspondentKey);

    internal bool TrySendReply(string correspondentKey, string message)
    {
        if (!CanReply(correspondentKey) || message.Length == 0 || message.Length > MaxMessageLength)
        {
            return false;
        }

        var world = worldByKey[correspondentKey];
        var name = GetDisplayName(correspondentKey);
        SendChatCommand($"/tell {name}@{world} {message}");
        return true;
    }

    internal void Remove(string correspondentKey)
    {
        byCorrespondent.TryRemove(correspondentKey, out _);
        displayNameByKey.TryRemove(correspondentKey, out _);
        worldByKey.TryRemove(correspondentKey, out _);
        lastActivityByKey.TryRemove(correspondentKey, out _);
        archive.Delete(correspondentKey);
    }

    private void Persist(string correspondentKey)
    {
        if (!configuration.ArchiveWhispersToDisk || activeContentId == 0)
        {
            return;
        }

        if (!byCorrespondent.TryGetValue(correspondentKey, out var list))
        {
            return;
        }

        WhisperMessage[] snapshot;
        lock (list)
        {
            snapshot = list.ToArray();
        }

        archive.Save(GetDisplayName(correspondentKey), correspondentKey, snapshot);
    }

    // The game itself will echo this back through ChatMessage as a TellOutgoing line once
    // processed, which is what actually appends it to the thread - same "let the real event be the
    // source of truth" reasoning as Alpha Chat not locally faking its own sent-message state.
    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (message.LogKind is not (XivChatType.TellIncoming or XivChatType.TellOutgoing))
        {
            return;
        }

        var playerPayload = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        var name = playerPayload?.PlayerName ?? StripDecoration(message.Sender.TextValue);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string? world = null;
        if (playerPayload is not null)
        {
            try
            {
                world = playerPayload.World.Value.Name.ToString();
            }
            catch
            {
                world = null;
            }
        }

        var key = world is { Length: > 0 } ? $"{name}@{world}" : name;
        displayNameByKey[key] = name;
        if (world is { Length: > 0 })
        {
            worldByKey[key] = world;
        }

        var whisper = new WhisperMessage(key, name, message.LogKind == XivChatType.TellOutgoing, message.Message.TextValue, DateTime.UtcNow);
        var list = byCorrespondent.GetOrAdd(key, _ => []);
        lock (list)
        {
            list.Add(whisper);
            if (list.Count > WhisperArchiveMaxSoftCap)
            {
                list.RemoveRange(0, list.Count - WhisperArchiveMaxSoftCap);
            }
        }

        lastActivityByKey[key] = whisper.AtUtc;
        Persist(key);
        OnWhisperMessage?.Invoke(whisper);
    }

    // Soft in-memory cap matching archive MaxStoredLines so RAM doesn't grow unbounded mid-session.
    private const int WhisperArchiveMaxSoftCap = 500;

    private static string StripDecoration(string raw)
    {
        var trimmed = raw.Trim().TrimStart('➜', '→', ':', ' ');
        return trimmed.StartsWith("To ", StringComparison.Ordinal) ? trimmed[3..].Trim() : trimmed;
    }

    private static void SendChatCommand(string command)
    {
        var utf8 = Utf8String.FromString(command);
        try
        {
            UIModule.Instance()->ProcessChatBoxEntry(utf8);
        }
        finally
        {
            utf8->Dtor(true);
        }
    }

    public void Dispose()
    {
        Plugin.ChatGui.ChatMessage -= OnChatMessage;
    }
}
