using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.startup;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.window.windows;

public class StartupProgressWindow : Window
{
    public StartupProgressState ProgressState { get; } = new();

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

        float clampedProgress = Math.Clamp(ProgressState.Progress, 0f, 1f);
        string stepLabel = ProgressState.TotalSteps > 0
            ? $"Step {Math.Clamp(ProgressState.CurrentStep, 1, ProgressState.TotalSteps)}/{ProgressState.TotalSteps}"
            : "Startup";

        ImGui.TextColored(new Vector4(0.92f, 0.74f, 0.31f, 1f), ProgressState.Title);
        ImGui.TextDisabled(stepLabel);
        ImGui.Separator();

        double elapsed = DateTime.UtcNow.TimeOfDay.TotalSeconds - _startTime;
        int dots = ((int)(elapsed * 2.0) % 4);
        string animated = "Working" + new string('.', dots);

        ImGui.Dummy(new Vector2(0, 6));
        ImGui.TextWrapped(ProgressState.Phase);
        ImGui.Dummy(new Vector2(0, 10));
        ImGui.ProgressBar(clampedProgress, new Vector2(-1, 16f), $"{MathF.Round(clampedProgress * 100f)}%");
        ImGui.Dummy(new Vector2(0, 10));
        ImGui.Text(animated);
        ImGui.TextWrapped(ProgressState.Status);

        if (!string.IsNullOrWhiteSpace(ProgressState.Detail))
        {
            ImGui.Dummy(new Vector2(0, 6));
            ImGui.TextColored(new Vector4(0.70f, 0.74f, 0.80f, 1f), ProgressState.Detail);
        }

        ImGui.End();
    }
}
