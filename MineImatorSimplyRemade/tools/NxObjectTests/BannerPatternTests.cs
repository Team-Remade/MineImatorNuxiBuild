using System.Text.Json;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs;

internal static partial class Program
{
    private static void BannerPatterns(Fixture fixture)
    {
        string version = Path.GetFullPath(Path.Combine("data", "minecraft", "versions", "26.3"));
        if (!Directory.Exists(version))
            version = Path.GetFullPath(Path.Combine("MineImatorSimplyRemade", "data", "minecraft",
                "versions", "26.3"));
        Check(BannerPatternSystem.Colors.Count == 16 && BannerPatternSystem.Colors.Distinct().Count() == 16,
            "Banner editor uses exactly the 16 Minecraft dye colors");
        Check(BannerPatternSystem.Patterns.Count > 40 &&
              BannerPatternSystem.Patterns.Distinct().Count() == BannerPatternSystem.Patterns.Count,
            "Banner patterns include the complete default entity mask catalog");
        Check(BannerPatternSystem.NormalizePattern("minecraft:stripe_top") == "stripe_top" &&
              BannerPatternSystem.NormalizePattern("br") == "square_bottom_right" &&
              BannerPatternSystem.NormalizeColor("minecraft:red") == "red",
            "Banner pattern and color aliases normalize across Mine-imator formats");

        var first = new BannerPatternLayer { Pattern = "gradient", Color = "red" };
        var second = new BannerPatternLayer { Pattern = "gradient_up", Color = "blue" };
        byte[] baseOnly = BannerPatternSystem.Compose("white", [], version);
        byte[] layered = BannerPatternSystem.Compose("white", [first, second], version);
        byte[] reversed = BannerPatternSystem.Compose("white", [second, first], version);
        Check(layered.Length == 64 * 64 * 4 && Enumerable.Range(0, layered.Length).Any(index =>
                index % 4 != 3 && layered[index] != baseOnly[index]), "Banner masks tint generated 64x64 banner pixels");
        Check(!layered.SequenceEqual(baseOnly) && !layered.SequenceEqual(reversed),
            "Banner layer composition preserves authored bottom-to-top order");

        var template = new MiTemplate
        {
            ExtraValues = new Dictionary<string, JsonElement>
            {
                ["pattern_type"] = JsonSerializer.SerializeToElement("banner"),
                ["pattern_base_color"] = JsonSerializer.SerializeToElement("green"),
                ["pattern_pattern_list"] = JsonSerializer.SerializeToElement(new[] { "ts", "cross" }),
                ["pattern_color_list"] = JsonSerializer.SerializeToElement(new[] { "orange", "black" })
            }
        };
        var timeline = new MiTimeline
        {
            ExtraValues = new Dictionary<string, JsonElement>
            {
                ["pattern_base_color"] = JsonSerializer.SerializeToElement("blue")
            }
        };
        Check(BannerPatternSystem.TryReadMineImator(template, timeline, out string importedBase, out var importedLayers),
            "Mine-imator banner fields are recognized");
        Check(importedBase == "blue" && importedLayers.Select(layer => layer.Pattern)
                  .SequenceEqual(new[] { "stripe_top", "cross" }) &&
              importedLayers.Select(layer => layer.Color).SequenceEqual(new[] { "orange", "black" }),
            "Timeline base color overrides the template while pattern layer order is retained");

        var obj = new SceneObject
        {
            HasBannerPattern = true,
            BannerBaseColor = "purple",
            BannerPatternLayers =
            [
                new BannerPatternLayer { Pattern = "circle", Color = "yellow" },
                new BannerPatternLayer { Pattern = "border", Color = "red" }
            ]
        };
        ProjectSceneObjectEntry entry = ProjectSceneSerializer.SerializeObjectForLibrary(obj);
        string json = JsonSerializer.Serialize(entry, AppJsonContext.Default.ProjectSceneObjectEntry);
        var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectSceneObjectEntry)!;
        Check(restored.BannerBaseColor == "purple" &&
              restored.HasBannerPattern &&
              restored.BannerPatternLayers.Select(layer => layer.Pattern + ":" + layer.Color)
                  .SequenceEqual(new[] { "circle:yellow", "border:red" }),
            "Banner design state survives project serialization");
        Check(entry.BannerPatternLayers[0] != obj.BannerPatternLayers[0],
            "Banner design serialization copies mutable layers");
    }
}
