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
        _name = string.IsNullOrWhiteSpace(_projects.Manifest.ProjectName) ? "Untitled Project" : _projects.Manifest.ProjectName;
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

        var html = new StringBuilder("""
            <style>
            #project-dialog-panel{margin:auto;width:420px;padding:16px;background:#202127;border:1px #393b44;}
            #project-dialog-panel h3{margin:0 0 10px 0;color:#dedfe4;}
            #project-dialog-panel input{width:100%;padding:6px;margin-bottom:8px;background:#191a1f;border:1px #50525e;color:#dedfe4;}
            #project-dialog-error{color:#eb9271;margin-bottom:8px;}
            #project-dialog-actions{display:flex;flex-direction:row;justify-content:flex-end;}
            #project-dialog-actions button{margin-left:6px;padding:7px 14px;background:#30323a;border:1px #50525e;}
            </style>
            """);
        html.Append("<div id='project-dialog-panel'><h3>").Append(Escape(title)).Append("</h3>")
            .Append("<div>").Append(Escape(label)).Append("</div>")
            .Append("<input id='project-dialog-name' type='text' value='").Append(Escape(_name)).Append("'/>");

        if (!string.IsNullOrEmpty(_error))
            html.Append("<div id='project-dialog-error'>").Append(Escape(_error)).Append("</div>");

        html.Append("<div id='project-dialog-actions'>")
            .Append("<button id='project-dialog-cancel'>Cancel</button>")
            .Append("<button id='project-dialog-confirm'>").Append(Escape(actionLabel)).Append("</button>")
            .Append("</div></div>");

        _root.SetInnerRml(html.ToString());

        _root.GetElementById("project-dialog-cancel")?.AddEventListener("click", _ => Close());
        _root.GetElementById("project-dialog-confirm")?.AddEventListener("click", _ => Confirm());
    }

    private void Confirm()
    {
        string name = _root.GetElementById("project-dialog-name") is ElementFormControlInput input
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
                return true;
            }

            _projects.CreateNewProject(name);
            if (_spawnMenu != null)
                ProjectSceneSerializer.LoadSceneFromManifest(_projects.Manifest, _mainViewport, _spawnMenu, _timeline, _properties);

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
