using MineImatorSimplyRemade.core.diagnostics;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Timeline
{
    internal void SampleClothFrame(int frame, Action sample)
    {
        using var profile = FrameProfiler.Shared.Measure(ProfileStage.ClothTimelineSampling);
        static bool GeometryChannel(string path) => path.StartsWith("position.", StringComparison.Ordinal) ||
            path.StartsWith("rotation.", StringComparison.Ordinal) || path.StartsWith("mirotation.", StringComparison.Ordinal) ||
            path.StartsWith("scale.", StringComparison.Ordinal) || path.StartsWith("pivot.", StringComparison.Ordinal) ||
            path.StartsWith("bone.", StringComparison.Ordinal) || path.StartsWith("constraint.", StringComparison.Ordinal) ||
            path.StartsWith("bend.", StringComparison.Ordinal) || path.StartsWith("copy_constraint.", StringComparison.Ordinal) ||
            path.StartsWith("path_follow.", StringComparison.Ordinal) || path.StartsWith("modifier.", StringComparison.Ordinal) ||
            path.StartsWith("shapekey.", StringComparison.Ordinal) || path == "visible";
        var owners = new Dictionary<string, SceneObject>(StringComparer.Ordinal);
        foreach (var row in _displayRows)
            if (row.Object is { } obj) owners.TryAdd(obj.ObjectId, obj);
        if (Viewport != null)
            foreach (var obj in CollectAllObjects(Viewport.SceneObjects)) owners.TryAdd(obj.ObjectId, obj);
        var saved = new List<(SceneObject Owner, string Path, object Value)>();
        foreach (var pair in _propertyKeyframes)
        {
            int dot = pair.Key.IndexOf('.');
            if (dot < 0 || !GeometryChannel(pair.Key[(dot + 1)..])) continue;
            if (owners.TryGetValue(pair.Key[..dot], out var owner))
            {
                string path = pair.Key[(dot + 1)..];
                saved.Add((owner, path, GetPropertyValue(owner, path)));
            }
        }
        int previous = _currentFrame;
        using var uncached = SceneObject.SuspendRenderEvaluation();
        try
        {
            _currentFrame = frame;
            ApplyKeyframeChannels(GeometryChannel, owners);
            if (Viewport != null) InverseKinematicsSolver.Evaluate(Viewport.SceneObjects);
            sample();
        }
        finally
        {
            _currentFrame = previous;
            foreach (var (owner, path, value) in saved) SetPropertyValue(owner, path, value);
            if (Viewport != null) InverseKinematicsSolver.Evaluate(Viewport.SceneObjects);
        }
    }
}
