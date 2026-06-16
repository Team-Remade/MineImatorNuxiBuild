using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade;

/// <summary>
/// Loads <c>terrain.png</c> (Minecraft 1.3.2, expected 256×256) and slices it
/// into 256 individual 16×16 OpenGL textures stored by their grid coordinate key
/// <c>"x,y"</c> (e.g. <c>"8,2"</c> is column 8, row 2).
///
/// Call <see cref="Initialize"/> once at startup after the OpenGL context is ready.
/// </summary>
public static class TerrainAtlas
{
    public const int TileSize   = 16;
    public const int AtlasTiles = 16;

    /// <summary>All sliced tile textures, keyed as <c>"x,y"</c>.</summary>
    public static readonly Dictionary<string, uint> Textures = new();

    private static GL? _gl;

    public static void Initialize(GL gl)
    {
        _gl = gl;
        LoadAtlas();
    }

    private static unsafe void LoadAtlas()
    {
        if (_gl == null) return;

        string basePath   = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string atlasPath  = Path.Combine(basePath, "data", "minecraft", "versions", "1.3.2", "terrain.png");

        if (!File.Exists(atlasPath))
        {
            Console.WriteLine($"[TerrainAtlas] File not found: {atlasPath}");
            return;
        }

        StbImage.stbi_set_flip_vertically_on_load(0);

        ImageResult atlas;
        using (var stream = File.OpenRead(atlasPath))
            atlas = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        int atlasSize = AtlasTiles * TileSize;
        if (atlas.Width != atlasSize || atlas.Height != atlasSize)
        {
            Console.WriteLine($"[TerrainAtlas] Size mismatch: expected {atlasSize}×{atlasSize}, got {atlas.Width}×{atlas.Height}");
            return;
        }

        // atlas.Data is a flat RGBA byte array, row-major, top-to-bottom.
        byte[] src = atlas.Data;

        for (int ty = 0; ty < AtlasTiles; ty++)
        {
            for (int tx = 0; tx < AtlasTiles; tx++)
            {
                // Extract the tile's pixel rows from the atlas.
                byte[] tile = new byte[TileSize * TileSize * 4];
                for (int row = 0; row < TileSize; row++)
                {
                    int srcRow  = ty * TileSize + row;
                    int srcCol  = tx * TileSize;
                    int srcIdx  = (srcRow * atlasSize + srcCol) * 4;
                    int dstIdx  = row * TileSize * 4;
                    System.Buffer.BlockCopy(src, srcIdx, tile, dstIdx, TileSize * 4);
                }

                // Upload tile to a new OpenGL texture.
                uint texId = _gl.GenTexture();
                _gl.BindTexture(GLEnum.Texture2D, texId);

                fixed (byte* ptr = tile)
                {
                    _gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba,
                                   (uint)TileSize, (uint)TileSize, 0,
                                   PixelFormat.Rgba, GLEnum.UnsignedByte, ptr);
                }

                // Nearest-neighbour (pixel-art) filtering, wrapping enabled.
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS,     (int)TextureWrapMode.Repeat);
                _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT,     (int)TextureWrapMode.Repeat);

                _gl.BindTexture(GLEnum.Texture2D, 0);

                string key = $"{tx},{ty}";
                Textures[key] = texId;
            }
        }

        Console.WriteLine($"[TerrainAtlas] Loaded {Textures.Count} tiles from {atlasPath}");
    }
}
