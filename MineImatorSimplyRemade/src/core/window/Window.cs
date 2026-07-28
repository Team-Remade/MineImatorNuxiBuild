using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GlmSharp;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using StbImageSharp;
using Monitor = Silk.NET.GLFW.Monitor;

namespace MineImatorSimplyRemade.core.window;

public class Window : IDisposable
{
    protected unsafe WindowHandle* windowHandle;
    public unsafe WindowHandle* WindowHandle => windowHandle;
    public int WindowWidth, WindowHeight;
    
    private GL _gl;
    public GL GL => _gl;
    protected Glfw Glfw;
    
    private vec4 clearColor = new vec4(0, 0, 0, 1);
    private ImGuiIOPtr io;
    protected ImGuiContextPtr ImGuiContext { get; private set; }

    // Dear ImGui stores io.IniFilename as a raw pointer (it never copies or owns the
    // string), so the backing buffer must be allocated on the unmanaged heap and kept
    // alive for the lifetime of this window's context. Freed in ShutdownImgui().
    private unsafe byte* _iniFilenameBuffer;

    // Exposed to subclasses for custom render paths.
    protected float ClearR => clearColor.r;
    protected float ClearG => clearColor.g;
    protected float ClearB => clearColor.b;
    protected float ClearA => clearColor.a;

    /// <summary>
    /// Activates this window's ImGui context and both backends.
    /// Call before any ImGui work when implementing a custom <see cref="Render"/> override.
    /// </summary>
    protected unsafe void SetContextCurrent()
    {
        if (ImGuiContext.Handle != null)
        {
            ImGui.SetCurrentContext(ImGuiContext);
            ImGuiImplGLFW.SetCurrentContext(ImGuiContext);
            ImGuiImplOpenGL3.SetCurrentContext(ImGuiContext);
        }
    }

    private unsafe void FrameBufferResizeCallback(WindowHandle* windowHandle, int width, int height)
    {
        WindowWidth = width;
        WindowHeight = height;
    }

    /// <summary>
    /// Creates a visible window with its own GL context.
    /// Used for the main application window.
    /// </summary>
    public unsafe Window(int width, int height, string title, Glfw glfw, GL gl = null, bool visible = true)
    {
        Glfw = glfw;

        if (!visible)
            Glfw.WindowHint(WindowHintBool.Visible, false);

        windowHandle = Glfw.CreateWindow(width, height, title, null, null);

        if (!visible)
            Glfw.WindowHint(WindowHintBool.Visible, true);

        _gl = gl;

        WindowWidth = width;
        WindowHeight = height;

        Glfw.SetFramebufferSizeCallback(windowHandle, FrameBufferResizeCallback);
    }

    /// <summary>
    /// Creates a window that shares the GL context of <paramref name="shareWith"/>.
    /// The window starts hidden; call <see cref="Show"/> to make it visible.
    /// Use this for all secondary windows (camera view, render output, etc.) so
    /// they can access textures and FBOs created on the main context.
    /// </summary>
    public unsafe Window(int width, int height, string title, Glfw glfw, GL gl,
                         WindowHandle* shareWith)
    {
        Glfw = glfw;

        // Start hidden — caller decides when to show it.
        Glfw.WindowHint(WindowHintBool.Visible, false);
        windowHandle = Glfw.CreateWindow(width, height, title, null, shareWith);
        // Reset the visible hint so subsequent windows aren't affected.
        Glfw.WindowHint(WindowHintBool.Visible, true);

        _gl = gl;

        WindowWidth = width;
        WindowHeight = height;

        Glfw.SetFramebufferSizeCallback(windowHandle, FrameBufferResizeCallback);
    }

    /// <summary>Makes the OS window visible.</summary>
    public unsafe void Show() => Glfw.ShowWindow(windowHandle);

    /// <summary>Hides the OS window without destroying it.</summary>
    public unsafe void Hide() => Glfw.HideWindow(windowHandle);

    /// <summary>Returns true if the OS close button has been pressed.</summary>
    public unsafe bool ShouldClose => Glfw.WindowShouldClose(windowHandle);

    /// <summary>
    /// Creates an independent ImGui context for this window and fully initialises
    /// both the GLFW and OpenGL3 backends against this window's GLFW handle.
    /// Each window that calls this owns its own context; <see cref="Render"/> sets
    /// it current at the start of every frame so windows never interfere.
    /// </summary>
    /// <param name="iniFilename">
    /// Absolute path this context should use for its imgui.ini (docking/window layout)
    /// settings, or <c>null</c> to disable ini load/save entirely for this context.
    /// Every window created via <see cref="Window"/> gets its own independent ImGui
    /// context, but Dear ImGui's default <c>io.IniFilename</c> is the bare relative
    /// name <c>"imgui.ini"</c> for every context. Left unset, every window in the
    /// process (startup splash, main window, camera window, ...) would silently read
    /// from and autosave to that same relative-to-CWD file, racing each other and
    /// making "is this the first launch" checks unreliable. Callers must pass an
    /// explicit absolute path (for windows whose layout should persist) or explicit
    /// <c>null</c> (for windows that have no dockable layout to persist) so only one
    /// context ever owns a given ini file.
    /// </param>
    public unsafe void SetupImgui(string? iniFilename)
    {
        ImGuiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(ImGuiContext);
        
        io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
        io.ConfigWindowsMoveFromTitleBarOnly = true;

        // io.IniFilename is a raw byte* that Dear ImGui reads from directly (on
        // NewFrame()/autosave) without ever copying it, so the buffer must be
        // allocated on the unmanaged heap and kept alive - see _iniFilenameBuffer.
        // Passing null disables ini load/save entirely for this context.
        _iniFilenameBuffer = iniFilename != null
            ? (byte*)Marshal.StringToCoTaskMemUTF8(iniFilename)
            : null;
        io.IniFilename = _iniFilenameBuffer;

        io.Fonts.AddFontDefault();
        
        ImGui.StyleColorsDark();
        
        ImGuiImplGLFW.SetCurrentContext(ImGuiContext);
        
        IntPtr nativeHandleValue = (IntPtr)windowHandle;
        ImGuiImplGLFW.InitForOpenGL(Unsafe.BitCast<IntPtr, GLFWwindowPtr>(nativeHandleValue), true);
        
        ImGuiImplOpenGL3.SetCurrentContext(ImGuiContext);
        ImGuiImplOpenGL3.Init("#version 150");
    }

    public virtual unsafe void Render()
    {
        SetContextCurrent();

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

    /// <summary>
    /// Tears down the ImGui backends and destroys the ImGui context for this window.
    /// Must be called while the shared GL context is still alive (i.e. before the
    /// owning GLFW window is destroyed) and while GLFW is still initialised.
    /// Idempotent: safe to call more than once.
    /// </summary>
    public virtual unsafe void ShutdownImgui()
    {
        if (ImGuiContext.Handle == null)
            return;

        ImGui.SetCurrentContext(ImGuiContext);
        ImGuiImplGLFW.SetCurrentContext(ImGuiContext);
        ImGuiImplOpenGL3.SetCurrentContext(ImGuiContext);

        ImGuiImplOpenGL3.Shutdown();
        ImGuiImplGLFW.Shutdown();
        ImGui.DestroyContext(ImGuiContext);

        ImGuiContext = default;

        if (_iniFilenameBuffer != null)
        {
            Marshal.FreeCoTaskMem((IntPtr)_iniFilenameBuffer);
            _iniFilenameBuffer = null;
        }
    }

    protected virtual void RenderUi()
    {
        
    }

    public virtual void SetGL(GL gl)
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

    public ImageResult LoadEmbeddedImage(string resourceName)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        using Stream? stream = assembly.GetManifestResourceStream($"MineImatorSimplyRemade.assets.img.{resourceName}.png");
        if (stream == null)
        {
            throw new FileNotFoundException($"Embedded resource {resourceName} not found");
        }
            
        ImageResult imageResult = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            
        return imageResult;
    }

    public unsafe void SetWindowIcon(ImageResult imageResult)
    {
        if (imageResult.Comp != ColorComponents.RedGreenBlueAlpha)
        {
            throw new NotSupportedException("Only RedGreenBlueAlpha is supported for window icons");
        }

        fixed (byte* ptr = imageResult.Data)
        {
            Image image = new Image
            {
                Width = imageResult.Width,
                Height = imageResult.Height,
                Pixels = ptr,
            };
            
            Glfw.SetWindowIcon(windowHandle, 1, &image);
        }
    }

    public unsafe void SetWindowTitle(string title)
    {
        Glfw.SetWindowTitle(windowHandle, title);
    }

    public virtual unsafe void Dispose()
    {
        // ShutdownImgui() -> ImGuiImplOpenGL3.Shutdown() deletes this window's
        // GL objects (shader program, VBO/VAO, font texture) via raw GL calls
        // that operate on whatever context is CURRENTLY bound on this thread -
        // not necessarily this window's own context. Every GL context numbers
        // its objects independently starting from 1, so two unrelated windows'
        // "first shader program ever created" can both end up with the same ID
        // (e.g. both land on program 3). If Dispose() is called while a
        // DIFFERENT window's context happens to be current (as main.cs does
        // for the startup splash window, which is disposed while the main
        // window's context is active), the delete calls silently corrupt that
        // other context's same-numbered objects instead of this window's own -
        // e.g. wiping out the main viewport's pick shader program.
        // Switching to this window's own context first (and restoring whatever
        // was current afterward) makes cleanup target the right objects
        // regardless of what the caller forgot to do.
        var previousContext = Glfw.GetCurrentContext();
        Glfw.MakeContextCurrent(windowHandle);
        ShutdownImgui();
        Glfw.DestroyWindow(windowHandle);
        if (previousContext != null)
            Glfw.MakeContextCurrent(previousContext);
    }
}