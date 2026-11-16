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
        ("radial.enabled", "text.modifier.radial_enabled"), ("radial.axis", "text.modifier.radial_axis"),
        ("radial.radius", "text.modifier.radial_radius"),
        ("rotation.x", "Rotation X"), ("rotation.y", "Rotation Y"), ("rotation.z", "Rotation Z"),
        ("scale.x", "Scale X"), ("scale.y", "Scale Y"), ("scale.z", "Scale Z"),
        ("target", "text.path.target")
    ];
    internal static readonly (string Channel, string Label)[] ShakeChannels =
    [
        ("enabled", "Enabled"),
        ("shake.position.enabled", "text.shake.position"), ("shake.rotation.enabled", "text.shake.rotation"),
        ("shake.bend.enabled", "text.shake.bend"),
        ("shake.position_power", "text.shake.position_power"), ("shake.rotation_power", "text.shake.rotation_power"),
        ("shake.bend_power", "text.shake.bend_power"), ("shake.intensity", "text.shake.intensity"),
        ("shake.speed", "text.shake.speed"), ("shake.offset", "text.shake.offset"),
        ("shake.automatic.enabled", "text.shake.automatic"), ("shake.influence", "text.shake.influence")
    ];

    private static IEnumerable<(string Channel, string Label)> ModifierChannels(SceneModifierType type) => type switch
    {
        SceneModifierType.Array => ArrayChannels,
        SceneModifierType.Shake => ShakeChannels,
        _ => [("enabled", "Enabled")]
    };

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
        if (found == null || !ModifierChannels(found.Type).Any(entry => entry.Channel == parts[2])) return false;
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
                SceneModifierType.Shake => Localization.T("text.shake.title"),
                _ => Localization.T("text.path.follow")
            };
            var channels = ModifierChannels(modifier.Type);
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
        "radial.enabled" => modifier.RadialEnabled ? 1f : 0f,
        "radial.axis" => (float)modifier.RadialAxis,
        "radial.radius" => modifier.RadialRadius,
        "rotation.x" => modifier.RotationX, "rotation.y" => modifier.RotationY, "rotation.z" => modifier.RotationZ,
        "scale.x" => modifier.ScaleX, "scale.y" => modifier.ScaleY, "scale.z" => modifier.ScaleZ,
        "target" => modifier.PathObjectId,
        "shake.position.enabled" => modifier.Shake.PositionEnabled ? 1f : 0f,
        "shake.rotation.enabled" => modifier.Shake.RotationEnabled ? 1f : 0f,
        "shake.bend.enabled" => modifier.Shake.BendEnabled ? 1f : 0f,
        "shake.position_power" => modifier.Shake.PositionPower,
        "shake.rotation_power" => modifier.Shake.RotationPower,
        "shake.bend_power" => modifier.Shake.BendPower,
        "shake.intensity" => modifier.Shake.Intensity,
        "shake.speed" => modifier.Shake.Speed,
        "shake.offset" => modifier.Shake.Offset,
        "shake.automatic.enabled" => modifier.Shake.AutomaticOffset ? 1f : 0f,
        "shake.influence" => modifier.Shake.KeyframeInfluence,
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
                "radial.enabled" => modifier with { RadialEnabled = value >= 0.5f },
                "radial.axis" when value is >= 0 and <= 2 && value == MathF.Truncate(value)
                    => modifier with { RadialAxis = (ArrayAxis)(int)value },
                "radial.radius" => modifier with { RadialRadius = value },
                "rotation.x" => modifier with { RotationX = value }, "rotation.y" => modifier with { RotationY = value },
                "rotation.z" => modifier with { RotationZ = value },
                "scale.x" => modifier with { ScaleX = value }, "scale.y" => modifier with { ScaleY = value },
                "scale.z" => modifier with { ScaleZ = value },
                "shake.position.enabled" => modifier with { Shake = modifier.Shake with { PositionEnabled = value >= 0.5f } },
                "shake.rotation.enabled" => modifier with { Shake = modifier.Shake with { RotationEnabled = value >= 0.5f } },
                "shake.bend.enabled" => modifier with { Shake = modifier.Shake with { BendEnabled = value >= 0.5f } },
                "shake.position_power" => modifier with { Shake = modifier.Shake with { PositionPower = value } },
                "shake.rotation_power" => modifier with { Shake = modifier.Shake with { RotationPower = value } },
                "shake.bend_power" => modifier with { Shake = modifier.Shake with { BendPower = value } },
                "shake.intensity" => modifier with { Shake = modifier.Shake with { Intensity = value } },
                "shake.speed" => modifier with { Shake = modifier.Shake with { Speed = value } },
                "shake.offset" => modifier with { Shake = modifier.Shake with { Offset = value } },
                "shake.automatic.enabled" => modifier with { Shake = modifier.Shake with { AutomaticOffset = value >= 0.5f } },
                "shake.influence" => modifier with { Shake = modifier.Shake with { KeyframeInfluence = value } },
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
