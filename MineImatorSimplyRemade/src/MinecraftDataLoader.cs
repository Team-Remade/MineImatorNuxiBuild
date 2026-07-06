using System.IO.Compression;
using System.Text;

namespace MineImatorSimplyRemade;

/// <summary>
/// Shared loader for Minecraft data roots and external asset containers
/// (resource packs and Java mods).
/// Atlases and registries should use this instead of hard-coding paths.
/// </summary>
public static class MinecraftDataLoader
{
    private const string ResourcePackKeyPrefix = "resourcepack";
    private static string _projectRoot = "";

    public delegate void AssetContainerScanCallback(string rootRelativePath, string containerName, int currentContainer, int totalContainers);

    public sealed class ResourcePackFile
    {
        public string PackName { get; init; } = "";
        public string RelativePath { get; init; } = "";
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }

    public static void SetProjectRoot(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            _projectRoot = "";
            return;
        }

        string fullPath = Path.GetFullPath(projectRoot);
        _projectRoot = Directory.Exists(fullPath) ? fullPath : "";
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

    public static IEnumerable<ResourcePackFile> EnumerateResourcePackFiles(string pathPrefix, string suffix, AssetContainerScanCallback? scanProgress = null)
    {
        string normalizedPrefix = Normalize(pathPrefix);
        string normalizedSuffix = suffix.Contains('.') ? suffix : "." + suffix;

        foreach (ResourcePackFile file in EnumerateAssetContainers(
                     Path.Combine("mods", "resourcepacks"),
                     normalizedPrefix,
                     normalizedSuffix,
                     new[] { ".zip" },
                     scanProgress))
            yield return file;

        foreach (ResourcePackFile file in EnumerateAssetContainers(
                     Path.Combine("mods", "javamods"),
                     normalizedPrefix,
                     normalizedSuffix,
                 new[] { ".jar", ".zip" },
                 scanProgress))
            yield return file;
    }

    private static IEnumerable<ResourcePackFile> EnumerateAssetContainers(
        string rootRelativePath,
        string normalizedPrefix,
        string normalizedSuffix,
        IEnumerable<string> archiveExtensions,
        AssetContainerScanCallback? scanProgress)
    {
        var allowedExt = new HashSet<string>(archiveExtensions, StringComparer.OrdinalIgnoreCase);

        var containers = new List<string>();
        foreach (string root in EnumerateContainerRoots(rootRelativePath))
        {
            if (!Directory.Exists(root))
                continue;

            containers.AddRange(
                Directory
                    .EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase));
        }

        if (containers.Count == 0)
            yield break;

        int totalContainers = containers.Count;
        int currentContainer = 0;

        foreach (string containerPath in containers)
        {
            currentContainer++;
            scanProgress?.Invoke(Normalize(rootRelativePath), Path.GetFileName(containerPath), currentContainer, totalContainers);

            if (Directory.Exists(containerPath))
            {
                foreach (ResourcePackFile file in EnumerateDirectoryPack(containerPath, normalizedPrefix, normalizedSuffix))
                    yield return file;

                continue;
            }

            if (!File.Exists(containerPath))
                continue;

            string ext = Path.GetExtension(containerPath);
            if (!allowedExt.Contains(ext))
                continue;

            foreach (ResourcePackFile file in EnumerateZipPack(containerPath, normalizedPrefix, normalizedSuffix))
                yield return file;
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

    public static bool TryParseTextureAssetPath(
        string relativePath,
        out string assetNamespace,
        out string textureCategory,
        out string textureKey)
    {
        assetNamespace = "";
        textureCategory = "";
        textureKey = "";

        string normalized = Normalize(relativePath);
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Expected format: assets/<namespace>/textures/<category>/<path>.png
        // Path can be a direct file (5 parts) or nested folders (6+ parts).
        if (parts.Length < 5)
            return false;

        if (!string.Equals(parts[0], "assets", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(parts[2], "textures", StringComparison.OrdinalIgnoreCase))
            return false;

        string fileName = parts[^1];
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return false;

        assetNamespace = parts[1].ToLowerInvariant();
        textureCategory = parts[3].ToLowerInvariant();

        string[] keyParts = parts.Skip(4).ToArray();
        if (keyParts.Length == 0)
            return false;

        keyParts[^1] = Path.GetFileNameWithoutExtension(keyParts[^1]);
        textureKey = string.Join('/', keyParts);

        return !string.IsNullOrWhiteSpace(assetNamespace) &&
               !string.IsNullOrWhiteSpace(textureCategory) &&
               !string.IsNullOrWhiteSpace(textureKey);
    }

    public static IReadOnlyList<string> GetAvailableResourcePackIds()
    {
        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        AddContainerIds(ids, Path.Combine("mods", "resourcepacks"), new[] { ".zip" });
        AddContainerIds(ids, Path.Combine("mods", "javamods"), new[] { ".jar", ".zip" });

        return ids.ToList();
    }

    public static IReadOnlyList<string> GetAvailableStandaloneResourcePackIds()
    {
        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        AddContainerIds(ids, Path.Combine("mods", "resourcepacks"), new[] { ".zip" });

        return ids.ToList();
    }

    public static IReadOnlyList<string> GetAvailableJavaModIds()
    {
        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        AddContainerIds(ids, Path.Combine("mods", "javamods"), new[] { ".jar", ".zip" });

        return ids.ToList();
    }

    private static void AddContainerIds(SortedSet<string> ids, string rootRelativePath, IEnumerable<string> allowedExtensions)
    {
        var extSet = new HashSet<string>(allowedExtensions, StringComparer.OrdinalIgnoreCase);

        foreach (string root in EnumerateContainerRoots(rootRelativePath))
        {
            if (!Directory.Exists(root))
                continue;

            foreach (string path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!Directory.Exists(path) && !extSet.Contains(Path.GetExtension(path)))
                    continue;

                string id = GetResourcePackId(name);
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }
        }
    }

    private static IEnumerable<string> EnumerateContainerRoots(string rootRelativePath)
    {
        string baseRoot = Path.Combine(GetBasePath(), rootRelativePath);
        yield return baseRoot;

        if (string.IsNullOrWhiteSpace(_projectRoot))
            yield break;

        string projectRoot = Path.Combine(_projectRoot, rootRelativePath);
        if (string.Equals(
                Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(baseRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        yield return projectRoot;
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}