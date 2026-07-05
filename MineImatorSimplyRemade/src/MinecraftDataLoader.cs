using System.IO.Compression;
using System.Text;

namespace MineImatorSimplyRemade;

/// <summary>
/// Shared loader for Minecraft data roots and resource-pack assets.
/// Atlases and registries should use this instead of hard-coding paths.
/// </summary>
public static class MinecraftDataLoader
{
    private const string ResourcePackKeyPrefix = "resourcepack";

    public sealed class ResourcePackFile
    {
        public string PackName { get; init; } = "";
        public string RelativePath { get; init; } = "";
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }

    public static string GetBasePath()
    {
        return Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    }

    public static string GetVersionRoot(string fallbackVersion = "1.3.2")
    {
        if (!string.IsNullOrWhiteSpace(BlockRegistry.VersionRoot) && Directory.Exists(BlockRegistry.VersionRoot))
            return BlockRegistry.VersionRoot;

        string basePath = GetBasePath();
        string fallback = Path.Combine(basePath, "data", "minecraft", "versions", fallbackVersion);
        if (Directory.Exists(fallback))
            return fallback;

        string versionsDir = Path.Combine(basePath, "data", "minecraft", "versions");
        if (!Directory.Exists(versionsDir))
            return fallback;

        string[] dirs = Directory.GetDirectories(versionsDir);
        if (dirs.Length == 0)
            return fallback;

        return dirs.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase).First();
    }

    public static IEnumerable<ResourcePackFile> EnumerateResourcePackFiles(string pathPrefix, string suffix)
    {
        string normalizedPrefix = Normalize(pathPrefix);
        string normalizedSuffix = suffix.Contains('.') ? suffix : "." + suffix;

        string packsRoot = Path.Combine(GetBasePath(), "mods", "resourcepacks");
        if (!Directory.Exists(packsRoot))
            yield break;

        var packContainers = Directory
            .EnumerateFileSystemEntries(packsRoot, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string containerPath in packContainers)
        {
            if (Directory.Exists(containerPath))
            {
                foreach (ResourcePackFile file in EnumerateDirectoryPack(containerPath, normalizedPrefix, normalizedSuffix))
                    yield return file;

                continue;
            }

            if (File.Exists(containerPath) &&
                string.Equals(Path.GetExtension(containerPath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                foreach (ResourcePackFile file in EnumerateZipPack(containerPath, normalizedPrefix, normalizedSuffix))
                    yield return file;
            }
        }
    }

    private static IEnumerable<ResourcePackFile> EnumerateDirectoryPack(string packDir, string normalizedPrefix, string normalizedSuffix)
    {
        string packName = Path.GetFileName(packDir);
        string[] files = Directory.GetFiles(packDir, "*", SearchOption.AllDirectories)
            .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string filePath in files)
        {
            string rel = Normalize(Path.GetRelativePath(packDir, filePath));
            if (!rel.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!rel.EndsWith(normalizedSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            byte[] data;
            try
            {
                data = File.ReadAllBytes(filePath);
            }
            catch
            {
                continue;
            }

            yield return new ResourcePackFile
            {
                PackName = packName,
                RelativePath = rel,
                Data = data
            };
        }
    }

    private static IEnumerable<ResourcePackFile> EnumerateZipPack(string zipPath, string normalizedPrefix, string normalizedSuffix)
    {
        string packName = Path.GetFileName(zipPath);
        ZipArchive? archive = null;

        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch
        {
            yield break;
        }

        using (archive)
        {
            foreach (ZipArchiveEntry entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(entry.FullName) || entry.FullName.EndsWith('/'))
                    continue;

                string rel = Normalize(entry.FullName);
                if (!rel.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!rel.EndsWith(normalizedSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                byte[] data;
                try
                {
                    using var stream = entry.Open();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    data = ms.ToArray();
                }
                catch
                {
                    continue;
                }

                yield return new ResourcePackFile
                {
                    PackName = packName,
                    RelativePath = rel,
                    Data = data
                };
            }
        }
    }

    public static string DecodeUtf8(byte[] data)
    {
        return Encoding.UTF8.GetString(data);
    }

    public static string GetResourcePackId(string packName)
    {
        if (string.IsNullOrWhiteSpace(packName))
            return "unknown";

        string raw = Path.GetFileNameWithoutExtension(packName).Trim().ToLowerInvariant();
        var sb = new StringBuilder(raw.Length);

        foreach (char ch in raw)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                sb.Append(ch);
                continue;
            }

            if (ch == '_' || ch == '-')
            {
                sb.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                sb.Append('_');
            }
        }

        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    public static string BuildResourcePackTextureKey(string packName, string textureKey)
    {
        string packId = GetResourcePackId(packName);
        return BuildResourcePackTextureKeyFromId(packId, textureKey);
    }

    public static string BuildResourcePackTextureKeyFromId(string packId, string textureKey)
    {
        string normalizedPackId = NormalizeResourcePackId(packId);
        string normalizedKey = Normalize(textureKey);
        return $"{ResourcePackKeyPrefix}:{normalizedPackId}:{normalizedKey}";
    }

    public static string NormalizeResourcePackId(string? packId)
    {
        if (string.IsNullOrWhiteSpace(packId))
            return "";

        string raw = packId.Trim().ToLowerInvariant();
        var sb = new StringBuilder(raw.Length);

        foreach (char ch in raw)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                sb.Append(ch);
                continue;
            }

            if (ch == '_' || ch == '-')
            {
                sb.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                sb.Append('_');
            }
        }

        return sb.ToString();
    }

    public static IReadOnlyList<string> GetAvailableResourcePackIds()
    {
        string packsRoot = Path.Combine(GetBasePath(), "mods", "resourcepacks");
        if (!Directory.Exists(packsRoot))
            return Array.Empty<string>();

        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFileSystemEntries(packsRoot, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!Directory.Exists(path) &&
                !string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            string id = GetResourcePackId(name);
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id);
        }

        return ids.ToList();
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}