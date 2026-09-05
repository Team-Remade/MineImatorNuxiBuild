using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using OrientationEnum = Dock.Model.Core.Orientation;

namespace MineImatorSimplyRemade.core.ui.Dock;

/// <summary>
/// Builds the main editor dockspace layout, replacing the old ImGui version's
/// <c>MainWindow.SetupDefaultDockSpace</c> (see git history at commit
/// <c>a9d68a5</c>, just before the Avalonia+Veldrid rewrite began -
/// <c>ImGuiP.DockBuilder*</c> calls). Reproduces the exact same split
/// proportions and grouping:
///
/// <code>
/// Root
///  +- 70% Left column                    +- 30% Right column
///  |   +- 75% Viewport                    |   +- 30% Scene Tree
///  |   +- 25% Timeline / Content Browser  |   +- 70% Properties
///  |       (tabbed together)              |
/// </code>
///
/// Every tool's <see cref="Tool.Content"/> is currently a
/// <see cref="PlaceholderPanelView"/> - each panel's own porting pass (Viewport,
/// Timeline, PropertiesPanel, SceneTree, ContentBrowser) should replace its
/// tool's <c>Content</c> with the real ported view once that pass lands,
/// without needing to touch this layout structure at all.
///
/// SpawnMenu and PreferencesPanel are intentionally NOT part of this default
/// layout - in the old renderer they were floating/popup-style windows, not
/// dockspace members (see the migration research notes), so they'll become
/// separate Avalonia windows/dialogs rather than dock tools.
/// </summary>
public sealed class AppDockFactory : Factory
{
    public const string ViewportToolId = "Viewport";
    public const string SceneTreeToolId = "SceneTree";
    public const string PropertiesToolId = "Properties";
    public const string TimelineToolId = "Timeline";
    public const string ContentBrowserToolId = "ContentBrowser";

    /// <summary>The viewport's backing model. Owned by <c>MainWindow</c> (so the
    /// scene survives layout resets) and rendered by <see cref="ViewportView"/>.</summary>
    private readonly Viewport _viewportModel;
    private readonly Action _openSpawnMenu;
    private readonly Action<ProjectAssetEntry> _spawnAsset;
    private readonly Action<ProjectAssetEntry> _addSoundToTimeline;
    private readonly Action _importResourcePack;
    private readonly Action _importResourcePackFolder;

    public AppDockFactory(Viewport viewportModel, Action openSpawnMenu,
        Action<ProjectAssetEntry> spawnAsset, Action<ProjectAssetEntry> addSoundToTimeline,
        Action importResourcePack, Action importResourcePackFolder)
    {
        _viewportModel = viewportModel;
        _openSpawnMenu = openSpawnMenu;
        _spawnAsset = spawnAsset;
        _addSoundToTimeline = addSoundToTimeline;
        _importResourcePack = importResourcePack;
        _importResourcePackFolder = importResourcePackFolder;
    }

    public Tool ViewportTool { get; private set; } = null!;
    public Tool SceneTreeTool { get; private set; } = null!;

    /// <summary>
    /// The Scene Tree panel's backing model, exposed so the host (MainWindow)
    /// can call <see cref="SceneTree.Initialize"/> once SelectionManager exists,
    /// wire the Edit menu's Duplicate/Delete to it, and (once Viewport is ported)
    /// assign its <see cref="SceneTree.SceneRoots"/> to the viewport's scene
    /// objects.
    /// </summary>
    public SceneTree SceneTreeModel { get; } = new();

    /// <summary>
    /// The Timeline panel's backing model. The host (MainWindow) calls
    /// <see cref="Timeline.Initialize"/> once SelectionManager exists and, once
    /// the Viewport is ported, assigns <see cref="Timeline.SceneObjectsProvider"/>.
    /// </summary>
    public Timeline TimelineModel { get; } = new();

    /// <summary>
    /// The Properties panel's backing model. The host (MainWindow) calls
    /// <see cref="PropertiesPanel.Initialize"/> once SelectionManager exists,
    /// assigns <see cref="PropertiesPanel.Timeline"/>/<see cref="PropertiesPanel.SpawnMenu"/>,
    /// and wires the viewport hooks once the Viewport is ported.
    /// </summary>
    public PropertiesPanel PropertiesModel { get; } = new();
    public Tool PropertiesTool { get; private set; } = null!;
    public Tool TimelineTool { get; private set; } = null!;
    public Tool ContentBrowserTool { get; private set; } = null!;
    public ContentBrowserView? ContentBrowser { get; private set; }

    public override IRootDock CreateLayout()
    {
        ViewportTool = new Tool
        {
            Id = ViewportToolId,
            Title = "Viewport",
            CanClose = false,
            Content = new Func<IServiceProvider, object>(_ => new TemplateResult<Control>(new ViewportView(_viewportModel, _openSpawnMenu), null!)),
        };

        SceneTreeTool = new Tool
        {
            Id = SceneTreeToolId,
            Title = "Scene Tree",
            Content = new Func<IServiceProvider, object>(_ => new TemplateResult<Control>(new SceneTreeView(SceneTreeModel), null!)),
        };

        PropertiesTool = new Tool
        {
            Id = PropertiesToolId,
            Title = "Properties",
            Content = new Func<IServiceProvider, object>(_ => new TemplateResult<Control>(new PropertiesView(PropertiesModel), null!)),
        };

        TimelineTool = new Tool
        {
            Id = TimelineToolId,
            Title = "Timeline",
            Content = new Func<IServiceProvider, object>(_ => new TemplateResult<Control>(new TimelineView(TimelineModel), null!)),
        };

        ContentBrowser = new ContentBrowserView
        {
            SpawnAssetRequested = _spawnAsset,
            AddSoundToTimelineRequested = _addSoundToTimeline,
            ImportResourcePackRequested = _importResourcePack,
            ImportResourcePackFolderRequested = _importResourcePackFolder
        };

        ContentBrowserTool = new Tool
        {
            Id = ContentBrowserToolId,
            Title = "Content Browser",
            Content = new Func<IServiceProvider, object>(_ => new TemplateResult<Control>(ContentBrowser, null!)),
        };

        var viewportDock = new ToolDock
        {
            Id = "ViewportDock",
            Proportion = 0.75,
            ActiveDockable = ViewportTool,
            VisibleDockables = CreateList<IDockable>(ViewportTool),
        };

        // Timeline and Content Browser share one dock, tabbed together -
        // matches DockBuilderDockWindow(TimelineDockId, timelineDockId) and
        // DockBuilderDockWindow(ContentBrowserDockId, timelineDockId) both
        // targeting the same node in the old layout.
        var bottomLeftDock = new ToolDock
        {
            Id = "TimelineContentBrowserDock",
            Proportion = 0.25,
            ActiveDockable = TimelineTool,
            VisibleDockables = CreateList<IDockable>(TimelineTool, ContentBrowserTool),
        };

        var leftColumn = new ProportionalDock
        {
            Id = "LeftColumn",
            Orientation = OrientationEnum.Vertical,
            Proportion = 0.7,
            VisibleDockables = CreateList<IDockable>(
                viewportDock,
                new ProportionalDockSplitter { Id = "LeftColumnSplitter" },
                bottomLeftDock),
        };

        var sceneTreeDock = new ToolDock
        {
            Id = "SceneTreeDock",
            Proportion = 0.3,
            ActiveDockable = SceneTreeTool,
            VisibleDockables = CreateList<IDockable>(SceneTreeTool),
        };

        var propertiesDock = new ToolDock
        {
            Id = "PropertiesDock",
            Proportion = 0.7,
            ActiveDockable = PropertiesTool,
            VisibleDockables = CreateList<IDockable>(PropertiesTool),
        };

        var rightColumn = new ProportionalDock
        {
            Id = "RightColumn",
            Orientation = OrientationEnum.Vertical,
            Proportion = 0.3,
            VisibleDockables = CreateList<IDockable>(
                sceneTreeDock,
                new ProportionalDockSplitter { Id = "RightColumnSplitter" },
                propertiesDock),
        };

        var mainLayout = new ProportionalDock
        {
            Id = "MainLayout",
            Orientation = OrientationEnum.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                leftColumn,
                new ProportionalDockSplitter { Id = "MainLayoutSplitter" },
                rightColumn),
        };

        var root = CreateRootDock();
        root.Id = "Root";
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        root.DefaultDockable = mainLayout;

        return root;
    }
}
