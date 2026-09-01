using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.render;
using StbImageSharp;
using Veldrid;

namespace MineImatorSimplyRemade;

/// <summary>
/// Loads item textures from Minecraft data.
///
/// Legacy versions are read from <c>gui/items.png</c> (sliced into <c>"x,y"</c>
/// keys). Modern versions are read from <c>textures/item/*.png</c> (flattened
/// version roots) or <c>assets/minecraft/textures/item/*.png</c> (namespaced
/// roots), using path-based keys (for example <c>"diamond_sword"</c>).
///
/// Call <see cref="Initialize"/> once at startup after the OpenGL context is ready.
/// </summary>
public static class ItemsAtlas
{
    public sealed class TemporaryItemSheet
    {
        public string SourcePath { get; init; } = "";
        public byte[] Pixels { get; init; } = Array.Empty<byte>();
        public int Width { get; init; }
        public int Height { get; init; }
        public int Columns { get; init; }
        public int Rows { get; init; }
    }

    public const int TileSize   = 16;
    public const int AtlasTiles = 16;

    /// <summary>All sliced tile textures, keyed as <c>"x,y"</c>.</summary>
    public static readonly Dictionary<string, Texture> Textures = new();

    /// <summary>
    /// Raw RGBA pixel bytes for each tile, keyed as <c>"x,y"</c>.
    /// Each value is a <c>TileSize * TileSize * 4</c> byte array (top-to-bottom, RGBA).
    /// </summary>
    public static readonly Dictionary<string, byte[]> TilePixels = new();

    /// <summary>
    /// Actual texture dimensions for each tile or custom item image.
    /// Grid-atlas entries are <c>16x16</c>; imported/custom images may be rectangular.
    /// </summary>
    public static readonly Dictionary<string, (int Width, int Height)> TileDimensions = new();

    /// <summary>
    /// Cached temporary item sheets imported from external sources such as MIObject assets.
    /// These survive atlas rebuilds so individual tiles can be re-sliced later.
    /// </summary>
    public static readonly Dictionary<string, TemporaryItemSheet> TemporaryItemSheets = new();

    public static void Initialize(Action<float, string>? progress = null)
    {
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
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
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

        return TryRegisterCustomTexture(key, img.Data, img.Width, img.Height);
    }

    public static bool TryRegisterCustomTexture(string key, byte[] pixels, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(key) || pixels == null || pixels.Length == 0 || width <= 0 || height <= 0)
            return false;

        UpsertTileTexture(key, pixels, width, height);
        return true;
    }

    public static bool TryGetTileDimensions(string key, out int width, out int height)
    {
        if (TileDimensions.TryGetValue(key, out var dims))
        {
            width = dims.Width;
            height = dims.Height;
            return true;
        }

        width = TileSize;
        height = TileSize;
        return false;
    }

    public static string BuildTemporaryItemSheetKey(string sheetPath, int columns, int rows)
    {
        string normalizedPath = Path.GetFullPath(sheetPath ?? string.Empty)
            .Replace('\\', '/')
            .ToLowerInvariant();
        return $"miobjectsheet:{normalizedPath}|{columns}|{rows}";
    }

    public static bool TryRegisterTemporaryItemSheet(string sheetKey, string sheetPath, int columns, int rows)
    {
        if (string.IsNullOrWhiteSpace(sheetKey) || string.IsNullOrWhiteSpace(sheetPath) || columns <= 0 || rows <= 0)
            return false;

        if (TemporaryItemSheets.ContainsKey(sheetKey))
            return true;

        ImageResult img;
        try
        {
            using var stream = File.OpenRead(sheetPath);
            img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load temporary item sheet '{sheetPath}': {ex.Message}");
            return false;
        }

        TemporaryItemSheets[sheetKey] = new TemporaryItemSheet
        {
            SourcePath = Path.GetFullPath(sheetPath),
            Pixels = img.Data,
            Width = img.Width,
            Height = img.Height,
            Columns = columns,
            Rows = rows
        };

        return true;
    }

    public static bool TryRegisterTemporaryItemTile(string sheetKey, string tileKey, int columnIndex, int rowIndex)
    {
        if (string.IsNullOrWhiteSpace(sheetKey) || string.IsNullOrWhiteSpace(tileKey))
            return false;
        if (!TemporaryItemSheets.TryGetValue(sheetKey, out var sheet))
            return false;

        int cellWidth = Math.Max(1, sheet.Width / Math.Max(1, sheet.Columns));
        int cellHeight = Math.Max(1, sheet.Height / Math.Max(1, sheet.Rows));
        int clampedColumn = Math.Clamp(columnIndex, 0, Math.Max(0, sheet.Columns - 1));
        int clampedRow = Math.Clamp(rowIndex, 0, Math.Max(0, sheet.Rows - 1));
        byte[] tilePixels = ExtractTile(sheet.Pixels, sheet.Width, clampedColumn * cellWidth, clampedRow * cellHeight, cellWidth, cellHeight);

        UpsertTileTexture(tileKey, tilePixels, cellWidth, cellHeight);
        return true;
    }

    public static void EnsureProjectCustomTexturesLoaded()
    {
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

    private static void LoadAtlas(Action<float, string>? progress = null)
    {
        progress?.Invoke(0f, "Clearing previous item textures...");

        foreach (Texture tex in Textures.Values)
            tex.Dispose();
        Textures.Clear();
        TilePixels.Clear();
        TileDimensions.Clear();

        string versionRoot = MinecraftDataLoader.GetVersionRoot();
        string atlasPath = Path.Combine(versionRoot, "gui", "items.png");

        bool loadedAnyBase = false;
        StbImage.stbi_set_flip_vertically_on_load(0);

        if (File.Exists(atlasPath))
        {
            ImageResult atlas;
            using (var stream = File.OpenRead(atlasPath))
                atlas = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            const int atlasSize = AtlasTiles * TileSize;
            if (atlas.Width == atlasSize && atlas.Height == atlasSize)
            {
                SliceGridAtlas(atlas.Data, atlasSize);
                loadedAnyBase = true;
                progress?.Invoke(0.12f, "Loaded legacy item sheet");
            }
            else
            {
                Console.WriteLine($"Ignoring legacy item atlas with unexpected size {atlas.Width}x{atlas.Height}: {atlasPath}");
            }
        }

        int modernCount = LoadModernVersionItemTextures(versionRoot,
            (value, detail) => progress?.Invoke(0.12f + value * 0.08f, detail));
        if (modernCount > 0)
            loadedAnyBase = true;

        if (!loadedAnyBase)
        {
            Console.WriteLine($"No base item textures found in version root: {versionRoot}");
        }

        progress?.Invoke(0.20f, "Loaded base item textures");

        ApplyResourcePackItemsOverrides((value, detail) => progress?.Invoke(0.20f + value * 0.75f, detail));
        EnsureProjectCustomTexturesLoaded();
        progress?.Invoke(1f, $"Loaded {Textures.Count} item texture(s)");

        Console.WriteLine($"Loaded {Textures.Count} tiles");
    }

    private static int LoadModernVersionItemTextures(string versionRoot, Action<float, string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(versionRoot))
            return 0;

        string? itemRoot = ResolveVersionItemRoot(versionRoot);
        if (string.IsNullOrWhiteSpace(itemRoot) || !Directory.Exists(itemRoot))
            return 0;

        string[] files;
        try
        {
            files = Directory
                .GetFiles(itemRoot, "*.png", SearchOption.AllDirectories)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to enumerate modern item textures: {ex.Message}");
            return 0;
        }

        int loaded = 0;
        for (int i = 0; i < files.Length; i++)
        {
            string filePath = files[i];
            string relative = Path.GetRelativePath(itemRoot, filePath)
                .Replace('\\', '/');
            string key = Path.ChangeExtension(relative, null) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                continue;

            ImageResult img;
            try
            {
                using var stream = File.OpenRead(filePath);
                img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load item texture '{relative}': {ex.Message}");
                continue;
            }

            if (img.Width <= 0 || img.Height <= 0)
                continue;

            UpsertTileTexture(key, img.Data, img.Width, img.Height);
            loaded++;

            float ratio = files.Length <= 0 ? 1f : (i + 1) / (float)files.Length;
            progress?.Invoke(ratio, $"Base item texture: {key}");
        }

        return loaded;
    }

    private static string? ResolveVersionItemRoot(string versionRoot)
    {
        string flattened = Path.Combine(versionRoot, "textures", "item");
        if (Directory.Exists(flattened))
            return flattened;

        string namespaced = Path.Combine(versionRoot, "assets", "minecraft", "textures", "item");
        if (Directory.Exists(namespaced))
            return namespaced;

        return null;
    }

    private static void ApplyResourcePackItemsOverrides(Action<float, string>? progress = null)
    {
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

            const int atlasSize = AtlasTiles * TileSize;
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

            string key = MinecraftDataLoader.BuildResourcePackTextureKey(file.PackName, $"{assetNamespace}/item/{textureKey}");
            UpsertTileTexture(key, img.Data, img.Width, img.Height);
            progress?.Invoke(1f, $"Mod item texture: {assetNamespace}/item/{textureKey}");
        }
    }

    private static void SliceGridAtlas(byte[] src, int atlasSize, string keyPrefix = "")
    {
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

    private static byte[] ExtractTile(byte[] rgbaPixels, int imageWidth, int startX, int startY, int tileWidth, int tileHeight)
    {
        var tilePixels = new byte[tileWidth * tileHeight * 4];

        for (int y = 0; y < tileHeight; y++)
        {
            int srcIndex = ((startY + y) * imageWidth + startX) * 4;
            int dstIndex = y * tileWidth * 4;
            System.Buffer.BlockCopy(rgbaPixels, srcIndex, tilePixels, dstIndex, tileWidth * 4);
        }

        return tilePixels;
    }

    private static void UpsertTileTexture(string key, byte[] pixels, int width, int height)
    {
        if (Textures.TryGetValue(key, out Texture? oldTexture))
            oldTexture.Dispose();

        Texture texture = VeldridTextureLoader.UploadRgba(pixels, (uint)width, (uint)height,
            nearest: true, generateMipmaps: false, repeat: true);

        Textures[key] = texture;
        TilePixels[key] = pixels;
        TileDimensions[key] = (width, height);
    }
}
