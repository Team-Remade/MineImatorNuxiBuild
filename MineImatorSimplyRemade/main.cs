using GlmSharp;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.startup;
using MineImatorSimplyRemade.core.window;
using MineImatorSimplyRemade.core.window.windows;
using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using System.Diagnostics;


public static class main
{
    public const string ApplicationLocalDirectory = "SimplyRemadeNuxi";
    private const int MainLoopTargetFps = 60;

    public static readonly string LocalPath =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string ApplicationLocalDirectoryPath {get; private set;} = Path.Combine(main.LocalPath, main.ApplicationLocalDirectory);
    
    private static Glfw Glfw { get; set; }
    private static MainWindow MainWindow { get; set; }
    private static CameraWindow CameraWindow { get; set; }
    private static GL _gl;
    private static GL? _startupGl;
    
    private static bool isVulkan = false;
    
    private static unsafe int Main(string[] args)
    {
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

        var monitor   = Glfw.GetPrimaryMonitor();
        var videoMode = Glfw.GetVideoMode(monitor);
        var size      = new ivec2(videoMode->Width - 200, videoMode->Height - 160);

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
        _gl = GL.GetApi(Glfw.GetProcAddress);
        MainWindow.SetGL(_gl);
        MainWindow.SetupImgui();
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
        CameraWindow.SetClearColor(new vec4(0.1f, 0.1f, 0.1f, 1f));
        // Each window owns its own ImGui context. Render() sets it current before
        // every frame so the two contexts never interfere.
        Glfw.MakeContextCurrent(CameraWindow.WindowHandle);
        CameraWindow.SetupImgui();
        // Restore main window context after camera window setup.
        Glfw.MakeContextCurrent(MainWindow.WindowHandle);

        // Wire the camera viewport to this window.
        // MainWindow.SetGL has already run, so GetCameraViewport() is valid.
        var camViewport = MainWindow.GetCameraViewport();
        if (camViewport != null)
        {
            CameraWindow.Panel = camViewport;

            // Pop button → show the window and mark undocked.
            camViewport.PopRequested += () =>
            {
                Glfw.MakeContextCurrent(CameraWindow.WindowHandle);
                CameraWindow.Show();
                Glfw.MakeContextCurrent(MainWindow.WindowHandle);
            };
        }

        byte* versionPtr = _gl.GetString(StringName.Version);
        string openGlVersion = SilkMarshal.PtrToString((IntPtr)versionPtr) ?? throw new InvalidOperationException();
        Console.WriteLine($"OpenGL Version: {openGlVersion}");

        byte* rendererPtr = _gl.GetString(StringName.Renderer);
        string gpuRenderer = SilkMarshal.PtrToString((IntPtr)rendererPtr) ?? throw new InvalidOperationException();
        Console.WriteLine($"GPU: {gpuRenderer}");

        // ── Main loop ─────────────────────────────────────────────────────────
        var frameTimer = Stopwatch.StartNew();
        long targetFrameTicks = Stopwatch.Frequency / MainLoopTargetFps;

        while (!Glfw.WindowShouldClose(MainWindow.WindowHandle))
        {
            long frameStartTicks = frameTimer.ElapsedTicks;

            Glfw.PollEvents();

            // Main window always renders.
            Glfw.MakeContextCurrent(MainWindow.WindowHandle);
            MainWindow.Render();

            // Camera window: handle OS close button, then render if visible.
            // CameraWindow.Render() manages its own context switching internally.
            if (CameraWindow.ShouldClose)
            {
                if (camViewport != null) camViewport.Undocked = false;
                CameraWindow.Hide();
                Glfw.SetWindowShouldClose(CameraWindow.WindowHandle, false);
            }
            else if (IsWindowVisible())
            {
                CameraWindow.Render();
                // Restore main context after camera window's render.
                Glfw.MakeContextCurrent(MainWindow.WindowHandle);
            }

            LimitFrameRate(frameTimer, frameStartTicks, targetFrameTicks);
        }

        _gl.Dispose();
        Glfw.DestroyWindow(CameraWindow.WindowHandle);
        Glfw.DestroyWindow(MainWindow.WindowHandle);
        Glfw.Terminate();

        return 0;
    }

    private static void LimitFrameRate(Stopwatch frameTimer, long frameStartTicks, long targetFrameTicks)
    {
        if (targetFrameTicks <= 0)
            return;

        while (true)
        {
            long elapsedTicks = frameTimer.ElapsedTicks - frameStartTicks;
            long remainingTicks = targetFrameTicks - elapsedTicks;
            if (remainingTicks <= 0)
                break;

            long oneMillisecondTicks = Stopwatch.Frequency / 1000;
            if (remainingTicks > oneMillisecondTicks * 2)
            {
                int sleepMs = (int)(remainingTicks / oneMillisecondTicks) - 1;
                if (sleepMs > 0)
                    Thread.Sleep(sleepMs);
            }
            else
            {
                Thread.SpinWait(100);
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

        var startupWindow = new StartupProgressWindow(560, 210, "Preparing Mine Imator Simply Remade", Glfw);
        if (startupWindow.WindowHandle == null)
        {
            return null;
        }

        startupWindow.CenterWindow();
        startupWindow.SetClearColor(new vec4(0.11f, 0.11f, 0.13f, 1f));

        Glfw.MakeContextCurrent(startupWindow.WindowHandle);
        _startupGl = GL.GetApi(Glfw.GetProcAddress);
        startupWindow.SetGL(_startupGl);
        startupWindow.SetupImgui();

        return startupWindow;
    }

    private static unsafe void RunFfmpegBootstrap(StartupProgressWindow? startupWindow)
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
            Detail = downloadError == null ? "Continuing into asset initialization" : "FFmpeg failed to initialize; continuing startup",
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
