using System.Numerics;
using MineImatorSimplyRemade.core.render;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>
/// Procedural cone mesh used as the spot-light coverage indicator.
/// Apex is at the local origin (0,0,0); the base sits on the plane
/// z = +1 with unit radius. The viewport scales the model matrix by the spot
/// light's <c>tan(halfAngle) * range</c> on the X/Y axes and by <c>range</c>
/// on the Z axis so the same geometry fits any cone/range combination.
/// Ported from the old GL <c>Mesh</c> subclass to <see cref="VeldridMesh"/>.
/// </summary>
public class LightConeMesh : VeldridMesh
{
    public const int Segments = 48;

    public LightConeMesh() : base(VeldridContext.Device)
    {
        GenerateVertices();
        Upload(VeldridContext.StandardOutputDescription);
    }

    private void GenerateVertices()
    {
        const float baseZ = 1f;
        const float baseR = 1f;

        var verts = new List<Vector3>(Segments + 1);
        var norms = new List<Vector3>(Segments + 1);
        var idx = new List<uint>(Segments * 6);

        verts.Add(new Vector3(0f, 0f, 0f));
        norms.Add(new Vector3(0f, 0f, 1f)); // placeholder; recomputed below

        for (int i = 0; i < Segments; i++)
        {
            float t = (i / (float)Segments) * MathF.Tau;
            float c = MathF.Cos(t);
            float s = MathF.Sin(t);
            verts.Add(new Vector3(c * baseR, s * baseR, baseZ));
            Vector3 n = Vector3.Normalize(new Vector3(c, s, 1f));
            norms.Add(n);
        }

        Vector3 apexN = Vector3.Zero;
        for (int i = 0; i < Segments; i++) apexN += norms[i + 1];
        norms[0] = Vector3.Normalize(apexN);

        for (int i = 0; i < Segments; i++)
        {
            uint next = (uint)(((i + 1) % Segments) + 1);
            uint curr = (uint)(i + 1);
            idx.Add(0);
            idx.Add(curr);
            idx.Add(next);
        }

        Vertices.AddRange(verts);
        Normals.AddRange(norms);
        Indices = idx.ToArray();

        DoubleSided = true;
    }
}
