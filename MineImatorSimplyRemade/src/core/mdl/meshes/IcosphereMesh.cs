using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public sealed class IcosphereMesh : Mesh
{
    public float Radius { get; }
    public int Subdivision { get; private set; }
    public bool SmoothShading { get; private set; }
    public float Morph { get; private set; }

    public IcosphereMesh(GL gl, float radius = 0.5f, int subdivision = 0,
        bool smoothShading = true, float morph = 0f) : base(gl)
    {
        if (radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), "The radius must be positive.");
        Radius = radius;
        Subdivision = Math.Clamp(subdivision, 0, 5);
        SmoothShading = smoothShading;
        Morph = Math.Clamp(morph, -2f, 5f);
        GenerateVertices();
        Upload();
    }

    private IcosphereMesh(GL gl, IcosphereMesh source) : base(gl)
    {
        Radius = source.Radius;
        Subdivision = source.Subdivision;
        SmoothShading = source.SmoothShading;
        Morph = source.Morph;
    }

    protected override Mesh CreateCloneGeometry(GL gl) => new IcosphereMesh(gl, this);

    public void SetGeometry(int subdivision, bool smoothShading, float morph)
    {
        subdivision = Math.Clamp(subdivision, 0, 5);
        morph = Math.Clamp(morph, -2f, 5f);
        if (Subdivision == subdivision && SmoothShading == smoothShading &&
            MathF.Abs(Morph - morph) < 0.000001f)
            return;
        Subdivision = subdivision;
        SmoothShading = smoothShading;
        Morph = morph;
        Rebuild();
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
        float t = (1f + MathF.Sqrt(5f)) /
            (2f + Morph * (Morph > 0f ? Morph : 1f));
        t = Math.Clamp(t, 0.001f, 10000f);
        var positions = new List<vec3>
        {
            Unit(-1f, t, 0f), Unit(1f, t, 0f), Unit(-1f, -t, 0f), Unit(1f, -t, 0f),
            Unit(0f, -1f, t), Unit(0f, 1f, t), Unit(0f, -1f, -t), Unit(0f, 1f, -t),
            Unit(t, 0f, -1f), Unit(t, 0f, 1f), Unit(-t, 0f, -1f), Unit(-t, 0f, 1f)
        };
        var faces = new List<(int A, int B, int C)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
            (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
            (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)
        };
        for (int level = 0; level < Subdivision; level++)
        {
            var midpoint = new Dictionary<(int, int), int>();
            var divided = new List<(int A, int B, int C)>(faces.Count * 4);
            foreach (var face in faces)
            {
                int ab = Midpoint(face.A, face.B);
                int bc = Midpoint(face.B, face.C);
                int ca = Midpoint(face.C, face.A);
                divided.Add((face.A, ab, ca));
                divided.Add((face.B, bc, ab));
                divided.Add((face.C, ca, bc));
                divided.Add((ab, bc, ca));
            }
            faces = divided;
            continue;

            int Midpoint(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (midpoint.TryGetValue(key, out int index)) return index;
                index = positions.Count;
                positions.Add(((positions[a] + positions[b]) * 0.5f).Normalized);
                midpoint.Add(key, index);
                return index;
            }
        }

        foreach (var face in faces)
        {
            vec3 a = positions[face.A], b = positions[face.B], c = positions[face.C];
            vec3 flat = vec3.Cross(b - a, c - a).Normalized;
            Add(a, SmoothShading ? a : flat);
            Add(b, SmoothShading ? b : flat);
            Add(c, SmoothShading ? c : flat);
        }
    }

    private void Add(vec3 unit, vec3 normal)
    {
        vec3 appUnit = new(unit.x, unit.z, -unit.y);
        vec3 appNormal = new(normal.x, normal.z, -normal.y);
        Vertices.Add(appUnit * Radius);
        Normals.Add(appNormal);
        TexCoords.Add(new vec2(
            0.5f + MathF.Atan2(unit.x, unit.z) / MathF.Tau,
            0.5f - MathF.Asin(Math.Clamp(unit.y, -1f, 1f)) / MathF.PI));
    }

    private static vec3 Unit(float x, float y, float z) => new vec3(x, y, z).Normalized;
}
