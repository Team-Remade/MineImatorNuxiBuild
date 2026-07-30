using System.Numerics;
using GlmSharp;

namespace MineImatorSimplyRemade.core.mdl;

public struct Frustum
{
    private vec4 _left, _right, _bottom, _top, _near, _far;

    public static Frustum FromViewProj(mat4 vp)
    {
        Frustum f;
        f._left   = Row(vp, 3) + Row(vp, 0);
        f._right  = Row(vp, 3) - Row(vp, 0);
        f._bottom = Row(vp, 3) + Row(vp, 1);
        f._top    = Row(vp, 3) - Row(vp, 1);
        f._near   = Row(vp, 3) + Row(vp, 2);
        f._far    = Row(vp, 3) - Row(vp, 2);

        Normalize(ref f._left);
        Normalize(ref f._right);
        Normalize(ref f._bottom);
        Normalize(ref f._top);
        Normalize(ref f._near);
        Normalize(ref f._far);

        return f;
    }

    public bool TestSphere(vec3 center, float radius)
    {
        // Skip the near-plane test — when the camera is inside or very close to a
        // mesh the sphere centre may be behind the near plane even though part of
        // the mesh is still visible.  The classic sphere-vs-frustum test would
        // incorrectly cull it.  The tiny GPU cost of not culling near objects far
        // outweighs the visual popping of geometry disappearing when the user
        // zooms in close.
        //if (Dot(center, _near)   + radius < 0) return false;
        if (Dot(center, _far)    + radius < 0) return false;
        if (Dot(center, _left)   + radius < 0) return false;
        if (Dot(center, _right)  + radius < 0) return false;
        if (Dot(center, _bottom) + radius < 0) return false;
        if (Dot(center, _top)    + radius < 0) return false;
        return true;
    }

    private static float Dot(vec3 v, vec4 plane) =>
        v.x * plane.x + v.y * plane.y + v.z * plane.z + plane.w;

    /// <summary>
    /// Extracts row <paramref name="r"/> from column-major <paramref name="m"/>.
    /// GlmSharp indexer is <c>m[col, row]</c>, so we must use <c>m[0, r]</c>
    /// for the first element of row r, <c>m[1, r]</c> for the second, etc.
    /// </summary>
    private static vec4 Row(mat4 m, int r) =>
        new vec4(m[0, r], m[1, r], m[2, r], m[3, r]);

    private static void Normalize(ref vec4 plane)
    {
        float len = MathF.Sqrt(plane.x * plane.x + plane.y * plane.y + plane.z * plane.z);
        if (len > 1e-8f)
        {
            float inv = 1f / len;
            plane.x *= inv; plane.y *= inv; plane.z *= inv; plane.w *= inv;
        }
    }
}
