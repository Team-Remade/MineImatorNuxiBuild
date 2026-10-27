using GlmSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum CameraEffectType
{
    CameraShake = 0,
    FilmGrain = 1,
    DepthOfField = 2,
    Bloom = 3,
    ColorCorrection = 4
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

public class ColorCorrectionSettings
{
    public float Contrast { get; set; } = 100f;
    public float Brightness { get; set; } = 100f;
    public float Saturation { get; set; } = 100f;
    public float Vibrance { get; set; } = 0f;
    public float Hue { get; set; } = 0f;
    public bool Invert { get; set; } = false;
    public float InvertStrength { get; set; } = 100f;
    public float ColorBurn { get; set; } = 0f;

    public ColorCorrectionSettings Clone() => (ColorCorrectionSettings)MemberwiseClone();

    // Suggested slider ranges only; typed percentage and hue values remain unrestricted.
    public static readonly (string Key, string Label, float Min, float Max)[] Controls =
    [
        ("contrast", "Contrast (%)", 0f, 200f),
        ("brightness", "Brightness (%)", 0f, 200f),
        ("saturation", "Saturation (%)", 0f, 200f),
        ("vibrance", "Vibrance (%)", -100f, 100f),
        ("hue", "Hue (degrees)", -180f, 180f),
        ("invert", "Invert", 0f, 1f),
        ("invert_strength", "Invert Strength (%)", 0f, 100f),
        ("color_burn", "Color Burn (%)", 0f, 100f)
    ];

    public float GetValue(string key) => key switch
    {
        "contrast" => Contrast,
        "brightness" => Brightness,
        "saturation" => Saturation,
        "vibrance" => Vibrance,
        "hue" => Hue,
        "invert" => Invert ? 1f : 0f,
        "invert_strength" => InvertStrength,
        "color_burn" => ColorBurn,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown color correction control.")
    };

    public void SetValue(string key, float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Color correction values must be finite.");

        switch (key)
        {
            case "contrast": Contrast = value; break;
            case "brightness": Brightness = value; break;
            case "saturation": Saturation = value; break;
            case "vibrance": Vibrance = value; break;
            case "hue": Hue = value; break;
            case "invert": Invert = value >= 0.5f; break;
            case "invert_strength": InvertStrength = Math.Clamp(value, 0f, 100f); break;
            case "color_burn": ColorBurn = Math.Clamp(value, 0f, 100f); break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown color correction control.");
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
    public ColorCorrectionSettings ColorCorrection = new();
}
