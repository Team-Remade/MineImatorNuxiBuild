using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public abstract class RadialMesh : Mesh
{
    public int Segments { get; private set; }
    public bool SmoothShading { get; private set; }
    public bool Closed { get; private set; }
    public bool Mapped { get; private set; }
    private bvec2 _mappedMirrors;
    protected abstract bool IsCone { get; }

    protected RadialMesh(GL gl, int segments, bool smoothShading, bool closed, bool mapped) : base(gl)
    {
        Segments = Math.Clamp(segments, 3, 256);
        SmoothShading = smoothShading;
        Closed = closed;
        Mapped = mapped;
        GenerateVertices();
        Upload();
    }

    protected RadialMesh(GL gl, RadialMesh source) : base(gl)
    {
        Segments = source.Segments;
        SmoothShading = source.SmoothShading;
        Closed = source.Closed;
        Mapped = source.Mapped;
        _mappedMirrors = source._mappedMirrors;
    }

    public void SetGeometry(int segments, bool smoothShading, bool closed)
    {
        segments = Math.Clamp(segments, 3, 256);
        if (Segments == segments && SmoothShading == smoothShading && Closed == closed)
            return;
        Segments = segments;
        SmoothShading = smoothShading;
        Closed = closed;
        Rebuild();
    }

    public void SetMapped(bool mapped)
    {
        if (Mapped == mapped) return;
        Mapped = mapped;
        Rebuild();
    }

    public void SetMappedMirrors(bvec2 mirrors)
    {
        if (_mappedMirrors.x == mirrors.x && _mappedMirrors.y == mirrors.y)
            return;

        _mappedMirrors = mirrors;
        if (Mapped) Rebuild();
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

    protected sealed override void GenerateVertices()
    {
        // Mine-imator uses radius 8 and Z-up. Convert to unit-sized Y-up meshes
        // with (x, y, z) -> (x, z, -y), and flip texture V for OpenGL.
        float sideWidth = Mapped ? 1f / (IsCone ? 2f : 3f) : 1f;
        float sideOffset = Mapped ? 0f : 0.25f;
        float mirrorU = Mapped && _mappedMirrors.x ? -1f : 1f;
        float mirrorV = Mapped && _mappedMirrors.y ? -1f : 1f;
        float bottomV = mirrorV < 0f ? 1f : 0f;
        float topV = 1f - bottomV;
        for (int segment = 0; segment < Segments; segment++)
        {
            float a = (float)segment / Segments;
            float b = (float)(segment + 1) / Segments;
            var normalA = new vec3(MathF.Cos(a * MathF.Tau), 0f, MathF.Sin(a * MathF.Tau));
            var normalB = segment + 1 == Segments
                ? new vec3(1f, 0f, 0f)
                : new vec3(MathF.Cos(b * MathF.Tau), 0f, MathF.Sin(b * MathF.Tau));
            vec3 bottomA = normalA * 0.5f + new vec3(0f, -0.5f, 0f);
            vec3 bottomB = normalB * 0.5f + new vec3(0f, -0.5f, 0f);
            vec3 topA = normalA * 0.5f + new vec3(0f, 0.5f, 0f);
            vec3 topB = normalB * 0.5f + new vec3(0f, 0.5f, 0f);

            if (Closed)
            {
                float bottomCenterU = Mapped ? (IsCone ? 0.75f : 5f / 6f) : 0.75f;
                var bottomCenterUv = new vec2(bottomCenterU, 0.5f);
                // The source's mirrored cap center V=-0.5 wraps to the same 0.5.
                var bottomUvA = bottomCenterUv + new vec2(normalA.x * sideWidth * 0.5f * mirrorU, -normalA.z * 0.5f * mirrorV);
                var bottomUvB = bottomCenterUv + new vec2(normalB.x * sideWidth * 0.5f * mirrorU, -normalB.z * 0.5f * mirrorV);
                vec3 down = new(0f, -1f, 0f);
                AddVertex(new vec3(0f, -0.5f, 0f), down, bottomCenterUv);
                AddVertex(bottomA, down, bottomUvA);
                AddVertex(bottomB, down, bottomUvB);

                if (!IsCone)
                {
                    float topCenterU = Mapped ? 0.5f : 0.75f;
                    var topCenterUv = new vec2(topCenterU, 0.5f);
                    var topUvA = topCenterUv + new vec2(normalA.x * sideWidth * 0.5f * mirrorU, normalA.z * 0.5f * mirrorV);
                    var topUvB = topCenterUv + new vec2(normalB.x * sideWidth * 0.5f * mirrorU, normalB.z * 0.5f * mirrorV);
                    vec3 up = new(0f, 1f, 0f);
                    AddVertex(new vec3(0f, 0.5f, 0f), up, topCenterUv);
                    AddVertex(topB, up, topUvB);
                    AddVertex(topA, up, topUvA);
                }
            }

            float uA = sideOffset + (mirrorU < 0f ? 1f - a : a) * sideWidth;
            float uB = sideOffset + (mirrorU < 0f ? 1f - b : b) * sideWidth;
            if (IsCone)
            {
                // Preserve Mine-imator's shading: radial base normals and an
                // upward tip when smooth; one averaged normal per side otherwise.
                vec3 sideNormal = new vec3((normalA.x + normalB.x) * 0.5f, 1f,
                    (normalA.z + normalB.z) * 0.5f).Normalized;
                AddVertex(new vec3(0f, 0.5f, 0f), SmoothShading ? new vec3(0f, 1f, 0f) : sideNormal, new vec2(uB, topV));
                AddVertex(bottomB, SmoothShading ? normalB : sideNormal, new vec2(uB, bottomV));
                AddVertex(bottomA, SmoothShading ? normalA : sideNormal, new vec2(uA, bottomV));
            }
            else
            {
                vec3 sideNormal = (normalA + normalB).Normalized;
                vec3 nA = SmoothShading ? normalA : sideNormal;
                vec3 nB = SmoothShading ? normalB : sideNormal;
                AddVertex(bottomA, nA, new vec2(uA, bottomV));
                AddVertex(topA, nA, new vec2(uA, topV));
                AddVertex(topB, nB, new vec2(uB, topV));
                AddVertex(topB, nB, new vec2(uB, topV));
                AddVertex(bottomB, nB, new vec2(uB, bottomV));
                AddVertex(bottomA, nA, new vec2(uA, bottomV));
            }
        }
    }

    private void AddVertex(vec3 position, vec3 normal, vec2 uv)
    {
        Vertices.Add(position);
        Normals.Add(normal);
        TexCoords.Add(uv);
    }
}
