using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade;

// ── Data models ───────────────────────────────────────────────────────────────

/// <summary>
/// Represents one variant entry from a blockstate JSON file.
/// A variant corresponds to a specific block state (e.g. "axis=y") and
/// points to a model with optional rotation overrides.
/// </summary>
public class BlockVariantEntry
{
    /// <summary>Display name for the UI (e.g. "axis=y" or "snowy=false").</summary>
    public string VariantKey { get; init; } = "";

    /// <summary>Fully-resolved model path, e.g. "minecraft:block/oak_log".</summary>
    public string ModelPath { get; init; } = "";

    /// <summary>Optional X-rotation from the blockstate (0, 90, 180, 270).</summary>
    public int RotationX { get; init; }

    /// <summary>Optional Y-rotation from the blockstate (0, 90, 180, 270).</summary>
    public int RotationY { get; init; }

    /// <summary>
    /// Absolute path to a CEM <c>.jem</c> file, or empty string if this variant
    /// uses a standard block model instead of an entity model.
    /// When set, <see cref="SpawnMenu"/> will use <see cref="CemLoader"/> instead
    /// of <see cref="MinecraftModelMesh"/>.
    /// </summary>
    public string CemPath { get; init; } = "";

    /// <summary>
    /// For multi-part blocks (doors, beds), this holds the second-part variant.
    /// When non-null, its meshes are baked into the same object offset by
    /// <see cref="PartOffsetX"/>, <see cref="PartOffsetY"/>, <see cref="PartOffsetZ"/>.
    /// </summary>
    public BlockVariantEntry? TopHalf { get; init; }

    /// <summary>World-unit offset applied to <see cref="TopHalf"/> meshes.</summary>
    public float PartOffsetX { get; init; } = 0f;
    public float PartOffsetY { get; init; } = 1f; // doors: head is 1 block above
    public float PartOffsetZ { get; init; } = 0f;
}

/// <summary>
/// Resolved, flattened Minecraft block model — the result of walking the
/// parent chain and merging texture and element data.
/// </summary>
public class ResolvedBlockModel
{
    /// <summary>Resolved texture map: slot name → texture key (e.g. "grass_block_top").</summary>
    public Dictionary<string, string> Textures { get; } = new();

    /// <summary>Geometry elements parsed from the model JSON.</summary>
    public List<BlockModelElement> Elements { get; } = new();

    /// <summary>
    /// Custom texture atlas size declared by the model's <c>texture_size</c> field.
    /// Standard block models use [16,16] (default); models with larger atlases (e.g.
    /// bed) declare the actual pixel dimensions so UVs can be normalised correctly.
    /// </summary>
    public float TextureSizeX { get; set; } = 16f;
    public float TextureSizeY { get; set; } = 16f;
}

/// <summary>One cuboid element inside a block model JSON.</summary>
public class BlockModelElement
{
    public float[] From { get; set; } = { 0, 0, 0 };
    public float[] To   { get; set; } = { 16, 16, 16 };

    /// <summary>Optional per-element rotation.</summary>
    public ElementRotation? Rotation { get; set; }

    /// <summary>Face definitions keyed by face name (down/up/north/south/west/east).</summary>
    public Dictionary<string, BlockModelFace> Faces { get; } = new();
}

/// <summary>Optional rotation applied to a single element.</summary>
public class ElementRotation
{
    public float[] Origin { get; set; } = { 8, 8, 8 };
    public string  Axis   { get; set; } = "y";
    public float   Angle  { get; set; }
    public bool    Rescale{ get; set; }
}

/// <summary>One face of a cuboid element.</summary>
public class BlockModelFace
{
    /// <summary>UV rectangle [u1, v1, u2, v2] in 0–16 space. Null = use face default.</summary>
    public float[]? Uv        { get; set; }
    public string   Texture   { get; set; } = "";
    public string?  Cullface  { get; set; }
    public int      TintIndex { get; set; } = -1;
    public int      Rotation  { get; set; } = 0;
}

// ── NUX manifest ──────────────────────────────────────────────────────────────

internal class NuxManifest
{
    [JsonProperty("version")] public string Version { get; set; } = "";
    [JsonProperty("name")]    public string Name    { get; set; } = "";
    [JsonProperty("revision")]public int    Revision{ get; set; }
}

// ── BlockRegistry ─────────────────────────────────────────────────────────────

/// <summary>
/// Loads all block data from the most recent <c>.nux</c> version folder found
/// under <c>data/minecraft/versions/</c>.
///
/// After <see cref="Initialize"/> is called:
/// <list type="bullet">
///   <item><see cref="Blocks"/> — sorted list of block names (e.g. "grass_block")</item>
///   <item><see cref="GetVariants"/> — variants for a given block name</item>
///   <item><see cref="ResolveModel"/> — fully-resolved model for a given model path</item>
/// </list>
/// </summary>
public static class BlockRegistry
{
    // ── Public data ───────────────────────────────────────────────────────────

    /// <summary>All block names discovered from blockstate files, sorted.</summary>
    public static IReadOnlyList<string> Blocks => _blocks;

    /// <summary>The resolved version string (e.g. "1.3.2").</summary>
    public static string LoadedVersion { get; private set; } = "";

    /// <summary>Root path of the loaded version folder.</summary>
    public static string VersionRoot { get; private set; } = "";

    // ── Private state ─────────────────────────────────────────────────────────

    private static readonly List<string> _blocks = new();

    /// <summary>Block name → list of variant entries.</summary>
    private static readonly Dictionary<string, List<BlockVariantEntry>> _variants = new();

    /// <summary>Raw parsed model JSON cache (model path → JObject).</summary>
    private static readonly Dictionary<string, JObject?> _rawModelCache = new();

    /// <summary>Resolved model cache (model path → resolved).</summary>
    private static readonly Dictionary<string, ResolvedBlockModel> _resolvedCache = new();

    // ── Initialization ────────────────────────────────────────────────────────

    /// <summary>
    /// Scans <c>data/minecraft/versions/</c> for <c>.nux</c> files, picks the
    /// most recent one by revision (then name), and loads all blockstates.
    /// Safe to call multiple times; subsequent calls re-load from scratch.
    /// </summary>
    public static void Initialize()
    {
        _blocks.Clear();
        _variants.Clear();
        _rawModelCache.Clear();
        _resolvedCache.Clear();

        string basePath    = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string versionsDir = Path.Combine(basePath, "data", "minecraft", "versions");

        if (!Directory.Exists(versionsDir))
        {
            Console.WriteLine($"[BlockRegistry] Versions directory not found: {versionsDir}");
            return;
        }

        // ── Find the most recent .nux file ────────────────────────────────────
        string? versionRoot = FindBestVersionRoot(versionsDir);
        if (versionRoot == null)
        {
            Console.WriteLine("[BlockRegistry] No valid version found.");
            return;
        }

        VersionRoot   = versionRoot;
        LoadedVersion = Path.GetFileName(versionRoot);
        Console.WriteLine($"[BlockRegistry] Loading version '{LoadedVersion}' from {versionRoot}");

        BuildExtraVariants();

        // ── Load blockstates ──────────────────────────────────────────────────
        string blockstatesDir = Path.Combine(versionRoot, "blockstates");
        if (!Directory.Exists(blockstatesDir))
        {
            Console.WriteLine($"[BlockRegistry] blockstates directory not found: {blockstatesDir}");
            return;
        }

        foreach (string file in Directory.GetFiles(blockstatesDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            string blockName = Path.GetFileNameWithoutExtension(file);
            try
            {
                ParseBlockstate(blockName, file);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BlockRegistry] Error parsing blockstate '{blockName}': {ex.Message}");
            }
        }

        _blocks.AddRange(_variants.Keys.OrderBy(k => k));
        Console.WriteLine($"[BlockRegistry] Loaded {_blocks.Count} blocks.");
    }

    // ── Public query API ──────────────────────────────────────────────────────

    /// <summary>Returns the variant list for a block, or an empty list.</summary>
    public static IReadOnlyList<BlockVariantEntry> GetVariants(string blockName)
    {
        return _variants.TryGetValue(blockName, out var list) ? list : Array.Empty<BlockVariantEntry>();
    }

    /// <summary>
    /// Resolves a model path (e.g. "minecraft:block/grass_block") to a
    /// <see cref="ResolvedBlockModel"/> by walking the parent chain.
    /// Returns null if the model cannot be found.
    /// </summary>
    public static ResolvedBlockModel? ResolveModel(string modelPath)
    {
        string normalized = NormalizeModelPath(modelPath);
        if (_resolvedCache.TryGetValue(normalized, out var cached))
            return cached;

        var resolved = BuildResolvedModel(normalized, new HashSet<string>());
        if (resolved != null)
            _resolvedCache[normalized] = resolved;
        return resolved;
    }

    // ── Extra variants ────────────────────────────────────────────────────────

    /// <summary>
    /// Additional variants injected after blockstate parsing, keyed by block name.
    /// Used to surface CEM models (e.g. large chests) that have no corresponding
    /// blockstate variant in the JSON files.
    /// Populated lazily inside <see cref="BuildExtraVariants"/> once
    /// <see cref="VersionRoot"/> is known.
    /// </summary>
    private static readonly Dictionary<string, List<BlockVariantEntry>> _extraVariants = new();

    /// <summary>
    /// Blocks whose parsed variants should be fully replaced by <see cref="_extraVariants"/>
    /// (rather than appended to them).
    /// </summary>
    private static readonly HashSet<string> _replaceVariants = new();

    private static void BuildExtraVariants()
    {
        _extraVariants.Clear();
        _replaceVariants.Clear();

        void AddCemVariant(string blockName, string variantKey, string cemStem)
        {
            string cemDir  = Path.Combine(VersionRoot, "optifine", "cem");
            string cemFile = Path.Combine(cemDir, cemStem + ".jem");
            if (!File.Exists(cemFile)) return;

            if (!_extraVariants.TryGetValue(blockName, out var list))
            {
                list = new List<BlockVariantEntry>();
                _extraVariants[blockName] = list;
            }

            list.Add(new BlockVariantEntry
            {
                VariantKey = variantKey,
                ModelPath  = "",
                CemPath    = cemFile
            });
        }

        // Large chest variants on chest + trapped_chest
        AddCemVariant("chest",         "large",         "chest_large");
        AddCemVariant("trapped_chest", "large",         "trapped_chest_large");

        // Bed: pair foot + head as a single two-block object (head is +1 in Z).
        // Replace the useless "minecraft:block/bed" default variant entirely.
        AddBedVariant("red_bed");
        _replaceVariants.Add("red_bed");
    }

    private static void AddBedVariant(string blockName)
    {
        // Foot and head share the same texture: minecraft:entity/bed/red
        // blockName is e.g. "red_bed" → models are "red_bed_foot" and "red_bed_head"
        string footModel = $"minecraft:block/{blockName}_foot";
        string headModel = $"minecraft:block/{blockName}_head";

        var foot = new BlockVariantEntry
        {
            VariantKey = "default",
            ModelPath  = footModel,
        };

        var head = new BlockVariantEntry
        {
            VariantKey = "head",
            ModelPath  = headModel,
        };

        var combined = new BlockVariantEntry
        {
            VariantKey  = "default",
            ModelPath   = footModel,   // foot at Z=0
            TopHalf     = head,
            PartOffsetX = 0f,
            PartOffsetY = 0f,
            PartOffsetZ = 1f           // head (pillow) at Z=+1
        };

        if (!_extraVariants.TryGetValue(blockName, out var list))
        {
            list = new List<BlockVariantEntry>();
            _extraVariants[blockName] = list;
        }
        list.Add(combined);
    }

    // ── CEM mapping ───────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a block model path (normalised, no namespace) to the CEM .jem filename
    /// (without extension) that should be used instead of a block model JSON.
    /// Key = normalised model path (e.g. "block/chest").
    /// Value = .jem filename stem (e.g. "chest").
    /// </summary>
    private static readonly Dictionary<string, string> CemModelMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "block/chest",           "chest"              },
        { "block/trapped_chest",   "trapped_chest"      },
        { "block/ender_chest",     "ender_chest"        },
        { "block/chest_large",     "chest_large"        },
        { "block/trapped_chest_large", "trapped_chest_large" },
        { "block/locked_chest",    "chest"              }, // locked chest reuses normal chest model
    };

    /// <summary>
    /// Returns the absolute path to the CEM .jem file for <paramref name="normalizedModelPath"/>,
    /// or null if no CEM override is registered.
    /// </summary>
    private static string? GetCemPath(string normalizedModelPath)
    {
        if (!CemModelMap.TryGetValue(normalizedModelPath, out string? stem)) return null;
        string cemDir = Path.Combine(VersionRoot, "optifine", "cem");
        string cemFile = Path.Combine(cemDir, stem + ".jem");
        return File.Exists(cemFile) ? cemFile : null;
    }

    // ── Private parsing helpers ───────────────────────────────────────────────

    private static string? FindBestVersionRoot(string versionsDir)
    {
        string[] nuxFiles = Directory.GetFiles(versionsDir, "*.nux", SearchOption.TopDirectoryOnly);
        if (nuxFiles.Length == 0)
        {
            // Fall back: look for any subdirectory
            string[] subDirs = Directory.GetDirectories(versionsDir);
            if (subDirs.Length > 0)
                return subDirs.OrderByDescending(d => d).First();
            return null;
        }

        // Parse all manifests and pick the highest revision (tie-break: alphabetically last name)
        NuxManifest? best = null;
        string?      bestFolder = null;

        foreach (string nuxFile in nuxFiles)
        {
            try
            {
                string text = File.ReadAllText(nuxFile);
                var manifest = JsonConvert.DeserializeObject<NuxManifest>(text);
                if (manifest == null) continue;

                // The version folder has the same name as the .nux file (minus extension)
                string folderName = Path.GetFileNameWithoutExtension(nuxFile);
                string candidate  = Path.Combine(versionsDir, folderName);
                if (!Directory.Exists(candidate)) continue;

                if (best == null ||
                    manifest.Revision > best.Revision ||
                    (manifest.Revision == best.Revision &&
                     string.Compare(manifest.Name, best.Name, StringComparison.OrdinalIgnoreCase) > 0))
                {
                    best       = manifest;
                    bestFolder = candidate;
                }
            }
            catch { /* skip malformed manifests */ }
        }

        return bestFolder;
    }

    private static void ParseBlockstate(string blockName, string filePath)
    {
        string text = File.ReadAllText(filePath);
        var root    = JObject.Parse(text);

        var variantList = new List<BlockVariantEntry>();

        if (root["variants"] is JObject variantsObj)
        {
            foreach (var prop in variantsObj.Properties())
            {
                string variantKey = prop.Name; // e.g. "axis=y", "snowy=false", ""
                ParseVariantToken(variantKey, prop.Value, variantList);
            }
        }
        else if (root["multipart"] is JArray multipartArr)
        {
            // Multipart blockstates (fences, glass panes, walls, redstone, etc.)
            // Each part has an "apply" field with a model (and optional rotation).
            // We collect all unique model+rotation combinations as individual variants
            // so the user can select any part to preview.
            var seen = new HashSet<string>();
            foreach (JToken part in multipartArr)
            {
                if (part is not JObject partObj) continue;
                JToken? applyToken = partObj["apply"];
                if (applyToken == null) continue;

                // "apply" can be a single object or an array
                IEnumerable<JObject> applies = applyToken is JArray applyArr
                    ? applyArr.OfType<JObject>()
                    : applyToken is JObject applyObj ? new[] { applyObj } : Enumerable.Empty<JObject>();

                foreach (JObject apply in applies)
                {
                    string model = apply["model"]?.Value<string>() ?? "";
                    if (string.IsNullOrEmpty(model)) continue;

                    int rx = apply["x"]?.Value<int>() ?? 0;
                    int ry = apply["y"]?.Value<int>() ?? 0;

                    // Derive a readable variant key from the model name + rotation
                    string modelShort = model.Contains(':') ? model[(model.IndexOf(':') + 1)..] : model;
                    if (modelShort.StartsWith("block/")) modelShort = modelShort["block/".Length..];
                    string dedupeKey = $"{model}|{rx}|{ry}";
                    if (!seen.Add(dedupeKey)) continue;

                    string variantKey = modelShort;
                    if (rx != 0 || ry != 0)
                        variantKey += $" (x{rx} y{ry})";

                    string cemPath = GetCemPath(NormalizeModelPath(model));

                    variantList.Add(new BlockVariantEntry
                    {
                        VariantKey = variantKey,
                        ModelPath  = model,
                        RotationX  = rx,
                        RotationY  = ry,
                        CemPath    = cemPath ?? ""
                    });
                }
            }

            // If no parts could be parsed, fall back to a named placeholder
            if (variantList.Count == 0)
            {
                variantList.Add(new BlockVariantEntry
                {
                    VariantKey = "default",
                    ModelPath  = "",
                    RotationX  = 0,
                    RotationY  = 0
                });
            }
        }

        // Merge (or replace) extra variants registered for this block
        if (_extraVariants.TryGetValue(blockName, out var extras))
        {
            if (_replaceVariants.Contains(blockName))
                variantList = extras.ToList();
            else
                variantList.AddRange(extras);
        }

        // Compress two-block-tall variants (doors): pair lower+upper halves
        var compressed = CompressTwoBlockVariants(variantList);

        if (compressed.Count > 0)
            _variants[blockName] = compressed;
    }

    /// <summary>
    /// Detects blockstate variant lists that use <c>half=lower</c> / <c>half=upper</c>
    /// (door-style two-block-tall blocks) and merges each lower variant with its
    /// matching upper variant.  The resulting list contains only the lower-half
    /// entries with <see cref="BlockVariantEntry.TopHalf"/> populated, keyed by
    /// a display name that omits the <c>half=</c> property.
    ///
    /// If no <c>half=</c> properties are found the original list is returned unchanged.
    /// </summary>
    private static List<BlockVariantEntry> CompressTwoBlockVariants(List<BlockVariantEntry> variants)
    {
        // Only compress when all variants carry a half= property
        bool hasHalf = variants.Any(v =>
            v.VariantKey.Contains("half=lower", StringComparison.OrdinalIgnoreCase) ||
            v.VariantKey.Contains("half=upper", StringComparison.OrdinalIgnoreCase));

        if (!hasHalf) return variants;

        // Separate into lower and upper buckets, keyed by the state without half=
        var lowers = new Dictionary<string, BlockVariantEntry>();
        var uppers = new Dictionary<string, BlockVariantEntry>();

        foreach (var v in variants)
        {
            string key = v.VariantKey;
            if (key.Contains("half=lower", StringComparison.OrdinalIgnoreCase))
            {
                string baseKey = StripHalfProperty(key);
                lowers[baseKey] = v;
            }
            else if (key.Contains("half=upper", StringComparison.OrdinalIgnoreCase))
            {
                string baseKey = StripHalfProperty(key);
                uppers[baseKey] = v;
            }
        }

        // Pair each lower with its matching upper
        var result = new List<BlockVariantEntry>();
        foreach (var (baseKey, lower) in lowers.OrderBy(k => k.Key))
        {
            uppers.TryGetValue(baseKey, out var upper);
            result.Add(new BlockVariantEntry
            {
                VariantKey  = baseKey,   // display name without half=
                ModelPath   = lower.ModelPath,
                RotationX   = lower.RotationX,
                RotationY   = lower.RotationY,
                CemPath     = lower.CemPath,
                TopHalf     = upper,
                PartOffsetY = 1f         // door upper half is 1 block above
            });
        }

        return result.Count > 0 ? result : variants;
    }

    /// <summary>
    /// Removes the <c>half=lower</c> or <c>half=upper</c> segment (and any
    /// trailing/leading comma) from a variant key string.
    /// </summary>
    private static string StripHalfProperty(string key)
    {
        // Remove ",half=lower", ",half=upper", "half=lower,", "half=upper,"
        string result = key;
        foreach (string pat in new[] { ",half=lower", ",half=upper", "half=lower,", "half=upper," })
            result = result.Replace(pat, "", StringComparison.OrdinalIgnoreCase);
        // If it was the only property, strip without comma
        result = result.Replace("half=lower", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("half=upper", "", StringComparison.OrdinalIgnoreCase)
                       .Trim(',', ' ');
        return string.IsNullOrEmpty(result) ? "default" : result;
    }

    private static void ParseVariantToken(string variantKey, JToken token, List<BlockVariantEntry> list)
    {
        // A variant value can be a single object or an array of objects (random rotations)
        if (token is JArray arr)
        {
            // Multiple weighted variants — expose each as its own entry
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is JObject obj)
                {
                    string suffix = arr.Count > 1 ? $" [{i}]" : "";
                    list.Add(ParseVariantObject(variantKey.Length > 0 ? variantKey + suffix : $"default{suffix}", obj));
                }
            }
        }
        else if (token is JObject obj)
        {
            string displayKey = variantKey.Length > 0 ? variantKey : "default";
            list.Add(ParseVariantObject(displayKey, obj));
        }
    }

    private static BlockVariantEntry ParseVariantObject(string key, JObject obj)
    {
        string model = obj["model"]?.Value<string>() ?? "";
        int    rx    = obj["x"]?.Value<int>() ?? 0;
        int    ry    = obj["y"]?.Value<int>() ?? 0;

        string cemPath = GetCemPath(NormalizeModelPath(model));

        return new BlockVariantEntry
        {
            VariantKey = key,
            ModelPath  = model,
            RotationX  = rx,
            RotationY  = ry,
            CemPath    = cemPath ?? ""
        };
    }

    // ── Model resolution ──────────────────────────────────────────────────────

    private static ResolvedBlockModel? BuildResolvedModel(string modelPath, HashSet<string> visited)
    {
        if (!visited.Add(modelPath))
        {
            Console.WriteLine($"[BlockRegistry] Circular parent reference: {modelPath}");
            return null;
        }

        JObject? raw = LoadRawModel(modelPath);
        if (raw == null) return null;

        // Recursively resolve parent first
        ResolvedBlockModel? parentResolved = null;
        if (raw["parent"] is JToken parentToken)
        {
            string parentPath = NormalizeModelPath(parentToken.Value<string>() ?? "");
            // Skip the built-in "builtin/generated" or "builtin/entity" parents
            if (!parentPath.StartsWith("builtin/") && !parentPath.Contains("builtin/"))
                parentResolved = BuildResolvedModel(parentPath, visited);
        }

        // Start from parent (or empty) then overlay this model's data
        var result = new ResolvedBlockModel();

        // Inherit parent texture size, then allow this model to override it
        if (parentResolved != null)
        {
            result.TextureSizeX = parentResolved.TextureSizeX;
            result.TextureSizeY = parentResolved.TextureSizeY;
        }

        // texture_size: [w, h] — present in models that use non-16×16 UV space
        if (raw["texture_size"] is JArray tsArr && tsArr.Count >= 2)
        {
            result.TextureSizeX = tsArr[0].Value<float>();
            result.TextureSizeY = tsArr[1].Value<float>();
        }

        // Copy parent textures
        if (parentResolved != null)
            foreach (var kvp in parentResolved.Textures)
                result.Textures[kvp.Key] = kvp.Value;

        // Copy parent elements (only if this model does not override them)
        bool hasOwnElements = raw["elements"] is JArray ea && ea.Count > 0;
        if (parentResolved != null && !hasOwnElements)
            foreach (var el in parentResolved.Elements)
                result.Elements.Add(el);

        // Override / add textures from this model
        if (raw["textures"] is JObject texObj)
        {
            foreach (var prop in texObj.Properties())
            {
                string slotName  = prop.Name;
                string slotValue = prop.Value.Value<string>() ?? "";
                result.Textures[slotName] = slotValue;
            }
        }

        // Resolve texture reference chains (e.g. "#all" → "block/grass")
        ResolveTextureRefs(result.Textures);

        // Parse own elements
        if (hasOwnElements && raw["elements"] is JArray elements)
        {
            foreach (JToken elToken in elements)
            {
                if (elToken is not JObject elObj) continue;
                var element = ParseElement(elObj, result.Textures);
                if (element != null)
                    result.Elements.Add(element);
            }
        }

        return result;
    }

    private static void ResolveTextureRefs(Dictionary<string, string> textures)
    {
        // Each value may be "#slotName" → dereference until we get a concrete path.
        // Limit iterations to avoid infinite loops on malformed data.
        for (int pass = 0; pass < 8; pass++)
        {
            bool changed = false;
            foreach (var key in textures.Keys.ToList())
            {
                string value = textures[key];
                if (value.StartsWith('#'))
                {
                    string refKey = value[1..];
                    if (textures.TryGetValue(refKey, out string? resolved) && !resolved.StartsWith('#'))
                    {
                        textures[key] = resolved;
                        changed = true;
                    }
                }
            }
            if (!changed) break;
        }
    }

    private static BlockModelElement? ParseElement(JObject obj, Dictionary<string, string> textures)
    {
        var fromArr = obj["from"]?.ToObject<float[]>();
        var toArr   = obj["to"]?.ToObject<float[]>();
        if (fromArr == null || toArr == null || fromArr.Length < 3 || toArr.Length < 3)
            return null;

        var el = new BlockModelElement
        {
            From = fromArr,
            To   = toArr,
        };

        // Optional element rotation
        if (obj["rotation"] is JObject rotObj)
        {
            el.Rotation = new ElementRotation
            {
                Origin  = rotObj["origin"]?.ToObject<float[]>() ?? new float[] { 8, 8, 8 },
                Axis    = rotObj["axis"]?.Value<string>() ?? "y",
                Angle   = rotObj["angle"]?.Value<float>() ?? 0f,
                Rescale = rotObj["rescale"]?.Value<bool>() ?? false
            };
        }

        // Faces
        if (obj["faces"] is JObject facesObj)
        {
            foreach (var faceProp in facesObj.Properties())
            {
                if (faceProp.Value is not JObject faceObj) continue;

                var face = new BlockModelFace
                {
                    Texture   = faceObj["texture"]?.Value<string>() ?? "",
                    Cullface  = faceObj["cullface"]?.Value<string>(),
                    TintIndex = faceObj["tintindex"]?.Value<int>() ?? -1,
                    Rotation  = faceObj["rotation"]?.Value<int>() ?? 0
                };

                if (faceObj["uv"] is JArray uvArr && uvArr.Count == 4)
                {
                    // UV values are always in 0–16 logical space regardless of texture_size.
                    // texture_size only describes the pixel resolution of the atlas texture
                    // and does not affect how UVs are interpreted here.
                    face.Uv = new float[]
                    {
                        uvArr[0].Value<float>(),
                        uvArr[1].Value<float>(),
                        uvArr[2].Value<float>(),
                        uvArr[3].Value<float>()
                    };
                }

                el.Faces[faceProp.Name] = face;
            }
        }

        return el;
    }

    private static JObject? LoadRawModel(string normalizedPath)
    {
        if (_rawModelCache.TryGetValue(normalizedPath, out var cached))
            return cached;

        // normalizedPath is like "block/grass_block" or "minecraft:block/grass_block"
        // Strip the "minecraft:" prefix if present
        string relativePath = normalizedPath;
        if (relativePath.StartsWith("minecraft:"))
            relativePath = relativePath["minecraft:".Length..];

        // models/<relativePath>.json
        string filePath = Path.Combine(VersionRoot, "models", relativePath + ".json");

        JObject? result = null;
        if (File.Exists(filePath))
        {
            try
            {
                string text = File.ReadAllText(filePath);
                result = JObject.Parse(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BlockRegistry] Failed to parse model '{filePath}': {ex.Message}");
            }
        }

        _rawModelCache[normalizedPath] = result;
        return result;
    }

    /// <summary>
    /// Normalises a model reference to a consistently-formed path.
    /// Examples:
    ///   "minecraft:block/grass_block"  → "block/grass_block"
    ///   "block/grass_block"            → "block/grass_block"
    ///   "#all"                         → "#all"   (texture ref, leave as-is)
    /// </summary>
    private static string NormalizeModelPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        // Keep texture refs as-is
        if (path.StartsWith('#')) return path;
        // Strip namespace prefix
        int colon = path.IndexOf(':');
        if (colon >= 0) return path[(colon + 1)..];
        return path;
    }

    // ── Texture key resolution ────────────────────────────────────────────────

    /// <summary>
    /// Given a resolved model and a face texture reference (e.g. "#side" or
    /// "block/grass_block_side"), returns the bare texture key suitable for
    /// lookup in <see cref="TerrainAtlas.Textures"/>.
    /// Returns null if it cannot be resolved.
    /// </summary>
    public static string? ResolveTextureKey(ResolvedBlockModel model, string textureRef)
    {
        if (string.IsNullOrEmpty(textureRef)) return null;

        string value = textureRef;

        // Dereference # chains
        for (int i = 0; i < 8 && value.StartsWith('#'); i++)
        {
            string key = value[1..];
            if (!model.Textures.TryGetValue(key, out string? next) || string.IsNullOrEmpty(next))
                return null;
            value = next;
        }

        if (value.StartsWith('#')) return null; // unresolved

        // Strip namespace prefix (e.g. "minecraft:block/grass" → "block/grass")
        int colon = value.IndexOf(':');
        if (colon >= 0) value = value[(colon + 1)..];

        // "block/" textures are keyed by bare name in TerrainAtlas (e.g. "grass_block_top")
        if (value.StartsWith("block/")) return value["block/".Length..];

        // "entity/" textures are keyed by their full relative path (e.g. "entity/bed/red")
        // TerrainAtlas stores these with the path intact so return as-is.
        return value;
    }
}
