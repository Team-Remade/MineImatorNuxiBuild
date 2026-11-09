using System.Numerics;
using GlmSharp;
using MineImatorSimplyRemade.core;

namespace MineImatorSimplyRemade.gizmo;

public readonly record struct SnapHit(vec3 Position, vec3 Normal, SnapElements Element);

/// <summary>A frozen drag-start scene with screen buckets and a triangle BVH.</summary>
public sealed class SnapGeometry
{
    private readonly record struct Triangle(vec3 A, vec3 B, vec3 C)
    {
        public vec3 Normal => vec3.Cross(B - A, C - A).Normalized;
        public vec3 Min => vec3.Min(A, vec3.Min(B, C));
        public vec3 Max => vec3.Max(A, vec3.Max(B, C));
        public vec3 Center => (A + B + C) / 3f;
    }
    private readonly record struct Candidate(SnapHit Hit, Vector2 Screen);
    private readonly record struct Edge(vec3 A, vec3 B);
    private sealed class Node
    {
        public vec3 Min, Max;
        public int Start, Count;
        public Node? Left, Right;
    }

    private const float CellSize = 32f;
    private readonly List<Triangle> _triangles = [];
    private readonly Dictionary<(int, int), List<Candidate>> _buckets = [];
    private readonly HashSet<(vec3, SnapElements)> _points = [];
    private readonly Camera _camera;
    private readonly Vector2 _imageMin, _imageSize;
    private readonly mat4 _viewProjection;
    private Node? _root;
    private bool _built;

    public SnapGeometry(Camera camera, Vector2 imageMin, Vector2 imageSize)
    {
        _camera = new Camera
        {
            Target = camera.Target, Yaw = camera.Yaw, Pitch = camera.Pitch, Roll = camera.Roll,
            Distance = camera.Distance, FovY = camera.FovY, Near = camera.Near, Far = camera.Far
        };
        _imageMin = imageMin;
        _imageSize = imageSize;
        _viewProjection = camera.GetProjectionMatrix(imageSize.X / imageSize.Y) * camera.GetViewMatrix();
    }

    public void AddOrigin(vec3 position) => AddPoint(new SnapHit(position, vec3.Zero, SnapElements.Origin));

    public void AddMesh(IReadOnlyList<vec3> positions, uint[]? indices)
    {
        if (_built) throw new InvalidOperationException("Cannot change a built snap scene.");
        var edges = new Dictionary<Edge, (vec3 Normal, int Count, bool Coplanar)>();
        int count = indices is { Length: > 0 } ? indices.Length : positions.Count;
        for (int i = 0; i + 2 < count; i += 3)
        {
            int ia = indices is { Length: > 0 } ? checked((int)indices[i]) : i;
            int ib = indices is { Length: > 0 } ? checked((int)indices[i + 1]) : i + 1;
            int ic = indices is { Length: > 0 } ? checked((int)indices[i + 2]) : i + 2;
            if ((uint)ia >= positions.Count || (uint)ib >= positions.Count || (uint)ic >= positions.Count)
                throw new ArgumentException("Snap mesh index exceeds vertex count.", nameof(indices));
            vec3 a = positions[ia], b = positions[ib], c = positions[ic];
            if (!Finite(a) || !Finite(b) || !Finite(c)) continue;
            if (vec3.Cross(b - a, c - a).LengthSqr < 1e-16f) continue;
            var triangle = new Triangle(a, b, c);
            _triangles.Add(triangle);
            vec3 normal = triangle.Normal;
            AddPoint(new SnapHit(a, normal, SnapElements.Vertex));
            AddPoint(new SnapHit(b, normal, SnapElements.Vertex));
            AddPoint(new SnapHit(c, normal, SnapElements.Vertex));
            AddEdge(a, b, normal);
            AddEdge(b, c, normal);
            AddEdge(c, a, normal);
        }
        foreach (var (edge, value) in edges)
        {
            // Coplanar shared edges are triangulation diagonals, not modeling edges.
            if (value.Count == 2 && value.Coplanar) continue;
            AddPoint(new SnapHit((edge.A + edge.B) * 0.5f, value.Normal, SnapElements.EdgeMidpoint));
        }

        void AddEdge(vec3 a, vec3 b, vec3 normal)
        {
            bool ordered = a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;
            var key = ordered ? new Edge(a, b) : new Edge(b, a);
            if (edges.TryGetValue(key, out var previous))
                edges[key] = (previous.Normal, previous.Count + 1,
                    previous.Coplanar && vec3.Dot(previous.Normal, normal) > 0.9999f);
            else
                edges[key] = (normal, 1, true);
        }
    }

    public void Build()
    {
        if (_built) return;
        _root = BuildNode(0, _triangles.Count);
        _points.Clear();
        _built = true;
    }

    public SnapHit? Find(Vector2 mouse, SnapElements elements, float radius, bool xray)
    {
        Build();
        SnapHit? best = null;
        float bestPixels = radius * radius, bestDepth = float.PositiveInfinity;
        int x0 = Cell(mouse.X - radius), x1 = Cell(mouse.X + radius);
        int y0 = Cell(mouse.Y - radius), y1 = Cell(mouse.Y + radius);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                if (!_buckets.TryGetValue((x, y), out var candidates)) continue;
                foreach (var candidate in candidates)
                {
                    if ((elements & candidate.Hit.Element) == 0) continue;
                    float pixels = Vector2.DistanceSquared(mouse, candidate.Screen);
                    float depth = (candidate.Hit.Position - _camera.Position).Length;
                    if (pixels > bestPixels || (pixels == bestPixels && depth >= bestDepth)) continue;
                    if (!xray && !Visible(candidate.Hit.Position)) continue;
                    best = candidate.Hit;
                    bestPixels = pixels;
                    bestDepth = depth;
                }
            }
        // Discrete targets take precedence within the radius; faces fill the rest.
        if (best.HasValue || (elements & SnapElements.Face) == 0) return best;
        vec3 direction = _camera.ProjectRayNormal(mouse, _imageMin, _imageSize);
        float near = NearDistance(direction);
        vec3 origin = _camera.Position + direction * near;
        float distance = near * (_camera.Far / _camera.Near - 1f);
        Triangle hit = default;
        return Raycast(_root, origin, direction, ref distance, ref hit)
            ? new SnapHit(origin + direction * distance, hit.Normal, SnapElements.Face) : null;
    }

    private bool Visible(vec3 point)
    {
        vec3 delta = point - _camera.Position;
        float distance = delta.Length;
        if (distance < 1e-7f) return false;
        vec3 direction = delta / distance;
        float near = NearDistance(direction);
        float nearest = MathF.Max(0f, distance - near - MathF.Max(0.0001f, distance * 0.00001f));
        Triangle hit = default;
        return !Raycast(_root, _camera.Position + direction * near, direction, ref nearest, ref hit);
    }

    private float NearDistance(vec3 direction) =>
        _camera.Near / -(_camera.GetViewMatrix() * new vec4(direction, 0f)).z;

    private void AddPoint(SnapHit hit)
    {
        if (_built) throw new InvalidOperationException("Cannot change a built snap scene.");
        if (!Finite(hit.Position) || !_points.Add((hit.Position, hit.Element))) return;
        vec4 clip = _viewProjection * new vec4(hit.Position, 1f);
        if (clip.w <= 0f || clip.z < -clip.w || clip.z > clip.w) return;
        var screen = _imageMin + new Vector2(clip.x / clip.w * 0.5f + 0.5f,
            -clip.y / clip.w * 0.5f + 0.5f) * _imageSize;
        var cell = (Cell(screen.X), Cell(screen.Y));
        if (!_buckets.TryGetValue(cell, out var bucket)) _buckets[cell] = bucket = [];
        bucket.Add(new Candidate(hit, screen));
    }

    private Node? BuildNode(int start, int count)
    {
        if (count == 0) return null;
        var node = new Node { Start = start, Count = count, Min = new(float.PositiveInfinity), Max = new(float.NegativeInfinity) };
        for (int i = start; i < start + count; i++)
        {
            node.Min = vec3.Min(node.Min, _triangles[i].Min);
            node.Max = vec3.Max(node.Max, _triangles[i].Max);
        }
        if (count <= 8) return node;
        vec3 size = node.Max - node.Min;
        int axis = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
        _triangles.Sort(start, count, Comparer<Triangle>.Create((a, b) => a.Center[axis].CompareTo(b.Center[axis])));
        node.Left = BuildNode(start, count / 2);
        node.Right = BuildNode(start + count / 2, count - count / 2);
        return node;
    }

    private bool Raycast(Node? node, vec3 origin, vec3 direction, ref float nearest, ref Triangle hit)
    {
        if (node == null || !IntersectsBox(node.Min, node.Max, origin, direction, nearest)) return false;
        if (node.Left != null)
        {
            bool left = Raycast(node.Left, origin, direction, ref nearest, ref hit);
            bool right = Raycast(node.Right, origin, direction, ref nearest, ref hit);
            return left || right;
        }
        bool found = false;
        for (int i = node.Start; i < node.Start + node.Count; i++)
        {
            var triangle = _triangles[i];
            vec3 e1 = triangle.B - triangle.A, e2 = triangle.C - triangle.A;
            vec3 p = vec3.Cross(direction, e2);
            float determinant = vec3.Dot(e1, p);
            if (MathF.Abs(determinant) < 1e-10f) continue;
            vec3 t = origin - triangle.A;
            float u = vec3.Dot(t, p) / determinant;
            if (u < -1e-6f || u > 1f + 1e-6f) continue;
            vec3 q = vec3.Cross(t, e1);
            float v = vec3.Dot(direction, q) / determinant;
            if (v < -1e-6f || u + v > 1f + 1e-6f) continue;
            float distance = vec3.Dot(e2, q) / determinant;
            if (distance < 0f || distance >= nearest) continue;
            nearest = distance;
            hit = triangle;
            found = true;
        }
        return found;
    }

    private static bool IntersectsBox(vec3 min, vec3 max, vec3 origin, vec3 direction, float far)
    {
        float near = 0f;
        for (int axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(direction[axis]) < 1e-10f)
            {
                if (origin[axis] < min[axis] || origin[axis] > max[axis]) return false;
                continue;
            }
            float a = (min[axis] - origin[axis]) / direction[axis];
            float b = (max[axis] - origin[axis]) / direction[axis];
            near = MathF.Max(near, MathF.Min(a, b));
            far = MathF.Min(far, MathF.Max(a, b));
            if (near > far) return false;
        }
        return true;
    }

    private static int Cell(float value) => (int)MathF.Floor(value / CellSize);
    private static bool Finite(vec3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
}
