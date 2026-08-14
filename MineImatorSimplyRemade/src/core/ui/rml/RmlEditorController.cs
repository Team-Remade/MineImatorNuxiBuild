using MineImatorSimplyRemade.core.ui.Panels;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Owns the main RML document and all panel controllers.</summary>
public sealed class RmlEditorController : IDisposable
{
    private readonly EditorShell _shell;
    private readonly RmlContentBrowserController _content;
    private readonly RmlSceneTreeController _sceneTree;
    private readonly RmlPreferencesController _preferences;
    private readonly RmlTimelineController _timeline;
    private readonly RmlPropertiesController _properties;
    private readonly RmlViewportController _viewport;
    private readonly RmlSpawnMenuController? _spawnMenu;

    public RmlEditorController(
        RmlWindowHost host,
        Menubar menu,
        Viewport mainViewport,
        Viewport renderSurface,
        SceneTree sceneTree,
        PropertiesPanel properties,
        PreferencesPanel preferences,
        Timeline timeline,
        ContentBrowser content,
        SpawnMenu? spawnMenu)
    {
        _shell = new EditorShell(host, menu);
        _sceneTree = new RmlSceneTreeController(Required("scene-tree-body"), mainViewport, sceneTree);
        _properties = new RmlPropertiesController(Required("properties-body"), timeline, properties);
        _timeline = new RmlTimelineController(Required("timeline-body"), timeline);
        _content = new RmlContentBrowserController(Required("content-browser-body"))
        {
            SpawnMenu = spawnMenu,
            Timeline = timeline,
            ImportResourcePackRequested = content.ImportResourcePackRequested,
            ImportResourcePackFolderRequested = content.ImportResourcePackFolderRequested
        };
        _preferences = new RmlPreferencesController(Required("preferences-overlay"), Required("preferences-body"), preferences);
        _viewport = new RmlViewportController(Required("viewport-surface"), mainViewport, renderSurface);
        _shell.BindCommand("preferences", _preferences.Toggle);
        if (spawnMenu != null)
        {
            _spawnMenu = new RmlSpawnMenuController(Required("spawn-overlay"), Required("spawn-body"), spawnMenu);
            _shell.BindCommand("spawn-object", _spawnMenu.Toggle);
        }

        Element Required(string id) => _shell.GetRegionElement(id)
            ?? throw new InvalidOperationException($"Editor shell is missing required element '{id}'.");
    }

    public void Update(bool showHome, string status)
    {
        _shell.SetHomeVisible(showHome);
        _shell.SetStatus(status);
        _sceneTree.Update();
        _properties.Update();
        _timeline.Update();
        _content.Update();
    }

    /// <summary>Runs scene rendering before RmlUi composites the shell.</summary>
    public void RenderSceneSurface() => _viewport.Render();

    public EditorShell.Region ViewportRegion => _shell.GetRegion("viewport-surface");

    public void Dispose()
    {
        _sceneTree.Dispose();
        _properties.Dispose();
    }
}
