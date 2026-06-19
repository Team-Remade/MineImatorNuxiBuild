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
    /// Empty string when <see cref="IsCustom"/> is <c>true</c> (path is chosen at runtime).
    /// </summary>
    public string FilePath { get; init; } = "";

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

    /// <summary>
    /// When <c>true</c> the variant has no fixed file; the user picks one at
    /// spawn time.  <c>"file"</c> may be omitted in the manifest.
    /// </summary>
    [JsonPropertyName("custom")]
    public bool Custom { get; set; } = false;
}

// ── CharacterEntry ────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single character model discovered on disk.
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
/// Scans every <c>characters/</c> sub-folder found anywhere inside the
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

    /// <summary>Full path to the <c>data/</c> directory that was scanned.</summary>
    public static string DataRoot { get; private set; } = "";

    // ── Internal state ────────────────────────────────────────────────────────
    private static readonly List<CharacterEntry> _characters = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Initialization ────────────────────────────────────────────────────────

    /// <summary>
    /// Scans all <c>characters/</c> folders under <c>data/</c> (relative to
    /// the application base directory) and populates <see cref="Characters"/>.
    /// Safe to call multiple times — clears and rebuilds the list each time.
    /// </summary>
    public static void Initialize()
    {
        _characters.Clear();

        // Resolve the data directory relative to the application executable.
        string baseDir  = AppContext.BaseDirectory;
        string dataPath = Path.Combine(baseDir, "data");

        if (!Directory.Exists(dataPath))
        {
            Console.WriteLine($"[CharacterRegistry] data/ directory not found at: {dataPath}");
            return;
        }

        DataRoot = dataPath;

        // Find every directory named "characters" anywhere under data/
        foreach (string charDir in Directory.EnumerateDirectories(
                     dataPath, "characters", SearchOption.AllDirectories))
        {
            ScanCharactersDirectory(charDir);
        }

        _characters.Sort((a, b) =>
            string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"[CharacterRegistry] Found {_characters.Count} character(s) " +
                          $"across all 'characters/' folders.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Recursively enumerates <paramref name="charDir"/> and adds every
    /// supported model file as a <see cref="CharacterEntry"/>.
    /// </summary>
    private static void ScanCharactersDirectory(string charDir)
    {
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

            _characters.Add(new CharacterEntry
            {
                Name            = name,
                FilePath        = filePath,
                Group           = group,
                TextureVariants = variants
            });
        }
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

            var manifest = JsonSerializer.Deserialize<TexturesNuxManifest>(json, _jsonOptions);
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

                if (string.IsNullOrWhiteSpace(entry.File)) continue;
                string texPath = Path.Combine(modelDir, entry.File);
                if (!File.Exists(texPath))
                {
                    Console.WriteLine(
                        $"[CharacterRegistry] textures.nux variant '{entry.Name}' " +
                        $"references missing file: {texPath}");
                    continue;
                }

                result.Add(new CharacterTextureVariant
                {
                    Name     = string.IsNullOrWhiteSpace(entry.Name) ? entry.File : entry.Name,
                    FilePath = texPath
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CharacterRegistry] Failed to parse textures.nux at {manifestPath}: {ex.Message}");
            return Array.Empty<CharacterTextureVariant>();
        }
    }
}
