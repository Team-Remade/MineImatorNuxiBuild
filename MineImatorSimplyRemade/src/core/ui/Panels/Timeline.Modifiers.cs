using System.Text.Json;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Timeline
{
    internal static readonly (string Channel, string Label)[] ArrayChannels =
    [
        ("enabled", "Enabled"), ("count", "text.modifier.count"), ("axis", "text.modifier.axis"),
        ("distance", "text.modifier.distance"),
        ("rotation.x", "Rotation X"), ("rotation.y", "Rotation Y"), ("rotation.z", "Rotation Z"),
        ("scale.x", "Scale X"), ("scale.y", "Scale Y"), ("scale.z", "Scale Z"),
        ("target", "text.path.target")
    ];

    internal static bool IsModifierReferencePath(string path) =>
        path.StartsWith("modifier.", StringComparison.Ordinal) && path.EndsWith(".target", StringComparison.Ordinal);

    internal static bool IsDiscreteModifierPath(string path) =>
        path.StartsWith("modifier.", StringComparison.Ordinal) &&
        (path.EndsWith(".enabled", StringComparison.Ordinal) || path.EndsWith(".count", StringComparison.Ordinal) ||
            path.EndsWith(".axis", StringComparison.Ordinal));

    internal static bool TryGetModifier(SceneObject obj, string path, out SceneModifier modifier, out string channel)
    {
        modifier = null!;
        channel = "";
        if (!path.StartsWith("modifier.", StringComparison.Ordinal)) return false;
        string[] parts = path.Split('.', 3);
        if (parts.Length != 3) return false;
        var found = obj.Modifiers.FirstOrDefault(entry => entry.Id == parts[1]);
        if (found == null || !(found.Type != SceneModifierType.Array
                ? parts[2] == "enabled" : ArrayChannels.Any(entry => entry.Channel == parts[2]))) return false;
        modifier = found;
        channel = parts[2];
        return true;
    }

    private static IEnumerable<(string Path, string Label)> GetModifierChannels(SceneObject obj)
    {
        for (int i = 0; i < obj.Modifiers.Count; i++)
        {
            var modifier = obj.Modifiers[i];
            string label = modifier.Type switch
            {
                SceneModifierType.Array => Localization.T("text.modifier.array"),
                SceneModifierType.Cloth => Localization.T("text.cloth.title"),
                _ => Localization.T("text.path.follow")
            };
            IEnumerable<(string Channel, string Label)> channels = modifier.Type == SceneModifierType.Array
                ? ArrayChannels : [("enabled", "Enabled")];
            foreach (var channel in channels)
                yield return ($"modifier.{modifier.Id}.{channel.Channel}", $"{label} {i + 1}: {(Localization.IsTranslationKey(channel.Label) ? Localization.T(channel.Label) : Localization.DisplayText(channel.Label))}");
        }
    }

    internal static object GetModifierValue(SceneModifier modifier, string channel) => channel switch
    {
        "enabled" => modifier.Enabled ? 1f : 0f,
        "count" => (float)modifier.Count,
        "axis" => (float)modifier.Axis,
        "distance" => modifier.Distance,
        "rotation.x" => modifier.RotationX, "rotation.y" => modifier.RotationY, "rotation.z" => modifier.RotationZ,
        "scale.x" => modifier.ScaleX, "scale.y" => modifier.ScaleY, "scale.z" => modifier.ScaleZ,
        "target" => modifier.PathObjectId,
        _ => throw new InvalidDataException($"Unknown modifier channel: {channel}.")
    };

    internal static SceneModifier WithModifierValue(SceneModifier modifier, string channel, object rawValue)
    {
        SceneModifier updated;
        if (channel == "target")
        {
            if (rawValue is JsonElement json && json.ValueKind == JsonValueKind.String)
                rawValue = json.GetString() ?? "";
            if (rawValue is not string target)
                throw new InvalidDataException("Modifier path targets must be object IDs.");
            updated = modifier with { PathObjectId = target };
        }
        else
        {
            if (!TryConvertKeyframeValue(rawValue, out float value) || !float.IsFinite(value))
                throw new InvalidDataException($"Invalid modifier keyframe: {channel}.");
            updated = channel switch
            {
                "enabled" => modifier with { Enabled = value >= 0.5f },
                "count" => modifier with { Count = (int)Math.Clamp(MathF.Round(value), 1f, SceneModifier.MaxInstances) },
                "axis" when value is >= 0 and <= 2 && value == MathF.Truncate(value)
                    => modifier with { Axis = (ArrayAxis)(int)value },
                "distance" => modifier with { Distance = value },
                "rotation.x" => modifier with { RotationX = value }, "rotation.y" => modifier with { RotationY = value },
                "rotation.z" => modifier with { RotationZ = value },
                "scale.x" => modifier with { ScaleX = value }, "scale.y" => modifier with { ScaleY = value },
                "scale.z" => modifier with { ScaleZ = value },
                _ => throw new InvalidDataException($"Invalid modifier channel or value: {channel}.")
            };
        }
        return updated;
    }

    private static void SetModifierValue(SceneObject obj, SceneModifier modifier, string channel, object rawValue)
    {
        obj.Modifiers[obj.Modifiers.FindIndex(entry => entry.Id == modifier.Id)] =
            WithModifierValue(modifier, channel, rawValue).Validated();
        if (modifier.Type == SceneModifierType.FollowPath) obj.SetEvaluatedPathFrame(null);
    }
}
