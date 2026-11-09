using GlmSharp;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public enum BiomeTintKind
{
    None, Grass, Foliage, Water, Oak, Spruce, Birch, Jungle, Acacia, DarkOak, Mangrove
}

public sealed class BiomeColors
{
    public int Grass { get; set; } = 0x91bd59;
    public int Foliage { get; set; } = 0x77ab2f;
    public int Water { get; set; } = 0x3f76e4;
    public int Oak { get; set; } = 0x77ab2f;
    public int Spruce { get; set; } = 0x619961;
    public int Birch { get; set; } = 0x80a755;
    public int Jungle { get; set; } = 0x77ab2f;
    public int Acacia { get; set; } = 0x77ab2f;
    public int DarkOak { get; set; } = 0x77ab2f;
    public int Mangrove { get; set; } = 0x92c648;

    public int GetColor(BiomeTintKind kind) => kind switch
    {
        BiomeTintKind.Grass => Grass,
        BiomeTintKind.Foliage => Foliage,
        BiomeTintKind.Water => Water,
        BiomeTintKind.Oak => Oak,
        BiomeTintKind.Spruce => Spruce,
        BiomeTintKind.Birch => Birch,
        BiomeTintKind.Jungle => Jungle,
        BiomeTintKind.Acacia => Acacia,
        BiomeTintKind.DarkOak => DarkOak,
        BiomeTintKind.Mangrove => Mangrove,
        _ => 0xffffff
    };

    public void SetColor(BiomeTintKind kind, int color)
    {
        color = Math.Clamp(color, 0, 0xffffff);
        switch (kind)
        {
            case BiomeTintKind.Grass: Grass = color; break;
            case BiomeTintKind.Foliage: Foliage = color; break;
            case BiomeTintKind.Water: Water = color; break;
            case BiomeTintKind.Oak: Oak = color; break;
            case BiomeTintKind.Spruce: Spruce = color; break;
            case BiomeTintKind.Birch: Birch = color; break;
            case BiomeTintKind.Jungle: Jungle = color; break;
            case BiomeTintKind.Acacia: Acacia = color; break;
            case BiomeTintKind.DarkOak: DarkOak = color; break;
            case BiomeTintKind.Mangrove: Mangrove = color; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public BiomeColors Clone() => (BiomeColors)MemberwiseClone();
    public vec3 GetTint(BiomeTintKind kind) => Unpack(GetColor(kind));
    public static vec3 Unpack(int color) => new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f, (color & 255) / 255f);
    public static int Pack(vec3 color) =>
        ((int)MathF.Round(Math.Clamp(color.x, 0, 1) * 255) << 16) |
        ((int)MathF.Round(Math.Clamp(color.y, 0, 1) * 255) << 8) |
        (int)MathF.Round(Math.Clamp(color.z, 0, 1) * 255);
}

public sealed class BiomePreset(string id, float temperature, float downfall,
    int? grass = null, int? foliage = null, int water = 0x3f76e4, string modifier = "")
{
    public string Id { get; } = id;
    public string Name { get; } = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' '));
    private BiomeColors? _colors;

    public BiomeColors CreateColors() => (_colors ??= BuildColors()).Clone();

    private BiomeColors BuildColors()
    {
        int grassColor = grass ?? BiomeColors.Pack(MinecraftModelMesh.LoadColormapTint("grass.png", temperature, downfall));
        int foliageColor = foliage ?? BiomeColors.Pack(MinecraftModelMesh.LoadColormapTint("foliage.png", temperature, downfall));
        if (modifier == "dark_forest")
            grassColor = ((grassColor & 0xfefefe) + 0x28340a) >> 1;
        // A scene-wide tint uses one representative swamp color, not per-position noise.
        if (modifier == "swamp")
            grassColor = 0x6a7039;
        return new BiomeColors
        {
            Grass = grassColor, Foliage = foliageColor, Water = water,
            Oak = foliageColor, Jungle = foliageColor, Acacia = foliageColor, DarkOak = foliageColor
        };
    }
}

public static class BiomeEnvironment
{
    // Climate and effect values: misode/mcmeta, tag 26.3-summary, data/worldgen/biome/data.json.
    public static IReadOnlyList<BiomePreset> Presets { get; } = Array.AsReadOnly(new BiomePreset[]
    {
        new("badlands", 2f, 0f, 0x90814d, 0x9e814d),
        new("bamboo_jungle", .95f, .9f),
        new("basalt_deltas", 2f, 0f),
        new("beach", .8f, .4f),
        new("birch_forest", .6f, .6f),
        new("cherry_grove", .5f, .8f, 0xb6db61, 0xb6db61, 0x5db7ef),
        new("cold_ocean", .5f, .5f, water: 0x3d57d6),
        new("crimson_forest", 2f, 0f),
        new("dappled_forest", .6f, .6f, 0xdf6827, 0xe68e30, 0x375154),
        new("dark_forest", .7f, .8f, modifier: "dark_forest"),
        new("deep_cold_ocean", .5f, .5f, water: 0x3d57d6),
        new("deep_dark", .8f, .4f),
        new("deep_frozen_ocean", .5f, .5f, water: 0x3938c9),
        new("deep_lukewarm_ocean", .5f, .5f, water: 0x45adf2),
        new("deep_ocean", .5f, .5f),
        new("desert", 2f, 0f),
        new("dripstone_caves", .8f, .4f),
        new("end_barrens", .5f, .5f),
        new("end_highlands", .5f, .5f),
        new("end_midlands", .5f, .5f),
        new("eroded_badlands", 2f, 0f, 0x90814d, 0x9e814d),
        new("flower_forest", .7f, .8f),
        new("forest", .7f, .8f),
        new("frozen_ocean", 0f, .5f, water: 0x3938c9),
        new("frozen_peaks", -.7f, .9f),
        new("frozen_river", 0f, .5f, water: 0x3938c9),
        new("grove", -.2f, .8f),
        new("ice_spikes", 0f, .5f),
        new("jagged_peaks", -.7f, .9f),
        new("jungle", .95f, .9f),
        new("lukewarm_ocean", .5f, .5f, water: 0x45adf2),
        new("lush_caves", .5f, .5f),
        new("mangrove_swamp", .8f, .9f, foliage: 0x8db127, water: 0x3a7a6a, modifier: "swamp"),
        new("meadow", .5f, .8f, water: 0x0e4ecf),
        new("mushroom_fields", .9f, 1f),
        new("nether_wastes", 2f, 0f),
        new("ocean", .5f, .5f),
        new("old_growth_birch_forest", .6f, .6f),
        new("old_growth_pine_taiga", .3f, .8f),
        new("old_growth_spruce_taiga", .25f, .8f),
        new("pale_garden", .7f, .8f, 0x778272, 0x878d76, 0x76889d),
        new("plains", .8f, .4f),
        new("river", .5f, .5f),
        new("savanna", 2f, 0f),
        new("savanna_plateau", 2f, 0f),
        new("small_end_islands", .5f, .5f),
        new("snowy_beach", .05f, .3f, water: 0x3d57d6),
        new("snowy_plains", 0f, .5f),
        new("snowy_slopes", -.3f, .9f),
        new("snowy_taiga", -.5f, .4f, water: 0x3d57d6),
        new("soul_sand_valley", 2f, 0f),
        new("sparse_jungle", .95f, .8f),
        new("stony_peaks", 1f, .3f),
        new("stony_shore", .2f, .3f),
        new("sulfur_caves", .8f, .4f, grass: 0xaba64f, water: 0x34bf89),
        new("sunflower_plains", .8f, .4f),
        new("swamp", .8f, .9f, foliage: 0x6a7039, water: 0x617b64, modifier: "swamp"),
        new("taiga", .25f, .8f),
        new("the_end", .5f, .5f),
        new("the_void", .5f, .5f),
        new("warm_ocean", .5f, .5f, water: 0x43d5ee),
        new("warped_forest", 2f, 0f),
        new("windswept_forest", .2f, .3f),
        new("windswept_gravelly_hills", .2f, .3f),
        new("windswept_hills", .2f, .3f),
        new("windswept_savanna", 2f, 0f),
        new("wooded_badlands", 2f, 0f, 0x90814d, 0x9e814d)
    });

    public static string Biome { get; private set; } = "default";
    private static BiomeColors _colors = new();

    public static void Apply(string biome, BiomeColors customColors)
    {
        var colors = biome switch
        {
            "default" or "custom" => customColors.Clone(),
            _ => (Presets.FirstOrDefault(p => p.Id == biome)
                ?? throw new ArgumentException($"Unknown biome '{biome}'.", nameof(biome))).CreateColors()
        };
        Biome = biome;
        _colors = colors;
    }

    public static vec3 GetTint(BiomeTintKind kind) => Biome == "default"
        ? MinecraftModelMesh.GetLegacyTint(kind) : _colors.GetTint(kind);

}
