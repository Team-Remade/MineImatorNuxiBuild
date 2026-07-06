using Silk.NET.OpenGL;
using StbImageSharp;
using MineImatorSimplyRemade.core.project;

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

    public static void Initialize(GL gl, Action<float, string>? progress = null)
    {
        _gl = gl;
        LoadAtlas(progress);
    }

    public static string BuildProjectCustomTextureKey(string relativePath)
    {
        string normalized = (relativePath ?? string.Empty)
            .Replace('\\', '/')
            .Trim();

        while (normalized.StartsWith('/'))
            normalized = normalized[1..];

        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "images/custom.png";

        return $"project:{normalized}";
    }

    public static bool TryRegisterCustomTextureFromFile(string key, string filePath)
    {
        if (_gl == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        ImageResult img;
        try
        {
            using var stream = File.OpenRead(filePath);
            img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load custom item image '{filePath}': {ex.Message}");
            return false;
        }

        if (img.Width != img.Height)
        {
            Console.WriteLine($"Custom item image must be square: {filePath}");
            return false;
        }

        UpsertTileTexture(key, img.Data, img.Width, img.Height);
        return true;
    }

    public static void EnsureProjectCustomTexturesLoaded()
    {
        if (_gl == null)
            return;

        var projectManager = ProjectManager.Instance;
        if (!projectManager.HasProject)
            return;

        foreach (var asset in projectManager.GetProjectAssets())
        {
            if (asset.AssetType != ProjectAssetType.Image)
                continue;

            string fullPath = projectManager.GetAssetFullPath(asset);
            if (!File.Exists(fullPath))
                continue;

            string keySource = asset.StoredInProject && !string.IsNullOrWhiteSpace(asset.RelativePath)
                ? asset.RelativePath
                : asset.DisplayName;

            string key = BuildProjectCustomTextureKey(keySource);
            if (Textures.ContainsKey(key))
                continue;

            TryRegisterCustomTextureFromFile(key, fullPath);
        }
    }

    private static unsafe void LoadAtlas(Action<float, string>? progress = null)
    {
        if (_gl == null) return;

        progress?.Invoke(0f, "Clearing previous item textures...");

        foreach (uint tex in Textures.Values)
            _gl.DeleteTexture(tex);
        Textures.Clear();
        TilePixels.Clear();

        string versionRoot = MinecraftDataLoader.GetVersionRoot();
        string atlasPath = Path.Combine(versionRoot, "gui", "items.png");

        if (!File.Exists(atlasPath))
        {
            Console.WriteLine($"File not found: {atlasPath}");
            return;
        }

        StbImage.stbi_set_flip_vertically_on_load(0);

        ImageResult atlas;
        using (var stream = File.OpenRead(atlasPath))
            atlas = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        int atlasSize = AtlasTiles * TileSize;
        if (atlas.Width != atlasSize || atlas.Height != atlasSize)
        {
            Console.WriteLine($"Size mismatch: expected {atlasSize}×{atlasSize}, got {atlas.Width}×{atlas.Height}");
            return;
        }

        SliceGridAtlas(atlas.Data, atlasSize);
        progress?.Invoke(0.20f, "Loaded base item atlas");

        ApplyResourcePackItemsOverrides((value, detail) => progress?.Invoke(0.20f + value * 0.75f, detail));
        EnsureProjectCustomTexturesLoaded();
        progress?.Invoke(1f, $"Loaded {Textures.Count} item texture(s)");

        Console.WriteLine($"Loaded {Textures.Count} tiles");
    }

    private static void ApplyResourcePackItemsOverrides(Action<float, string>? progress = null)
    {
        if (_gl == null) return;

        // Legacy/old-style item sheet: add a namespaced 16x16 grid for the pack.
        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets/minecraft/textures/gui", "items.png", (_, containerName, current, total) =>
                 {
                     float ratio = total <= 0 ? 0f : (current - 1) / (float)total;
                     progress?.Invoke(ratio * 0.25f, $"Scanning item sheets {current}/{total}: {containerName}");
                 }))
        {
            ImageResult atlas;
            try
            {
                atlas = ImageResult.FromMemory(file.Data, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load sheet override '{file.RelativePath}' from '{file.PackName}': {ex.Message}");
                continue;
            }

            int atlasSize = AtlasTiles * TileSize;
            if (atlas.Width != atlasSize || atlas.Height != atlasSize)
            {
                Console.WriteLine($"Ignoring sheet override '{file.RelativePath}' from '{file.PackName}' due to size mismatch.");
                continue;
            }

            string packPrefix = MinecraftDataLoader.BuildResourcePackTextureKey(file.PackName, "");
            SliceGridAtlas(atlas.Data, atlasSize, packPrefix);
            progress?.Invoke(0.25f, $"Applied item sheet from {file.PackName}");
        }

        // Modern packs expose per-item textures in assets/minecraft/textures/item/*.png.
        // Add each texture with a namespaced key so defaults remain available.
        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets/minecraft/textures/item", ".png", (_, containerName, current, total) =>
                 {
                     float ratio = total <= 0 ? 0f : (current - 1) / (float)total;
                     progress?.Invoke(0.25f + ratio * 0.35f, $"Scanning item overrides {current}/{total}: {containerName}");
                 }))
        {
            ImageResult img;
            try
            {
                img = ImageResult.FromMemory(file.Data, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load item texture '{file.RelativePath}' from '{file.PackName}': {ex.Message}");
                continue;
            }

            if (img.Width != img.Height)
            {
                Console.WriteLine($"Ignoring non-square item texture '{file.RelativePath}' from '{file.PackName}'.");
                continue;
            }

            string baseKey = file.RelativePath
                .Replace("assets/minecraft/textures/item/", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".png", "", StringComparison.OrdinalIgnoreCase)
                .Replace('\\', '/');

            if (string.IsNullOrWhiteSpace(baseKey))
                continue;

            string key = MinecraftDataLoader.BuildResourcePackTextureKey(file.PackName, baseKey);

            UpsertTileTexture(key, img.Data, img.Width, img.Height);
            progress?.Invoke(0.60f, $"Item override: {baseKey}");
        }

        // Load non-minecraft namespaced item textures from external containers (e.g. Java mods).
        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets", ".png", (_, containerName, current, total) =>
                 {
                     float ratio = total <= 0 ? 0f : (current - 1) / (float)total;
                     progress?.Invoke(0.60f + ratio * 0.40f, $"Scanning mod item textures {current}/{total}: {containerName}");
                 }))
        {
            if (!MinecraftDataLoader.TryParseTextureAssetPath(file.RelativePath, out string assetNamespace, out string category, out string textureKey))
                continue;

            if (string.Equals(assetNamespace, "minecraft", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(category, "item", StringComparison.OrdinalIgnoreCase))
                continue;

            ImageResult img;
            try
            {
                img = ImageResult.FromMemory(file.Data, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load namespaced item texture '{file.RelativePath}' from '{file.PackName}': {ex.Message}");
                continue;
            }

            if (img.Width != img.Height)
            {
                Console.WriteLine($"Ignoring non-square namespaced item texture '{file.RelativePath}' from '{file.PackName}'.");
                continue;
            }

            string key = MinecraftDataLoader.BuildResourcePackTextureKey(file.PackName, $"{assetNamespace}/item/{textureKey}");
            UpsertTileTexture(key, img.Data, img.Width, img.Height);
            progress?.Invoke(1f, $"Mod item texture: {assetNamespace}/item/{textureKey}");
        }
    }

    private static void SliceGridAtlas(byte[] src, int atlasSize, string keyPrefix = "")
    {
        if (_gl == null) return;

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

                string key = string.IsNullOrWhiteSpace(keyPrefix)
                    ? $"{tx},{ty}"
                    : $"{keyPrefix}{tx},{ty}";
                UpsertTileTexture(key, tile, TileSize, TileSize);
            }
        }
    }

    private static unsafe void UpsertTileTexture(string key, byte[] pixels, int width, int height)
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

        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS,     (int)TextureWrapMode.Repeat);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT,     (int)TextureWrapMode.Repeat);

        _gl.BindTexture(GLEnum.Texture2D, 0);

        Textures[key] = texId;
        TilePixels[key] = pixels;
    }
}
