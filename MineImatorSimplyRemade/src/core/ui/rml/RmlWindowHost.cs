using RmlUiNet;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Owns the RmlUi context and adapters associated with one GLFW window.</summary>
public sealed unsafe class RmlWindowHost : IDisposable
{
    private static readonly object RuntimeLock = new();
    private static int _hostCount;
    private static RmlSystem? _system;
    private static RmlOpenGlRenderer? _defaultRenderer;
    private static bool _resolverInstalled;
    private static nint _nativeLibrary;

    private readonly string _contextName;
    private readonly RmlGlfwInput _input;
    private bool _disposed;

    public Context Context { get; }
    public RmlOpenGlRenderer Renderer { get; }

    public RmlWindowHost(Glfw glfw, WindowHandle* window, GL gl, int width, int height, string name)
    {
        EnsureNativeResolver();
        _contextName = $"{name}-{Guid.NewGuid():N}";
        Renderer = new RmlOpenGlRenderer(gl);

        lock (RuntimeLock)
        {
            if (_hostCount == 0)
            {
                _system = new RmlSystem();
                _defaultRenderer = Renderer;
                Rml.SetSystemInterface(_system);
                Rml.SetRenderInterface(Renderer);
                if (!Rml.Initialise()) throw new InvalidOperationException("RmlUi failed to initialise.");

                string fontPath = Path.Combine(AppContext.BaseDirectory, "assets", "NotoSans.ttf");
                if (!Rml.LoadFontFace(fontPath, fallbackFace: true))
                    throw new FileNotFoundException("RmlUi could not load its bundled font.", fontPath);
            }
            _hostCount++;
        }

        Context = Rml.CreateContext(_contextName, new Vector2i(width, height), Renderer)
                  ?? throw new InvalidOperationException("RmlUi failed to create a window context.");
        _input = new RmlGlfwInput(glfw, window, Context);
    }

    private static void EnsureNativeResolver()
    {
        lock (RuntimeLock)
        {
            if (_resolverInstalled) return;
            NativeLibrary.SetDllImportResolver(typeof(Rml).Assembly, ResolveNativeLibrary);
            _resolverInstalled = true;
        }
    }

    private static nint ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Equals("RmlUiNative", StringComparison.OrdinalIgnoreCase)) return 0;
        if (_nativeLibrary != 0) return _nativeLibrary;

        string fileName = OperatingSystem.IsWindows() ? "RmlUiNative.dll" :
            OperatingSystem.IsMacOS() ? "libRmlUiNative.dylib" : "libRmlUiNative.so";
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        _nativeLibrary = NativeLibrary.Load(path);
        return _nativeLibrary;
    }

    public ElementDocument LoadDocument(string rml)
    {
        ElementDocument document = Context.LoadDocumentFromMemory(rml, $"memory://{_contextName}.rml")
                                   ?? throw new InvalidOperationException("RmlUi failed to parse the document.");
        document.Show();
        return document;
    }

    public void Resize(int width, int height) => Context.SetDimensions(Math.Max(1, width), Math.Max(1, height));

    public void Render(int width, int height)
    {
        Resize(width, height);
        Context.Update();
        Renderer.BeginFrame(width, height);
        Context.Render();
        Renderer.EndFrame();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _input.Dispose();
        Rml.RemoveContext(_contextName);

        lock (RuntimeLock)
        {
            _hostCount--;
            if (_hostCount == 0)
            {
                Rml.Shutdown();
                _system?.Dispose();
                _system = null;
                _defaultRenderer = null;
            }
        }

        Renderer.Dispose();
        _disposed = true;
    }
}
