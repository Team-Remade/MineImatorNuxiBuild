using GlmSharp;
using MineImatorSimplyRemade.core.mdl.meshes;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public sealed class FluidSceneObject : SceneObject
{
    private FluidSettings _settings = new();
    private FluidSimulation? _simulation;
    private float _evaluatedTime = -1f;
    private long _evaluatedColliderHash;

    public FluidSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value.Validated();
            ResetRuntime();
        }
    }

    public FluidSimulation Simulation => _simulation ??= new FluidSimulation(Settings);
    public IReadOnlyList<FluidParticle> Particles => Simulation.Particles;

    public FluidSceneObject()
    {
        ObjectType = "Fluid Simulation";
        SpawnCategory = "Effects";
        PivotOffset = vec3.Zero;
        CastShadow = false;
        IncludeInAmbientOcclusion = false;
        MaterialSettings = new MaterialSettings
        {
            AlbedoColor = new vec4(0.08f, 0.45f, 0.9f, 0.78f),
            Metallic = 0.05f,
            Roughness = 0.12f,
            PbrEnabled = true,
            DoubleSided = true
        };
    }

    public void ResetRuntime()
    {
        _simulation = null;
        _evaluatedTime = -1f;
        _evaluatedColliderHash = 0;
    }

    public void EvaluateGeometry(float seconds, IReadOnlyList<ClothTriangle>? worldColliders = null)
    {
        if (!float.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Visuals.Count == 0) return;
        float target = MathF.Floor(seconds * 60f) / 60f;
        IReadOnlyList<ClothTriangle>? localColliders = null;
        if (Settings.MeshCollisions && worldColliders is { Count: > 0 })
        {
            mat4 inverse = GetWorldMatrix().Inverse;
            localColliders = worldColliders.Select(triangle => new ClothTriangle(
                (inverse * new vec4(triangle.A, 1)).xyz,
                (inverse * new vec4(triangle.B, 1)).xyz,
                (inverse * new vec4(triangle.C, 1)).xyz)).ToArray();
        }
        long colliderHash = ColliderHash(localColliders);
        if (_evaluatedTime == target && _simulation != null && colliderHash == _evaluatedColliderHash) return;
        if (colliderHash != _evaluatedColliderHash)
            ResetRuntime();
        _evaluatedColliderHash = colliderHash;
        Simulation.SimulateToTime(target, localColliders);
        FluidGeometry.Populate(Visuals[0], Settings, Simulation.Particles);
        Visuals[0].IsTranslucent = Visuals[0].AlphaMode != MaterialAlphaMode.Clip;
        Visuals[0].Upload();
        _evaluatedTime = target;
    }

    private static long ColliderHash(IReadOnlyList<ClothTriangle>? triangles)
    {
        if (triangles is not { Count: > 0 }) return 0;
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (ClothTriangle triangle in triangles)
            {
                Add(triangle.A); Add(triangle.B); Add(triangle.C);
            }
            return (long)hash;

            void Add(vec3 point)
            {
                hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(point.x)) * 1099511628211UL;
                hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(point.y)) * 1099511628211UL;
                hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(point.z)) * 1099511628211UL;
            }
        }
    }
}
