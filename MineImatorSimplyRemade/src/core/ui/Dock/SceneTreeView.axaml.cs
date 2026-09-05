using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Dock;

/// <summary>
/// Avalonia port of the old ImGui <c>core.ui.Panels.SceneTree</c> panel.
///
/// Renders the scene hierarchy (from the injected <see cref="SceneTree"/> model's
/// <see cref="SceneTree.SceneRoots"/>) as a native <c>TreeView</c> with:
///  • live search filtering,
///  • multi-selection kept in sync with the global <see cref="SelectionManager"/>,
///  • a right-click context menu (Duplicate / Rename / Set-or-Clear Active Camera / Delete),
///  • double-click / F2 inline-style rename (via a small dialog),
///  • Duplicate / Delete toolbar buttons.
///
/// All hierarchy mutation is delegated to the model, which raises
/// <see cref="SceneTree.TreeChanged"/> so this view rebuilds.
/// </summary>
public partial class SceneTreeView : UserControl
{
    /// <summary>Lightweight wrapper so the TreeView can bind to a display name +
    /// filtered children while still mapping back to the real scene object.</summary>
    public sealed class SceneTreeNode
    {
        public SceneObject Model { get; }
        public string DisplayName => Model.GetDisplayName();
        public ObservableCollection<SceneTreeNode> Children { get; } = new();

        public SceneTreeNode(SceneObject model)
        {
            Model = model;
        }
    }

    private readonly SceneTree _model;
    private readonly ObservableCollection<SceneTreeNode> _rootNodes = new();
    private readonly Dictionary<SceneObject, SceneTreeNode> _nodeLookup = new();

    // Guards against selection sync feedback loops between the TreeView and the
    // SelectionManager.
    private bool _syncingSelection;
    private SceneTreeNode? _dragSource;
    private Point _dragStart;

    /// <summary>Parameterless constructor for the XAML designer / previewer.</summary>
    public SceneTreeView() : this(new SceneTree())
    {
    }

    public SceneTreeView(SceneTree model)
    {
        _model = model;
        InitializeComponent();

        Tree.ItemsSource = _rootNodes;

        DuplicateButton.Click += (_, _) => { _model.DuplicateSelectedObjects(); };
        DeleteButton.Click += (_, _) => { _model.DeleteSelectedObjects(); };

        SearchBox.TextChanged += (_, _) => Rebuild();
        Tree.SelectionChanged += OnTreeSelectionChanged;
        Tree.DoubleTapped += (_, _) => RenameSelected();
        Tree.AddHandler(PointerPressedEvent, OnTreePointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Tree.PointerMoved += OnTreePointerMoved;
        Tree.AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
        Tree.AddHandler(DragDrop.DropEvent, OnTreeDrop);

        BuildContextMenu();

        _model.TreeChanged += OnModelTreeChanged;
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.SelectionChanged += OnSelectionManagerChanged;

        AttachedToVisualTree += (_, _) => Rebuild();
        DetachedFromVisualTree += (_, _) =>
        {
            _model.TreeChanged -= OnModelTreeChanged;
            if (SelectionManager.Instance != null)
                SelectionManager.Instance.SelectionChanged -= OnSelectionManagerChanged;
        };
    }

    private void OnModelTreeChanged() => Rebuild();

    private void OnSelectionManagerChanged() => SyncSelectionFromManager();

    // ── Tree building ─────────────────────────────────────────────────────────

    /// <summary>Rebuilds the node tree from the model, honouring the search filter.</summary>
    public void Rebuild()
    {
        string search = SearchBox.Text?.Trim() ?? "";
        var filter = string.IsNullOrEmpty(search) ? null : _model.BuildFilterVisibleSet(search);

        _rootNodes.Clear();
        _nodeLookup.Clear();

        foreach (var obj in _model.SceneRoots.ToList())
        {
            if (obj.HideInSceneTree) continue;
            if (filter != null && !filter.Contains(obj)) continue;
            _rootNodes.Add(BuildNode(obj, filter));
        }

        EmptyLabel.IsVisible = _rootNodes.Count == 0;

        SyncSelectionFromManager();
    }

    private SceneTreeNode BuildNode(SceneObject obj, HashSet<SceneObject>? filter)
    {
        var node = new SceneTreeNode(obj);
        _nodeLookup[obj] = node;

        foreach (var child in obj.Children.ToList())
        {
            if (child.HideInSceneTree) continue;
            if (filter != null && !filter.Contains(child)) continue;
            node.Children.Add(BuildNode(child, filter));
        }

        return node;
    }

    // ── Selection sync ──────────────────────────────────────────────────────

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || SelectionManager.Instance == null)
            return;

        _syncingSelection = true;
        try
        {
            SelectionManager.Instance.ClearSelection();
            foreach (var item in Tree.SelectedItems)
                if (item is SceneTreeNode node)
                    SelectionManager.Instance.SelectObject(node.Model);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void SyncSelectionFromManager()
    {
        if (_syncingSelection || SelectionManager.Instance == null)
            return;

        _syncingSelection = true;
        try
        {
            Tree.SelectedItems.Clear();
            foreach (var obj in SelectionManager.Instance.SelectedObjects)
                if (_nodeLookup.TryGetValue(obj, out var node))
                    Tree.SelectedItems.Add(node);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    // ── Right-click selection ─────────────────────────────────────────────────

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Tree);
        var node = FindNodeFrom(e.Source as Visual);

        if (point.Properties.IsLeftButtonPressed)
        {
            _dragSource = node;
            _dragStart = e.GetPosition(Tree);
            return;
        }

        if (!point.Properties.IsRightButtonPressed)
            return;

        if (node == null)
            return;

        // If the right-clicked item is not already part of the selection, make
        // it the sole selection (mirrors the old ImGui behaviour of preserving
        // an existing multi-selection when right-clicking within it).
        if (!Tree.SelectedItems.Contains(node))
        {
            Tree.SelectedItems.Clear();
            Tree.SelectedItems.Add(node);
        }
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource == null || !e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed)
            return;

        var position = e.GetPosition(Tree);
        var dragOffset = position - _dragStart;
        if (Math.Sqrt(dragOffset.X * dragOffset.X + dragOffset.Y * dragOffset.Y) < 4)
            return;

        var data = new DataObject();
        data.Set("MineImator.SceneTreeNode", _dragSource);
        _dragSource = null;
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        var source = e.Data.Get("MineImator.SceneTreeNode") as SceneTreeNode;
        var target = FindNodeFrom(e.Source as Visual);
        if (source == null || source == target || target?.Model.IsDescendantOf(source.Model) == true)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Move;
    }

    private void OnTreeDrop(object? sender, DragEventArgs e)
    {
        var source = e.Data.Get("MineImator.SceneTreeNode") as SceneTreeNode;
        var target = FindNodeFrom(e.Source as Visual);
        if (source == null || source == target || target?.Model.IsDescendantOf(source.Model) == true)
            return;

        _model.ReparentObject(source.Model, target?.Model);
        e.DragEffects = DragDropEffects.Move;
    }

    private static SceneTreeNode? FindNodeFrom(Visual? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is TreeViewItem item && item.DataContext is SceneTreeNode node)
                return node;
            current = current.GetVisualParent();
        }
        return null;
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private void BuildContextMenu()
    {
        var duplicateItem = new MenuItem { Header = "Duplicate" };
        duplicateItem.Click += (_, _) => _model.DuplicateSelectedObjects();

        var renameItem = new MenuItem { Header = "Rename" };
        renameItem.Click += (_, _) => RenameSelected();

        var activeCameraItem = new MenuItem { Header = "Set as Active Camera" };
        activeCameraItem.Click += (_, _) => ToggleActiveCamera();

        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => _model.DeleteSelectedObjects();

        var menu = new ContextMenu
        {
            Items = { duplicateItem, renameItem, activeCameraItem, new Separator(), deleteItem },
        };

        menu.Opening += (_, _) =>
        {
            var target = SelectedObjects().FirstOrDefault();
            bool hasSelection = target != null;
            duplicateItem.IsEnabled = hasSelection;
            renameItem.IsEnabled = hasSelection;
            deleteItem.IsEnabled = hasSelection;

            if (target is CameraSceneObject cam)
            {
                activeCameraItem.IsVisible = true;
                activeCameraItem.Header = cam.Active ? "Clear Active Camera" : "Set as Active Camera";
            }
            else
            {
                activeCameraItem.IsVisible = false;
            }
        };

        Tree.ContextMenu = menu;
    }

    private void ToggleActiveCamera()
    {
        if (SelectedObjects().FirstOrDefault() is not CameraSceneObject cam)
            return;

        if (cam.Active)
            cam.Active = false;
        else
            CameraSceneObject.SetActiveExclusive(cam);
    }

    // ── Rename ──────────────────────────────────────────────────────────────

    private IEnumerable<SceneObject> SelectedObjects() =>
        Tree.SelectedItems.OfType<SceneTreeNode>().Select(n => n.Model);

    private async void RenameSelected()
    {
        var target = SelectedObjects().FirstOrDefault();
        if (target == null)
            return;

        string? newName = await PromptRenameAsync(target.GetDisplayName());
        if (!string.IsNullOrWhiteSpace(newName))
            _model.RenameObject(target, newName);
    }

    private async System.Threading.Tasks.Task<string?> PromptRenameAsync(string current)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;

        string? result = null;
        var dialog = new Window
        {
            Title = "Rename",
            Width = 320,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var textBox = new TextBox { Text = current, MinWidth = 260 };
        var okButton = new Button { Content = "Rename", MinWidth = 90 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 90 };
        okButton.Click += (_, _) => { result = textBox.Text; dialog.Close(); };
        cancelButton.Click += (_, _) => { result = null; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                textBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { okButton, cancelButton },
                },
            },
        };

        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return result;
    }
}
