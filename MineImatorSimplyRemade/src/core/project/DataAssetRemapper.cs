using System.Text.Json;

namespace MineImatorSimplyRemade.core.project;

public sealed class DataAssetRemapRules
{
    // Minecraft paths omit "versions/<version>/" so rules survive version upgrades.
    public Dictionary<string, string> PathPrefixes { get; set; } = new();
}

public sealed class DataAssetRemapper
{
    public const string ReferencePrefix = "data://";
    private static readonly Lazy<DataAssetRemapper> Instance = new(() =>
        new DataAssetRemapper(Path.Combine(MinecraftDataLoader.GetBasePath(), "data")));
    public static DataAssetRemapper Current => Instance.Value;

    public string DataRoot { get; }
    private readonly KeyValuePair<string, string>[] _mappings;

    public DataAssetRemapper(string dataRoot)
    {
        DataRoot = Path.GetFullPath(dataRoot);
        _mappings = LoadMappings();
    }

    public string ToReference(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase) ||
            !Path.IsPathRooted(path))
            return path;

        string fullPath = Path.GetFullPath(path);
        string rootPrefix = Path.TrimEndingDirectorySeparator(DataRoot) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            ? ReferencePrefix + Path.GetRelativePath(DataRoot, fullPath).Replace('\\', '/')
            : path;
    }

    /// <summary>Finds an existing data asset without filename guessing or modifying the source project.</summary>
    public string? Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (File.Exists(path))
            return Path.GetFullPath(path);

        string normalized = path.Replace('\\', '/');
        string relative;
        if (normalized.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase))
            relative = normalized[ReferencePrefix.Length..];
        else if (normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
            relative = normalized[5..];
        else
        {
            int dataIndex = normalized.LastIndexOf("/data/", StringComparison.OrdinalIgnoreCase);
            if (dataIndex < 0)
                return null;
            relative = normalized[(dataIndex + 6)..];
        }

        if (!IsSafeRelativePath(relative))
            return null;

        string? exact = FindFile(DataRoot, relative);
        if (exact != null)
            return exact;

        string logical = relative;
        string? originalVersion = null;
        string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 && parts[0].Equals("minecraft", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals("versions", StringComparison.OrdinalIgnoreCase))
        {
            originalVersion = parts[2];
            logical = "minecraft/" + string.Join('/', parts.Skip(3));
        }

        var candidates = new List<string> { logical };
        foreach (var mapping in _mappings)
        {
            bool matches = mapping.Key.EndsWith('/')
                ? logical.StartsWith(mapping.Key, StringComparison.OrdinalIgnoreCase)
                : logical.Equals(mapping.Key, StringComparison.OrdinalIgnoreCase);
            if (!matches) continue;

            string candidate = mapping.Value + logical[mapping.Key.Length..];
            if (IsSafeRelativePath(candidate))
                candidates.Add(candidate);
        }

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (candidate.StartsWith("minecraft/", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string versionRoot in GetVersionRoots(originalVersion))
                {
                    string? file = FindFile(versionRoot, candidate[10..]);
                    if (file != null)
                        return file;
                }
            }
            else
            {
                string? file = FindFile(DataRoot, candidate);
                if (file != null)
                    return file;
            }
        }

        return null;
    }

    private IEnumerable<string> GetVersionRoots(string? originalVersion)
    {
        string versionsRoot = Path.Combine(DataRoot, "minecraft", "versions");
        if (!Directory.Exists(versionsRoot))
            yield break;

        var roots = new List<string>();
        if (!string.IsNullOrEmpty(originalVersion))
            roots.Add(Path.Combine(versionsRoot, originalVersion));

        string activeRoot = BlockRegistry.VersionRoot;
        if (!string.IsNullOrEmpty(activeRoot) &&
            string.Equals(Path.GetDirectoryName(Path.GetFullPath(activeRoot)), versionsRoot, StringComparison.OrdinalIgnoreCase))
            roots.Add(activeRoot);

        roots.AddRange(Directory.GetDirectories(versionsRoot)
            .OrderByDescending(root => Version.TryParse(Path.GetFileName(root), out var version) ? version : new Version())
            .ThenByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase));

        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(root))
                yield return root;
        }
    }

    private KeyValuePair<string, string>[] LoadMappings()
    {
        string path = Path.Combine(DataRoot, "asset-remaps.json");
        if (!File.Exists(path))
            return [];

        try
        {
            var rules = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.DataAssetRemapRules);
            return (rules?.PathPrefixes ?? new Dictionary<string, string>())
                .Where(pair => IsSafeRelativePath(pair.Key) && IsSafeRelativePath(pair.Value))
                .OrderByDescending(pair => pair.Key.Length)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Could not load data asset remaps: {ex.Message}");
            return [];
        }
    }

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !path.StartsWith('/') &&
        path.IndexOfAny([':', '\\', '\0', '*', '?']) < 0 &&
        !path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part is "." or "..");

    private static string? FindFile(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(path) ? path : null;
    }
}
