using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// ImGui scene-tree panel.  Ported from the Nuxi reference project (ExampleSceneTree).
///
/// Supported features
/// ──────────────────
///  • Recursive tree built from Viewport.SceneObjects + SceneObject.Children
///  • Single-click selection (synced bidirectionally with SelectionManager)
///  • Inline rename (double-click label area)
///  • Right-click context menu: Duplicate / Delete
///  • Drag-and-drop reparenting (drop on item = child; drop on blank = unparent to root)
///
/// Not yet implemented
/// ────────────────────
///  • Multi-selection
///  • Per-type object icons
///  • Keyframe deep-copy on Duplicate
/// </summary>
public class SceneTree : UiPanel
{
    // ── Owner reference ─────────────────────────────────────────────────────
    public Viewport Viewport { get; set; }

    // ── State ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirror of the first entry in SelectionManager.SelectedObjects, kept in sync
    /// via the SelectionChanged event for efficient per-frame highlight lookups.
    /// </summary>
    private SceneObject _selectedObject;
    private SceneObject _renamingObject;
    private string      _renameBuffer = "";

    // Context-menu state
    private SceneObject _contextMenuTarget;
    private bool        _openContextMenu;

    // Drag-and-drop state
    private SceneObject _draggingObject;

    // Running ID counter reset each frame
    private int _nodeIdCounter;

    // ── Constructor ─────────────────────────────────────────────────────────

    public SceneTree()
    {
        // Subscribe to SelectionManager once it has been initialized.
        // SetGL() is called after SelectionManager.Initialize() in MainWindow,
        // but we can't hook up here yet since SelectionManager may not exist.
        // Wire the event in SetViewport() or lazily on first Render().
    }

    /// <summary>
    /// Wires the SelectionManager event subscription.  Called after both
    /// SelectionManager.Initialize() and the SceneTree are ready.
    /// </summary>
    public void Initialize()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.SelectionChanged += OnSelectionChanged;
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Rebuilds internal state and redraws the panel.</summary>
    public override void Render()
    {
        ImGui.Begin("Scene Tree");

        if (Viewport == null)
        {
            ImGui.TextDisabled("(no viewport)");
            ImGui.End();
            return;
        }

        // Reset per-frame id counter
        _nodeIdCounter  = 0;
        _openContextMenu = false;

        // Draw each top-level object — snapshot the list first so that a
        // reparent/delete triggered during the same frame doesn't mutate it.
        foreach (var obj in Viewport.SceneObjects.ToList())
        {
            RenderNode(obj);
        }

        // ── Root-level drop target ───────────────────────────────────────────
        // Covers the remaining empty space so dropping onto blank area
        // unparents the object back to the viewport root.
        var remaining  = ImGui.GetContentRegionAvail();
        float dropHeight = Math.Max(remaining.Y, 8f);
        ImGui.InvisibleButton("##root_drop_target", new Vector2(-1, dropHeight));

        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload   = ImGui.AcceptDragDropPayload("SCENE_OBJECT");
                bool delivered = payload.Handle != null && ImGui.IsDelivery(payload);
                if (delivered && _draggingObject != null)
                {
                    if (_draggingObject.Parent != null)
                        ReparentObject(_draggingObject, newParent: null);
                    _draggingObject = null;
                }
            }
            ImGui.EndDragDropTarget();
        }

        // Context menu (opened deferred to avoid conflicting with tree-click handling)
        if (_openContextMenu && _contextMenuTarget != null)
            ImGui.OpenPopup("##SceneTreeContextMenu");

        if (ImGui.BeginPopup("##SceneTreeContextMenu"))
        {
            if (_contextMenuTarget != null)
            {
                ImGui.TextDisabled(_contextMenuTarget.GetDisplayName());
                ImGui.Separator();

                if (ImGui.MenuItem("Duplicate"))
                {
                    DuplicateObject(_contextMenuTarget);
                    _contextMenuTarget = null;
                }

                if (_contextMenuTarget is CameraSceneObject cam)
                {
                    string activeLabel = cam.Active ? "Clear Active Camera" : "Set as Active Camera";
                    if (ImGui.MenuItem(activeLabel))
                    {
                        if (cam.Active)
                        {
                            cam.Active = false;
                        }
                        else
                        {
                            CameraSceneObject.SetActiveExclusive(cam);
                        }
                        _contextMenuTarget = null;
                    }
                }

                if (ImGui.MenuItem("Delete"))
                {
                    DeleteObject(_contextMenuTarget);
                    _contextMenuTarget = null;
                }
            }
            ImGui.EndPopup();
        }

        // Cancel drag if the mouse was released outside any target.
        if (_draggingObject != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            _draggingObject = null;

        ImGui.End();
    }

    /// <summary>
    /// Forces the selection to match an externally-chosen object
    /// (e.g. a future viewport colour-pick).
    /// </summary>
    public void SetSelection(SceneObject obj)
    {
        if (SelectionManager.Instance == null) return;
        SelectionManager.Instance.ClearSelection();
        if (obj != null)
            SelectionManager.Instance.SelectObject(obj);
    }

    /// <summary>No-op — tree is rebuilt every frame.</summary>
    public void Refresh() { }

    /// <summary>No-op — tree is rebuilt every frame.</summary>
    public void RefreshObject(SceneObject obj) { }

    /// <summary>Duplicates every selected object using the same logic as the context menu.</summary>
    public void DuplicateSelectedObjects()
    {
        var selectedObjects = SelectionManager.Instance?.SelectedObjects.ToList()
            ?? (_selectedObject != null ? new List<SceneObject> { _selectedObject } : new List<SceneObject>());

        if (selectedObjects.Count == 0)
            return;

        var duplicateRoots = selectedObjects
            .Where(original => !selectedObjects.Any(other => other != original && original.IsDescendantOf(other)))
            .ToList();

        if (duplicateRoots.Count == 0)
            return;

        var duplicates = new List<SceneObject>(duplicateRoots.Count);
        foreach (var original in duplicateRoots)
        {
            var duplicate = DuplicateObject(original, selectDuplicate: false);
            if (duplicate != null)
                duplicates.Add(duplicate);
        }

        if (duplicates.Count == 0)
            return;

        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.ClearSelection();
            foreach (var duplicate in duplicates)
                SelectionManager.Instance.SelectObject(duplicate);
        }
        else
        {
            SelectObject(duplicates[0]);
        }
    }

    /// <summary>Deletes every selected object using the same logic as the context menu.</summary>
    public void DeleteSelectedObjects()
    {
        var selectedObjects = SelectionManager.Instance?.SelectedObjects.ToList()
            ?? (_selectedObject != null ? new List<SceneObject> { _selectedObject } : new List<SceneObject>());

        if (selectedObjects.Count == 0)
            return;

        // Delete roots and descendants will be removed automatically
        var deleteRoots = selectedObjects
            .Where(original => !selectedObjects.Any(other => other != original && original.IsDescendantOf(other)))
            .ToList();

        foreach (var obj in deleteRoots)
        {
            DeleteObject(obj);
        }
    }

    // ── Rendering helpers ───────────────────────────────────────────────────

    private void RenderNode(SceneObject obj)
    {
        if (obj.HideInSceneTree) return;

        int nodeId    = ++_nodeIdCounter;
        bool hasChildren = obj.Children.Any(c => !c.HideInSceneTree);
        bool isSelected  = SelectionManager.Instance != null
            ? SelectionManager.Instance.IsSelected(obj)
            : _selectedObject == obj;
        bool isRenaming  = _renamingObject == obj;

        ImGuiTreeNodeFlags flags =
            ImGuiTreeNodeFlags.OpenOnArrow |
            ImGuiTreeNodeFlags.SpanAvailWidth;

        if (!hasChildren) flags |= ImGuiTreeNodeFlags.Leaf;
        if (isSelected)   flags |= ImGuiTreeNodeFlags.Selected;

        ImGui.PushID(nodeId);

        bool nodeOpen;

        if (isRenaming)
        {
            nodeOpen = ImGui.TreeNodeEx("##renaming", flags);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            ImGui.SetKeyboardFocusHere();
            if (ImGui.InputText("##rename_input", ref _renameBuffer, 128,
                    ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
            {
                CommitRename(obj);
            }
            else if (!ImGui.IsItemActive() && !ImGui.IsItemFocused())
            {
                CommitRename(obj);
            }
        }
        else
        {
            nodeOpen = ImGui.TreeNodeEx(obj.GetDisplayName() + "##node", flags);
        }

        // Single click → select
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen())
            SelectObject(obj);

        // Double click → begin inline rename
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            BeginRename(obj);

        // Right click → context menu
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            SelectObject(obj);
            _contextMenuTarget = obj;
            _openContextMenu   = true;
        }

        // ── Drag source ─────────────────────────────────────────────────────
        if (ImGui.BeginDragDropSource())
        {
            _draggingObject = obj;
            unsafe
            {
                byte dummy = 1;
                ImGui.SetDragDropPayload("SCENE_OBJECT", &dummy, 1);
            }
            ImGui.Text("Move: " + obj.GetDisplayName());
            ImGui.EndDragDropSource();
        }

        // ── Drop target ─────────────────────────────────────────────────────
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload   = ImGui.AcceptDragDropPayload("SCENE_OBJECT");
                bool delivered = payload.Handle != null && ImGui.IsDelivery(payload);
                if (delivered && _draggingObject != null)
                {
                    if (_draggingObject != obj && !obj.IsDescendantOf(_draggingObject))
                        ReparentObject(_draggingObject, obj);
                    _draggingObject = null;
                }
            }
            ImGui.EndDragDropTarget();
        }

        // ── Recurse ─────────────────────────────────────────────────────────
        if (nodeOpen)
        {
            foreach (var child in obj.Children.ToList())
                RenderNode(child);
            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    // ── Selection ───────────────────────────────────────────────────────────

    private void SelectObject(SceneObject obj)
    {
        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.ClearSelection();
            if (obj != null)
                SelectionManager.Instance.SelectObject(obj);
        }
        else
        {
            if (_selectedObject != null) _selectedObject.IsSelected = false;
            _selectedObject = obj;
            if (_selectedObject != null) _selectedObject.IsSelected = true;
        }
    }

    private void OnSelectionChanged()
    {
        _selectedObject = SelectionManager.Instance?.SelectedObjects.Count > 0
            ? SelectionManager.Instance.SelectedObjects[0]
            : null;
    }

    // ── Rename ──────────────────────────────────────────────────────────────

    private void BeginRename(SceneObject obj)
    {
        _renamingObject = obj;
        _renameBuffer   = obj.GetDisplayName();
    }

    private void CommitRename(SceneObject obj)
    {
        var trimmed = _renameBuffer.Trim();
        if (!string.IsNullOrEmpty(trimmed))
            obj.Name = trimmed;
        _renamingObject = null;
        _renameBuffer   = "";
    }

    // ── Duplicate ───────────────────────────────────────────────────────────

    private SceneObject DuplicateObject(SceneObject original, bool selectDuplicate = true)
    {
        var duplicate = CreateSceneObjectDuplicate(original);
        if (duplicate == null) return null;

        DuplicateChildrenRecursive(original, duplicate);

        if (original.Parent != null)
            original.Parent.AddChild(duplicate);
        else
            Viewport?.SceneObjects.Add(duplicate);

        if (selectDuplicate)
        {
            if (SelectionManager.Instance != null)
            {
                SelectionManager.Instance.ClearSelection();
                SelectionManager.Instance.SelectObject(duplicate);
            }
            else
            {
                SelectObject(duplicate);
            }
        }

        return duplicate;
    }

    private void DuplicateChildrenRecursive(SceneObject original, SceneObject duplicateParent)
    {
        foreach (var child in original.Children)
        {
            if (child is CharacterSceneObject) continue;

            var childDup = CreateSceneObjectDuplicate(child);
            if (childDup == null) continue;

            duplicateParent.AddChild(childDup);
            DuplicateChildrenRecursive(child, childDup);
        }
    }

    /// <summary>Shallow-copies a SceneObject without copying its children.</summary>
    private SceneObject CreateSceneObjectDuplicate(SceneObject original)
    {
        if (original is CharacterSceneObject) return null;

        SceneObject dup;

        switch (original)
        {
            case LightSceneObject light:
                dup = new LightSceneObject
                {
                    LightColor          = light.LightColor,
                    LightEnergy         = light.LightEnergy,
                    LightRange          = light.LightRange,
                    LightIndirectEnergy = light.LightIndirectEnergy,
                    LightSpecular       = light.LightSpecular,
                    LightShadowEnabled  = light.LightShadowEnabled
                };
                break;

            case CameraSceneObject cam:
                dup = new CameraSceneObject
                {
                    Fov  = cam.Fov,
                    Near = cam.Near,
                    Far  = cam.Far
                };
                // Duplicates always start inactive so only one camera can be
                // active at a time.  We still copy the visual set lists so
                // RefreshActiveMesh can hide the right meshes.
                foreach (var mesh in cam.InactiveVisuals)
                    ((CameraSceneObject)dup).InactiveVisuals.Add(mesh);
                foreach (var mesh in cam.ActiveVisuals)
                    ((CameraSceneObject)dup).ActiveVisuals.Add(mesh);
                ((CameraSceneObject)dup).RefreshActiveMesh();
                break;

            default:
                dup = new SceneObject();
                break;
        }

        var baseName = GetBaseName(original.GetDisplayName());
        int nextNum  = GetNextAvailableNameNumber(baseName);
        dup.Name = nextNum > 1 ? $"{baseName}{nextNum}" : baseName;

        dup.ObjectType          = original.ObjectType;
        dup.IsSelectable        = original.IsSelectable;
        dup.Position            = original.Position;
        dup.Rotation            = original.Rotation;
        dup.Scale               = original.Scale;
        dup.PivotOffset         = original.PivotOffset;
        dup.InheritPivotOffset  = original.InheritPivotOffset;
        dup.ObjectVisible       = original.ObjectVisible;
        dup.SpawnCategory   = original.SpawnCategory;
        dup.BlockVariant    = original.BlockVariant;
        dup.TextureType     = original.TextureType;
        dup.ResourcePackId  = original.ResourcePackId;
        dup.SourceAssetPath = original.SourceAssetPath;

        foreach (var mesh in original.Visuals)
            dup.AddMesh(mesh);

        return dup;
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    private void DeleteObject(SceneObject obj)
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.DeselectObject(obj);
        else if (_selectedObject == obj)
            SelectObject(null);

        if (obj.Parent != null)
            obj.Parent.RemoveChild(obj);
        else
            Viewport?.SceneObjects.Remove(obj);

        RemoveDescendantsFromViewport(obj);
    }

    private void RemoveDescendantsFromViewport(SceneObject obj)
    {
        foreach (var child in obj.Children.ToList())
        {
            Viewport?.SceneObjects.Remove(child);
            RemoveDescendantsFromViewport(child);
        }
    }

    // ── Reparent ────────────────────────────────────────────────────────────

    private void ReparentObject(SceneObject obj, SceneObject newParent)
    {
        if (obj.Parent != null)
            obj.Parent.RemoveChild(obj);
        else
            Viewport?.SceneObjects.Remove(obj);

        if (newParent != null)
            newParent.AddChild(obj);
        else
            Viewport?.SceneObjects.Add(obj);
    }

    // ── Naming helpers ──────────────────────────────────────────────────────

    private static string GetBaseName(string name)
    {
        int i = name.Length - 1;
        while (i >= 0 && char.IsDigit(name[i]))
            i--;
        return (i >= 0 && i < name.Length - 1) ? name[..(i + 1)] : name;
    }

    private int GetNextAvailableNameNumber(string baseName)
    {
        var used = new HashSet<int>();

        if (Viewport != null)
            foreach (var root in Viewport.SceneObjects)
                ScanNode(root);

        int next = 1;
        while (used.Contains(next)) next++;
        return next;

        void ScanNode(SceneObject node)
        {
            var n = node.GetDisplayName();
            if (n == baseName)
                used.Add(1);
            else if (n.StartsWith(baseName) && n.Length > baseName.Length)
            {
                var suffix = n[baseName.Length..];
                if (int.TryParse(suffix, out int num))
                    used.Add(num);
            }
            foreach (var child in node.Children)
                ScanNode(child);
        }
    }
}
