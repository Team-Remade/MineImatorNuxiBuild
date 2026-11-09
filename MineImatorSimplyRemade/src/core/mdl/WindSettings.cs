using GlmSharp;

namespace MineImatorSimplyRemade.core.mdl;

public readonly record struct WindSettings(
    bool Enabled, float Speed, float Strength, float Direction,
    float DirectionalSpeed, float DirectionalStrength, float Time)
{
    public vec4 Parameters => new(Time * Speed * MathF.Tau, Strength,
        Time * DirectionalSpeed * MathF.Tau, DirectionalStrength);

    public vec2 Heading => new(MathF.Cos(Direction * MathF.PI / 180f),
        MathF.Sin(Direction * MathF.PI / 180f));

    public float MaximumDisplacement => Enabled ? 0.12f * (Strength + DirectionalStrength) : 0f;

    public static int ClassifyBlock(string blockName, string textureKey)
    {
        int textureKind = ClassifyTexture(textureKey);
        int blockKind = ClassifyTexture(blockName);
        return blockKind == 3 && textureKind == 4 ? 4 : blockKind != 0 ? blockKind : textureKind;
    }

    // 0: solid, 1: leaves, 2: water, 3: rooted plant, 4: upper half of a tall plant.
    public static int ClassifyTexture(string textureKey)
    {
        string key = textureKey.Replace('\\', '/');
        key = key[(Math.Max(key.LastIndexOf('/'), key.LastIndexOf(':')) + 1)..].ToLowerInvariant();
        if (key.EndsWith(".png", StringComparison.Ordinal)) key = key[..^4];
        if (key is "water_still" or "water_flow" or "water") return 2;
        if (key.EndsWith("_leaves", StringComparison.Ordinal) || key.StartsWith("leaves_", StringComparison.Ordinal))
            return 1;
        if (key is "grass" or "short_grass" or "tall_grass" or "tallgrass" or "fern" or "large_fern" or "dead_bush" or "deadbush" or
            "dandelion" or "poppy" or "blue_orchid" or "allium" or "azure_bluet" or "oxeye_daisy" or
            "cornflower" or "lily_of_the_valley" or "wither_rose" or "vine" or "lily_pad" or "waterlily" or
            "sugar_cane" or "reeds" or "bamboo" or "bamboo_stalk" or "pink_petals" or "wildflowers" or "leaf_litter" or
            "sunflower" or "rose_bush" or "peony" or "lilac" or "carrots" or "potatoes" or "beetroots" ||
            key.Contains("sapling", StringComparison.Ordinal) || key.Contains("tulip", StringComparison.Ordinal) ||
            key.Contains("seagrass", StringComparison.Ordinal) || key.Contains("kelp", StringComparison.Ordinal) ||
            key.Contains("wheat", StringComparison.Ordinal) || key.Contains("carrots_stage", StringComparison.Ordinal) ||
            key.Contains("potatoes_stage", StringComparison.Ordinal) || key.Contains("beetroots_stage", StringComparison.Ordinal) ||
            key.StartsWith("flower_", StringComparison.Ordinal) || key.StartsWith("double_plant_", StringComparison.Ordinal) ||
            key.StartsWith("tall_grass_", StringComparison.Ordinal) || key.StartsWith("large_fern_", StringComparison.Ordinal) ||
            key.StartsWith("sunflower_", StringComparison.Ordinal) || key.StartsWith("rose_bush_", StringComparison.Ordinal) ||
            key.StartsWith("peony_", StringComparison.Ordinal) || key.StartsWith("lilac_", StringComparison.Ordinal))
            return key.EndsWith("_top", StringComparison.Ordinal) || key.EndsWith("_upper", StringComparison.Ordinal) ? 4 : 3;
        return 0;
    }
}
