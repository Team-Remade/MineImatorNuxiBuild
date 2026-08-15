using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using RmlUiNet;
using RmlUiNet.Input;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Retained-mode scene hierarchy controller.</summary>
public sealed class RmlSceneTreeController : IDisposable
{
    private readonly Element _root;
    private readonly Viewport _viewport;
    private readonly SceneTree _operations;
    private readonly Dictionary<string, SceneObject> _rows = new();
    private readonly HashSet<SceneObject> _collapsed = new();
    private string _search = string.Empty;
    private string _signature = string.Empty;
    private SceneObject? _lastClicked;
    private SceneObject? _dragging;
    private string _dropStatus = string.Empty;
    private SceneObject? _contextTarget;
    private float _contextX;
    private float _contextY;
    private bool _disposed;

    public RmlSceneTreeController(Element root, Viewport viewport, SceneTree operations)
    {
        _root = root;
        _viewport = viewport;
        _operations = operations;
        _root.AddEventListener("keydown", HandleKeyDown);
        SelectionManager.Instance.SelectionChanged += OnSelectionChanged;
        Refresh(force: true);
    }

    public void Update() => Refresh(force: false);

    private void OnSelectionChanged()
    {
        foreach (SceneObject selected in SelectionManager.Instance.SelectedObjects)
            for (SceneObject? ancestor = selected.Parent; ancestor != null; ancestor = ancestor.Parent)
                _collapsed.Remove(ancestor);
        Refresh(force: true);
    }

    private void Refresh(bool force)
    {
        List<SceneObject> visible = FlattenVisible();
        string signature = string.Join('|', visible.Select(obj =>
            $"{RuntimeHelpers.GetHashCode(obj)}:{obj.GetDisplayName()}:{obj.Children.Count}:{SelectionManager.Instance.IsSelected(obj)}"));
        signature += $"|q={_search}";
        if (!force && signature == _signature) return;
        _signature = signature;
        _rows.Clear();

        var html = new StringBuilder("""
            <div id="tree-tools"><input id="tree-search" type="text" value="" placeholder="Search scene objects..."/></div>
            <div id="tree-list">
            """);

        int rowIndex = 0;
        foreach (SceneObject root in _viewport.SceneObjects.ToList())
            AppendNode(root, 0, html, ref rowIndex);
        if (rowIndex == 0) html.Append("<div class='empty'>No scene objects to display.</div>");
        html.Append("</div><div id='tree-actions'>");
        SceneObject? primary = SelectionManager.Instance.SelectedObjects.FirstOrDefault();
        if (primary != null)
            html.Append("<input id='tree-rename' value='").Append(Escape(primary.GetDisplayName())).Append("' style='width:100px;height:25px;background:#17181d;color:#eee;border:1px #464852'/>")
                .Append("<button id='tree-rename-apply'>Rename</button>");
        html.Append("<button id='tree-duplicate'>Duplicate</button>")
            .Append("<button id='tree-delete'>Delete</button><button id='tree-unparent'>Move to Root</button>");
        if (primary is CameraSceneObject camera)
            html.Append("<button id='tree-camera'>").Append(camera.Active ? "Clear Active Camera" : "Set Active Camera").Append("</button>");
        if (!string.IsNullOrEmpty(_dropStatus))
            html.Append("<span style='color:#d9b24e;margin-left:5px'>").Append(Escape(_dropStatus)).Append("</span>");
        html.Append("</div>");
        if (_contextTarget != null)
        {
            float left = Math.Clamp(_contextX, 0, Math.Max(0, _root.GetClientWidth() - 185));
            float top = Math.Clamp(_contextY, 0, Math.Max(0, _root.GetClientHeight() - 180));
            html.Append("<div id='tree-context' style='left:").Append(left.ToString(CultureInfo.InvariantCulture)).Append("px;top:")
                .Append(top.ToString(CultureInfo.InvariantCulture)).Append("px'>")
                .Append("<div id='tree-context-title'>").Append(Escape(_contextTarget.GetDisplayName())).Append("</div>")
                .Append("<button id='tree-context-rename'>Rename</button><button id='tree-context-duplicate'>Duplicate selection</button>")
                .Append("<button id='tree-context-root'>Move selection to root</button>");
            if (_contextTarget is CameraSceneObject contextCamera)
                html.Append("<button id='tree-context-camera'>").Append(contextCamera.Active ? "Clear Active Camera" : "Set Active Camera").Append("</button>");
            html.Append("<button id='tree-context-delete'>Delete selection</button><button id='tree-context-close'>Close</button></div>");
        }
        _root.SetInnerRml(html.ToString());

        if (_root.GetElementById("tree-search") is ElementFormControlInput search)
        {
            search.SetValue(_search);
            void ApplySearch()
            {
                _search = search.GetValue();
                Refresh(force: true);
            }
            search.AddEventListener("input", _ => ApplySearch());
            search.AddEventListener("change", _ => ApplySearch());
        }
        Bind("tree-duplicate", _operations.DuplicateSelectedObjects);
        Bind("tree-delete", _operations.DeleteSelectedObjects);
        Bind("tree-unparent", UnparentSelected);
        Bind("tree-camera", ToggleSelectedCamera);
        Bind("tree-rename-apply", RenameSelected);
        Bind("tree-context-rename", ContextRename);
        Bind("tree-context-duplicate", ContextDuplicate);
        Bind("tree-context-root", ContextUnparent);
        Bind("tree-context-camera", ContextCamera);
        Bind("tree-context-delete", ContextDelete);
        Bind("tree-context-close", CloseContext);
        if (_root.GetElementById("tree-rename") is ElementFormControlInput rename)
            rename.AddEventListener("keydown", e =>
            {
                if (Key(e) == KeyIdentifier.KI_RETURN || Key(e) == KeyIdentifier.KI_NUMPADENTER)
                    RenameSelected();
            });
        _root.GetElementById("tree-list")?.AddEventListener("dragdrop", DropAtRoot);
        foreach ((string id, SceneObject obj) in _rows)
        {
            _root.GetElementById(id)?.AddEventListener("click", e => Select(obj, e));
            _root.GetElementById(id)?.AddEventListener("dblclick", _ => BeginRename(obj));
            Bind(id + "-toggle", () => Toggle(obj));
            if (_root.GetElementById(id)?.GetParentNode() is Element row)
            {
                row.AddEventListener("mousedown", e => OpenContext(obj, e));
                row.AddEventListener("dragstart", _ => { _dragging = obj; _dropStatus = $"Moving {obj.GetDisplayName()}"; });
                row.AddEventListener("dragend", _ => { _dragging = null; _dropStatus = string.Empty; Refresh(force: true); });
                row.AddEventListener("dragdrop", e => DropOn(obj, e));
            }
        }
    }

    private void AppendNode(SceneObject obj, int depth, StringBuilder html, ref int rowIndex)
    {
        if (obj.HideInSceneTree || !MatchesFilterBranch(obj)) return;
        string id = $"tree-row-{rowIndex++}";
        _rows[id] = obj;
        bool selected = SelectionManager.Instance.IsSelected(obj);
        bool hasChildren = obj.Children.Any(child => !child.HideInSceneTree && MatchesFilterBranch(child));
        bool collapsed = _collapsed.Contains(obj) && string.IsNullOrWhiteSpace(_search);
        html.Append("<div class='tree-row").Append(selected ? " selected" : string.Empty).Append("' style='padding-left:")
            .Append(depth * 13).Append("px'><button id='").Append(id).Append("-toggle' class='twisty'>")
            .Append(hasChildren ? (collapsed ? "&#9656;" : "&#9662;") : "")
            .Append("</button><button id='").Append(id).Append("' class='tree-name'>")
            .Append(Escape(obj.GetDisplayName())).Append("</button><span class='tree-type'>")
            .Append(Escape(obj.ObjectType)).Append("</span></div>");
        if (!collapsed)
            foreach (SceneObject child in obj.Children.ToList()) AppendNode(child, depth + 1, html, ref rowIndex);
    }

    private bool MatchesFilterBranch(SceneObject obj)
    {
        if (string.IsNullOrWhiteSpace(_search)) return true;
        if (obj.GetDisplayName().Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return obj.Children.Any(MatchesFilterBranch);
    }

    private List<SceneObject> FlattenVisible()
    {
        var result = new List<SceneObject>();
        void Add(SceneObject obj)
        {
            if (obj.HideInSceneTree) return;
            result.Add(obj);
            foreach (SceneObject child in obj.Children) Add(child);
        }
        foreach (SceneObject root in _viewport.SceneObjects) Add(root);
        return result;
    }

    private List<SceneObject> FlattenDisplayed()
    {
        var result = new List<SceneObject>();
        void Add(SceneObject obj)
        {
            if (obj.HideInSceneTree || !MatchesFilterBranch(obj)) return;
            result.Add(obj);
            if (_collapsed.Contains(obj) && string.IsNullOrWhiteSpace(_search)) return;
            foreach (SceneObject child in obj.Children) Add(child);
        }
        foreach (SceneObject root in _viewport.SceneObjects) Add(root);
        return result;
    }

    private void Select(SceneObject obj, Event e)
    {
        bool control = Parameter(e, "ctrl_key") || Parameter(e, "meta_key");
        bool shift = Parameter(e, "shift_key");
        if (control)
            SelectionManager.Instance.ToggleSelection(obj);
        else if (shift && _lastClicked != null)
        {
            List<SceneObject> flat = FlattenVisible().Where(MatchesFilterBranch).ToList();
            int start = flat.IndexOf(_lastClicked), end = flat.IndexOf(obj);
            if (start >= 0 && end >= 0)
                for (int i = Math.Min(start, end); i <= Math.Max(start, end); i++)
                    if (!SelectionManager.Instance.IsSelected(flat[i])) SelectionManager.Instance.SelectObject(flat[i]);
        }
        else
        {
            SelectionManager.Instance.ClearSelection();
            SelectionManager.Instance.SelectObject(obj);
        }
        _lastClicked = obj;
    }

    private void HandleKeyDown(Event e)
    {
        KeyIdentifier key = Key(e);
        if (key == KeyIdentifier.KI_ESCAPE && _contextTarget != null)
        {
            e.StopPropagation();
            CloseContext();
            return;
        }
        if (e.TargetElement is ElementFormControlInput) return;

        SceneObject? selected = SelectionManager.Instance.SelectedObjects.LastOrDefault();
        List<SceneObject> displayed = FlattenDisplayed();
        if (displayed.Count == 0) return;
        int index = selected == null ? -1 : displayed.IndexOf(selected);
        SceneObject? next = null;
        switch (key)
        {
            case KeyIdentifier.KI_UP:
                next = displayed[Math.Max(0, index <= 0 ? 0 : index - 1)];
                break;
            case KeyIdentifier.KI_DOWN:
                next = displayed[Math.Min(displayed.Count - 1, index + 1)];
                break;
            case KeyIdentifier.KI_HOME:
                next = displayed[0];
                break;
            case KeyIdentifier.KI_END:
                next = displayed[^1];
                break;
            case KeyIdentifier.KI_LEFT when selected != null:
                if (selected.Children.Count > 0 && !_collapsed.Contains(selected)) Toggle(selected);
                else next = selected.Parent;
                break;
            case KeyIdentifier.KI_RIGHT when selected != null:
                if (selected.Children.Count > 0 && _collapsed.Contains(selected)) Toggle(selected);
                else next = selected.Children.FirstOrDefault(child => !child.HideInSceneTree && MatchesFilterBranch(child));
                break;
            case KeyIdentifier.KI_F2 when selected != null:
                BeginRename(selected);
                break;
            case KeyIdentifier.KI_DELETE when selected != null:
                _operations.DeleteSelectedObjects();
                break;
            default:
                return;
        }
        e.StopPropagation();
        if (next != null) SelectSingleAndFocus(next);
    }

    private void SelectSingleAndFocus(SceneObject obj)
    {
        SelectionManager.Instance.ClearSelection();
        SelectionManager.Instance.SelectObject(obj);
        _lastClicked = obj;
        string? id = _rows.FirstOrDefault(pair => ReferenceEquals(pair.Value, obj)).Key;
        if (!string.IsNullOrEmpty(id)) _root.GetElementById(id)?.Focus();
    }

    private void RenameSelected()
    {
        SceneObject? selected = SelectionManager.Instance.SelectedObjects.FirstOrDefault();
        if (selected != null && _root.GetElementById("tree-rename") is ElementFormControlInput input)
            _operations.RenameObject(selected, input.GetValue());
        Refresh(force: true);
    }

    private void BeginRename(SceneObject obj)
    {
        if (!SelectionManager.Instance.IsSelected(obj))
        {
            SelectionManager.Instance.ClearSelection();
            SelectionManager.Instance.SelectObject(obj);
        }
        _root.GetElementById("tree-rename")?.Focus();
    }

    private void OpenContext(SceneObject obj, Event e)
    {
        if (Number(e, "button") != 1) return;
        e.StopPropagation();
        if (!SelectionManager.Instance.IsSelected(obj))
        {
            SelectionManager.Instance.ClearSelection();
            SelectionManager.Instance.SelectObject(obj);
        }
        _contextTarget = obj;
        _contextX = Number(e, "mouse_x") - _root.GetAbsoluteLeft();
        _contextY = Number(e, "mouse_y") - _root.GetAbsoluteTop();
        Refresh(force: true);
    }

    private void ContextRename()
    {
        SceneObject? target = _contextTarget;
        CloseContext();
        if (target != null) BeginRename(target);
    }

    private void ContextDuplicate() { _operations.DuplicateSelectedObjects(); CloseContext(); }
    private void ContextUnparent() { UnparentSelected(); CloseContext(); }
    private void ContextCamera() { ToggleSelectedCamera(); CloseContext(); }
    private void ContextDelete() { CloseContext(); _operations.DeleteSelectedObjects(); }
    private void CloseContext() { _contextTarget = null; Refresh(force: true); }

    private static bool Parameter(Event e, string name) => e.Parameters.TryGetValue(name, out object? value) && value switch
    {
        bool flag => flag,
        int number => number != 0,
        _ => bool.TryParse(value?.ToString(), out bool parsed) && parsed
    };

    private static KeyIdentifier Key(Event e)
    {
        if (!e.Parameters.TryGetValue("key_identifier", out object? value)) return KeyIdentifier.KI_UNKNOWN;
        try { return (KeyIdentifier)Convert.ToByte(value); }
        catch (Exception) { return KeyIdentifier.KI_UNKNOWN; }
    }

    private static float Number(Event e, string name)
    {
        if (!e.Parameters.TryGetValue(name, out object? value)) return 0;
        try { return Convert.ToSingle(value); }
        catch (Exception) { return 0; }
    }

    private void Toggle(SceneObject obj)
    {
        if (obj.Children.Count == 0) return;
        if (!_collapsed.Add(obj)) _collapsed.Remove(obj);
        Refresh(force: true);
    }

    private void UnparentSelected()
    {
        bool changed = false;
        foreach (SceneObject obj in SelectionManager.Instance.SelectedObjects.ToList())
            changed |= _operations.ReparentObject(obj, null);
        _dropStatus = changed ? "Moved selection to scene root" : "Selection is already at scene root";
        Refresh(force: true);
    }

    private void ToggleSelectedCamera()
    {
        if (SelectionManager.Instance.SelectedObjects.FirstOrDefault() is CameraSceneObject camera)
        {
            if (camera.Active) camera.Active = false;
            else CameraSceneObject.SetActiveExclusive(camera);
            ProjectManager.Instance.SetDirty(true);
            Refresh(force: true);
        }
    }

    private void DropOn(SceneObject parent, Event e)
    {
        e.StopPropagation();
        if (_dragging == null) return;
        SceneObject dragged = _dragging;
        bool moved = _operations.ReparentObject(dragged, parent);
        _dropStatus = moved ? $"Moved {dragged.GetDisplayName()} under {parent.GetDisplayName()}" : "That hierarchy move is not allowed";
        _dragging = null;
        Refresh(force: true);
    }

    private void DropAtRoot(Event e)
    {
        if (_dragging == null) return;
        SceneObject dragged = _dragging;
        bool moved = _operations.ReparentObject(dragged, null);
        _dropStatus = moved ? $"Moved {dragged.GetDisplayName()} to scene root" : "Object is already at scene root";
        _dragging = null;
        Refresh(force: true);
    }

    private void Bind(string id, Action action) => _root.GetElementById(id)?.AddEventListener("click", _ => action());
    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        SelectionManager.Instance.SelectionChanged -= OnSelectionChanged;
        _disposed = true;
    }
}
