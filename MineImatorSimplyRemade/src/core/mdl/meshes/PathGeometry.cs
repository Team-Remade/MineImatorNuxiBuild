using GlmSharp;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public enum PathShape { None, Flat, Tube }

public sealed record PathSettings
{
    public bool Closed { get; init; } = true;
    public bool Smooth { get; init; } = true;
    public int Detail { get; init; } = 6;
    public PathShape Shape { get; init; }
    public float Radius { get; init; } = 0.5f;
    public int Sides { get; init; } = 12;
    public bool SmoothSegments { get; init; } = true;
    public bool SmoothRing { get; init; } = true;
    public bool Invert { get; init; }
    public bool MappedTexture { get; init; }
    public float TextureLength { get; init; } = 1f;

    public PathSettings Validated()
    {
        if (!Enum.IsDefined(Shape) || !float.IsFinite(Radius) || Radius <= 0f ||
            !float.IsFinite(TextureLength) || TextureLength <= 0f)
            throw new InvalidDataException("Path radius and texture length must be positive finite numbers, and shape must be valid.");
        return this with { Detail = Math.Clamp(Detail, 1, 64), Sides = Math.Clamp(Sides, 3, 32) };
    }
}

public readonly record struct PathControlPoint(vec3 Position, float Angle, float Scale);
public readonly record struct PathSample(vec3 Position, vec3 Tangent, vec3 Normal, float Scale, float Distance)
{
    public mat4 Frame
    {
        get
        {
            vec3 side = vec3.Cross(Tangent, Normal);
            var frame = mat4.Identity;
            frame.m00 = side.x; frame.m01 = side.y; frame.m02 = side.z;
            frame.m10 = Tangent.x; frame.m11 = Tangent.y; frame.m12 = Tangent.z;
            frame.m20 = Normal.x; frame.m21 = Normal.y; frame.m22 = Normal.z;
            frame.m30 = Position.x; frame.m31 = Position.y; frame.m32 = Position.z;
            return frame;
        }
    }
}

public static class PathGeometry
{
    public const int MaxSegments = 2048;

    public static PathSample[] Sample(IReadOnlyList<PathControlPoint> points, PathSettings settings)
    {
        var s = settings.Validated();
        if (points.Count < 2) return [];
        foreach (var point in points)
            if (!Finite(point.Position) || !float.IsFinite(point.Angle) || !float.IsFinite(point.Scale))
                throw new InvalidDataException("Path control points must contain finite values.");

        int spans = s.Closed ? points.Count : points.Count - 1;
        int segments = Math.Min(MaxSegments, spans * (s.Detail + 1));
        // A denser arc-length table prevents uneven control-point spacing from changing follower speed.
        int subdivisions = Math.Min(MaxSegments * 4, spans * Math.Max(16, s.Detail * 2));
        var positions = new vec3[subdivisions + 1];
        var angles = new float[positions.Length];
        var scales = new float[positions.Length];
        var distances = new float[positions.Length];
        for (int i = 0; i <= subdivisions; i++)
        {
            float t = (float)i / subdivisions * spans;
            int span = Math.Min((int)t, spans - 1);
            float u = t - span;
            var a = Point(span);
            var b = Point(span + 1);
            if (s.Smooth)
            {
                vec3 before = Point(span - 1).Position, after = Point(span + 2).Position;
                positions[i] = 0.5f * (2f * a.Position + (b.Position - before) * u +
                    (2f * before - 5f * a.Position + 4f * b.Position - after) * u * u +
                    (-before + 3f * a.Position - 3f * b.Position + after) * u * u * u);
            }
            else positions[i] = a.Position * (1f - u) + b.Position * u;
            angles[i] = a.Angle * (1f - u) + b.Angle * u;
            scales[i] = Math.Max(0f, a.Scale * (1f - u) + b.Scale * u);
            if (i > 0) distances[i] = distances[i - 1] + (positions[i] - positions[i - 1]).Length;
            if (!Finite(positions[i]) || !float.IsFinite(distances[i]))
                throw new InvalidDataException("Path coordinates exceed the supported numeric range.");
        }
        if (distances[^1] <= 1e-6f) return [];

        var sampled = new vec3[segments + 1];
        var rolls = new float[sampled.Length];
        var radii = new float[sampled.Length];
        int cursor = 1;
        for (int i = 0; i <= segments; i++)
        {
            float distance = distances[^1] * i / segments;
            while (cursor < subdivisions && distances[cursor] < distance) cursor++;
            float delta = distances[cursor] - distances[cursor - 1];
            float blend = delta > 1e-8f ? (distance - distances[cursor - 1]) / delta : 0f;
            sampled[i] = positions[cursor - 1] * (1f - blend) + positions[cursor] * blend;
            rolls[i] = (angles[cursor - 1] * (1f - blend) + angles[cursor] * blend) * (MathF.PI / 180f);
            radii[i] = scales[cursor - 1] * (1f - blend) + scales[cursor] * blend;
        }
        var tangents = new vec3[sampled.Length];
        var normals = new vec3[sampled.Length];
        for (int i = 0; i <= segments; i++)
        {
            vec3 before = sampled[i == 0 ? (s.Closed ? segments - 1 : 0) : i - 1];
            vec3 after = sampled[i == segments ? (s.Closed ? 1 : segments) : i + 1];
            tangents[i] = Normalize(after - before, i == 0 ? vec3.UnitY : tangents[i - 1]);
            normals[i] = i == 0 ? Perpendicular(tangents[i]) :
                Normalize(normals[i - 1] - tangents[i] * vec3.Dot(normals[i - 1], tangents[i]), Perpendicular(tangents[i]));
        }
        float twist = s.Closed ? MathF.Atan2(vec3.Dot(vec3.Cross(normals[^1], normals[0]), tangents[0]),
            vec3.Dot(normals[^1], normals[0])) : 0f;
        var result = new PathSample[sampled.Length];
        for (int i = 0; i <= segments; i++)
        {
            float angle = rolls[i] + twist * i / segments;
            vec3 normal = normals[i] * MathF.Cos(angle) + vec3.Cross(tangents[i], normals[i]) * MathF.Sin(angle);
            result[i] = new(sampled[i], tangents[i], normal, radii[i], distances[^1] * i / segments);
        }
        if (s.Closed) result[^1] = result[0] with { Distance = distances[^1] };
        return result;

        PathControlPoint Point(int index) => points[s.Closed
            ? (index % points.Count + points.Count) % points.Count : Math.Clamp(index, 0, points.Count - 1)];
    }

    public static PathSample AtDistance(IReadOnlyList<PathSample> samples, float distance, bool closed)
    {
        if (samples.Count < 2 || !float.IsFinite(distance))
            throw new InvalidDataException("A path follower requires a non-empty path and a finite offset.");
        float length = samples[^1].Distance;
        distance = closed ? (distance % length + length) % length : Math.Clamp(distance, 0f, length);
        float index = distance / length * (samples.Count - 1);
        int lower = Math.Min((int)index, samples.Count - 2);
        float t = index - lower;
        var a = samples[lower]; var b = samples[lower + 1];
        vec3 tangent = Normalize(a.Tangent * (1f - t) + b.Tangent * t, a.Tangent);
        vec3 normal = a.Normal * (1f - t) + b.Normal * t;
        normal = Normalize(normal - tangent * vec3.Dot(normal, tangent), Perpendicular(tangent));
        return new(a.Position * (1f - t) + b.Position * t, tangent, normal,
            a.Scale * (1f - t) + b.Scale * t, distance);
    }

    public static void Populate(Mesh mesh, IReadOnlyList<PathSample> samples, PathSettings settings, bool pickOnly = false)
    {
        var s = settings.Validated();
        mesh.Vertices.Clear(); mesh.Normals.Clear(); mesh.TexCoords.Clear(); mesh.Indices = null;
        mesh.PickOnly = pickOnly;
        if (samples.Count < 2 || (s.Shape == PathShape.None && !pickOnly)) return;
        bool flat = s.Shape == PathShape.Flat && !pickOnly;
        int sides = flat ? 2 : pickOnly ? 6 : s.Sides;
        float radius = pickOnly ? 0.04f : s.Radius;
        float length = samples[^1].Distance;
        int segment = 0, strip = 0;
        for (int i = 0; i < samples.Count - 1; i++)
        for (int j = 0; j < (flat ? 1 : sides); j++)
        {
            segment = i; strip = j;
            int next = (j + 1) % sides;
            vec3 a = Position(i, j), b = Position(i, next), c = Position(i + 1, j), d = Position(i + 1, next);
            Triangle(a, c, b, i, j, i + 1, j, i, j + 1);
            Triangle(b, c, d, i, j + 1, i + 1, j, i + 1, j + 1);
        }

        vec3 Radial(int ring, float side)
        {
            var sample = samples[ring];
            vec3 across = vec3.Cross(sample.Tangent, sample.Normal);
            float angle = flat ? MathF.PI * side : MathF.Tau * side / sides;
            return across * MathF.Cos(angle) + sample.Normal * MathF.Sin(angle);
        }
        vec3 Position(int ring, int side) => samples[ring].Position + Radial(ring, side) * radius *
            (pickOnly ? 1f : samples[ring].Scale);
        void Triangle(vec3 a, vec3 b, vec3 c, int ar, int av, int br, int bv, int cr, int cv)
        {
            vec3 face = Normalize(vec3.Cross(b - a, c - a), samples[ar].Normal);
            if (s.Invert && !pickOnly) { (b, c) = (c, b); (br, cr) = (cr, br); (bv, cv) = (cv, bv); face = -face; }
            Vertex(a, ar, av, face); Vertex(b, br, bv, face); Vertex(c, cr, cv, face);
        }
        void Vertex(vec3 position, int ring, int side, vec3 face)
        {
            vec3 normal = face;
            if (flat ? s.SmoothSegments : s.SmoothSegments || s.SmoothRing)
            {
                normal = flat ? samples[ring].Normal
                    : s.SmoothSegments ? Radial(ring, s.SmoothRing ? side % sides : strip + 0.5f)
                    : Normalize(Radial(segment, side % sides) + Radial(segment + 1, side % sides), face);
                if (s.Invert && !pickOnly) normal = -normal;
            }
            mesh.Vertices.Add(position); mesh.Normals.Add(normal);
            mesh.TexCoords.Add(new((float)side / (flat ? 1 : sides),
                samples[ring].Distance / (s.MappedTexture ? length : s.TextureLength)));
        }
    }

    private static bool Finite(vec3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    private static vec3 Normalize(vec3 v, vec3 fallback) => v.LengthSqr > 1e-12f ? v.Normalized : fallback;
    private static vec3 Perpendicular(vec3 tangent) => Normalize(
        vec3.Cross(MathF.Abs(tangent.z) < 0.9f ? vec3.UnitZ : vec3.UnitX, tangent), vec3.UnitX);
}
