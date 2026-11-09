using System.Text.Json;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using NativeFileDialogSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private string _libraryReplacementMessage = "";

    private void ReplaceLibraryAssetFromDialog(ProjectManager manager, ProjectSceneObjectEntry selected)
    {
        var dialog = Dialog.FileOpen("mimodel,miobject,nxObj,glb,gltf,fbx,obj,dae,blend,3ds,stl,ply");
        if (dialog.IsError)
        {
            _libraryReplacementMessage = Localization.T("text.panel.properties.replace_asset_dialog_failed");
            Console.Error.WriteLine(_libraryReplacementMessage);
            return;
        }
        if (!dialog.IsOk || string.IsNullOrWhiteSpace(dialog.Path))
            return;

        try
        {
            var result = ReplaceLibraryAsset(manager, selected, dialog.Path);
            _libraryReplacementMessage = Localization.Format("text.panel.properties.replace_asset_result",
                result.Instances, result.Matched, result.Retained);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or JsonException or NotSupportedException)
        {
            Console.Error.WriteLine($"Asset replacement failed: {exception}");
            _libraryReplacementMessage = Localization.Format("text.panel.properties.replace_asset_failed", exception.Message);
        }
    }

    internal (int Instances, int Matched, int Retained) ReplaceLibraryAsset(
        ProjectManager manager, ProjectSceneObjectEntry selected, string path)
    {
        if (Viewport == null || SpawnMenu == null)
            throw new InvalidOperationException("The viewport and model importer must be ready.");

        var scene = Viewport.SceneObjects;
        var originalRoots = scene.ToArray();
        var selection = SelectionManager.Instance?.SelectedObjects.ToArray() ?? [];
        var temporaryNodes = new HashSet<SceneObject>();
        var prepared = new List<LibraryAssetReplacement>();
        var originalLibrary = manager.Manifest.ObjectLibrary;
        manager.Manifest.ObjectLibrary = originalLibrary.ToList();
        bool committed = false;
        try
        {
            var asset = SpawnMenu.SpawnCustomModelFromPath(path)
                ?? throw new InvalidDataException($"Could not load the replacement model '{path}'.");
            temporaryNodes.UnionWith(LibraryAssetReplacement.Nodes(asset));
            scene.Remove(asset);

            var oldBase = ProjectSceneSerializer.SpawnObjectFromEntry(selected, Viewport, SpawnMenu)
                ?? throw new InvalidDataException("Could not load the original library asset.");
            temporaryNodes.UnionWith(LibraryAssetReplacement.Nodes(oldBase));
            scene.Remove(oldBase);
            var importedParts = LibraryAssetReplacement.Nodes(asset).ToHashSet();
            var baseReplacement = LibraryAssetReplacement.Prepare(oldBase, asset, this);
            baseReplacement.RetainUnmatchedParts();
            var oldParts = baseReplacement.Matches.Values.ToHashSet();
            foreach (var node in LibraryAssetReplacement.Nodes(asset))
                if (importedParts.Contains(node) && !oldParts.Contains(node) ||
                    string.IsNullOrWhiteSpace(node.LibrarySourceId))
                    node.LibrarySourceId = Guid.NewGuid().ToString("N");

            var baseEntry = ProjectSceneSerializer.SerializeObjectForLibrary(asset);
            SetLibraryIds(baseEntry);
            var entries = new Dictionary<string, ProjectSceneObjectEntry>(StringComparer.OrdinalIgnoreCase);
            IndexEntries(baseEntry);
            var oldIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectLibraryEntryIds([selected], oldIds);

            foreach (var root in originalRoots)
                PrepareInstances(root);

            // Replace lists/entries rather than mutating the old library, which undo still owns.
            var library = manager.Manifest.ObjectLibrary.Select(Clone).ToList();
            RewriteLibrary(library);
            foreach (var replacement in prepared)
                replacement.Commit(scene);
            manager.Manifest.ObjectLibrary = library;
            committed = true;

            var matches = prepared.SelectMany(item => item.Matches)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            if (SelectionManager.Instance is { } selectionManager)
            {
                selectionManager.ClearSelection();
                foreach (var node in selection)
                    selectionManager.SelectObject(matches.GetValueOrDefault(node, node));
            }
            Timeline?.RefreshFromScene();
            manager.SetDirty(true);
            return (prepared.Count, baseReplacement.Matches.Count - 1, baseReplacement.RetainedPartCount);

            void PrepareInstances(SceneObject node)
            {
                if (node.IsRuntimeTransient)
                    return;
                if (oldIds.Contains(node.LibrarySourceId) && entries.TryGetValue(node.LibrarySourceId, out var entry))
                {
                    var target = ProjectSceneSerializer.SpawnObjectFromEntry(entry, Viewport, SpawnMenu)
                        ?? throw new InvalidDataException($"Could not prepare replacement for '{node.Name}'.");
                    temporaryNodes.UnionWith(LibraryAssetReplacement.Nodes(target));
                    scene.Remove(target);
                    prepared.Add(LibraryAssetReplacement.Prepare(node, target, this, useReplacementLibraryIds: true));
                    return;
                }
                foreach (var child in node.Children)
                    PrepareInstances(child);
            }

            void IndexEntries(ProjectSceneObjectEntry entry)
            {
                entries.Add(entry.LibraryEntryId, entry);
                foreach (var child in entry.Children)
                    IndexEntries(child);
            }

            bool RewriteLibrary(List<ProjectSceneObjectEntry> nodes)
            {
                bool changed = false;
                for (int index = 0; index < nodes.Count; index++)
                {
                    var entry = nodes[index];
                    if (oldIds.Contains(entry.LibraryEntryId) && entries.TryGetValue(entry.LibraryEntryId, out var replacement))
                    {
                        nodes[index] = Clone(replacement);
                        changed = true;
                    }
                    else if (RewriteLibrary(entry.Children))
                    {
                        entry.RestoreExactChildren = true;
                        changed = true;
                    }
                }
                return changed;
            }
        }
        finally
        {
            // Importers may add roots before reporting failure; none may leak into the scene.
            var retainedRoots = committed ? prepared.Select(item => item.Replacement).ToHashSet() : [];
            foreach (var root in scene.Where(root => !originalRoots.Contains(root) && !retainedRoots.Contains(root)))
                temporaryNodes.UnionWith(LibraryAssetReplacement.Nodes(root));
            scene.RemoveAll(root => !originalRoots.Contains(root) && !retainedRoots.Contains(root));
            var liveNodes = scene.SelectMany(LibraryAssetReplacement.Nodes).ToHashSet();
            foreach (var mesh in temporaryNodes.Where(node => !liveNodes.Contains(node))
                .SelectMany(node => node.Visuals).Distinct())
                mesh.Dispose();
            if (!committed)
            {
                manager.Manifest.ObjectLibrary = originalLibrary;
                Timeline?.RefreshFromScene();
            }
        }
    }

    private static void SetLibraryIds(ProjectSceneObjectEntry entry)
    {
        entry.LibraryEntryId = entry.LibrarySourceId;
        foreach (var child in entry.Children)
            SetLibraryIds(child);
    }

    private static ProjectSceneObjectEntry Clone(ProjectSceneObjectEntry entry) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(entry, AppJsonContext.Default.ProjectSceneObjectEntry),
            AppJsonContext.Default.ProjectSceneObjectEntry)!;
}
