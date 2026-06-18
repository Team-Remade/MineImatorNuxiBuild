using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade;

/// <summary>
/// Loads every PNG from <c>data/minecraft/versions/1.3.2/textures/block/</c>
/// and uploads each one as an individual OpenGL texture.
///
/// Textures are keyed by their filename without extension (e.g. <c>"grass_block_top"</c>).
/// Textures with the <c>.mcmeta</c> sidecar (animated textures) are still loaded
/// as a static image from the first frame.
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

    private static GL? _gl;

    public static void Initialize(GL gl)
    {
        _gl = gl;
        LoadTextures();
    }

    private static unsafe void LoadTextures()
    {
        if (_gl == null) return;

        string basePath   = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string blockDir   = Path.Combine(basePath, "data", "minecraft", "versions", "1.3.2", "textures", "block");

        if (!Directory.Exists(blockDir))
        {
            Console.WriteLine($"[TerrainAtlas] Directory not found: {blockDir}");
            return;
        }

        StbImage.stbi_set_flip_vertically_on_load(0);

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

            // Nearest-neighbour (pixel-art) filtering, wrapping enabled.
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS,     (int)TextureWrapMode.Repeat);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT,     (int)TextureWrapMode.Repeat);

            _gl.BindTexture(GLEnum.Texture2D, 0);

            Textures[key]   = texId;
            TilePixels[key] = pixels;
        }

        Console.WriteLine($"[TerrainAtlas] Loaded {Textures.Count} textures from {blockDir}");
    }
}
