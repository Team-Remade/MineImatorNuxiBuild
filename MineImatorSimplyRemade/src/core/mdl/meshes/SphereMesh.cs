using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>A smooth UV sphere centred at the origin.</summary>
public sealed class SphereMesh : Mesh
{
    public float Radius { get; private set; }
    public int Segments { get; private set; }
    public int Rings { get; private set; }
    public bool SmoothShading { get; private set; }

    public SphereMesh(GL gl, float radius = 0.5f, int segments = 32, int rings = 30,
                      bool smoothShading = true) : base(gl)
    {
        if (radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), "The radius must be positive.");
        if (segments < 3)
            throw new ArgumentOutOfRangeException(nameof(segments), "A sphere needs at least three segments.");
        if (rings < 2)
            throw new ArgumentOutOfRangeException(nameof(rings), "A sphere needs at least two rings.");

        Radius = radius;
        Segments = segments;
        Rings = rings;
        SmoothShading = smoothShading;

        GenerateVertices();
        Upload();
    }

    public void SetGeometry(int segments, int rings, bool smoothShading)
    {
        segments = Math.Clamp(segments, 3, 256);
        rings = Math.Clamp(rings, 2, 254);
        if (Segments == segments && Rings == rings && SmoothShading == smoothShading)
            return;

        Segments = segments;
        Rings = rings;
        SmoothShading = smoothShading;
        Vertices.Clear();
        Normals.Clear();
        TexCoords.Clear();
        Indices = null;
        GenerateVertices();
        Upload();
    }

    private SphereMesh(GL gl, SphereMesh source) : base(gl)
    {
        Radius = source.Radius;
        Segments = source.Segments;
        Rings = source.Rings;
        SmoothShading = source.SmoothShading;
    }

    protected override Mesh CreateCloneGeometry(GL gl) => new SphereMesh(gl, this);

    protected override void GenerateVertices()
    {
        // Reference Z-axial coordinates become app Y-up via (x,z,-y).
        vec3 Normal(int segment, int ring)
        {
            if (ring == 0) return new vec3(0f, -1f, 0f);
            if (ring == Rings) return new vec3(0f, 1f, 0f);
            float longitude = segment == Segments ? 0f : MathF.Tau * segment / Segments;
            float latitude = MathF.PI * ring / Rings;
            float radial = MathF.Sin(latitude);
            return new vec3(MathF.Sin(longitude) * radial, -MathF.Cos(latitude),
                            MathF.Cos(longitude) * radial);
        }

        void Add(vec3 radialNormal, float u, vec3 flatNormal)
        {
            Vertices.Add(radialNormal * Radius);
            Normals.Add(SmoothShading ? radialNormal : flatNormal);
            // Reference U has a quarter turn; V follows height, not latitude angle.
            TexCoords.Add(new vec2(1.25f - u, (1f + radialNormal.y) * 0.5f));
        }

        for (int segment = 0; segment < Segments; segment++)
        {
            float u0 = (float)segment / Segments;
            float u1 = (float)(segment + 1) / Segments;
            for (int ring = 0; ring < Rings; ring++)
            {
                vec3 n1 = Normal(segment, ring);
                vec3 n2 = Normal(segment, ring + 1);
                vec3 n3 = Normal(segment + 1, ring);
                vec3 n4 = Normal(segment + 1, ring + 1);
                vec3 flatNormal = (n1 + n2 + n3 + n4).Normalized;

                // Pole UVs stay local to each wedge; omit zero-area pole triangles.
                if (ring > 0)
                {
                    Add(n1, u0, flatNormal);
                    Add(n3, u1, flatNormal);
                    Add(n4, u1, flatNormal);
                }
                if (ring + 1 < Rings)
                {
                    Add(n1, u0, flatNormal);
                    Add(n4, u1, flatNormal);
                    Add(n2, u0, flatNormal);
                }
            }
        }
        Indices = null;
    }
}
