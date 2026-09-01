using System;
using System.Collections.Generic;
using System.Linq;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// Scene-tree model.  Originally an ImGui <c>UiPanel</c> (ported from the Nuxi
/// reference project) that both stored the scene hierarchy logic AND drew the
/// tree with ImGui.
///
/// MIGRATION: in the Avalonia port the tree UI moved to
/// <see cref="core.ui.Dock.SceneTreeView"/> (a native <c>TreeView</c>), so this
/// class is now a plain model. It owns the selection / duplicate / delete /
/// reparent / rename logic and raises <see cref="TreeChanged"/> whenever the
/// hierarchy is mutated so the view can rebuild.
///
/// The old panel read the scene roots from <c>Viewport.SceneObjects</c>. Since
/// the Viewport isn't ported to Avalonia/Veldrid yet, the roots collection is
/// injected via <see cref="SceneRoots"/> instead of taking a hard dependency on
/// the still-broken <c>Viewport</c> type. Once Viewport lands, its host just
/// assigns <c>sceneTree.SceneRoots = viewport.SceneObjects</c>.
///
/// Supported features
/// ──────────────────
///  • Recursive tree built from <see cref="SceneRoots"/> + SceneObject.Children
///  • Multi-selection (delegated to <see cref="SelectionManager"/>)
///  • Rename, duplicate, delete, drag-and-drop reparenting (invoked by the view)
///
/// Not yet implemented
/// ────────────────────
///  • Per-type object icons
///  • Keyframe deep-copy on Duplicate
/// </summary>
public class SceneTree
{
    // ── Data source ─────────────────────────────────────────────────────────

    /// <summary>
    /// The root-level scene objects. Injected by the host (formerly
    /// <c>Viewport.SceneObjects</c>). Defaults to an empty list so the view can
    /// bind safely before a real scene is available.
    /// </summary>
    public IList<SceneObject> SceneRoots { get; set; } = new List<SceneObject>();

    // ── State ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirror of the first entry in SelectionManager.SelectedObjects, kept in sync
    /// via the SelectionChanged event (fallback when no SelectionManager exists).
    /// </summary>
    private SceneObject _selectedObject;
    private SceneObject _lastClickedObject;

    /// <summary>Raised whenever the hierarchy is mutated (duplicate/delete/reparent/rename).</summary>
    public event Action TreeChanged;

    private void RaiseTreeChanged() => TreeChanged?.Invoke();

    // ── Constructor ─────────────────────────────────────────────────────────

    public SceneTree()
    {
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

    /// <summary>
    /// Forces the selection to match an externally-chosen object
    /// (e.g. a viewport colour-pick).
    /// </summary>
    public void SetSelection(SceneObject obj)
    {
        if (SelectionManager.Instance == null)
        {
            SelectObject(obj);
            return;
        }
        SelectionManager.Instance.ClearSelection();
        if (obj != null)
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

        RaiseTreeChanged();
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

    // ── Selection ───────────────────────────────────────────────────────────

    /// <summary>
    /// Selects <paramref name="obj"/> respecting Ctrl (toggle) and Shift (range)
    /// modifiers, mirroring the old ImGui click handling.
    /// </summary>
    public void HandleClick(SceneObject obj, bool ctrlHeld, bool shiftHeld, string searchTerm = "")
    {
        if (SelectionManager.Instance != null)
        {
            if (ctrlHeld)
            {
                SelectionManager.Instance.ToggleSelection(obj);
                _lastClickedObject = obj;
            }
            else if (shiftHeld && _lastClickedObject != null)
            {
                var filter = string.IsNullOrEmpty(searchTerm) ? null : BuildFilterVisibleSet(searchTerm);
                var flatTree = FlattenVisibleTree(filter);
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
    }

    public bool IsSelected(SceneObject obj)
    {
        return SelectionManager.Instance != null
            ? SelectionManager.Instance.IsSelected(obj)
            : _selectedObject == obj;
    }

    // ── Tree traversal helpers (used by the view) ─────────────────────────────

    public List<SceneObject> FlattenVisibleTree(HashSet<SceneObject>? visibilityFilter = null)
    {
        var result = new List<SceneObject>();
        if (SceneRoots == null) return result;
        foreach (var root in SceneRoots)
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

    public HashSet<SceneObject> BuildFilterVisibleSet(string searchTerm)
    {
        var visible = new HashSet<SceneObject>();

        if (SceneRoots == null)
            return visible;

        foreach (var root in SceneRoots)
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

    public void RenameObject(SceneObject obj, string newName)
    {
        if (obj == null) return;
        var trimmed = newName?.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            obj.Name = trimmed;
            RaiseTreeChanged();
        }
    }

    // ── Duplicate ───────────────────────────────────────────────────────────

    public SceneObject DuplicateObject(SceneObject original, bool selectDuplicate = true)
    {
        var duplicate = CreateSceneObjectDuplicate(original);
        if (duplicate == null) return null;

        DuplicateChildrenRecursive(original, duplicate);

        if (original.Parent != null)
            original.Parent.AddChild(duplicate);
        else
            SceneRoots?.Add(duplicate);

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

            RaiseTreeChanged();
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

        // TODO(migration): the old GL Mesh exposed a Clone() so each duplicate
        // owned an independent copy (editing material/geometry on either object
        // wouldn't mutate the other). The new VeldridMesh has no Clone() yet, so
        // for now the duplicate shares the original's mesh instances. Restore a
        // deep copy once VeldridMesh gains a Clone()/copy path.
        foreach (var mesh in original.Visuals)
            dup.AddMesh(mesh);

        // Preserve the exact material state (own explicit settings, or
        // inherited-from-parent) instead of leaving it null/default.
        dup.CopyMaterialSettingsFrom(original);

        return dup;
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    public void DeleteObject(SceneObject obj)
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
            SceneRoots?.Remove(obj);

        RemoveDescendantsFromViewport(obj);

        RaiseTreeChanged();
    }

    private void RemoveDescendantsFromViewport(SceneObject obj)
    {
        foreach (var child in obj.Children.ToList())
        {
            SceneRoots?.Remove(child);
            RemoveDescendantsFromViewport(child);
        }
    }

    // ── Reparent ────────────────────────────────────────────────────────────

    public void ReparentObject(SceneObject obj, SceneObject newParent)
    {
        if (obj == null) return;
        if (newParent != null && (newParent == obj || newParent.IsDescendantOf(obj)))
            return;

        if (obj.Parent != null)
            obj.Parent.RemoveChild(obj);
        else
            SceneRoots?.Remove(obj);

        if (newParent != null)
            newParent.AddChild(obj);
        else
            SceneRoots?.Add(obj);

        RaiseTreeChanged();
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

        if (SceneRoots != null)
            foreach (var root in SceneRoots)
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
