using System.Net;
using System.Text;
using MineImatorSimplyRemade.core.project;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Retained-mode replacement for MainWindow's ImGui-drawn "Unsaved Changes" modal
/// (see CheckAndHandleUnsavedChanges/RenderUnsavedChangesDialog in the old ImGui MainWindow).</summary>
public sealed class RmlUnsavedChangesDialogController
{
    private readonly Element _overlay;
    private readonly Element _root;
    private readonly ProjectManager _projects;

    public Action? SaveRequested { get; set; }
    public Action? ExitWithoutSavingRequested { get; set; }
    public Action? CancelRequested { get; set; }
    public bool Visible { get; private set; }

    public RmlUnsavedChangesDialogController(Element overlay, Element root, ProjectManager projects)
    {
        _overlay = overlay;
        _root = root;
        _projects = projects;
    }

    public void Show()
    {
        Visible = true;
        _overlay.SetProperty("display", "flex");
        Build();
    }

    public void Close()
    {
        Visible = false;
        _overlay.SetProperty("display", "none");
    }

    private void Build()
    {
        string projectName = _projects.HasProject ? _projects.Manifest.ProjectName : string.Empty;

        var html = new StringBuilder("""
            <style>
            #unsaved-changes-panel{margin:auto;width:420px;padding:16px;background:#202127;border:1px #393b44;}
            #unsaved-changes-panel h3{margin:0 0 10px 0;color:#dedfe4;}
            #unsaved-changes-panel p{margin:0 0 8px 0;color:#dedfe4;}
            #unsaved-changes-actions{display:flex;flex-direction:row;justify-content:flex-end;}
            #unsaved-changes-actions button{margin-left:6px;padding:7px 14px;background:#30323a;border:1px #50525e;}
            </style>
            """);
        html.Append("<div id='unsaved-changes-panel'><h3>Unsaved Changes</h3>")
            .Append("<p>The project \"").Append(Escape(projectName)).Append("\" has unsaved changes.</p>")
            .Append("<p>What would you like to do?</p>")
            .Append("<div id='unsaved-changes-actions'>")
            .Append("<button id='unsaved-changes-cancel'>Cancel</button>")
            .Append("<button id='unsaved-changes-exit'>Exit without Saving</button>")
            .Append("<button id='unsaved-changes-save'>Save</button>")
            .Append("</div></div>");

        _root.SetInnerRml(html.ToString());

        _root.GetElementById("unsaved-changes-cancel")?.AddEventListener("click", _ => Cancel());
        _root.GetElementById("unsaved-changes-exit")?.AddEventListener("click", _ => ExitWithoutSaving());
        _root.GetElementById("unsaved-changes-save")?.AddEventListener("click", _ => Save());
    }

    private void Save()
    {
        Close();
        SaveRequested?.Invoke();
    }

    private void ExitWithoutSaving()
    {
        Close();
        ExitWithoutSavingRequested?.Invoke();
    }

    private void Cancel()
    {
        Close();
        CancelRequested?.Invoke();
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
