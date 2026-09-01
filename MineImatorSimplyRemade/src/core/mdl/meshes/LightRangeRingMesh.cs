using System.Numerics;
using MineImatorSimplyRemade.core.render;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>
/// A procedural thin annulus (ring) on the XZ plane, centred at the local origin.
/// Built at unit radius (inner = 0.97, outer = 1.0) so the caller can scale the
/// model matrix by the desired world-space radius. Used as a range-of-influence
/// indicator for <c>LightSceneObject</c>. Ported from the old GL <c>Mesh</c>
/// subclass to <see cref="VeldridMesh"/>.
/// </summary>
public class LightRangeRingMesh : VeldridMesh
{
    public const int Segments = 64;
    public const float InnerRadius = 0.97f;
    public const float OuterRadius = 1.0f;

    public LightRangeRingMesh() : base(VeldridContext.Device)
    {
        GenerateVertices();
        Upload(VeldridContext.StandardOutputDescription);
    }

    private void GenerateVertices()
    {
        var verts = new List<Vector3>(Segments * 2);
        var norms = new List<Vector3>(Segments * 2);
        var idx = new List<uint>(Segments * 6);

        for (int i = 0; i < Segments; i++)
        {
            float t = (i / (float)Segments) * MathF.Tau;
            float c = MathF.Cos(t);
            float s = MathF.Sin(t);

            verts.Add(new Vector3(c * OuterRadius, 0f, s * OuterRadius));
            verts.Add(new Vector3(c * InnerRadius, 0f, s * InnerRadius));
        }

        for (int i = 0; i < Segments; i++)
        {
            uint outer0 = (uint)(i * 2);
            uint inner0 = (uint)(i * 2 + 1);
            uint outer1 = (uint)(((i + 1) % Segments) * 2);
            uint inner1 = (uint)(((i + 1) % Segments) * 2 + 1);

            idx.Add(outer0);
            idx.Add(inner0);
            idx.Add(outer1);
            idx.Add(outer1);
            idx.Add(inner0);
            idx.Add(inner1);
        }

        var up = new Vector3(0f, 1f, 0f);
        for (int i = 0; i < Segments * 2; i++) norms.Add(up);

        Vertices.AddRange(verts);
        Normals.AddRange(norms);
        Indices = idx.ToArray();

        DoubleSided = true;
    }
}
