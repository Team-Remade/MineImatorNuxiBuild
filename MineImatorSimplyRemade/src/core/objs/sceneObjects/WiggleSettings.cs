using GlmSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public sealed record WigglePointSettings
{
    public bool Enabled { get; init; }
    public float Mass { get; init; } = 1;
    public float Stiffness { get; init; } = 400;
    public float Damping { get; init; } = 10;
    public float Gravity { get; init; } = 1;
    public float Wind { get; init; } = 1;
    public float Stretch { get; init; }

    public void Validate()
    {
        WiggleSettings.Range(Mass, 0.01f, 1000, "Mass");
        WiggleSettings.Range(Stiffness, 0, 10000, "Stiffness");
        WiggleSettings.Range(Damping, 0, 1000, "Damping");
        WiggleSettings.Range(Gravity, -100, 100, "Gravity");
        WiggleSettings.Range(Wind, -100, 100, "Wind");
        WiggleSettings.Range(Stretch, 0, 1, "Stretch");
    }
}

public sealed record WiggleSettings
{
    public bool Enabled { get; init; }
    public bool Mute { get; init; }
    public bool Freeze { get; init; }
    public bool Connected { get; init; }
    public bool Chain { get; init; }
    public WigglePointSettings Head { get; init; } = new();
    public WigglePointSettings Tail { get; init; } = new() { Enabled = true };
    public WigglePointSettings Bend { get; init; } = new();
    public float TailX { get; init; }
    public float TailY { get; init; } = 1;
    public float TailZ { get; init; }
    public bool Collisions { get; init; }
    public bool MeshCollisions { get; init; }
    public float Radius { get; init; } = 0.05f;
    public float Friction { get; init; } = 0.5f;
    public float Bounce { get; init; }

    internal vec3 TailOffset => new(TailX, TailY, TailZ);
    internal WiggleSettings BakeSettings => this with { Enabled = true, Mute = false, Freeze = false };

    public void Validate()
    {
        if (Head == null || Tail == null || Bend == null) throw new InvalidDataException("Wiggle head/tail/bend settings are missing.");
        Head.Validate();
        Tail.Validate();
        Bend.Validate();
        Range(TailX, -10000, 10000, "Tail X");
        Range(TailY, -10000, 10000, "Tail Y");
        Range(TailZ, -10000, 10000, "Tail Z");
        Range(Radius, 0.001f, 1000, "Collision radius");
        Range(Friction, 0, 1, "Friction");
        Range(Bounce, 0, 1, "Bounce");
    }

    internal static void Range(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"Wiggle {name} must be between {min} and {max}.");
    }
}

public sealed record WiggleRigSettings
{
    public bool Enabled { get; init; } = true;
    public bool Mute { get; init; }
    public bool Freeze { get; init; }
}

public sealed record WiggleSceneSettings
{
    public bool Enabled { get; init; } = true;
    public int Quality { get; init; } = 8;
    public int Preroll { get; init; } = 30;
    public bool Loop { get; init; }
    public float Gravity { get; init; } = -9.81f;

    public void Validate()
    {
        if (Quality is < 1 or > 32 || Preroll is < 0 or > 600)
            throw new InvalidDataException("Wiggle quality must be 1-32 and preroll 0-600 frames.");
        WiggleSettings.Range(Gravity, -1000, 1000, "Scene gravity");
    }
}

public sealed record WiggleColliderSettings
{
    public bool Enabled { get; init; }
    public float Radius { get; init; } = 0.5f;
    public float OffsetX { get; init; }
    public float OffsetY { get; init; }
    public float OffsetZ { get; init; }

    public void Validate()
    {
        WiggleSettings.Range(Radius, 0.001f, 10000, "Collider radius");
        WiggleSettings.Range(OffsetX, -10000, 10000, "Collider offset X");
        WiggleSettings.Range(OffsetY, -10000, 10000, "Collider offset Y");
        WiggleSettings.Range(OffsetZ, -10000, 10000, "Collider offset Z");
    }
}

public sealed class WiggleBake
{
    public const long MaxBytes = 64 * 1024 * 1024;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ulong Configuration { get; set; }
    public float Framerate { get; set; } = 30;
    public WiggleSettings Settings { get; set; } = new();
    public WiggleSceneSettings Scene { get; set; } = new();
    public float[] EndOffset { get; set; } = [];
    public float[][] Frames { get; set; } = [];
    public float[][] BendFrames { get; set; } = [];

    public void Validate()
    {
        if (Settings == null || Scene == null || Frames == null || BendFrames == null || EndOffset == null)
            throw new InvalidDataException("Wiggle bake settings or frames are missing.");
        Settings.Validate();
        Scene.Validate();
        WiggleSettings.Range(Framerate, 1, 1000, "Bake framerate");
        if (EndOffset.Length != 3 || EndOffset.Any(v => !float.IsFinite(v)) ||
            Frames.Length == 0 || ((long)Frames.Length * 16 + (long)BendFrames.Length * 3) * sizeof(float) > MaxBytes)
            throw new InvalidDataException("Wiggle bake has invalid endpoints or exceeds 64 MiB.");
        foreach (var frame in Frames)
            if (frame == null || frame.Length != 16 || frame.Any(v => !float.IsFinite(v)) ||
                !float.IsFinite(Matrix(frame).Determinant) ||
                MathF.Abs(Matrix(frame).Determinant) < 1e-10f)
                throw new InvalidDataException("Wiggle bake contains an invalid bone transform.");
        if ((BendFrames.Length != 0 && BendFrames.Length != Frames.Length) ||
            (Settings.Bend.Enabled && BendFrames.Length != Frames.Length) ||
            BendFrames.Any(frame => frame == null || frame.Length != 3 || frame.Any(v => !float.IsFinite(v))))
            throw new InvalidDataException("Wiggle bake contains invalid or missing bend frames.");
    }

    internal static float[] Values(mat4 m) =>
        [m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13,
         m.m20, m.m21, m.m22, m.m23, m.m30, m.m31, m.m32, m.m33];

    internal static mat4 Matrix(float[] v) => new(
        new vec4(v[0], v[1], v[2], v[3]), new vec4(v[4], v[5], v[6], v[7]),
        new vec4(v[8], v[9], v[10], v[11]), new vec4(v[12], v[13], v[14], v[15]));
}

internal static class WiggleConfiguration
{
    internal static ulong Compute(IEnumerable<SceneObject> objects, WiggleSceneSettings scene)
    {
        var hash = new Hash();
        hash.Add(scene.Quality); hash.Add(scene.Preroll); hash.Add(scene.Gravity); hash.Add(scene.Loop);
        foreach (var obj in objects)
        {
            int depth = 0;
            for (var p = obj.Parent; p != null; p = p.Parent) depth++;
            hash.Add(depth); hash.Add(obj is BoneSceneObject);
            AddRig(ref hash, obj.WiggleRig);
            AddCollider(ref hash, obj.WiggleCollider);
            if (obj is BoneSceneObject bone)
            {
                AddBone(ref hash, bone.Wiggle);
                vec3 end = bone.GetWiggleEndOffset();
                hash.Add(end.x); hash.Add(end.y); hash.Add(end.z);
            }
        }
        return hash.Value;
    }

    internal static ulong AuthoredHash(SceneObject obj)
    {
        var hash = new Hash();
        AddRig(ref hash, obj.WiggleRig);
        hash.Add(obj.WiggleRig.Mute); hash.Add(obj.WiggleRig.Freeze);
        AddCollider(ref hash, obj.WiggleCollider);
        if (obj is BoneSceneObject bone)
        {
            AddBone(ref hash, bone.Wiggle);
            hash.Add(bone.Wiggle.Mute); hash.Add(bone.Wiggle.Freeze);
        }
        return hash.Value;
    }

    private static void AddRig(ref Hash h, WiggleRigSettings r) => h.Add(r.Enabled);
    private static void AddCollider(ref Hash h, WiggleColliderSettings c)
    {
        h.Add(c.Enabled); h.Add(c.Radius); h.Add(c.OffsetX); h.Add(c.OffsetY); h.Add(c.OffsetZ);
    }
    private static void AddBone(ref Hash h, WiggleSettings w)
    {
        h.Add(w.Enabled); h.Add(w.Connected); h.Add(w.Chain);
        h.Add(w.TailX); h.Add(w.TailY); h.Add(w.TailZ);
        h.Add(w.Collisions); h.Add(w.MeshCollisions); h.Add(w.Radius); h.Add(w.Friction); h.Add(w.Bounce);
        AddPoint(ref h, w.Head); AddPoint(ref h, w.Tail); AddPoint(ref h, w.Bend);
    }
    private static void AddPoint(ref Hash h, WigglePointSettings p)
    {
        h.Add(p.Enabled); h.Add(p.Mass); h.Add(p.Stiffness); h.Add(p.Damping);
        h.Add(p.Gravity); h.Add(p.Wind); h.Add(p.Stretch);
    }
    private struct Hash
    {
        internal ulong Value = 14695981039346656037UL;
        public Hash() { }
        internal void Add(bool v) => Add(v ? 1 : 0);
        internal void Add(float v) => Add(BitConverter.SingleToInt32Bits(v));
        internal void Add(int v)
        {
            for (int i = 0; i < 4; i++)
                Value = unchecked((Value ^ (byte)(v >> (i * 8))) * 1099511628211UL);
        }
    }
}

