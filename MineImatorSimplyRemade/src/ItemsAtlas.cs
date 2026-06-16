using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade;

/// <summary>
/// Loads <c>gui/items.png</c> (Minecraft 1.3.2, expected 256×256) and slices it
/// into 256 individual 16×16 OpenGL textures stored by their grid coordinate key
/// <c>"x,y"</c> (e.g. <c>"0,0"</c> is column 0, row 0).
///
/// Call <see cref="Initialize"/> once at startup after the OpenGL context is ready.
/// The tiles are initialised and ready for use; item rendering will bind them
/// per-object as that feature is implemented.
/// </summary>
public static class ItemsAtlas
{
    public const int TileSize   = 16;
    public const int AtlasTiles = 16;

    /// <summary>All sliced tile textures, keyed as <c>"x,y"</c>.</summary>
    public static readonly Dictionary<string, uint> Textures = new();

    /// <summary>
    /// Raw RGBA pixel bytes for each tile, keyed as <c>"x,y"</c>.
    /// Each value is a <c>TileSize * TileSize * 4</c> byte array (top-to-bottom, RGBA).
    /// </summary>
    public static readonly Dictionary<string, byte[]> TilePixels = new();

    private static GL? _gl;

    public static void Initialize(GL gl)
    {
        _gl = gl;
        LoadAtlas();
    }

    private static unsafe void LoadAtlas()
    {
        if (_gl == null) return;

        string basePath  = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string atlasPath = Path.Combine(basePath, "data", "minecraft", "versions", "1.3.2", "gui", "items.png");

        if (!File.Exists(atlasPath))
        {
            Console.WriteLine($"[ItemsAtlas] File not found: {atlasPath}");
            return;
        }

        StbImage.stbi_set_flip_vertically_on_load(0);

        ImageResult atlas;
        using (var stream = File.OpenRead(atlasPath))
            atlas = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        int atlasSize = AtlasTiles * TileSize;
        if (atlas.Width != atlasSize || atlas.Height != atlasSize)
        {
            Console.WriteLine($"[ItemsAtlas] Size mismatch: expected {atlasSize}×{atlasSize}, got {atlas.Width}×{atlas.Height}");
            return;
        }

        byte[] src = atlas.Data;

        for (int ty = 0; ty < AtlasTiles; ty++)
        {
            for (int tx = 0; tx < AtlasTiles; tx++)
            {
                byte[] tile = new byte[TileSize * TileSize * 4];
                for (int row = 0; row < TileSize; row++)
                {
                    int srcRow = ty * TileSize + row;
                    int srcCol = tx * TileSize;
                    int srcIdx = (srcRow * atlasSize + srcCol) * 4;
                    int dstIdx = row * TileSize * 4;
                    System.Buffer.BlockCopy(src, srcIdx, tile, dstIdx, TileSize * 4);
                }

                uint texId = _gl.GenTexture();
                _gl.BindTexture(GLEnum.Texture2D, texId);

                fixed (byte* ptr = tile)
                {
                    _gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba,
                                   (uint)TileSize, (uint)TileSize, 0,
                                   PixelFormat.Rgba, GLEnum.UnsignedByte, ptr);
                }

                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS,     (int)TextureWrapMode.Repeat);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT,     (int)TextureWrapMode.Repeat);

                _gl.BindTexture(GLEnum.Texture2D, 0);

                string key = $"{tx},{ty}";
                Textures[key] = texId;
                TilePixels[key] = tile;
            }
        }

        Console.WriteLine($"[ItemsAtlas] Loaded {Textures.Count} tiles from {atlasPath}");
    }
}
