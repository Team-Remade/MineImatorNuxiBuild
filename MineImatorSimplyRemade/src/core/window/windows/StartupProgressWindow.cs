using System.Numerics;
using Hexa.NET.ImGui;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.window.windows;

public class StartupProgressWindow : Window
{
    public string StatusMessage { get; set; } = "Preparing startup...";

    private double _startTime = DateTime.UtcNow.TimeOfDay.TotalSeconds;

    public StartupProgressWindow(int width, int height, string title, Glfw glfw, GL gl = null)
        : base(width, height, title, glfw, gl)
    {
    }

    protected override void RenderUi()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar |
                                 ImGuiWindowFlags.NoResize |
                                 ImGuiWindowFlags.NoMove |
                                 ImGuiWindowFlags.NoScrollbar |
                                 ImGuiWindowFlags.NoSavedSettings;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(22f, 20f));
        ImGui.Begin("##StartupProgress", flags);
        ImGui.PopStyleVar();

        ImGui.Text("Setting up video encoding tools");
        ImGui.Separator();

        double elapsed = DateTime.UtcNow.TimeOfDay.TotalSeconds - _startTime;
        int dots = ((int)(elapsed * 2.0) % 4);
        string animated = "Working" + new string('.', dots);

        ImGui.Dummy(new Vector2(0, 6));
        ImGui.TextWrapped("First launch detected. FFmpeg is being downloaded to your local app data folder.");
        ImGui.Dummy(new Vector2(0, 10));
        ImGui.Text($"{animated}");
        ImGui.TextWrapped(StatusMessage);

        ImGui.End();
    }
}
