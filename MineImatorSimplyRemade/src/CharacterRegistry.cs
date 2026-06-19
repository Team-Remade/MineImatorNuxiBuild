namespace MineImatorSimplyRemade;

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
}

/// <summary>
/// Scans every <c>characters/</c> sub-folder found anywhere inside the
/// application's <c>data/</c> directory and collects all 3-D model files
/// into a flat, sorted registry that the spawn menu can display.
///
/// Supported extensions: <c>.glb .gltf .fbx .obj .dae .3ds .ply .stl .x3d</c>
///
/// Typical layout
/// ──────────────
/// <code>
/// data/
///   minecraft/
///     characters/
///       Steve.glb
///       Alex.glb
///   teamFortress2/
///     characters/
///       heavy/
///         heavy_v2.fbx
/// </code>
/// </summary>
public static class CharacterRegistry
{
    // ── Supported model file extensions ──────────────────────────────────────
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".glb", ".gltf", ".fbx", ".obj", ".dae", ".3ds", ".ply", ".stl", ".x3d"
    };

    // ── Public data ───────────────────────────────────────────────────────────

    /// <summary>All discovered character entries, sorted by name.</summary>
    public static IReadOnlyList<CharacterEntry> Characters => _characters;

    /// <summary>Full path to the <c>data/</c> directory that was scanned.</summary>
    public static string DataRoot { get; private set; } = "";

    // ── Internal state ────────────────────────────────────────────────────────
    private static readonly List<CharacterEntry> _characters = new();

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

            _characters.Add(new CharacterEntry
            {
                Name     = name,
                FilePath = filePath,
                Group    = group
            });
        }
    }
}
