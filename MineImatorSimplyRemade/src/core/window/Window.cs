using System.Runtime.CompilerServices;
using GlmSharp;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using Monitor = Silk.NET.GLFW.Monitor;

namespace MineImatorSimplyRemade.core.window;

public class Window : IDisposable
{
    protected unsafe WindowHandle* windowHandle;
    public unsafe WindowHandle* WindowHandle => windowHandle;
    private GL _gl;
    private Glfw Glfw;
    private vec4 clearColor = new vec4(0, 0, 0, 1);
    private ImGuiIOPtr io;

    public unsafe Window(int width, int height, string title, Glfw glfw, GL gl = null)
    {
        Glfw = glfw;
        windowHandle = Glfw.CreateWindow(width, height, title, null, null);
        _gl = gl;
    }

    public unsafe void SetupImgui()
    {
        var context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);
        
        io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

        io.Fonts.AddFontDefault();
        
        ImGui.StyleColorsDark();
        
        ImGuiImplGLFW.SetCurrentContext(context);
        
        IntPtr nativeHandleValue = (IntPtr)windowHandle;
        
        ImGuiImplGLFW.InitForOpenGL(Unsafe.BitCast<IntPtr, GLFWwindowPtr>(nativeHandleValue), true);
        
        ImGuiImplOpenGL3.SetCurrentContext(context);
        ImGuiImplOpenGL3.Init("#version 150");
    }

    public unsafe void Render()
    {
        Glfw.MakeContextCurrent(windowHandle);
        _gl.ClearColor(clearColor.r, clearColor.g, clearColor.b, clearColor.a);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        
        ImGuiImplOpenGL3.NewFrame();
        ImGuiImplGLFW.NewFrame();
        ImGui.NewFrame();
        
        RenderUi();
        
        ImGui.Render();
        ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
        
        if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
        {
            ImGui.UpdatePlatformWindows();
            ImGui.RenderPlatformWindowsDefault();
        }
        
        Glfw.SwapBuffers(windowHandle);
    }

    protected virtual void RenderUi()
    {
        
    }

    public void SetGL(GL gl)
    {
        _gl = gl;
    }

    public void SetClearColor(vec4 color)
    {
        clearColor = color;
    }
    
    public unsafe void CenterWindow()
    {
        Monitor* monitor = Glfw.GetPrimaryMonitor();
        VideoMode* mode = Glfw.GetVideoMode(monitor);

        int monitorX, monitorY;
        Glfw.GetMonitorPos(monitor, out monitorX, out monitorY);
        
        int windowWidth, windowHeight;
        Glfw.GetWindowSize(windowHandle, out windowWidth, out windowHeight);
        
        int centerX = monitorX + (mode->Width - windowWidth) / 2;
        int centerY = monitorY + (mode->Height - windowHeight) / 2;
        
        Glfw.SetWindowPos(windowHandle, centerX, centerY);
    }

    public void Dispose()
    {
    }
}