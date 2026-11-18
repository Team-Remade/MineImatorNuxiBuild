using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

public sealed record ArmorPieceSelection(int VariantIndex, string CustomTexturePath = "",
    string TrimPattern = "none", string TrimMaterial = "quartz", uint LeatherColor = 0xA06540);

public static class ArmorModelBuilder
{
    public static IReadOnlyList<string> Pieces { get; } = Array.AsReadOnly(
        new[] { "helmet", "chestplate", "leggings", "boots" });

    public static bool Supports(CharacterEntry entry) =>
        entry.TextureVariants.Count > 0 &&
        Path.GetExtension(entry.FilePath).Equals(".mimodel", StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileNameWithoutExtension(entry.FilePath) is "armor" or "armor_baby";

    public static string[] TrimPatterns(CharacterEntry entry) => ArmorTrimTexture.Patterns(entry);
    public static string[] TrimMaterials(CharacterEntry entry) => ArmorTrimTexture.Materials(entry);

    public static bool IsLeather(CharacterEntry entry, ArmorPieceSelection selection) =>
        (uint)selection.VariantIndex < (uint)entry.TextureVariants.Count &&
        !entry.TextureVariants[selection.VariantIndex].IsCustom &&
        entry.TextureVariants[selection.VariantIndex].Name.Equals("Leather", StringComparison.OrdinalIgnoreCase);

    internal static string PrepareImport(MiTemplate template, string modelPath)
    {
        if (template.ModelDefinition is not { ValueKind: JsonValueKind.Object } definition ||
            !definition.TryGetProperty("name", out var name) || name.GetString() != "armor")
            return modelPath;
        var entry = new CharacterEntry
        {
            FilePath = modelPath,
            TextureVariants = CharacterRegistry.LoadTextureVariants(Path.GetDirectoryName(modelPath)!)
        };
        if (!Supports(entry))
            throw new InvalidDataException("The imported armor model does not support piece selections.");
        definition.TryGetProperty("state", out var state);
        var selections = Pieces.Select(piece =>
        {
            string material = Text(state, piece, "iron");
            int variant = material == "none" ? -1 : entry.TextureVariants.ToList().FindIndex(v =>
                !v.IsCustom && (v.Name.Equals(material, StringComparison.OrdinalIgnoreCase) ||
                    v.Name == "Default" && material == "iron"));
            if (variant < 0 && material != "none")
                throw new InvalidDataException($"Imported armor material '{material}' is unavailable.");
            string dye = Text(template.Armor, piece + "_dye", "#A06540");
            if (dye.Length != 7 || dye[0] != '#' ||
                !uint.TryParse(dye.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint color))
                throw new InvalidDataException($"Invalid imported {piece} dye '{dye}'. Expected #RRGGBB.");
            return new ArmorPieceSelection(variant, TrimPattern: Text(template.Armor, piece + "_trim_pattern", "none"),
                TrimMaterial: Text(template.Armor, piece + "_trim_material", "quartz"), LeatherColor: color);
        }).ToArray();
        return Save(Build(entry, selections), Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimplyRemadeNuxi", "Armor"));

        static string Text(JsonElement? element, string key, string fallback) =>
            element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(key, out var property)
                ? property.GetString() ?? fallback : fallback;
    }

    public static MiModel Build(CharacterEntry entry, IReadOnlyList<ArmorPieceSelection> selections)
    {
        if (!Supports(entry) || selections.Count != Pieces.Count)
            throw new ArgumentException("Armor requires a supported model and four piece selections.");

        var result = ReadModel(entry.FilePath);
        var targets = IndexParts(result.Parts);
        foreach (var part in targets.Values) part.Shapes = [];
        var sources = new Dictionary<string, MiModel>(StringComparer.OrdinalIgnoreCase);
        var trimmed = new Dictionary<(string, int), string>();
        for (int slot = 0; slot < Pieces.Count; slot++)
        {
            var selection = selections[slot];
            if (selection.VariantIndex == -1) continue;
            if ((uint)selection.VariantIndex >= (uint)entry.TextureVariants.Count)
                throw new ArgumentException("The selected armor variant is unavailable.");
            var variant = entry.TextureVariants[selection.VariantIndex];
            string path = string.IsNullOrEmpty(variant.ModelPath) ? entry.FilePath : variant.ModelPath;
            if (!sources.TryGetValue(path, out var source))
                sources.Add(path, source = ReadModel(path));
            string? textureOverride = variant.IsCustom ? selection.CustomTexturePath : variant.FilePath;
            if (variant.IsCustom && string.IsNullOrWhiteSpace(textureOverride))
                throw new InvalidDataException($"Choose a custom texture for the {Pieces[slot]}.");
            if (!string.IsNullOrEmpty(textureOverride) && !File.Exists(textureOverride))
                throw new FileNotFoundException("The selected armor texture is missing.", textureOverride);

            int count = 0;
            foreach (var (key, part) in IndexParts(source.Parts))
            {
                var shapes = (part.Shapes ?? []).Where(shape =>
                    shape.Description == Pieces[slot] || shape.Description == Pieces[slot] + "_baby").ToList();
                if (shapes.Count == 0) continue;
                if (!targets.TryGetValue(key, out var target))
                    throw new InvalidDataException($"Armor variant has an incompatible bone: {key}");
                foreach (var shape in shapes)
                {
                    // Copy each shape: several slots can use the same source with different overrides.
                    var copy = JsonSerializer.Deserialize(
                        JsonSerializer.Serialize(shape, AppJsonContext.Default.MiShape),
                        AppJsonContext.Default.MiShape)!;
                    copy.Texture = !string.IsNullOrEmpty(textureOverride) ? Path.GetFullPath(textureOverride) :
                        !string.IsNullOrEmpty(shape.Texture) ? shape.Texture :
                        !string.IsNullOrEmpty(part.Texture) ? part.Texture : source.Texture;
                    copy.TextureSize ??= part.TextureSize ?? source.TextureSize;
                    if (IsLeather(entry, selection) ||
                        selection.TrimPattern != "none" && selection.TrimMaterial != "none")
                    {
                        var trimKey = (copy.Texture, slot);
                        if (!trimmed.TryGetValue(trimKey, out string? texture))
                            trimmed.Add(trimKey, texture = ArmorTrimTexture.Apply(entry, copy.Texture, slot, selection,
                                IsLeather(entry, selection)));
                        copy.Texture = texture;
                    }
                    target.Shapes.Add(copy);
                    count++;
                }
            }
            if (count == 0)
                throw new InvalidDataException($"Armor variant has no {Pieces[slot]} geometry.");
        }
        return result;
    }

    public static string Save(MiModel model, string directory)
    {
        string json = JsonSerializer.Serialize(model, AppJsonContext.Default.MiModel);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        string path = Path.Combine(directory, $"{model.Name}_{hash}.mimodel");
        Directory.CreateDirectory(directory);
        if (!File.Exists(path)) File.WriteAllText(path, json);
        return path;
    }

    private static MiModel ReadModel(string path)
    {
        string json = MineImatorLoader.EscapeJsonControlCharacters(File.ReadAllText(path));
        var model = JsonSerializer.Deserialize(json, AppJsonContext.Default.MiModel)
            ?? throw new InvalidDataException($"Could not read armor model: {path}");
        MineImatorLoader.NormalizeModelTextureSizes(model);
        model.DirectoryPath = Path.GetDirectoryName(Path.GetFullPath(path))!;
        model.FullPath = path;
        model.Texture = Resolve(model.Texture);
        model.TextureMaterial = Resolve(model.TextureMaterial);
        model.TextureNormal = Resolve(model.TextureNormal);
        ResolveDictionary(model.Textures);
        foreach (var part in IndexParts(model.Parts).Values)
        {
            part.Texture = Resolve(part.Texture);
            part.TextureMaterial = Resolve(part.TextureMaterial);
            part.TextureNormal = Resolve(part.TextureNormal);
            ResolveDictionary(part.Textures);
            foreach (var shape in part.Shapes ?? [])
            {
                shape.Texture = Resolve(shape.Texture);
                shape.TextureMaterial = Resolve(shape.TextureMaterial);
                shape.TextureNormal = Resolve(shape.TextureNormal);
            }
        }
        return model;

        string Resolve(string reference) => string.IsNullOrEmpty(reference) ? reference :
            Path.GetFullPath(Path.Combine(model.DirectoryPath, reference));

        void ResolveDictionary(Dictionary<string, string>? textures)
        {
            if (textures == null) return;
            foreach (string key in textures.Keys.ToArray()) textures[key] = Resolve(textures[key]);
        }
    }

    private static Dictionary<string, MiPart> IndexParts(List<MiPart>? parts)
    {
        var result = new Dictionary<string, MiPart>(StringComparer.Ordinal);
        Visit(parts, "");
        return result;

        void Visit(List<MiPart>? children, string prefix)
        {
            foreach (var part in children ?? [])
            {
                string key = prefix + "/" + part.Name;
                if (!result.TryAdd(key, part))
                    throw new InvalidDataException($"Armor has duplicate bones: {key}");
                Visit(part.Parts, key);
            }
        }
    }
}
