using GlmSharp;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class SceneTree
{
    internal IReadOnlyList<SceneObject> ApplyArrays(SceneObject source)
    {
        if (Viewport.IsPhysicsBaking)
            throw new InvalidOperationException(Localization.T("text.modifier.apply_baking"));

        using var uncached = SceneObject.SuspendRenderEvaluation();
        var evaluation = new ModifierEvaluation(Viewport.SceneObjects);
        if (source is BoneSceneObject || source.GetCombinedGeometryOwner() != source)
            throw new InvalidOperationException(Localization.T("text.modifier.apply_inherited"));
        if (source.InheritModifiers && source.Parent is { } parent)
        {
            var inherited = evaluation.GetDeltas(parent);
            if (inherited.Count != 1 || !SceneObject.ArrayMatrixNear(inherited[0], mat4.Identity))
                throw new InvalidOperationException(Localization.T("text.modifier.apply_inherited"));
        }

        ValidateHierarchy(source, isRoot: true);
        var deltas = evaluation.GetDeltas(source);
        if (deltas.Count < 2)
            throw new InvalidOperationException(Localization.T("text.modifier.apply_no_copies"));

        var sourceWorld = source.GetWorldMatrixNoPivot();
        var arrayIds = source.Modifiers.Where(m => m.Type == SceneModifierType.Array).Select(m => m.Id).ToHashSet();
        var firstWorld = deltas[0] * sourceWorld;
        bool moveSource = !SceneObject.ArrayMatrixNear(firstWorld, sourceWorld);
        var copies = new List<SceneObject>(deltas.Count - 1);
        SceneObject? firstPose = null;
        bool applied = false;
        try
        {
            for (int i = moveSource ? 0 : 1; i < deltas.Count; i++)
            {
                var target = deltas[i] * sourceWorld;
                if (!source.TryGetArrayPlacement(target, out var position, out var rotation, out var scale))
                    throw UnrepresentablePose();
                var ids = new Dictionary<string, string>(StringComparer.Ordinal);
                var duplicate = DuplicateHierarchy(source, ids) ??
                    throw new InvalidOperationException(Localization.T("text.modifier.apply_hierarchy"));
                if (i == 0) firstPose = duplicate;
                else copies.Add(duplicate);
                if (source.Parent != null) source.Parent.AddChild(duplicate);
                if (i != 0 && source.Parent == null) Viewport.SceneObjects.Add(duplicate);
                RemapDuplicateReferences(duplicate, ids);
                SnapshotHierarchy(source, duplicate);
                RemoveArrays(duplicate, arrayIds);
                duplicate.SetLocalPosition(position);
                if (rotation != source.Rotation) duplicate.SetLocalRotation(rotation);
                duplicate.SetLocalScale(scale);
                CheckPlacement(source, duplicate, deltas[i]);
            }

            if (firstPose != null)
            {
                source.CopyLocalPoseFrom(firstPose);
                RemoveTransformKeys(source);
            }
            RemoveArrays(source, arrayIds);
            applied = true;
            ProjectManager.Instance.SetDirty(true);
            return copies;
        }
        finally
        {
            if (firstPose != null) Discard(firstPose);
            if (!applied)
                foreach (var duplicate in copies) Discard(duplicate);
        }
    }

    private static void ValidateHierarchy(SceneObject node, bool isRoot)
    {
        if (node.IsRuntimeTransient) return;
        if (node.Modifiers.Any(m => m.RigidBodyBake != null || m.ClothBake != null) ||
            node is BoneSceneObject { EvaluatedWiggleWorld: not null })
            throw new InvalidOperationException(Localization.T("text.modifier.apply_baked"));
        if (node.IsFollowingPath || node.CopyConstraints.Any(c => c.Enabled) ||
            node.Modifiers.Any(m => m.Enabled && (m.Type == SceneModifierType.Shake ||
                m.Type == SceneModifierType.FollowPath || !isRoot && m.Type == SceneModifierType.Array)) ||
            (!isRoot && !node.InheritModifiers && node.GetCombinedGeometryOwner() == node))
            throw new InvalidOperationException(Localization.T("text.modifier.apply_hierarchy"));
        foreach (var child in node.Children) ValidateHierarchy(child, isRoot: false);
    }

    private static void SnapshotHierarchy(SceneObject source, SceneObject copy)
    {
        copy.CopyLocalPoseFrom(source);
        RemoveTransformKeys(copy);
        VisitCopyChildren(source, copy, SnapshotHierarchy);
    }

    private static void CheckPlacement(SceneObject source, SceneObject copy, mat4 delta)
    {
        if (!SceneObject.ArrayMatrixNear(delta * source.GetWorldMatrix(), copy.GetWorldMatrix()))
            throw UnrepresentablePose();
        VisitCopyChildren(source, copy, (child, duplicate) => CheckPlacement(child, duplicate, delta));
    }

    private static void VisitCopyChildren(SceneObject source, SceneObject copy, Action<SceneObject, SceneObject> visit)
    {
        var originals = source.Children.Where(child => !child.IsRuntimeTransient).ToArray();
        var duplicates = copy.Children.Where(child => !child.IsRuntimeTransient).ToArray();
        if (originals.Length != duplicates.Length)
            throw new InvalidOperationException(Localization.T("text.modifier.apply_hierarchy"));
        for (int i = 0; i < originals.Length; i++)
            visit(originals[i], duplicates[i]);
    }

    private static void RemoveArrays(SceneObject node, HashSet<string> ids)
    {
        node.Modifiers.RemoveAll(m => ids.Contains(m.Id));
        foreach (string key in node.Keyframes.Keys.Where(key => ids.Any(id =>
                     key.StartsWith($"modifier.{id}.", StringComparison.Ordinal))).ToArray())
            node.Keyframes.Remove(key);
    }

    private static void RemoveTransformKeys(SceneObject node)
    {
        foreach (string key in node.Keyframes.Keys.Where(key =>
                     key.StartsWith("position.", StringComparison.Ordinal) ||
                     key.StartsWith("rotation.", StringComparison.Ordinal) ||
                     key.StartsWith("mirotation.", StringComparison.Ordinal) ||
                     key.StartsWith("scale.", StringComparison.Ordinal) ||
                     key.StartsWith("pivot.", StringComparison.Ordinal)).ToArray())
            node.Keyframes.Remove(key);
    }

    private void Discard(SceneObject copy)
    {
        if (copy.Parent != null) copy.Parent.RemoveChild(copy);
        else Viewport.SceneObjects.Remove(copy);
        DisposeMeshes(copy);

        static void DisposeMeshes(SceneObject node)
        {
            foreach (var child in node.Children) DisposeMeshes(child);
            foreach (var mesh in node.Visuals) mesh.Dispose();
        }
    }

    private static InvalidOperationException UnrepresentablePose() =>
        new(Localization.T("text.modifier.apply_transform"));
}
