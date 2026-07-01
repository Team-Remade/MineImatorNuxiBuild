using Hexa.NET.ImGui;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class Menubar : UiPanel
{
    public enum RenderRequestKind
    {
        Image,
        Video
    }

    public Action? NewProjectRequested { get; set; }
    public Action? OpenProjectRequested { get; set; }
    public Action? OpenRecentRequested { get; set; }
    public Action? SaveProjectRequested { get; set; }
    public Action? SaveProjectAsRequested { get; set; }
    public Action? UndoRequested { get; set; }
    public Action? RedoRequested { get; set; }
    public Action? ImportAssetRequested { get; set; }
    public Action? ResetWorkCameraRequested { get; set; }
    public Action? HomeScreenRequested { get; set; }
    public Action<RenderRequestKind>? RenderRequested { get; set; }

    public override void Render()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New Project"))
                {
                    NewProjectRequested?.Invoke();
                }
                if (ImGui.MenuItem("Open Project"))
                {
                    OpenProjectRequested?.Invoke();
                }
                if (ImGui.MenuItem("Open Recent..."))
                {
                    OpenRecentRequested?.Invoke();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Save Project", "Ctrl+S"))
                {
                    SaveProjectRequested?.Invoke();
                }
                if (ImGui.MenuItem("Save As", "Ctrl+Shift+S"))
                {
                    SaveProjectAsRequested?.Invoke();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Import Asset"))
                {
                    ImportAssetRequested?.Invoke();
                }
                if (ImGui.MenuItem("Import from world"))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Exit"))
                {
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z"))
                {
                    UndoRequested?.Invoke();
                }
                if (ImGui.MenuItem("Redo", "Ctrl+Y"))
                {
                    RedoRequested?.Invoke();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Cut"))
                {
                }
                if (ImGui.MenuItem("Copy"))
                {
                }
                if (ImGui.MenuItem("Paste"))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Select All"))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Duplicate"))
                {
                }
                if (ImGui.MenuItem("Delete"))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Hide"))
                {
                }

                if (ImGui.MenuItem("Show"))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Preferences"))
                {
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Render"))
            {
                if (ImGui.MenuItem("Render Image"))
                {
                    RenderRequested?.Invoke(RenderRequestKind.Image);
                }
                if (ImGui.MenuItem("Render Animation"))
                {
                    RenderRequested?.Invoke(RenderRequestKind.Video);
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Reset Work Camera"))
                {
                    ResetWorkCameraRequested?.Invoke();
                }
                if (ImGui.MenuItem("Show Secondary View"))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Show Timeline Markers"))
                {
                }
                if (ImGui.MenuItem("Playback Time"))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Show Shortcuts Bar"))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Home Screen"))
                {
                    HomeScreenRequested?.Invoke();
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                if (ImGui.MenuItem("About"))
                {
                }
                if (ImGui.MenuItem("Tutorials"))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Report Bugs"))
                {
                }
                if (ImGui.MenuItem("Visit the Forums"))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Support Us"))
                {
                }
                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }
    }
}