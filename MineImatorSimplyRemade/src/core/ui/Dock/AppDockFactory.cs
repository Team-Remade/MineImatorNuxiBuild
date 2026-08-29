using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
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

    public Tool ViewportTool { get; private set; } = null!;
    public Tool SceneTreeTool { get; private set; } = null!;
    public Tool PropertiesTool { get; private set; } = null!;
    public Tool TimelineTool { get; private set; } = null!;
    public Tool ContentBrowserTool { get; private set; } = null!;

    public override IRootDock CreateLayout()
    {
        ViewportTool = new Tool
        {
            Id = ViewportToolId,
            Title = "Viewport",
            CanClose = false,
            Content = new ViewportView(),
        };

        SceneTreeTool = new Tool
        {
            Id = SceneTreeToolId,
            Title = "Scene Tree",
            Content = new PlaceholderPanelView("Scene Tree"),
        };

        PropertiesTool = new Tool
        {
            Id = PropertiesToolId,
            Title = "Properties",
            Content = new PlaceholderPanelView("Properties"),
        };

        TimelineTool = new Tool
        {
            Id = TimelineToolId,
            Title = "Timeline",
            Content = new PlaceholderPanelView("Timeline"),
        };

        ContentBrowserTool = new Tool
        {
            Id = ContentBrowserToolId,
            Title = "Content Browser",
            Content = new PlaceholderPanelView("Content Browser"),
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
