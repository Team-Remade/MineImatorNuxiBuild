using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
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
///  • Multi-selection (Ctrl+click toggle, Shift+click range)
///  • Inline rename (double-click label area)
///  • Right-click context menu (preserves multi-selection)
///  • Drag-and-drop reparenting (drop on item = child; drop on blank = unparent to root)
///
/// Not yet implemented
/// ────────────────────
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
    private SceneObject _lastClickedObject;
    private SceneObject _selectionToReveal;
    private SceneObject _renamingObject;
    private string      _renameBuffer = "";
    private string      _searchQuery = "";
    private HashSet<SceneObject>? _filteredVisibleSet;

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

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##sceneTreeSearch", "Search scene objects...", ref _searchQuery, 128);
        ImGui.Separator();

        string searchTerm = _searchQuery.Trim();
        _filteredVisibleSet = string.IsNullOrEmpty(searchTerm)
            ? null
            : BuildFilterVisibleSet(searchTerm);

        // Draw each top-level object — snapshot the list first so that a
        // reparent/delete triggered during the same frame doesn't mutate it.
        int renderedRootCount = 0;
        foreach (var obj in Viewport.SceneObjects.ToList())
        {
            if (_filteredVisibleSet != null && !_filteredVisibleSet.Contains(obj))
                continue;

            RenderNode(obj, _filteredVisibleSet);
            renderedRootCount++;
        }

        if (_filteredVisibleSet != null && renderedRootCount == 0)
            ImGui.TextDisabled("No scene objects match the current search.");

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

    /// <summary>Renames an object without depending on the immediate-mode editor.</summary>
    public bool RenameObject(SceneObject obj, string name)
    {
        if (obj == null) return false;
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed == obj.GetDisplayName()) return false;
        obj.Name = trimmed;
        ProjectManager.Instance.SetDirty(true);
        return true;
    }

    /// <summary>Moves an object in the hierarchy; a null parent moves it to the scene root.</summary>
    public bool ReparentObject(SceneObject obj, SceneObject? newParent)
    {
        if (obj == null || ReferenceEquals(obj, newParent) ||
            (newParent != null && newParent.IsDescendantOf(obj)))
            return false;

        if (ReferenceEquals(obj.Parent, newParent)) return false;
        if (obj.Parent != null)
            obj.Parent.RemoveChild(obj);
        else
            Viewport?.SceneObjects.Remove(obj);

        if (newParent != null)
            newParent.AddChild(obj);
        else if (Viewport != null && !Viewport.SceneObjects.Contains(obj))
            Viewport.SceneObjects.Add(obj);

        ProjectManager.Instance.SetDirty(true);
        return true;
    }

    // ── Rendering helpers ───────────────────────────────────────────────────

    private void RenderNode(SceneObject obj, HashSet<SceneObject>? visibilityFilter)
    {
        if (obj.HideInSceneTree) return;

        int nodeId    = ++_nodeIdCounter;
        bool hasChildren = obj.Children.Any(c => !c.HideInSceneTree &&
            (visibilityFilter == null || visibilityFilter.Contains(c)));
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
            nodeOpen = ImGui.TreeNodeEx("##renaming", flags);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            ImGui.SetKeyboardFocusHere();
            if (ImGui.InputText("##rename_input", ref _renameBuffer, 128,
                    ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll) || !ImGui.IsItemActive() && !ImGui.IsItemFocused())
            {
                CommitRename(obj);
            }
        }
        else
        {
            nodeOpen = ImGui.TreeNodeEx(obj.GetDisplayName() + "##node", flags);
        }

        if (_selectionToReveal == obj)
            ImGui.SetScrollHereY(0.5f);

        // Single click → select (multi-select with Ctrl/Shift)
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen())
            HandleClick(obj);

        // Double click → begin inline rename
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
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
            {
                if (child.HideInSceneTree) continue;
                if (visibilityFilter != null && !visibilityFilter.Contains(child)) continue;
                RenderNode(child, visibilityFilter);
            }
            ImGui.TreePop();
        }

        ImGui.PopID();
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
            if (child is CharacterSceneObject || child.IsRuntimeTransient) continue;

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
            case ParticleSpawnerSceneObject particleSpawner:
                dup = new ParticleSpawnerSceneObject
                {
                    ParticleLibraryEntryId = particleSpawner.ParticleLibraryEntryId,
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
                    Far  = cam.Far
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

            default:
                dup = new SceneObject();
                break;
        }

        // Assign a fresh object ID / pick-colour ID. Without this the duplicate
        // keeps the default PickColorId of 0, which the viewport's colour-pick
        // pass treats as "nothing" — the object would render correctly but
        // could never be selected again by clicking on it.
        dup.AssignObjectId();

        var baseName = GetBaseName(original.GetDisplayName());
        int nextNum  = GetNextAvailableNameNumber(baseName);
        dup.Name = nextNum > 1 ? $"{baseName}{nextNum}" : baseName;

        dup.ObjectType          = original.ObjectType;
        dup.LibrarySourceId     = original.LibrarySourceId;
        dup.IsSelectable        = original.IsSelectable;
        dup.Position            = original.Position;
        dup.Rotation            = original.Rotation;
        dup.Scale               = original.Scale;
        dup.PivotOffset         = original.PivotOffset;
        dup.InheritPivotOffset  = original.InheritPivotOffset;
        dup.ObjectVisible       = original.ObjectVisible;
        dup.InvertFaces         = original.InvertFaces;
        dup.InheritVisibility   = original.InheritVisibility;
        dup.InheritPosition     = original.InheritPosition;
        dup.InheritRotation     = original.InheritRotation;
        dup.InheritScale        = original.InheritScale;
        dup.CastShadow          = original.CastShadow;
        dup.BlurTexture         = original.BlurTexture;
        dup.TextureMipmaps      = original.TextureMipmaps;
        dup.IncludeInAmbientOcclusion = original.IncludeInAmbientOcclusion;
        dup.IncludeInFog        = original.IncludeInFog;
        dup.RenderInHighQuality = original.RenderInHighQuality;
        dup.RenderInLowQuality  = original.RenderInLowQuality;
        dup.RenderDepthOffset   = original.RenderDepthOffset;
        dup.SpawnCategory   = original.SpawnCategory;
        dup.BlockVariant    = original.BlockVariant;
        dup.TextureType     = original.TextureType;
        dup.ResourcePackId  = original.ResourcePackId;
        dup.SourceAssetPath = original.SourceAssetPath;
        dup.AlbedoTexturePath   = original.AlbedoTexturePath;
        dup.CameraTextureObjectId = original.CameraTextureObjectId;
        dup.PrimitiveCubeMapped = original.PrimitiveCubeMapped;
        dup.PrimitiveSphereSmooth = original.PrimitiveSphereSmooth;
        dup.PrimitiveSphereSegments = original.PrimitiveSphereSegments;
        dup.PrimitiveSphereRings = original.PrimitiveSphereRings;
        dup.PrimitivePlaneFaceCamera = original.PrimitivePlaneFaceCamera;
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
        foreach (var mesh in original.Visuals)
            dup.AddMesh(mesh.Clone());

        // Preserve the exact material state (own explicit settings, or
        // inherited-from-parent) instead of leaving it null/default.
        dup.CopyMaterialSettingsFrom(original);

        return dup;
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    private void DeleteObject(SceneObject obj)
    {
        if (obj is ParticleSpawnerSceneObject particleSpawner)
            particleSpawner.ResetRuntime();

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
