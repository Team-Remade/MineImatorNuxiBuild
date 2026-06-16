using GlmSharp;

namespace MineImatorSimplyRemade.gizmo;

/// <summary>
/// Port of GizmoPlugin/GizmoHelper.cs.
/// Only GetEdge is needed for the gizmo; ScaledOrthogonal lives in GizmoMath.
/// </summary>
public static class GizmoHelper
{
    /// <summary>
    /// Port of https://github.com/godotengine/godot/blob/master/core/math/aabb.cpp#L361
    /// Returns the two endpoints of AABB edge <paramref name="edge"/> (0..11).
    /// </summary>
    public static void GetEdge(vec3 position, vec3 size, int edge, out vec3 from, out vec3 to)
    {
        from = to = default;
        switch (edge)
        {
            case 0:
                from = new(position.x + size.x, position.y, position.z);
                to   = new(position.x, position.y, position.z);
                break;
            case 1:
                from = new(position.x + size.x, position.y, position.z + size.z);
                to   = new(position.x + size.x, position.y, position.z);
                break;
            case 2:
                from = new(position.x, position.y, position.z + size.z);
                to   = new(position.x + size.x, position.y, position.z + size.z);
                break;
            case 3:
                from = new(position.x, position.y, position.z);
                to   = new(position.x, position.y, position.z + size.z);
                break;
            case 4:
                from = new(position.x, position.y + size.y, position.z);
                to   = new(position.x + size.x, position.y + size.y, position.z);
                break;
            case 5:
                from = new(position.x + size.x, position.y + size.y, position.z);
                to   = new(position.x + size.x, position.y + size.y, position.z + size.z);
                break;
            case 6:
                from = new(position.x + size.x, position.y + size.y, position.z + size.z);
                to   = new(position.x, position.y + size.y, position.z + size.z);
                break;
            case 7:
                from = new(position.x, position.y + size.y, position.z + size.z);
                to   = new(position.x, position.y + size.y, position.z);
                break;
            case 8:
                from = new(position.x, position.y, position.z + size.z);
                to   = new(position.x, position.y + size.y, position.z + size.z);
                break;
            case 9:
                from = new(position.x, position.y, position.z);
                to   = new(position.x, position.y + size.y, position.z);
                break;
            case 10:
                from = new(position.x + size.x, position.y, position.z);
                to   = new(position.x + size.x, position.y + size.y, position.z);
                break;
            case 11:
                from = new(position.x + size.x, position.y, position.z + size.z);
                to   = new(position.x + size.x, position.y + size.y, position.z + size.z);
                break;
        }
    }
}
