using System.Globalization;
using System.Text.Json.Nodes;
using GlmSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

// Keep the Mine-imator parameter names and random ranges intact in project files.
public sealed class ParticlePreset
{
    public JsonObject Document { get; }
    public JsonObject Settings => (JsonObject)Document["particles"]!;
    public JsonObject[] Types { get; }

    public ParticlePreset(JsonObject document)
    {
        Document = (JsonObject)document.DeepClone();
        if (Document["particles"] is not JsonObject settings ||
            settings["types"] is not JsonArray types || types.Count is < 1 or > 64)
            throw new InvalidDataException("A particle preset must contain particles and between 1 and 64 types.");
        Types = types.Select(node => node as JsonObject ??
            throw new InvalidDataException("Particle types must be objects.")).ToArray();
        ValidateNumbers(Document);
        if (Text(settings, "spawn_region_type", "box") is not ("sphere" or "cube" or "box"))
            throw new InvalidDataException("Path-based particle regions are not supported.");
        if (Text(settings, "bounding_box_type", "none") is not ("none" or "ground" or "spawn" or "custom"))
            throw new InvalidDataException("Unknown particle bounding box type.");
        foreach (JsonObject type in Types)
        {
            string source = Text(type, "temp_type", "template");
            if (source == "template")
                _ = Sprite(Text(type, "sprite_template", "generic"));
            else if (source != "templateobj")
                throw new InvalidDataException($"Particle source '{source}' is not supported.");
            if (Text(type, "sprite_template_tex", "default") != "default")
                throw new InvalidDataException("External particle texture packs are not supported.");
            if (source == "templateobj" &&
                !(Document["templates"] as JsonArray ?? []).OfType<JsonObject>()
                    .Any(template => Text(template, "id") == Text(type, "temp") && Text(template, "type") == "block"))
                throw new InvalidDataException("Object particles must reference an embedded block template.");
            if (source == "templateobj")
            {
                var template = ((JsonArray)Document["templates"]!).OfType<JsonObject>()
                    .First(item => Text(item, "id") == Text(type, "temp"));
                if (template["block"] is not JsonObject block || string.IsNullOrWhiteSpace(Text(block, "name")))
                    throw new InvalidDataException("Particle block templates must specify a block name.");
                foreach (string texture in new[] { "tex", "tex_material", "tex_normal" })
                    if (block[texture] is JsonValue value && value.TryGetValue<string>(out string? reference) &&
                        reference != "default")
                        throw new InvalidDataException("External particle block textures are not supported.");
            }
            foreach (string key in new[] { "color", "color_random_start", "color_random_end",
                         "color_mix", "color_mix_random_start", "color_mix_random_end" })
                if (type[key] != null) _ = Color(Text(type, key));
            foreach (string key in new[] { "angle", "spd", "spd_add", "spd_mul", "rot", "rot_spd",
                         "rot_spd_add", "rot_spd_mul" })
                foreach (string suffix in new[] { "", "_israndom", "_random_min", "_random_max" })
                    if (type[key + suffix] != null) _ = Vector(type, key + suffix);
        }
    }

    public static ParticlePreset Load(string path) => new(
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject ??
        throw new InvalidDataException("The particle preset root must be an object."));

    public static IEnumerable<string> EnumerateFiles(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.miparticles", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase)
            : [];

    public static (int Frames, float Size) Sprite(string name) => name switch
    {
        "generic" or "effect" or "spark" => (8, 8f / 16f),
        "big_smoke" => (12, 1f),
        "explosion" => (16, 2f),
        "splash" => (4, 8f / 16f),
        "flame" or "firefly" or "flash" or "glow" => (1, 8f / 16f),
        _ => throw new InvalidDataException($"Unknown particle sprite template '{name}'.")
    };

    public static float Number(JsonObject obj, string key, float fallback = 0f) =>
        obj[key] is { } node ? Component(node, fallback) : fallback;

    public static bool Flag(JsonObject obj, string key, bool fallback = false) =>
        obj[key] is JsonValue value
            ? value.TryGetValue<bool>(out bool flag) ? flag :
                Number(obj, key) != 0
            : fallback;

    public static string Text(JsonObject obj, string key, string fallback = "") =>
        obj[key] is not { } node ? fallback :
            node is JsonValue value && value.TryGetValue<string>(out string? text) ? text :
                throw new InvalidDataException($"Particle parameter '{key}' must be text.");

    public static vec3 Vector(JsonObject obj, string key, float fallback = 0f) =>
        obj[key] is not { } node ? new vec3(fallback) :
            node is JsonArray { Count: 3 } array
                ? new vec3(Component(array[0], fallback), Component(array[1], fallback), Component(array[2], fallback))
                : throw new InvalidDataException($"Particle parameter '{key}' must have three numeric components.");

    private static float Component(JsonNode? node, float fallback)
    {
        if (node == null) return fallback;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<float>(out float number) && float.IsFinite(number)) return number;
            if (value.TryGetValue<int>(out int integer)) return integer;
            if (value.TryGetValue<double>(out double real) && double.IsFinite(real) && Math.Abs(real) <= float.MaxValue)
                return (float)real;
        }
        throw new InvalidDataException("Particle parameters must contain finite numeric values.");
    }

    public static float Sample(JsonObject obj, string key, Random random, float fallback = 0f)
    {
        if (!Flag(obj, key + "_israndom"))
            return Number(obj, key, fallback);
        return Range(Number(obj, key + "_random_min", fallback),
            Number(obj, key + "_random_max", fallback), random);
    }

    public static vec3 SampleVector(JsonObject obj, string key, string extend, Random random, float fallback = 0f)
    {
        vec3 values = Vector(obj, key, fallback);
        vec3 flags = Vector(obj, key + "_israndom");
        vec3 min = Vector(obj, key + "_random_min", fallback);
        vec3 max = Vector(obj, key + "_random_max", fallback);
        vec3 result = vec3.Zero;
        bool separate = Flag(obj, extend);
        for (int axis = 0; axis < 3; axis++)
        {
            int index = separate ? axis : 0;
            result[axis] = flags[index] != 0 ? Range(min[index], max[index], random) : values[index];
        }
        return result;
    }

    public static float Range(float min, float max, Random random) =>
        min + (max - min) * random.NextSingle();

    public static vec3 Color(string text)
    {
        if (text.Length != 7 || text[0] != '#' ||
            !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            throw new InvalidDataException($"Invalid particle color '{text}'. Expected #RRGGBB.");
        return new vec3((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) / 255f;
    }

    public static vec3 SampleColor(JsonObject obj, string key, Random random)
    {
        if (!Flag(obj, key + "_israndom"))
            return Color(Text(obj, key, "#FFFFFF"));
        vec3 start = Color(Text(obj, key + "_random_start", "#FFFFFF"));
        return start + (Color(Text(obj, key + "_random_end", "#FFFFFF")) - start) * random.NextSingle();
    }

    private static void ValidateNumbers(JsonNode node)
    {
        if (node is JsonValue value && value.TryGetValue<float>(out float number) && !float.IsFinite(number))
            throw new InvalidDataException("Particle parameters must be finite.");
        if (node is JsonObject obj)
            foreach (var pair in obj)
                if (pair.Value != null) ValidateNumbers(pair.Value);
        if (node is JsonArray array)
            foreach (JsonNode? item in array)
                if (item != null) ValidateNumbers(item);
    }
}
