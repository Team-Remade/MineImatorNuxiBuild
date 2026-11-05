using GlmSharp;
using System.Text.Json.Nodes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum ParticleEmissionShape
{
    Box = 0,
    Sphere = 1
}

public class ParticleSpawnerSceneObject : SceneObject
{
    private sealed class ParticleInstance
    {
        public SceneObject Root = null!;
        public vec3 Velocity;
        public vec3 AngularVelocity;
        public float Lifetime;
        public float Age;
        public float StartScale;
        public float EndScale;
    }

    private static readonly Random Random = new();

    private readonly List<ParticleInstance> _particles = new();
    private float _spawnAccumulator;
    private bool _oneShotFired;
    private float _simulatedTimeSeconds;
    private Viewport? _runtimeViewport;
    private ParticlePreset? _preset;
    private ParticlePresetSimulation? _presetSimulation;
    private readonly Dictionary<ParticlePresetSimulation.Particle, SceneObject> _presetObjects = new();

    public JsonObject? Preset => _preset?.Document;
    public int Seed = 0;
    public bool Freeze = false;
    public string AttractorObjectId = "";
    public float AttractorForce = 0f;

    public string ParticleLibraryEntryId = "";
    public string ParticleLibraryDisplayName = "";

    public bool Emitting = true;
    public bool OneShot = false;
    public int Amount = 64;
    public float SpawnRate = 20f;

    public float LifetimeMin = 1f;
    public float LifetimeMax = 2f;

    public float SimulationSpeed = 1f;
    public float LinearDamping = 0f;
    public float AngularDamping = 0f;

    public ParticleEmissionShape EmissionShape = ParticleEmissionShape.Box;
    public bool UseDirectionalEmission = false;
    public vec3 Direction = vec3.UnitY;
    public float SpreadDegrees = 35f;
    public float InitialSpeedMin = 1f;
    public float InitialSpeedMax = 2f;

    public vec3 SpawnBoxExtents = new vec3(0.1f, 0.1f, 0.1f);
    public vec3 InitialVelocityMin = new vec3(-0.25f, 0.75f, -0.25f);
    public vec3 InitialVelocityMax = new vec3(0.25f, 1.25f, 0.25f);
    public vec3 Gravity = new vec3(0f, -1.25f, 0f);

    public vec3 InitialRotationMinDegrees = vec3.Zero;
    public vec3 InitialRotationMaxDegrees = new vec3(360f, 360f, 360f);
    public vec3 AngularVelocityMinDegrees = new vec3(-45f, -45f, -45f);
    public vec3 AngularVelocityMaxDegrees = new vec3(45f, 45f, 45f);

    public float StartScaleMin = 0.75f;
    public float StartScaleMax = 1.25f;
    public float EndScaleMin = 0.05f;
    public float EndScaleMax = 0.25f;
    public bool TopLevelParticles = false;

    public int ActiveParticleCount => _presetSimulation?.Particles.Count ?? _particles.Count;

    public vec3 EmissionExtents => _preset == null ? SpawnBoxExtents :
        !ParticlePreset.Flag(_preset.Settings, "spawn_region_use") ? vec3.Zero :
        ParticlePreset.Text(_preset.Settings, "spawn_region_type", "box") switch
        {
            "sphere" => new vec3(ParticlePreset.Number(_preset.Settings, "spawn_region_sphere_radius") / 16f),
            "cube" => new vec3(ParticlePreset.Number(_preset.Settings, "spawn_region_cube_size") / 32f),
            _ => ParticlePreset.Vector(_preset.Settings, "spawn_region_box_size") / 32f
        };

    public ParticleEmissionShape EffectiveEmissionShape => _preset == null ? EmissionShape :
        ParticlePreset.Text(_preset.Settings, "spawn_region_type") == "sphere"
            ? ParticleEmissionShape.Sphere : ParticleEmissionShape.Box;

    public void SetPreset(JsonObject? document)
    {
        ParticlePreset? preset = document == null ? null : new ParticlePreset(document);
        ResetRuntime();
        _preset = preset;
    }

    public ParticleSpawnerSceneObject()
    {
        ObjectType = "Particle Spawner";
        SpawnCategory = "Particle Spawners";
        PivotOffset = vec3.Zero;
    }

    public void SetParticleSource(string libraryEntryId, string displayName)
    {
        ParticleLibraryEntryId = libraryEntryId ?? "";
        ParticleLibraryDisplayName = displayName ?? "";
        _preset = null;
        ResetRuntime();
    }

    public void ResetRuntime()
    {
        _spawnAccumulator = 0f;
        _oneShotFired = false;
        _simulatedTimeSeconds = 0f;

        foreach (var particle in _particles)
            RemoveRuntimeNode(particle.Root, _runtimeViewport);

        _particles.Clear();
        foreach (SceneObject obj in _presetObjects.Values)
            RemoveRuntimeNode(obj, _runtimeViewport);
        _presetObjects.Clear();
        _presetSimulation = null;
    }

    public void Step(float deltaTime, Viewport viewport, SpawnMenu spawnMenu)
    {
        if (deltaTime <= 0f || IsRuntimeTransient)
            return;

        _runtimeViewport = viewport;

        float dt = MathF.Min(deltaTime, 0.1f);
        StepInternal(dt, viewport, spawnMenu);
        _simulatedTimeSeconds += dt;
        if (_preset != null) SyncPresetObjects(viewport, spawnMenu);
    }

    public void SimulateToTime(float timelineSeconds, Viewport viewport, SpawnMenu spawnMenu)
    {
        if (IsRuntimeTransient)
            return;

        _runtimeViewport = viewport;

        float target = Math.Max(0f, timelineSeconds);
        if (target + 0.0001f < _simulatedTimeSeconds)
            ResetRuntime();

        float remaining = target - _simulatedTimeSeconds;
        while (remaining > 0.0001f)
        {
            float chunk = MathF.Min(remaining, 1f / 120f);
            StepInternal(chunk, viewport, spawnMenu);
            _simulatedTimeSeconds += chunk;
            remaining -= chunk;
        }

        _simulatedTimeSeconds = target;
        if (_preset != null) SyncPresetObjects(viewport, spawnMenu);
    }

    private void StepInternal(float dt, Viewport viewport, SpawnMenu spawnMenu)
    {
        float simulationSpeed = MathF.Max(0f, SimulationSpeed);
        if (simulationSpeed <= 0f)
            return;

        dt *= simulationSpeed;
        if (_preset != null)
        {
            AdvancePreset(dt, viewport);
            return;
        }

        float minLifetime = MathF.Max(0.01f, MathF.Min(LifetimeMin, LifetimeMax));
        float maxLifetime = MathF.Max(minLifetime, MathF.Max(LifetimeMin, LifetimeMax));
        float linearDampingFactor = MathF.Exp(-MathF.Max(0f, LinearDamping) * dt);
        float angularDampingFactor = MathF.Exp(-MathF.Max(0f, AngularDamping) * dt);

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var particle = _particles[i];
            particle.Age += dt;
            if (particle.Age >= particle.Lifetime)
            {
                RemoveRuntimeNode(particle.Root, _runtimeViewport);
                _particles.RemoveAt(i);
                continue;
            }

            particle.Velocity += Gravity * dt;
            particle.Velocity *= linearDampingFactor;
            particle.AngularVelocity *= angularDampingFactor;

            vec3 localPos = particle.Root.LocalPosition + particle.Velocity * dt;
            vec3 localRot = particle.Root.LocalRotation + particle.AngularVelocity * dt;
            float t = particle.Age / MathF.Max(0.0001f, particle.Lifetime);
            float uniformScale = Lerp(particle.StartScale, particle.EndScale, t);

            particle.Root.SetLocalPosition(localPos);
            particle.Root.SetLocalRotation(localRot);
            particle.Root.SetLocalScale(new vec3(uniformScale, uniformScale, uniformScale));
        }

        if (!Emitting || string.IsNullOrWhiteSpace(ParticleLibraryEntryId))
            return;

        int maxParticles = Math.Clamp(Amount, 1, 10000);
        if (_particles.Count >= maxParticles)
            return;

        if (OneShot)
        {
            if (_oneShotFired)
                return;

            int burstCount = maxParticles - _particles.Count;
            for (int i = 0; i < burstCount; i++)
            {
                if (!TrySpawnParticle(viewport, spawnMenu, minLifetime, maxLifetime))
                    break;
            }

            _oneShotFired = true;
            return;
        }

        float rate = MathF.Max(0f, SpawnRate);
        if (rate <= 0f)
            return;

        _spawnAccumulator += dt * rate;
        int toSpawn = Math.Min((int)_spawnAccumulator, maxParticles - _particles.Count);
        if (toSpawn <= 0)
            return;

        _spawnAccumulator -= toSpawn;

        for (int i = 0; i < toSpawn; i++)
        {
            if (!TrySpawnParticle(viewport, spawnMenu, minLifetime, maxLifetime))
                break;
        }
    }

    private void AdvancePreset(float dt, Viewport viewport)
    {
        if (Freeze) return;
        _presetSimulation ??= new ParticlePresetSimulation(_preset!, Seed);
        SceneObject? attractor = FindSceneObject(viewport.SceneObjects, AttractorObjectId);
        _presetSimulation.AttractorPosition = attractor?.GetWorldPosition();
        _presetSimulation.AttractorForce = AttractorForce;
        mat4 world = GetWorldMatrixNoPivot();
        vec3 scale = new(new vec3(world.m00, world.m01, world.m02).Length,
            new vec3(world.m10, world.m11, world.m12).Length,
            new vec3(world.m20, world.m21, world.m22).Length);
        mat4 rotation = world * mat4.Scale(new vec3(1f / Math.Max(scale.x, 1e-6f),
            1f / Math.Max(scale.y, 1e-6f), 1f / Math.Max(scale.z, 1e-6f)));
        rotation.m30 = rotation.m31 = rotation.m32 = 0;
        _presetSimulation.Advance(dt, Emitting, GetWorldPosition(), rotation);
    }

    private void SyncPresetObjects(Viewport viewport, SpawnMenu spawnMenu)
    {
        if (_presetSimulation == null) return;
        MaterialSettings material = GetEffectiveMaterialSettings();
        var active = _presetSimulation.Particles.ToHashSet();
        foreach (var pair in _presetObjects.ToArray())
            if (!active.Contains(pair.Key))
            {
                RemoveRuntimeNode(pair.Value, viewport);
                _presetObjects.Remove(pair.Key);
            }
        foreach (var particle in _presetSimulation.Particles)
        {
            if (!_presetObjects.TryGetValue(particle, out SceneObject? root))
            {
                root = spawnMenu.CreatePresetParticle(_preset!, particle.TypeIndex);
                MarkRuntimeNode(root);
                viewport.SceneObjects.Add(root);
                _presetObjects.Add(particle, root);
            }
            root.SetLocalPosition(particle.Position);
            mat4 rotation = particle.RotationPrefix * ParticlePresetSimulation.DegreesRotation(particle.Rotation);
            root.SetLocalRotationMatrix(rotation,
                MineImatorSimplyRemade.core.mdl.mineImator.MineImatorLoader.MatrixToEulerRzRyRx(rotation));
            root.SetLocalScale(new vec3(particle.Scale));
            if (root is ParticleSpriteSceneObject sprite)
            {
                sprite.RollRadians = particle.SpriteAngle * MathF.PI / 180f;
                spawnMenu.SetPresetParticleFrame(sprite, particle.Frame);
            }
            ApplyParticleColor(root, particle.Color, Math.Clamp(particle.Alpha, 0, 1), material);
        }
    }

    private static void ApplyParticleColor(SceneObject obj, vec3 color, float alpha, MaterialSettings source)
    {
        obj.SetExplicitMaterialSettings();
        var material = source.Clone();
        material.AlbedoColor *= new vec4(color, alpha);
        material.DoubleSided |= obj is ParticleSpriteSceneObject;
        obj.MaterialSettings = material;
        foreach (SceneObject child in obj.Children)
            ApplyParticleColor(child, color, alpha, source);
    }

    private static SceneObject? FindSceneObject(IEnumerable<SceneObject> objects, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (SceneObject obj in objects)
        {
            if (obj.ObjectId == id) return obj;
            SceneObject? nested = FindSceneObject(obj.Children, id);
            if (nested != null) return nested;
        }
        return null;
    }

    private bool TrySpawnParticle(Viewport viewport, SpawnMenu spawnMenu, float minLifetime, float maxLifetime)
    {
        if (ProjectManager.Instance?.Manifest?.ObjectLibrary == null)
            return false;

        var entry = FindLibraryEntryById(ProjectManager.Instance.Manifest.ObjectLibrary, ParticleLibraryEntryId);
        if (entry == null)
            return false;

        if (string.Equals(entry.SpawnCategory, "Particle Spawners", StringComparison.OrdinalIgnoreCase))
            return false;

        SceneObject? root = ProjectSceneSerializer.SpawnObjectFromEntry(entry, viewport, spawnMenu, TopLevelParticles ? null : this);
        if (root == null)
            return false;

        MarkRuntimeNode(root);

        vec3 spawnPos = BuildSpawnPosition();

        vec3 spawnRot = new vec3(
            DegreesToRadians(RandomRange(InitialRotationMinDegrees.x, InitialRotationMaxDegrees.x)),
            DegreesToRadians(RandomRange(InitialRotationMinDegrees.y, InitialRotationMaxDegrees.y)),
            DegreesToRadians(RandomRange(InitialRotationMinDegrees.z, InitialRotationMaxDegrees.z)));

        float startScale = MathF.Max(0.001f, RandomRange(StartScaleMin, StartScaleMax));

        if (TopLevelParticles)
            root.SetLocalPosition(GetWorldPosition() + spawnPos);
        else
            root.SetLocalPosition(spawnPos);
        root.SetLocalRotation(spawnRot);
        root.SetLocalScale(new vec3(startScale, startScale, startScale));

        vec3 velocity = UseDirectionalEmission
            ? BuildDirectionalVelocity()
            : new vec3(
                RandomRange(InitialVelocityMin.x, InitialVelocityMax.x),
                RandomRange(InitialVelocityMin.y, InitialVelocityMax.y),
                RandomRange(InitialVelocityMin.z, InitialVelocityMax.z));

        _particles.Add(new ParticleInstance
        {
            Root = root,
            Velocity = velocity,
            AngularVelocity = new vec3(
                DegreesToRadians(RandomRange(AngularVelocityMinDegrees.x, AngularVelocityMaxDegrees.x)),
                DegreesToRadians(RandomRange(AngularVelocityMinDegrees.y, AngularVelocityMaxDegrees.y)),
                DegreesToRadians(RandomRange(AngularVelocityMinDegrees.z, AngularVelocityMaxDegrees.z))),
            Lifetime = RandomRange(minLifetime, maxLifetime),
            Age = 0f,
            StartScale = startScale,
            EndScale = MathF.Max(0.001f, RandomRange(EndScaleMin, EndScaleMax))
        });

        return true;
    }

    private vec3 BuildSpawnPosition()
    {
        vec3 extents = new vec3(
            MathF.Max(0f, SpawnBoxExtents.x),
            MathF.Max(0f, SpawnBoxExtents.y),
            MathF.Max(0f, SpawnBoxExtents.z));

        if (EmissionShape == ParticleEmissionShape.Sphere)
        {
            vec3 unit = RandomPointInsideUnitSphere();
            return new vec3(unit.x * extents.x, unit.y * extents.y, unit.z * extents.z);
        }

        return new vec3(
            RandomRange(-extents.x, extents.x),
            RandomRange(-extents.y, extents.y),
            RandomRange(-extents.z, extents.z));
    }

    private vec3 BuildDirectionalVelocity()
    {
        vec3 direction = Direction;
        if (direction.LengthSqr < 1e-8f)
            direction = vec3.UnitY;
        direction = direction.Normalized;

        float spreadRadians = DegreesToRadians(Math.Clamp(SpreadDegrees, 0f, 180f));
        float cosMin = MathF.Cos(spreadRadians);
        float cosTheta = Lerp(1f, cosMin, (float)Random.NextDouble());
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = (float)(Random.NextDouble() * Math.PI * 2.0);

        float lx = MathF.Cos(phi) * sinTheta;
        float ly = MathF.Sin(phi) * sinTheta;
        float lz = cosTheta;

        vec3 basisUp = MathF.Abs(direction.z) > 0.999f ? vec3.UnitX : vec3.UnitZ;
        vec3 tangent = vec3.Cross(basisUp, direction).Normalized;
        vec3 bitangent = vec3.Cross(direction, tangent).Normalized;

        vec3 randomizedDirection = (tangent * lx + bitangent * ly + direction * lz).Normalized;
        float speed = MathF.Max(0f, RandomRange(InitialSpeedMin, InitialSpeedMax));
        return randomizedDirection * speed;
    }

    private static vec3 RandomPointInsideUnitSphere()
    {
        while (true)
        {
            vec3 p = new vec3(
                RandomRange(-1f, 1f),
                RandomRange(-1f, 1f),
                RandomRange(-1f, 1f));

            if (p.LengthSqr <= 1f)
                return p;
        }
    }

    private void MarkRuntimeNode(SceneObject node)
    {
        node.IsRuntimeTransient = true;
        node.RuntimeHiddenParent = this;
        node.IsSelectable = false;
        node.HideInSceneTree = true;

        foreach (var child in node.Children)
            MarkRuntimeNode(child);
    }

    private static void RemoveRuntimeNode(SceneObject node, Viewport? viewport)
    {
        foreach (var child in node.Children.ToList())
            RemoveRuntimeNode(child, viewport);

        foreach (var mesh in node.Visuals.ToList())
        {
            node.RemoveMesh(mesh);
            mesh.Dispose();
        }

        if (node.Parent == null)
            viewport?.SceneObjects.Remove(node);

        node.Parent?.RemoveChild(node);
    }

    private static ProjectSceneObjectEntry? FindLibraryEntryById(IEnumerable<ProjectSceneObjectEntry> nodes, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        foreach (var node in nodes)
        {
            if (string.Equals(node.LibraryEntryId, id, StringComparison.OrdinalIgnoreCase))
                return node;

            var nested = FindLibraryEntryById(node.Children, id);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static float RandomRange(float a, float b)
    {
        if (a > b)
            (a, b) = (b, a);

        return a + (float)Random.NextDouble() * (b - a);
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * (MathF.PI / 180f);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * Math.Clamp(t, 0f, 1f);
    }
}
