using System.Drawing;
using Silk.NET.Core.Native;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;


class main
{
    private static Glfw _glfw;
    private static unsafe WindowHandle* _window;
    private static GL _gl;
    
    private static bool isVulkan = false;
    
    private static unsafe int Main(string[] args)
    {
        _glfw = Glfw.GetApi();
        if (!_glfw.Init())
        {
            Console.WriteLine("Failed to initialize GLFW");
            return 1;
        }
        
        _glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
        _glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
        _glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
        
        _window = _glfw.CreateWindow(640, 480, "Mine Imator Simply Remade: Nuxi", null, null);
        if (_window == null)
        {
            Console.WriteLine("Failed to create window");
            _glfw.Terminate();
            return 1;
        }
        
        _glfw.MakeContextCurrent(_window);
        
        _gl = GL.GetApi(_glfw.GetProcAddress);
        
        byte* versionPtr = _gl.GetString(StringName.Version);
        string openGlVersion = SilkMarshal.PtrToString((IntPtr)versionPtr) ?? throw new InvalidOperationException();
        Console.WriteLine($"OpenGL Version: {openGlVersion}");
        
        byte* rendererPtr = _gl.GetString(StringName.Renderer);
        string gpuRenderer = SilkMarshal.PtrToString((IntPtr)rendererPtr) ?? throw new InvalidOperationException();
        Console.WriteLine($"GPU: {gpuRenderer}");
        
        _gl.ClearColor(Color.Black);

        while (!_glfw.WindowShouldClose(_window))
        {
            _glfw.PollEvents();
            
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            
            _glfw.SwapBuffers(_window);
        }
        
        _gl.Dispose();
        _glfw.DestroyWindow(_window);
        _glfw.MakeContextCurrent(null);
        _glfw.Terminate();
        
        return 0;
    }
}