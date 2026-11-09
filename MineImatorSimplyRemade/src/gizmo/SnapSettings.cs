using GlmSharp;

namespace MineImatorSimplyRemade.gizmo;

public enum SnapMode { Increment, Grid, Geometry }

[Flags]
public enum SnapElements { Vertex = 1, EdgeMidpoint = 2, Face = 4, Origin = 8 }

public sealed class SnapSettings
{
    public bool Enabled { get; set; }
    private SnapMode _mode;
    private SnapElements _elements = SnapElements.Vertex;
    public SnapMode Mode
    {
        get => _mode;
        set => _mode = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    public SnapElements Elements
    {
        get => _elements;
        set => _elements = value != 0 && (value & ~(SnapElements.Vertex | SnapElements.EdgeMidpoint |
            SnapElements.Face | SnapElements.Origin)) == 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    public bool Move { get; set; } = true;
    public bool Rotate { get; set; } = true;
    public bool Scale { get; set; } = true;
    public bool AlignToNormal { get; set; }
    public bool XRay { get; set; }
    private float _translation = 1f, _rotation = 15f, _scale = 0.1f, _radius = 16f;
    public float TranslationStep { get => _translation; set => _translation = Positive(value); }
    public float RotationStep { get => _rotation; set => _rotation = Positive(value); }
    public float ScaleStep { get => _scale; set => _scale = Positive(value); }
    public float PixelRadius
    {
        get => _radius;
        set => _radius = float.IsFinite(value) && value >= 1f && value <= 256f
            ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static float Positive(float value) => float.IsFinite(value) && value > 0f
        ? value : throw new ArgumentOutOfRangeException(nameof(value), "Snap values must be finite and positive.");
}

public static class SnapMath
{
    public static float Round(float value, float step) =>
        MathF.Round(value / step, MidpointRounding.AwayFromZero) * step;

    public static vec3 Constrain(vec3 delta, mat4 basis, Gizmo3D.TransformPlane plane)
    {
        vec3 result = vec3.Zero;
        for (int axis = 0; axis < 3; axis++)
        {
            if (!Allows(plane, axis)) continue;
            vec3 direction = new vec3(basis[axis, 0], basis[axis, 1], basis[axis, 2]).Normalized;
            result += direction * vec3.Dot(delta, direction);
        }
        return result;
    }

    public static vec3 Translation(vec3 center, vec3 delta, mat4 basis,
        Gizmo3D.TransformPlane plane, float step, bool absolute)
    {
        vec3 result = vec3.Zero;
        for (int axis = 0; axis < 3; axis++)
        {
            if (!Allows(plane, axis)) continue;
            vec3 direction = new vec3(basis[axis, 0], basis[axis, 1], basis[axis, 2]).Normalized;
            float start = absolute ? vec3.Dot(center, direction) : 0f;
            result += direction * (Round(start + vec3.Dot(delta, direction), step) - start);
        }
        return result;
    }

    private static bool Allows(Gizmo3D.TransformPlane plane, int axis) => plane switch
    {
        Gizmo3D.TransformPlane.X => axis == 0,
        Gizmo3D.TransformPlane.Y => axis == 1,
        Gizmo3D.TransformPlane.Z => axis == 2,
        Gizmo3D.TransformPlane.YZ => axis != 0,
        Gizmo3D.TransformPlane.XZ => axis != 1,
        Gizmo3D.TransformPlane.XY => axis != 2,
        _ => true
    };

    public static mat4 Align(vec3 from, vec3 to)
    {
        from = from.Normalized;
        to = to.Normalized;
        float dot = Math.Clamp(vec3.Dot(from, to), -1f, 1f);
        if (dot > 0.999999f) return mat4.Identity;
        vec3 axis = vec3.Cross(from, to);
        if (axis.LengthSqr < 1e-10f)
            axis = vec3.Cross(from, MathF.Abs(from.x) < 0.9f ? vec3.UnitX : vec3.UnitY);
        return GizmoMath.AxisAngle(axis.Normalized, MathF.Acos(dot));
    }
}
