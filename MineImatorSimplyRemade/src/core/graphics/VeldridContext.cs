using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.GLFW;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

public static class VeldridContext
{
    public static GraphicsDevice? GraphicsDevice { get; private set; }
    public static ResourceFactory? ResourceFactory => GraphicsDevice?.ResourceFactory;
    public static CommandList? MainCommandList { get; private set; }
    public static Swapchain? MainSwapchain => _hasMainSwapchain ? GraphicsDevice?.MainSwapchain : null;
    private static bool _hasMainSwapchain;

    public static VeldridSceneRenderer? SceneRenderer { get; private set; }

    public static VeldridPipelineCache? PipelineCache { get; private set; }
    public static VeldridUniformBufferPool? UniformBufferPool { get; private set; }
    public static VeldridResourceSetPool? ResourceSetPool { get; private set; }
    public static VeldridBufferUploadManager? UploadManager { get; private set; }
    public static VeldridCommandRecorder? CommandRecorder { get; private set; }

    public static bool IsInitialized => GraphicsDevice != null;

    public static void CreateSceneRenderer(Silk.NET.OpenGL.GL gl, GraphicsBackend backend)
    {
        if (IsInitialized)
            throw new InvalidOperationException("A Veldrid device is already initialized.");

        // The editor still owns an OpenGL window. Do not attach a competing
        // swapchain to it: render the scene offscreen and bridge its output.
        var options = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: null,
            syncToVerticalBlank: false,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferDepthRangeZeroToOne: true,
            preferStandardClipSpaceYDirection: true);

        try
        {
            GraphicsDevice = backend switch
            {
                GraphicsBackend.Vulkan => Veldrid.GraphicsDevice.CreateVulkan(options),
                GraphicsBackend.Direct3D11 => Veldrid.GraphicsDevice.CreateD3D11(options),
                _ => throw new NotSupportedException($"Unsupported scene backend: {backend}")
            };
            SceneRenderer = new VeldridSceneRenderer(GraphicsDevice, gl);
            Console.WriteLine($"[Veldrid] Experimental scene renderer: Backend={GraphicsDevice.BackendType}, DeviceName={GraphicsDevice.DeviceName}. UI, previews and exports remain OpenGL.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static unsafe GraphicsDevice CreateGraphicsDevice(
        Glfw glfw,
        WindowHandle* windowHandle,
        int width,
        int height,
        GraphicsBackend preferredBackend = GraphicsBackend.Vulkan)
    {
        var swapchainSource = CreateSwapchainSource(glfw, windowHandle);
        var swapchainDesc = new SwapchainDescription(
            swapchainSource,
            (uint)Math.Max(1, width),
            (uint)Math.Max(1, height),
            PixelFormat.R32_Float,
            false,
            false);

        var options = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: PixelFormat.R32_Float,
            syncToVerticalBlank: false,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferDepthRangeZeroToOne: true);

        try
        {
            GraphicsDevice = preferredBackend switch
            {
                GraphicsBackend.Direct3D11 when GraphicsDevice.IsBackendSupported(GraphicsBackend.Direct3D11) =>
                    GraphicsDevice.CreateD3D11(options, swapchainDesc),
                GraphicsBackend.Metal when GraphicsDevice.IsBackendSupported(GraphicsBackend.Metal) =>
                    GraphicsDevice.CreateMetal(options, swapchainDesc),
                GraphicsBackend.Vulkan when GraphicsDevice.IsBackendSupported(GraphicsBackend.Vulkan) =>
                    GraphicsDevice.CreateVulkan(options, swapchainDesc),
                _ => GraphicsDevice.IsBackendSupported(GraphicsBackend.Direct3D11)
                    ? GraphicsDevice.CreateD3D11(options, swapchainDesc)
                    : (GraphicsDevice.IsBackendSupported(GraphicsBackend.Vulkan)
                        ? GraphicsDevice.CreateVulkan(options, swapchainDesc)
                        : GraphicsDevice.CreateD3D11(options, swapchainDesc))
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Veldrid] Failed to initialize backend {preferredBackend}: {ex.Message}. Falling back to available backend...");
            if (GraphicsDevice.IsBackendSupported(GraphicsBackend.Direct3D11))
                GraphicsDevice = GraphicsDevice.CreateD3D11(options, swapchainDesc);
            else if (GraphicsDevice.IsBackendSupported(GraphicsBackend.Vulkan))
                GraphicsDevice = GraphicsDevice.CreateVulkan(options, swapchainDesc);
            else
                throw;
        }

        MainCommandList = GraphicsDevice.ResourceFactory.CreateCommandList();
        _hasMainSwapchain = true;

        // Initialize shared caching and resource management subsystems
        PipelineCache = new VeldridPipelineCache(GraphicsDevice);
        UniformBufferPool = new VeldridUniformBufferPool(GraphicsDevice);
        ResourceSetPool = new VeldridResourceSetPool(GraphicsDevice);
        UploadManager = new VeldridBufferUploadManager(GraphicsDevice);
        CommandRecorder = new VeldridCommandRecorder(GraphicsDevice);

        Console.WriteLine($"[Veldrid] Graphics Device Initialized: Backend={GraphicsDevice.BackendType}, DeviceName={GraphicsDevice.DeviceName}");

        return GraphicsDevice;
    }

    /// <summary>
    /// Resets per-frame allocator pools at the start of a new frame.
    /// </summary>
    public static void BeginFrame()
    {
        UniformBufferPool?.Reset();
    }

    /// <summary>
    /// Submits pending command lists and executes frame submission.
    /// </summary>
    public static void EndFrame()
    {
        CommandRecorder?.SubmitPending();
    }

    public static unsafe SwapchainSource CreateSwapchainSource(Glfw glfw, WindowHandle* windowHandle)
    {
        var nativeWindow = new GlfwNativeWindow(glfw, windowHandle);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var hwnd = nativeWindow.Win32?.Hwnd ?? IntPtr.Zero;
            using var process = Process.GetCurrentProcess();
            var hinstance = process.MainModule?.BaseAddress
                ?? throw new InvalidOperationException("Unable to obtain the Win32 module instance.");
            return SwapchainSource.CreateWin32((nint)hwnd, hinstance);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (nativeWindow.X11.HasValue)
            {
                return SwapchainSource.CreateXlib(nativeWindow.X11.Value.Display, (nint)nativeWindow.X11.Value.Window);
            }
            if (nativeWindow.Wayland.HasValue)
            {
                return SwapchainSource.CreateWayland(nativeWindow.Wayland.Value.Display, nativeWindow.Wayland.Value.Surface);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var nsWindow = nativeWindow.Cocoa ?? IntPtr.Zero;
            return SwapchainSource.CreateNSWindow(nsWindow);
        }

        throw new PlatformNotSupportedException("Unsupported operating system or display server for Veldrid swapchain.");
    }

    public static void Dispose()
    {
        GraphicsDevice?.WaitForIdle();
        SceneRenderer?.Dispose();
        SceneRenderer = null;
        CommandRecorder?.Dispose();
        CommandRecorder = null;
        UploadManager?.Dispose();
        UploadManager = null;
        ResourceSetPool?.Dispose();
        ResourceSetPool = null;
        UniformBufferPool?.Dispose();
        UniformBufferPool = null;
        PipelineCache?.Dispose();
        PipelineCache = null;

        MainCommandList?.Dispose();
        MainCommandList = null;
        GraphicsDevice?.Dispose();
        GraphicsDevice = null;
        _hasMainSwapchain = false;
    }
}
