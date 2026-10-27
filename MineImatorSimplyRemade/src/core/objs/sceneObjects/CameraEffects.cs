using GlmSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum CameraEffectType
{
    CameraShake = 0,
    FilmGrain = 1,
    DepthOfField = 2,
    Bloom = 3
}

public enum BloomBlendMode
{
    Normal = 0,
    Add = 1,
    Screen = 2,
    Multiply = 3,
    Overlay = 4
}

public class BloomSettings
{
    public float Radius { get; set; } = 50f;
    public float Intensity { get; set; } = 100f;
    public float Threshold { get; set; } = 80f;
    public float AnamorphicRatio { get; set; } = 100f;
    public float BlendColorR { get; set; } = 1f;
    public float BlendColorG { get; set; } = 1f;
    public float BlendColorB { get; set; } = 1f;
    public BloomBlendMode BlendMode { get; set; } = BloomBlendMode.Add;

    public BloomSettings Clone() => (BloomSettings)MemberwiseClone();

    public static readonly (string Key, string Label, float Min, float Max)[] Controls =
    [
        ("radius", "Radius (%)", 0f, 100f),
        ("intensity", "Intensity (%)", 0f, 1000f),
        ("threshold", "Threshold (%)", 0f, 100f),
        ("anamorphic_ratio", "Anamorphic Ratio (%)", 1f, 1000f),
        ("blend_color.r", "Blend Color R", 0f, 1f),
        ("blend_color.g", "Blend Color G", 0f, 1f),
        ("blend_color.b", "Blend Color B", 0f, 1f),
        ("mode", "Blend Mode", 0f, 4f)
    ];

    public float GetValue(string key) => key switch
    {
        "radius" => Radius,
        "intensity" => Intensity,
        "threshold" => Threshold,
        "anamorphic_ratio" => AnamorphicRatio,
        "blend_color.r" => BlendColorR,
        "blend_color.g" => BlendColorG,
        "blend_color.b" => BlendColorB,
        "mode" => (float)BlendMode,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown bloom control.")
    };

    public void SetValue(string key, float value)
    {
        switch (key)
        {
            case "radius": Radius = Math.Clamp(value, 0f, 100f); break;
            case "intensity": Intensity = Math.Clamp(value, 0f, 1000f); break;
            case "threshold": Threshold = Math.Clamp(value, 0f, 100f); break;
            case "anamorphic_ratio": AnamorphicRatio = Math.Clamp(value, 1f, 1000f); break;
            case "blend_color.r": BlendColorR = Math.Clamp(value, 0f, 1f); break;
            case "blend_color.g": BlendColorG = Math.Clamp(value, 0f, 1f); break;
            case "blend_color.b": BlendColorB = Math.Clamp(value, 0f, 1f); break;
            case "mode": BlendMode = (BloomBlendMode)MathF.Round(Math.Clamp(value, 0f, 4f)); break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown bloom control.");
        }
    }
}

public class FilmGrainSettings
{
    public float Strength = 0.15f;
    public float Saturation = 0.5f;
    public float Size = 1f;
}

public enum CameraShakeMode
{
    Rotational = 0,
    Positional = 1,
    Both = 2
}

public class CameraShakeSettings
{
    public CameraShakeMode Mode = CameraShakeMode.Both;

    // Overall shake multiplier applied to all shake axes.
    public float Trauma = 1f;

    // For rotational shake these are radians. For positional shake these are world units.
    public vec3 Strength = new vec3(0.03f, 0.03f, 0.03f);

    // Per-axis angular/noise speed multipliers.
    public vec3 Speed = new vec3(3f, 3.5f, 2.5f);

    // Per-axis phase offsets for deterministic shake pattern variation.
    public vec3 Offset = vec3.Zero;
}

public class CameraEffect
{
    public CameraEffectType Type = CameraEffectType.CameraShake;
    public CameraShakeSettings Shake = new CameraShakeSettings();
    public FilmGrainSettings FilmGrain = new FilmGrainSettings();
    public DepthOfFieldSettings DepthOfField = new();
    public BloomSettings Bloom = new();
}
