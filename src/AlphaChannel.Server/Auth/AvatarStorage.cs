namespace AlphaChannel.Server.Auth;

// Disk-backed profile pictures under data/avatars (or AVATAR_STORAGE_PATH). Filenames are
// "{accountId:N}.{ext}" only — never client-supplied paths — so GET /avatars/{file} can't traverse.
internal sealed class AvatarStorage
{
    internal const int MaxBytes = 1024 * 1024; // 1 MB

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp",
    };

    private readonly string root;

    public AvatarStorage(IHostEnvironment env, IConfiguration config)
    {
        root = config["AVATAR_STORAGE_PATH"]
               ?? Path.Combine(env.ContentRootPath, "data", "avatars");
        Directory.CreateDirectory(root);
    }

    public string Root => root;

    public static string? ToPublicUrl(string? fileName) =>
        string.IsNullOrEmpty(fileName) ? null : $"/avatars/{fileName}";

    public static bool IsSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('/') || fileName.Contains('\\')
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var ext = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(ext))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        return Guid.TryParseExact(stem, "N", out _) || Guid.TryParse(stem, out _);
    }

    public static string? DetectExtension(ReadOnlySpan<byte> header, string? declaredFileName)
    {
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
        {
            return ".png";
        }

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }

        if (header.Length >= 12 &&
            header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
            header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            return ".webp";
        }

        var fromName = Path.GetExtension(declaredFileName ?? string.Empty);
        return AllowedExtensions.Contains(fromName) ? fromName.ToLowerInvariant() switch
        {
            ".jpeg" => ".jpg",
            var ext => ext.ToLowerInvariant(),
        } : null;
    }

    public string BuildFileName(Guid accountId, string extension) =>
        $"{accountId:N}{extension.ToLowerInvariant()}";

    public string GetFullPath(string fileName) => Path.Combine(root, fileName);

    public void DeleteIfExists(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !IsSafeFileName(fileName))
        {
            return;
        }

        var path = GetFullPath(fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };
}
