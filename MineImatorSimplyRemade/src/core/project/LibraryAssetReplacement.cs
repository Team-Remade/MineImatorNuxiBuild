using GlmSharp;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.project;

internal sealed class LibraryAssetReplacement
{
    public SceneObject Source { get; }
    public SceneObject Replacement { get; }
    public IReadOnlyDictionary<SceneObject, SceneObject> Matches => _matches;
    public int RetainedPartCount { get; }

    private readonly Dictionary<SceneObject, SceneObject> _matches = new();
    private readonly SceneObject[] _sourceNodes;

    private LibraryAssetReplacement(SceneObject source, SceneObject replacement)
    {
        Source = source;
        Replacement = replacement;
        _sourceNodes = Nodes(source).ToArray();
        _matches.Add(source, replacement);
        var available = Nodes(replacement).Skip(1).ToHashSet();

        foreach (var old in _sourceNodes.Skip(1))
        {
            SceneObject? match = Unique(available.Where(candidate =>
                !string.IsNullOrEmpty(old.LibrarySourceId) &&
                candidate.LibrarySourceId == old.LibrarySourceId));
            if (match == null && old.Parent != null && _matches.TryGetValue(old.Parent, out var parent))
                match = FindNamed(old, available.Where(candidate => candidate.Parent == parent));
            match ??= FindNamed(old, available);
            if (match == null)
                continue;
            _matches.Add(old, match);
            available.Remove(match);
        }

        RetainedPartCount = _sourceNodes.Count(node => !_matches.ContainsKey(node));
    }

    public static LibraryAssetReplacement Prepare(SceneObject source, SceneObject replacement,
        PropertiesPanel? propertiesPanel = null, bool useReplacementLibraryIds = false)
    {
        var result = new LibraryAssetReplacement(source, replacement);
        var ids = result._matches.ToDictionary(pair => pair.Value.ObjectId, pair => pair.Key.ObjectId);
        var states = new List<(SceneObject, ProjectSceneObjectEntry)>();
        foreach (var (old, target) in result._matches)
        {
            var state = ProjectSceneSerializer.CaptureObjectState(old);
            var asset = ProjectSceneSerializer.CaptureObjectState(target);

            // Construction belongs to the new asset; authored values belong to the instance.
            state.ObjectType = asset.ObjectType;
            state.SpawnCategory = asset.SpawnCategory;
            state.BlockVariant = asset.BlockVariant;
            state.TextureType = asset.TextureType;
            state.ResourcePackId = asset.ResourcePackId;
            state.SourceAssetPath = asset.SourceAssetPath;
            state.SourceNodePath = asset.SourceNodePath;
            state.TextureOverridePath = asset.TextureOverridePath;
            state.RestoreExactChildren = true;
            if (useReplacementLibraryIds)
                state.LibrarySourceId = target.LibrarySourceId;
            state.BoneName = asset.BoneName;
            state.BoneEndOffset = asset.BoneEndOffset;
            state.PivotOffset = asset.PivotOffset;
            state.InheritPivotOffset = asset.InheritPivotOffset;
            state.PivotOffsetInheritsModelOrigin = asset.PivotOffsetInheritsModelOrigin;
            if (string.IsNullOrEmpty(state.AlbedoTexturePath))
                state.AlbedoTexturePath = asset.AlbedoTexturePath;
            if (!state.HasMaterialOverrides)
            {
                state.NormalTexturePath = asset.NormalTexturePath;
                state.EmissionTexturePath = asset.EmissionTexturePath;
                state.SurfaceTexturePath = asset.SurfaceTexturePath;
                state.OcclusionTexturePath = asset.OcclusionTexturePath;
                state.RoughnessTexturePath = asset.RoughnessTexturePath;
                state.MetallicTexturePath = asset.MetallicTexturePath;
                state.HeightTexturePath = asset.HeightTexturePath;
            }

            if (old is MiBoneSceneObject oldBone && target is MiBoneSceneObject newBone)
            {
                newBone.OffsetPosition = oldBone.OffsetPosition;
                newBone.OffsetRotation = oldBone.OffsetRotation;
                newBone.OffsetScale = oldBone.OffsetScale;
                state.Position = Vector(newBone.Position);
                state.Rotation = Vector(newBone.Rotation);
                state.Scale = Vector(newBone.Scale);
            }
            states.Add((target, state));
        }
        ProjectSceneSerializer.ApplyObjectStates(propertiesPanel, states);
        foreach (var node in Nodes(replacement))
        {
            node.RestoreExactChildren = true;
            ProjectSceneSerializer.RemapObjectReferences(node, ids);
        }
        return result;
    }

    public void RetainUnmatchedParts()
    {
        // Matched descendants can move to a new rig parent. Do not leave their old
        // geometry inside an unmatched branch that is being retained.
        foreach (var old in _sourceNodes.Skip(1))
            if (_matches.ContainsKey(old))
                old.Parent?.RemoveChild(old);
        foreach (var old in _sourceNodes.Skip(1))
            if (!_matches.ContainsKey(old))
            {
                old.RestoreExactChildren = true;
                if (old.Parent != null && _matches.TryGetValue(old.Parent, out var newParent))
                    newParent.AddChild(old);
            }
    }

    public void Commit(List<SceneObject> scene)
    {
        var parent = Source.Parent;
        var siblings = (parent?.Children ?? scene).ToList();
        int index = siblings.IndexOf(Source);
        if (index < 0)
            throw new InvalidOperationException("The asset instance is no longer in the scene.");
        RetainUnmatchedParts();
        if (parent == null)
            scene[index] = Replacement;
        else
        {
            parent.RemoveChild(Source);
            parent.AddChild(Replacement);
            parent.MoveChild(Replacement, index);
            parent.RestoreExactChildren = true;
        }
    }

    internal static IEnumerable<SceneObject> Nodes(SceneObject root)
    {
        yield return root;
        foreach (var child in root.Children)
            if (!child.IsRuntimeTransient)
                foreach (var node in Nodes(child))
                    yield return node;
    }

    private static SceneObject? FindNamed(SceneObject source, IEnumerable<SceneObject> candidates)
    {
        var nodes = candidates.ToArray();
        string name = PartName(source);
        var exact = Unique(nodes.Where(node => string.Equals(PartName(node), name, StringComparison.OrdinalIgnoreCase)));
        if (exact != null)
            return exact;
        string canonical = CanonicalName(name);
        return canonical.Length == 0 ? null : Unique(nodes.Where(node => CanonicalName(PartName(node)) == canonical));
    }

    private static SceneObject? Unique(IEnumerable<SceneObject> nodes)
    {
        var matches = nodes.Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string PartName(SceneObject node) =>
        node is BoneSceneObject bone && !string.IsNullOrWhiteSpace(bone.BoneName) ? bone.BoneName : node.Name;

    private static string CanonicalName(string name)
    {
        string key = new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return key switch
        {
            "arml" or "larm" or "armleft" or "leftupperarm" or "upperarmleft" => "leftarm",
            "armr" or "rarm" or "armright" or "rightupperarm" or "upperarmright" => "rightarm",
            "legl" or "lleg" or "legleft" or "leftupperleg" or "upperlegleft" => "leftleg",
            "legr" or "rleg" or "legright" or "rightupperleg" or "upperlegright" => "rightleg",
            "torso" or "chest" => "body",
            _ => key
        };
    }

    private static ProjectVec3 Vector(vec3 value) => new() { X = value.x, Y = value.y, Z = value.z };
}
