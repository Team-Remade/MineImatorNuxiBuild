using Hexa.NET.ImGui;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class Menubar : UiPanel
{
    public override void Render()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New Project"))
                {
                }
                if (ImGui.MenuItem("Open Project"))
                {
                }
                if (ImGui.MenuItem("Open Recent..."))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Save Project"))
                {
                }
                if (ImGui.MenuItem("Save As"))
                {
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Import Asset"))
                {
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
                if (ImGui.MenuItem("Undo"))
                {
                }
                if (ImGui.MenuItem("Redo"))
                {
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
                }
                if (ImGui.MenuItem("Render Animation"))
                {
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Reset Work Camera"))
                {
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