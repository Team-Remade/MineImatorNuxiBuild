using System.Text.Json;
using System.Text.Json.Nodes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;

namespace MineImatorSimplyRemade.core.history;

internal sealed class SceneHistoryAction
{
    private readonly IReadOnlyList<Change> _changes;
    private readonly IReadOnlyList<SceneObjectChange>? _sceneObjectChanges;
    private readonly IReadOnlyList<DirectObjectChange>? _directObjectChanges;

    private SceneHistoryAction(IReadOnlyList<Change> changes, IReadOnlyList<SceneObjectChange>? sceneObjectChanges)
    {
        _changes = changes;
        _sceneObjectChanges = sceneObjectChanges;
    }

    private SceneHistoryAction(IReadOnlyList<DirectObjectChange> directObjectChanges)
    {
        _changes = [];
        _directObjectChanges = directObjectChanges;
    }

    public static SceneHistoryAction? Create(
        IEnumerable<(SceneObject Object, ProjectSceneObjectEntry Before, ProjectSceneObjectEntry After)> changes)
    {
        DirectObjectChange[] directChanges = changes
            .Where(change => !JsonSerializer.Serialize(change.Before, AppJsonContext.Default.ProjectSceneObjectEntry)
                .Equals(JsonSerializer.Serialize(change.After, AppJsonContext.Default.ProjectSceneObjectEntry), StringComparison.Ordinal))
            .Select(change => new DirectObjectChange(change.Object, change.Before, change.After))
            .ToArray();
        return directChanges.Length == 0 ? null : new SceneHistoryAction(directChanges);
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

        ProjectSceneSerializer.ApplyObjectStates(
            propertiesPanel,
            _directObjectChanges.Select(change => (change.Object, undo ? change.Before : change.After)));
        return true;
    }

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
