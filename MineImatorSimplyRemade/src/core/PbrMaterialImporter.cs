using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StbImageSharp;

namespace MineImatorSimplyRemade.core;

public sealed record PbrMaterialImportResult(
    string AlbedoPath, string NormalPath, string EmissionPath, string SurfacePath,
    bool HasRoughness, bool HasMetallic, bool HasHeight, string Summary)
{
    // Original image sources; packed ARM/RMA images may supply more than one component.
    public string? OcclusionPath { get; init; } = "";
    public string? RoughnessPath { get; init; } = "";
    public string? MetallicPath { get; init; } = "";
    public string? HeightPath { get; init; } = "";
}

/// <summary>CPU-only, linear-data texture-set import. Source files are never modified.</summary>
public static class PbrMaterialImporter
{
    private const int MaxDimension = 8192;
    private const long MaxPixels = 32 * 1024 * 1024;
    private const long MaxEncodedBytes = 256 * 1024 * 1024;
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif", ".hdr", ".exr"];
    private static readonly Regex Resolution = new(@"(?<resolution>[_\-. ](?:\d+[kK]|\d{3,5}(?:x\d{3,5})?))$", RegexOptions.CultureInvariant);
    private static readonly Regex MapSuffix = new(
        @"^(?<stem>.+?)[_\-. ](?<map>basecolor|roughness|metalness|normalgl|normaldx|displacement|emissive|occlusion|diffuse|metallic|emission|normal|nor_gl|nor_dx|albedo|height|rough|metal|color|diff|disp|arm|orm|rma|ao)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed record Source(string Path, string Stem, string Resolution, string Kind, bool DirectX);
    private sealed record Pixels(int Width, int Height, byte[] Data);

    /// <summary>
    /// Matches suffix-tagged siblings with the same stem and resolution. Empty strings denote
    /// absent color/normal/emission maps; SurfacePath always names a persistent RGBA PNG.
    /// Output uses albedo dimensions, otherwise the largest source; smaller maps are bilinear
    /// resampled in linear byte space. Inputs are limited to 8192 per edge and 32 megapixels.
    /// Throws for missing files, unrecognized sets, corrupt images, and oversized images.
    /// </summary>
    public static PbrMaterialImportResult Import(string selectedTexturePath)
    {
        string selected = ValidatePath(selectedTexturePath);

        Source anchor = Parse(selected);
        var sources = Directory.EnumerateFiles(Path.GetDirectoryName(selected)!)
            .Where(IsSupported).Select(Parse)
            .Where(s => s.Kind.Length > 0 && string.Equals(s.Stem, anchor.Stem, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Resolution, anchor.Resolution, StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.Kind)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => string.Equals(s.Path, selected, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(s => s.DirectX ? 1 : 0).ThenBy(s => ExtensionRank(s.Path))
                .ThenBy(s => s.Path, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Path, StringComparer.Ordinal).First());
        if (sources.Count == 0)
            throw new InvalidDataException("No recognized PBR map suffixes match the selected material stem and resolution.");

        // Hash actual contents, not timestamps, so replacement sources cannot reuse stale pixels.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("PbrMaterialImporter-v3\0"));
        var images = new Dictionary<string, Pixels>();
        long totalPixels = 0;
        foreach (var entry in sources.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            Source source = entry.Value;
            hash.AppendData(Encoding.UTF8.GetBytes(entry.Key + "\0"));
            Pixels image = ReadTexture(source.Path, hash);
            totalPixels += (long)image.Width * image.Height;
            if (totalPixels > MaxPixels * 4)
                throw new InvalidDataException("Material set exceeds the 128 megapixel decoded-size limit.");
            images.Add(entry.Key, image);
        }

        string cache = CacheDirectory(hash);

        string ColorPath(string kind)
        {
            if (!sources.TryGetValue(kind, out Source source)) return "";
            if (!RequiresConversion(source.Path)) return source.Path;
            Pixels image = images[kind];
            ConvertPixels(image, color: true, directXNormal: false);
            return CachePng(cache, kind, image);
        }

        string normalPath = "";
        if (sources.TryGetValue("normal", out Source normal))
        {
            if (normal.DirectX || RequiresConversion(normal.Path))
            {
                Pixels image = images["normal"];
                ConvertPixels(image, color: false, directXNormal: normal.DirectX);
                normalPath = CachePng(cache, "normal", image);
            }
            else normalPath = normal.Path;
        }

        // Prefer color dimensions; otherwise use the largest source, with deterministic ties.
        Pixels reference = images.TryGetValue("albedo", out Pixels albedo) ? albedo
            : images.OrderByDescending(e => (long)e.Value.Width * e.Value.Height)
                .ThenBy(e => e.Key, StringComparer.Ordinal).First().Value;
        int width = reference.Width, height = reference.Height;
        var surface = new byte[checked(width * height * 4)];
        images.TryGetValue("packed", out Pixels packed);
        bool rma = sources.TryGetValue("packed", out Source packedSource)
            && ParseMapAlias(packedSource.Path) == "rma";
        images.TryGetValue("ao", out Pixels ao);
        images.TryGetValue("roughness", out Pixels roughness);
        images.TryGetValue("metallic", out Pixels metallic);
        images.TryGetValue("height", out Pixels displacement);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            surface[i] = Sample(ao ?? packed, x, y, width, height, ao != null ? 0 : rma ? 2 : 0, 255);
            surface[i + 1] = Sample(roughness ?? packed, x, y, width, height, roughness != null ? 0 : rma ? 0 : 1, 255);
            surface[i + 2] = Sample(metallic ?? packed, x, y, width, height, metallic != null ? 0 : rma ? 1 : 2, 255);
            surface[i + 3] = Sample(displacement, x, y, width, height, 0, 128);
        }

        string surfacePath = CachePng(cache, "surface", new Pixels(width, height, surface));
        return new PbrMaterialImportResult(ColorPath("albedo"), normalPath, ColorPath("emission"), surfacePath,
            roughness != null || packed != null, metallic != null || packed != null, displacement != null,
            $"{anchor.Stem}: {sources.Count} maps, {width}x{height} surface (AO / roughness / metallic / height).")
        {
            OcclusionPath = sources.TryGetValue("ao", out Source aoSource) ? aoSource.Path : packedSource?.Path ?? "",
            RoughnessPath = sources.TryGetValue("roughness", out Source roughnessSource) ? roughnessSource.Path : packedSource?.Path ?? "",
            MetallicPath = sources.TryGetValue("metallic", out Source metallicSource) ? metallicSource.Path : packedSource?.Path ?? "",
            HeightPath = sources.TryGetValue("height", out Source heightSource) ? heightSource.Path : ""
        };
    }

    /// <summary>
    /// Replaces one packed surface channel using the selected image's linear red channel.
    /// Channels are AO=0, roughness=1, metallic=2, height=3. An empty source clears only that
    /// channel to 255/255/255/128. An existing pack keeps its dimensions and other bytes;
    /// without one, a neutral pack is initialized at the selected image's dimensions.
    /// Empty existing and source paths return an empty path. No sibling discovery is performed.
    /// </summary>
    public static string SetSurfaceMap(string existingSurfacePath, int channel, string sourcePath)
    {
        if (channel is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(channel), "Surface channel must be between 0 and 3.");
        if (string.IsNullOrEmpty(existingSurfacePath) && string.IsNullOrEmpty(sourcePath)) return "";

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"PbrMaterialImporter-surface-v1\0{channel}\0existing\0"));
        Pixels existing = string.IsNullOrEmpty(existingSurfacePath) ? null : ReadTexture(existingSurfacePath, hash);
        hash.AppendData(Encoding.UTF8.GetBytes("source\0"));
        Pixels source = string.IsNullOrEmpty(sourcePath) ? null : ReadTexture(sourcePath, hash);
        Pixels reference = existing ?? source!;
        int width = reference.Width, height = reference.Height;
        byte[] data = existing?.Data ?? new byte[checked(width * height * 4)];
        if (existing == null)
            for (int i = 0; i < data.Length; i += 4)
            {
                data[i] = data[i + 1] = data[i + 2] = 255;
                data[i + 3] = 128;
            }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            data[(y * width + x) * 4 + channel] = Sample(source, x, y, width, height, 0, channel == 3 ? (byte)128 : (byte)255);

        return CachePng(CacheDirectory(hash), "surface", new Pixels(width, height, data));
    }

    /// <summary>
    /// Validates only this image and returns its full path, or a cached PNG for HDR/EXR or
    /// explicit DX conversion. Color HDR/EXR RGB is sRGB-encoded; data remains linear and
    /// alpha is untouched. DX conversion inverts green. Filenames have no semantic meaning.
    /// </summary>
    public static string ImportTexture(string sourcePath, bool color = false, bool directXNormal = false)
    {
        string path = ValidatePath(sourcePath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"PbrMaterialImporter-texture-v1\0{color}\0{directXNormal}\0"));
        Pixels image = ReadTexture(path, hash);
        bool convert = RequiresConversion(path);
        if (!convert && !directXNormal) return path;
        ConvertPixels(image, color && convert, directXNormal);
        return CachePng(CacheDirectory(hash), "texture", image);
    }

    private static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Selected material texture does not exist.", path);
        if (!IsSupported(path)) throw new NotSupportedException("Supported material textures: PNG, JPG/JPEG, BMP, TGA, GIF, HDR, EXR.");
        return path;
    }

    private static Pixels ReadTexture(string path, IncrementalHash hash)
    {
        path = ValidatePath(path);
        if (new FileInfo(path).Length > MaxEncodedBytes)
            throw new InvalidDataException("Material texture exceeds the 256 MiB encoded-size limit.");
        byte[] encoded = File.ReadAllBytes(path);
        hash.AppendData(Encoding.UTF8.GetBytes(path + "\0"));
        hash.AppendData(SHA256.HashData(encoded));
        return Decode(path, encoded);
    }

    private static string CacheDirectory(IncrementalHash hash)
    {
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MineImatorSimplyRemade", "PbrMaterials", Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        Directory.CreateDirectory(cache);
        return cache;
    }

    private static bool RequiresConversion(string path) => Path.GetExtension(path).ToLowerInvariant() is ".exr" or ".hdr";

    private static void ConvertPixels(Pixels image, bool color, bool directXNormal)
    {
        if (directXNormal)
            for (int i = 1; i < image.Data.Length; i += 4) image.Data[i] = (byte)(255 - image.Data[i]);
        if (!color) return;
        // Only color maps need the regular loader's sRGB transfer. Data and alpha stay linear.
        for (int i = 0; i < image.Data.Length; i++)
        {
            if ((i & 3) == 3) continue;
            float linear = image.Data[i] / 255f;
            float srgb = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
            image.Data[i] = (byte)Math.Clamp((int)(srgb * 255f + 0.5f), 0, 255);
        }
    }

    private static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string ParseMapAlias(string path)
    {
        string name = Resolution.Replace(Path.GetFileNameWithoutExtension(path), "");
        return MapSuffix.Match(name).Groups["map"].Value.ToLowerInvariant();
    }

    private static Source Parse(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        Match resolution = Resolution.Match(name);
        if (resolution.Success) name = name[..resolution.Index];
        Match match = MapSuffix.Match(name);
        string alias = match.Groups["map"].Value.ToLowerInvariant();
        string kind = alias switch
        {
            "diffuse" or "diff" or "albedo" or "basecolor" or "color" => "albedo",
            "normal" or "nor_gl" or "normalgl" or "nor_dx" or "normaldx" => "normal",
            "rough" or "roughness" => "roughness",
            "metal" or "metallic" or "metalness" => "metallic",
            "ao" or "occlusion" => "ao",
            "disp" or "displacement" or "height" => "height",
            "arm" or "orm" or "rma" => "packed",
            "emission" or "emissive" => "emission",
            _ => ""
        };
        return new Source(path, match.Success ? match.Groups["stem"].Value : name,
            resolution.Value.Length > 0 ? resolution.Value[1..] : "", kind, alias is "nor_dx" or "normaldx");
    }

    private static int ExtensionRank(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".exr" => 0, ".png" => 1, ".hdr" => 2, ".tga" => 3, ".bmp" => 4, ".gif" => 5, _ => 6
    };

    private static Pixels Decode(string path, byte[] encoded)
    {
        if (Path.GetExtension(path).Equals(".exr", StringComparison.OrdinalIgnoreCase))
        {
            var exr = ExrTextureDecoder.Decode(encoded);
            ValidateDimensions(exr.Width, exr.Height);
            if (exr.Data.LongLength != (long)exr.Width * exr.Height * 4)
                throw new InvalidDataException("EXR decoder returned invalid RGBA data.");
            return new Pixels(exr.Width, exr.Height, exr.Data);
        }
        using var stream = new MemoryStream(encoded, false);
        ImageInfo? info = ImageInfo.FromStream(stream);
        if (info == null) throw new InvalidDataException("Invalid material image: " + path);
        ValidateDimensions(info.Value.Width, info.Value.Height);
        // stb's C# "thread" flags are actually static. Undo its flip instead of changing
        // shared loader settings and leaving the renderer with a different orientation.
        bool flipped = (StbImage.stbi__vertically_flip_on_load_set != 0
            ? StbImage.stbi__vertically_flip_on_load_local : StbImage.stbi__vertically_flip_on_load_global) != 0;
        if (Path.GetExtension(path).Equals(".hdr", StringComparison.OrdinalIgnoreCase))
        {
            var hdr = ImageResultFloat.FromMemory(encoded, ColorComponents.RedGreenBlueAlpha);
            var bytes = new byte[hdr.Data.Length];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = float.IsFinite(hdr.Data[i]) ? (byte)(Math.Clamp(hdr.Data[i], 0f, 1f) * 255f + 0.5f) : (byte)0;
            return Unflip(new Pixels(hdr.Width, hdr.Height, bytes), flipped);
        }
        var image = ImageResult.FromMemory(encoded, ColorComponents.RedGreenBlueAlpha);
        return Unflip(new Pixels(image.Width, image.Height, image.Data), flipped);
    }

    private static Pixels Unflip(Pixels image, bool flipped)
    {
        if (!flipped) return image;
        int stride = image.Width * 4;
        for (int y = 0; y < image.Height / 2; y++)
            for (int x = 0; x < stride; x++)
            {
                int top = y * stride + x, bottom = (image.Height - 1 - y) * stride + x;
                (image.Data[top], image.Data[bottom]) = (image.Data[bottom], image.Data[top]);
            }
        return image;
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            throw new InvalidDataException("Material texture exceeds the 8192 dimension / 32 megapixel limit.");
    }

    private static byte Sample(Pixels image, int x, int y, int width, int height, int channel, byte fallback)
    {
        if (image == null) return fallback;
        float sx = Math.Clamp((x + 0.5f) * image.Width / width - 0.5f, 0, image.Width - 1);
        float sy = Math.Clamp((y + 0.5f) * image.Height / height - 0.5f, 0, image.Height - 1);
        int x0 = (int)sx, y0 = (int)sy;
        int x1 = Math.Min(x0 + 1, image.Width - 1), y1 = Math.Min(y0 + 1, image.Height - 1);
        float tx = sx - x0, ty = sy - y0;
        float top = image.Data[(y0 * image.Width + x0) * 4 + channel] * (1 - tx) + image.Data[(y0 * image.Width + x1) * 4 + channel] * tx;
        float bottom = image.Data[(y1 * image.Width + x0) * 4 + channel] * (1 - tx) + image.Data[(y1 * image.Width + x1) * 4 + channel] * tx;
        return (byte)(top * (1 - ty) + bottom * ty + 0.5f);
    }

    private static string CachePng(string directory, string name, Pixels image)
    {
        string path = Path.Combine(directory, name + ".png");
        if (File.Exists(path)) return path;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(temporary)) WritePng(output, image);
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path)) { } // A concurrent import already published this content.
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return path;
    }

    private static void WritePng(Stream output, Pixels image)
    {
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), image.Height);
        header[8] = 8;
        header[9] = 6; // RGBA8, retaining height in alpha.
        WriteChunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            for (int y = 0; y < image.Height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(image.Data, y * image.Width * 4, image.Width * 4);
            }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        byte[] tag = Encoding.ASCII.GetBytes(type);
        output.Write(tag);
        output.Write(data);
        uint crc = 0xffffffff;
        foreach (byte b in tag) crc = UpdateCrc(crc, b);
        foreach (byte b in data) crc = UpdateCrc(crc, b);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        return crc;
    }
}
