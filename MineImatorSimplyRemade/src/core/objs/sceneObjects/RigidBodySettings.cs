using GlmSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum RigidBodyType { Active, Passive }
public enum RigidBodyShape { Box, Sphere, Capsule, Cylinder, ConvexHull, Mesh }

/// <summary>Per-object rigid body settings, mirroring Blender's Physics > Rigid Body panel.</summary>
public sealed record RigidBodySettings
{
    public const int CollectionCount = 16;
    public const int AllCollections = (1 << CollectionCount) - 1;

    public RigidBodyType Type { get; init; } = RigidBodyType.Active;
    /// <summary>Follow authored animation as a kinematic body. Keyframe off on active bodies to hand over to dynamics.</summary>
    public bool Animated { get; init; }
    public RigidBodyShape Shape { get; init; } = RigidBodyShape.ConvexHull;
    public float Mass { get; init; } = 1f;
    public float Friction { get; init; } = 0.5f;
    public float Bounciness { get; init; }
    public float Margin { get; init; } = 0.04f;
    public float LinearDamping { get; init; } = 0.04f;
    public float AngularDamping { get; init; } = 0.1f;
    public bool Deactivation { get; init; }
    public bool ContinuousCollision { get; init; }
    public int CollisionCollections { get; init; } = 1;
    /// <summary>Build the collision shape from child meshes too, except children with their own rigid body.</summary>
    public bool IncludeChildren { get; init; } = true;

    public void Validate()
    {
        static void Range(float value, float min, float max, string name)
        {
            if (!float.IsFinite(value) || value < min || value > max)
                throw new InvalidDataException($"Rigid body {name} must be between {min} and {max}.");
        }
        if (!Enum.IsDefined(Type) || !Enum.IsDefined(Shape))
            throw new InvalidDataException("Rigid body type or collision shape is invalid.");
        Range(Mass, 0.001f, 1000000f, "mass");
        Range(Friction, 0f, 10f, "friction");
        Range(Bounciness, 0f, 1f, "bounciness");
        Range(Margin, 0f, 1f, "collision margin");
        Range(LinearDamping, 0f, 1f, "linear damping");
        Range(AngularDamping, 0f, 1f, "angular damping");
        if (CollisionCollections is <= 0 or > AllCollections)
            throw new InvalidDataException("Rigid bodies must belong to at least one of the 16 collision collections.");
        if (Type == RigidBodyType.Active && Shape == RigidBodyShape.Mesh)
            throw new InvalidDataException("Mesh collision shapes are only supported on passive rigid bodies. Use Convex Hull for active bodies.");
    }
}

/// <summary>Scene-wide simulation settings, mirroring Blender's Rigid Body World panel.</summary>
public sealed record RigidBodyWorldSettings
{
    public float GravityX { get; init; }
    public float GravityY { get; init; } = -9.81f;
    public float GravityZ { get; init; }
    public float Speed { get; init; } = 1f;
    public int Substeps { get; init; } = 10;
    public int SolverIterations { get; init; } = 10;
    public int StartFrame { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public vec3 Gravity => new(GravityX, GravityY, GravityZ);

    public void Validate()
    {
        if (!float.IsFinite(GravityX) || !float.IsFinite(GravityY) || !float.IsFinite(GravityZ) ||
            MathF.Abs(GravityX) > 1000 || MathF.Abs(GravityY) > 1000 || MathF.Abs(GravityZ) > 1000 ||
            !float.IsFinite(Speed) || Speed is < 0f or > 100f || Substeps is < 1 or > 100 ||
            SolverIterations is < 1 or > 100 || StartFrame is < 0 or > 1000000)
            throw new InvalidDataException("Rigid body world settings are outside the supported ranges.");
    }
}

/// <summary>Saved world transforms (without pivot) for one simulated object, indexed from <see cref="StartFrame"/>.</summary>
public sealed class RigidBodyBake
{
    public const long MaxBytes = 128L * 1024 * 1024;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public float Framerate { get; init; }
    public int StartFrame { get; init; }
    public RigidBodySettings Settings { get; init; } = new();
    public RigidBodyWorldSettings World { get; init; } = new();
    public float[][] Frames { get; init; } = [];

    public void Validate()
    {
        if (!Guid.TryParseExact(Id, "N", out _) || !float.IsFinite(Framerate) || Framerate is < 1 or > 120 ||
            StartFrame < 0 || Settings == null || World == null || Frames == null || Frames.Length == 0 ||
            (long)Frames.Length * 16 * sizeof(float) > MaxBytes)
            throw new InvalidDataException("Invalid saved rigid body bake.");
        Settings.Validate();
        World.Validate();
        foreach (var frame in Frames)
            if (frame == null || frame.Length != 16 || frame.Any(v => !float.IsFinite(v)) ||
                MathF.Abs(WiggleBake.Matrix(frame).Determinant) < 1e-12f)
                throw new InvalidDataException("Saved rigid body bake contains an invalid transform.");
    }

    /// <summary>True when the bake still matches the authored settings it was simulated with.</summary>
    public bool Matches(RigidBodySettings settings, RigidBodyWorldSettings world, float framerate) =>
        Framerate == framerate && World == world &&
        // Animated may be keyframed for handoff, so its current timeline value does not invalidate the bake.
        Settings == settings with { Animated = Settings.Animated };

    /// <summary>Returns the baked transform, holding the last frame after the baked range.</summary>
    public mat4? GetWorld(int frame)
    {
        if (frame < StartFrame) return null;
        return WiggleBake.Matrix(Frames[Math.Min(frame - StartFrame, Frames.Length - 1)]);
    }
}

/// <summary>Saved fluid particle positions (fluid-local space) from a coupled rigid body bake.</summary>
public sealed class FluidBake
{
    public const long MaxBytes = 256L * 1024 * 1024;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public float Framerate { get; init; }
    public FluidSettings Settings { get; init; } = new();
    public float[][] Frames { get; init; } = [];

    public void Validate()
    {
        if (!Guid.TryParseExact(Id, "N", out _) || !float.IsFinite(Framerate) || Framerate is < 1 or > 120 ||
            Settings == null || Frames == null || Frames.Length == 0)
            throw new InvalidDataException("Invalid saved fluid bake.");
        Settings.Validated();
        int length = Settings.ParticleCount * 3;
        if ((long)length * sizeof(float) * Frames.Length > MaxBytes)
            throw new InvalidDataException("Saved fluid bake exceeds the supported size.");
        foreach (var frame in Frames)
            if (frame == null || frame.Length != length || frame.Any(v => !float.IsFinite(v)))
                throw new InvalidDataException("Saved fluid bake contains invalid particle positions.");
    }

    public FluidParticle[] GetParticles(int frame)
    {
        var data = Frames[Math.Clamp(frame, 0, Frames.Length - 1)];
        var particles = new FluidParticle[data.Length / 3];
        for (int i = 0; i < particles.Length; i++)
            particles[i] = new(new vec3(data[i * 3], data[i * 3 + 1], data[i * 3 + 2]), vec3.Zero);
        return particles;
    }
}
