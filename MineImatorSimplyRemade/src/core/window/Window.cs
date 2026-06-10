using System.Drawing;
using GlmSharp;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.window;

public class Window
{
    private static unsafe WindowHandle* windowHandle;
    public unsafe WindowHandle* WindowHandle => windowHandle;
    private GL _gl;
    private vec4 clearColor = new vec4(0, 0, 0, 1);

    public unsafe Window(int width, int height, string title, GL gl = null)
    {
        windowHandle = main.Glfw.CreateWindow(width, height, title, null, null);
        _gl = gl;
    }

    public unsafe void Render()
    {
        main.Glfw.MakeContextCurrent(windowHandle);
        _gl.ClearColor(clearColor.r, clearColor.g, clearColor.b, clearColor.a);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        main.Glfw.SwapBuffers(windowHandle);
    }

    public void SetGL(GL gl)
    {
        _gl = gl;
    }

    public void SetClearColor(vec4 color)
    {
        clearColor = color;
    }
}