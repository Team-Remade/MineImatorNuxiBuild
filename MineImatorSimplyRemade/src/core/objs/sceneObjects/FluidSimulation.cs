using GlmSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum FluidRenderMode
{
    Particles,
    Surface
}

public sealed record FluidSettings
{
    public const int MaxParticles = 1024;

    public int ParticleCount { get; init; } = 216;
    public int Seed { get; init; } = 1;
    public float ParticleRadius { get; init; } = 0.065f;
    public float SmoothingRadius { get; init; } = 0.28f;
    public float Gravity { get; init; } = 9.81f;
    public float Viscosity { get; init; } = 0.08f;
    public float Pressure { get; init; } = 0.85f;
    public float Bounce { get; init; } = 0.25f;
    public bool MeshCollisions { get; init; }
    public float CollisionFriction { get; init; } = 0.15f;
    public bool ShowBounds { get; init; } = true;
    public int Iterations { get; init; } = 4;
    public vec3 Bounds { get; init; } = new(1.8f, 1.8f, 1.8f);
    public vec3 InitialVelocity { get; init; } = vec3.Zero;
    public float SimulationSpeed { get; init; } = 1f;
    public FluidRenderMode RenderMode { get; init; } = FluidRenderMode.Surface;
    public int SurfaceResolution { get; init; } = 20;
    public float SurfaceRadius { get; init; } = 0.4f;
    public float SurfaceThreshold { get; init; } = 0.16f;
    /// <summary>Co-simulate with rigid bodies when baking: bodies float/sink and displace particles.</summary>
    public bool RigidBodyInteraction { get; init; } = true;
    /// <summary>Fluid density in kg/m^3 used for rigid body buoyancy (water is 1000).</summary>
    public float Density { get; init; } = 1000f;
    public float LinearDrag { get; init; } = 4f;
    public float AngularDrag { get; init; } = 0.5f;

    public FluidSettings Validated()
    {
        static float Finite(float value, string name)
        {
            if (!float.IsFinite(value)) throw new InvalidDataException($"Fluid {name} must be finite.");
            return value;
        }
        if (ParticleCount is < 1 or > MaxParticles ||
            Seed is < 0 or > 1000000 ||
            Finite(ParticleRadius, "particle radius") is < 0.005f or > 0.5f ||
            Finite(SmoothingRadius, "smoothing radius") is < 0.02f or > 2f ||
            Finite(Gravity, "gravity") is < -100f or > 100f ||
            Finite(Viscosity, "viscosity") is < 0f or > 1f ||
            Finite(Pressure, "pressure") is < 0f or > 2f ||
            Finite(Bounce, "bounce") is < 0f or > 1f ||
            Finite(CollisionFriction, "collision friction") is < 0f or > 1f ||
            Iterations is < 1 or > 12 ||
            !float.IsFinite(Bounds.x) || !float.IsFinite(Bounds.y) || !float.IsFinite(Bounds.z) ||
            Bounds.x is < 0.1f or > 50f || Bounds.y is < 0.1f or > 50f || Bounds.z is < 0.1f or > 50f ||
            !float.IsFinite(InitialVelocity.x) || !float.IsFinite(InitialVelocity.y) || !float.IsFinite(InitialVelocity.z) ||
            InitialVelocity.Length > 100f ||
            Finite(SimulationSpeed, "simulation speed") is < 0f or > 8f ||
            !Enum.IsDefined(RenderMode) ||
            SurfaceResolution is < 8 or > 32 ||
            Finite(SurfaceRadius, "surface radius") is < 0.02f or > 2f ||
            Finite(SurfaceThreshold, "surface threshold") is < 0.01f or > 0.99f ||
            Finite(Density, "density") is < 0f or > 100000f ||
            Finite(LinearDrag, "linear drag") is < 0f or > 10f ||
            Finite(AngularDrag, "angular drag") is < 0f or > 10f)
            throw new InvalidDataException("Fluid settings are outside the supported editor ranges.");
        return this;
    }
}

public readonly record struct FluidParticle(vec3 Position, vec3 Velocity);

/// <summary>Solid bodies that particles cannot enter, evaluated in fluid-local space.</summary>
public interface IFluidBodyCollider
{
    void Collide(ref vec3 position, float radius);
}

public sealed class FluidSimulation
{
    public const float TimeStep = 1f / 60f;
    private readonly FluidSettings _settings;
    private readonly List<FluidParticle> _particles = [];
    private readonly Dictionary<Cell, List<int>> _grid = [];
    private vec3[] _predicted = [];
    private double _simulatedTime;
    private ClothCollisionTree? _collisionTree;

    public IReadOnlyList<FluidParticle> Particles => _particles;
    public float SimulatedTime => (float)_simulatedTime;

    public FluidSimulation(FluidSettings settings)
    {
        _settings = settings.Validated();
        Reset();
    }

    public void Reset()
    {
        _particles.Clear();
        _simulatedTime = 0;
        float spacing = _settings.SmoothingRadius * 0.58f;
        int columns = Math.Max(1, (int)MathF.Floor(_settings.Bounds.x * 2f / spacing));
        int layers = Math.Max(1, (int)MathF.Floor(_settings.Bounds.z * 2f / spacing));
        var random = new Random(_settings.Seed);

        for (int i = 0; i < _settings.ParticleCount; i++)
        {
            int x = i % columns;
            int z = i / columns % layers;
            int y = i / (columns * layers);
            float jitter = spacing * 0.025f;
            vec3 position = new(
                -_settings.Bounds.x + spacing * (x + 0.5f) + (float)(random.NextDouble() * 2 - 1) * jitter,
                -_settings.Bounds.y + spacing * (y + 0.5f) + (float)(random.NextDouble() * 2 - 1) * jitter,
                -_settings.Bounds.z + spacing * (z + 0.5f) + (float)(random.NextDouble() * 2 - 1) * jitter);
            _particles.Add(new FluidParticle(ClampToBounds(position), _settings.InitialVelocity));
        }
        _predicted = new vec3[_particles.Count];
    }

    public void SimulateToTime(float seconds, IReadOnlyList<ClothTriangle>? colliders = null,
        IFluidBodyCollider? bodies = null)
    {
        if (!float.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if ((double)seconds + 0.000001 < _simulatedTime)
            Reset();
        // Coupled rigid body stepping supplies body triangles even when scene-mesh collisions are off.
        if ((_settings.MeshCollisions || bodies != null) && colliders is { Count: > 0 })
        {
            if (_collisionTree == null || _collisionTree.EntryCount != colliders.Count)
                _collisionTree = ClothCollisionTree.ForScene(colliders, []);
            else
                _collisionTree.RefitScene(colliders, null, 0f, 1f);
        }
        else
            _collisionTree = null;
        double target = seconds;
        while (_simulatedTime + TimeStep <= target + 0.000001)
        {
            Step(TimeStep * _settings.SimulationSpeed, bodies);
            _simulatedTime += TimeStep;
        }
    }

    private void Step(float dt, IFluidBodyCollider? bodies)
    {
        if (dt <= 0f) return;
        float h = _settings.SmoothingRadius;

        if (_settings.Viscosity > 0f)
        {
            var positions = _particles.Select(p => p.Position).ToArray();
            RebuildGrid(positions, h);
            var delta = new vec3[_particles.Count];
            for (int i = 0; i < _particles.Count; i++)
                VisitNeighbors(positions, i, h, (j, _, _, distance) =>
                {
                    if (j <= i || distance <= 1e-8f || distance >= h) return;
                    float weight = MathF.Pow(1f - distance / h, 2);
                    vec3 velocityDelta = (_particles[j].Velocity - _particles[i].Velocity) *
                        (_settings.Viscosity * weight * 0.5f);
                    delta[i] += velocityDelta;
                    delta[j] -= velocityDelta;
                });
            for (int i = 0; i < _particles.Count; i++)
                _particles[i] = _particles[i] with { Velocity = _particles[i].Velocity + delta[i] };
        }

        for (int i = 0; i < _particles.Count; i++)
        {
            vec3 velocity = _particles[i].Velocity + new vec3(0f, -_settings.Gravity, 0f) * dt;
            _predicted[i] = _particles[i].Position + velocity * dt;
            _particles[i] = _particles[i] with { Velocity = velocity };
        }

        for (int iteration = 0; iteration < _settings.Iterations; iteration++)
        {
            RebuildGrid(_predicted, h);
            var corrections = new vec3[_particles.Count];
            for (int i = 0; i < _particles.Count; i++)
                VisitNeighbors(_predicted, i, h, (j, _, direction, distance) =>
                {
                    if (j <= i || distance >= h) return;
                    float overlap = (h - distance) / h;
                    vec3 correction = direction * (overlap * overlap * h * _settings.Pressure *
                        (0.5f / _settings.Iterations));
                    corrections[i] -= correction;
                    corrections[j] += correction;
                });

            for (int i = 0; i < _particles.Count; i++)
            {
                _predicted[i] = ClampToBounds(_predicted[i] + corrections[i]);
                if (_collisionTree != null)
                {
                    vec3 position = _predicted[i];
                    vec3 velocity = (position - _particles[i].Position) / dt;
                    _collisionTree.CollideSphere(ref position, _particles[i].Position, ref velocity,
                        _settings.ParticleRadius, _settings.CollisionFriction, _settings.Bounce);
                    _predicted[i] = ClampToBounds(position);
                }
                if (bodies != null)
                {
                    // Position-only push-out: the final velocity is derived from displacement below.
                    vec3 position = _predicted[i];
                    bodies.Collide(ref position, _settings.ParticleRadius);
                    _predicted[i] = ClampToBounds(position);
                }
            }
        }

        for (int i = 0; i < _particles.Count; i++)
        {
            FluidParticle previous = _particles[i];
            vec3 position = ClampToBounds(_predicted[i]);
            vec3 velocity = (position - previous.Position) / dt;
            float minX = -_settings.Bounds.x + _settings.ParticleRadius;
            float maxX = _settings.Bounds.x - _settings.ParticleRadius;
            float minY = -_settings.Bounds.y + _settings.ParticleRadius;
            float maxY = _settings.Bounds.y - _settings.ParticleRadius;
            float minZ = -_settings.Bounds.z + _settings.ParticleRadius;
            float maxZ = _settings.Bounds.z - _settings.ParticleRadius;
            if (_predicted[i].x < minX && velocity.x < 0 || _predicted[i].x > maxX && velocity.x > 0) velocity.x *= -_settings.Bounce;
            if (_predicted[i].y < minY && velocity.y < 0 || _predicted[i].y > maxY && velocity.y > 0) velocity.y *= -_settings.Bounce;
            if (_predicted[i].z < minZ && velocity.z < 0 || _predicted[i].z > maxZ && velocity.z > 0) velocity.z *= -_settings.Bounce;
            _particles[i] = new(position, velocity);
        }
    }

    private void RebuildGrid(vec3[] positions, float cellSize)
    {
        _grid.Clear();
        for (int i = 0; i < positions.Length; i++)
        {
            Cell cell = CellAt(positions[i], cellSize);
            if (!_grid.TryGetValue(cell, out var list))
                _grid[cell] = list = [];
            list.Add(i);
        }
    }

    private void VisitNeighbors(vec3[] positions, int index, float radius,
        Action<int, vec3, vec3, float> visit)
    {
        Cell center = CellAt(positions[index], radius);
        for (int z = -1; z <= 1; z++)
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
            if (_grid.TryGetValue(new Cell(center.X + x, center.Y + y, center.Z + z), out var bucket))
                foreach (int neighbor in bucket)
                {
                    vec3 offset = positions[neighbor] - positions[index];
                    float distance = offset.Length;
                    visit(neighbor, offset, distance > 1e-8f ? offset / distance : vec3.UnitX, distance);
                }
    }

    private vec3 ClampToBounds(vec3 position) => new(
        Math.Clamp(position.x, -_settings.Bounds.x + _settings.ParticleRadius, _settings.Bounds.x - _settings.ParticleRadius),
        Math.Clamp(position.y, -_settings.Bounds.y + _settings.ParticleRadius, _settings.Bounds.y - _settings.ParticleRadius),
        Math.Clamp(position.z, -_settings.Bounds.z + _settings.ParticleRadius, _settings.Bounds.z - _settings.ParticleRadius));

    private static Cell CellAt(vec3 position, float size) =>
        new((int)MathF.Floor(position.x / size), (int)MathF.Floor(position.y / size), (int)MathF.Floor(position.z / size));

    private readonly record struct Cell(int X, int Y, int Z);
}
