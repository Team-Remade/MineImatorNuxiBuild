using System.Text.Json;
using System.Text.Json.Serialization;

namespace MineImatorSimplyRemade;

// ── Texture variant types ─────────────────────────────────────────────────────

/// <summary>
/// A single selectable texture variant declared in a <c>textures.nux</c> manifest.
/// </summary>
public class CharacterTextureVariant
{
    /// <summary>Display name shown in the spawn-menu dropdown (e.g. "Herobrine").</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Absolute path to the texture PNG on disk.
    /// Empty for model-only variants or when <see cref="IsCustom"/> is <c>true</c>.
    /// </summary>
    public string FilePath { get; init; } = "";

    /// <summary>Optional complete model variant for equipment with multiple textures.</summary>
    public string ModelPath { get; init; } = "";

    /// <summary>
    /// When <c>true</c> this variant has no fixed file; the user picks one via a
    /// file-open dialog at spawn time.  Declared in <c>textures.nux</c> with
    /// <c>"custom": true</c> and no <c>"file"</c> field.
    /// </summary>
    public bool IsCustom { get; init; } = false;
}

/// <summary>
/// Parsed representation of a <c>textures.nux</c> manifest file.
/// </summary>
internal class TexturesNuxManifest
{
    [JsonPropertyName("default")]
    public string Default { get; set; } = "";

    [JsonPropertyName("variants")]
    public List<TexturesNuxVariantEntry> Variants { get; set; } = new();
}

internal class TexturesNuxVariantEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    /// <summary>
    /// When <c>true</c> the variant has no fixed file; the user picks one at
    /// spawn time.  <c>"file"</c> may be omitted in the manifest.
    /// </summary>
    [JsonPropertyName("custom")]
    public bool Custom { get; set; } = false;
}

// ── CharacterEntry ────────────────────────────────────────────────────────────

/// <summary>
/// Represents a character, equipment, or prop model discovered on disk.
/// </summary>
public class CharacterEntry
{
    /// <summary>Display name derived from the file name without extension.</summary>
    public string Name { get; init; } = "";

    /// <summary>Absolute path to the model file.</summary>
    public string FilePath { get; init; } = "";

    /// <summary>
    /// Optional group label (the name of the immediate parent folder inside
    /// the <c>characters</c> directory, or empty if the file sits directly
    /// inside <c>characters/</c>).
    /// </summary>
    public string Group { get; init; } = "";

    /// <summary>
    /// Texture variants loaded from a sibling <c>textures.nux</c> file.
    /// Empty when no manifest exists.  The first entry is always the default texture.
    /// </summary>
    public IReadOnlyList<CharacterTextureVariant> TextureVariants { get; init; } =
        Array.Empty<CharacterTextureVariant>();
}

// ── CharacterRegistry ─────────────────────────────────────────────────────────

/// <summary>
/// Scans every <c>characters/</c>, <c>equipment/</c>, and <c>props/</c> folder inside the
/// application's <c>data/</c> directory and collects all 3-D model files
/// into a flat, sorted registry that the spawn menu can display.
///
/// Supported extensions: <c>.glb .gltf .fbx .obj .dae .3ds .ply .stl .x3d .mimodel</c>
///
/// When a model's directory contains a <c>textures.nux</c> manifest the
/// available texture variants are parsed and stored on <see cref="CharacterEntry.TextureVariants"/>.
///
/// Typical layout
/// ──────────────
/// <code>
/// data/
///   minecraft/
///     characters/
///       steve/
///         steve.mimodel
///         textures.nux
///         steve.png
///         herobrine.png
/// </code>
/// </summary>
public static class CharacterRegistry
{
    // ── Supported model file extensions ──────────────────────────────────────
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".glb", ".gltf", ".fbx", ".obj", ".dae", ".3ds", ".ply", ".stl", ".x3d", ".mimodel"
    };

    // ── Public data ───────────────────────────────────────────────────────────

    /// <summary>All discovered character entries, sorted by name.</summary>
    public static IReadOnlyList<CharacterEntry> Characters => _characters;
    /// <summary>Standalone equipment models, sorted by name.</summary>
    public static IReadOnlyList<CharacterEntry> Equipment => _equipment;
    /// <summary>Special-block and prop models, sorted by name.</summary>
    public static IReadOnlyList<CharacterEntry> Props => _props;

    // ── Internal state ────────────────────────────────────────────────────────
    private static readonly List<CharacterEntry> _characters = new();
    private static readonly List<CharacterEntry> _equipment = new();
    private static readonly List<CharacterEntry> _props = new();



    // ── Initialization ────────────────────────────────────────────────────────

    /// <summary>
    /// Scans model catalog folders under <c>data/</c> (relative to
    /// the application base directory) and populates the three catalogs.
    /// Safe to call multiple times — clears and rebuilds the list each time.
    /// </summary>
    public static void Initialize(Action<float, string>? progress = null)
    {
        _characters.Clear();
        _equipment.Clear();
        _props.Clear();

        progress?.Invoke(0f, "Locating model directories...");

        // Resolve the data directory relative to the application executable.
        // Use Environment.ProcessPath (same as BlockRegistry) so that single-file
        // self-contained publishes find data/ next to the .exe rather than in the
        // temp extraction directory that AppContext.BaseDirectory points to.
        string baseDir  = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string dataPath = Path.Combine(baseDir, "data");

        if (!Directory.Exists(dataPath))
        {
            Console.WriteLine($"data/ directory not found at: {dataPath}");
            return;
        }

        var catalogs = new[]
        {
            (Folder: "characters", Entries: _characters),
            (Folder: "equipment", Entries: _equipment),
            (Folder: "props", Entries: _props)
        };
        var directories = catalogs.SelectMany(catalog =>
                Directory.EnumerateDirectories(dataPath, catalog.Folder, SearchOption.AllDirectories)
                    .Select(path => (Path: path, catalog.Entries)))
            .ToArray();

        int directoryCount = Math.Max(directories.Length, 1);

        for (int i = 0; i < directories.Length; i++)
        {
            string charDir = directories[i].Path;
            ScanCharactersDirectory(charDir, directories[i].Entries);
            string label = Path.GetFileName(Path.GetDirectoryName(charDir) ?? charDir);
            progress?.Invoke((i + 1) / (float)directoryCount, $"Models: {label}");
        }

        foreach (var catalog in catalogs)
            catalog.Entries.Sort((a, b) =>
                string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        string summary = $"Found {_characters.Count} character(s), {_equipment.Count} equipment model(s), {_props.Count} prop(s)";
        Console.WriteLine(summary);
        progress?.Invoke(1f, summary);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Recursively enumerates <paramref name="charDir"/> and adds every
    /// supported model file as a <see cref="CharacterEntry"/>.
    /// </summary>
    private static void ScanCharactersDirectory(string charDir, List<CharacterEntry> entries)
    {
        var variantModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in Directory.EnumerateFiles(
                     charDir, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(filePath);
            if (!SupportedExtensions.Contains(ext))
                continue;

            // Build a group label from the relative path between charDir and
            // the file's directory (empty when the file is directly in charDir).
            string relDir  = Path.GetRelativePath(charDir, Path.GetDirectoryName(filePath)!);
            string group   = relDir == "." ? "" : relDir.Replace(Path.DirectorySeparatorChar, '/');

            string name    = Path.GetFileNameWithoutExtension(filePath);
            string modelDir = Path.GetDirectoryName(filePath)!;

            // Try to load texture variants from a sibling textures.nux manifest.
            var variants = LoadTextureVariants(modelDir);
            foreach (var variant in variants.Skip(1))
                if (!string.IsNullOrEmpty(variant.ModelPath))
                    variantModels.Add(Path.GetFullPath(variant.ModelPath));

            entries.Add(new CharacterEntry
            {
                Name            = name,
                FilePath        = filePath,
                Group           = group,
                TextureVariants = variants
            });
        }

        // Baked variants belong in their parent's dropdown, not in the model catalog.
        entries.RemoveAll(entry => variantModels.Contains(Path.GetFullPath(entry.FilePath)));
    }

    /// <summary>
    /// Reads a <c>textures.nux</c> file from <paramref name="modelDir"/> (if
    /// present) and returns the list of texture variants with absolute paths.
    /// Returns an empty array when no manifest exists or parsing fails.
    /// </summary>
    private static IReadOnlyList<CharacterTextureVariant> LoadTextureVariants(string modelDir)
    {
        string manifestPath = Path.Combine(modelDir, "textures.nux");
        if (!File.Exists(manifestPath))
            return Array.Empty<CharacterTextureVariant>();

        try
        {
            string json = File.ReadAllText(manifestPath);
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<CharacterTextureVariant>();

            var manifest = JsonSerializer.Deserialize(json, AppJsonContext.Default.TexturesNuxManifest);
            if (manifest == null || manifest.Variants.Count == 0)
                return Array.Empty<CharacterTextureVariant>();

            var result = new List<CharacterTextureVariant>(manifest.Variants.Count);
            foreach (var entry in manifest.Variants)
            {
                // Custom variant: no file required — user picks one at spawn time.
                if (entry.Custom)
                {
                    result.Add(new CharacterTextureVariant
                    {
                        Name     = string.IsNullOrWhiteSpace(entry.Name) ? "Custom" : entry.Name,
                        FilePath = "",
                        IsCustom = true
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.File) && string.IsNullOrWhiteSpace(entry.Model))
                    continue;
                string texPath = string.IsNullOrWhiteSpace(entry.File)
                    ? "" : Path.GetFullPath(Path.Combine(modelDir, entry.File));
                string modelPath = string.IsNullOrWhiteSpace(entry.Model)
                    ? "" : Path.GetFullPath(Path.Combine(modelDir, entry.Model));
                if ((!string.IsNullOrEmpty(texPath) && !File.Exists(texPath)) ||
                    (!string.IsNullOrEmpty(modelPath) &&
                     (!File.Exists(modelPath) || !SupportedExtensions.Contains(Path.GetExtension(modelPath)))))
                {
                    Console.WriteLine(
                        $"textures.nux variant '{entry.Name}' " +
                        $"references a missing or unsupported file: {texPath} {modelPath}");
                    continue;
                }

                result.Add(new CharacterTextureVariant
                {
                    Name     = string.IsNullOrWhiteSpace(entry.Name)
                        ? (string.IsNullOrEmpty(entry.Model) ? entry.File : entry.Model) : entry.Name,
                    FilePath = texPath,
                    ModelPath = modelPath
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to parse textures.nux at {manifestPath}: {ex.Message}");
            return [];
        }
    }
}
