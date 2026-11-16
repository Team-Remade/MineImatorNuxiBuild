using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

public sealed record ArmorPieceSelection(int VariantIndex, string CustomTexturePath = "");

public static class ArmorModelBuilder
{
    public static IReadOnlyList<string> Pieces { get; } = Array.AsReadOnly(
        new[] { "helmet", "chestplate", "leggings", "boots" });

    public static bool Supports(CharacterEntry entry) =>
        entry.TextureVariants.Count > 0 &&
        Path.GetExtension(entry.FilePath).Equals(".mimodel", StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileNameWithoutExtension(entry.FilePath) is "armor" or "armor_baby";

    public static MiModel Build(CharacterEntry entry, IReadOnlyList<ArmorPieceSelection> selections)
    {
        if (!Supports(entry) || selections.Count != Pieces.Count)
            throw new ArgumentException("Armor requires a supported model and four piece selections.");

        var result = ReadModel(entry.FilePath);
        var targets = IndexParts(result.Parts);
        foreach (var part in targets.Values) part.Shapes = [];
        var sources = new Dictionary<string, MiModel>(StringComparer.OrdinalIgnoreCase);
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
