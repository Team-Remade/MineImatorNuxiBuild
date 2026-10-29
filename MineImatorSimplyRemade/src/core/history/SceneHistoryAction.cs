using System.Text.Json;
using System.Text.Json.Nodes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.history;

internal sealed class SceneHistoryAction
{
    private readonly IReadOnlyList<Change> _changes;
    private readonly IReadOnlyList<SceneObjectChange>? _sceneObjectChanges;
    private readonly IReadOnlyList<DirectObjectChange>? _directObjectChanges;
    private readonly IReadOnlyList<HierarchyChange> _hierarchyChanges = [];
    private readonly SceneObject[] _beforeSelection = [];
    private readonly SceneObject[] _afterSelection = [];
    private readonly ParticleSpawnerSceneObject[] _removedOnUndo = [];
    private readonly ParticleSpawnerSceneObject[] _removedOnRedo = [];
    private readonly ProjectState? _beforeProject;
    private readonly ProjectState? _afterProject;

    private SceneHistoryAction(IReadOnlyList<Change> changes, IReadOnlyList<SceneObjectChange>? sceneObjectChanges)
    {
        _changes = changes;
        _sceneObjectChanges = sceneObjectChanges;
    }

    private SceneHistoryAction(
        IReadOnlyList<DirectObjectChange> directObjectChanges,
        IReadOnlyList<HierarchyChange> hierarchyChanges,
        HierarchyState beforeHierarchy,
        HierarchyState afterHierarchy,
        ProjectState? beforeProject,
        ProjectState? afterProject)
    {
        _changes = [];
        _directObjectChanges = directObjectChanges;
        _hierarchyChanges = hierarchyChanges;
        _beforeProject = beforeProject;
        _afterProject = afterProject;
        if (hierarchyChanges.Count > 0)
        {
            _beforeSelection = beforeHierarchy.Selection.Where(beforeHierarchy.Children.ContainsKey).ToArray();
            _afterSelection = afterHierarchy.Selection.Where(afterHierarchy.Children.ContainsKey).ToArray();
            _removedOnUndo = afterHierarchy.Children.Keys.Except(beforeHierarchy.Children.Keys).OfType<ParticleSpawnerSceneObject>().ToArray();
            _removedOnRedo = beforeHierarchy.Children.Keys.Except(afterHierarchy.Children.Keys).OfType<ParticleSpawnerSceneObject>().ToArray();
        }
    }

    public static SceneHistoryAction? Create(
        IEnumerable<(SceneObject Object, ProjectSceneObjectEntry Before, ProjectSceneObjectEntry After)> changes,
        HierarchyState beforeHierarchy,
        HierarchyState afterHierarchy,
        ProjectState beforeProject,
        ProjectState afterProject)
    {
        DirectObjectChange[] directChanges = changes
            .Where(change => !JsonSerializer.Serialize(change.Before, AppJsonContext.Default.ProjectSceneObjectEntry)
                .Equals(JsonSerializer.Serialize(change.After, AppJsonContext.Default.ProjectSceneObjectEntry), StringComparison.Ordinal))
            .Select(change => new DirectObjectChange(change.Object, change.Before, change.After))
            .ToArray();
        var hierarchyChanges = new List<HierarchyChange>();
        if (!ReferenceEquals(beforeHierarchy, afterHierarchy))
        {
            if (!beforeHierarchy.Roots.SequenceEqual(afterHierarchy.Roots))
                hierarchyChanges.Add(new HierarchyChange(null, beforeHierarchy.Roots, afterHierarchy.Roots));
            foreach (SceneObject parent in beforeHierarchy.Children.Keys.Union(afterHierarchy.Children.Keys))
            {
                SceneObject[] before = beforeHierarchy.Children.GetValueOrDefault(parent) ?? [];
                SceneObject[] after = afterHierarchy.Children.GetValueOrDefault(parent) ?? [];
                if (!before.SequenceEqual(after))
                    hierarchyChanges.Add(new HierarchyChange(parent, before, after));
            }
        }

        bool projectChanged = beforeProject != afterProject;
        return directChanges.Length == 0 && hierarchyChanges.Count == 0 && !projectChanged
            ? null
            : new SceneHistoryAction(directChanges, hierarchyChanges, beforeHierarchy, afterHierarchy,
                projectChanged ? beforeProject : null, projectChanged ? afterProject : null);
    }

    public bool Apply(Viewport viewport, PropertiesPanel? propertiesPanel, bool undo)
    {
        if (_directObjectChanges == null)
            return false;

        foreach (DirectObjectChange change in _directObjectChanges)
        {
            if (!string.Equals(change.Object.ObjectId, change.Before.ObjectId, StringComparison.Ordinal))
                return false;
        }

        if (_hierarchyChanges.Count > 0)
        {
            foreach (ParticleSpawnerSceneObject spawner in undo ? _removedOnUndo : _removedOnRedo)
                spawner.ResetRuntime();
            SelectionManager.Instance?.ClearSelection();
            // Detach all changed lists before attaching any, so cross-parent moves
            // cannot remove a child that has already been restored elsewhere.
            foreach (HierarchyChange change in _hierarchyChanges)
            {
                if (change.Parent == null)
                    viewport.SceneObjects.RemoveAll(obj => !obj.IsRuntimeTransient);
                else
                    foreach (SceneObject child in change.Parent.Children.Where(obj => !obj.IsRuntimeTransient).ToArray())
                        change.Parent.RemoveChild(child);
            }
            foreach (HierarchyChange change in _hierarchyChanges)
            {
                foreach (SceneObject child in undo ? change.Before : change.After)
                {
                    if (change.Parent == null)
                    {
                        child.Parent?.RemoveChild(child);
                        viewport.SceneObjects.Add(child);
                    }
                    else
                        change.Parent.AddChild(child);
                }
            }
        }

        ProjectSceneSerializer.ApplyObjectStates(
            propertiesPanel,
            _directObjectChanges.Select(change => (change.Object, undo ? change.Before : change.After)));
        ProjectState? projectState = undo ? _beforeProject : _afterProject;
        if (projectState != null)
        {
            ProjectManifest manifest = ProjectManager.Instance.Manifest;
            ProjectRenderSettings settings = JsonSerializer.Deserialize(projectState.SettingsJson, AppJsonContext.Default.ProjectRenderSettings)!;
            bool skyTexturesChanged = manifest.Settings.SunTexture != settings.SunTexture ||
                                      manifest.Settings.MoonTexture != settings.MoonTexture ||
                                      manifest.Settings.CloudTexture != settings.CloudTexture;
            manifest.ProjectName = projectState.Name;
            manifest.Settings = settings;
            propertiesPanel?.LoadProjectSettingsFromManifest(manifest);
            if (skyTexturesChanged)
                viewport.ReloadSkyTextures();
        }
        if (_hierarchyChanges.Count > 0 && SelectionManager.Instance is { } selection)
        {
            SceneObject[] targetSelection = undo ? _beforeSelection : _afterSelection;
            foreach (SceneObject obj in targetSelection)
                selection.SelectObject(obj);
            foreach (SceneObject obj in selection.SelectedObjects.Except(targetSelection).ToArray())
                selection.DeselectObject(obj);
        }
        return true;
    }

    internal sealed record ProjectState(string Name, string SettingsJson)
    {
        public static ProjectState Capture(ProjectManifest manifest) => new(
            manifest.ProjectName,
            JsonSerializer.Serialize(manifest.Settings, AppJsonContext.Default.ProjectRenderSettings));
    }

    internal sealed class HierarchyState
    {
        public SceneObject[] Roots { get; }
        public Dictionary<SceneObject, SceneObject[]> Children { get; } = new();
        public SceneObject[] Selection { get; }

        public static HierarchyState Capture(Viewport viewport, HierarchyState? previous) =>
            previous != null && previous.Matches(viewport) ? previous : new HierarchyState(viewport);

        private bool Matches(Viewport viewport)
        {
            if (!MatchesObjects(viewport.SceneObjects, Roots, ignoreTransient: true) ||
                !MatchesObjects(SelectionManager.Instance?.SelectedObjects ?? [], Selection, ignoreTransient: false))
                return false;
            foreach (var (obj, children) in Children)
                if (!MatchesObjects(obj.Children, children, ignoreTransient: true))
                    return false;
            return true;
        }

        private static bool MatchesObjects(IReadOnlyList<SceneObject> current, SceneObject[] saved, bool ignoreTransient)
        {
            int index = 0;
            for (int i = 0; i < current.Count; i++)
            {
                SceneObject obj = current[i];
                if (ignoreTransient && obj.IsRuntimeTransient) continue;
                if (index >= saved.Length || !ReferenceEquals(obj, saved[index++]))
                    return false;
            }
            return index == saved.Length;
        }

        public HierarchyState(Viewport viewport)
        {
            Roots = viewport.SceneObjects.Where(obj => !obj.IsRuntimeTransient).ToArray();
            Selection = SelectionManager.Instance?.SelectedObjects.ToArray() ?? [];
            foreach (SceneObject root in Roots)
                CaptureChildren(root);
        }

        private void CaptureChildren(SceneObject obj)
        {
            if (Children.ContainsKey(obj))
                return;
            SceneObject[] children = obj.Children.Where(child => !child.IsRuntimeTransient).ToArray();
            Children.Add(obj, children);
            foreach (SceneObject child in children)
                CaptureChildren(child);
        }
    }

    private sealed record HierarchyChange(SceneObject? Parent, SceneObject[] Before, SceneObject[] After);

    public static SceneHistoryAction? Create(string beforeJson, string afterJson)
    {
        JsonNode? before = JsonNode.Parse(beforeJson, documentOptions: AppJsonContext.DocumentOptions);
        JsonNode? after = JsonNode.Parse(afterJson, documentOptions: AppJsonContext.DocumentOptions);
        if (before == null || after == null)
            return null;

        var changes = new List<Change>();
        CollectChanges(before, true, after, true, [], changes);
        return changes.Count == 0
            ? null
            : new SceneHistoryAction(changes, CreateSceneObjectChanges(before, after, changes));
    }

    public string Apply(string currentJson, bool undo)
    {
        JsonNode root = JsonNode.Parse(currentJson, documentOptions: AppJsonContext.DocumentOptions)
            ?? throw new JsonException("Scene history state is empty.");
        foreach (Change change in _changes)
            root = ApplyChange(
                root,
                change.Path,
                undo ? change.Before : change.After,
                undo ? change.BeforeExists : change.AfterExists);
        return root.ToJsonString(AppJsonContext.Default.Options);
    }

    public bool TryGetAffectedSceneRootIndices(out int[] rootIndices)
    {
        var indices = new HashSet<int>();
        foreach (Change change in _changes)
        {
            if (change.Path.Count < 2 ||
                !string.Equals(change.Path[0].Property, "SceneObjects", StringComparison.Ordinal) ||
                change.Path[1].Index is not int rootIndex)
            {
                rootIndices = [];
                return false;
            }

            indices.Add(rootIndex);
        }

        rootIndices = indices.Order().ToArray();
        return rootIndices.Length > 0;
    }

    public bool TryGetSceneObjectChanges(bool undo, out IReadOnlyList<SceneObjectState> states)
    {
        if (_sceneObjectChanges == null)
        {
            states = [];
            return false;
        }

        states = _sceneObjectChanges
            .Select(change => new SceneObjectState(change.IndexPath, undo ? change.Before : change.After))
            .ToArray();
        return states.Count > 0;
    }

    private static IReadOnlyList<SceneObjectChange>? CreateSceneObjectChanges(
        JsonNode before,
        JsonNode after,
        IReadOnlyList<Change> changes)
    {
        var objectPaths = new Dictionary<string, int[]>();
        foreach (Change change in changes)
        {
            if (!TryGetSceneObjectIndexPath(change.Path, out int[] indexPath))
                return null;

            objectPaths.TryAdd(string.Join('/', indexPath), indexPath);
        }

        var objectChanges = new List<SceneObjectChange>(objectPaths.Count);
        foreach (int[] indexPath in objectPaths.Values)
        {
            JsonObject? beforeObject = FindSceneObject(before, indexPath);
            JsonObject? afterObject = FindSceneObject(after, indexPath);
            if (beforeObject == null || afterObject == null)
                return null;

            ProjectSceneObjectEntry? beforeEntry = DeserializeObjectWithoutChildren(beforeObject);
            ProjectSceneObjectEntry? afterEntry = DeserializeObjectWithoutChildren(afterObject);
            if (beforeEntry == null || afterEntry == null ||
                !string.Equals(beforeEntry.ObjectId, afterEntry.ObjectId, StringComparison.Ordinal))
                return null;

            objectChanges.Add(new SceneObjectChange(indexPath, beforeEntry, afterEntry));
        }

        return objectChanges;
    }

    private static bool TryGetSceneObjectIndexPath(IReadOnlyList<PathSegment> path, out int[] indexPath)
    {
        indexPath = [];
        if (path.Count < 3 || !string.Equals(path[0].Property, "SceneObjects", StringComparison.Ordinal) ||
            path[1].Index is not int rootIndex)
            return false;

        var indices = new List<int> { rootIndex };
        int position = 2;
        while (position + 1 < path.Count &&
               string.Equals(path[position].Property, "Children", StringComparison.Ordinal) &&
               path[position + 1].Index is int childIndex)
        {
            indices.Add(childIndex);
            position += 2;
        }

        if (position >= path.Count || string.Equals(path[position].Property, "Children", StringComparison.Ordinal))
            return false;

        indexPath = indices.ToArray();
        return true;
    }

    private static JsonObject? FindSceneObject(JsonNode root, IReadOnlyList<int> indexPath)
    {
        JsonNode? current = root["SceneObjects"]?[indexPath[0]];
        for (int index = 1; index < indexPath.Count && current != null; index++)
            current = current["Children"]?[indexPath[index]];
        return current as JsonObject;
    }

    private static ProjectSceneObjectEntry? DeserializeObjectWithoutChildren(JsonObject source)
    {
        var shallowObject = new JsonObject();
        foreach ((string name, JsonNode? value) in source)
        {
            if (!string.Equals(name, "Children", StringComparison.Ordinal))
                shallowObject[name] = value?.DeepClone();
        }

        return shallowObject.Deserialize(AppJsonContext.Default.ProjectSceneObjectEntry);
    }

    private static void CollectChanges(
        JsonNode? before,
        bool beforeExists,
        JsonNode? after,
        bool afterExists,
        List<PathSegment> path,
        List<Change> changes)
    {
        if (beforeExists == afterExists && JsonNode.DeepEquals(before, after))
            return;

        if (beforeExists && afterExists && before is JsonObject beforeObject && after is JsonObject afterObject)
        {
            foreach (string key in beforeObject.Select(pair => pair.Key).Union(afterObject.Select(pair => pair.Key)))
            {
                path.Add(new PathSegment(key, null));
                CollectChanges(
                    beforeObject[key],
                    beforeObject.ContainsKey(key),
                    afterObject[key],
                    afterObject.ContainsKey(key),
                    path,
                    changes);
                path.RemoveAt(path.Count - 1);
            }
            return;
        }

        if (beforeExists && afterExists && before is JsonArray beforeArray && after is JsonArray afterArray && beforeArray.Count == afterArray.Count)
        {
            for (int index = 0; index < beforeArray.Count; index++)
            {
                path.Add(new PathSegment(null, index));
                CollectChanges(beforeArray[index], true, afterArray[index], true, path, changes);
                path.RemoveAt(path.Count - 1);
            }
            return;
        }

        changes.Add(new Change(path.ToArray(), before?.DeepClone(), beforeExists, after?.DeepClone(), afterExists));
    }

    private static JsonNode ApplyChange(JsonNode root, IReadOnlyList<PathSegment> path, JsonNode? value, bool valueExists)
    {
        if (path.Count == 0)
            return value?.DeepClone() ?? new JsonObject();

        JsonNode parent = root;
        for (int index = 0; index < path.Count - 1; index++)
        {
            PathSegment segment = path[index];
            parent = segment.Property != null
                ? parent[segment.Property] ?? throw new JsonException($"Missing history property '{segment.Property}'.")
                : parent[segment.Index!.Value] ?? throw new JsonException("Missing history array item.");
        }

        PathSegment leaf = path[^1];
        if (leaf.Property != null)
        {
            JsonObject parentObject = parent.AsObject();
            if (!valueExists)
                parentObject.Remove(leaf.Property);
            else
                parentObject[leaf.Property] = value?.DeepClone();
        }
        else
        {
            JsonArray parentArray = parent.AsArray();
            if (!valueExists)
                parentArray.RemoveAt(leaf.Index!.Value);
            else
                parentArray[leaf.Index!.Value] = value?.DeepClone();
        }

        return root;
    }

    private sealed record Change(
        IReadOnlyList<PathSegment> Path,
        JsonNode? Before,
        bool BeforeExists,
        JsonNode? After,
        bool AfterExists);
    private sealed record SceneObjectChange(
        IReadOnlyList<int> IndexPath,
        ProjectSceneObjectEntry Before,
        ProjectSceneObjectEntry After);
    private sealed record DirectObjectChange(
        SceneObject Object,
        ProjectSceneObjectEntry Before,
        ProjectSceneObjectEntry After);
    public sealed record SceneObjectState(IReadOnlyList<int> IndexPath, ProjectSceneObjectEntry Entry);
    private readonly record struct PathSegment(string? Property, int? Index);
}
