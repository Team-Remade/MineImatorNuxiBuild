using System.Text.Json.Nodes;
using MineImatorSimplyRemade.core.render;
using StbImageSharp;
using Veldrid;

namespace MineImatorSimplyRemade;

/// <summary>
/// Describes the animation sequence for a spritesheet texture parsed from a
/// <c>.mcmeta</c> sidecar file.
/// </summary>
public class AnimatedTextureInfo
{
    /// <summary>Width of a single frame in pixels (always equal to the texture width).</summary>
    public int FrameWidth  { get; init; }

    /// <summary>Height of a single frame in pixels.</summary>
    public int FrameHeight { get; init; }

    /// <summary>Total number of frames in the spritesheet (image height / frame height).</summary>
    public int TotalFrames { get; init; }

    /// <summary>
    /// Ordered list of frame indices to display.  Each index is a row in the
    /// spritesheet (0 = top row).  May repeat indices for holds.
    /// </summary>
    public int[] Frames { get; init; } = Array.Empty<int>();

    /// <summary>How many ticks (1/20 s) each frame is shown for (default 1).</summary>
    public int FrameTime { get; init; } = 1;
}

/// <summary>
/// Loads every PNG from <c>data/minecraft/versions/1.3.2/textures/block/</c>
/// and uploads each one as an individual OpenGL texture.
///
/// Textures are keyed by their filename without extension (e.g. <c>"grass_block_top"</c>).
/// Textures with a <c>.mcmeta</c> sidecar are stored as the full spritesheet; callers
/// should use <see cref="AnimatedTextures"/> to obtain frame UV offsets at runtime.
///
/// Call <see cref="Initialize"/> once at startup after the OpenGL context is ready.
/// </summary>
public static class TerrainAtlas
{
    /// <summary>
    /// Conventional tile size – kept for compatibility with code that reads this
    /// constant (e.g. <see cref="core.mdl.meshes.ExtrudedItemMesh"/>).
    /// </summary>
    public const int TileSize = 16;

    /// <summary>All loaded block textures, keyed by filename without extension.</summary>
    public static readonly Dictionary<string, Texture> Textures = new();

    /// <summary>
    /// Raw RGBA pixel bytes for each texture, keyed by filename without extension.
    /// Each value is <c>width * height * 4</c> bytes (top-to-bottom, RGBA).
    /// </summary>
    public static readonly Dictionary<string, byte[]> TilePixels = new();

    /// <summary>
    /// Animation metadata for animated textures, keyed by filename without extension.
    /// Only present for textures that have a <c>.mcmeta</c> sidecar.
    /// </summary>
    public static readonly Dictionary<string, AnimatedTextureInfo> AnimatedTextures = new();

    /// <summary>Memoised results of <see cref="IsTextureTranslucent"/>, cleared on reload.</summary>
    private static readonly Dictionary<string, bool> _translucencyCache = new();

    public static void Initialize(Action<float, string>? progress = null)
    {
        LoadTextures(progress);
    }

    /// <summary>
    /// True when the texture identified by <paramref name="key"/> has at least one
    /// pixel with a "meaningfully partial" alpha value (neither essentially opaque
    /// nor essentially transparent). This distinguishes true alpha-*blend* textures
    /// like water (every pixel around ~0.7 alpha) from alpha-*cutout* textures like
    /// leaves or glass panes (pixels are either fully opaque or fully transparent,
    /// with a hard edge between shape and cutout). Callers use this to decide
    /// whether a mesh needs proper depth-tested, non-depth-writing blend rendering
    /// (see <see cref="Mesh.IsTranslucent"/>) instead of the cheaper cutout path,
    /// which — because it writes full depth for any non-transparent pixel — would
    /// otherwise make blend-style textures wrongly occlude geometry behind them.
    /// Result is memoised; the cache is cleared whenever textures are reloaded.
    /// </summary>
    public static bool IsTextureTranslucent(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (_translucencyCache.TryGetValue(key, out bool cached))
            return cached;

        bool result = false;
        if (TilePixels.TryGetValue(key, out byte[]? pixels))
        {
            for (int i = 3; i < pixels.Length; i += 4)
            {
                byte a = pixels[i];
                // Leave a little headroom around 0/255 for compression/quantisation
                // noise on otherwise-binary cutout edges.
                if (a > 8 && a < 248)
                {
                    result = true;
                    break;
                }
            }
        }

        _translucencyCache[key] = result;
        return result;
    }

    private static void LoadTextures(Action<float, string>? progress = null)
    {
        progress?.Invoke(0f, "Clearing previous terrain textures...");

        // Reinitialize safely when called multiple times.
        foreach (Texture tex in Textures.Values)
            tex.Dispose();
        Textures.Clear();
        TilePixels.Clear();
        AnimatedTextures.Clear();
        _translucencyCache.Clear();

        string versionRoot = MinecraftDataLoader.GetVersionRoot();
        string texturesDir = Path.Combine(versionRoot, "textures");
        string blockDir    = Path.Combine(texturesDir, "block");

        if (!Directory.Exists(blockDir))
        {
            return;
        }

        StbImage.stbi_set_flip_vertically_on_load(0);

        // Load block textures (flat key = filename without extension, e.g. "grass_block_top")
        string[] files = Directory.GetFiles(blockDir, "*.png", SearchOption.TopDirectoryOnly);
        int blockFileCount = Math.Max(files.Length, 1);

        for (int i = 0; i < files.Length; i++)
        {
            string filePath = files[i];
            string key = Path.GetFileNameWithoutExtension(filePath);

            ImageResult img;
            try
            {
                using var stream = File.OpenRead(filePath);
                img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load '{filePath}': {ex.Message}");
                continue;
            }

            bool hasAnim = File.Exists(filePath + ".mcmeta");
            UpsertTexture(key, img.Data, img.Width, img.Height, hasAnim);

            // Parse animation metadata if a .mcmeta sidecar exists
            if (hasAnim)
            {
                var anim = ParseMcMetaFromText(File.ReadAllText(filePath + ".mcmeta"), img.Width, img.Height);
                if (anim != null)
                    AnimatedTextures[key] = anim;
            }

            progress?.Invoke(0.05f + ((i + 1) / (float)blockFileCount) * 0.45f, $"Block texture: {key}");
        }

        // Load entity textures recursively, keyed by their path relative to texturesDir
        // e.g. "entity/bed/red.png" → key "entity/bed/red"
        string entityDir = Path.Combine(texturesDir, "entity");
        if (Directory.Exists(entityDir))
        {
            string[] entityFiles = Directory.GetFiles(entityDir, "*.png", SearchOption.AllDirectories);
            int entityFileCount = Math.Max(entityFiles.Length, 1);

            for (int i = 0; i < entityFiles.Length; i++)
            {
                string filePath = entityFiles[i];
                // Build a relative path key: "entity/bed/red"
                string relative = Path.GetRelativePath(texturesDir, filePath)
                                      .Replace('\\', '/')
                                      .Replace(".png", "");

                ImageResult img;
                try
                {
                    using var stream = File.OpenRead(filePath);
                    img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load entity texture '{filePath}': {ex.Message}");
                    continue;
                }

                bool hasAnim = File.Exists(filePath + ".mcmeta");
                UpsertTexture(relative, img.Data, img.Width, img.Height, hasAnim);

                if (hasAnim)
                {
                    var anim = ParseMcMetaFromText(File.ReadAllText(filePath + ".mcmeta"), img.Width, img.Height);
                    if (anim != null)
                        AnimatedTextures[relative] = anim;
                }

                progress?.Invoke(0.50f + ((i + 1) / (float)entityFileCount) * 0.15f, $"Entity texture: {relative}");
            }
        }

        ApplyResourcePackOverrides((value, detail) => progress?.Invoke(0.65f + value * 0.35f, detail));

        Console.WriteLine($"Loaded {Textures.Count} textures " +
                          $"({AnimatedTextures.Count} animated)");
        progress?.Invoke(1f, $"Loaded {Textures.Count} terrain texture(s)");
    }

    private static void ApplyResourcePackOverrides(Action<float, string>? progress = null)
    {
        var mcmetaByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets", ".png.mcmeta", (_, containerName, current, total) =>
                 {
                     float ratio = total <= 0 ? 0f : (current - 1) / (float)total;
                     progress?.Invoke(ratio * 0.20f, $"Scanning animation metadata {current}/{total}: {containerName}");
                 }))
        {
            mcmetaByPath.TryAdd(file.RelativePath, MinecraftDataLoader.DecodeUtf8(file.Data));
        }

        // Add block textures using namespaced keys so default keys stay intact.
        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets/minecraft/textures/block", ".png", (_, containerName, current, total) =>
                 {
                     float ratio = total <= 0 ? 0f : (current - 1) / (float)total;
                     progress?.Invoke(0.20f + ratio * 0.30f, $"Scanning block overrides {current}/{total}: {containerName}");
                 }))
        {
            string baseKey = Path.GetFileNameWithoutExtension(file.RelativePath);
            if (string.IsNullOrWhiteSpace(baseKey)) continue;

            string key = MinecraftDataLoader.BuildResourcePackTextureKey(file.PackName, baseKey);

            ImageResult img;
            try
            {
                img = ImageResult.FromMemory(file.Data, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load resourcepack texture '{file.RelativePath}' from '{file.PackName}': {ex.Message}");
                continue;
            }

            string mcmetaPath = file.RelativePath + ".mcmeta";
            bool hasAnim = mcmetaByPath.TryGetValue(mcmetaPath, out string? animText);

            UpsertTexture(key, img.Data, img.Width, img.Height, hasAnim);

            if (hasAnim)
            {
                var anim = ParseMcMetaFromText(animText!, img.Width, img.Height);
                if (anim != null)
                    AnimatedTextures[key] = anim;
            }
            else
            {
                AnimatedTextures.Remove(key);
            }

            progress?.Invoke(0.50f, $"Block override: {baseKey}");
        }

        // Add entity textures using namespaced keys so default keys stay intact.
        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets/minecraft/textures/entity", ".png", (_, containerName, current, total) =>
                 {
                     float ratio = total <= 0 ? 0f : (current - 1) / (float)total;
                     progress?.Invoke(0.50f + ratio * 0.20f, $"Scanning entity overrides {current}/{total}: {containerName}");
                 }))
        {
            string baseRelative = file.RelativePath
                .Replace("assets/minecraft/textures/", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".png", "", StringComparison.OrdinalIgnoreCase)
                .Replace('\\', '/');

            if (string.IsNullOrWhiteSpace(baseRelative))
                continue;

            string relative = MinecraftDataLoader.BuildResourcePackTextureKey(file.PackName, baseRelative);

            ImageResult img;
            try
            {
                img = ImageResult.FromMemory(file.Data, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load resourcepack entity texture '{file.RelativePath}' from '{file.PackName}': {ex.Message}");
                continue;
            }

            string mcmetaPath = file.RelativePath + ".mcmeta";
            bool hasAnim = mcmetaByPath.TryGetValue(mcmetaPath, out string? animText);

            UpsertTexture(relative, img.Data, img.Width, img.Height, hasAnim);

            if (hasAnim)
            {
                var anim = ParseMcMetaFromText(animText!, img.Width, img.Height);
                if (anim != null)
                    AnimatedTextures[relative] = anim;
            }
            else
            {
                AnimatedTextures.Remove(relative);
            }

            progress?.Invoke(0.70f, $"Entity override: {baseRelative}");
        }

        // Load non-minecraft namespaced textures from external containers (e.g. Java mods).
        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets", ".png", (_, containerName, current, total) =>
                 {
                     float ratio = total <= 0 ? 0f : (current - 1) / (float)total;
                     progress?.Invoke(0.70f + ratio * 0.30f, $"Scanning namespaced textures {current}/{total}: {containerName}");
                 }))
        {
            if (!MinecraftDataLoader.TryParseTextureAssetPath(file.RelativePath, out string assetNamespace, out string category, out string textureKey))
                continue;

            if (string.Equals(assetNamespace, "minecraft", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(category, "block", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(category, "entity", StringComparison.OrdinalIgnoreCase))
                continue;

            string key = MinecraftDataLoader.BuildResourcePackTextureKey(file.PackName, $"{assetNamespace}/{category}/{textureKey}");

            ImageResult img;
            try
            {
                img = ImageResult.FromMemory(file.Data, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load namespaced texture '{file.RelativePath}' from '{file.PackName}': {ex.Message}");
                continue;
            }

            string mcmetaPath = file.RelativePath + ".mcmeta";
            bool hasAnim = mcmetaByPath.TryGetValue(mcmetaPath, out string? animText);

            UpsertTexture(key, img.Data, img.Width, img.Height, hasAnim);

            if (hasAnim)
            {
                var anim = ParseMcMetaFromText(animText!, img.Width, img.Height);
                if (anim != null)
                    AnimatedTextures[key] = anim;
            }
            else
            {
                AnimatedTextures.Remove(key);
            }

            progress?.Invoke(1f, $"Namespaced texture: {assetNamespace}/{category}/{textureKey}");
        }
    }

    private static void UpsertTexture(string key, byte[] pixels, int width, int height, bool hasAnim)
    {
        if (Textures.TryGetValue(key, out Texture? oldTexture))
            oldTexture.Dispose();

        // Animated spritesheets are sampled per-frame with explicit UV offsets
        // (see AnimatedTextures), so they must clamp rather than repeat/wrap -
        // repeating would bleed adjacent animation frames into the sampled tile.
        Texture texture = VeldridTextureLoader.UploadRgba(pixels, (uint)width, (uint)height,
            nearest: true, generateMipmaps: false, repeat: !hasAnim);

        Textures[key] = texture;
        TilePixels[key] = pixels;
    }

    private static AnimatedTextureInfo? ParseMcMetaFromText(string metaText, int imgWidth, int imgHeight)
    {
        try
        {
            var root = JsonNode.Parse(metaText)?.AsObject();
            var anim = root?["animation"] as JsonObject;
            if (anim == null) return null;

            // Frame size: default is square (width × width), can be overridden
            int frameW = anim["width"]?.GetValue<int>()     ?? imgWidth;
            int frameH = anim["height"]?.GetValue<int>()    ?? imgWidth; // square frames by default
            int frameTime = anim["frametime"]?.GetValue<int>() ?? 1;

            int totalFrames = imgHeight / frameH;
            if (totalFrames < 1) totalFrames = 1;

            int[] frames;
            if (anim["frames"] is JsonArray framesArr && framesArr.Count > 0)
            {
                // Each entry can be an int (frame index) or an object {index, time}
                // For now read just the index; per-frame time overrides are rare
                frames = framesArr
                    .Select(t => t is JsonObject fo
                        ? fo["index"]?.GetValue<int>() ?? 0
                        : t?.GetValue<int>() ?? 0)
                    .ToArray();
            }
            else
            {
                // No explicit frame list — play all frames in order
                frames = Enumerable.Range(0, totalFrames).ToArray();
            }

            return new AnimatedTextureInfo
            {
                FrameWidth  = frameW,
                FrameHeight = frameH,
                TotalFrames = totalFrames,
                Frames      = frames,
                FrameTime   = frameTime
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to parse mcmeta: {ex.Message}");
            return null;
        }
    }
}
