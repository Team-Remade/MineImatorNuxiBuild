using GlmSharp;
using MineImatorSimplyRemade.core.mdl.meshes;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public sealed class PathPointSceneObject : SceneObject
{
    public float PathPointAngle;
    public float PathPointScale = 1f;

    public PathPointSceneObject()
    {
        ObjectType = "Path Point";
        SpawnCategory = "Path Points";
        PivotOffset = vec3.Zero;
        CastShadow = false;
    }
}

public sealed class PathSceneObject : SceneObject
{
    private PathSettings _settings = new();
    private PathSettings? _sampledSettings;
    private PathControlPoint[] _sampledPoints = [];
    private PathSample[] _samples = [];
    private PathSample[]? _uploadedSamples;

    public PathSettings Settings { get => _settings; set => _settings = value.Validated(); }
    public IReadOnlyList<PathSample> Samples { get { UpdateSamples(); return _samples; } }
    public float Length => Samples.Count == 0 ? 0f : Samples[^1].Distance;

    public PathSceneObject()
    {
        ObjectType = "Path";
        SpawnCategory = "Primitives";
        PivotOffset = vec3.Zero;
        MaterialSettings = new MaterialSettings { DoubleSided = true };
    }

    private void UpdateSamples()
    {
        var points = Children.OfType<PathPointSceneObject>()
            .Select(p => new PathControlPoint(p.Position, p.PathPointAngle, p.PathPointScale)).ToArray();
        if (_sampledSettings == Settings && _sampledPoints.AsSpan().SequenceEqual(points)) return;
        _samples = PathGeometry.Sample(points, Settings);
        _sampledSettings = Settings;
        _sampledPoints = points;
    }

    public void EvaluateGeometry()
    {
        UpdateSamples();
        if (Visuals.Count == 0 || ReferenceEquals(_uploadedSamples, _samples)) return;
        PathGeometry.Populate(Visuals[0], _samples, Settings, Settings.Shape == PathShape.None);
        Visuals[0].Upload();
        _uploadedSamples = _samples;
    }

    internal static void AddFollowerJobs(List<SceneObject> objects, Dictionary<string, SceneObject> ids,
        List<CopyConstraintSolver.EvaluationJob> jobs)
    {
        foreach (var obj in objects)
        {
            obj.SetEvaluatedPathFrame(null);
            if (obj is PathSceneObject or PathPointSceneObject || obj.FollowPathModifier is not { Enabled: true } ||
                string.IsNullOrEmpty(obj.PathFollowObjectId) ||
                !float.IsFinite(obj.PathFollowOffset) ||
                !ids.TryGetValue(obj.PathFollowObjectId, out var target) || target is not PathSceneObject path ||
                IsWithin(path, obj) || path.Samples.Count < 2) continue;
            jobs.Add(new(obj, [obj], new SceneObject[] { path }, () =>
                obj.SetEvaluatedPathFrame(path.GetWorldMatrixNoPivot() *
                    PathGeometry.AtDistance(path.Samples, obj.PathFollowOffset, path.Settings.Closed).Frame), IsPath: true));
        }
    }

    private static bool IsWithin(SceneObject node, SceneObject ancestor)
    {
        var seen = new HashSet<SceneObject>();
        for (SceneObject? current = node; current != null; current = current.Parent)
            if (!seen.Add(current) || ReferenceEquals(current, ancestor)) return true;
        return false;
    }
}
