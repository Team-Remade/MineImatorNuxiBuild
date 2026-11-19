using System.Numerics;
using GlmSharp;
using JoltPhysicsSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>Collision geometry (world space) and pose captured when the simulation is created.</summary>
/// <param name="World">Object world transform without pivot; simulated poses are applied relative to it.</param>
/// <param name="Points">World-space source vertices for the collision shape.</param>
/// <param name="Triangles">Indices into <paramref name="Points"/>; used by the Mesh shape.</param>
public sealed record RigidBodySource(string ObjectId, RigidBodySettings Settings, mat4 World, vec3[] Points, int[] Triangles);

/// <summary>One fluid coupled to the rigid body world. Geometry is in the fluid's local space.</summary>
public sealed class RigidBodyFluid
{
    public required string ObjectId { get; init; }
    public required FluidSimulation Simulation { get; init; }
    public required FluidSettings Settings { get; init; }
    /// <summary>Fluid local-to-world transform for the current frame.</summary>
    public mat4 World { get; set; } = mat4.Identity;
    public IReadOnlyList<ClothTriangle>? SceneColliders { get; set; }
}

/// <summary>Per-frame driving input for one body.</summary>
/// <param name="World">Sampled authored world transform without pivot, used as the kinematic target.</param>
/// <param name="Dynamic">True when the body is simulated (not following animation) over this frame.</param>
public readonly record struct RigidBodyTarget(mat4 World, bool Dynamic);

/// <summary>
/// Jolt Physics rigid body world. Bodies start from their captured pose; active bodies can follow animation
/// (kinematic) and switch to dynamics. Coupled fluids apply Jolt buoyancy to bodies and bodies push particles out.
/// </summary>
public sealed class RigidBodySimulation : IDisposable
{
    public const int MaxBodies = 4096;
    public const int MaxSourcePoints = 200000;
    /// <summary>Upper bound on fluid weight vs. body weight, keeping very light bodies stable at the surface.</summary>
    public const float MaxBuoyancy = 50f;
    private const float MinHalfExtent = 0.005f;
    private const int MinSurfaceSamples = 8;

    private static readonly Lock InitLock = new();
    private static bool _initialized;

    private readonly RigidBodyWorldSettings _world;
    private readonly List<IDisposable> _disposables = [];
    private readonly ObjectLayerPairFilterMask _pairFilter;
    private readonly BroadPhaseLayerInterfaceMask _broadPhase;
    private readonly ObjectVsBroadPhaseLayerFilterMask _objectVsBroadPhase;
    private readonly PhysicsSystem _system;
    private readonly JobSystem _jobs;
    private readonly BodyInterface _bodies;
    private readonly SimBody[] _simBodies;
    private readonly Dictionary<RigidBodyFluid, FluidCollider> _fluidColliders = [];
    private readonly List<float> _heights = [];
    private bool _disposed;

    private sealed class SimBody
    {
        public required RigidBodySource Source { get; init; }
        public required BodyID Id { get; init; }
        public required Body Handle { get; init; }
        public required mat4 InitialWorld { get; init; }
        public required mat4 InitialBodyInverse { get; init; }
        public required float Volume { get; init; }
        /// <summary>Exact solid used to displace fluid particles; null for Mesh shapes.</summary>
        public required BodySolid? Solid { get; init; }
        /// <summary>Body-local triangle soup for Mesh shapes, collided as fluid scene triangles.</summary>
        public Vector3[] MeshVertices { get; init; } = [];
        public int[] MeshTriangles { get; init; } = [];
        public MotionType Motion { get; set; }
    }

    /// <summary>Signed distance to a convex body shape in body-local space (negative inside).</summary>
    private sealed class BodySolid
    {
        private enum Kind { Box, Sphere, Capsule, Cylinder, Hull }
        private Kind _kind;
        private Vector3 _half;
        private float _radius, _halfHeight;
        private (Vector3 Normal, float Offset)[] _planes = [];

        public static BodySolid? For(Shape shape) => shape switch
        {
            BoxShape box => new BodySolid { _kind = Kind.Box, _half = box.HalfExtent },
            SphereShape sphere => new BodySolid { _kind = Kind.Sphere, _radius = sphere.Radius },
            CapsuleShape capsule => new BodySolid
                { _kind = Kind.Capsule, _radius = capsule.Radius, _halfHeight = capsule.HalfHeightOfCylinder },
            CylinderShape cylinder => new BodySolid
                { _kind = Kind.Cylinder, _radius = cylinder.Radius, _halfHeight = cylinder.HalfHeight },
            ConvexHullShape hull => Hull(hull),
            _ => null
        };

        private static BodySolid Hull(ConvexHullShape hull)
        {
            // Jolt stores hull points relative to the center of mass.
            Vector3 com = hull.CenterOfMass;
            var points = new Vector3[hull.GetNumPoints()];
            for (uint i = 0; i < points.Length; i++) points[i] = hull.GetPoint(i) + com;
            Vector3 centroid = Vector3.Zero;
            foreach (var point in points) centroid += point;
            centroid /= Math.Max(1, points.Length);
            var planes = new List<(Vector3, float)>();
            var indices = new uint[256];
            for (uint face = 0; face < hull.GetNumFaces(); face++)
            {
                int count = (int)hull.GetFaceVertices(face, (uint)indices.Length, indices);
                if (count < 3) continue;
                // Newell's method is robust for slightly non-planar faces.
                Vector3 normal = Vector3.Zero, faceCenter = Vector3.Zero;
                for (int i = 0; i < count; i++)
                {
                    Vector3 a = points[indices[i]], b = points[indices[(i + 1) % count]];
                    normal += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
                    faceCenter += a;
                }
                if (normal.LengthSquared() < 1e-20f) continue;
                normal = Vector3.Normalize(normal);
                faceCenter /= count;
                if (Vector3.Dot(normal, faceCenter - centroid) < 0) normal = -normal;
                planes.Add((normal, Vector3.Dot(normal, faceCenter)));
            }
            return new BodySolid { _kind = Kind.Hull, _planes = planes.ToArray() };
        }

        public float Distance(Vector3 p, out Vector3 normal)
        {
            switch (_kind)
            {
                case Kind.Box:
                {
                    Vector3 q = Vector3.Abs(p) - _half;
                    Vector3 outside = Vector3.Max(q, Vector3.Zero);
                    if (outside.LengthSquared() > 0f)
                    {
                        normal = Vector3.Normalize(outside * new Vector3(MathF.Sign(p.X), MathF.Sign(p.Y), MathF.Sign(p.Z)));
                        return outside.Length();
                    }
                    if (q.X >= q.Y && q.X >= q.Z) { normal = new(p.X < 0 ? -1 : 1, 0, 0); return q.X; }
                    if (q.Y >= q.Z) { normal = new(0, p.Y < 0 ? -1 : 1, 0); return q.Y; }
                    normal = new(0, 0, p.Z < 0 ? -1 : 1);
                    return q.Z;
                }
                case Kind.Sphere:
                    return Radial(p, _radius, out normal);
                case Kind.Capsule:
                    return Radial(p - new Vector3(0, Math.Clamp(p.Y, -_halfHeight, _halfHeight), 0), _radius, out normal);
                case Kind.Cylinder:
                {
                    Vector2 horizontal = new(p.X, p.Z);
                    float length = horizontal.Length();
                    Vector3 radial = length > 1e-7f ? new Vector3(p.X / length, 0, p.Z / length) : Vector3.UnitX;
                    float side = length - _radius, cap = MathF.Abs(p.Y) - _halfHeight;
                    Vector3 up = new(0, p.Y < 0 ? -1 : 1, 0);
                    if (side <= 0 && cap <= 0)
                    {
                        normal = side > cap ? radial : up;
                        return MathF.Max(side, cap);
                    }
                    Vector3 offset = radial * MathF.Max(side, 0) + up * MathF.Max(cap, 0);
                    normal = Vector3.Normalize(offset);
                    return offset.Length();
                }
                default:
                {
                    float best = float.MinValue;
                    normal = Vector3.UnitY;
                    foreach (var (planeNormal, offset) in _planes)
                    {
                        float distance = Vector3.Dot(planeNormal, p) - offset;
                        if (distance > best) { best = distance; normal = planeNormal; }
                    }
                    return best;
                }
            }
        }

        private static float Radial(Vector3 offset, float radius, out Vector3 normal)
        {
            float length = offset.Length();
            normal = length > 1e-7f ? offset / length : Vector3.UnitY;
            return length - radius;
        }
    }

    public int BodyCount => _simBodies.Length;

    public static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initialized) return;
            if (!Foundation.Init(false))
                throw new InvalidOperationException("Jolt Physics failed to initialize.");
            _initialized = true;
        }
    }

    public RigidBodySimulation(IReadOnlyList<RigidBodySource> sources, RigidBodyWorldSettings world)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(world);
        world.Validate();
        if (sources.Count > MaxBodies)
            throw new InvalidDataException($"Rigid body simulations are limited to {MaxBodies} bodies.");
        EnsureInitialized();
        _world = world;
        try
        {
            // Native filter lifetimes are owned by the physics system; keep wrappers referenced alongside it.
            _pairFilter = new ObjectLayerPairFilterMask();
            _broadPhase = new BroadPhaseLayerInterfaceMask(1);
            _broadPhase.ConfigureLayer(new BroadPhaseLayer(0), RigidBodySettings.AllCollections, 0);
            _objectVsBroadPhase = new ObjectVsBroadPhaseLayerFilterMask(_broadPhase);
            _system = new PhysicsSystem(new PhysicsSystemSettings
            {
                MaxBodies = sources.Count + 1,
                MaxBodyPairs = 65536,
                MaxContactConstraints = 65536,
                ObjectLayerPairFilter = _pairFilter,
                BroadPhaseLayerInterface = _broadPhase,
                ObjectVsBroadPhaseLayerFilter = _objectVsBroadPhase
            });
            var physics = _system.Settings;
            physics.NumVelocitySteps = (uint)world.SolverIterations;
            physics.NumPositionSteps = (uint)Math.Clamp(world.SolverIterations / 5, 1, 20);
            _system.Settings = physics;
            _system.Gravity = ToNumerics(world.Gravity);
            _jobs = new JobSystemThreadPool();
            _bodies = _system.BodyInterface;
            _simBodies = new SimBody[sources.Count];
            for (int i = 0; i < sources.Count; i++) _simBodies[i] = CreateBody(sources[i]);
            _system.OptimizeBroadPhase();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private T Track<T>(T disposable) where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    private SimBody CreateBody(RigidBodySource source)
    {
        var settings = source.Settings;
        settings.Validate();
        if (source.Points.Length == 0)
            throw new InvalidDataException($"Rigid body {source.ObjectId}: no visible mesh to build a collision shape from.");
        if (source.Points.Length > MaxSourcePoints)
            throw new InvalidDataException($"Rigid body {source.ObjectId}: collision source exceeds {MaxSourcePoints} vertices.");
        if (!Finite(source.World) || MathF.Abs(source.World.Determinant) < 1e-12f)
            throw new InvalidDataException($"Rigid body {source.ObjectId}: the object transform must be finite and nonsingular.");

        Quaternion rotation = RotationOf(source.World);
        Quaternion inverseRotation = Quaternion.Conjugate(rotation);
        var local = new Vector3[source.Points.Length];
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = 0; i < local.Length; i++)
        {
            if (!Finite(source.Points[i]))
                throw new InvalidDataException($"Rigid body {source.ObjectId}: collision geometry is not finite.");
            local[i] = Vector3.Transform(ToNumerics(source.Points[i]), inverseRotation);
            min = Vector3.Min(min, local[i]);
            max = Vector3.Max(max, local[i]);
        }
        Vector3 center = (min + max) * 0.5f;
        Vector3 half = (max - min) * 0.5f;
        for (int i = 0; i < local.Length; i++) local[i] -= center;
        Vector3 position = Vector3.Transform(center, rotation);

        bool isStatic = settings.Type == RigidBodyType.Passive && !settings.Animated;
        Shape shape = Track(BuildShape(source, settings, local, half));
        float volume = settings.Shape == RigidBodyShape.Mesh ? 0f : shape.Volume;
        if (!isStatic && !(volume > 0f))
            throw new InvalidDataException($"Rigid body {source.ObjectId}: the collision shape has no volume.");
        var layer = ObjectLayerPairFilterMask.GetObjectLayer((uint)settings.CollisionCollections, (uint)settings.CollisionCollections);
        var motion = isStatic ? MotionType.Static : MotionType.Kinematic;
        using var creation = new BodyCreationSettings(shape, position, rotation, motion, layer)
        {
            Friction = settings.Friction,
            Restitution = settings.Bounciness,
            LinearDamping = settings.LinearDamping,
            AngularDamping = settings.AngularDamping,
            AllowSleeping = settings.Deactivation,
            MotionQuality = settings.ContinuousCollision ? MotionQuality.LinearCast : MotionQuality.Discrete
        };
        if (settings.Type == RigidBodyType.Active)
        {
            creation.OverrideMassProperties = OverrideMassProperties.CalculateInertia;
            creation.MassPropertiesOverride = new MassProperties { Mass = settings.Mass };
        }
        var body = _bodies.CreateBody(creation) ??
            throw new InvalidDataException($"Rigid body {source.ObjectId}: Jolt could not create the body.");
        _bodies.AddBody(body.ID, isStatic ? Activation.DontActivate : Activation.Activate);
        mat4 bodyWorld = Compose(position, rotation);
        return new SimBody
        {
            Source = source, Id = body.ID, Handle = body, InitialWorld = source.World,
            InitialBodyInverse = bodyWorld.Inverse, Volume = volume, Motion = motion,
            Solid = BodySolid.For(shape),
            MeshVertices = settings.Shape == RigidBodyShape.Mesh ? local : [],
            MeshTriangles = settings.Shape == RigidBodyShape.Mesh ? source.Triangles : []
        };
    }

    private static Shape BuildShape(RigidBodySource source, RigidBodySettings settings, Vector3[] local, Vector3 half)
    {
        Vector3 safeHalf = Vector3.Max(half, new Vector3(MinHalfExtent));
        float Radius(float limit) => MathF.Min(settings.Margin, limit * 0.9f);
        ShapeSettings Box() => new BoxShapeSettings(safeHalf, Radius(MathF.Min(safeHalf.X, MathF.Min(safeHalf.Y, safeHalf.Z))));
        ShapeSettings shapeSettings;
        switch (settings.Shape)
        {
            case RigidBodyShape.Box:
                shapeSettings = Box();
                break;
            case RigidBodyShape.Sphere:
                shapeSettings = new SphereShapeSettings(MathF.Max(safeHalf.X, MathF.Max(safeHalf.Y, safeHalf.Z)));
                break;
            case RigidBodyShape.Capsule:
            {
                float radius = MathF.Max(safeHalf.X, safeHalf.Z);
                shapeSettings = new CapsuleShapeSettings(MathF.Max(safeHalf.Y - radius, 0.001f), radius);
                break;
            }
            case RigidBodyShape.Cylinder:
            {
                float radius = MathF.Max(safeHalf.X, safeHalf.Z);
                shapeSettings = new CylinderShapeSettings(safeHalf.Y, radius, Radius(MathF.Min(safeHalf.Y, radius)));
                break;
            }
            case RigidBodyShape.ConvexHull:
            {
                var unique = local.Select(p => new Vector3(MathF.Round(p.X, 4), MathF.Round(p.Y, 4), MathF.Round(p.Z, 4)))
                    .Distinct().ToArray();
                // Flat or tiny sources (planes) have no hull volume; a thin box matches Blender's usable result.
                bool flat = half.X < MinHalfExtent || half.Y < MinHalfExtent || half.Z < MinHalfExtent;
                shapeSettings = unique.Length < 4 || flat ? Box() : new ConvexHullShapeSettings(unique, settings.Margin);
                break;
            }
            case RigidBodyShape.Mesh:
            {
                if (source.Triangles.Length < 3 || source.Triangles.Length % 3 != 0)
                    throw new InvalidDataException($"Rigid body {source.ObjectId}: Mesh shapes need triangle geometry.");
                var triangles = new IndexedTriangle[source.Triangles.Length / 3];
                for (int t = 0; t < triangles.Length; t++)
                {
                    int a = source.Triangles[t * 3], b = source.Triangles[t * 3 + 1], c = source.Triangles[t * 3 + 2];
                    if ((uint)a >= local.Length || (uint)b >= local.Length || (uint)c >= local.Length)
                        throw new InvalidDataException($"Rigid body {source.ObjectId}: invalid mesh index.");
                    triangles[t] = new IndexedTriangle(a, b, c, 0, 0);
                }
                shapeSettings = new MeshShapeSettings(local.AsSpan(), triangles.AsSpan());
                break;
            }
            default:
                throw new InvalidDataException("Unknown rigid body shape.");
        }
        using (shapeSettings)
        {
            Shape? shape = null;
            try { shape = shapeSettings.Create(); }
            catch (Exception ex) when (settings.Shape == RigidBodyShape.ConvexHull)
            {
                Console.Error.WriteLine($"Rigid body {source.ObjectId}: convex hull failed ({ex.Message}); using a box.");
            }
            if (shape == null && settings.Shape == RigidBodyShape.ConvexHull)
            {
                using var fallback = Box();
                shape = fallback.Create();
            }
            return shape ?? throw new InvalidDataException(
                $"Rigid body {source.ObjectId}: Jolt could not build the {settings.Shape} collision shape from this geometry.");
        }
    }

    /// <summary>Advances one timeline frame. Kinematic bodies interpolate toward their targets over all substeps.</summary>
    public void Step(float frameSeconds, IReadOnlyList<RigidBodyTarget> targets, IReadOnlyList<RigidBodyFluid> fluids,
        float fluidStartSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!float.IsFinite(frameSeconds) || frameSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(frameSeconds));
        if (targets.Count != _simBodies.Length)
            throw new ArgumentException("Every body requires a target.", nameof(targets));

        var kinematicStart = new (Vector3 Position, Quaternion Rotation)[_simBodies.Length];
        var kinematicEnd = new (Vector3 Position, Quaternion Rotation)[_simBodies.Length];
        for (int i = 0; i < _simBodies.Length; i++)
        {
            var body = _simBodies[i];
            if (body.Motion == MotionType.Static) continue;
            var desired = body.Source.Settings.Type == RigidBodyType.Active && targets[i].Dynamic
                ? MotionType.Dynamic : MotionType.Kinematic;
            if (desired != body.Motion)
            {
                _bodies.SetMotionType(body.Id, desired, Activation.Activate);
                body.Motion = desired;
            }
            if (desired != MotionType.Kinematic) continue;
            kinematicStart[i] = (_bodies.GetPosition(body.Id), _bodies.GetRotation(body.Id));
            if (!Finite(targets[i].World) || MathF.Abs(targets[i].World.Determinant) < 1e-12f)
                throw new InvalidDataException($"Rigid body {body.Source.ObjectId}: animated transform must be finite and nonsingular.");
            // B_t = N_t * N_0^-1 * B_0, made rigid for Jolt.
            mat4 target = targets[i].World * body.InitialWorld.Inverse * body.InitialBodyInverse.Inverse;
            kinematicEnd[i] = (new Vector3(target.m30, target.m31, target.m32), RotationOf(target));
        }

        int substeps = _world.Substeps;
        float dt = frameSeconds * _world.Speed / substeps;
        for (int k = 0; k < substeps; k++)
        {
            float s = (k + 1f) / substeps;
            if (dt > 0f)
            {
                for (int i = 0; i < _simBodies.Length; i++)
                {
                    if (_simBodies[i].Motion != MotionType.Kinematic) continue;
                    _bodies.MoveKinematic(_simBodies[i].Id,
                        Vector3.Lerp(kinematicStart[i].Position, kinematicEnd[i].Position, s),
                        Quaternion.Slerp(kinematicStart[i].Rotation, kinematicEnd[i].Rotation, s), dt);
                }
                foreach (var fluid in fluids) ApplyBuoyancy(fluid, dt);
                var error = _system.Update(dt, 1, _jobs);
                if (error != PhysicsUpdateError.None)
                    throw new InvalidDataException($"Rigid body solver capacity exceeded ({error}). Reduce overlapping bodies.");
            }
            float fluidTime = fluidStartSeconds + frameSeconds * s;
            foreach (var fluid in fluids)
            {
                // Only refit collision data when the fluid's fixed 60 Hz step is actually due.
                if (fluid.Simulation.SimulatedTime + FluidSimulation.TimeStep > fluidTime + 0.000001f) continue;
                var collider = ColliderFor(fluid);
                collider.Refresh();
                fluid.Simulation.SimulateToTime(fluidTime, FluidTriangles(fluid), collider);
            }
        }
        // MoveKinematic leaves each kinematic body with its last substep velocity, so a later
        // switch to dynamics (keyframed Animated handoff) carries that momentum naturally.
    }

    /// <summary>Returns the simulated object world transform without pivot.</summary>
    public mat4 GetWorld(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var body = _simBodies[index];
        mat4 bodyWorld = Compose(_bodies.GetPosition(body.Id), _bodies.GetRotation(body.Id));
        mat4 world = bodyWorld * body.InitialBodyInverse * body.InitialWorld;
        if (!Finite(world)) throw new InvalidDataException($"Rigid body {body.Source.ObjectId}: simulation became nonfinite.");
        return world;
    }

    public vec3 GetLinearVelocity(int index) => FromNumerics(_bodies.GetLinearVelocity(_simBodies[index].Id));

    public bool IsDynamic(int index) => _simBodies[index].Motion == MotionType.Dynamic;

    private void ApplyBuoyancy(RigidBodyFluid fluid, float dt)
    {
        var settings = fluid.Settings;
        var particles = fluid.Simulation.Particles;
        if (particles.Count == 0 || (settings.Density <= 0f && settings.LinearDrag <= 0f && settings.AngularDrag <= 0f)) return;
        mat4 world = fluid.World, inverse = world.Inverse;
        vec3 axisX = (world * new vec4(1, 0, 0, 0)).xyz, axisY = (world * new vec4(0, 1, 0, 0)).xyz,
            axisZ = (world * new vec4(0, 0, 1, 0)).xyz;
        vec3 normal = vec3.Cross(axisZ, axisX).Normalized;
        if (vec3.Dot(normal, axisY) < 0) normal = -normal;
        float h = settings.SmoothingRadius;
        vec3 bounds = settings.Bounds;
        foreach (var body in _simBodies)
        {
            if (body.Motion != MotionType.Dynamic) continue;
            var box = body.Handle.WorldSpaceBounds;
            vec3 min = new(float.MaxValue), max = new(float.MinValue);
            for (int corner = 0; corner < 8; corner++)
            {
                vec3 p = new((corner & 1) == 0 ? box.Min.X : box.Max.X, (corner & 2) == 0 ? box.Min.Y : box.Max.Y,
                    (corner & 4) == 0 ? box.Min.Z : box.Max.Z);
                vec3 l = (inverse * new vec4(p, 1)).xyz;
                min = vec3.Min(min, l);
                max = vec3.Max(max, l);
            }
            if (max.x < -bounds.x - h || min.x > bounds.x + h || max.y < -bounds.y - h || min.y > bounds.y + h ||
                max.z < -bounds.z - h || min.z > bounds.z + h) continue;
            if (!EstimateSurface(particles, settings, min, max, out float surface, out vec3 flow)) continue;
            if (min.y >= surface) continue;
            vec3 center = (min + max) * 0.5f;
            vec3 surfacePoint = (world * new vec4(center.x, surface, center.z, 1)).xyz;
            // Particle velocities are per simulated second; Jolt velocities are per physics second.
            vec3 fluidVelocity = (world * new vec4(flow, 0)).xyz * settings.SimulationSpeed / MathF.Max(_world.Speed, 1e-6f);
            float buoyancy = MathF.Min(settings.Density * body.Volume / body.Source.Settings.Mass, MaxBuoyancy);
            // BodyInterface.ApplyBuoyancyImpulse in JoltPhysicsSharp 2.24 passes the body ID by pointer while joltc
            // expects it by value, so the locked call always fails. The Body overload is correct; wake the body manually.
            if (body.Handle.ApplyBuoyancyImpulse(ToNumerics(surfacePoint), ToNumerics(normal), buoyancy,
                    settings.LinearDrag, settings.AngularDrag, ToNumerics(fluidVelocity), _system.Gravity, dt) &&
                !body.Handle.IsActive)
                _bodies.ActivateBody(body.Id);
        }
    }

    /// <summary>
    /// Estimates the undisturbed water level around a body footprint (fluid-local space). Water columns in the ring
    /// around the body are treated as uniformly filled, so the surface is 2 * median - floor; unlike a maximum or
    /// high percentile, the median ignores splashes and particles stacked on the body.
    /// </summary>
    private bool EstimateSurface(IReadOnlyList<FluidParticle> particles, FluidSettings settings, vec3 min, vec3 max,
        out float surface, out vec3 flow)
    {
        surface = 0f;
        flow = vec3.Zero;
        float pad = settings.SmoothingRadius;
        for (int attempt = 0; attempt < 4; attempt++, pad *= 2f)
        {
            _heights.Clear();
            foreach (var particle in particles)
            {
                vec3 p = particle.Position;
                bool inRing = p.x >= min.x - pad && p.x <= max.x + pad && p.z >= min.z - pad && p.z <= max.z + pad &&
                    !(p.x > min.x && p.x < max.x && p.z > min.z && p.z < max.z);
                if (inRing) _heights.Add(p.y);
            }
            if (_heights.Count >= MinSurfaceSamples) break;
        }
        // Bodies wider than the pool have no surrounding ring; fall back to the whole fluid.
        if (_heights.Count < MinSurfaceSamples)
        {
            _heights.Clear();
            foreach (var particle in particles) _heights.Add(particle.Position.y);
            pad = float.PositiveInfinity;
        }
        if (_heights.Count == 0) return false;
        _heights.Sort();
        float floor = MathF.Max(_heights[0] - settings.ParticleRadius, -settings.Bounds.y);
        float median = _heights[_heights.Count / 2];
        surface = MathF.Min(2f * median - floor, settings.Bounds.y);
        int count = 0;
        foreach (var particle in particles)
        {
            vec3 p = particle.Position;
            if (p.y >= median && p.x >= min.x - pad && p.x <= max.x + pad && p.z >= min.z - pad && p.z <= max.z + pad)
            {
                flow += particle.Velocity;
                count++;
            }
        }
        if (count > 0) flow /= count;
        return true;
    }

    private FluidCollider ColliderFor(RigidBodyFluid fluid)
    {
        if (!_fluidColliders.TryGetValue(fluid, out var collider))
            _fluidColliders.Add(fluid, collider = new FluidCollider(this, fluid));
        return collider;
    }

    /// <summary>Scene triangles plus posed Mesh-shape bodies, in fluid-local space.</summary>
    private IReadOnlyList<ClothTriangle>? FluidTriangles(RigidBodyFluid fluid)
    {
        if (!_simBodies.Any(body => body.MeshTriangles.Length > 0)) return fluid.SceneColliders;
        var triangles = new List<ClothTriangle>(fluid.SceneColliders ?? []);
        mat4 inverse = fluid.World.Inverse;
        foreach (var body in _simBodies)
        {
            if (body.MeshTriangles.Length == 0) continue;
            mat4 local = inverse * Compose(_bodies.GetPosition(body.Id), _bodies.GetRotation(body.Id));
            vec3 Point(int index) => (local * new vec4(FromNumerics(body.MeshVertices[index]), 1)).xyz;
            for (int i = 0; i + 2 < body.MeshTriangles.Length; i += 3)
                triangles.Add(new ClothTriangle(Point(body.MeshTriangles[i]), Point(body.MeshTriangles[i + 1]),
                    Point(body.MeshTriangles[i + 2])));
        }
        return triangles;
    }

    /// <summary>
    /// Pushes fluid particles out of every convex body using exact signed distances in body-local space, so even
    /// particles that end up deep inside a fast body exit along the shortest path. Jolt's NarrowPhaseQuery.CollideShape
    /// is not used: in JoltPhysicsSharp 2.24 it ignores the query transform.
    /// </summary>
    private sealed class FluidCollider(RigidBodySimulation owner, RigidBodyFluid fluid) : IFluidBodyCollider
    {
        private readonly List<(BodySolid Solid, Vector3 Position, Quaternion Rotation, Quaternion Inverse,
            Vector3 Min, Vector3 Max)> _bodies = [];
        private mat4 _world, _inverse;
        private float _worldRadiusScale = 1f;

        public void Refresh()
        {
            _world = fluid.World;
            _inverse = _world.Inverse;
            _worldRadiusScale = ((_world * new vec4(1, 0, 0, 0)).xyz.Length + (_world * new vec4(0, 1, 0, 0)).xyz.Length +
                (_world * new vec4(0, 0, 1, 0)).xyz.Length) / 3f;
            float pad = fluid.Settings.ParticleRadius * _worldRadiusScale;
            _bodies.Clear();
            foreach (var body in owner._simBodies)
            {
                if (body.Solid == null) continue;
                var box = body.Handle.WorldSpaceBounds;
                var rotation = owner._bodies.GetRotation(body.Id);
                _bodies.Add((body.Solid, owner._bodies.GetPosition(body.Id), rotation, Quaternion.Conjugate(rotation),
                    box.Min - new Vector3(pad), box.Max + new Vector3(pad)));
            }
        }

        public void Collide(ref vec3 position, float radius)
        {
            if (_bodies.Count == 0) return;
            Vector3 p = ToNumerics((_world * new vec4(position, 1)).xyz);
            float worldRadius = radius * _worldRadiusScale;
            bool moved = false;
            foreach (var body in _bodies)
            {
                if (p.X < body.Min.X || p.Y < body.Min.Y || p.Z < body.Min.Z ||
                    p.X > body.Max.X || p.Y > body.Max.Y || p.Z > body.Max.Z) continue;
                Vector3 local = Vector3.Transform(p - body.Position, body.Inverse);
                float distance = body.Solid.Distance(local, out Vector3 normal);
                if (distance >= worldRadius) continue;
                Vector3 exit = local + normal * (worldRadius - distance);
                Vector3 wall = DomainViolation(body.Position + Vector3.Transform(exit, body.Rotation), radius);
                if (wall != Vector3.Zero)
                    exit = SlideAlongWall(body, local, normal, Vector3.Transform(wall, body.Inverse), worldRadius);
                p = body.Position + Vector3.Transform(exit, body.Rotation);
                moved = true;
            }
            if (moved) position = (_inverse * new vec4(FromNumerics(p), 1)).xyz;
        }

        /// <summary>World-space outward normal(s) of fluid domain walls the point would cross, or zero.</summary>
        private Vector3 DomainViolation(Vector3 world, float radius)
        {
            vec3 local = (_inverse * new vec4(FromNumerics(world), 1)).xyz;
            vec3 limit = fluid.Settings.Bounds - new vec3(radius * 0.999f);
            vec3 outward = new(local.x > limit.x ? 1 : local.x < -limit.x ? -1 : 0,
                local.y > limit.y ? 1 : local.y < -limit.y ? -1 : 0, local.z > limit.z ? 1 : local.z < -limit.z ? -1 : 0);
            if (outward == vec3.Zero) return Vector3.Zero;
            // Fluid transforms are rotation and axis scale, so transformed axes keep wall normal directions.
            return Vector3.Normalize(ToNumerics((_world * new vec4(outward, 0)).xyz));
        }

        /// <summary>
        /// Water trapped between a body and a domain wall (e.g. under a sunken body) cannot exit through the wall,
        /// so march it out along the wall instead of letting the domain clamp push it back inside every step.
        /// </summary>
        private static Vector3 SlideAlongWall(
            (BodySolid Solid, Vector3 Position, Quaternion Rotation, Quaternion Inverse, Vector3 Min, Vector3 Max) body,
            Vector3 local, Vector3 normal, Vector3 wall, float radius)
        {
            Vector3 direction = normal - Vector3.Dot(normal, wall) * wall;
            if (direction.LengthSquared() < 1e-8f) direction = local - Vector3.Dot(local, wall) * wall;
            if (direction.LengthSquared() < 1e-8f)
                direction = Vector3.Cross(wall, MathF.Abs(wall.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ);
            direction = Vector3.Normalize(direction);
            Vector3 point = local;
            for (int i = 0; i < 32; i++)
            {
                float distance = body.Solid.Distance(point, out _);
                if (distance >= radius) break;
                point += direction * MathF.Max(radius - distance, radius * 0.25f);
            }
            return point;
        }
    }
    internal static Quaternion RotationOf(mat4 m)
    {
        Vector3 x = new(m.m00, m.m01, m.m02), y = new(m.m10, m.m11, m.m12);
        if (x.LengthSquared() < 1e-20f || y.LengthSquared() < 1e-20f) return Quaternion.Identity;
        x = Vector3.Normalize(x);
        y -= Vector3.Dot(y, x) * x;
        if (y.LengthSquared() < 1e-20f) return Quaternion.Identity;
        y = Vector3.Normalize(y);
        Vector3 z = Vector3.Cross(x, y);
        // System.Numerics stores basis images in rows.
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
    }

    internal static mat4 Compose(Vector3 position, Quaternion rotation)
    {
        Vector3 x = Vector3.Transform(Vector3.UnitX, rotation), y = Vector3.Transform(Vector3.UnitY, rotation),
            z = Vector3.Transform(Vector3.UnitZ, rotation);
        return new mat4(new vec4(x.X, x.Y, x.Z, 0), new vec4(y.X, y.Y, y.Z, 0), new vec4(z.X, z.Y, z.Z, 0),
            new vec4(position.X, position.Y, position.Z, 1));
    }

    private static Vector3 ToNumerics(vec3 v) => new(v.x, v.y, v.z);
    private static vec3 FromNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
    private static bool Finite(vec3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    internal static bool Finite(mat4 m)
    {
        for (int i = 0; i < 16; i++)
            if (!float.IsFinite(m[i])) return false;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fluidColliders.Clear();
        _system?.Dispose();
        _jobs?.Dispose();
        for (int i = _disposables.Count - 1; i >= 0; i--) _disposables[i].Dispose();
        _disposables.Clear();
    }
}
