using System.Reflection;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Owns the main RML document and all panel controllers.</summary>
public sealed class RmlEditorController : IDisposable
{
    private readonly EditorShell _shell;
    private readonly RmlHomeController _home;
    private readonly RmlContentBrowserController _content;
    private readonly RmlSceneTreeController _sceneTree;
    private readonly RmlPreferencesController _preferences;
    private readonly RmlTimelineController _timeline;
    private readonly RmlPropertiesController _properties;
    private readonly RmlViewportController _viewport;
    private readonly RmlSpawnMenuController? _spawnMenu;
    private readonly RmlToastController _toast;
    private readonly RmlAboutController _about;
    private readonly RmlUpdateController _update;
    private readonly RmlRenderController _render;
    private readonly RmlProjectDialogController _projectDialog;

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
        _projectDialog = new RmlProjectDialogController(Required("project-dialog-overlay"), Required("project-dialog-body"),
            ProjectManager.Instance, mainViewport, spawnMenu, timeline, properties);
        _home = new RmlHomeController(Required("home-overlay"), Required("home-body"), ProjectManager.Instance)
        {
            NewProjectRequested = _projectDialog.OpenNewProject,
            LoadProjectRequested = menu.OpenProjectRequested,
            OpenRecentRequested = path => menu.OpenRecentRequested?.Invoke()
        };
        _shell.BindCommand("new-project", _projectDialog.OpenNewProject);
        _shell.BindCommand("save-as", _projectDialog.OpenSaveAs);
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
        _toast = new RmlToastController(Required("toast"), Required("toast-text"));
        _about = new RmlAboutController(Required("about-overlay"), Required("about-body"), ResolveAppVersion());
        _update = new RmlUpdateController(Required("update-overlay"), Required("update-body"));
        _render = new RmlRenderController(Required("render-overlay"), Required("render-body"));
        _projectDialog.SuccessToastRequested = _toast.ShowSuccess;
        _projectDialog.ErrorToastRequested = _toast.ShowError;
        _shell.BindCommand("preferences", _preferences.Toggle);
        _shell.BindCommand("about", _about.Toggle);
        _shell.BindCommand("updates", _update.Toggle);
        _shell.BindCommand("render-image", () => _render.Show(RmlRenderController.RenderMode.Image));
        _shell.BindCommand("render-video", () => _render.Show(RmlRenderController.RenderMode.Video));
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
        _home.SetVisible(showHome);
        _home.Update();
        _shell.SetStatus(status);
        _sceneTree.Update();
        _properties.Update();
        _timeline.Update();
        _content.Update();
        _toast.Update();
        _update.Update();
        _render.Update();
    }

    public void ShowSuccessToast(string message) => _toast.ShowSuccess(message);

    public void ShowErrorToast(string message) => _toast.ShowError(message);

    private static string ResolveAppVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational!;

        string? fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (!string.IsNullOrWhiteSpace(fileVersion))
            return fileVersion!;

        return assembly.GetName().Version?.ToString() ?? "Unknown";
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
