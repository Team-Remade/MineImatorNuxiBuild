using GlmSharp;
using MineImatorSimplyRemade.core.window;
using MineImatorSimplyRemade.core.window.windows;
using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;


public static class main
{
    private static Glfw Glfw { get; set; }
    private static List<Window> Windows { get; } = new List<Window>();
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
        
        Glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
        Glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
        Glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
        
        var monitor = Glfw.GetPrimaryMonitor();
        var videoMode = Glfw.GetVideoMode(monitor);
        var size = new ivec2(videoMode->Width, videoMode->Height);

        size.x -= 200;
        size.y -= 160;
        
        Windows.Add(new MainWindow(size.x, size.y, "Mine Imator Simply Remade: Nuxi", Glfw));
        if (Windows[0].WindowHandle == null)
        {
            Console.WriteLine("Failed to create main window!");
            Glfw.Terminate();
            return 1;
        }
        Windows[0].CenterWindow();

        Windows[0].SetClearColor(new vec4(0.3f, 0.4f, 0.5f, 1));
        
        Glfw.MakeContextCurrent(Windows[0].WindowHandle);
        
        _gl = GL.GetApi(Glfw.GetProcAddress);
        
        Windows[0].SetGL(_gl);
        Windows[0].SetupImgui();
        
        byte* versionPtr = _gl.GetString(StringName.Version);
        string openGlVersion = SilkMarshal.PtrToString((IntPtr)versionPtr) ?? throw new InvalidOperationException();
        Console.WriteLine($"OpenGL Version: {openGlVersion}");
        
        byte* rendererPtr = _gl.GetString(StringName.Renderer);
        string gpuRenderer = SilkMarshal.PtrToString((IntPtr)rendererPtr) ?? throw new InvalidOperationException();
        Console.WriteLine($"GPU: {gpuRenderer}");

        while (!Glfw.WindowShouldClose(Windows[0].WindowHandle))
        {
            Glfw.PollEvents();

            foreach (var window in Windows)
            {
                window.Render();
            }
            
            Thread.Sleep(1);
        }
        
        _gl.Dispose();
        
        foreach (var window in Windows)
        {
            Glfw.DestroyWindow(window.WindowHandle);
        }
        
        Glfw.Terminate();
        
        return 0;
    }
}