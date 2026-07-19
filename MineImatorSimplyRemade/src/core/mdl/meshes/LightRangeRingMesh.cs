using System;
using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>
/// A procedural thin annulus (ring) on the XZ plane, centred at the local origin.
/// Built at unit radius (inner = 0.97, outer = 1.0) so the caller can scale the
/// model matrix by the desired world-space radius.
/// 64 segments ≈ 5.6° per step — visually smooth at typical editor zoom levels.
/// Used as a range-of-influence indicator for <c>LightSceneObject</c>.
/// </summary>
public class LightRangeRingMesh : Mesh
{
    public const int   Segments    = 64;
    public const float InnerRadius = 0.97f;
    public const float OuterRadius = 1.0f;

    public LightRangeRingMesh(GL gl) : base(gl)
    {
        GenerateVertices();
        Upload();
    }

    protected override void GenerateVertices()
    {
        var verts = new List<vec3>(Segments * 2);
        var norms = new List<vec3>(Segments * 2);
        var idx   = new List<uint>(Segments * 6);

        // Vertices 0..Segments-1   : outer ring
        // Vertices Segments..2N-1  : inner ring
        // Each segment produces a quad: outer[i], inner[i], outer[i+1], inner[i+1]
        // split into two CCW triangles when viewed from +Y.
        for (int i = 0; i < Segments; i++)
        {
            float t = (i / (float)Segments) * MathF.Tau;
            float c = MathF.Cos(t);
            float s = MathF.Sin(t);

            verts.Add(new vec3(c * OuterRadius, 0f, s * OuterRadius)); // outer i
            verts.Add(new vec3(c * InnerRadius, 0f, s * InnerRadius)); // inner i
        }

        for (int i = 0; i < Segments; i++)
        {
            uint outer0 = (uint)(i * 2);
            uint inner0 = (uint)(i * 2 + 1);
            uint outer1 = (uint)(((i + 1) % Segments) * 2);
            uint inner1 = (uint)(((i + 1) % Segments) * 2 + 1);

            // CCW from +Y: outer0 → inner0 → outer1
            idx.Add(outer0);
            idx.Add(inner0);
            idx.Add(outer1);
            // CCW from +Y: outer1 → inner0 → inner1
            idx.Add(outer1);
            idx.Add(inner0);
            idx.Add(inner1);
        }

        // Flat normal pointing up; both faces of the ring use the same normal
        // because the ring is rendered with DoubleSided.
        var up = new vec3(0f, 1f, 0f);
        for (int i = 0; i < Segments * 2; i++) norms.Add(up);

        Vertices.AddRange(verts);
        Normals.AddRange(norms);
        Indices = idx.ToArray();

        DoubleSided = true;
    }
}
