using System.Security.Cryptography;
using ImageMagick;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

internal static class ArmorTrimTexture
{
    internal static string TextureRoot(CharacterEntry entry) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry.FilePath)!, "..", "..", "..", "textures"));

    internal static string[] Patterns(CharacterEntry entry) =>
        Choices(Path.Combine(TextureRoot(entry), "trims", "entity", "humanoid"));

    internal static string[] Materials(CharacterEntry entry) =>
        Choices(Path.Combine(TextureRoot(entry), "palettes", "trim"))
            .Where(name => !name.EndsWith("_darker", StringComparison.Ordinal)).ToArray();

    private static string[] Choices(string directory) => Directory.EnumerateFiles(directory, "*.png")
        .Select(path => Path.GetFileNameWithoutExtension(path))
        .OrderBy(name => name, StringComparer.Ordinal).ToArray();

    internal static string Apply(CharacterEntry entry, string source, int slot, ArmorPieceSelection selection,
        bool leather)
    {
        bool hasTrim = selection.TrimPattern != "none" && selection.TrimMaterial != "none";
        if (!leather && !hasTrim) return source;
        try
        {
            using var image = Read(source);
            byte[] pixels = image.ToByteArray(MagickFormat.Rgba);
            int width = checked((int)image.Width), height = checked((int)image.Height);
            if (leather)
            {
                if (selection.LeatherColor > 0xFFFFFF)
                    throw new InvalidDataException("Leather color must be a 24-bit RGB color.");
                for (int p = 0; p < pixels.Length; p += 4)
                for (int c = 0; c < 3; c++)
                {
                    uint channel = (selection.LeatherColor >> (16 - c * 8)) & 255;
                    pixels[p + c] = (byte)((pixels[p + c] * channel + 127) / 255);
                }
                using var overlay = Read(Path.Combine(Path.GetDirectoryName(source)!,
                    Path.GetFileNameWithoutExtension(source) + "_overlay.png"));
                if (overlay.Width != image.Width || overlay.Height != image.Height)
                    throw new InvalidDataException("Leather armor and overlay textures have different sizes.");
                byte[] detail = overlay.ToByteArray(MagickFormat.Rgba);
                for (int p = 0; p < pixels.Length; p += 4)
                    Over(pixels, p, detail, p, detail[p + 3]);
            }
            if (hasTrim) ApplyTrim(entry, source, slot, selection, pixels, width, height);
            using var output = new MagickImage(pixels, new MagickReadSettings
                { Width = image.Width, Height = image.Height, Depth = 8, Format = MagickFormat.Rgba });
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(BitConverter.GetBytes(width));
            hash.AppendData(BitConverter.GetBytes(height));
            hash.AppendData(pixels);
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SimplyRemadeNuxi", "Armor");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "trim_" + Convert.ToHexString(hash.GetHashAndReset()) + ".png");
            if (!File.Exists(path)) output.Write(path, MagickFormat.Png32);
            return path;
        }
        catch (MagickException ex)
        {
            throw new InvalidDataException($"Could not generate armor texture: {ex.Message}", ex);
        }
    }

    private static void ApplyTrim(CharacterEntry entry, string source, int slot, ArmorPieceSelection selection,
        byte[] pixels, int width, int height)
    {
        if (!Patterns(entry).Contains(selection.TrimPattern, StringComparer.Ordinal) ||
            !Materials(entry).Contains(selection.TrimMaterial, StringComparer.Ordinal))
            throw new InvalidDataException("The selected armor trim pattern or material is unavailable.");
        string root = TextureRoot(entry);
        string layer = slot == 2 ? "humanoid_leggings" : "humanoid";
        string material = selection.TrimMaterial;
        if (Path.GetFileNameWithoutExtension(source) == material &&
            File.Exists(Path.Combine(root, "palettes", "trim", material + "_darker.png")))
            material += "_darker";
        using var pattern = Read(Path.Combine(root, "trims", "entity", layer, selection.TrimPattern + ".png"));
        using var palette = Read(Path.Combine(root, "palettes", "trim_base.png"));
        using var colors = Read(Path.Combine(root, "palettes", "trim", material + ".png"));
        byte[] trim = pattern.ToByteArray(MagickFormat.Rgba);
        byte[] from = palette.ToByteArray(MagickFormat.Rgba);
        byte[] to = colors.ToByteArray(MagickFormat.Rgba);
        if (from.Length != to.Length)
            throw new InvalidDataException("Armor trim palettes have different sizes.");
        var replacements = new Dictionary<int, int>();
        for (int i = 0; i < from.Length; i += 4)
            replacements[Key(from, i)] = i;
        // Trim UVs use the adult 64-wide atlas, including on baby armor.
        int trimWidth = checked((int)pattern.Width), trimHeight = checked((int)pattern.Height);
        int overlayHeight = Math.Min(height, checked(trimHeight * width / trimWidth));
        for (int y = 0; y < overlayHeight; y++)
        for (int x = 0; x < width; x++)
        {
            int t = ((y * trimWidth / width) * trimWidth + x * trimWidth / width) * 4;
            bool mapped = replacements.TryGetValue(Key(trim, t), out int color);
            Over(pixels, (y * width + x) * 4, mapped ? to : trim, mapped ? color : t, trim[t + 3]);
        }
    }

    private static void Over(byte[] pixels, int p, byte[] source, int s, int alpha)
    {
        if (alpha == 0) return;
        int outputAlpha = alpha * 255 + pixels[p + 3] * (255 - alpha);
        for (int c = 0; c < 3; c++)
            pixels[p + c] = (byte)((source[s + c] * alpha * 255 +
                pixels[p + c] * pixels[p + 3] * (255 - alpha) + outputAlpha / 2) / outputAlpha);
        pixels[p + 3] = (byte)((outputAlpha + 127) / 255);
    }

    private static MagickImage Read(string path)
    {
        var info = new MagickImageInfo(path);
        if (info.Width > 8192 || info.Height > 8192 || (long)info.Width * info.Height > 32 * 1024 * 1024)
            throw new InvalidDataException("Armor textures are limited to 8192 pixels per edge and 32 megapixels.");
        return new MagickImage(path) { Depth = 8 };
    }

    private static int Key(byte[] bytes, int offset) =>
        bytes[offset] << 16 | bytes[offset + 1] << 8 | bytes[offset + 2];
}
