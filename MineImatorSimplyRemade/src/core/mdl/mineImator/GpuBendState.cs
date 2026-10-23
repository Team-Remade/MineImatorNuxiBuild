using GlmSharp;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

/// <summary>Static shape metadata and dynamic angle used by GPU bend deformation.</summary>
public sealed class GpuBendState
{
    public vec3 Angle;
    public vec3 Pivot;
    public vec3 ShapePosition;
    public mat4 InverseShapeTransform = mat4.Identity;
    public int SegmentAxis;
    public int ScaleCorrectionAxis = -1;
    public float AxisStart;
    public float BendStart;
    public float BendEnd;
    public float BendSize;
    public bool InvertWeight;
    public bool Sharp;
    // All planes omit block expansion; extruded planes retain per-segment pinch correction.
    public bool Plane;
    public bool ExtrudedPlane;
}
