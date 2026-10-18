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
            (uint)width,
            (uint)height,
            PixelFormat.R32_Float,
            false,
            false);

        var options = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: PixelFormat.R32_Float,
            syncToVerticalBlank: true,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferDepthRangeZeroToOne: true);

        GraphicsDevice = preferredBackend switch
        {
            GraphicsBackend.Direct3D11 when GraphicsDevice.IsBackendSupported(GraphicsBackend.Direct3D11) =>
                GraphicsDevice.CreateD3D11(options, swapchainDesc),
            GraphicsBackend.Metal when GraphicsDevice.IsBackendSupported(GraphicsBackend.Metal) =>
                GraphicsDevice.CreateMetal(options, swapchainDesc),
            GraphicsBackend.Vulkan when GraphicsDevice.IsBackendSupported(GraphicsBackend.Vulkan) =>
                GraphicsDevice.CreateVulkan(options, swapchainDesc),
            _ => GraphicsDevice.IsBackendSupported(GraphicsBackend.Vulkan)
                ? GraphicsDevice.CreateVulkan(options, swapchainDesc)
                : GraphicsDevice.CreateD3D11(options, swapchainDesc)
        };

        MainCommandList = GraphicsDevice.ResourceFactory.CreateCommandList();
        return GraphicsDevice;
    }

    public static unsafe SwapchainSource CreateSwapchainSource(Glfw glfw, WindowHandle* windowHandle)
    {
        var nativeWindow = new GlfwNativeWindow(glfw, windowHandle);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var hwnd = nativeWindow.Win32?.Hwnd ?? IntPtr.Zero;
            var hinstance = Process.GetCurrentProcess().Handle;
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
        MainCommandList?.Dispose();
        MainCommandList = null;
        GraphicsDevice?.Dispose();
        GraphicsDevice = null;
    }
}
