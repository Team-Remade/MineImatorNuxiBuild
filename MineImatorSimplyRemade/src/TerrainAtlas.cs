using Newtonsoft.Json.Linq;
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

        string basePath    = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string texturesDir = Path.Combine(basePath, "data", "minecraft", "versions", "1.3.2", "textures");
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

            byte[] pixels = img.Data;

            uint texId = _gl.GenTexture();
            _gl.BindTexture(GLEnum.Texture2D, texId);

            fixed (byte* ptr = pixels)
            {
                _gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba,
                               (uint)img.Width, (uint)img.Height, 0,
                               PixelFormat.Rgba, GLEnum.UnsignedByte, ptr);
            }

            // Nearest-neighbour (pixel-art) filtering.
            // Animated textures are vertical spritesheets — use clamp-to-edge so
            // frame boundaries don't bleed into adjacent frames.
            bool hasAnim = File.Exists(filePath + ".mcmeta");
            var  wrapMode = hasAnim ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat;

            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS,     (int)wrapMode);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT,     (int)wrapMode);

            _gl.BindTexture(GLEnum.Texture2D, 0);

            Textures[key]   = texId;
            TilePixels[key] = pixels;

            // Parse animation metadata if a .mcmeta sidecar exists
            if (hasAnim)
            {
                var anim = ParseMcMeta(filePath + ".mcmeta", img.Width, img.Height);
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

                byte[] pixels = img.Data;
                uint texId = _gl.GenTexture();
                _gl.BindTexture(GLEnum.Texture2D, texId);
                fixed (byte* ptr = pixels)
                {
                    _gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba,
                                   (uint)img.Width, (uint)img.Height, 0,
                                   PixelFormat.Rgba, GLEnum.UnsignedByte, ptr);
                }
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS,     (int)TextureWrapMode.Repeat);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT,     (int)TextureWrapMode.Repeat);
                _gl.BindTexture(GLEnum.Texture2D, 0);

                Textures[relative]   = texId;
                TilePixels[relative] = pixels;
            }
        }

        Console.WriteLine($"[TerrainAtlas] Loaded {Textures.Count} textures " +
                          $"({AnimatedTextures.Count} animated) from {blockDir}");
    }

    private static AnimatedTextureInfo? ParseMcMeta(string metaPath, int imgWidth, int imgHeight)
    {
        try
        {
            var root = JObject.Parse(File.ReadAllText(metaPath));
            var anim = root["animation"] as JObject;
            if (anim == null) return null;

            // Frame size: default is square (width × width), can be overridden
            int frameW = anim["width"]?.Value<int>()     ?? imgWidth;
            int frameH = anim["height"]?.Value<int>()    ?? imgWidth; // square frames by default
            int frameTime = anim["frametime"]?.Value<int>() ?? 1;

            int totalFrames = imgHeight / frameH;
            if (totalFrames < 1) totalFrames = 1;

            int[] frames;
            if (anim["frames"] is JArray framesArr && framesArr.Count > 0)
            {
                // Each entry can be an int (frame index) or an object {index, time}
                // For now read just the index; per-frame time overrides are rare
                frames = framesArr
                    .Select(t => t is JObject fo
                        ? fo["index"]?.Value<int>() ?? 0
                        : t.Value<int>())
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
            Console.WriteLine($"[TerrainAtlas] Failed to parse mcmeta '{metaPath}': {ex.Message}");
            return null;
        }
    }
}
