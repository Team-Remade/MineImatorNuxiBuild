using System.Text.Json;

namespace MineImatorSimplyRemade.core.world;

/// <summary>
/// Vanilla Java default block states (e.g. <c>oak_log</c> → <c>axis=y</c>), generated from the
/// game's <c>blocks.json</c> data report. Since 26.1, chunk palettes store a block in its default
/// state as a bare ID with no properties, so the importer needs these to pick the right variant.
/// </summary>
internal static class JavaBlockStateDefaults
{
    internal const string FileName = "block_state_defaults.json";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> _defaults = new(Load);

    /// <summary>Returns the sorted <c>key=value,...</c> default properties for a vanilla block, if it has any.</summary>
    public static string? Get(string blockId)
    {
        string name = blockId.StartsWith("minecraft:", StringComparison.Ordinal) ? blockId["minecraft:".Length..] : blockId;
        if (name.Contains(':')) return null;
        return _defaults.Value.TryGetValue(name, out string? properties) ? properties : null;
    }

    private static IReadOnlyDictionary<string, string> Load()
    {
        string? path = FindFile();
        if (path == null) return new Dictionary<string, string>();
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in document.RootElement.EnumerateObject())
                if (entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { Length: > 0 } properties)
                    defaults[entry.Name] = properties;
            return defaults;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Failed to load {path}: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    private static string? FindFile()
    {
        string current = Path.Combine(MinecraftDataLoader.GetVersionRoot(), FileName);
        if (File.Exists(current)) return current;

        // Defaults are effectively version-independent, so fall back to any bundled version.
        string versions = Path.Combine(MinecraftDataLoader.GetBasePath(), "data", "minecraft", "versions");
        if (!Directory.Exists(versions)) return null;
        return Directory.GetDirectories(versions)
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(dir => Path.Combine(dir, FileName))
            .FirstOrDefault(File.Exists);
    }
}
