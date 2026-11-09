using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using NativeFileDialogSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// ImGui scene-tree panel.  Ported from the Nuxi reference project (ExampleSceneTree).
///
/// Supported features
/// ──────────────────
///  • Recursive tree built from Viewport.SceneObjects + SceneObject.Children
///  • Multi-selection (Ctrl+click toggle, Shift+click range)
///  • Inline rename (double-click label area)
///  • Right-click context menu (preserves multi-selection)
///  • Ordered multi-object dragging (row edges = siblings, center = child, blank = root)
///  • Move up/down, unparent, and expand/collapse all
///
/// Not yet implemented
/// ────────────────────
///  • Per-type object icons
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
    private SceneObject _lastClickedObject;
    private SceneObject _selectionToReveal;
    private SceneObject _renamingObject;
    private readonly ObjectNameEditor _nameEditor = new();
    private bool _renameFocusPending;
    private string      _searchQuery = "";
    private HashSet<SceneObject>? _filteredVisibleSet;

    // Context-menu state
    private SceneObject _contextMenuTarget;
    private bool        _openContextMenu;
    private string? _exportMessage;

    // Drag-and-drop state
    private SceneObject _draggingObject;
    private List<SceneObject> _draggingObjects = [];
    private SceneObject? _pendingClickObject;
    private bool _movePending;
    private SceneObject? _moveTarget;
    private SceneDropPlacement _movePlacement;
    private SceneObject? _hoverDropTarget;
    private double _hoverDropSince;
    private int _hoverDropFrame = -1;
    private readonly Dictionary<SceneObject, bool> _openOverrides = new();

    private readonly List<List<SceneObject>> _childSnapshotsByDepth = [];
    private uint _lockedIcon;
    private uint _unlockedIcon;
    private uint _visibleIcon;
    private uint _hiddenIcon;

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
        ImGui.Begin(Localization.Label("text.common.scene_tree", "Scene Tree"));

        if (Viewport == null)
        {
            UiText.Disabled(Localization.T("text.panel.scene_tree.no_viewport"));
            ImGui.End();
            return;
        }

        if (_lockedIcon == 0)
            _lockedIcon = SvgLoader.LoadTexture(Gl, "MineImatorSimplyRemade.assets.img.button.mdi--lock.svg");
        if (_unlockedIcon == 0)
            _unlockedIcon = SvgLoader.LoadTexture(Gl, "MineImatorSimplyRemade.assets.img.button.mdi--lock-open-variant-outline.svg");
        if (_visibleIcon == 0)
            _visibleIcon = SvgLoader.LoadTexture(Gl, "MineImatorSimplyRemade.assets.img.button.mdi--eye-outline.svg");
        if (_hiddenIcon == 0)
            _hiddenIcon = SvgLoader.LoadTexture(Gl, "MineImatorSimplyRemade.assets.img.button.mdi--eye-off-outline.svg");

        _openContextMenu = false;

        if (_selectedObject != null && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
            Keybinds.Pressed("rename"))
            BeginRename(_selectedObject);

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) &&
            !ImGui.GetIO().WantTextInput && !ImGui.IsAnyItemActive() && _draggingObject == null &&
            Keybinds.Pressed("group"))
            GroupSelectedObjects();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##sceneTreeSearch", Localization.T("text.panel.scene_tree.search_scene_objects"), ref _searchQuery, 128);
        if (ImGui.SmallButton(Localization.Label("text.panel.scene_tree.expand_all", "Expand All")))
            SetAllExpanded(true);
        ImGui.SameLine();
        if (ImGui.SmallButton(Localization.Label("text.panel.scene_tree.collapse_all", "Collapse All")))
            SetAllExpanded(false);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Localization.T("text.panel.scene_tree.drag_help"));
        ImGui.Separator();
        float treeTop = ImGui.GetCursorScreenPos().Y;

        string searchTerm = _searchQuery.Trim();
        _filteredVisibleSet = string.IsNullOrEmpty(searchTerm)
            ? null
            : BuildFilterVisibleSet(searchTerm);

        // Draw each top-level object — snapshot the list first so that a
        // reparent/delete triggered during the same frame doesn't mutate it.
        int renderedRootCount = 0;
        if (ImGui.BeginTable("##sceneTreeItems", 3, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(Localization.Label("text.common.object", "Object"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Localization.Label("text.panel.scene_tree.lock", "Lock"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            ImGui.TableSetupColumn(Localization.Label("text.panel.scene_tree.visibility", "Visibility"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            foreach (var obj in GetChildSnapshot(Viewport.SceneObjects, depth: 0))
            {
                if (_filteredVisibleSet != null && !_filteredVisibleSet.Contains(obj))
                    continue;

                RenderNode(obj, _filteredVisibleSet, depth: 1);
                renderedRootCount++;
            }
            ImGui.EndTable();
        }

        if (_filteredVisibleSet != null && renderedRootCount == 0)
            UiText.Disabled(Localization.T("text.panel.scene_tree.no_scene_objects_match_the_current_search"));

        // The reveal request is only needed for one frame. By this point all
        // ancestors have been opened and the selected row has requested scroll.
        _selectionToReveal = null;

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
                if (_draggingObject != null &&
                    SceneHierarchy.CanMove(Viewport.SceneObjects, _draggingObjects, null, SceneDropPlacement.RootEnd))
                {
                    var payload = ImGui.AcceptDragDropPayload("SCENE_OBJECT");
                    if (payload.Handle != null && ImGui.IsDelivery(payload))
                        QueueMove(null, SceneDropPlacement.RootEnd);
                }
            }
            ImGui.EndDragDropTarget();
        }

        // Context menu (opened deferred to avoid conflicting with tree-click handling)
        if (_openContextMenu && _contextMenuTarget != null)
            ImGui.OpenPopup("##SceneTreeContextMenu");

        SceneObject exportTarget = null;
        if (ImGui.BeginPopup("##SceneTreeContextMenu"))
        {
            if (_contextMenuTarget != null)
            {
                UiText.Disabled(_contextMenuTarget.GetDisplayName());
                ImGui.Separator();

                if (ImGui.MenuItem(Localization.Label("text.common.rename", "Rename"), Keybinds.Label("rename")))
                    BeginRename(_contextMenuTarget);
                ImGui.Separator();

                var moveRoots = GetSelectedMoveRoots();
                if (ImGui.MenuItem(Localization.Label("text.panel.scene_tree.move_up", "Move Up"), "",
                        false, SceneHierarchy.CanShift(Viewport.SceneObjects, moveRoots, down: false)))
                    MoveSelectedObjects(down: false);
                if (ImGui.MenuItem(Localization.Label("text.panel.scene_tree.move_down", "Move Down"), "",
                        false, SceneHierarchy.CanShift(Viewport.SceneObjects, moveRoots, down: true)))
                    MoveSelectedObjects(down: true);
                if (ImGui.MenuItem(Localization.Label("text.panel.scene_tree.unparent", "Unparent to Root"), "",
                        false, moveRoots.Any(obj => obj.Parent != null)))
                    UnparentSelectedObjects();
                if (ImGui.MenuItem(Localization.Label("text.panel.scene_tree.group", "Group Selection"), Keybinds.Label("group")))
                    GroupSelectedObjects();
                ImGui.Separator();

                if (ImGui.MenuItem(Localization.Label("text.menu.hide", "Hide"), Keybinds.Label("hide")))
                    SetSelectedObjectsHidden(true);
                if (ImGui.MenuItem(Localization.Label("text.common.show", "Show"), Keybinds.Label("show")))
                    SetSelectedObjectsHidden(false);
                ImGui.Separator();

                if (ImGui.MenuItem(Localization.Label("text.common.duplicate", "Duplicate"), Keybinds.Label("duplicate")))
                {
                    DuplicateObject(_contextMenuTarget);
                    _contextMenuTarget = null;
                }

                if (_contextMenuTarget != null && ImGui.MenuItem(Localization.Label(
                        "text.panel.scene_tree.export_object", "Export Object (.nxObj)...")))
                {
                    exportTarget = _contextMenuTarget;
                    _contextMenuTarget = null;
                }

                if (_contextMenuTarget is CameraSceneObject cam)
                {
                    string activeLabel = cam.Active ? Localization.Label("text.panel.scene_tree.clear_active_camera", "Clear Active Camera") : Localization.Label("text.panel.scene_tree.set_as_active_camera", "Set as Active Camera");
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

                if (ImGui.MenuItem(Localization.Label("text.common.delete", "Delete"), Keybinds.Label("delete")))
                {
                    DeleteObject(_contextMenuTarget);
                    _contextMenuTarget = null;
                }
            }
            ImGui.EndPopup();
        }

        // Open the result at panel scope, not inside the context menu that just closed.
        string exportPopupTitle = Localization.Label("text.panel.scene_tree.object_export", "Object Export");
        if (exportTarget != null)
        {
            ExportObject(exportTarget);
            if (_exportMessage != null)
                ImGui.OpenPopup(exportPopupTitle);
        }

        bool exportPopupOpen = true;
        if (ImGui.BeginPopupModal(exportPopupTitle, ref exportPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 420f * UiTheme.Scale);
            ImGui.TextUnformatted(_exportMessage ?? "");
            ImGui.PopTextWrapPos();
            if (ImGui.Button(Localization.Label("text.window.main.close", "Close")))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        // Apply after traversing the tree so a moved row cannot render twice.
        if (_movePending)
        {
            if (SceneHierarchy.Move(Viewport.SceneObjects, _draggingObjects, _moveTarget, _movePlacement))
            {
                HierarchyChanged();
                if (_movePlacement == SceneDropPlacement.Inside && _moveTarget != null)
                    _openOverrides[_moveTarget] = true;
            }
            _movePending = false;
        }

        if (_draggingObject != null && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
        {
            float mouseY = ImGui.GetMousePos().Y;
            float bottom = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - ImGui.GetStyle().WindowPadding.Y;
            float speed = 350f * UiTheme.Scale * ImGui.GetIO().DeltaTime;
            if (mouseY < treeTop + 20f * UiTheme.Scale)
                ImGui.SetScrollY(Math.Max(0, ImGui.GetScrollY() - speed));
            else if (mouseY > bottom - 20f * UiTheme.Scale)
                ImGui.SetScrollY(ImGui.GetScrollY() + speed);
        }

        if (_pendingClickObject != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            HandleClick(_pendingClickObject);
            _pendingClickObject = null;
        }
        if (_draggingObject != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _draggingObject = null;
            _draggingObjects.Clear();
            _hoverDropTarget = null;
        }

        if (_nameEditor.CommitIfHidden())
            _renamingObject = null;
        ImGui.End();
    }

    private void ExportObject(SceneObject root)
    {
        _exportMessage = null;
        try
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string name = new string(root.GetDisplayName().Select(c =>
                char.IsControl(c) || invalidChars.Contains(c) ? '_' : c).Take(100).ToArray()).Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(name))
                name = "Object";
            string stem = name.Split('.')[0];
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
                name = "_" + name;
            if (!name.EndsWith(".nxObj", StringComparison.OrdinalIgnoreCase))
                name += ".nxObj";

            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), name);
            var result = Dialog.FileSave("nxObj", defaultPath);
            if (result.IsError)
            {
                _exportMessage = Localization.Format("text.panel.scene_tree.export_failed", result.ErrorMessage);
                return;
            }
            if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
                return;

            string path = result.Path.EndsWith(".nxObj", StringComparison.OrdinalIgnoreCase)
                ? result.Path : result.Path + ".nxObj";
            NxObjectArchive.Export(path, root);
            _exportMessage = Localization.Format("text.panel.scene_tree.export_succeeded", path);
        }
        catch (Exception ex)
        {
            _exportMessage = Localization.Format("text.panel.scene_tree.export_failed", ex.Message);
        }
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

    public void SelectAllObjects()
    {
        if (Viewport == null || SelectionManager.Instance == null) return;
        SelectionManager.Instance.ClearSelection();
        foreach (var obj in FlattenVisibleTree(_filteredVisibleSet))
            SelectionManager.Instance.SelectObject(obj);
    }

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
        var objectIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var original in duplicateRoots)
        {
            var duplicate = DuplicateObject(original, selectDuplicate: false, objectIds: objectIds);
            if (duplicate != null)
                duplicates.Add(duplicate);
        }

        if (duplicates.Count == 0)
            return;

        foreach (var duplicate in duplicates)
            RemapDuplicateReferences(duplicate, objectIds);

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

    /// <summary>Groups the selected hierarchy roots beneath a new empty object.</summary>
    public void GroupSelectedObjects()
    {
        var selectedObjects = SelectionManager.Instance?.SelectedObjects.ToList()
            ?? (_selectedObject != null ? new List<SceneObject> { _selectedObject } : new List<SceneObject>());
        if (Viewport == null || selectedObjects.Count == 0)
            return;

        // Keep selected descendants in their existing hierarchy rather than flattening them.
        var roots = SceneHierarchy.GetMoveRoots(Viewport.SceneObjects, selectedObjects);
        if (roots.Count == 0)
            return;

        var parent = roots[0].Parent;
        if (roots.Any(obj => obj.Parent != parent))
            parent = null;

        int nameNumber = GetNextAvailableNameNumber("Empty");
        var group = new SceneObject
        {
            Name = nameNumber == 1 ? "Empty" : $"Empty{nameNumber}",
            ObjectType = "Empty",
            SpawnCategory = "Primitives",
            PivotOffset = GlmSharp.vec3.Zero,
            ScaleResize = false
        };
        group.AssignObjectId();

        if (parent != null)
        {
            int index = parent.Children.ToList().IndexOf(roots[0]);
            parent.AddChild(group);
            parent.MoveChild(group, index);
        }
        else
        {
            int index = Viewport.SceneObjects.IndexOf(roots[0]);
            Viewport.SceneObjects.Insert(index >= 0 ? index : Viewport.SceneObjects.Count, group);
        }

        SceneHierarchy.Move(Viewport.SceneObjects, roots, group, SceneDropPlacement.Inside);

        SetSelection(group);
        MineImatorSimplyRemade.core.project.ProjectManager.Instance.SetDirty(true);
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

    public void SetSelectedObjectsHidden(bool hidden)
    {
        var selectedObjects = SelectionManager.Instance?.SelectedObjects.ToList()
            ?? (_selectedObject != null ? new List<SceneObject> { _selectedObject } : new List<SceneObject>());
        bool changed = false;
        foreach (var obj in selectedObjects)
        {
            changed |= obj.SetHiddenRecursive(hidden);
        }
        if (changed)
        {
            SelectionManager.Instance?.SyncGizmoSelection();
            MineImatorSimplyRemade.core.project.ProjectManager.Instance.SetDirty(true);
        }
    }

    private List<SceneObject> GetSelectedMoveRoots() =>
        SceneHierarchy.GetMoveRoots(Viewport.SceneObjects,
            SelectionManager.Instance?.SelectedObjects ??
            (_selectedObject != null ? new List<SceneObject> { _selectedObject } : []));

    public void MoveSelectedObjects(bool down)
    {
        if (Viewport != null && SceneHierarchy.Shift(Viewport.SceneObjects, GetSelectedMoveRoots(), down))
            HierarchyChanged();
    }

    public void UnparentSelectedObjects()
    {
        if (Viewport == null)
            return;
        var roots = GetSelectedMoveRoots().Where(obj => obj.Parent != null).ToList();
        if (roots.Count > 0 && SceneHierarchy.Move(Viewport.SceneObjects, roots, null, SceneDropPlacement.RootEnd))
            HierarchyChanged();
    }

    private void HierarchyChanged()
    {
        ProjectManager.Instance.SetDirty(true);
        SelectionManager.Instance?.SyncGizmoSelection();
    }

    private void QueueMove(SceneObject? target, SceneDropPlacement placement)
    {
        _moveTarget = target;
        _movePlacement = placement;
        _movePending = true;
    }

    private void SetAllExpanded(bool expanded)
    {
        _openOverrides.Clear();
        foreach (var obj in FlattenVisibleTree())
            _openOverrides[obj] = expanded;
        _selectionToReveal = null;
    }

    // ── Rendering helpers ───────────────────────────────────────────────────

    private void RenderNode(SceneObject obj, HashSet<SceneObject>? visibilityFilter, int depth)
    {
        if (obj.HideInSceneTree) return;

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();

        bool hasChildren = HasVisibleChildren(obj.Children, visibilityFilter);
        bool isSelected  = SelectionManager.Instance != null
            ? SelectionManager.Instance.IsSelected(obj)
            : _selectedObject == obj;
        bool isRenaming  = _renamingObject == obj;

        ImGuiTreeNodeFlags flags =
            ImGuiTreeNodeFlags.OpenOnArrow |
            ImGuiTreeNodeFlags.SpanAvailWidth;

        if (!hasChildren) flags |= ImGuiTreeNodeFlags.Leaf;
        if (isSelected)   flags |= ImGuiTreeNodeFlags.Selected;

        ImGui.PushID(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj));

        if (_openOverrides.Remove(obj, out bool expanded))
            ImGui.SetNextItemOpen(expanded, ImGuiCond.Always);
        else if (visibilityFilter != null && hasChildren)
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);

        // A viewport pick can select an item whose branch is collapsed. Open
        // every ancestor on the way to it before rendering the node so the
        // selected row exists this frame and can be scrolled into view.
        if (_selectionToReveal != null &&
            (_selectionToReveal == obj || _selectionToReveal.IsDescendantOf(obj)))
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        }

        bool nodeOpen;

        if (isRenaming)
        {
            nodeOpen = ImGui.TreeNodeEx("###node", flags);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            if (_nameEditor.Render(obj, "##rename_input", _renameFocusPending))
                _renamingObject = null;
            _renameFocusPending = false;
        }
        else
        {
            nodeOpen = ImGui.TreeNodeEx(obj.GetDisplayName() + "###node", flags);
        }

        if (_selectionToReveal == obj)
            ImGui.SetScrollHereY(0.5f);

        // Single click → select (multi-select with Ctrl/Shift)
        if (!isRenaming && ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen())
        {
            if (isSelected && !ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyShift)
                _pendingClickObject = obj;
            else
                HandleClick(obj);
        }

        // Double click → begin inline rename
        if (!isRenaming && ImGui.IsItemHovered() && !ImGui.IsItemToggledOpen() &&
            ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            BeginRename(obj);

        // Right click → context menu (don't clear multi-selection)
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (SelectionManager.Instance != null && !SelectionManager.Instance.IsSelected(obj))
            {
                SelectionManager.Instance.ClearSelection();
                SelectionManager.Instance.SelectObject(obj);
            }
            _contextMenuTarget = obj;
            _openContextMenu   = true;
        }

        // ── Drag source ─────────────────────────────────────────────────────
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();
        if (!isRenaming && ImGui.BeginDragDropSource())
        {
            if (_draggingObject == null)
            {
                _draggingObject = obj;
                _draggingObjects = SelectionManager.Instance?.IsSelected(obj) == true
                    ? GetSelectedMoveRoots()
                    : [obj];
            }
            _pendingClickObject = null;
            unsafe
            {
                byte dummy = 1;
                ImGui.SetDragDropPayload("SCENE_OBJECT", &dummy, 1);
            }
            ImGui.TextUnformatted(Localization.Format("text.panel.scene_tree.move", obj.GetDisplayName()));
            ImGui.TextUnformatted(Localization.Format("text.panel.scene_tree.move_count", _draggingObjects.Count));
            ImGui.TextUnformatted(Localization.T("text.panel.scene_tree.drag_help"));
            ImGui.EndDragDropSource();
        }

        // ── Drop target ─────────────────────────────────────────────────────
        if (!isRenaming && ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                float fraction = (ImGui.GetMousePos().Y - rowMin.Y) / Math.Max(1f, rowMax.Y - rowMin.Y);
                var placement = fraction < 0.25f ? SceneDropPlacement.Before :
                    fraction > 0.75f ? SceneDropPlacement.After : SceneDropPlacement.Inside;
                if (_draggingObject != null &&
                    SceneHierarchy.CanMove(Viewport.SceneObjects, _draggingObjects, obj, placement))
                {
                    var payload = ImGui.AcceptDragDropPayload("SCENE_OBJECT",
                        ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                    if (payload.Handle != null)
                    {
                        uint color = ImGui.GetColorU32(ImGuiCol.DragDropTarget);
                        var draw = ImGui.GetWindowDrawList();
                        if (placement == SceneDropPlacement.Inside)
                        {
                            draw.AddRect(rowMin, rowMax, color);
                            int frame = ImGui.GetFrameCount();
                            if (_hoverDropTarget != obj || _hoverDropFrame != frame - 1)
                            {
                                _hoverDropTarget = obj;
                                _hoverDropSince = ImGui.GetTime();
                            }
                            _hoverDropFrame = frame;
                            if (ImGui.GetTime() - _hoverDropSince >= 0.6)
                                _openOverrides[obj] = true;
                        }
                        else
                        {
                            _hoverDropTarget = null;
                            float y = placement == SceneDropPlacement.Before ? rowMin.Y : rowMax.Y;
                            draw.AddLine(new Vector2(rowMin.X, y), new Vector2(rowMax.X, y), color, 2f * UiTheme.Scale);
                        }
                        if (ImGui.IsDelivery(payload))
                            QueueMove(obj, placement);
                    }
                }
                else if (_draggingObject != null)
                    ImGui.SetTooltip(Localization.T("text.panel.scene_tree.invalid_drop"));
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.TableSetColumnIndex(1);
        RenderLockButton(obj);
        ImGui.TableSetColumnIndex(2);
        RenderVisibilityButton(obj);
        ImGui.TableSetColumnIndex(0);

        // ── Recurse ─────────────────────────────────────────────────────────
        if (nodeOpen)
        {
            foreach (var child in GetChildSnapshot(obj.Children, depth))
            {
                if (child.HideInSceneTree) continue;
                if (visibilityFilter != null && !visibilityFilter.Contains(child)) continue;
                RenderNode(child, visibilityFilter, depth + 1);
            }
            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private unsafe void RenderLockButton(SceneObject obj)
    {
        float iconSize = ImGui.GetTextLineHeight();
        uint texture = obj.IsLocked ? _lockedIcon : _unlockedIcon;
        Vector4 tint = obj.IsLocked
            ? new Vector4(1f, 0.75f, 0.3f, 1f)
            : new Vector4(1f, 1f, 1f, 0.45f);

        bool clicked;
        fixed (byte* id = "##lock\0"u8)
            clicked = ImGui.ImageButton(id, new ImTextureRef(texId: (ulong)texture),
                new Vector2(iconSize), Vector2.Zero, Vector2.One, Vector4.Zero, tint);

        if (clicked)
        {
            obj.SetLockedRecursive(!obj.IsLocked);
            MineImatorSimplyRemade.core.project.ProjectManager.Instance.SetDirty(true);
        }

        if (ImGui.IsItemHovered())
            UiText.Tooltip(obj.IsLocked
                ? Localization.T("text.panel.scene_tree.unlock_viewport_selection_for_this_item_and_all_descendants")
                : Localization.T("text.panel.scene_tree.lock_viewport_selection_for_this_item_and_all_descendants"));
    }

    private unsafe void RenderVisibilityButton(SceneObject obj)
    {
        float iconSize = ImGui.GetTextLineHeight();
        uint texture = obj.IsHidden ? _hiddenIcon : _visibleIcon;
        Vector4 tint = obj.IsHidden
            ? new Vector4(1f, 0.75f, 0.3f, 1f)
            : new Vector4(1f, 1f, 1f, 0.45f);

        bool clicked;
        fixed (byte* id = "##visibility\0"u8)
            clicked = ImGui.ImageButton(id, new ImTextureRef(texId: (ulong)texture),
                new Vector2(iconSize), Vector2.Zero, Vector2.One, Vector4.Zero, tint);

        if (clicked)
        {
            obj.SetHiddenRecursive(!obj.IsHidden);
            SelectionManager.Instance?.SyncGizmoSelection();
            MineImatorSimplyRemade.core.project.ProjectManager.Instance.SetDirty(true);
        }

        if (ImGui.IsItemHovered())
            UiText.Tooltip(obj.IsHidden
                ? Localization.T("text.panel.scene_tree.show_in_viewport")
                : Localization.T("text.panel.scene_tree.hide_in_viewport"));
    }

    private List<SceneObject> GetChildSnapshot(IReadOnlyCollection<SceneObject> children, int depth)
    {
        while (_childSnapshotsByDepth.Count <= depth)
            _childSnapshotsByDepth.Add([]);

        var snapshot = _childSnapshotsByDepth[depth];
        snapshot.Clear();
        snapshot.EnsureCapacity(children.Count);
        foreach (var child in children)
            snapshot.Add(child);
        return snapshot;
    }

    private static bool HasVisibleChildren(
        IEnumerable<SceneObject> children,
        HashSet<SceneObject>? visibilityFilter)
    {
        foreach (var child in children)
        {
            if (!child.HideInSceneTree &&
                (visibilityFilter == null || visibilityFilter.Contains(child)))
                return true;
        }

        return false;
    }

    // ── Selection ───────────────────────────────────────────────────────────

    private void HandleClick(SceneObject obj)
    {
        bool ctrlHeld  = ImGui.GetIO().KeyCtrl;
        bool shiftHeld = ImGui.GetIO().KeyShift;

        if (SelectionManager.Instance != null)
        {
            if (ctrlHeld)
            {
                SelectionManager.Instance.ToggleSelection(obj);
                _lastClickedObject = obj;
            }
            else if (shiftHeld && _lastClickedObject != null)
            {
                var flatTree = FlattenVisibleTree(_filteredVisibleSet);
                int startIdx = flatTree.IndexOf(_lastClickedObject);
                int endIdx   = flatTree.IndexOf(obj);
                if (startIdx >= 0 && endIdx >= 0)
                {
                    int low  = Math.Min(startIdx, endIdx);
                    int high = Math.Max(startIdx, endIdx);
                    for (int i = low; i <= high; i++)
                    {
                        if (!SelectionManager.Instance.IsSelected(flatTree[i]))
                            SelectionManager.Instance.SelectObject(flatTree[i]);
                    }
                }
                _lastClickedObject = obj;
            }
            else
            {
                SelectionManager.Instance.ClearSelection();
                if (obj != null)
                    SelectionManager.Instance.SelectObject(obj);
                _lastClickedObject = obj;
            }
        }
        else
        {
            if (_selectedObject != null) _selectedObject.IsSelected = false;
            _selectedObject = obj;
            if (_selectedObject != null) _selectedObject.IsSelected = true;
            _lastClickedObject = obj;
        }
    }

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
        if (_renamingObject != null && SelectionManager.Instance?.IsSelected(_renamingObject) != true)
        {
            _nameEditor.Commit();
            _renamingObject = null;
        }

        _selectedObject = SelectionManager.Instance?.SelectedObjects.Count > 0
            ? SelectionManager.Instance.SelectedObjects[0]
            : null;

        // The most recently-added object is the useful target for Ctrl-click
        // multi-selection; for ordinary selection it is also the sole object.
        _selectionToReveal = SelectionManager.Instance?.SelectedObjects.LastOrDefault();
    }

    private List<SceneObject> FlattenVisibleTree(HashSet<SceneObject>? visibilityFilter = null)
    {
        var result = new List<SceneObject>();
        if (Viewport == null) return result;
        foreach (var root in Viewport.SceneObjects)
            FlattenNode(root, result, visibilityFilter);
        return result;
    }

    private void FlattenNode(SceneObject obj, List<SceneObject> result, HashSet<SceneObject>? visibilityFilter)
    {
        if (obj.HideInSceneTree) return;
        if (visibilityFilter != null && !visibilityFilter.Contains(obj)) return;
        result.Add(obj);
        foreach (var child in obj.Children)
            FlattenNode(child, result, visibilityFilter);
    }

    private HashSet<SceneObject> BuildFilterVisibleSet(string searchTerm)
    {
        var visible = new HashSet<SceneObject>();

        if (Viewport == null)
            return visible;

        foreach (var root in Viewport.SceneObjects)
            PopulateFilterVisibleSet(root, searchTerm, visible);

        return visible;
    }

    private bool PopulateFilterVisibleSet(SceneObject obj, string searchTerm, HashSet<SceneObject> visible)
    {
        if (obj.HideInSceneTree)
            return false;

        bool selfMatches = obj.GetDisplayName().Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
        bool childMatches = false;

        foreach (var child in obj.Children)
            childMatches |= PopulateFilterVisibleSet(child, searchTerm, visible);

        if (selfMatches || childMatches)
        {
            visible.Add(obj);
            return true;
        }

        return false;
    }

    // ── Rename ──────────────────────────────────────────────────────────────

    private void BeginRename(SceneObject obj)
    {
        if (obj.HideInSceneTree)
            return;

        if (_filteredVisibleSet != null && !_filteredVisibleSet.Contains(obj))
            _searchQuery = "";

        _nameEditor.Commit();
        _pendingClickObject = null;
        _renamingObject = obj;
        _selectionToReveal = obj;
        _renameFocusPending = true;
    }

    // ── Duplicate ───────────────────────────────────────────────────────────

    private SceneObject DuplicateObject(SceneObject original, bool selectDuplicate = true,
        Dictionary<string, string>? objectIds = null)
    {
        bool remapReferences = objectIds == null;
        objectIds ??= new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicate = CreateSceneObjectDuplicate(original, objectIds);
        if (duplicate == null) return null;

        // Mine-imator characters are rebuilt by their loader, including their
        // complete bone hierarchy and mesh regeneration state.
        if (original is not CharacterSceneObject)
            DuplicateChildrenRecursive(original, duplicate, objectIds);

        if (remapReferences)
            RemapDuplicateReferences(duplicate, objectIds);

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

    private void DuplicateChildrenRecursive(SceneObject original, SceneObject duplicateParent,
        Dictionary<string, string> objectIds)
    {
        foreach (var child in original.Children)
        {
            if (child.IsRuntimeTransient) continue;

            var childDup = CreateSceneObjectDuplicate(child, objectIds);
            if (childDup == null) continue;

            duplicateParent.AddChild(childDup);
            UpdateDuplicatedBoneOwner(childDup);
            if (child is not CharacterSceneObject)
                DuplicateChildrenRecursive(child, childDup, objectIds);
        }
    }

    /// <summary>Shallow-copies a SceneObject without copying its children.</summary>
    private SceneObject CreateSceneObjectDuplicate(SceneObject original, Dictionary<string, string> objectIds)
    {
        SceneObject dup;

        switch (original)
        {
            case CharacterSceneObject character when character.SourceMineImatorModel != null:
                var characterDuplicate = MineImatorLoader.Instance.CreateCharacterFromModel(
                    character.SourceMineImatorModel);
                if (characterDuplicate == null) return null;

                characterDuplicate.CharacterName = character.CharacterName;
                characterDuplicate.ModelBendStyle = character.ModelBendStyle;
                dup = characterDuplicate;
                break;

            case CharacterSceneObject:
                return null;

            case ProceduralEffectSceneObject effect:
                dup = new ProceduralEffectSceneObject { Settings = effect.Settings };
                break;
            case PathSceneObject path:
                dup = new PathSceneObject { Settings = path.Settings };
                break;
            case PathPointSceneObject point:
                dup = new PathPointSceneObject { PathPointAngle = point.PathPointAngle, PathPointScale = point.PathPointScale };
                break;

            case ParticleSpawnerSceneObject particleSpawner:
                dup = new ParticleSpawnerSceneObject
                {
                    ParticleLibraryEntryId = particleSpawner.ParticleLibraryEntryId,
                    Seed = particleSpawner.Seed,
                    Freeze = particleSpawner.Freeze,
                    AttractorObjectId = particleSpawner.AttractorObjectId,
                    AttractorForce = particleSpawner.AttractorForce,
                    SimulationSpeed = particleSpawner.SimulationSpeed,
                    LinearDamping = particleSpawner.LinearDamping,
                    AngularDamping = particleSpawner.AngularDamping,
                    EmissionShape = particleSpawner.EmissionShape,
                    UseDirectionalEmission = particleSpawner.UseDirectionalEmission,
                    Direction = particleSpawner.Direction,
                    SpreadDegrees = particleSpawner.SpreadDegrees,
                    InitialSpeedMin = particleSpawner.InitialSpeedMin,
                    InitialSpeedMax = particleSpawner.InitialSpeedMax,
                    ParticleLibraryDisplayName = particleSpawner.ParticleLibraryDisplayName,
                    Emitting = particleSpawner.Emitting,
                    OneShot = particleSpawner.OneShot,
                    Amount = particleSpawner.Amount,
                    SpawnRate = particleSpawner.SpawnRate,
                    LifetimeMin = particleSpawner.LifetimeMin,
                    LifetimeMax = particleSpawner.LifetimeMax,
                    SpawnBoxExtents = particleSpawner.SpawnBoxExtents,
                    InitialVelocityMin = particleSpawner.InitialVelocityMin,
                    InitialVelocityMax = particleSpawner.InitialVelocityMax,
                    Gravity = particleSpawner.Gravity,
                    InitialRotationMinDegrees = particleSpawner.InitialRotationMinDegrees,
                    InitialRotationMaxDegrees = particleSpawner.InitialRotationMaxDegrees,
                    AngularVelocityMinDegrees = particleSpawner.AngularVelocityMinDegrees,
                    AngularVelocityMaxDegrees = particleSpawner.AngularVelocityMaxDegrees,
                    StartScaleMin = particleSpawner.StartScaleMin,
                    StartScaleMax = particleSpawner.StartScaleMax,
                    EndScaleMin = particleSpawner.EndScaleMin,
                    EndScaleMax = particleSpawner.EndScaleMax,
                    TopLevelParticles = particleSpawner.TopLevelParticles
                };
                ((ParticleSpawnerSceneObject)dup).SetPreset(particleSpawner.Preset);
                break;

            case LightSceneObject light:
                dup = new LightSceneObject
                {
                    Type               = light.Type,
                    LightColor         = light.LightColor,
                    LightEnergy        = light.LightEnergy,
                    LightRange         = light.LightRange,
                    LightIndirectEnergy = light.LightIndirectEnergy,
                    LightSpecular      = light.LightSpecular,
                    LightShadowEnabled = light.LightShadowEnabled,
                    LightSpotAngle     = light.LightSpotAngle,
                    LightSpotBlend     = light.LightSpotBlend
                };
                break;

            case CameraSceneObject cam:
                dup = new CameraSceneObject
                {
                    Fov  = cam.Fov,
                    Near = cam.Near,
                    Far  = cam.Far,
                    BladeCount = cam.BladeCount,
                    BladeAngle = cam.BladeAngle,
                    BladeStretch = cam.BladeStretch,
                    OrbitEnabled = cam.OrbitEnabled,
                    OrbitDistance = cam.OrbitDistance,
                    OrbitRotationXY = cam.OrbitRotationXY,
                    OrbitRotationZ = cam.OrbitRotationZ,
                    OrbitLookAtPoint = cam.OrbitLookAtPoint,
                    ColorManagementOverrideEnabled = cam.ColorManagementOverrideEnabled,
                    ColorManagementEnabled = cam.ColorManagementEnabled,
                    ColorManagementToneMapper = cam.ColorManagementToneMapper,
                    ColorManagementExposure = cam.ColorManagementExposure,
                    ColorManagementGamma = cam.ColorManagementGamma,
                    ColorManagementLook = cam.ColorManagementLook
                };
                foreach (var effect in cam.Effects)
                    ((CameraSceneObject)dup).Effects.Add(CameraSceneObject.CloneEffect(effect));
                // Duplicates always start inactive so only one camera can be
                // active at a time.  We still copy the visual set lists so
                // RefreshActiveMesh can hide the right meshes.
                foreach (var mesh in cam.InactiveVisuals)
                    ((CameraSceneObject)dup).InactiveVisuals.Add(mesh);
                foreach (var mesh in cam.ActiveVisuals)
                    ((CameraSceneObject)dup).ActiveVisuals.Add(mesh);
                ((CameraSceneObject)dup).RefreshActiveMesh();
                break;

            case MiBoneSceneObject mineImatorBone:
                dup = new MiBoneSceneObject { ModelOwner = mineImatorBone.ModelOwner };
                break;

            case BoneSceneObject:
                dup = new BoneSceneObject();
                break;

            default:
                dup = new SceneObject();
                break;
        }

        // Assign a fresh object ID / pick-colour ID. Without this the duplicate
        // keeps the default PickColorId of 0, which the viewport's colour-pick
        // pass treats as "nothing" — the object would render correctly but
        // could never be selected again by clicking on it.
        dup.AssignObjectId();
        if (!string.IsNullOrEmpty(original.ObjectId))
            objectIds[original.ObjectId] = dup.ObjectId;
        CopyBoneMetadata(original, dup);

        var baseName = GetBaseName(original.GetDisplayName());
        int nextNum  = GetNextAvailableNameNumber(baseName);
        dup.Name = nextNum > 1 ? $"{baseName}{nextNum}" : baseName;

        dup.ObjectType          = original.ObjectType;
        dup.LibrarySourceId     = original.LibrarySourceId;
        dup.IsSelectable        = original.IsSelectable;
        dup.IsLocked            = original.IsLocked;
        dup.IsHidden            = original.IsHidden;
        dup.Position            = original.Position;
        dup.PathFollowObjectId  = original.PathFollowObjectId;
        dup.PathFollowOffset    = original.PathFollowOffset;
        dup.Modifiers = original.Modifiers.ToList();
        dup.Rotation            = original.Rotation;
        dup.Scale               = original.Scale;
        dup.PivotOffset         = original.PivotOffset;
        dup.PivotOffsetInheritsModelOrigin = original.PivotOffsetInheritsModelOrigin;
        dup.InheritPivotOffset  = original.InheritPivotOffset;
        dup.ObjectVisible       = original.ObjectVisible;
        dup.InvertFaces         = original.InvertFaces;
        dup.InheritVisibility   = original.InheritVisibility;
        dup.InheritPosition     = original.InheritPosition;
        dup.InheritRotation     = original.InheritRotation;
        dup.InheritScale        = original.InheritScale;
        CopyPropertyInheritanceSettings(original, dup, original is CharacterSceneObject, objectIds);
        dup.ScaleResize         = original.ScaleResize;
        dup.LockBend            = original.LockBend;
        dup.LockToBentHalf      = original is MiBoneSceneObject ? original.LockBend > 0f : original.LockToBentHalf;
        dup.CastShadow          = original.CastShadow;
        dup.BlurTexture         = original.BlurTexture;
        dup.TextureMipmaps      = original.TextureMipmaps;
        dup.IncludeInAmbientOcclusion = original.IncludeInAmbientOcclusion;
        dup.IncludeInFog        = original.IncludeInFog;
        dup.AffectedByWind      = original.AffectedByWind;
        dup.TerrainAffectedByWind = original.TerrainAffectedByWind;
        dup.RenderInHighQuality = original.RenderInHighQuality;
        dup.RenderInLowQuality  = original.RenderInLowQuality;
        dup.RenderDepthOffset   = original.RenderDepthOffset;
        dup.SpawnCategory   = original.SpawnCategory;
        dup.BlockVariant    = original.BlockVariant;
        dup.TextureType     = original.TextureType;
        dup.ResourcePackId  = original.ResourcePackId;
        dup.NormalResourcePackId = original.NormalResourcePackId;
        dup.EmissionResourcePackId = original.EmissionResourcePackId;
        dup.SourceAssetPath = original.SourceAssetPath;
        dup.SourceNodePath = original.SourceNodePath?.ToList();
        dup.RestoreExactChildren = original.RestoreExactChildren;
        dup.AlbedoTexturePath   = original.AlbedoTexturePath;
        dup.NormalTexturePath = original.NormalTexturePath;
        dup.EmissionTexturePath = original.EmissionTexturePath;
        dup.SurfaceTexturePath = original.SurfaceTexturePath;
        dup.OcclusionTexturePath = original.OcclusionTexturePath;
        dup.RoughnessTexturePath = original.RoughnessTexturePath;
        dup.MetallicTexturePath = original.MetallicTexturePath;
        dup.HeightTexturePath = original.HeightTexturePath;
        dup.CameraTextureObjectId = original.CameraTextureObjectId;
        dup.PrimitiveCubeMapped = original.PrimitiveCubeMapped;
        dup.PrimitiveSphereSmooth = original.PrimitiveSphereSmooth;
        dup.PrimitiveSphereSegments = original.PrimitiveSphereSegments;
        dup.PrimitiveSphereRings = original.PrimitiveSphereRings;
        dup.PrimitiveRadialSegments = original.PrimitiveRadialSegments;
        dup.PrimitiveRadialSmooth = original.PrimitiveRadialSmooth;
        dup.PrimitiveRadialClosed = original.PrimitiveRadialClosed;
        dup.PrimitiveRadialMapped = original.PrimitiveRadialMapped;
        dup.PrimitivePlaneFaceCamera = original.PrimitivePlaneFaceCamera;
        dup.ItemBounce = original.ItemBounce;
        dup.ItemSpin = original.ItemSpin;
        dup.ItemFaceCamera = original.ItemFaceCamera;
        dup.ItemTileKey = original.ItemTileKey;
        dup.TemporaryItemSheetPath = original.TemporaryItemSheetPath;
        dup.TemporaryItemSheetCacheKey = original.TemporaryItemSheetCacheKey;
        dup.TemporaryItemSheetColumns = original.TemporaryItemSheetColumns;
        dup.TemporaryItemSheetRows = original.TemporaryItemSheetRows;
        dup.TemporaryItemSheetColumnIndex = original.TemporaryItemSheetColumnIndex;
        dup.TemporaryItemSheetRowIndex = original.TemporaryItemSheetRowIndex;
        dup.TextMeshFontPath = original.TextMeshFontPath;
        dup.TextMeshBaseString = original.TextMeshBaseString;
        dup.TextMeshStringOverride = original.TextMeshStringOverride;
        dup.TextMeshExtruded = original.TextMeshExtruded;
        dup.TextMeshExtrusionDepth = original.TextMeshExtrusionDepth;
        dup.TextMeshFaceCamera = original.TextMeshFaceCamera;
        dup.TextMeshHorizontalAlignment = original.TextMeshHorizontalAlignment;
        dup.TextMeshVerticalAlignment = original.TextMeshVerticalAlignment;
        dup.TextMeshAntialiasing = original.TextMeshAntialiasing;
        dup.TextMeshFontSize = original.TextMeshFontSize;
        dup.TextMeshOutlineEnabled = original.TextMeshOutlineEnabled;
        dup.TextMeshOutlineColor = original.TextMeshOutlineColor;
        dup.TextMeshOutlineThickness = original.TextMeshOutlineThickness;
        dup.TextureOverridePath = original.TextureOverridePath;
        dup.TileX           = original.TileX;
        dup.TileY           = original.TileY;
        dup.TileZ           = original.TileZ;

        // Clone each mesh instead of sharing the original's instance — sharing
        // meant editing material/geometry on either object silently mutated
        // both, and the duplicate's material settings were lost the moment
        // any Properties-panel edit lazily created a fresh default
        // MaterialSettings and stomped the shared mesh.
        if (original is not MiBoneSceneObject)
            foreach (var mesh in original.Visuals)
                dup.AddMesh(mesh.Clone());

        if (dup is MiBoneSceneObject miDuplicate && miDuplicate.IndicatorMesh == null &&
            original is MiBoneSceneObject { IndicatorMesh: not null } && Gl != null)
            miDuplicate.CreateIndicator(Gl);

        // Preserve the exact material state (own explicit settings, or
        // inherited-from-parent) instead of leaving it null/default.
        dup.CopyMaterialSettingsFrom(original);

        return dup;
    }

    private static void CopyBoneMetadata(SceneObject original, SceneObject duplicate)
    {
        if (original is not BoneSceneObject source || duplicate is not BoneSceneObject target)
            return;

        if (source is MiBoneSceneObject miSource && target is MiBoneSceneObject miTarget)
            miTarget.CopyConstructionStateFrom(miSource);
        target.BoneName = source.BoneName;
        target.EndOffset = source.EndOffset;
        target.Constraints.Clear();
        foreach (var constraint in source.Constraints)
            target.Constraints.Add(constraint.Clone());
    }

    private static void RemapDuplicateReferences(SceneObject duplicate, IReadOnlyDictionary<string, string> objectIds)
    {
        ProjectSceneSerializer.RemapObjectReferences(duplicate, objectIds);
        foreach (var child in duplicate.Children)
            RemapDuplicateReferences(child, objectIds);
    }

    private static void UpdateDuplicatedBoneOwner(SceneObject duplicate)
    {
        if (duplicate is not MiBoneSceneObject bone) return;
        for (var ancestor = duplicate.Parent; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor is CharacterSceneObject character)
            {
                bone.ModelOwner = character;
                break;
            }
    }

    private void CopyPropertyInheritanceSettings(SceneObject original, SceneObject duplicate, bool copyChildren,
        Dictionary<string, string> objectIds)
    {
        duplicate.UsePropertyInheritance = original.UsePropertyInheritance;
        duplicate.InheritAlpha = original.InheritAlpha;
        duplicate.InheritColor = original.InheritColor;
        duplicate.InheritTexture = original.InheritTexture;
        duplicate.InheritSurface = original.InheritSurface;
        duplicate.InheritSubsurface = original.InheritSubsurface;
        duplicate.InheritGlowColor = original.InheritGlowColor;
        duplicate.InheritSelect = original.InheritSelect;
        duplicate.InheritPose = original.InheritPose;
        duplicate.Modifiers = original.Modifiers.ToList();
        duplicate.CopyConstraints.Clear();
        foreach (var constraint in original.CopyConstraints)
            duplicate.CopyConstraints.Add(constraint.Clone());
        duplicate.Keyframes = original.Keyframes.ToDictionary(pair => pair.Key, pair => pair.Value.Select(key => new ObjectKeyframe
        {
            Frame = key.Frame,
            Value = key.Value,
            InterpolationType = key.InterpolationType
        }).ToList());

        if (!copyChildren) return;

        // Character descendants are rebuilt by the loader instead of the normal copy path.
        var usedChildren = new HashSet<SceneObject>();
        foreach (var child in original.Children)
        {
            var childDuplicate = duplicate.Children.FirstOrDefault(candidate =>
                !usedChildren.Contains(candidate) && candidate.ObjectType == child.ObjectType &&
                (candidate is BoneSceneObject candidateBone && child is BoneSceneObject childBone
                    ? candidateBone.BoneName == childBone.BoneName
                    : candidate.Name == child.Name));
            if (childDuplicate == null)
            {
                if (child.IsRuntimeTransient) continue;
                childDuplicate = CreateSceneObjectDuplicate(child, objectIds);
                if (childDuplicate == null) continue;
                duplicate.AddChild(childDuplicate);
                UpdateDuplicatedBoneOwner(childDuplicate);
                if (child is not CharacterSceneObject)
                    DuplicateChildrenRecursive(child, childDuplicate, objectIds);
                usedChildren.Add(childDuplicate);
                continue;
            }

            usedChildren.Add(childDuplicate);
            if (!string.IsNullOrEmpty(child.ObjectId))
                objectIds[child.ObjectId] = childDuplicate.ObjectId;
            CopyBoneMetadata(child, childDuplicate);
            childDuplicate.NormalResourcePackId = child.NormalResourcePackId;
            childDuplicate.EmissionResourcePackId = child.EmissionResourcePackId;
            childDuplicate.NormalTexturePath = child.NormalTexturePath;
            childDuplicate.EmissionTexturePath = child.EmissionTexturePath;
            childDuplicate.SurfaceTexturePath = child.SurfaceTexturePath;
            childDuplicate.OcclusionTexturePath = child.OcclusionTexturePath;
            childDuplicate.RoughnessTexturePath = child.RoughnessTexturePath;
            childDuplicate.MetallicTexturePath = child.MetallicTexturePath;
            childDuplicate.HeightTexturePath = child.HeightTexturePath;
            CopyPropertyInheritanceSettings(child, childDuplicate, true, objectIds);
            childDuplicate.CopyMaterialSettingsFrom(child);
        }
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    private void DeleteObject(SceneObject obj)
    {
        ResetParticleSpawners(obj);

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

    private static void ResetParticleSpawners(SceneObject obj)
    {
        if (obj is ParticleSpawnerSceneObject spawner)
            spawner.ResetRuntime();

        foreach (var child in obj.Children.ToArray())
            ResetParticleSpawners(child);
    }

    private void RemoveDescendantsFromViewport(SceneObject obj)
    {
        foreach (var child in obj.Children.ToList())
        {
            Viewport?.SceneObjects.Remove(child);
            RemoveDescendantsFromViewport(child);
        }
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
