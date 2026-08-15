using System.Net;
using System.Text;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Retained-mode replacement for MainWindow's ImGui-drawn New Project / Save Project As
/// name-entry modal (see OpenNewProjectPopup/OpenSaveAsPopup/RenderProjectDialogs/
/// ExecuteProjectDialogAction in the old ImGui MainWindow).</summary>
public sealed class RmlProjectDialogController
{
    private enum Mode
    {
        NewProject,
        SaveAs
    }

    private readonly Element _overlay;
    private readonly Element _root;
    private readonly ProjectManager _projects;
    private readonly Viewport _mainViewport;
    private readonly SpawnMenu? _spawnMenu;
    private readonly Timeline _timeline;
    private readonly PropertiesPanel _properties;
    private Mode _mode = Mode.NewProject;
    private string _name = "Untitled Project";
    private string _error = string.Empty;

    public Action<string>? SuccessToastRequested { get; set; }
    public Action<string>? ErrorToastRequested { get; set; }

    /// <summary>Invoked with true for Save-As, false for New Project after the project was
    /// successfully created/saved, so MainWindow can leave the home screen (it owns
    /// _showProjectHome; this controller has no way to flip that flag itself).</summary>
    public Action<bool>? ProjectReady { get; set; }
    public bool Visible { get; private set; }

    public RmlProjectDialogController(Element overlay, Element root, ProjectManager projects,
        Viewport mainViewport, SpawnMenu? spawnMenu, Timeline timeline, PropertiesPanel properties)
    {
        _overlay = overlay;
        _root = root;
        _projects = projects;
        _mainViewport = mainViewport;
        _spawnMenu = spawnMenu;
        _timeline = timeline;
        _properties = properties;
    }

    public void OpenNewProject()
    {
        _mode = Mode.NewProject;
        _name = "Untitled Project";
        _error = string.Empty;
        Show();
    }

    public void OpenSaveAs()
    {
        if (!_projects.HasProject)
        {
            OpenNewProject();
            return;
        }

        _mode = Mode.SaveAs;
        _name = string.IsNullOrWhiteSpace(_projects.Manifest.ProjectName)
            ? "Untitled Project"
            : _projects.Manifest.ProjectName;
        _error = string.Empty;
        Show();
    }

    public void Close()
    {
        Visible = false;
        _overlay.SetProperty("display", "none");
    }

    private void Show()
    {
        Visible = true;
        _overlay.SetProperty("display", "flex");
        Build();
    }

    private void Build()
    {
        string title = _mode == Mode.SaveAs ? "Save Project As" : "New Project";
        string label = _mode == Mode.SaveAs ? "Project name (copy)" : "Project name";
        string actionLabel = _mode == Mode.SaveAs ? "Save Copy" : "Create";

        // Note: SetInnerRml() only parses element markup for the fragment being inserted -
        // unlike a document's <head>, a <style> tag embedded here isn't picked up as a
        // stylesheet by RmlUi, so it would just render as raw literal text. All of the
        // project-dialog's CSS lives in EditorShell's document-level <style> block instead.
        var html = new StringBuilder();

        html.Append("<div id='project-dialog-panel'>")
            .Append("<h3>")
            .Append(Escape(title))
            .Append("</h3>")
            .Append("<div>")
            .Append(Escape(label))
            .Append("</div>")
            .Append("<input id='project-dialog-name' type='text' value='")
            .Append(Escape(_name))
            .Append("'/>");

        if (!string.IsNullOrEmpty(_error))
        {
            html.Append("<div id='project-dialog-error'>")
                .Append(Escape(_error))
                .Append("</div>");
        }

        html.Append("<div id='project-dialog-actions'>")
            .Append("<button id='project-dialog-cancel'>Cancel</button>")
            .Append("<button id='project-dialog-confirm'>")
            .Append(Escape(actionLabel))
            .Append("</button>")
            .Append("</div>")
            .Append("</div>");

        // IMPORTANT: Put the dialog inside the overlay, not the main root.
        _overlay.SetInnerRml(html.ToString());

        // Elements returned by GetElementById are cached by the RmlUi wrapper keyed on their
        // native pointer. Since SetInnerRml() above tears down and recreates the whole overlay
        // subtree every rebuild, a freed native element's pointer address can get reused for a
        // new element - and when that happens, GetElementById hands back the *stale* cached
        // wrapper, whose click handler dictionary already thinks a listener is registered, so
        // the real native listener never gets attached to the new element (see
        // RmlHomeController.Bind for the same issue). Using a fresh EventListener per bind
        // (instead of the Action<Event> overload, which relies on that cached per-wrapper
        // dictionary) sidesteps this entirely: it always performs the actual native registration.
        _overlay.GetElementById("project-dialog-cancel")?
            .AddEventListener("click", new ClickListener(Close));

        _overlay.GetElementById("project-dialog-confirm")?
            .AddEventListener("click", new ClickListener(Confirm));
    }

    private sealed class ClickListener(Action action) : EventListener
    {
        public override void ProcessEvent(Event ev) => action();
    }

    private void Confirm()
    {
        // The name input was created inside _overlay (see Build()), not _root - _root
        // ("project-dialog-body") is actually replaced/removed by _overlay.SetInnerRml, so
        // looking it up through _root would always fail.
        string name = _overlay.GetElementById("project-dialog-name") is ElementFormControlInput input
            ? input.GetValue()
            : _name;
        name = string.IsNullOrWhiteSpace(name) ? "Untitled Project" : name.Trim();
        _name = name;

        if (ExecuteAction(name))
            Close();
        else
            Build();
    }

    private bool ExecuteAction(string name)
    {
        string title = _mode == Mode.SaveAs ? "Save Project As" : "New Project";
        try
        {
            if (_mode == Mode.SaveAs)
            {
                ProjectSceneSerializer.WriteSceneToManifest(_projects.Manifest, _mainViewport, _timeline, _properties);
                _projects.SaveProjectAs(name);
                SuccessToastRequested?.Invoke($"Saved copy as {name}");
                ProjectReady?.Invoke(true);
                return true;
            }

            _projects.CreateNewProject(name);
            if (_spawnMenu != null)
                ProjectSceneSerializer.LoadSceneFromManifest(_projects.Manifest, _mainViewport, _spawnMenu, _timeline,
                    _properties);

            ProjectReady?.Invoke(false);
            return true;
        }
        catch (Exception ex)
        {
            _error = $"{title} failed: {ex.Message}";
            ErrorToastRequested?.Invoke(_error);
            return false;
        }
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}