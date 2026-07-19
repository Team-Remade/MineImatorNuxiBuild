using System;
using System.Collections.Generic;
using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>
/// Procedural "stick" mesh used as the unselected spot-light aim indicator.
/// A short, thin rectangular cross-section prism that runs from the local
/// origin to z = +1, with X / Y half-thickness of 0.015.  The viewport scales
/// the model matrix along the Z axis by the spot light's <c>LightRange</c>
/// so the stick always reaches the same distance as the cone base it
/// represents when the light is selected.
/// </summary>
public class LightStickMesh : Mesh
{
    /// Thickness of the stick measured in the XY plane.  The default is thin
    /// enough to read as a line at most editor zooms without vanishing; increase
    /// if your camera is very far away or decrease if it looks too chunky.
    public const float HalfThickness = 0.0015f;

    public LightStickMesh(GL gl) : base(gl)
    {
        GenerateVertices();
        Upload();
    }

    protected override void GenerateVertices()
    {
        const float h = HalfThickness;
        const float backZ = 1f;

        // Six faces of a thin rectangular prism.  Each face lists its own
        // vertices so face-normals come out flat (no smoothing across edges).
        var verts = new List<vec3>();
        var norms = new List<vec3>();
        var idx   = new List<uint>();

        void AddQuad(vec3 a, vec3 b, vec3 c, vec3 d, vec3 n)
        {
            uint start = (uint)verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            idx.Add(start);     idx.Add(start + 1); idx.Add(start + 2);
            idx.Add(start);     idx.Add(start + 2); idx.Add(start + 3);
        }

        // +X face
        AddQuad(
            new vec3( h, -h, 0f),  new vec3( h,  h, 0f),
            new vec3( h,  h, backZ), new vec3( h, -h, backZ),
            new vec3( 1f, 0f, 0f));
        // -X face
        AddQuad(
            new vec3(-h,  h, 0f),  new vec3(-h, -h, 0f),
            new vec3(-h, -h, backZ), new vec3(-h,  h, backZ),
            new vec3(-1f, 0f, 0f));
        // +Y face
        AddQuad(
            new vec3(-h,  h, 0f),  new vec3( h,  h, 0f),
            new vec3( h,  h, backZ), new vec3(-h,  h, backZ),
            new vec3(0f,  1f, 0f));
        // -Y face
        AddQuad(
            new vec3( h, -h, 0f),  new vec3(-h, -h, 0f),
            new vec3(-h, -h, backZ), new vec3( h, -h, backZ),
            new vec3(0f, -1f, 0f));

        Vertices.AddRange(verts);
        Normals.AddRange(norms);
        Indices = idx.ToArray();

        DoubleSided = true;
    }
}
