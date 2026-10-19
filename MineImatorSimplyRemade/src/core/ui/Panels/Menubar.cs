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
    public Action? DuplicateRequested { get; set; }
    public Action? DeleteRequested { get; set; }
    public Action? ImportAssetRequested { get; set; }
    public Action? ImportWorldRequested { get; set; }
    public Action? ImportResourcePackRequested { get; set; }
    public Action? ImportResourcePackFolderRequested { get; set; }
    public Action? ResetWorkCameraRequested { get; set; }
    public Action? ResetLayoutRequested { get; set; }
    public Action? HomeScreenRequested { get; set; }
    public Action? AboutRequested { get; set; }
    public Action? ReportBugsRequested { get; set; }
    public Action? VisitForumsRequested { get; set; }
    public Action? SupportUsRequested { get; set; }
    public Action<RenderRequestKind>? RenderRequested { get; set; }
    public Action? PreferencesRequested { get; set; }
    public Action? CheckForUpdatesRequested { get; set; }
    public Action? ExitRequested { get; set; }

    public override void Render()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu(Localization.Label("text.menu.file", "File")))
            {
                if (ImGui.MenuItem(Localization.Label("text.common.new_project", "New Project"), "Ctrl+N"))
                {
                    NewProjectRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.open_project", "Open Project"), "Ctrl+O"))
                {
                    OpenProjectRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.open_recent", "Open Recent...")))
                {
                    OpenRecentRequested?.Invoke();
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.menu.save_project", "Save Project"), "Ctrl+S"))
                {
                    SaveProjectRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.save_as", "Save As"), "Ctrl+Shift+S"))
                {
                    SaveProjectAsRequested?.Invoke();
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.common.import_asset", "Import Asset")))
                {
                    ImportAssetRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.common.import_resource_pack", "Import Resource Pack")))
                {
                    ImportResourcePackRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.import_resource_pack_folder", "Import Resource Pack Folder")))
                {
                    ImportResourcePackFolderRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.common.import_from_world", "Import from world")))
                {
                    ImportWorldRequested?.Invoke();
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.menu.exit", "Exit")))
                {
                    ExitRequested?.Invoke();
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu(Localization.Label("text.menu.edit", "Edit")))
            {
                if (ImGui.MenuItem(Localization.Label("text.menu.undo", "Undo"), "Ctrl+Z"))
                {
                    UndoRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.redo", "Redo"), "Ctrl+Y"))
                {
                    RedoRequested?.Invoke();
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.menu.cut", "Cut")))
                {
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.copy", "Copy")))
                {
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.paste", "Paste")))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.menu.select_all", "Select All")))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.common.duplicate", "Duplicate"), "Ctrl+D"))
                {
                    DuplicateRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.common.delete", "Delete"), "Del"))
                {
                    DeleteRequested?.Invoke();
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.menu.hide", "Hide")))
                {
                }

                if (ImGui.MenuItem(Localization.Label("text.common.show", "Show")))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.common.preferences", "Preferences")))
                {
                    PreferencesRequested?.Invoke();
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu(Localization.Label("text.common.render", "Render")))
            {
                if (ImGui.MenuItem(Localization.Label("text.menu.render_image", "Render Image"), "F7"))
                {
                    RenderRequested?.Invoke(RenderRequestKind.Image);
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.render_animation", "Render Animation"), "F8"))
                {
                    RenderRequested?.Invoke(RenderRequestKind.Video);
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu(Localization.Label("text.menu.view", "View")))
            {
                if (ImGui.MenuItem(Localization.Label("text.menu.reset_layout", "Reset Layout")))
                {
                    ResetLayoutRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.reset_work_camera", "Reset Work Camera")))
                {
                    ResetWorkCameraRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.show_secondary_view", "Show Secondary View")))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.menu.show_timeline_markers", "Show Timeline Markers")))
                {
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.playback_time", "Playback Time")))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.menu.show_shortcuts_bar", "Show Shortcuts Bar")))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.menu.home_screen", "Home Screen")))
                {
                    HomeScreenRequested?.Invoke();
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu(Localization.Label("text.menu.help", "Help")))
            {
                if (ImGui.MenuItem(Localization.Label("text.common.check_for_updates", "Check for Updates")))
                {
                    CheckForUpdatesRequested?.Invoke();
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.menu.about", "About")))
                {
                    AboutRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.tutorials", "Tutorials")))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.menu.report_bugs", "Report Bugs")))
                {
                    ReportBugsRequested?.Invoke();
                }
                if (ImGui.MenuItem(Localization.Label("text.menu.visit_the_forums", "Visit the Forums")))
                {
                    VisitForumsRequested?.Invoke();
                }
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.Label("text.menu.support_us", "Support Us")))
                {
                    SupportUsRequested?.Invoke();
                }
                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }
    }
}
