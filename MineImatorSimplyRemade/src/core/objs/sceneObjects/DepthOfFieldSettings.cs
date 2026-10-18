namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum DepthOfFieldFocusTarget
{
    PickPoint,
    Foreground,
    Background
}

public class DepthOfFieldSettings
{
    public float Depth { get; set; } = 10f;
    public float FocusRange { get; set; } = 2f;
    public float FadeSize { get; set; } = 5f;
    public float BlurSize { get; set; } = 50f;
    public float AnamorphicRatio { get; set; } = 1f;
    public float EdgeHighlight { get; set; }
    public float HighlightThreshold { get; set; } = 0.8f;
    public float HighlightStrength { get; set; } = 1f;
    public bool ColorFringe { get; set; }
    public float RedAngle { get; set; }
    public float GreenAngle { get; set; } = 120f;
    public float BlueAngle { get; set; } = 240f;
    public float RedOffset { get; set; } = 1f;
    public float GreenOffset { get; set; } = 1f;
    public float BlueOffset { get; set; } = 1f;

    public DepthOfFieldSettings Clone() => (DepthOfFieldSettings)MemberwiseClone();

    // Shared by the property editor and timeline so every exposed control is animatable.
    public static readonly (string Key, string Label, float Min, float Max)[] Controls =
    [
        ("depth", "Depth", 0f, 1000000f),
        ("focus_range", "Focus Range", 0f, 1000000f),
        ("fade_size", "Fade Size", 0f, 1000000f),
        ("blur_size", "Blur Size (%)", 0f, 100f),
        ("anamorphic_ratio", "Anamorphic Ratio", 0.1f, 10f),
        ("edge_highlight", "Edge Highlight", 0f, 1f),
        ("highlight_threshold", "Highlight Threshold", 0f, 1f),
        ("highlight_strength", "Highlight Strength", 0f, 10f),
        ("color_fringe", "Color Fringe", 0f, 1f),
        ("red_angle", "Red Angle (degrees)", -360f, 360f),
        ("green_angle", "Green Angle (degrees)", -360f, 360f),
        ("blue_angle", "Blue Angle (degrees)", -360f, 360f),
        ("red_offset", "Red Offset (pixels at 1080p)", -100f, 100f),
        ("green_offset", "Green Offset (pixels at 1080p)", -100f, 100f),
        ("blue_offset", "Blue Offset (pixels at 1080p)", -100f, 100f)
    ];

    public float GetValue(string key) => key switch
    {
        "depth" => Depth,
        "focus_range" => FocusRange,
        "fade_size" => FadeSize,
        "blur_size" => BlurSize,
        "anamorphic_ratio" => AnamorphicRatio,
        "edge_highlight" => EdgeHighlight,
        "highlight_threshold" => HighlightThreshold,
        "highlight_strength" => HighlightStrength,
        "color_fringe" => ColorFringe ? 1f : 0f,
        "red_angle" => RedAngle,
        "green_angle" => GreenAngle,
        "blue_angle" => BlueAngle,
        "red_offset" => RedOffset,
        "green_offset" => GreenOffset,
        "blue_offset" => BlueOffset,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown depth-of-field control.")
    };

    public void SetValue(string key, float value)
    {
        switch (key)
        {
            case "depth": Depth = value; break;
            case "focus_range": FocusRange = value; break;
            case "fade_size": FadeSize = value; break;
            case "blur_size": BlurSize = value; break;
            case "anamorphic_ratio": AnamorphicRatio = value; break;
            case "edge_highlight": EdgeHighlight = value; break;
            case "highlight_threshold": HighlightThreshold = value; break;
            case "highlight_strength": HighlightStrength = value; break;
            case "color_fringe": ColorFringe = value >= 0.5f; break;
            case "red_angle": RedAngle = value; break;
            case "green_angle": GreenAngle = value; break;
            case "blue_angle": BlueAngle = value; break;
            case "red_offset": RedOffset = value; break;
            case "green_offset": GreenOffset = value; break;
            case "blue_offset": BlueOffset = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown depth-of-field control.");
        }
    }

    public static float LinearizeDepth(float depth, float near, float far) =>
        (float)(near / (1.0 - depth + depth * (double)near / far));
}
