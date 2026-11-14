using GlmSharp;
using MineImatorSimplyRemade.core.diagnostics;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>Refittable BVH for cloth contact candidates, including swept vertex paths.</summary>
internal sealed class ClothCollisionTree
{
    private record struct Entry(vec3 Min, vec3 Max, int A, int B, int C, ClothTriangle Triangle, ClothTriangle Previous = default);
    private sealed class Node(vec3 min, vec3 max, int start, int count, Node? left, Node? right)
    {
        public vec3 Min = min, Max = max;
        public readonly int Start = start, Count = count;
        public readonly Node? Left = left, Right = right;
    }
    private readonly Entry[] _entries;
    private readonly Node? _root;
    private readonly List<Entry> _candidates = [];
    public int EntryCount => _entries.Length;
    private ClothCollisionTree(Entry[] entries)
    {
        _entries = entries;
        _root = entries.Length == 0 ? null : Build(0, entries.Length);
    }
    private Node Build(int start, int count)
    {
        vec3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = start; i < start + count; i++)
        { min = vec3.Min(min, _entries[i].Min); max = vec3.Max(max, _entries[i].Max); }
        if (count <= 8) return new(min, max, start, count, null, null);
        vec3 extent = max - min;
        int axis = extent.x > extent.y ? (extent.x > extent.z ? 0 : 2) : (extent.y > extent.z ? 1 : 2);
        Array.Sort(_entries, start, count, Comparer<Entry>.Create((a, b) =>
            ((a.Min[axis] + a.Max[axis]) * 0.5f).CompareTo((b.Min[axis] + b.Max[axis]) * 0.5f)));
        int half = count / 2;
        return new(min, max, start, count, Build(start, half), Build(start + half, count - half));
    }
    private static bool Overlaps(vec3 min, vec3 max, vec3 otherMin, vec3 otherMax) =>
        min.x <= otherMax.x && min.y <= otherMax.y && min.z <= otherMax.z &&
        max.x >= otherMin.x && max.y >= otherMin.y && max.z >= otherMin.z;

    private List<Entry> Query(vec3 min, vec3 max)
    {
        _candidates.Clear();
        int visited = 0;
        Collect(_root);
        FrameProfiler.Shared.Count(ProfileCounter.ClothBvhNodesVisited, visited);
        return _candidates;

        void Collect(Node? node)
        {
            visited++;
            if (node == null || !Overlaps(min, max, node.Min, node.Max)) return;
            if (node.Left == null)
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    var entry = _entries[i];
                    if (Overlaps(min, max, entry.Min, entry.Max)) _candidates.Add(entry);
                }
            else { Collect(node.Left); Collect(node.Right); }
        }
    }

    public void RefitCloth(vec3[] p, vec3[] old)
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            ref var entry = ref _entries[i];
            entry.Min = vec3.Min(vec3.Min(p[entry.A], vec3.Min(p[entry.B], p[entry.C])),
                vec3.Min(old[entry.A], vec3.Min(old[entry.B], old[entry.C])));
            entry.Max = vec3.Max(vec3.Max(p[entry.A], vec3.Max(p[entry.B], p[entry.C])),
                vec3.Max(old[entry.A], vec3.Max(old[entry.B], old[entry.C])));
        }
        Refit(_root);
    }

    public void RefitEdges(vec3[] p)
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            ref var entry = ref _entries[i];
            entry.Min = vec3.Min(p[entry.A], p[entry.B]);
            entry.Max = vec3.Max(p[entry.A], p[entry.B]);
        }
        Refit(_root);
    }

    public void RefitScene(IReadOnlyList<ClothTriangle> triangles, IReadOnlyList<ClothTriangle>? previous,
        float start, float end)
    {
        if (triangles.Count != _entries.Length)
            throw new ArgumentException("Scene collider count does not match this collision tree.", nameof(triangles));
        bool interpolate = previous?.Count == triangles.Count;
        for (int i = 0; i < _entries.Length; i++)
        {
            ref var entry = ref _entries[i];
            // Build sorts entries spatially; keep each leaf's original source triangle.
            ClothTriangle current = triangles[entry.C];
            ClothTriangle prior = interpolate ? previous![entry.C] : current;
            entry.Previous = Interpolate(prior, current, start);
            entry.Triangle = Interpolate(prior, current, end);
            entry.Min = vec3.Min(vec3.Min(entry.Triangle.A, vec3.Min(entry.Triangle.B, entry.Triangle.C)),
                vec3.Min(entry.Previous.A, vec3.Min(entry.Previous.B, entry.Previous.C)));
            entry.Max = vec3.Max(vec3.Max(entry.Triangle.A, vec3.Max(entry.Triangle.B, entry.Triangle.C)),
                vec3.Max(entry.Previous.A, vec3.Max(entry.Previous.B, entry.Previous.C)));
        }
        Refit(_root);
    }

    public bool MatchesStationaryScene(IReadOnlyList<ClothTriangle> triangles)
    {
        if (triangles.Count != _entries.Length) return false;
        foreach (var entry in _entries)
            if (entry.Triangle != triangles[entry.C] || entry.Previous != entry.Triangle) return false;
        return true;
    }

    private static ClothTriangle Interpolate(ClothTriangle from, ClothTriangle to, float t) => new(
        vec3.Lerp(from.A, to.A, t), vec3.Lerp(from.B, to.B, t), vec3.Lerp(from.C, to.C, t));

    private void Refit(Node? node)
    {
        if (node == null) return;
        if (node.Left is { } left && node.Right is { } right)
        {
            Refit(left); Refit(right);
            node.Min = vec3.Min(left.Min, right.Min);
            node.Max = vec3.Max(left.Max, right.Max);
        }
        else
        {
            node.Min = new(float.MaxValue); node.Max = new(float.MinValue);
            for (int i = node.Start; i < node.Start + node.Count; i++)
            {
                node.Min = vec3.Min(node.Min, _entries[i].Min);
                node.Max = vec3.Max(node.Max, _entries[i].Max);
            }
        }
    }
    public static ClothCollisionTree ForScene(IReadOnlyList<ClothTriangle> triangles, IReadOnlyList<ClothTriangle> previous)
    {
        var prior = previous.Count == triangles.Count ? previous : triangles;
        return new(triangles.Select((t, i) => new Entry(
            vec3.Min(vec3.Min(t.A, vec3.Min(t.B, t.C)), vec3.Min(prior[i].A, vec3.Min(prior[i].B, prior[i].C))),
            vec3.Max(vec3.Max(t.A, vec3.Max(t.B, t.C)), vec3.Max(prior[i].A, vec3.Max(prior[i].B, prior[i].C))),
            -1, -1, i, t, prior[i])).ToArray());
    }
    public static ClothCollisionTree ForCloth(List<(int A, int B, int C)> triangles, vec3[] p, vec3[] old) => new(
        triangles.Select(t => new Entry(
            vec3.Min(vec3.Min(p[t.A], vec3.Min(p[t.B], p[t.C])), vec3.Min(old[t.A], vec3.Min(old[t.B], old[t.C]))),
            vec3.Max(vec3.Max(p[t.A], vec3.Max(p[t.B], p[t.C])), vec3.Max(old[t.A], vec3.Max(old[t.B], old[t.C]))),
            t.A, t.B, t.C, default)).ToArray());

    public void CollidePoint(int point, vec3[] p, vec3[] old, float[] mass, float radius, HashSet<(int, int)>? neighbors)
    {
        vec3 pad = new(radius);
        foreach (var entry in Query(vec3.Min(p[point], old[point]) - pad, vec3.Max(p[point], old[point]) + pad))
        {
            if (entry.A >= 0 && AdjacentPoint(point, entry, neighbors!)) continue;
            ResolvePoint(point, entry, p, old, mass, radius);
        }
    }

    private static bool AdjacentPoint(int point, Entry entry, HashSet<(int, int)> neighbors) =>
        point == entry.A || point == entry.B || point == entry.C ||
                neighbors!.Contains(ClothSimulation.Pair(point, entry.A)) ||
                neighbors.Contains(ClothSimulation.Pair(point, entry.B)) ||
                neighbors.Contains(ClothSimulation.Pair(point, entry.C));

    public void CollectPointContacts(int point, vec3[] p, vec3[] old, float radius, HashSet<(int, int)> neighbors,
        List<(int Point, int A, int B, int C)> contacts)
    {
        vec3 pad = new(radius);
        foreach (var entry in Query(vec3.Min(p[point], old[point]) - pad, vec3.Max(p[point], old[point]) + pad))
        {
            if (AdjacentPoint(point, entry, neighbors)) continue;
            vec3 closest = ClosestTriangle(p[point], p[entry.A], p[entry.B], p[entry.C], out _);
            float motion = (p[point] - old[point]).Length + MathF.Max((p[entry.A] - old[entry.A]).Length,
                MathF.Max((p[entry.B] - old[entry.B]).Length, (p[entry.C] - old[entry.C]).Length));
            if ((p[point] - closest).Length <= radius + motion)
                contacts.Add((point, entry.A, entry.B, entry.C));
        }
    }

    public static void CollideClothPoint((int Point, int A, int B, int C) contact, vec3[] p, vec3[] old,
        float[] mass, float radius) =>
        ResolvePoint(contact.Point, new(default, default, contact.A, contact.B, contact.C, default), p, old, mass, radius);

    private static void ResolvePoint(int point, Entry entry, vec3[] p, vec3[] old, float[] mass, float radius)
    {
        bool cloth = entry.A >= 0;
        vec3 a = cloth ? p[entry.A] : entry.Triangle.A;
        vec3 b = cloth ? p[entry.B] : entry.Triangle.B;
        vec3 c = cloth ? p[entry.C] : entry.Triangle.C;
        vec3 normal = vec3.Cross(b - a, c - a);
        if (normal.LengthSqr < 1e-16f) return;
        normal = normal.Normalized;
        vec3 closest = ClosestTriangle(p[point], a, b, c, out vec3 weights);
        vec3 delta = p[point] - closest;
        float distance = delta.Length;
        vec3 motion = cloth ? ((a - old[entry.A]) + (b - old[entry.B]) + (c - old[entry.C])) / 3 :
            ((a - entry.Previous.A) + (b - entry.Previous.B) + (c - entry.Previous.C)) / 3;
        vec3 relativeOld = old[point] + motion;
        float side = vec3.Dot(relativeOld - a, normal) >= 0 ? 1 : -1;
        float correction = radius - distance;
        if (distance > 1e-7f) normal = delta / distance;
        else normal *= side;
        // Catch a vertex crossing the triangle between substeps, even if it ends outside the contact shell.
        vec3 faceNormal = vec3.Cross(b - a, c - a).Normalized;
        float before = vec3.Dot(relativeOld - a, faceNormal), after = vec3.Dot(p[point] - a, faceNormal);
        if (before * after < 0)
        {
            vec3 hit = vec3.Lerp(relativeOld, p[point], before / (before - after));
            vec3 onTriangle = ClosestTriangle(hit, a, b, c, out vec3 hitWeights);
            if ((onTriangle - hit).LengthSqr < 1e-10f)
            { normal = faceNormal * side; correction = radius + MathF.Abs(after); weights = hitWeights; }
        }
        if (correction <= 0) return;
        float inverse = mass[point];
        if (cloth) inverse += mass[entry.A] * weights.x * weights.x +
            mass[entry.B] * weights.y * weights.y + mass[entry.C] * weights.z * weights.z;
        if (inverse == 0) return;
        // Near fixed vertices, do not amplify penetration into arbitrarily large particle corrections.
        if (cloth) inverse = MathF.Max(inverse, MathF.Max(mass[point], MathF.Max(mass[entry.A] * MathF.Abs(weights.x),
            MathF.Max(mass[entry.B] * MathF.Abs(weights.y), mass[entry.C] * MathF.Abs(weights.z)))));
        vec3 push = normal * (correction / inverse);
        p[point] += push * mass[point];
        if (cloth)
        {
            p[entry.A] -= push * (mass[entry.A] * weights.x);
            p[entry.B] -= push * (mass[entry.B] * weights.y);
            p[entry.C] -= push * (mass[entry.C] * weights.z);
        }
    }

    public static void CollideEdges(List<(int A, int B)> edges, vec3[] p, float[] mass, float[] radii,
        bool[] self, HashSet<(int, int)> neighbors)
        => ForEdges(edges, p).CollideEdgePairs(edges, p, mass, radii, self, neighbors);

    public static ClothCollisionTree ForEdges(List<(int A, int B)> edges, vec3[] p) => new(
        edges.Select((e, i) => new Entry(
            vec3.Min(p[e.A], p[e.B]), vec3.Max(p[e.A], p[e.B]), e.A, e.B, i, default)).ToArray());

    public void CollideEdgePairs(List<(int A, int B)> edges, vec3[] p, float[] mass, float[] radii,
        bool[] self, HashSet<(int, int)> neighbors)
    {
        var contacts = new List<(int, int)>();
        CollectEdgeContacts(edges, p, radii.Length == 0 ? 0 : radii.Max(), self, neighbors, contacts);
        foreach (var contact in contacts) CollideClothEdges(contact, edges, p, mass, radii);
    }

    public void CollectEdgeContacts(List<(int A, int B)> edges, vec3[] p, float radius,
        bool[] self, HashSet<(int, int)> neighbors, List<(int Edge, int Other)> contacts)
    {
        for (int i = 0; i < edges.Count; i++)
        {
            var (a, b) = edges[i];
            if (!self[a] && !self[b]) continue;
            vec3 pad = new(radius);
            foreach (var other in Query(vec3.Min(p[a], p[b]) - pad, vec3.Max(p[a], p[b]) + pad))
            {
                int c = other.A, d = other.B;
                if (other.C <= i || a == c || a == d || b == c || b == d ||
                    neighbors.Contains(ClothSimulation.Pair(a, c)) || neighbors.Contains(ClothSimulation.Pair(a, d)) ||
                    neighbors.Contains(ClothSimulation.Pair(b, c)) || neighbors.Contains(ClothSimulation.Pair(b, d))) continue;
                ClosestSegments(p[a], p[b], p[c], p[d], out float s, out float t);
                if ((vec3.Lerp(p[a], p[b], s) - vec3.Lerp(p[c], p[d], t)).LengthSqr > radius * radius) continue;
                contacts.Add((i, other.C));
            }
        }
    }

    public static void CollideClothEdges((int Edge, int Other) contact, List<(int A, int B)> edges,
        vec3[] p, float[] mass, float[] radii)
    {
        var (a, b) = edges[contact.Edge];
        var (c, d) = edges[contact.Other];
        ClosestSegments(p[a], p[b], p[c], p[d], out float s, out float t);
        vec3 delta = vec3.Lerp(p[a], p[b], s) - vec3.Lerp(p[c], p[d], t);
        float distance = delta.Length, radius = MathF.Max(MathF.Max(radii[a], radii[b]), MathF.Max(radii[c], radii[d]));
        if (distance >= radius) return;
        vec3 normal = distance > 1e-7f ? delta / distance : vec3.Cross(p[b] - p[a], p[d] - p[c]);
        if (normal.LengthSqr < 1e-16f) normal = vec3.UnitY;
        normal = normal.Normalized;
        float wa = 1 - s, wb = s, wc = 1 - t, wd = t;
        float inverse = mass[a] * wa * wa + mass[b] * wb * wb + mass[c] * wc * wc + mass[d] * wd * wd;
        if (inverse == 0) return;
        inverse = MathF.Max(inverse, MathF.Max(MathF.Max(mass[a] * wa, mass[b] * wb),
            MathF.Max(mass[c] * wc, mass[d] * wd)));
        vec3 correction = normal * ((radius - distance) / inverse);
        p[a] += correction * (mass[a] * wa); p[b] += correction * (mass[b] * wb);
        p[c] -= correction * (mass[c] * wc); p[d] -= correction * (mass[d] * wd);
    }
    private static void ClosestSegments(vec3 a, vec3 b, vec3 c, vec3 d, out float s, out float t)
    {
        vec3 u = b - a, v = d - c, r = a - c;
        float aa = vec3.Dot(u, u), bb = vec3.Dot(u, v), cc = vec3.Dot(v, v);
        float dd = vec3.Dot(u, r), ee = vec3.Dot(v, r), denominator = aa * cc - bb * bb;
        if (aa <= 1e-16f) { s = 0; t = cc > 1e-16f ? Math.Clamp(ee / cc, 0, 1) : 0; return; }
        if (cc <= 1e-16f) { t = 0; s = Math.Clamp(-dd / aa, 0, 1); return; }
        s = denominator > 1e-16f ? Math.Clamp((bb * ee - cc * dd) / denominator, 0, 1) : 0;
        t = (bb * s + ee) / cc;
        if (t < 0) { t = 0; s = Math.Clamp(-dd / aa, 0, 1); }
        else if (t > 1) { t = 1; s = Math.Clamp((bb - dd) / aa, 0, 1); }
    }
    internal static vec3 ClosestTriangle(vec3 p, vec3 a, vec3 b, vec3 c, out vec3 w)
    {
        vec3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = vec3.Dot(ab, ap), d2 = vec3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) { w = new(1, 0, 0); return a; }
        vec3 bp = p - b;
        float d3 = vec3.Dot(ab, bp), d4 = vec3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) { w = new(0, 1, 0); return b; }
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        { float v = d1 / (d1 - d3); w = new(1 - v, v, 0); return a + ab * v; }
        vec3 cp = p - c;
        float d5 = vec3.Dot(ab, cp), d6 = vec3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) { w = new(0, 0, 1); return c; }
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        { float v = d2 / (d2 - d6); w = new(1 - v, 0, v); return a + ac * v; }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        { float v = (d4 - d3) / (d4 - d3 + d5 - d6); w = new(0, 1 - v, v); return b + (c - b) * v; }
        float inverse = 1 / (va + vb + vc);
        w = new(1 - (vb + vc) * inverse, vb * inverse, vc * inverse);
        return a * w.x + b * w.y + c * w.z;
    }
}
