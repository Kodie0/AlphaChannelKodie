using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlphaChannel.Plugin.Whispers;

internal sealed class StoredWhisperLine
{
    [JsonPropertyName("d")]
    public int Direction { get; set; }

    [JsonPropertyName("t")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("u")]
    public long AtUnix { get; set; }
}

internal sealed class StoredWhisperConversation
{
    [JsonPropertyName("contact")]
    public string Contact { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string SendTarget { get; set; } = string.Empty;

    [JsonPropertyName("lines")]
    public List<StoredWhisperLine> Lines { get; set; } = [];
}

internal readonly record struct ArchivedWhisper(
    string Contact,
    string SendTarget,
    IReadOnlyList<WhisperMessage> Lines);

// Per-character /tell history on disk — same shape as Aetherphone's MessageArchive (SHA-256
// filenames, 500-line cap, atomic tmp→replace). Lives under the plugin config directory.
internal sealed class WhisperArchive
{
    private const int MaxStoredLines = 500;
    private readonly object sync = new();
    private readonly DirectoryInfo baseDir;
    private DirectoryInfo? activeRoot;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    internal WhisperArchive(string basePath)
    {
        baseDir = new DirectoryInfo(Path.Combine(basePath, "Whispers"));
        if (!baseDir.Exists)
        {
            baseDir.Create();
        }
    }

    internal void SetCharacter(ulong contentId)
    {
        lock (sync)
        {
            if (contentId == 0)
            {
                activeRoot = null;
                return;
            }

            var directory = new DirectoryInfo(Path.Combine(baseDir.FullName, contentId.ToString("x16")));
            if (!directory.Exists)
            {
                directory.Create();
            }

            activeRoot = directory;
        }
    }

    internal List<ArchivedWhisper> LoadAll()
    {
        DirectoryInfo? root;
        lock (sync)
        {
            root = activeRoot;
        }

        if (root is null || !root.Exists)
        {
            return [];
        }

        var result = new List<ArchivedWhisper>();
        foreach (var file in root.GetFiles("*.json"))
        {
            var stored = TryLoad(file);
            if (stored is null || stored.SendTarget.Length == 0)
            {
                continue;
            }

            var lines = new List<WhisperMessage>(stored.Lines.Count);
            foreach (var line in stored.Lines)
            {
                lines.Add(new WhisperMessage(
                    stored.SendTarget,
                    stored.Contact,
                    line.Direction == 1,
                    line.Text,
                    DateTimeOffset.FromUnixTimeMilliseconds(line.AtUnix).UtcDateTime));
            }

            result.Add(new ArchivedWhisper(stored.Contact, stored.SendTarget, lines));
        }

        result.Sort(static (left, right) => LastActivity(right).CompareTo(LastActivity(left)));
        return result;
    }

    internal void Save(string contact, string sendTarget, IReadOnlyList<WhisperMessage> lines)
    {
        if (sendTarget.Length == 0)
        {
            return;
        }

        var stored = new StoredWhisperConversation { Contact = contact, SendTarget = sendTarget };
        var start = lines.Count > MaxStoredLines ? lines.Count - MaxStoredLines : 0;
        for (var index = start; index < lines.Count; index++)
        {
            var line = lines[index];
            stored.Lines.Add(new StoredWhisperLine
            {
                Direction = line.Mine ? 1 : 0,
                Text = line.Text,
                AtUnix = new DateTimeOffset(line.AtUtc).ToUnixTimeMilliseconds(),
            });
        }

        try
        {
            lock (sync)
            {
                if (activeRoot is null)
                {
                    return;
                }

                var path = PathFor(activeRoot, sendTarget);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(stored, JsonOptions));
                File.Move(temp, path, overwrite: true);
            }
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Whispers] archive write failed for {sendTarget}: {exception.Message}");
        }
    }

    internal void Delete(string sendTarget)
    {
        if (sendTarget.Length == 0)
        {
            return;
        }

        try
        {
            lock (sync)
            {
                if (activeRoot is null)
                {
                    return;
                }

                var path = PathFor(activeRoot, sendTarget);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Whispers] archive delete failed for {sendTarget}: {exception.Message}");
        }
    }

    private static DateTime LastActivity(ArchivedWhisper conversation) =>
        conversation.Lines.Count > 0 ? conversation.Lines[^1].AtUtc : DateTime.MinValue;

    private static StoredWhisperConversation? TryLoad(FileInfo file)
    {
        try
        {
            return JsonSerializer.Deserialize<StoredWhisperConversation>(File.ReadAllText(file.FullName));
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Whispers] archive load failed for {file.Name}: {exception.Message}");
            return null;
        }
    }

    private static string PathFor(DirectoryInfo directory, string sendTarget)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sendTarget.ToLowerInvariant()));
        var builder = new StringBuilder(hash.Length * 2 + 5);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2"));
        }

        builder.Append(".json");
        return Path.Combine(directory.FullName, builder.ToString());
    }
}
