using GlmSharp;

namespace MineImatorSimplyRemade.core.mdl.material.materials;

public struct MaterialColorAdjustments
{
    public vec3 MultiplyHsv = vec3.Ones;
    public vec3 AddRgb;
    public vec3 AddHsv;
    public vec3 SubtractRgb;
    public vec3 SubtractHsv;

    public MaterialColorAdjustments() { }

    public static readonly (string Key, string Label, bool Hsv)[] Channels =
    {
        ("multiply_hsv", "Multiply HSV", true),
        ("add_rgb", "Add RGB", false),
        ("add_hsv", "Add HSV", true),
        ("subtract_rgb", "Subtract RGB", false),
        ("subtract_hsv", "Subtract HSV", true)
    };

    public readonly bool IsNeutral => MultiplyHsv == vec3.Ones && AddRgb == vec3.Zero &&
        AddHsv == vec3.Zero && SubtractRgb == vec3.Zero && SubtractHsv == vec3.Zero;

    public static bool IsChannel(string key) =>
        key is "multiply_hsv" or "add_rgb" or "add_hsv" or "subtract_rgb" or "subtract_hsv";

    public vec3 this[string key]
    {
        readonly get => key switch
        {
            "multiply_hsv" => MultiplyHsv,
            "add_rgb" => AddRgb,
            "add_hsv" => AddHsv,
            "subtract_rgb" => SubtractRgb,
            "subtract_hsv" => SubtractHsv,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown material color operation.")
        };
        set
        {
            switch (key)
            {
                case "multiply_hsv": MultiplyHsv = value; break;
                case "add_rgb": AddRgb = value; break;
                case "add_hsv": AddHsv = value; break;
                case "subtract_rgb": SubtractRgb = value; break;
                case "subtract_hsv": SubtractHsv = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown material color operation.");
            }
        }
    }

    public void Inherit(MaterialColorAdjustments parent)
    {
        MultiplyHsv *= parent.MultiplyHsv;
        AddRgb += parent.AddRgb;
        AddHsv += parent.AddHsv;
        SubtractRgb += parent.SubtractRgb;
        SubtractHsv += parent.SubtractHsv;
    }
}
