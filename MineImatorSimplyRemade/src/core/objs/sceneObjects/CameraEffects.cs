using GlmSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum CameraEffectType
{
    CameraShake = 0,
    FilmGrain = 1,
    DepthOfField = 2,
    Bloom = 3,
    ColorCorrection = 4,
    LensDirt = 5,
    Vignette = 6,
    ChromaticAberration = 7,
    Distort = 8
}

public class DistortSettings
{
    public bool RepeatImage { get; set; }
    public float ZoomAmount { get; set; } = 100f;
    public float DistortAmount { get; set; }

    public DistortSettings Clone() => (DistortSettings)MemberwiseClone();

    public static readonly (string Key, string Label, float Min, float Max)[] Controls =
    [
        ("repeat_image", "Repeat Image", 0f, 1f),
        ("zoom_amount", "Zoom Amount (%)", 1f, 1000f),
        ("distort_amount", "Distort Amount (%)", -100f, 100f)
    ];

    public float GetValue(string key) => key switch
    {
        "repeat_image" => RepeatImage ? 1f : 0f,
        "zoom_amount" => ZoomAmount,
        "distort_amount" => DistortAmount,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown distort control.")
    };

    public void SetValue(string key, float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Distort values must be finite.");
        switch (key)
        {
            case "repeat_image": RepeatImage = value >= 0.5f; break;
            case "zoom_amount": ZoomAmount = Math.Clamp(value, 1f, 1000f); break;
            case "distort_amount": DistortAmount = Math.Clamp(value, -100f, 100f); break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown distort control.");
        }
    }
}

public class ChromaticAberrationSettings
{
    public float BlurAmount { get; set; }
    public bool DistortRgb { get; set; } = true;
    public float RedOffset { get; set; } = 1f;
    public float GreenOffset { get; set; }
    public float BlueOffset { get; set; } = -1f;

    public ChromaticAberrationSettings Clone() => (ChromaticAberrationSettings)MemberwiseClone();

    public static readonly (string Key, string Label, float Min, float Max)[] Controls =
    [
        ("blur_amount", "Blur Amount (%)", 0f, 100f),
        ("distort_rgb", "Distort RGB", 0f, 1f),
        ("red_offset", "Red Offset (%)", -100f, 100f),
        ("green_offset", "Green Offset (%)", -100f, 100f),
        ("blue_offset", "Blue Offset (%)", -100f, 100f)
    ];

    public float GetValue(string key) => key switch
    {
        "blur_amount" => BlurAmount,
        "distort_rgb" => DistortRgb ? 1f : 0f,
        "red_offset" => RedOffset,
        "green_offset" => GreenOffset,
        "blue_offset" => BlueOffset,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown chromatic aberration control.")
    };

    public void SetValue(string key, float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Chromatic aberration values must be finite.");
        switch (key)
        {
            case "blur_amount": BlurAmount = Math.Clamp(value, 0f, 100f); break;
            case "distort_rgb": DistortRgb = value >= 0.5f; break;
            case "red_offset": RedOffset = Math.Clamp(value, -100f, 100f); break;
            case "green_offset": GreenOffset = Math.Clamp(value, -100f, 100f); break;
            case "blue_offset": BlueOffset = Math.Clamp(value, -100f, 100f); break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown chromatic aberration control.");
        }
    }
}

public class VignetteSettings
{
    public float Radius { get; set; } = 50f;
    public float Softness { get; set; } = 50f;
    public float Strength { get; set; } = 50f;
    public float ColorR { get; set; }
    public float ColorG { get; set; }
    public float ColorB { get; set; }

    public VignetteSettings Clone() => (VignetteSettings)MemberwiseClone();

    public static readonly (string Key, string Label, float Min, float Max)[] Controls =
    [
        ("radius", "Radius (%)", 0f, 100f),
        ("softness", "Softness (%)", 0f, 100f),
        ("strength", "Strength (%)", 0f, 100f),
        ("color.r", "Color R", 0f, 1f),
        ("color.g", "Color G", 0f, 1f),
        ("color.b", "Color B", 0f, 1f)
    ];

    public float GetValue(string key) => key switch
    {
        "radius" => Radius,
        "softness" => Softness,
        "strength" => Strength,
        "color.r" => ColorR,
        "color.g" => ColorG,
        "color.b" => ColorB,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown vignette control.")
    };

    public void SetValue(string key, float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Vignette values must be finite.");
        switch (key)
        {
            case "radius": Radius = Math.Clamp(value, 0f, 100f); break;
            case "softness": Softness = Math.Clamp(value, 0f, 100f); break;
            case "strength": Strength = Math.Clamp(value, 0f, 100f); break;
            case "color.r": ColorR = Math.Clamp(value, 0f, 1f); break;
            case "color.g": ColorG = Math.Clamp(value, 0f, 1f); break;
            case "color.b": ColorB = Math.Clamp(value, 0f, 1f); break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown vignette control.");
        }
    }
}

public class LensDirtSettings
{
    public string TexturePath { get; set; } = "";
    public bool AffectedByBloom { get; set; } = true;
    public bool AffectedByGlow { get; set; } = true;
    public float Radius { get; set; } = 50f;
    public float Intensity { get; set; } = 100f;
    public float Power { get; set; } = 100f;

    public LensDirtSettings Clone() => (LensDirtSettings)MemberwiseClone();

    public static readonly (string Key, string Label, float Min, float Max)[] Controls =
    [
        ("affected_by_bloom", "Affected by Bloom", 0f, 1f),
        ("affected_by_glow", "Affected by Glow", 0f, 1f),
        ("radius", "Radius (%)", 0f, 100f),
        ("intensity", "Intensity (%)", 0f, 1000f),
        ("power", "Power (%)", 1f, 1000f)
    ];

    public float GetValue(string key) => key switch
    {
        "affected_by_bloom" => AffectedByBloom ? 1f : 0f,
        "affected_by_glow" => AffectedByGlow ? 1f : 0f,
        "radius" => Radius,
        "intensity" => Intensity,
        "power" => Power,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown lens dirt control.")
    };

    public void SetValue(string key, float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Lens dirt values must be finite.");
        switch (key)
        {
            case "affected_by_bloom": AffectedByBloom = value >= 0.5f; break;
            case "affected_by_glow": AffectedByGlow = value >= 0.5f; break;
            case "radius": Radius = Math.Clamp(value, 0f, 100f); break;
            case "intensity": Intensity = Math.Clamp(value, 0f, 1000f); break;
            case "power": Power = Math.Clamp(value, 1f, 1000f); break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown lens dirt control.");
        }
    }
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
    public LensDirtSettings LensDirt = new();
    public VignetteSettings Vignette = new();
    public ChromaticAberrationSettings ChromaticAberration = new();
    public DistortSettings Distort = new();
    public ColorCorrectionSettings ColorCorrection = new();
}
