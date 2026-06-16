using MineImatorSimplyRemade.gizmo;
using MineImatorSimplyRemadeNuxi.core.objs;

namespace MineImatorSimplyRemadeNuxi.core;

/// <summary>
/// Manages the editor selection state.  Ported from ExampleSelectionManager (Godot reference),
/// skipping Gizmo3D, EditorCommandHistory, ProjectManager and timeline dependencies that do not
/// yet exist in this MonoGame codebase.
/// </summary>
public class SelectionManager
{
    // ── Singleton ────────────────────────────────────────────────────────────

    public static SelectionManager Instance { get; private set; }

    /// <summary>
    /// Creates the singleton instance.  Call once from App.Initialize() before any
    /// UI panel tries to access SelectionManager.Instance.
    /// </summary>
    public static void Initialize()
    {
        Instance = new SelectionManager();
    }

    // ── State ────────────────────────────────────────────────────────────────

    /// <summary>All currently selected scene objects (may be more than one).</summary>
    public List<SceneObject> SelectedObjects { get; } = new();

    /// <summary>
    /// The 3D gizmo used to transform selected objects.
    /// Assign from Viewport after both are created.
    /// </summary>
    private Gizmo3D? _gizmo;
    public Gizmo3D? Gizmo
    {
        get => _gizmo;
        set
        {
            if (_gizmo != null)
                _gizmo.TransformEnd -= OnGizmoTransformEnd;
            _gizmo = value;
            if (_gizmo != null)
                _gizmo.TransformEnd += OnGizmoTransformEnd;
        }
    }

    /// <summary>
    /// Incrementing counter used to generate unique pick-colour IDs.
    /// Starts at 1 so that ID 0 can mean "nothing".
    /// </summary>
    private int _nextPickColorId = 1;

    // ── Events ───────────────────────────────────────────────────────────────

    /// <summary>Raised whenever the selection changes (object added, removed, or cleared).</summary>
    public event Action SelectionChanged;

    // ── Private constructor (force use of Initialize()) ──────────────────────

    private SelectionManager() { }

    // ── Gizmo integration ────────────────────────────────────────────────────

    /// <summary>
    /// Re-syncs the gizmo's selection list with <see cref="SelectedObjects"/>.
    /// Safe to call when <see cref="Gizmo"/> is null.
    /// </summary>
    private void SyncGizmoSelection()
    {
        if (_gizmo == null) return;
        _gizmo.ClearSelection();
        foreach (var obj in SelectedObjects)
            _gizmo.Select(obj);
    }

    private void OnGizmoTransformEnd(Gizmo3D.TransformMode mode, Gizmo3D.TransformPlane plane)
    {
        // Notify all listeners (e.g. PropertiesPanel) so they can refresh.
        SelectionChanged?.Invoke();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Adds <paramref name="obj"/> to the selection.
    /// No-op if it is already selected or if <c>obj.IsSelectable</c> is false.
    /// </summary>
    public void SelectObject(SceneObject obj)
    {
        if (obj == null) return;
        if (SelectedObjects.Contains(obj)) return;
        if (!obj.IsSelectable) return;

        SelectedObjects.Add(obj);
        obj.IsSelected = true;

        SyncGizmoSelection();
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Removes <paramref name="obj"/> from the selection.
    /// No-op if it is not currently selected.
    /// </summary>
    public void DeselectObject(SceneObject obj)
    {
        if (obj == null) return;
        if (!SelectedObjects.Contains(obj)) return;

        SelectedObjects.Remove(obj);
        obj.IsSelected = false;

        SyncGizmoSelection();
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Selects <paramref name="obj"/> if it is not selected, or deselects it if it is.
    /// </summary>
    public void ToggleSelection(SceneObject obj)
    {
        if (obj == null) return;

        if (SelectedObjects.Contains(obj))
            DeselectObject(obj);
        else
            SelectObject(obj);
    }

    /// <summary>
    /// Deselects all objects and fires <see cref="SelectionChanged"/>.
    /// </summary>
    public void ClearSelection()
    {
        if (SelectedObjects.Count == 0) return;

        foreach (var obj in SelectedObjects)
            obj.IsSelected = false;

        SelectedObjects.Clear();

        SyncGizmoSelection();
        SelectionChanged?.Invoke();
    }

    /// <summary>Returns whether <paramref name="obj"/> is in the current selection.</summary>
    public bool IsSelected(SceneObject obj)
    {
        return obj != null && SelectedObjects.Contains(obj);
    }

    /// <summary>
    /// Generates a new globally-unique object identifier and a unique pick-colour ID.
    /// </summary>
    /// <returns>
    /// A tuple of (<c>uuid</c>) a GUID string, and (<c>pickColorId</c>) a
    /// monotonically-increasing integer suitable for colour-picking in a render pass.
    /// </returns>
    public (string uuid, int pickColorId) GetNextObjectId()
    {
        var uuid = Guid.NewGuid().ToString();
        var pickColorId = _nextPickColorId;
        _nextPickColorId++;
        return (uuid, pickColorId);
    }
}
