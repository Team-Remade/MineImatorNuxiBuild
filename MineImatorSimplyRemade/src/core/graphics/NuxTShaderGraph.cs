using System.Numerics;
using MineImatorSimplyRemade.core.mdl.material;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// A custom shader graph attached to a path tracer material, with the scene data it reads: the object's bounds,
/// the evaluation time and the world-space polylines of the paths its Path nodes follow. Two instances are equal
/// when they would render identically, so unchanged scenes keep matching fingerprints.
/// </summary>
public sealed class NuxTShaderGraph : IEquatable<NuxTShaderGraph>
{
    /// <summary>Points in every resampled path polyline, evenly spaced along its length.</summary>
    public const int PathSamples = 64;

    internal NuxTShaderGraph(ShaderGraphProgram program, Vector3 boundsMin, Vector3 boundsSize, float time, float frame,
        Vector3[]?[] paths)
    {
        Program = program;
        BoundsMin = boundsMin;
        BoundsSize = boundsSize;
        Time = time;
        Frame = frame;
        Paths = paths;
    }

    internal ShaderGraphProgram Program { get; }
    /// <summary>The graph ends in a Fog Volume Output: the mesh is a medium, not a surface.</summary>
    public bool IsVolume => Program.Volume;
    /// <summary>The surface may be partially transparent, so shadow rays must evaluate its alpha.</summary>
    public bool Translucent => Program.Translucent;
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsSize { get; }
    public float Time { get; }
    public float Frame { get; }
    /// <summary>One polyline per path slot of the graph; null when the path is missing from the scene.</summary>
    public IReadOnlyList<Vector3[]?> Paths { get; }

    /// <summary>
    /// Prepares a graph for tracing, or returns null when it is disabled or invalid. <paramref name="resolvePath"/>
    /// maps a Path node's saved scene object id to the world-space points of that path, or null if it is missing.
    /// </summary>
    public static NuxTShaderGraph? Create(ShaderGraph? graph, Vector3 boundsMin, Vector3 boundsSize, float time,
        float frame, Func<string, IReadOnlyList<Vector3>?> resolvePath)
    {
        if (graph is not { Enabled: true } || graph.Program is not { Error: null } program)
            return null;
        var paths = new Vector3[]?[program.PathTargets.Length];
        for (int i = 0; i < paths.Length; i++)
            if (resolvePath(program.PathTargets[i]) is { Count: > 0 } points)
                paths[i] = ResamplePath(points);
        return new NuxTShaderGraph(program, boundsMin, boundsSize, time, frame, paths);
    }

    /// <summary>Resamples a polyline to <see cref="PathSamples"/> points evenly spaced by arc length.</summary>
    public static Vector3[] ResamplePath(IReadOnlyList<Vector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
            throw new ArgumentException("A path needs at least one point.", nameof(points));
        var result = new Vector3[PathSamples];
        var lengths = new float[points.Count];
        for (int i = 1; i < points.Count; i++)
            lengths[i] = lengths[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        float total = lengths[^1];
        int segment = 0;
        for (int i = 0; i < PathSamples; i++)
        {
            float target = total * i / (PathSamples - 1);
            while (segment < points.Count - 2 && lengths[segment + 1] < target)
                segment++;
            if (points.Count == 1 || total <= 1e-9f)
            {
                result[i] = points[0];
                continue;
            }
            float span = lengths[segment + 1] - lengths[segment];
            float t = span > 1e-9f ? Math.Clamp((target - lengths[segment]) / span, 0f, 1f) : 0f;
            result[i] = Vector3.Lerp(points[segment], points[segment + 1], t);
        }
        return result;
    }

    public bool Equals(NuxTShaderGraph? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || !ReferenceEquals(Program, other.Program) || BoundsMin != other.BoundsMin ||
            BoundsSize != other.BoundsSize || !Time.Equals(other.Time) || !Frame.Equals(other.Frame) ||
            Paths.Count != other.Paths.Count)
            return false;
        for (int i = 0; i < Paths.Count; i++)
        {
            Vector3[]? a = Paths[i], b = other.Paths[i];
            if (a is null != b is null || a is not null && !a.AsSpan().SequenceEqual(b))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as NuxTShaderGraph);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Program));
        hash.Add(BoundsMin);
        hash.Add(BoundsSize);
        hash.Add(Time);
        hash.Add(Frame);
        foreach (Vector3[]? path in Paths)
        {
            if (path is null)
            {
                hash.Add(0);
                continue;
            }
            foreach (Vector3 point in path)
                hash.Add(point);
        }
        return hash.ToHashCode();
    }
}
