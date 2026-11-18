using System.Text.Json;
using MineImatorSimplyRemadeNuxi.core.objs;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

public sealed class BannerPatternLayer
{
    public string Pattern { get; set; } = "stripe_top";
    public string Color { get; set; } = "white";

    public BannerPatternLayer Clone() => new() { Pattern = Pattern, Color = Color };
}

public static class BannerPatternSystem
{
    public static IReadOnlyList<string> Colors { get; } =
    [
        "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
        "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"
    ];

    public static IReadOnlyList<string> Patterns { get; } =
    [
        "border", "bricks", "circle", "creeper", "cross", "curly_border", "diagonal_left",
        "diagonal_right", "diagonal_up_left", "diagonal_up_right", "flow", "flower",
        "globe", "gradient", "gradient_up", "guster", "half_horizontal",
        "half_horizontal_bottom", "half_vertical", "half_vertical_right", "mojang",
        "piglin", "rhombus", "skull", "small_stripes", "square_bottom_left",
        "square_bottom_right", "square_top_left", "square_top_right", "straight_cross",
        "stripe_bottom", "stripe_center", "stripe_downleft", "stripe_downright",
        "stripe_left", "stripe_middle", "stripe_right", "stripe_top", "triangle_bottom",
        "triangle_top", "triangles_bottom", "triangles_top"
    ];

    private static readonly Dictionary<string, uint> ColorValues = new(StringComparer.Ordinal)
    {
        ["white"] = 0xF9FFFE, ["orange"] = 0xF9801D, ["magenta"] = 0xC74EBD,
        ["light_blue"] = 0x3AB3DA, ["yellow"] = 0xFED83D, ["lime"] = 0x80C71F,
        ["pink"] = 0xF38BAA, ["gray"] = 0x474F52, ["light_gray"] = 0x9D9D97,
        ["cyan"] = 0x169C9C, ["purple"] = 0x8932B8, ["blue"] = 0x3C44AA,
        ["brown"] = 0x835432, ["green"] = 0x5E7C16, ["red"] = 0xB02E26,
        ["black"] = 0x1D1D21
    };

    private static readonly Dictionary<string, string> LegacyPatternNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "base", ["bl"] = "square_bottom_left", ["br"] = "square_bottom_right",
        ["tl"] = "square_top_left", ["tr"] = "square_top_right", ["bs"] = "stripe_bottom",
        ["ts"] = "stripe_top", ["ls"] = "stripe_left", ["rs"] = "stripe_right",
        ["cs"] = "stripe_center", ["ms"] = "stripe_middle", ["drs"] = "stripe_downright",
        ["dls"] = "stripe_downleft", ["ss"] = "small_stripes", ["cr"] = "cross",
        ["sc"] = "straight_cross", ["bt"] = "triangle_bottom", ["tt"] = "triangle_top",
        ["bts"] = "triangles_bottom", ["tts"] = "triangles_top", ["ld"] = "diagonal_left",
        ["rud"] = "diagonal_up_right", ["lud"] = "diagonal_up_left", ["rd"] = "diagonal_right",
        ["mc"] = "circle", ["mr"] = "rhombus", ["vh"] = "half_vertical",
        ["hh"] = "half_horizontal", ["vhr"] = "half_vertical_right",
        ["hhb"] = "half_horizontal_bottom", ["cre"] = "creeper", ["gra"] = "gradient",
        ["gru"] = "gradient_up", ["bri"] = "bricks", ["sku"] = "skull",
        ["flo"] = "flower", ["moj"] = "mojang", ["glb"] = "globe", ["pig"] = "piglin",
        ["bo"] = "border", ["cbo"] = "curly_border"
    };

    public static bool IsBannerBlock(string name) =>
        name.EndsWith("_banner", StringComparison.Ordinal);

    public static string BaseColorForBlock(string name)
    {
        string color = name;
        if (color.EndsWith("_wall_banner", StringComparison.Ordinal))
            color = color[..^"_wall_banner".Length];
        else if (color.EndsWith("_banner", StringComparison.Ordinal))
            color = color[..^"_banner".Length];
        return NormalizeColor(color);
    }

    public static string NormalizeColor(string? color)
    {
        if (!string.IsNullOrWhiteSpace(color))
        {
            color = color.Replace("minecraft:", "", StringComparison.Ordinal).ToLowerInvariant();
            if (ColorValues.ContainsKey(color)) return color;
        }
        return "white";
    }

    public static string NormalizePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return Patterns[0];
        pattern = pattern.Replace("minecraft:", "", StringComparison.Ordinal).ToLowerInvariant();
        if (LegacyPatternNames.TryGetValue(pattern, out string? longName)) pattern = longName;
        return Patterns.Contains(pattern, StringComparer.Ordinal) ? pattern : Patterns[0];
    }

    public static uint ColorValue(string color) => ColorValues[NormalizeColor(color)];

    public static void ApplyToScene(SceneObject root, string baseColor, IEnumerable<BannerPatternLayer> layers)
    {
        var layerList = layers.Select(layer => new BannerPatternLayer
        {
            Pattern = NormalizePattern(layer.Pattern), Color = NormalizeColor(layer.Color)
        }).ToList();
        root.HasBannerPattern = true;
        root.BannerBaseColor = NormalizeColor(baseColor);
        root.BannerPatternLayers = layerList;
        byte[] pixels = Compose(root.BannerBaseColor, layerList);
        uint texture = MineImatorLoader.Instance.LoadTextureFromRgba(pixels, 64, 64);
        if (texture == 0)
            throw new InvalidOperationException("Could not upload the generated banner texture.");
        MineImatorLoader.ApplyTextureOverrideToScene(root, texture);
    }

    public static bool TryApplyMineImator(SceneObject root, MiTemplate template, MiTimeline timeline)
    {
        if (!TryReadMineImator(template, timeline, out string baseColor, out var layers)) return false;
        ApplyToScene(root, baseColor, layers);
        return true;
    }

    public static bool TryReadMineImator(MiTemplate template, MiTimeline timeline,
        out string baseColor, out List<BannerPatternLayer> layers)
    {
        JsonElement? type = Read(timeline.ExtraValues, template.ExtraValues, "pattern_type");
        JsonElement? legacyBanner = Read(timeline.ExtraValues, template.ExtraValues, "is_banner");
        string patternType = StringValue(type);
        if (!string.IsNullOrEmpty(patternType) && patternType != "banner")
        {
            baseColor = "white";
            layers = [];
            return false;
        }
        bool banner = patternType == "banner" ||
            legacyBanner is { } value && value.ValueKind is JsonValueKind.True or JsonValueKind.Number &&
                (value.ValueKind == JsonValueKind.True || value.GetDouble() != 0);
        JsonElement? baseValue = Read(timeline.ExtraValues, template.ExtraValues,
            "pattern_base_color", "banner_base_color");
        if (!banner && baseValue == null)
        {
            baseColor = "white";
            layers = [];
            return false;
        }

        var patterns = ReadArray(Read(timeline.ExtraValues, template.ExtraValues,
            "pattern_pattern_list", "banner_pattern_list"));
        var colors = ReadArray(Read(timeline.ExtraValues, template.ExtraValues,
            "pattern_color_list", "banner_color_list"));
        layers = [];
        for (int i = 0; i < Math.Min(patterns.Count, colors.Count); i++)
            layers.Add(new BannerPatternLayer
            {
                Pattern = NormalizePattern(patterns[i]),
                Color = NormalizeColor(colors[i])
            });
        baseColor = NormalizeColor(StringValue(baseValue));
        return true;
    }

    internal static byte[] Compose(string baseColor, IReadOnlyList<BannerPatternLayer> layers)
        => Compose(baseColor, layers, MinecraftDataLoader.GetVersionRoot());

    internal static byte[] Compose(string baseColor, IReadOnlyList<BannerPatternLayer> layers, string versionRoot)
    {
        string root = Path.Combine(versionRoot, "textures", "entity", "banner");
        var result = ReadImage(Path.Combine(root, "banner_base.png"));
        ApplyMask(result.Pixels, result.Width, result.Height,
            ReadImage(Path.Combine(root, "base.png")), ColorValue(baseColor));
        foreach (var layer in layers)
            ApplyMask(result.Pixels, result.Width, result.Height,
                ReadImage(Path.Combine(root, NormalizePattern(layer.Pattern) + ".png")),
                ColorValue(layer.Color));
        return result.Pixels;
    }

    private static (byte[] Pixels, int Width, int Height) ReadImage(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        if (image.Width != 64 || image.Height != 64)
            throw new InvalidDataException($"Banner texture '{path}' must be 64x64.");
        return (image.Data, image.Width, image.Height);
    }

    private static void ApplyMask(byte[] destination, int width, int height,
        (byte[] Pixels, int Width, int Height) mask, uint color)
    {
        if (mask.Width != width || mask.Height != height || mask.Pixels.Length != destination.Length)
            throw new InvalidDataException("Banner pattern mask dimensions do not match the banner texture.");
        float red = ((color >> 16) & 255) / 255f;
        float green = ((color >> 8) & 255) / 255f;
        float blue = (color & 255) / 255f;
        for (int i = 0; i < destination.Length; i += 4)
        {
            float sourceAlpha = mask.Pixels[i + 3] / 255f;
            if (sourceAlpha <= 0f) continue;
            float intensity = (mask.Pixels[i] + mask.Pixels[i + 1] + mask.Pixels[i + 2]) / (3f * 255f);
            float destAlpha = destination[i + 3] / 255f;
            float outAlpha = sourceAlpha + destAlpha * (1f - sourceAlpha);
            float sourceWeight = sourceAlpha;
            for (int channel = 0; channel < 3; channel++)
            {
                float tint = channel == 0 ? red : channel == 1 ? green : blue;
                float source = tint * intensity;
                float previous = destination[i + channel] / 255f;
                destination[i + channel] = (byte)Math.Clamp(
                    (source * sourceWeight + previous * destAlpha * (1f - sourceAlpha)) /
                    Math.Max(outAlpha, 0.0001f) * 255f + 0.5f, 0f, 255f);
            }
            destination[i + 3] = (byte)Math.Clamp(outAlpha * 255f + 0.5f, 0f, 255f);
        }
    }

    private static JsonElement? Read(IReadOnlyDictionary<string, JsonElement> first,
        IReadOnlyDictionary<string, JsonElement> second, params string[] names)
    {
        foreach (string name in names)
        {
            if (first.TryGetValue(name, out JsonElement value)) return value;
            if (second.TryGetValue(name, out value)) return value;
        }
        return null;
    }

    private static string StringValue(JsonElement? value)
    {
        if (value is not { } element) return "";
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.ToString()
        };
    }

    private static List<string> ReadArray(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.Array } array) return [];
        return array.EnumerateArray().Select(item => StringValue(item)).ToList();
    }
}
