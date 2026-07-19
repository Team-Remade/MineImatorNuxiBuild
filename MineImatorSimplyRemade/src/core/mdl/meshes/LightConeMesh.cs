using System;
using System.Collections.Generic;
using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>
/// Procedural cone mesh used as the spot-light coverage indicator.
/// Apex is at the local origin (0,0,0); the base sits on the plane
/// z = +1 with unit radius (1,0,1)..(0,1,1)..(-1,0,1)..(0,-1,1).
/// The viewport scales the model matrix by the spot light's
/// <c>tan(halfAngle) * range</c> on the X / Y axes and by <c>range</c>
/// on the Z axis so the same geometry fits any cone / range combination.
/// 48 segments are sufficient for a smooth silhouette at typical editor zooms.
/// </summary>
public class LightConeMesh : Mesh
{
    public const int Segments = 48;

    public LightConeMesh(GL gl) : base(gl)
    {
        GenerateVertices();
        Upload();
    }

    protected override void GenerateVertices()
    {
        // Apex at the origin; base ring at z = +1 with radius 1.
        const float baseZ = 1f;
        const float baseR = 1f;

        var verts = new List<vec3>(Segments + 1);
        var norms = new List<vec3>(Segments + 1);
        var idx   = new List<uint>(Segments * 6);

        verts.Add(new vec3(0f, 0f, 0f));   // 0  : apex
        norms.Add(new vec3(0f, 0f, 1f));   // placeholder; recomputed below

        for (int i = 0; i < Segments; i++)
        {
            float t = (i / (float)Segments) * MathF.Tau;
            float c = MathF.Cos(t);
            float s = MathF.Sin(t);
            verts.Add(new vec3(c * baseR, s * baseR, baseZ));
            // Outward-slanted normal; bias the side normal toward the base.
            vec3 n = new vec3(c, s, 1f).Normalized;
            norms.Add(n);
        }

        // Replace apex normal with the average of the side normals for a
        // smoother lighting break at the tip (still always flat-shaded because
        // each face is its own triangle).
        vec3 apexN = vec3.Zero;
        for (int i = 0; i < Segments; i++) apexN += norms[i + 1];
        norms[0] = apexN.Normalized;

        // Side faces: apex → baseRing[i] → baseRing[i+1]
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
