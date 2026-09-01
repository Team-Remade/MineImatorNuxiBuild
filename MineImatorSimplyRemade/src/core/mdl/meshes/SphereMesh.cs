using System.Numerics;
using MineImatorSimplyRemade.core.render;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>A smooth UV sphere centred at the origin. Ported from the old GL
/// <c>Mesh</c> subclass to <see cref="VeldridMesh"/>.</summary>
public sealed class SphereMesh : VeldridMesh
{
    public float Radius { get; private set; }
    public int Segments { get; private set; }
    public int Rings { get; private set; }
    public bool SmoothShading { get; private set; }

    public SphereMesh(float radius = 0.5f, int segments = 32, int rings = 16, bool smoothShading = true)
        : base(VeldridContext.Device)
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
        Upload(VeldridContext.StandardOutputDescription);
    }

    public void SetGeometry(int segments, int rings, bool smoothShading)
    {
        segments = Math.Clamp(segments, 3, 256);
        rings = Math.Clamp(rings, 2, 128);
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
        Upload(VeldridContext.StandardOutputDescription);
    }

    private void GenerateVertices()
    {
        // Use one vertex at each pole. Duplicating a zero-width pole row can
        // produce cracks at the cap on some drivers because every other
        // triangle in that row is degenerate.
        Vertices.Add(new Vector3(0f, Radius, 0f));
        Normals.Add(new Vector3(0f, 1f, 0f));
        TexCoords.Add(new Vector2(0.5f, 1f));

        // Interior rings duplicate only the longitude seam, allowing U to run
        // cleanly from 0 to 1 without splitting the actual sphere geometry.
        for (int ring = 1; ring < Rings; ring++)
        {
            float v = (float)ring / Rings;
            float latitude = v * MathF.PI;
            float sinLatitude = MathF.Sin(latitude);
            float cosLatitude = MathF.Cos(latitude);

            for (int segment = 0; segment <= Segments; segment++)
            {
                float u = (float)segment / Segments;
                float longitude = u * MathF.Tau;

                var normal = new Vector3(
                    sinLatitude * MathF.Cos(longitude),
                    cosLatitude,
                    sinLatitude * MathF.Sin(longitude));

                Vertices.Add(normal * Radius);
                Normals.Add(normal);
                TexCoords.Add(new Vector2(u, 1f - v));
            }
        }

        uint bottomPole = (uint)Vertices.Count;
        Vertices.Add(new Vector3(0f, -Radius, 0f));
        Normals.Add(new Vector3(0f, -1f, 0f));
        TexCoords.Add(new Vector2(0.5f, 0f));

        var indices = new List<uint>(Segments * (Rings - 1) * 6);
        int stride = Segments + 1;

        // Top cap.
        for (int segment = 0; segment < Segments; segment++)
        {
            indices.Add(0);
            indices.Add((uint)(segment + 2));
            indices.Add((uint)(segment + 1));
        }

        // Quads between the interior latitude rings.
        for (int ring = 0; ring < Rings - 2; ring++)
        {
            uint upper = (uint)(1 + ring * stride);
            uint lower = upper + (uint)stride;
            for (int segment = 0; segment < Segments; segment++)
            {
                uint topLeft = upper + (uint)segment;
                uint topRight = topLeft + 1;
                uint bottomLeft = lower + (uint)segment;
                uint bottomRight = bottomLeft + 1;

                indices.Add(topLeft);
                indices.Add(bottomRight);
                indices.Add(bottomLeft);
                indices.Add(topLeft);
                indices.Add(topRight);
                indices.Add(bottomRight);
            }
        }

        // Bottom cap.
        uint lastRing = (uint)(1 + (Rings - 2) * stride);
        for (int segment = 0; segment < Segments; segment++)
        {
            indices.Add(lastRing + (uint)segment);
            indices.Add(lastRing + (uint)segment + 1);
            indices.Add(bottomPole);
        }

        Indices = indices.ToArray();

        if (!SmoothShading)
            ConvertToFlatShading();
    }

    private void ConvertToFlatShading()
    {
        if (Indices == null) return;

        var flatVertices = new List<Vector3>(Indices.Length);
        var flatNormals = new List<Vector3>(Indices.Length);
        var flatUvs = new List<Vector2>(Indices.Length);

        for (int i = 0; i < Indices.Length; i += 3)
        {
            uint i0 = Indices[i];
            uint i1 = Indices[i + 1];
            uint i2 = Indices[i + 2];
            Vector3 normal = Vector3.Normalize(Vector3.Cross(Vertices[(int)i1] - Vertices[(int)i0], Vertices[(int)i2] - Vertices[(int)i0]));
            uint[] triangle = [i0, i1, i2];
            foreach (uint index in triangle)
            {
                flatVertices.Add(Vertices[(int)index]);
                flatNormals.Add(normal);
                flatUvs.Add(TexCoords[(int)index]);
            }
        }

        Vertices.Clear(); Vertices.AddRange(flatVertices);
        Normals.Clear(); Normals.AddRange(flatNormals);
        TexCoords.Clear(); TexCoords.AddRange(flatUvs);
        Indices = null;
    }
}
