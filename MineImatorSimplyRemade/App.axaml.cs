using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MineImatorSimplyRemade.core.window.windows;

namespace MineImatorSimplyRemade;

/// <summary>
/// Avalonia application entry point. Replaces the old GLFW-driven main.cs loop:
/// window creation/lifetime is now managed by Avalonia's classic desktop lifetime
/// instead of a manual `while (!Glfw.WindowShouldClose(...))` loop.
///
/// MIGRATION STATUS: only <see cref="StartupProgressWindow"/> has been ported to
/// Avalonia so far. <c>MainWindow</c> and <c>CameraWindow</c> (and everything they
/// depend on: Viewport, Timeline, PropertiesPanel, SpawnMenu, SceneTree, Gizmo3D,
/// Input, etc.) are still ImGui-shaped and have not been ported yet, so this shell
/// only shows the startup window for now. See the migration plan for next steps.
/// </summary>
public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // TODO(migration): MainWindow itself is only a partial port right now (see
            // its class doc) - it has no dockspace/panels/viewport yet, and
            // InitializeRuntime()'s asset-loading pipeline (BlockRegistry, TerrainAtlas,
            // etc. against the GL-based renderer) hasn't been wired back up, so the
            // StartupProgressWindow orchestration from the old main.cs isn't restored
            // yet either. Once Viewport/the renderer are ported, restore the sequence:
            //   1. show StartupProgressWindow,
            //   2. run FfmpegBootstrap / Services.Initialize / runtime init while
            //      updating startupWindow.ProgressState,
            //   3. create+show MainWindow and CameraWindow,
            //   4. close startupWindow and set desktop.MainWindow to MainWindow.
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
