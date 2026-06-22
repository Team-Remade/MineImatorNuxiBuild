using GlmSharp;
using MineImatorSimplyRemade.core.window;
using MineImatorSimplyRemade.core.window.windows;
using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;


public static class main
{
    private static Glfw Glfw { get; set; }
    private static MainWindow MainWindow { get; set; }
    private static CameraWindow CameraWindow { get; set; }
    private static GL _gl;
    
    private static bool isVulkan = false;
    
    private static unsafe int Main(string[] args)
    {
        Glfw = Glfw.GetApi();
        if (!Glfw.Init())
        {
            Console.WriteLine("Failed to initialize GLFW");
            return 1;
        }

        // ── Main window ───────────────────────────────────────────────────────
        Glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
        Glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
        Glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);

        var monitor   = Glfw.GetPrimaryMonitor();
        var videoMode = Glfw.GetVideoMode(monitor);
        var size      = new ivec2(videoMode->Width - 200, videoMode->Height - 160);

        MainWindow = new MainWindow(size.x, size.y, "Mine Imator Simply Remade: Nuxi", Glfw);
        if (MainWindow.WindowHandle == null)
        {
            Console.WriteLine("Failed to create main window!");
            Glfw.Terminate();
            return 1;
        }
        MainWindow.CenterWindow();
        MainWindow.SetClearColor(new vec4(0.3f, 0.4f, 0.5f, 1));

        Glfw.MakeContextCurrent(MainWindow.WindowHandle);
        _gl = GL.GetApi(Glfw.GetProcAddress);
        MainWindow.SetGL(_gl);
        MainWindow.SetupImgui();

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
        while (!Glfw.WindowShouldClose(MainWindow.WindowHandle))
        {
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

            Thread.Sleep(1);
        }

        _gl.Dispose();
        Glfw.DestroyWindow(CameraWindow.WindowHandle);
        Glfw.DestroyWindow(MainWindow.WindowHandle);
        Glfw.Terminate();

        return 0;
    }

    private static unsafe bool IsWindowVisible()
    {
        // GLFW_VISIBLE attribute: 1 = visible, 0 = hidden.
        return Glfw.GetWindowAttrib(CameraWindow.WindowHandle, WindowAttributeGetter.Visible);
    }
}
