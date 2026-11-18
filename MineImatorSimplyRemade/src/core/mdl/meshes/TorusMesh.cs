using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public sealed class TorusMesh : Mesh
{
    public float Radius { get; }
    public int Detail { get; private set; }
    public bool SmoothShading { get; private set; }
    public float MajorRadius { get; private set; }
    public float MinorRadius { get; private set; }

    public TorusMesh(GL gl, float radius = 0.5f, int detail = 32, bool smoothShading = true,
        float majorRadius = 1f, float minorRadius = 0.4f) : base(gl)
    {
        if (radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), "The radius must be positive.");
        Radius = radius;
        Detail = NormalizeDetail(detail);
        SmoothShading = smoothShading;
        MajorRadius = Math.Clamp(majorRadius, 0f, 5f);
        MinorRadius = Math.Clamp(minorRadius, 0f, 5f);
        GenerateVertices();
        Upload();
    }

    private TorusMesh(GL gl, TorusMesh source) : base(gl)
    {
        Radius = source.Radius;
        Detail = source.Detail;
        SmoothShading = source.SmoothShading;
        MajorRadius = source.MajorRadius;
        MinorRadius = source.MinorRadius;
    }

    protected override Mesh CreateCloneGeometry(GL gl) => new TorusMesh(gl, this);

    public void SetGeometry(int detail, bool smoothShading, float majorRadius, float minorRadius)
    {
        detail = NormalizeDetail(detail);
        majorRadius = Math.Clamp(majorRadius, 0f, 5f);
        minorRadius = Math.Clamp(minorRadius, 0f, 5f);
        if (Detail == detail && SmoothShading == smoothShading &&
            MathF.Abs(MajorRadius - majorRadius) < 0.000001f &&
            MathF.Abs(MinorRadius - minorRadius) < 0.000001f)
            return;
        Detail = detail;
        SmoothShading = smoothShading;
        MajorRadius = majorRadius;
        MinorRadius = minorRadius;
        Rebuild();
    }

    private static int NormalizeDetail(int detail)
    {
        detail = Math.Clamp(detail, 3, 256);
        return detail > 8 ? detail / 2 * 2 : detail;
    }

    private void Rebuild()
    {
        Vertices.Clear();
        Normals.Clear();
        TexCoords.Clear();
        Indices = null;
        GenerateVertices();
        Upload();
    }

    protected override void GenerateVertices()
    {
        int majorSegments = Detail;
        int minorSegments = Math.Max(Detail / 2, 4);
        float major = Radius * MajorRadius;
        float minor = Radius * MinorRadius;
        for (int i = 0; i < majorSegments; i++)
        {
            float a0 = MathF.Tau * i / majorSegments;
            float a1 = MathF.Tau * (i + 1) / majorSegments;
            for (int j = 0; j < minorSegments; j++)
            {
                float b0 = MathF.Tau * j / minorSegments;
                float b1 = MathF.Tau * (j + 1) / minorSegments;
                (vec3 Position, vec3 Normal, vec2 Uv) p1 = Point(a0, b0, i / (float)majorSegments, j / (float)minorSegments);
                (vec3 Position, vec3 Normal, vec2 Uv) p2 = Point(a1, b0, (i + 1f) / majorSegments, j / (float)minorSegments);
                (vec3 Position, vec3 Normal, vec2 Uv) p3 = Point(a0, b1, i / (float)majorSegments, (j + 1f) / minorSegments);
                (vec3 Position, vec3 Normal, vec2 Uv) p4 = Point(a1, b1, (i + 1f) / majorSegments, (j + 1f) / minorSegments);
                vec3 flat = SmoothShading ? vec3.Zero :
                    (p1.Normal + p2.Normal + p3.Normal + p4.Normal).Normalized;
                Add(p1, flat);
                Add(p2, flat);
                Add(p3, flat);
                Add(p2, flat);
                Add(p4, flat);
                Add(p3, flat);
            }
        }

        (vec3 Position, vec3 Normal, vec2 Uv) Point(float a, float b, float u, float v)
        {
            float cosA = MathF.Cos(a), sinA = MathF.Sin(a);
            float cosB = MathF.Cos(b), sinB = MathF.Sin(b);
            var sourcePosition = new vec3((major + minor * cosB) * cosA,
                (major + minor * cosB) * sinA, minor * sinB);
            var sourceNormal = new vec3(cosB * cosA, cosB * sinA, sinB);
            return (new vec3(sourcePosition.x, sourcePosition.z, -sourcePosition.y),
                new vec3(sourceNormal.x, sourceNormal.z, -sourceNormal.y), new vec2(u, v));
        }

        void Add((vec3 Position, vec3 Normal, vec2 Uv) point, vec3 flat)
        {
            Vertices.Add(point.Position);
            Normals.Add(SmoothShading ? point.Normal : flat);
            TexCoords.Add(point.Uv);
        }
    }
}
