using GlmSharp;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public enum EffectPathShape { Line, Ring, Helix }

public sealed record ProceduralEffectSettings
{
    public EffectPathShape Shape { get; init; }
    public int Seed { get; init; } = 1;
    public int Segments { get; init; } = 64;
    public int Sides { get; init; } = 6;
    public float EndX { get; init; }
    public float EndY { get; init; } = 3f;
    public float EndZ { get; init; }
    public float Radius { get; init; } = 1f;
    public float Turns { get; init; } = 3f;
    public float Width { get; init; } = 0.035f;
    public float Taper { get; init; } = 0.7f;
    public float Noise { get; init; } = 0.35f;
    public int NoiseOctaves { get; init; } = 4;
    public int Branches { get; init; } = 5;
    public float BranchLength { get; init; } = 0.35f;
    public float RefreshRate { get; init; } = 12f;
    public bool SmoothMotion { get; init; }
    public float TimeOffset { get; init; }
    public float Reveal { get; init; } = 1f;

    public static ProceduralEffectSettings Lightning => new();
    public static ProceduralEffectSettings Portal => new()
    {
        Shape = EffectPathShape.Ring, Radius = 1.5f, Width = 0.055f,
        Noise = 0.08f, Branches = 0, Taper = 0f, SmoothMotion = true, RefreshRate = 3f
    };

    public sealed record Control(string Key, string Label, float Min, float Max, float Step,
        bool Discrete, Func<ProceduralEffectSettings, float> Read,
        Func<ProceduralEffectSettings, float, ProceduralEffectSettings> Write)
    {
        public string Path => $"effect.{Key}";
        public ProceduralEffectSettings Set(ProceduralEffectSettings settings, float value)
        {
            if (!float.IsFinite(value))
                throw new InvalidDataException($"Procedural effect '{Label}' must be finite.");
            value = Math.Clamp(value, Min, Max);
            return Write(settings, Discrete ? MathF.Round(value) : value);
        }
    }

    public static readonly IReadOnlyList<Control> Controls = new Control[]
    {
        new("shape", "Path Shape", 0, 2, 1, true, s => (int)s.Shape, (s, v) => s with { Shape = (EffectPathShape)(int)v }),
        new("seed", "Seed", 0, 1000000, 1, true, s => s.Seed, (s, v) => s with { Seed = (int)v }),
        new("segments", "Path Segments", 8, 128, 1, true, s => s.Segments, (s, v) => s with { Segments = (int)v }),
        new("sides", "Tube Sides", 3, 12, 1, true, s => s.Sides, (s, v) => s with { Sides = (int)v }),
        new("end_x", "End X", -10000, 10000, 0.01f, false, s => s.EndX, (s, v) => s with { EndX = v }),
        new("end_y", "End Y", -10000, 10000, 0.01f, false, s => s.EndY, (s, v) => s with { EndY = v }),
        new("end_z", "End Z", -10000, 10000, 0.01f, false, s => s.EndZ, (s, v) => s with { EndZ = v }),
        new("radius", "Path Radius", 0.001f, 1000, 0.01f, false, s => s.Radius, (s, v) => s with { Radius = v }),
        new("turns", "Helix Turns", -16, 16, 0.01f, false, s => s.Turns, (s, v) => s with { Turns = v }),
        new("width", "Tube Width", 0.001f, 100, 0.001f, false, s => s.Width, (s, v) => s with { Width = v }),
        new("taper", "End Taper", 0, 1, 0.01f, false, s => s.Taper, (s, v) => s with { Taper = v }),
        new("noise", "Noise Displacement", 0, 1000, 0.01f, false, s => s.Noise, (s, v) => s with { Noise = v }),
        new("octaves", "Noise Detail", 1, 5, 1, true, s => s.NoiseOctaves, (s, v) => s with { NoiseOctaves = (int)v }),
        new("branches", "Branch Count", 0, 16, 1, true, s => s.Branches, (s, v) => s with { Branches = (int)v }),
        new("branch_length", "Branch Length", 0.01f, 1, 0.01f, false, s => s.BranchLength, (s, v) => s with { BranchLength = v }),
        new("refresh_rate", "Refresh Rate", 0, 60, 0.1f, false, s => s.RefreshRate, (s, v) => s with { RefreshRate = v }),
        new("smooth", "Smooth Motion", 0, 1, 1, true, s => s.SmoothMotion ? 1 : 0, (s, v) => s with { SmoothMotion = v >= 0.5f }),
        new("time_offset", "Time Offset", -3600, 3600, 0.01f, false, s => s.TimeOffset, (s, v) => s with { TimeOffset = v }),
        new("reveal", "Path Reveal", 0, 1, 0.01f, false, s => s.Reveal, (s, v) => s with { Reveal = v })
    };

    public ProceduralEffectSettings Validated()
    {
        var result = this;
        foreach (var control in Controls)
        {
            float value = control.Read(result);
            if (!float.IsFinite(value) || value < control.Min || value > control.Max)
                result = control.Set(result, value);
        }
        return result;
    }

    public static Control? FindControl(string path) => path.StartsWith("effect.", StringComparison.Ordinal)
        ? Controls.FirstOrDefault(c => c.Path == path) : null;
}

public sealed record EffectPath(vec3[] Points, float Width, float Taper, bool Closed);

public static class ProceduralEffectGeometry
{
    public static (long Tick, float Blend) SampleTime(ProceduralEffectSettings settings, float seconds)
    {
        if (!float.IsFinite(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (settings.Noise == 0f && settings.Branches == 0)
            return (0, 0);
        double phase = ((double)seconds + settings.TimeOffset) * settings.RefreshRate;
        long tick = checked((long)Math.Floor(phase));
        float blend = settings.SmoothMotion ? (float)(phase - tick) : 0f;
        return (tick, blend * blend * (3f - 2f * blend));
    }

    public static IReadOnlyList<EffectPath> GeneratePaths(ProceduralEffectSettings settings, float seconds)
    {
        var s = settings.Validated();
        var sample = SampleTime(s, seconds);
        var paths = new List<EffectPath>(s.Branches + 1);
        if (s.Reveal <= 0f) return paths;
        vec3 end = new(s.EndX, s.EndY, s.EndZ);
        vec3 axis = SafeNormal(end, vec3.UnitY);
        vec3 u = Perpendicular(axis);
        vec3 v = vec3.Cross(axis, u);
        bool ring = s.Shape == EffectPathShape.Ring;
        if (!ring && end.LengthSqr < 1e-12f && s.Shape == EffectPathShape.Line)
            return paths;
        int count = Math.Max(1, (int)MathF.Ceiling(s.Segments * s.Reveal));
        var points = new vec3[count + 1];
        for (int i = 0; i <= count; i++)
        {
            float t = s.Reveal * i / count;
            vec3 p = s.Shape switch
            {
                EffectPathShape.Ring => new vec3(MathF.Cos(t * MathF.Tau), MathF.Sin(t * MathF.Tau), 0) * s.Radius,
                EffectPathShape.Helix => end * t +
                    (u * MathF.Cos(t * MathF.Tau * s.Turns) + v * MathF.Sin(t * MathF.Tau * s.Turns)) * s.Radius,
                _ => end * t
            };
            vec3 noise = FractalNoise(t, 0, s, sample, ring);
            float envelope = ring ? 1f : MathF.Sin(t * MathF.PI);
            points[i] = p + noise * (s.Noise * envelope);
        }
        // Endpoints and the closed seam are exact, even with animated noise.
        if (!ring && s.Shape == EffectPathShape.Line)
        {
            points[0] = vec3.Zero;
            if (s.Reveal == 1f) points[^1] = end;
        }
        bool closed = ring && s.Reveal == 1f;
        if (closed) points[^1] = points[0];
        paths.Add(new(points, s.Width, ring ? 0f : s.Taper * s.Reveal, closed));

        for (int branch = 0; branch < s.Branches; branch++)
        {
            // Attachment positions are seed-stable, so revealing a path never reshuffles branches.
            float attach = 0.1f + 0.8f * Random01(s.Seed, 0, branch, 7);
            if (attach >= s.Reveal) continue;
            float pointIndex = attach / s.Reveal * count;
            int index = Math.Min((int)pointIndex, count - 1);
            vec3 start = points[index] * (1f - (pointIndex - index)) + points[index + 1] * (pointIndex - index);
            vec3 direction = SafeNormal(points[index + 1] - points[index], axis);
            vec3 side = SafeNormal(AnimatedRandom(branch + 1, s, sample), u);
            vec3 target = start + SafeNormal(direction * 0.4f + side, u) *
                ((ring ? MathF.Tau * s.Radius : MathF.Max(end.Length, s.Radius)) * s.BranchLength);
            float growth = Math.Clamp((s.Reveal - attach) / (1f - attach), 0f, 1f);
            int segments = Math.Max(2, s.Segments / 2);
            var branchPoints = new vec3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float t = growth * i / segments;
                branchPoints[i] = start * (1f - t) + target * t +
                    FractalNoise(t, branch + 1, s, sample, false) * (s.Noise * 0.5f * MathF.Sin(t * MathF.PI));
            }
            branchPoints[0] = start;
            paths.Add(new(branchPoints, s.Width * 0.5f, growth, false));
        }
        return paths;
    }

    public static void Populate(Mesh mesh, ProceduralEffectSettings settings, float seconds)
    {
        var s = settings.Validated();
        mesh.Vertices.Clear();
        mesh.Normals.Clear();
        mesh.TexCoords.Clear();
        mesh.Indices = null;
        foreach (var path in GeneratePaths(s, seconds))
            AddTube(mesh, path, s.Sides);
    }

    private static void AddTube(Mesh mesh, EffectPath path, int sides)
    {
        var points = path.Points;
        int segments = points.Length - 1;
        var tangents = new vec3[points.Length];
        var frames = new vec3[points.Length];
        for (int i = 0; i <= segments; i++)
        {
            vec3 before = points[i == 0 ? (path.Closed ? segments - 1 : 0) : i - 1];
            vec3 after = points[i == segments ? (path.Closed ? 1 : segments) : i + 1];
            tangents[i] = SafeNormal(after - before, i == 0 ? vec3.UnitY : tangents[i - 1]);
            frames[i] = i == 0 ? Perpendicular(tangents[i]) :
                SafeNormal(frames[i - 1] - tangents[i] * vec3.Dot(frames[i - 1], tangents[i]), Perpendicular(tangents[i]));
        }
        // Distribute parallel-transport holonomy so a noisy loop has no twisted seam.
        float twist = path.Closed ? MathF.Atan2(vec3.Dot(vec3.Cross(frames[^1], frames[0]), tangents[0]),
            vec3.Dot(frames[^1], frames[0])) : 0f;
        var positions = new vec3[points.Length * sides];
        var normals = new vec3[positions.Length];
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            vec3 frame = frames[i] * MathF.Cos(twist * t) + vec3.Cross(tangents[i], frames[i]) * MathF.Sin(twist * t);
            vec3 binormal = vec3.Cross(tangents[i], frame);
            float radius = path.Width * 0.5f * (1f - path.Taper * t);
            for (int j = 0; j < sides; j++)
            {
                int k = i * sides + j;
                float angle = MathF.Tau * j / sides;
                normals[k] = frame * MathF.Cos(angle) + binormal * MathF.Sin(angle);
                positions[k] = points[i] + normals[k] * radius;
                if (path.Closed && i == segments)
                {
                    positions[k] = positions[j];
                    normals[k] = normals[j];
                }
            }
        }
        for (int i = 0; i < segments; i++)
        for (int j = 0; j < sides; j++)
        {
            int a = i * sides + j, b = i * sides + (j + 1) % sides;
            int c = a + sides, d = b + sides;
            Vertex(a, (float)j / sides, (float)i / segments);
            Vertex(b, (float)(j + 1) / sides, (float)i / segments);
            Vertex(c, (float)j / sides, (float)(i + 1) / segments);
            Vertex(b, (float)(j + 1) / sides, (float)i / segments);
            Vertex(d, (float)(j + 1) / sides, (float)(i + 1) / segments);
            Vertex(c, (float)j / sides, (float)(i + 1) / segments);
        }
        if (!path.Closed)
        {
            Cap(0, -tangents[0], true);
            Cap(segments, tangents[^1], false);
        }

        void Vertex(int index, float u, float v)
        {
            mesh.Vertices.Add(positions[index]);
            mesh.Normals.Add(normals[index]);
            mesh.TexCoords.Add(new(u, v));
        }
        void Cap(int ring, vec3 normal, bool reverse)
        {
            for (int j = 0; j < sides; j++)
            {
                int a = ring * sides + j, b = ring * sides + (j + 1) % sides;
                mesh.Vertices.Add(points[ring]);
                mesh.Vertices.Add(positions[reverse ? b : a]);
                mesh.Vertices.Add(positions[reverse ? a : b]);
                for (int k = 0; k < 3; k++)
                {
                    mesh.Normals.Add(normal);
                    mesh.TexCoords.Add(new(0.5f, (float)ring / segments));
                }
            }
        }
    }

    private static vec3 FractalNoise(float t, int strand, ProceduralEffectSettings s, (long Tick, float Blend) sample, bool periodic)
    {
        vec3 sum = vec3.Zero;
        float amplitude = 0.5f;
        for (int octave = 0; octave < s.NoiseOctaves; octave++)
        {
            int cells = 4 << octave;
            float x = t * cells;
            int cell = (int)MathF.Floor(x);
            float fraction = x - cell;
            int a = periodic ? cell % cells : cell;
            int b = periodic ? (cell + 1) % cells : cell + 1;
            sum += (AnimatedRandom(strand * 1024 + octave * 128 + a, s, sample) * (1f - fraction) +
                AnimatedRandom(strand * 1024 + octave * 128 + b, s, sample) * fraction) * amplitude;
            amplitude *= 0.5f;
        }
        return sum;
    }

    private static vec3 AnimatedRandom(int index, ProceduralEffectSettings s, (long Tick, float Blend) sample)
    {
        vec3 At(long tick) => new(
            Random01(s.Seed, tick, index, 0) * 2f - 1f,
            Random01(s.Seed, tick, index, 1) * 2f - 1f,
            Random01(s.Seed, tick, index, 2) * 2f - 1f);
        return At(sample.Tick) * (1f - sample.Blend) + At(sample.Tick + 1) * sample.Blend;
    }

    private static float Random01(int seed, long tick, int index, int channel)
    {
        // Explicit integer hashing is stable across runtimes and evaluation order.
        unchecked
        {
            ulong x = (ulong)tick ^ ((ulong)(uint)seed << 32) ^ (uint)(index * 31 + channel);
            x += 0x9e3779b97f4a7c15UL;
            x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
            x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
            x ^= x >> 31;
            return (x >> 40) / 16777216f;
        }
    }

    private static vec3 SafeNormal(vec3 value, vec3 fallback) => value.LengthSqr > 1e-12f ? value.Normalized : fallback;
    private static vec3 Perpendicular(vec3 axis) =>
        SafeNormal(vec3.Cross(axis, MathF.Abs(axis.z) < 0.9f ? vec3.UnitZ : vec3.UnitX), vec3.UnitX);
}
