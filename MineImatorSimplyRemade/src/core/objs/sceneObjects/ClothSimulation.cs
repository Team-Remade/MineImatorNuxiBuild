using GlmSharp;
using MineImatorSimplyRemade.core.diagnostics;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public sealed record ClothSurface(string ObjectId, ClothSettings Settings, vec3[] Vertices,
    int[] Triangles, ClothVertex[] VertexIds);

public readonly record struct ClothTriangle(vec3 A, vec3 B, vec3 C);

/// <summary>Fixed-step position-based cloth with shared particles and interpolated edge seams.</summary>
public sealed class ClothSimulation
{
    public const int MaxVertices = 12000;
    private readonly ClothSurface[] _surfaces;
    private readonly int[][] _maps;
    private readonly List<(int Surface, int Vertex)>[] _sources;
    private readonly List<(int Surface, int Vertex)>[] _pins;
    private readonly vec3[][] _pinAnchors;
    private readonly vec3[] _positions, _previous;
    private readonly float[] _mass, _thickness, _gravity, _damping;
    private readonly bool[] _collide, _self;
    private readonly List<(int A, int B, float RestLength, float Stiffness)> _constraints = [];
    private readonly List<(int Point, int A, int B, int C, vec3 Weights)> _seams = [];
    private readonly List<(int A, int B, int C)> _triangles = [];
    private readonly HashSet<(int, int)> _neighbors = [];
    private readonly List<(int A, int B)> _edges = [];
    private readonly ClothCollisionTree? _selfTree, _edgeTree;
    private ClothCollisionTree? _sceneTree;
    private readonly int _substeps, _iterations;
    private readonly vec3[] _startPins, _endPins, _before;
    private readonly vec3[] _contactPositions, _contactPrevious;
    private readonly List<(int Point, int A, int B, int C)> _pointContacts = [];
    private readonly List<(int Edge, int Other)> _edgeContacts = [];
    private readonly float _contactMargin, _maxThickness;
    private readonly bool _hasSceneCollisions;
    private bool _contactsValid;

    public ClothSimulation(ClothSurface[] surfaces, IReadOnlyDictionary<string, mat4> restPose)
    {
        _surfaces = surfaces;
        _substeps = surfaces.Length == 0 ? 1 : surfaces.Max(s => s.Settings.Substeps);
        _iterations = surfaces.Length == 0 ? 1 : surfaces.Max(s => s.Settings.Iterations);
        int total = surfaces.Sum(s => s.Vertices.Length);
        if (total > MaxVertices) throw new InvalidDataException($"Cloth is limited to {MaxVertices} source vertices per scene.");
        var parent = Enumerable.Range(0, total).ToArray();
        _maps = new int[surfaces.Length][];
        int offset = 0;
        int Root(int n)
        {
            while (parent[n] != n) { parent[n] = parent[parent[n]]; n = parent[n]; }
            return n;
        }
        void Union(int a, int b)
        {
            a = Root(a); b = Root(b);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int s = 0; s < surfaces.Length; s++)
        {
            var surface = surfaces[s];
            surface.Settings.Validate();
            if (!ids.TryAdd(surface.ObjectId, s) || surface.VertexIds.Length != surface.Vertices.Length ||
                surface.VertexIds.Distinct().Count() != surface.VertexIds.Length || surface.Triangles.Length % 3 != 0 ||
                surface.Triangles.Any(v => (uint)v >= surface.Vertices.Length) ||
                surface.Vertices.Any(v => !Finite(v)))
                throw new InvalidDataException("Cloth requires unique object/vertex IDs and finite triangle geometry.");
            _maps[s] = Enumerable.Range(offset, surface.Vertices.Length).ToArray();
            var coincident = new Dictionary<vec3, int>();
            for (int v = 0; v < surface.Vertices.Length; v++)
                if (coincident.TryGetValue(surface.Vertices[v], out int other)) Union(offset + v, other);
                else coincident.Add(surface.Vertices[v], offset + v);
            offset += surface.Vertices.Length;
        }
        var topology = _maps.Select(map => map.Select(Root).ToArray()).ToArray();
        var restVertices = new vec3[total];
        var localVertices = new vec3[total];
        for (int s = 0; s < surfaces.Length; s++)
            for (int v = 0; v < surfaces[s].Vertices.Length; v++)
            {
                restVertices[_maps[s][v]] = World(s, v, restPose);
                localVertices[_maps[s][v]] = surfaces[s].Vertices[v];
            }
        (Dictionary<int, vec3> Coordinates, vec3 U, vec3 V) RegionCoordinates(
            int[] vertices, bool reverse, vec3? referenceU = null, vec3? referenceV = null)
        {
            vec3 center = vec3.Zero;
            foreach (int vertex in vertices) center += localVertices[vertex];
            center /= vertices.Length;
            vec3 Covariance(vec3 direction)
            {
                vec3 result = vec3.Zero;
                foreach (int vertex in vertices)
                {
                    vec3 delta = localVertices[vertex] - center;
                    result += delta * vec3.Dot(delta, direction);
                }
                return result;
            }
            vec3 PrincipalDirection(vec3? perpendicular)
            {
                vec3 direction = vec3.Zero;
                foreach (vec3 axis in new[] { vec3.UnitX, vec3.UnitY, vec3.UnitZ })
                {
                    vec3 candidate = Covariance(axis);
                    if (perpendicular is { } normal) candidate -= normal * vec3.Dot(candidate, normal);
                    if (candidate.LengthSqr > direction.LengthSqr) direction = candidate;
                }
                if (direction.LengthSqr < 1e-20f)
                    throw new InvalidDataException("A cloth region requires a nondegenerate two-dimensional surface patch.");
                direction = direction.Normalized;
                for (int i = 0; i < 32; i++)
                {
                    vec3 next = Covariance(direction);
                    if (perpendicular is { } normal) next -= normal * vec3.Dot(next, normal);
                    direction = next.Normalized;
                }
                return direction;
            }
            vec3 u = PrincipalDirection(null), v = PrincipalDirection(u);
            if (referenceU is { } sameU && referenceV is { } sameV)
            {
                vec3 normal = vec3.Cross(sameU, sameV);
                float minNormal = float.PositiveInfinity, maxNormal = float.NegativeInfinity, radius = 0;
                foreach (int vertex in vertices)
                {
                    vec3 delta = localVertices[vertex] - center;
                    float depth = vec3.Dot(delta, normal);
                    minNormal = MathF.Min(minNormal, depth); maxNormal = MathF.Max(maxNormal, depth);
                    radius = MathF.Max(radius, delta.Length);
                }
                if (maxNormal - minNormal < MathF.Max(1e-8f, radius * 1e-5f)) { u = sameU; v = sameV; }
            }
            if (referenceU is { } ru && vec3.Dot(u, ru) < 0) u = -u;
            if (referenceV is { } rv && vec3.Dot(v, rv) < 0) v = -v;
            vec3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
            var coordinates = new Dictionary<int, vec3>();
            foreach (int vertex in vertices)
            {
                vec3 delta = localVertices[vertex] - center;
                vec3 position = new(vec3.Dot(delta, u), vec3.Dot(delta, v), 0);
                for (int axis = 0; axis < 2; axis++)
                {
                    min[axis] = MathF.Min(min[axis], position[axis]);
                    max[axis] = MathF.Max(max[axis], position[axis]);
                }
                coordinates.Add(vertex, position);
            }
            foreach (int vertex in vertices)
            {
                vec3 position = coordinates[vertex];
                for (int axis = 0; axis < 2; axis++) position[axis] = (position[axis] - min[axis]) / (max[axis] - min[axis]);
                if (reverse) position.x = 1 - position.x;
                coordinates[vertex] = position;
            }
            return (coordinates, u, v);
        }
        // Resolve groups before seams so duplicate UV vertices count only once.
        var groups = new Dictionary<(int, string), int[]>();
        for (int s = 0; s < surfaces.Length; s++)
            foreach (var group in surfaces[s].Settings.Groups)
            {
                var lookup = surfaces[s].VertexIds.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
                var vertices = group.Vertices.Select(v => lookup.TryGetValue(v, out int i) ? _maps[s][i]
                    : throw new InvalidDataException($"Vertex group '{group.Name}' no longer matches its mesh. Re-select its vertices."))
                    .Select(Root).Distinct().ToArray();
                groups.Add((s, group.Id), vertices);
            }
        var seamVertices = new List<(int Point, int A, int B, int C, vec3 Weights)>();
        for (int s = 0; s < surfaces.Length; s++)
            foreach (var group in surfaces[s].Settings.Groups.Where(g => g.SeamObjectId.Length != 0))
            {
                if (!ids.TryGetValue(group.SeamObjectId, out int target) ||
                    !groups.TryGetValue((target, group.SeamGroupId), out var b))
                    throw new InvalidDataException($"Seam '{group.Name}' has a missing or disabled cloth target.");
                var a = groups[(s, group.Id)];
                if (a.Length == 0 || b.Length == 0)
                    throw new InvalidDataException($"Seam '{group.Name}' requires nonempty groups of unique positions.");
                if (group.ReverseSeam) b = b.Reverse().ToArray();
                var dense = a.Length >= b.Length ? a : b;
                var sparse = a.Length >= b.Length ? b : a;
                int sparseSurface = a.Length >= b.Length ? target : s;
                var selected = sparse.ToHashSet();
                var regionTriangles = new List<(int A, int B, int C)>();
                var indices = surfaces[sparseSurface].Triangles;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int x = topology[sparseSurface][indices[i]], y = topology[sparseSurface][indices[i + 1]],
                        z = topology[sparseSurface][indices[i + 2]];
                    if (selected.Contains(x) && selected.Contains(y) && selected.Contains(z) &&
                        vec3.Cross(restVertices[y] - restVertices[x], restVertices[z] - restVertices[x]).LengthSqr > 1e-16f)
                        regionTriangles.Add((x, y, z));
                }
                if (regionTriangles.Count != 0)
                {
                    // Correspondence must not depend on the panels' different world-space poses.
                    var sparseRegion = RegionCoordinates(sparse, false);
                    var denseRegion = RegionCoordinates(dense, group.ReverseSeam, sparseRegion.U, sparseRegion.V);
                    var denseCoordinates = denseRegion.Coordinates;
                    var sparseCoordinates = sparseRegion.Coordinates;
                    regionTriangles.RemoveAll(triangle => vec3.Cross(sparseCoordinates[triangle.B] - sparseCoordinates[triangle.A],
                        sparseCoordinates[triangle.C] - sparseCoordinates[triangle.A]).LengthSqr < 1e-16f);
                    if (regionTriangles.Count == 0)
                        throw new InvalidDataException($"Seam '{group.Name}' requires a surface patch that can be mapped without overlapping folds.");
                    var projected = new Dictionary<vec3, int>();
                    foreach (int point in dense)
                    {
                        float distance = float.PositiveInfinity;
                        var best = regionTriangles[0];
                        vec3 weights = default;
                        foreach (var triangle in regionTriangles)
                        {
                            vec3 candidate = ClothCollisionTree.ClosestTriangle(denseCoordinates[point],
                                sparseCoordinates[triangle.A], sparseCoordinates[triangle.B], sparseCoordinates[triangle.C], out _);
                            float d = (candidate - denseCoordinates[point]).LengthSqr;
                            if (d < distance) { distance = d; best = triangle; }
                        }
                        vec3 coordinate = denseCoordinates[point];
                        if (projected.TryGetValue(coordinate, out int other) && restVertices[other] != restVertices[point])
                            throw new InvalidDataException($"Seam '{group.Name}' has incompatible region layouts that would collapse distinct vertices. Select corresponding rows and faces in both groups.");
                        projected[coordinate] = point;
                        vec3 ab = sparseCoordinates[best.B] - sparseCoordinates[best.A];
                        vec3 ac = sparseCoordinates[best.C] - sparseCoordinates[best.A];
                        vec3 ap = coordinate - sparseCoordinates[best.A];
                        float determinant = vec3.Cross(ab, ac).z;
                        weights.y = vec3.Cross(ap, ac).z / determinant;
                        weights.z = vec3.Cross(ab, ap).z / determinant;
                        weights.x = 1 - weights.y - weights.z;
                        if (weights.y == 0 && weights.z == 0) Union(point, best.A);
                        else if (weights.x == 0 && weights.z == 0) Union(point, best.B);
                        else if (weights.x == 0 && weights.y == 0) Union(point, best.C);
                        else seamVertices.Add((point, best.A, best.B, best.C, weights));
                    }
                    continue;
                }
                int denseSurface = a.Length >= b.Length ? s : target;
                var denseSelection = dense.ToHashSet();
                var denseIndices = surfaces[denseSurface].Triangles;
                for (int i = 0; i < denseIndices.Length; i += 3)
                {
                    int x = topology[denseSurface][denseIndices[i]], y = topology[denseSurface][denseIndices[i + 1]],
                        z = topology[denseSurface][denseIndices[i + 2]];
                    if (denseSelection.Contains(x) && denseSelection.Contains(y) && denseSelection.Contains(z) &&
                        vec3.Cross(restVertices[y] - restVertices[x], restVertices[z] - restVertices[x]).LengthSqr > 1e-16f)
                        throw new InvalidDataException($"Seam '{group.Name}' cannot weld a region to an edge or point. Select a matching surface region on both sides.");
                }
                for (int i = 0; i < dense.Length; i++)
                {
                    if (sparse.Length == 1) { Union(dense[i], sparse[0]); continue; }
                    int numerator = i * (sparse.Length - 1), denominator = dense.Length - 1;
                    int segment = numerator / denominator, remainder = numerator % denominator;
                    if (remainder == 0) Union(dense[i], sparse[segment]);
                    else
                    {
                        float t = remainder / (float)denominator;
                        seamVertices.Add((dense[i], sparse[segment], sparse[segment + 1], sparse[segment + 1], new(1 - t, t, 0)));
                    }
                }
            }
        var compact = new Dictionary<int, int>();
        foreach (var map in _maps)
            for (int i = 0; i < map.Length; i++)
            {
                int root = Root(map[i]);
                if (!compact.TryGetValue(root, out int index)) compact.Add(root, index = compact.Count);
                map[i] = index;
            }
        int count = compact.Count;
        foreach (var seam in seamVertices)
        {
            var mapped = (Point: compact[Root(seam.Point)], A: compact[Root(seam.A)],
                B: compact[Root(seam.B)], C: compact[Root(seam.C)], seam.Weights);
            _seams.Add(mapped);
        }
        _sources = Enumerable.Range(0, count).Select(_ => new List<(int, int)>()).ToArray();
        _pins = Enumerable.Range(0, count).Select(_ => new List<(int, int)>()).ToArray();
        _pinAnchors = new vec3[count][];
        _positions = new vec3[count]; _previous = new vec3[count];
        _startPins = new vec3[count]; _endPins = new vec3[count]; _before = new vec3[count];
        _contactPositions = new vec3[count]; _contactPrevious = new vec3[count];
        _mass = new float[count]; _thickness = new float[count];
        _gravity = new float[count]; _damping = new float[count];
        _collide = new bool[count]; _self = new bool[count];
        for (int s = 0; s < surfaces.Length; s++)
        {
            var settings = surfaces[s].Settings;
            var pinned = settings.Groups.Where(g => g.Pinned).SelectMany(g => g.Vertices).ToHashSet();
            for (int v = 0; v < _maps[s].Length; v++)
            {
                int p = _maps[s][v];
                _sources[p].Add((s, v));
                if (pinned.Contains(surfaces[s].VertexIds[v])) _pins[p].Add((s, v));
                _thickness[p] = MathF.Max(_thickness[p], settings.Thickness);
                _gravity[p] += settings.Gravity; _damping[p] += settings.Damping;
                _collide[p] |= settings.Collisions; _self[p] |= settings.SelfCollision;
            }
        }
        for (int p = 0; p < count; p++)
        {
            _gravity[p] /= _sources[p].Count; _damping[p] /= _sources[p].Count;
            _mass[p] = _pins[p].Count == 0 ? 1f : 0f;
            _hasSceneCollisions |= _collide[p] && _mass[p] != 0;
            var anchors = _pins[p].Count == 0 ? _sources[p] : _pins[p];
            foreach (var (s, v) in anchors) _positions[p] += World(s, v, restPose);
            _positions[p] /= anchors.Count;
            _previous[p] = _positions[p];
        }
        bool bindingPins = _seams.Any(seam => _pins[seam.Point].Count != 0 &&
            (_pins[seam.A].Count != 0 || _pins[seam.B].Count != 0 || _pins[seam.C].Count != 0) &&
            (_positions[seam.Point] - (_positions[seam.A] * seam.Weights.x +
                _positions[seam.B] * seam.Weights.y + _positions[seam.C] * seam.Weights.z)).LengthSqr > 1e-10f);
        if (bindingPins)
        {
            // Pins may move only while establishing the joined rest pose.
            Array.Fill(_mass, 1f);
            bool joined = false;
            for (int i = 0; i < 4096; i++)
            {
                SolveSeams();
                if (_seams.All(seam => (_positions[seam.Point] - (_positions[seam.A] * seam.Weights.x +
                        _positions[seam.B] * seam.Weights.y + _positions[seam.C] * seam.Weights.z)).LengthSqr <= 1e-10f))
                {
                    joined = true;
                    break;
                }
            }
            if (!joined)
            {
                float gap = _seams.Max(seam => (_positions[seam.Point] - (_positions[seam.A] * seam.Weights.x +
                    _positions[seam.B] * seam.Weights.y + _positions[seam.C] * seam.Weights.z)).Length);
                throw new InvalidDataException($"Pinned cloth seams could not settle into a joined rest pose (remaining gap {gap:G5}). Check for overlapping or contradictory seam mappings.");
            }
            for (int p = 0; p < count; p++) _mass[p] = _pins[p].Count == 0 ? 1f : 0f;
        }
        else
            for (int i = 0; i < _iterations; i++) SolveSeams();
        for (int p = 0; p < count; p++)
        {
            _pinAnchors[p] = new vec3[_pins[p].Count];
            for (int i = 0; i < _pins[p].Count; i++)
            {
                int s = _pins[p][i].Surface;
                mat4 matrix = restPose[_surfaces[s].ObjectId];
                if (!float.IsFinite(matrix.Determinant) || MathF.Abs(matrix.Determinant) < 1e-10f)
                    throw new InvalidDataException("Cloth pin binding requires a nonsingular object transform.");
                vec3 anchor = (matrix.Inverse * new vec4(_positions[p], 1)).xyz;
                if (!Finite(anchor)) throw new InvalidDataException("Cloth pin binding requires finite local anchors.");
                _pinAnchors[p][i] = anchor;
            }
        }
        _positions.CopyTo(_previous, 0);
        for (int s = 0; s < surfaces.Length; s++)
        {
            var adjacent = new Dictionary<(int, int), int>();
            var settings = surfaces[s].Settings;
            var indices = surfaces[s].Triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = _maps[s][indices[i]], b = _maps[s][indices[i + 1]], c = _maps[s][indices[i + 2]];
                if (a == b || b == c || c == a) continue;
                _triangles.Add((a, b, c));
                Edge(a, b, c, indices[i], indices[i + 1], indices[i + 2]);
                Edge(b, c, a, indices[i + 1], indices[i + 2], indices[i]);
                Edge(c, a, b, indices[i + 2], indices[i], indices[i + 1]);
            }
            void Edge(int a, int b, int opposite, int sourceA, int sourceB, int sourceOpposite)
            {
                var key = Pair(a, b);
                if (adjacent.TryGetValue(key, out int other))
                    AddConstraint(_maps[s][other], opposite, settings.Bend,
                        (restVertices[topology[s][other]] - restVertices[topology[s][sourceOpposite]]).Length);
                else
                {
                    adjacent.Add(key, sourceOpposite);
                    AddConstraint(a, b, settings.Stretch,
                        (restVertices[topology[s][sourceA]] - restVertices[topology[s][sourceB]]).Length);
                }
                if (_neighbors.Add(key)) _edges.Add(key);
            }
        }
        void AddConstraint(int a, int b, float stiffness, float restLength)
        {
            if (a != b) _constraints.Add((a, b, restLength,
                1f - MathF.Pow(1f - stiffness, 1f / _iterations)));
        }
        foreach (var seam in _seams)
        {
            if (seam.Point != seam.A) _neighbors.Add(Pair(seam.Point, seam.A));
            if (seam.Point != seam.B) _neighbors.Add(Pair(seam.Point, seam.B));
            if (seam.Point != seam.C) _neighbors.Add(Pair(seam.Point, seam.C));
        }
        if (_self.Any(value => value))
        {
            _maxThickness = _thickness.Max();
            _contactMargin = _maxThickness;
            _selfTree = ClothCollisionTree.ForCloth(_triangles, _positions, _positions);
            _edgeTree = ClothCollisionTree.ForEdges(_edges, _positions);
        }
    }

    public void Step(float seconds, IReadOnlyDictionary<string, mat4> pose, IReadOnlyList<ClothTriangle> colliders,
        IReadOnlyList<ClothTriangle>? previousColliders = null)
    {
        if (!float.IsFinite(seconds) || seconds <= 0 || seconds > 1)
            throw new InvalidDataException("Cloth timestep must be between zero and one second.");
        using var profile = FrameProfiler.Shared.Measure(ProfileStage.ClothSimulation);
        int substeps = _substeps, iterations = _iterations;
        float dt = seconds / substeps;
        var startPins = _startPins;
        var endPins = _endPins;
        _positions.CopyTo(startPins, 0);
        Array.Clear(endPins);
        for (int p = 0; p < _positions.Length; p++)
            if (_pins[p].Count != 0)
            {
                for (int i = 0; i < _pins[p].Count; i++)
                    endPins[p] += Transform(_pins[p][i].Surface, _pinAnchors[p][i], pose);
                endPins[p] /= _pins[p].Count;
                for (int i = 0; i < _pins[p].Count; i++)
                    if ((Transform(_pins[p][i].Surface, _pinAnchors[p][i], pose) - endPins[p]).Length > _thickness[p])
                        throw PinConflict(p);
            }
        ClothCollisionTree? scene = null;
        bool stationaryScene = true;
        if (_hasSceneCollisions && colliders.Count != 0)
        {
            if (_sceneTree == null || _sceneTree.EntryCount != colliders.Count)
                _sceneTree = ClothCollisionTree.ForScene(colliders, previousColliders ?? colliders);
            if (previousColliders?.Count == colliders.Count)
                for (int i = 0; i < colliders.Count; i++)
                    if (previousColliders[i] != colliders[i]) { stationaryScene = false; break; }
            scene = _sceneTree;
            if (stationaryScene && !scene.MatchesStationaryScene(colliders))
                scene.RefitScene(colliders, null, 0, 1);
        }
        for (int step = 0; step < substeps; step++)
        {
            if (!stationaryScene)
                scene!.RefitScene(colliders, previousColliders, step / (float)substeps, (step + 1f) / substeps);
            var before = _before;
            _positions.CopyTo(before, 0);
            for (int p = 0; p < _positions.Length; p++)
            {
                if (_mass[p] == 0)
                {
                    _positions[p] = vec3.Lerp(startPins[p], endPins[p], (step + 1f) / substeps);
                    _previous[p] = _positions[p];
                    continue;
                }
                vec3 current = _positions[p];
                float drag = MathF.Pow(1f - _damping[p], dt * 60f);
                _positions[p] += (current - _previous[p]) * drag - vec3.UnitY * (_gravity[p] * dt * dt);
                _previous[p] = current;
            }
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                foreach (var (a, b, rest, stiffness) in _constraints)
                {
                    vec3 difference = _positions[b] - _positions[a];
                    float length = difference.Length, mass = _mass[a] + _mass[b];
                    if (length < 1e-8f || mass == 0) continue;
                    vec3 correction = difference * ((length - rest) * stiffness / (length * mass));
                    _positions[a] += correction * _mass[a];
                    _positions[b] -= correction * _mass[b];
                }
                for (int p = 0; p < _positions.Length; p++)
                    if (_collide[p] && _mass[p] != 0)
                        scene?.CollidePoint(p, _positions, before, _mass, _thickness[p], null);
                if (_selfTree != null)
                {
                    EnsureSelfContacts(before);
                    foreach (var contact in _pointContacts)
                        ClothCollisionTree.CollideClothPoint(contact, _positions, before, _mass, _thickness[contact.Point]);
                    EnsureSelfContacts(before);
                    foreach (var contact in _edgeContacts)
                        ClothCollisionTree.CollideClothEdges(contact, _edges, _positions, _mass, _thickness);
                }
                SolveSeams();
            }

            for (int p = 0; p < _positions.Length; p++)
                if (!Finite(_positions[p]) || _positions[p].LengthSqr > 1e16f)
                {
                    var (surface, vertex) = _sources[p][0];
                    var id = _surfaces[surface].VertexIds[vertex];
                    throw new InvalidDataException($"Cloth exceeded its numeric range at substep {step + 1}/{substeps}: object {_surfaces[surface].ObjectId}, mesh {id.Mesh}, vertex {id.Vertex}, position {_positions[p]}, previous {_previous[p]}.");
                }
        }
    }

    private void SolveSeams()
    {
        Span<int> indices = stackalloc int[4];
        Span<float> gradients = stackalloc float[4];
        foreach (var (point, a, b, c, weights) in _seams)
        {
            vec3 error = _positions[point] - (_positions[a] * weights.x + _positions[b] * weights.y + _positions[c] * weights.z);
            indices[0] = point; indices[1] = a; indices[2] = b; indices[3] = c;
            gradients[0] = 1; gradients[1] = -weights.x; gradients[2] = -weights.y; gradients[3] = -weights.z;
            // Overlapping groups or other welds can make constraint indices identical.
            for (int i = 1; i < 4; i++)
                for (int j = 0; j < i; j++)
                    if (indices[i] == indices[j]) { gradients[j] += gradients[i]; gradients[i] = 0; break; }
            float mass = 0;
            for (int i = 0; i < 4; i++) mass += _mass[indices[i]] * gradients[i] * gradients[i];
            if (mass == 0)
            {
                if (error.Length > MathF.Max(MathF.Max(_thickness[point], _thickness[c]), MathF.Max(_thickness[a], _thickness[b])))
                    throw new InvalidDataException("Animation pulls a snap-bound, pinned cloth seam apart. Animate the joined panels together or unpin one side of the seam.");
                continue;
            }
            vec3 correction = error / mass;
            for (int i = 0; i < 4; i++) _positions[indices[i]] -= correction * (_mass[indices[i]] * gradients[i]);
        }
    }

    private void EnsureSelfContacts(vec3[] before)
    {
        using var profile = FrameProfiler.Shared.Measure(ProfileStage.ClothSelfContacts);
        float limit = _contactMargin * _contactMargin * 0.25f;
        if (_contactsValid)
        {
            int p = 0;
            for (; p < _positions.Length; p++)
                if ((_positions[p] - _contactPositions[p]).LengthSqr > limit ||
                    (before[p] - _contactPrevious[p]).LengthSqr > limit) break;
            if (p == _positions.Length) return;
        }
        // Each side may move by half the padding before the broad phase must be refreshed.
        _selfTree!.RefitCloth(_positions, before);
        _edgeTree!.RefitEdges(_positions);
        _pointContacts.Clear(); _edgeContacts.Clear();
        for (int p = 0; p < _positions.Length; p++)
            if (_self[p])
                _selfTree.CollectPointContacts(p, _positions, before, _thickness[p] + _contactMargin, _neighbors, _pointContacts);
        _edgeTree.CollectEdgeContacts(_edges, _positions, _maxThickness + _contactMargin, _self, _neighbors, _edgeContacts);
        _positions.CopyTo(_contactPositions, 0);
        before.CopyTo(_contactPrevious, 0);
        _contactsValid = true;
    }

    public vec3[] GetWorldVertices(int surface) => _maps[surface].Select(p => _positions[p]).ToArray();
    private InvalidDataException PinConflict(int particle)
    {
        var anchors = _pins[particle].Select(pin => $"{_surfaces[pin.Surface].ObjectId} (mesh {_surfaces[pin.Surface].VertexIds[pin.Vertex].Mesh}, vertex {_surfaces[pin.Surface].VertexIds[pin.Vertex].Vertex})");
        return new InvalidDataException($"Animation pulls snap-bound cloth pins apart: {string.Join(", ", anchors)}. Animate the joined panels together or unpin one side of the seam.");
    }

    private vec3 World(int s, int v, IReadOnlyDictionary<string, mat4> pose) =>
        Transform(s, _surfaces[s].Vertices[v], pose);

    private vec3 Transform(int s, vec3 local, IReadOnlyDictionary<string, mat4> pose)
    {
        if (!pose.TryGetValue(_surfaces[s].ObjectId, out mat4 matrix))
            throw new InvalidDataException("Cloth object is missing from the sampled pose.");
        vec3 position = (matrix * new vec4(local, 1)).xyz;
        if (!Finite(position)) throw new InvalidDataException("Cloth requires finite object transforms.");
        return position;
    }
    internal static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);
    internal static bool Finite(vec3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
}
