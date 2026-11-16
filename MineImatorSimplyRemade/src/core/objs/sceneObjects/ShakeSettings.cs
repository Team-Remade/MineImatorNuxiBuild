using GlmSharp;
using MineImatorSimplyRemade.core.ui.Panels;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public sealed record ShakeSettings
{
    public bool PositionEnabled { get; init; } = true;
    public bool RotationEnabled { get; init; }
    public bool BendEnabled { get; init; }
    public float PositionPower { get; init; } = 0.1f;
    public float RotationPower { get; init; } = 1f;
    public float BendPower { get; init; } = 1f;
    public float Intensity { get; init; } = 1f;
    public float Speed { get; init; } = 1f;
    public float Offset { get; init; }
    public bool AutomaticOffset { get; init; } = true;
    public float KeyframeInfluence { get; init; }

    public void Validate()
    {
        if (!float.IsFinite(PositionPower) || PositionPower is < 0 or > 5 ||
            !float.IsFinite(RotationPower) || RotationPower is < 0 or > 5 ||
            !float.IsFinite(BendPower) || BendPower is < 0 or > 5 ||
            !float.IsFinite(Intensity) || Intensity < 0 || !float.IsFinite(Intensity * 100f) ||
            !float.IsFinite(Speed) || Speed < 0 || !float.IsFinite(Speed * 100f) || !float.IsFinite(Offset) ||
            !float.IsFinite(KeyframeInfluence) || KeyframeInfluence is < -1 or > 1)
            throw new InvalidDataException("Shake requires powers from 0 to 5, finite non-negative intensity and speed, a finite offset and keyframe influence from -100% to 100%.");
    }
}

internal static class ShakeEvaluation
{
    internal static void Evaluate(SceneObject obj, int frame, float framerate)
    {
        vec3 position = vec3.Zero, rotation = vec3.Zero, bend = vec3.Zero;
        foreach (var modifier in obj.Modifiers)
        {
            if (modifier.Type != SceneModifierType.Shake || !modifier.Enabled) continue;
            modifier.Validated();
            var settings = modifier.Shake;
            double phase = frame / (double)framerate * settings.Speed + settings.Offset;
            uint seed = settings.AutomaticOffset ? HashId(modifier.Id) : 0;
            float strength = settings.Intensity * Envelope(obj, frame, settings.KeyframeInfluence);
            if (settings.PositionEnabled)
                position += Sample(phase, seed) * (strength * settings.PositionPower / 16f);
            if (settings.RotationEnabled)
                rotation += Sample(phase, seed + 101u) * glm.Radians(strength * settings.RotationPower);
            if (settings.BendEnabled)
                bend += Sample(phase, seed + 211u) * (strength * settings.BendPower);
        }
        if (!Finite(position) || !Finite(rotation) || !Finite(bend))
            throw new InvalidDataException($"Shake on '{obj.Name}' exceeds the supported numeric range.");
        obj.SetEvaluatedShake(position, rotation, bend);
    }

    private static bool Finite(vec3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private static float Envelope(SceneObject obj, int frame, float influence)
    {
        if (influence == 0f) return 1f;
        ObjectKeyframe? previous = null;
        int next = int.MaxValue;
        foreach (var track in obj.Keyframes.Values)
        foreach (var key in track)
        {
            if (key.Frame <= frame && (previous == null || key.Frame > previous.Frame)) previous = key;
            if (key.Frame > frame && key.Frame < next) next = key.Frame;
        }
        float progress = previous == null || next == int.MaxValue ? 0f :
            Math.Clamp(Timeline.ApplyInterpolation((frame - previous.Frame) / (float)(next - previous.Frame),
                previous.InterpolationType), 0f, 1f);
        float middle = MathF.Sin(progress * MathF.PI);
        return 1f + MathF.Abs(influence) * ((influence > 0f ? middle : 1f - middle) - 1f);
    }

    private static vec3 Sample(double phase, uint seed) =>
        new(Noise(phase, seed), Noise(phase, seed + 31u), Noise(phase, seed + 67u));

    private static float Noise(double phase, uint seed)
    {
        // A periodic lattice keeps even very large finite offsets safe to sample.
        double wrapped = phase % 1048576d;
        if (wrapped < 0) wrapped += 1048576d;
        uint index = (uint)Math.Floor(wrapped);
        float t = (float)(wrapped - index);
        t = t * t * t * (t * (t * 6f - 15f) + 10f);
        float first = Value(index, seed), last = Value((index + 1u) % 1048576u, seed);
        return first + (last - first) * t;
    }

    private static float Value(uint index, uint seed)
    {
        uint hash = unchecked(index * 747796405u + seed * 2891336453u + 277803737u);
        hash = unchecked((hash ^ (hash >> 16)) * 2246822519u);
        hash ^= hash >> 13;
        return (hash & 0x00ffffffu) / 8388607.5f - 1f;
    }

    private static uint HashId(string id)
    {
        uint hash = 2166136261u;
        foreach (char character in id) hash = unchecked((hash ^ character) * 16777619u);
        return hash;
    }
}
