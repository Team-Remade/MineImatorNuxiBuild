using System.Diagnostics;
using GlmSharp;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.diagnostics;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemade.core.log;
using MineImatorSimplyRemade.core.startup;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemade.core.window.windows;
using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

public static class main
{
    public const string ApplicationLocalDirectory = "SimplyRemadeNuxi";
    private const int MainLoopTargetFps = 60;

    public static readonly string LocalPath =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string ApplicationLocalDirectoryPath { get; } =
        Path.Combine(LocalPath, ApplicationLocalDirectory);

    private static Glfw Glfw { get; set; }
    private static MainWindow MainWindow { get; set; }
    private static CameraWindow CameraWindow { get; set; }
    private static GL _gl;
    private static GL? _startupGl;

    private static unsafe int Main(string[] args)
    {
        // Set up file logging (logs\latest.log + up to 5 rotated logs\previous-N.log)
        // as early as possible so startup output is captured too.
        Logger.Initialize();

        SentrySdk.Init(options =>
        {
            // A Sentry Data Source Name (DSN) is required.
            // See https://docs.sentry.io/product/sentry-basics/dsn-explainer/
            // You can set it in the SENTRY_DSN environment variable, or you can set it in code here.
            options.Dsn = "https://ba6126794a0f2713d069a8f2b7311187@o4511769100419072.ingest.us.sentry.io/4511769112739840";

            // When debug is enabled, the Sentry client will emit detailed debugging information to the console.
            // This might be helpful, or might interfere with the normal operation of your application.
            // We enable it here for demonstration purposes when first trying Sentry.
            // You shouldn't do this in your applications unless you're troubleshooting issues with Sentry.
            options.Debug = false;

            // This option is recommended. It enables Sentry's "Release Health" feature.
            options.AutoSessionTracking = true;
            // Enable logs to be sent to Sentry
            options.EnableLogs = true;
        });

        // Catch unhandled exceptions on background threads too (e.g. Task continuations),
        // writing a crash report and showing the native "something went wrong" dialog.
        CrashReporter.Initialize();

        try
        {
            string renderer = args.FirstOrDefault(arg => arg.StartsWith("--renderer=", StringComparison.OrdinalIgnoreCase))?
                .Split('=', 2)[1].ToLowerInvariant() ?? "opengl";
            if (renderer is not ("opengl" or "vulkan" or "d3d11"))
            {
                Console.Error.WriteLine("Supported renderers: --renderer=opengl, --renderer=vulkan, --renderer=d3d11");
                return 1;
            }

            PreferencesPanel.LoadLanguagePreference();
            NativeLibraryBootstrap.Initialize();

            Glfw = Glfw.GetApi();
            if (!Glfw.Init())
            {
                Console.WriteLine("Failed to initialize GLFW");
                return 1;
            }

            var startupWindow = CreateStartupWindow();
            UpdateStartupWindow(startupWindow, new StartupProgressState
            {
                CurrentStep = 1,
                TotalSteps = 8,
                Phase = "Bootstrapping startup",
                Status = "Checking local dependencies...",
                Detail = "Preparing early loading window",
                Progress = 0.02f
            });

            RunFfmpegBootstrap(startupWindow);

            // ── Main window ───────────────────────────────────────────────────────
            Glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
            Glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
            Glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);

            var monitor = Glfw.GetPrimaryMonitor();
            var videoMode = Glfw.GetVideoMode(monitor);
            var size = new ivec2(videoMode->Width - 200, videoMode->Height - 160);

            MainWindow = new MainWindow(size.x, size.y, "Mine Imator Nuxi", Glfw, visible: false);
            if (MainWindow.WindowHandle == null)
            {
                Console.WriteLine("Failed to create main window!");
                startupWindow?.Dispose();
                _startupGl?.Dispose();
                _startupGl = null;
                Glfw.Terminate();
                return 1;
            }

            MainWindow.CenterWindow();
            MainWindow.SetClearColor(new vec4(0.3f, 0.4f, 0.5f, 1));

            Glfw.MakeContextCurrent(MainWindow.WindowHandle);
            Glfw.SwapInterval(0); // Disable GLFW OpenGL VSync blocking to avoid conflicting with the frame limiter
            _gl = GL.GetApi(Glfw.GetProcAddress);
            MainWindow.SetGL(_gl);
            
            if (renderer != "opengl")
            {
                try
                {
                    VeldridContext.CreateSceneRenderer(_gl, renderer == "vulkan"
                        ? Veldrid.GraphicsBackend.Vulkan
                        : Veldrid.GraphicsBackend.Direct3D11);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Veldrid] Failed to initialize requested {renderer} scene renderer: {ex.Message}. Using OpenGL.");
                }
            }

            Directory.CreateDirectory(ApplicationLocalDirectoryPath);
            string mainWindowIniPath = Path.Combine(ApplicationLocalDirectoryPath, "imgui.ini");
            try
            {
                if (File.Exists(mainWindowIniPath))
                {
                    string settings = File.ReadAllText(mainWindowIniPath);
                    string migrated = Localization.MigrateWindowSettings(settings,
                        "Viewport", "Timeline", "Content Browser", "Scene Tree", "Properties", "Preferences",
                        "Spawn Menu##SpawnMenuWindow", "Import from world", "Camera View##CamViewInline", "Project Home Screen");
                    if (migrated != settings)
                        File.WriteAllText(mainWindowIniPath, migrated);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Unable to migrate localized panel layout: {ex.Message}");
            }
            MainWindow.SetupImgui(mainWindowIniPath);
            Services.Initialize();
            MainWindow.InitializeRuntime(progress => UpdateStartupWindow(startupWindow, RemapStartupState(progress)));
            startupWindow?.Dispose();
            _startupGl?.Dispose();
            _startupGl = null;
            MainWindow.Show();

            // ── Camera window ─────────────────────────────────────────────────────
            // Created now (before the loop) with context sharing so it can access
            // all textures/FBOs from the main window. Starts hidden; shown on demand.
            Glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
            Glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
            Glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);

            CameraWindow = new CameraWindow(Glfw, _gl, MainWindow.WindowHandle);
            CameraWindow.RenderingDebugRequested = MainWindow.ToggleRenderingDebug;
            CameraWindow.MetricsOverlay = MainWindow.MetricsOverlay;
            CameraWindow.SetClearColor(new vec4(0.1f, 0.1f, 0.1f, 1f));
            // Each window owns its own ImGui context. Render() sets it current before
            // every frame so the two contexts never interfere.
            Glfw.MakeContextCurrent(CameraWindow.WindowHandle);
            // No dockable panels in the camera preview window either - disable ini
            // load/save for the same reason as the startup window above.
            CameraWindow.SetupImgui(null);
            // Restore main window context after camera window setup.
            Glfw.MakeContextCurrent(MainWindow.WindowHandle);

            // CameraWindow.SetupImgui() gave it its own independent ImGui context/style,
            // which otherwise stays on ImGui's default blue theme forever. Apply the
            // user's saved theme/accent to it now, and keep it in sync whenever the user
            // changes theme/accent in Preferences (which only touches MainWindow's context).
            var preferencesPanel = MainWindow.GetPreferencesPanel();
            if (preferencesPanel != null)
            {
                CameraWindow.WithContextCurrent(preferencesPanel.ApplyThemeAndAccentToCurrentContext);

                preferencesPanel.ThemeChanged += _ =>
                    CameraWindow.WithContextCurrent(preferencesPanel.ApplyThemeAndAccentToCurrentContext);
                preferencesPanel.AccentColorChanged += _ =>
                    CameraWindow.WithContextCurrent(preferencesPanel.ApplyThemeAndAccentToCurrentContext);
            }

            // Wire the camera viewport to this window.
            // MainWindow.SetGL has already run, so GetCameraViewport() is valid.
            var camViewport = MainWindow.GetCameraViewport();
            if (camViewport != null)
            {
                CameraWindow.Panel = camViewport;

                void ShowUndockedCameraWindow()
                {
                    // Update the camera viewport's GLFW references to point to the camera window
                    // so that free-fly controls work correctly in the undocked window.
                    camViewport.GlfwApiPreview = Glfw;
                    camViewport.GlfwWindowPreview = CameraWindow.WindowHandle;

                    Glfw.MakeContextCurrent(CameraWindow.WindowHandle);
                    CameraWindow.Show();
                    Glfw.MakeContextCurrent(MainWindow.WindowHandle);
                }

                // Pop button → show the window and mark undocked.
                camViewport.PopRequested += ShowUndockedCameraWindow;

                camViewport.HideRequested += () =>
                {
                    Glfw.MakeContextCurrent(CameraWindow.WindowHandle);
                    CameraWindow.Hide();
                    Glfw.MakeContextCurrent(MainWindow.WindowHandle);
                };

                // Restore undocked preview window between launches.
                if (camViewport.Undocked)
                    ShowUndockedCameraWindow();
            }

            byte* versionPtr = _gl.GetString(StringName.Version);
            string openGlVersion = SilkMarshal.PtrToString((IntPtr)versionPtr) ?? throw new InvalidOperationException();
            Console.WriteLine($"OpenGL Version: {openGlVersion}");

            byte* rendererPtr = _gl.GetString(StringName.Renderer);
            string gpuRenderer = SilkMarshal.PtrToString((IntPtr)rendererPtr) ?? throw new InvalidOperationException();
            Console.WriteLine($"GPU: {gpuRenderer}");

            // ── Main loop ─────────────────────────────────────────────────────────
            var frameTimer = Stopwatch.StartNew();

            while (!Glfw.WindowShouldClose(MainWindow.WindowHandle) || !MainWindow.CanWindowClose())
            {
                var profiler = FrameProfiler.Shared;
                profiler.BeginFrame();
                try
                {
                    long frameStartTicks = frameTimer.ElapsedTicks;
                    using (profiler.Measure(ProfileStage.FrameWork))
                    {
                        using (profiler.Measure(ProfileStage.BackendBegin))
                            VeldridContext.BeginFrame();
                        using (profiler.Measure(ProfileStage.Events))
                            Glfw.PollEvents();

                        using (profiler.Measure(ProfileStage.MainWindow))
                        {
                            Glfw.MakeContextCurrent(MainWindow.WindowHandle);
                            MainWindow.Render();
                        }

                        // CameraWindow.Render manages its own context switching.
                        if (CameraWindow.ShouldClose)
                        {
                            if (camViewport != null) camViewport.DockToInlineVisible();
                            CameraWindow.Hide();
                            Glfw.SetWindowShouldClose(CameraWindow.WindowHandle, false);
                        }
                        else if (IsWindowVisible())
                        {
                            using (profiler.Measure(ProfileStage.PreviewWindow))
                            {
                                CameraWindow.Render();
                                Glfw.MakeContextCurrent(MainWindow.WindowHandle);
                            }
                        }
                        using (profiler.Measure(ProfileStage.BackendEnd))
                            VeldridContext.EndFrame();
                    }

                    int targetFps = preferencesPanel?.TargetFps ?? MainLoopTargetFps;
                    long workEndTicks = frameTimer.ElapsedTicks;
                    long targetFrameTicks = targetFps > 0 ? Stopwatch.Frequency / targetFps : 0;
                    using (profiler.Measure(ProfileStage.FrameLimiter))
                        LimitFrameRate(frameTimer, frameStartTicks, targetFrameTicks);
                    MainWindow.MetricsOverlay.RecordFrame(
                        (frameTimer.ElapsedTicks - frameStartTicks) * 1000.0 / Stopwatch.Frequency,
                        (workEndTicks - frameStartTicks) * 1000.0 / Stopwatch.Frequency, targetFps);
                }
                finally
                {
                    profiler.EndFrame();
                }
            }
            
            Console.WriteLine("Stopping...");
            
            var rng = new Random();
            var num = rng.Next(100);
            if (num == 5) Console.WriteLine("Fish");

            Services.Shutdown();

            MainWindow.ShutdownWorldImporter();
            MainWindow.ShutdownViewportFramebuffers();
            MainWindow.ShutdownImgui();
            CameraWindow.ShutdownImgui();

            VeldridContext.Dispose();

            _gl.Dispose();
            Glfw.DestroyWindow(CameraWindow.WindowHandle);
            Glfw.DestroyWindow(MainWindow.WindowHandle);
            Glfw.Terminate();

            return 0;
        }
        catch (Exception e)
        {
            // Log it, send it to Sentry, write a crash report, and show the native
            // "something went wrong" window instead of letting the exception escape.
            CrashReporter.Report(e, "Main");
            return 1;
        }
        finally
        {
            VeldridContext.Dispose();
            Logger.Shutdown();
        }
    }

    private static void LimitFrameRate(Stopwatch frameTimer, long frameStartTicks, long targetFrameTicks)
    {
        // Temporarily disabled frame limiter for performance/uncapped FPS benchmarking
        bool disableLimiter = false;
        if (disableLimiter || targetFrameTicks <= 0)
            return;

        long oneMillisecondTicks = Stopwatch.Frequency / 1000;

        while (true)
        {
            long elapsedTicks = frameTimer.ElapsedTicks - frameStartTicks;
            long remainingTicks = targetFrameTicks - elapsedTicks;
            if (remainingTicks <= 0)
                break;

            // Only sleep if remaining time is well above Windows standard timer resolution (~15ms)
            // Windows Thread.Sleep(1) often sleeps for 15.6ms, causing severe FPS drops (e.g. 60 FPS -> ~42 FPS)
            if (remainingTicks > oneMillisecondTicks * 16)
            {
                Thread.Sleep(1);
            }
            else if (remainingTicks > oneMillisecondTicks * 2)
            {
                Thread.Yield();
            }
            else
            {
                Thread.SpinWait(50);
            }
        }
    }

    private static unsafe bool IsWindowVisible()
    {
        // GLFW_VISIBLE attribute: 1 = visible, 0 = hidden.
        return Glfw.GetWindowAttrib(CameraWindow.WindowHandle, WindowAttributeGetter.Visible);
    }

    private static StartupProgressState RemapStartupState(StartupProgressState progress)
    {
        return new StartupProgressState
        {
            Title = progress.Title,
            CurrentStep = progress.CurrentStep + 1,
            TotalSteps = progress.TotalSteps + 1,
            Phase = progress.Phase,
            Status = progress.Status,
            Detail = progress.Detail,
            Progress = 0.10f + progress.Progress * 0.90f
        };
    }

    private static unsafe StartupProgressWindow? CreateStartupWindow()
    {
        Glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
        Glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
        Glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);

        var startupWindow = new StartupProgressWindow(560, 280, Localization.T("text.common.preparing_mine_imator_simply_remade"), Glfw);
        if (startupWindow.WindowHandle == null)
        {
            return null;
        }

        startupWindow.CenterWindow();
        startupWindow.SetClearColor(new vec4(0.11f, 0.11f, 0.13f, 1f));

        Glfw.MakeContextCurrent(startupWindow.WindowHandle);
        _startupGl = GL.GetApi(Glfw.GetProcAddress);
        startupWindow.SetGL(_startupGl);
        // No dockable panels in the startup splash - disable ini load/save entirely so
        // this context can never race the main window's context over imgui.ini.
        startupWindow.SetupImgui(null);

        return startupWindow;
    }

    private static void RunFfmpegBootstrap(StartupProgressWindow? startupWindow)
    {
        if (!FfmpegBootstrap.RequiresFirstTimeDownload())
        {
            UpdateStartupWindow(startupWindow, new StartupProgressState
            {
                CurrentStep = 1,
                TotalSteps = 8,
                Phase = "Bootstrapping startup",
                Status = "Verifying FFmpeg binaries...",
                Detail = "Local video encoding tools already exist",
                Progress = 0.08f
            });
            FfmpegBootstrap.EnsureFfmpegInstalled();
            return;
        }

        UpdateStartupWindow(startupWindow, new StartupProgressState
        {
            CurrentStep = 1,
            TotalSteps = 8,
            Phase = "Installing video encoding tools",
            Status = "First launch detected. Looking for an existing FFmpeg install.",
            Detail = "You can choose an existing ffmpeg binary, or cancel to download automatically",
            Progress = 0.05f
        });

        string importStatus = "No existing ffmpeg binary selected.";
        bool importedExistingInstall =
            FfmpegBootstrap.TryImportExistingInstallOnFirstLaunch(message => importStatus = message);

        if (importedExistingInstall && !FfmpegBootstrap.RequiresFirstTimeDownload())
        {
            UpdateStartupWindow(startupWindow, new StartupProgressState
            {
                CurrentStep = 1,
                TotalSteps = 8,
                Phase = "Bootstrapping startup",
                Status = "Using existing FFmpeg binaries.",
                Detail = importStatus,
                Progress = 0.09f
            });

            FfmpegBootstrap.EnsureFfmpegInstalled();
            return;
        }

        string status = "Preparing FFmpeg setup...";
        Exception? downloadError = null;

        var installTask = Task.Run(() =>
        {
            try
            {
                FfmpegBootstrap.EnsureFfmpegInstalled(message => status = message);
            }
            catch (Exception ex)
            {
                downloadError = ex;
            }
        });

        while (!installTask.IsCompleted)
        {
            UpdateStartupWindow(startupWindow, new StartupProgressState
            {
                CurrentStep = 1,
                TotalSteps = 8,
                Phase = "Installing video encoding tools",
                Status = "First launch detected. Downloading FFmpeg binaries.",
                Detail = status,
                Progress = 0.06f
            });

            Thread.Sleep(16);
        }

        if (downloadError != null)
        {
            Console.Error.WriteLine($"[FFmpeg] Failed to initialize local ffmpeg binaries: {downloadError.Message}");
        }

        UpdateStartupWindow(startupWindow, new StartupProgressState
        {
            CurrentStep = 1,
            TotalSteps = 8,
            Phase = "Bootstrapping startup",
            Status = "FFmpeg check complete.",
            Detail = downloadError == null
                ? "Continuing into asset initialization"
                : "FFmpeg failed to initialize; continuing startup",
            Progress = 0.10f
        });
    }

    private static unsafe void UpdateStartupWindow(StartupProgressWindow? startupWindow, StartupProgressState state)
    {
        if (startupWindow == null)
            return;

        startupWindow.ProgressState.Title = state.Title;
        startupWindow.ProgressState.CurrentStep = state.CurrentStep;
        startupWindow.ProgressState.TotalSteps = state.TotalSteps;
        startupWindow.ProgressState.Phase = state.Phase;
        startupWindow.ProgressState.Status = state.Status;
        startupWindow.ProgressState.Detail = state.Detail;
        startupWindow.ProgressState.Progress = state.Progress;

        Glfw.PollEvents();

        if (!Glfw.WindowShouldClose(startupWindow.WindowHandle))
        {
            Glfw.MakeContextCurrent(startupWindow.WindowHandle);
            startupWindow.Render();
        }

        if (MainWindow?.WindowHandle != null)
            Glfw.MakeContextCurrent(MainWindow.WindowHandle);
    }
}
