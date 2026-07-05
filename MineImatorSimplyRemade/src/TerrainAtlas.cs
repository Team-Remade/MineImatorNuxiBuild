using System.Text.Json.Nodes;
using Silk.NET.OpenGL;
using StbImageSharp;

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
    public static readonly Dictionary<string, uint> Textures = new();

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

    private static GL? _gl;

    public static void Initialize(GL gl)
    {
        _gl = gl;
        LoadTextures();
    }

    private static unsafe void LoadTextures()
    {
        if (_gl == null) return;

        // Reinitialize safely when called multiple times.
        foreach (uint tex in Textures.Values)
            _gl.DeleteTexture(tex);
        Textures.Clear();
        TilePixels.Clear();
        AnimatedTextures.Clear();

        string versionRoot = MinecraftDataLoader.GetVersionRoot();
        string texturesDir = Path.Combine(versionRoot, "textures");
        string blockDir    = Path.Combine(texturesDir, "block");

        if (!Directory.Exists(blockDir))
        {
            Console.WriteLine($"[TerrainAtlas] Directory not found: {blockDir}");
            return;
        }

        StbImage.stbi_set_flip_vertically_on_load(0);

        // Load block textures (flat key = filename without extension, e.g. "grass_block_top")
        string[] files = Directory.GetFiles(blockDir, "*.png", SearchOption.TopDirectoryOnly);

        foreach (string filePath in files)
        {
            string key = Path.GetFileNameWithoutExtension(filePath);

            ImageResult img;
            try
            {
                using var stream = File.OpenRead(filePath);
                img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TerrainAtlas] Failed to load '{filePath}': {ex.Message}");
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
        }

        // Load entity textures recursively, keyed by their path relative to texturesDir
        // e.g. "entity/bed/red.png" → key "entity/bed/red"
        string entityDir = Path.Combine(texturesDir, "entity");
        if (Directory.Exists(entityDir))
        {
            foreach (string filePath in Directory.GetFiles(entityDir, "*.png", SearchOption.AllDirectories))
            {
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
                    Console.WriteLine($"[TerrainAtlas] Failed to load entity texture '{filePath}': {ex.Message}");
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
            }
        }

        ApplyResourcePackOverrides();

        Console.WriteLine($"[TerrainAtlas] Loaded {Textures.Count} textures " +
                          $"({AnimatedTextures.Count} animated) from {blockDir}");
    }

    private static void ApplyResourcePackOverrides()
    {
        if (_gl == null) return;

        var mcmetaByPath = MinecraftDataLoader
            .EnumerateResourcePackFiles("assets", ".png.mcmeta")
            .ToDictionary(f => f.RelativePath, f => MinecraftDataLoader.DecodeUtf8(f.Data), StringComparer.OrdinalIgnoreCase);

        // Add block textures using namespaced keys so default keys stay intact.
        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets/minecraft/textures/block", ".png"))
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
                Console.WriteLine($"[TerrainAtlas] Failed to load resourcepack texture '{file.RelativePath}' from '{file.PackName}': {ex.Message}");
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
        }

        // Add entity textures using namespaced keys so default keys stay intact.
        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets/minecraft/textures/entity", ".png"))
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
                Console.WriteLine($"[TerrainAtlas] Failed to load resourcepack entity texture '{file.RelativePath}' from '{file.PackName}': {ex.Message}");
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
        }

        // Load non-minecraft namespaced textures from external containers (e.g. Java mods).
        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets", ".png"))
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
                Console.WriteLine($"[TerrainAtlas] Failed to load namespaced texture '{file.RelativePath}' from '{file.PackName}': {ex.Message}");
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
        }
    }

    private static unsafe void UpsertTexture(string key, byte[] pixels, int width, int height, bool hasAnim)
    {
        if (_gl == null) return;

        if (Textures.TryGetValue(key, out uint oldTexId) && oldTexId != 0)
            _gl.DeleteTexture(oldTexId);

        uint texId = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, texId);

        fixed (byte* ptr = pixels)
        {
            _gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba,
                           (uint)width, (uint)height, 0,
                           PixelFormat.Rgba, GLEnum.UnsignedByte, ptr);
        }

        var wrapMode = hasAnim ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat;
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS,     (int)wrapMode);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT,     (int)wrapMode);

        _gl.BindTexture(GLEnum.Texture2D, 0);

        Textures[key] = texId;
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
            Console.WriteLine($"[TerrainAtlas] Failed to parse mcmeta: {ex.Message}");
            return null;
        }
    }
}
