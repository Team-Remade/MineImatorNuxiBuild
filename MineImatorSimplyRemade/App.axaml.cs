using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.startup;
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
            var startupWindow = new StartupProgressWindow();
            desktop.MainWindow = startupWindow;
            startupWindow.Show();

            _ = Task.Run(() => InitializeResources(startupWindow.ProgressState))
                .ContinueWith(task => Dispatcher.UIThread.Post(() =>
                {
                    if (task.IsFaulted)
                    {
                        Exception error = task.Exception?.GetBaseException()
                            ?? new InvalidOperationException("Resource initialization failed.");
                        startupWindow.ProgressState.Phase = "Startup failed";
                        startupWindow.ProgressState.Status = error.Message;
                        startupWindow.ProgressState.Detail = "See the application log for details.";
                        Console.Error.WriteLine(error);
                        return;
                    }

                    var mainWindow = new MainWindow();
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                    startupWindow.Close();
                }));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void InitializeResources(StartupProgressState progress)
    {
        const int totalSteps = 5;

        void Report(int step, string phase, string status, float stepProgress, string detail = "")
        {
            progress.Title = "Preparing Mine Imator Simply Remade";
            progress.CurrentStep = step;
            progress.TotalSteps = totalSteps;
            progress.Phase = phase;
            progress.Status = status;
            progress.Detail = detail;
            progress.Progress = ((step - 1) + Math.Clamp(stepProgress, 0f, 1f)) / totalSteps;
        }

        Report(1, "Bootstrapping editor services", "Initializing audio engine...", 0f);
        Services.Initialize();
        Report(1, "Bootstrapping editor services", "Editor services ready.", 1f);

        BlockRegistry.Initialize((value, detail) =>
            Report(2, "Indexing Minecraft data", "Loading block registry...", value, detail));
        Report(2, "Indexing Minecraft data", "Block registry ready.", 1f,
            $"Loaded version {BlockRegistry.LoadedVersion}");

        TerrainAtlas.Initialize((value, detail) =>
            Report(3, "Loading terrain textures", "Building terrain atlas...", value, detail));
        Report(3, "Loading terrain textures", "Terrain atlas ready.", 1f,
            $"{TerrainAtlas.Textures.Count} texture(s) available");

        ItemsAtlas.Initialize((value, detail) =>
            Report(4, "Loading item textures", "Building item atlas...", value, detail));
        Report(4, "Loading item textures", "Item atlas ready.", 1f,
            $"{ItemsAtlas.TilePixels.Count} tile(s) available");

        CharacterRegistry.Initialize((value, detail) =>
            Report(5, "Discovering characters", "Scanning model libraries...", value, detail));
        Report(5, "Discovering characters", "Character registry ready.", 1f,
            $"{CharacterRegistry.Characters.Count} character(s) found");
    }
}
