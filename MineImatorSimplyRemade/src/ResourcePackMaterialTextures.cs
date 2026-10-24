using System.Text.Json;
using GlmSharp;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade;

/// <summary>Companion material images in the same UV space as the current diffuse texture.</summary>
public static class ResourcePackMaterialTextures
{
    private sealed record Pixels(byte[] Data, int Width, int Height, int FrameWidth, int FrameHeight);

    private sealed class Pack
    {
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Pixels?> Images = new(StringComparer.OrdinalIgnoreCase);
        public bool LabPbr;
        public string EmissiveSuffix = "";
    }

    private static GL? _gl;
    private static bool _indexed;
    private static uint _black;
    private static readonly Dictionary<string, Pack> Packs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string Pack, string Key, bool Item, bool Normal, uint Albedo), uint> Textures = new();
    private static readonly Dictionary<uint, Pixels?> DiffuseImages = new();
    private static readonly Dictionary<uint, vec3> EmissionAverages = new();
    private static readonly HashSet<uint> OwnedTextures = new();

    /// <summary>Call on the GL thread, with the owning context current, when atlases reload.</summary>
    public static void Initialize(GL gl)
    {
        if (ReferenceEquals(_gl, gl))
            foreach (uint texture in OwnedTextures)
                gl.DeleteTexture(texture);
        _gl = gl;
        Packs.Clear();
        Textures.Clear();
        DiffuseImages.Clear();
        EmissionAverages.Clear();
        OwnedTextures.Clear();
        _black = 0;
        _indexed = false;
    }

    public static vec3 GetEmissionAverage(uint textureId) =>
        EmissionAverages.TryGetValue(textureId, out vec3 average) ? average : vec3.Ones;

    public static uint GetTexture(string resourcePackId, string textureKey, bool item, bool normal, uint albedoTextureId = 0)
    {
        if (_gl == null) return 0;
        string packId = MinecraftDataLoader.NormalizeResourcePackId(resourcePackId);
        if (packId.Length == 0) return normal ? 0 : BlackTexture();
        string originalKey = textureKey ?? "";
        string key = originalKey.Replace('\\', '/').TrimStart('/');
        if (key.StartsWith("resourcepack:", StringComparison.OrdinalIgnoreCase))
        {
            int separator = key.IndexOf(':', "resourcepack:".Length);
            if (separator >= 0) key = key[(separator + 1)..];
        }
        var atlas = item ? ItemsAtlas.Textures : TerrainAtlas.Textures;
        if (albedoTextureId == 0) atlas.TryGetValue(originalKey, out albedoTextureId);
        var cacheKey = (packId, key, item, normal, albedoTextureId);
        if (Textures.TryGetValue(cacheKey, out uint cached)) return cached;
        EnsureIndexed();
        uint result = normal ? 0 : BlackTexture();
        if (Packs.TryGetValue(packId, out Pack? pack))
        {
            string path = AssetPath(key, item);
            Pixels? companion = Companion(pack, path, normal ? "_n" : "_e", key, item);
            bool packedEmission = false;
            if (!normal && companion == null && pack.EmissiveSuffix.Length > 0 && pack.EmissiveSuffix != "_e")
                companion = Companion(pack, path, pack.EmissiveSuffix, key, item);
            if (!normal && companion == null && pack.LabPbr)
            {
                companion = Companion(pack, path, "_s", key, item);
                packedEmission = companion != null;
            }
            if (companion != null)
            {
                Pixels? packAlbedo = Companion(pack, path, "", key, item);
                Pixels? diffuse = ReadDiffuse(albedoTextureId);
                Pixels target = diffuse ?? packAlbedo ?? companion;
                int frameWidth = target.FrameWidth, frameHeight = target.FrameHeight;
                if (!item && TerrainAtlas.AnimatedTextures.TryGetValue(originalKey, out var animation) &&
                    animation.FrameWidth > 0 && animation.FrameHeight > 0 &&
                    target.Width % animation.FrameWidth == 0 && target.Height % animation.FrameHeight == 0)
                {
                    frameWidth = animation.FrameWidth;
                    frameHeight = animation.FrameHeight;
                }
                else if (diffuse != null && packAlbedo != null &&
                         diffuse.Width == packAlbedo.Width && diffuse.Height == packAlbedo.Height)
                {
                    frameWidth = packAlbedo.FrameWidth;
                    frameHeight = packAlbedo.FrameHeight;
                }
                byte[] aligned = Align(companion, target.Width, target.Height, frameWidth, frameHeight);
                byte[] output;
                if (normal) output = DecodeNormalPixels(aligned, pack.LabPbr);
                else
                {
                    Pixels? albedo = packAlbedo ?? (diffuse == null ? null :
                        diffuse with { FrameWidth = frameWidth, FrameHeight = frameHeight });
                    byte[]? color = packedEmission && albedo != null
                        ? Align(albedo, target.Width, target.Height, frameWidth, frameHeight) : null;
                    output = DecodeEmissionPixels(aligned, color, packedEmission);
                }
                result = Upload(output, target.Width, target.Height);
                if (!normal) EmissionAverages[result] = Average(output);
            }
        }
        Textures[cacheKey] = result;
        return result;
    }

    private static string AssetPath(string key, bool item)
    {
        if (key.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) return key;
        string[] parts = key.Split('/');
        if (parts.Length >= 3 && (parts[1] == "block" || parts[1] == "item" || parts[1] == "entity"))
            return $"assets/{parts[0]}/textures/{string.Join('/', parts.Skip(1))}";
        if (key.StartsWith("entity/", StringComparison.OrdinalIgnoreCase))
            return "assets/minecraft/textures/" + key;
        return "assets/minecraft/textures/" + (item ? "item/" : "block/") + key;
    }

    private static Pixels? Companion(Pack pack, string path, string suffix, string key, bool item)
    {
        string[] coordinates = key.Split(',');
        if (!item || coordinates.Length != 2 || !int.TryParse(coordinates[0], out int column) ||
            !int.TryParse(coordinates[1], out int row) || column < 0 || column >= 16 || row < 0 || row >= 16)
            return Image(pack, path + suffix + ".png");
        string sheetPath = "assets/minecraft/textures/gui/items" + suffix + ".png";
        string cachePath = sheetPath + "#" + key;
        if (pack.Images.TryGetValue(cachePath, out Pixels? cached)) return cached;
        Pixels? sheet = Image(pack, sheetPath);
        Pixels? result = null;
        if (sheet != null && sheet.Width % 16 == 0 && sheet.Height % 16 == 0)
        {
            int width = sheet.Width / 16, height = sheet.Height / 16;
            byte[] data = new byte[checked(width * height * 4)];
            for (int y = 0; y < height; y++)
                System.Buffer.BlockCopy(sheet.Data, ((row * height + y) * sheet.Width + column * width) * 4,
                    data, y * width * 4, width * 4);
            result = new Pixels(data, width, height, width, height);
        }
        pack.Images[cachePath] = result;
        return result;
    }

    private static void EnsureIndexed()
    {
        if (_indexed) return;
        foreach (string suffix in new[] { ".png", ".png.mcmeta", ".properties" })
        {
            foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("", suffix))
            {
                string path = file.RelativePath.Replace('\\', '/').TrimStart('/');
                if (suffix == ".png" && !path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) continue;
                if (suffix == ".properties" && !path.EndsWith("/texture.properties", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith("/emissive.properties", StringComparison.OrdinalIgnoreCase)) continue;
                string id = MinecraftDataLoader.GetResourcePackId(file.PackName);
                if (!Packs.TryGetValue(id, out Pack? pack)) Packs[id] = pack = new Pack();
                // Atlas upserts use the last matching container when IDs collide.
                pack.Files[path] = file.Data;
            }
        }
        foreach (Pack pack in Packs.Values)
        {
            foreach (string path in new[] { "assets/minecraft/shaders/texture.properties", "shaders/texture.properties", "assets/minecraft/optifine/texture.properties" })
                if (pack.Files.TryGetValue(path, out byte[]? data) && Property(data, "format") is string format)
                    pack.LabPbr = format.Equals("lab-pbr/1.3", StringComparison.OrdinalIgnoreCase);
            foreach (string path in new[] { "assets/minecraft/mcpatcher/emissive.properties", "assets/minecraft/optifine/emissive.properties" })
                if (pack.Files.TryGetValue(path, out byte[]? data) && Property(data, "suffix.emissive") is string suffix)
                    pack.EmissiveSuffix = suffix;
        }
        _indexed = true;
    }

    private static string? Property(byte[] data, string name)
    {
        foreach (string line in MinecraftDataLoader.DecodeUtf8(data).Split('\n'))
        {
            string text = line.Trim().TrimStart('\uFEFF');
            if (text.Length == 0 || text[0] == '#' || text[0] == '!') continue;
            int separator = text.IndexOfAny(new[] { '=', ':' });
            if (separator >= 0 && text[..separator].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return text[(separator + 1)..].Trim();
        }
        return null;
    }

    private static Pixels? Image(Pack pack, string path)
    {
        if (pack.Images.TryGetValue(path, out Pixels? cached)) return cached;
        Pixels? result = null;
        if (pack.Files.TryGetValue(path, out byte[]? bytes))
        {
            try
            {
                StbImage.stbi_set_flip_vertically_on_load(0);
                var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
                int fw = image.Width, fh = image.Height;
                if (pack.Files.TryGetValue(path + ".mcmeta", out byte[]? metadata))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(metadata);
                        if (document.RootElement.TryGetProperty("animation", out var animation))
                        {
                            fw = animation.TryGetProperty("width", out var width) ? width.GetInt32() : image.Width;
                            fh = animation.TryGetProperty("height", out var height) ? height.GetInt32() : fw;
                        }
                    }
                    catch (Exception) { fw = image.Width; fh = image.Height; }
                    if (fw <= 0 || fh <= 0 || image.Width % fw != 0 || image.Height % fh != 0)
                    { fw = image.Width; fh = image.Height; }
                }
                result = new Pixels(image.Data, image.Width, image.Height, fw, fh);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to decode material texture '{path}': {ex.Message}");
            }
        }
        pack.Images[path] = result;
        return result;
    }

    // Preserve frame boundaries: a single companion tile repeats for every diffuse frame.
    private static byte[] Align(Pixels source, int width, int height, int frameWidth, int frameHeight)
    {
        // Some packs omit companion sidecars for sheets whose layout matches the albedo.
        if (source.FrameWidth == source.Width && source.FrameHeight == source.Height &&
            (long)source.Width * height == (long)source.Height * width &&
            (long)source.Width * frameWidth % width == 0 && (long)source.Height * frameHeight % height == 0)
            source = source with
            {
                FrameWidth = (int)((long)source.Width * frameWidth / width),
                FrameHeight = (int)((long)source.Height * frameHeight / height)
            };
        byte[] result = new byte[checked(width * height * 4)];
        int sourceColumns = source.Width / source.FrameWidth;
        int sourceFrames = sourceColumns * (source.Height / source.FrameHeight);
        int columns = width / frameWidth;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int frame = ((y / frameHeight) * columns + x / frameWidth) % sourceFrames;
            int sx = (frame % sourceColumns) * source.FrameWidth + (x % frameWidth) * source.FrameWidth / frameWidth;
            int sy = (frame / sourceColumns) * source.FrameHeight + (y % frameHeight) * source.FrameHeight / frameHeight;
            System.Buffer.BlockCopy(source.Data, (sy * source.Width + sx) * 4, result, (y * width + x) * 4, 4);
        }
        return result;
    }

    public static byte[] DecodeNormalPixels(byte[] rgba, bool labPbr)
    {
        if (rgba.Length % 4 != 0) throw new ArgumentException("Expected RGBA pixels.", nameof(rgba));
        byte[] result = (byte[])rgba.Clone();
        for (int i = 0; i < result.Length; i += 4)
        {
            if (labPbr)
            {
                float x = rgba[i] / 127.5f - 1f;
                float y = rgba[i + 1] / 127.5f - 1f;
                result[i + 1] = (byte)(255 - rgba[i + 1]);
                result[i + 2] = (byte)Math.Clamp((int)MathF.Round((MathF.Sqrt(MathF.Max(0, 1 - x * x - y * y)) + 1) * 127.5f), 0, 255);
            }
            result[i + 3] = 255;
        }
        return result;
    }

    public static byte[] DecodeEmissionPixels(byte[] rgba, byte[]? albedo, bool labPbr)
    {
        if (rgba.Length % 4 != 0 || (albedo != null && albedo.Length != rgba.Length))
            throw new ArgumentException("Expected equally sized RGBA images.");
        byte[] result = new byte[rgba.Length];
        for (int i = 0; i < result.Length; i += 4)
        {
            float strength = labPbr ? (rgba[i + 3] == 255 ? 0 : rgba[i + 3] / 254f) : rgba[i + 3] / 255f;
            for (int c = 0; c < 3; c++)
                result[i + c] = (byte)Math.Clamp((int)MathF.Round((labPbr ? albedo?[i + c] ?? 0 : rgba[i + c]) * strength), 0, 255);
            result[i + 3] = 255;
        }
        return result;
    }

    private static vec3 Average(byte[] pixels)
    {
        double r = 0, g = 0, b = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        { r += pixels[i]; g += pixels[i + 1]; b += pixels[i + 2]; }
        double scale = 255.0 * (pixels.Length / 4);
        return new vec3((float)(r / scale), (float)(g / scale), (float)(b / scale));
    }

    private static uint BlackTexture()
    {
        if (_black == 0)
        {
            _black = Upload(new byte[] { 0, 0, 0, 255 }, 1, 1);
            EmissionAverages[_black] = vec3.Zero;
        }
        return _black;
    }

    private static unsafe Pixels? ReadDiffuse(uint texture)
    {
        if (texture == 0) return null;
        if (DiffuseImages.TryGetValue(texture, out Pixels? cached)) return cached;
        GL gl = _gl!;
        int binding = gl.GetInteger(GLEnum.TextureBinding2D);
        Pixels? result = null;
        try
        {
            gl.BindTexture(GLEnum.Texture2D, texture);
            gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureWidth, out int width);
            gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureHeight, out int height);
            if (width > 0 && height > 0)
            {
                byte[] data = new byte[checked(width * height * 4)];
                WithPixelStore(true, () =>
                {
                    fixed (byte* pointer = data) gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
                });
                result = new Pixels(data, width, height, width, height);
            }
        }
        finally { gl.BindTexture(GLEnum.Texture2D, (uint)binding); }
        DiffuseImages[texture] = result;
        return result;
    }

    private static unsafe uint Upload(byte[] data, int width, int height)
    {
        GL gl = _gl!;
        int binding = gl.GetInteger(GLEnum.TextureBinding2D);
        uint texture = gl.GenTexture();
        try
        {
            gl.BindTexture(GLEnum.Texture2D, texture);
            WithPixelStore(false, () =>
            {
                fixed (byte* pointer = data)
                    gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, GLEnum.UnsignedByte, pointer);
            });
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.Repeat);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.Repeat);
            OwnedTextures.Add(texture);
            return texture;
        }
        catch { gl.DeleteTexture(texture); throw; }
        finally { gl.BindTexture(GLEnum.Texture2D, (uint)binding); }
    }

    private static void WithPixelStore(bool read, Action action)
    {
        GL gl = _gl!;
        GLEnum bufferTarget = read ? GLEnum.PixelPackBuffer : GLEnum.PixelUnpackBuffer;
        GLEnum bufferBinding = read ? GLEnum.PixelPackBufferBinding : GLEnum.PixelUnpackBufferBinding;
        GLEnum[] stores = read
            ? new[] { GLEnum.PackAlignment, GLEnum.PackRowLength, GLEnum.PackSkipPixels, GLEnum.PackSkipRows }
            : new[] { GLEnum.UnpackAlignment, GLEnum.UnpackRowLength, GLEnum.UnpackSkipPixels, GLEnum.UnpackSkipRows };
        int buffer = gl.GetInteger(bufferBinding);
        int[] previous = stores.Select(store => gl.GetInteger(store)).ToArray();
        try
        {
            gl.BindBuffer(bufferTarget, 0);
            for (int i = 0; i < stores.Length; i++) gl.PixelStore(stores[i], i == 0 ? 1 : 0);
            action();
        }
        finally
        {
            for (int i = 0; i < stores.Length; i++) gl.PixelStore(stores[i], previous[i]);
            gl.BindBuffer(bufferTarget, (uint)buffer);
        }
    }
}
