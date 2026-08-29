using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Veldrid;

namespace MineImatorSimplyRemade.core.window;

/// <summary>
/// Render bridge between Veldrid and Avalonia, using the "CPU blit" strategy:
/// Veldrid renders a scene into an offscreen color+depth framebuffer on a headless
/// (swapchain-less) GraphicsDevice, the color target is copied to a CPU-visible
/// staging texture, and that staging texture's pixels are copied into an Avalonia
/// <see cref="WriteableBitmap"/> that a normal &lt;Image&gt; control can display.
///
/// This intentionally avoids native GPU context/swapchain sharing with Avalonia's
/// own renderer (there is no first-party Avalonia+Veldrid interop), trading a
/// per-frame GPU-&gt;CPU-&gt;GPU round trip for simplicity and full cross-platform
/// support. Every viewport-like panel (Viewport, CameraWindow preview, thumbnail
/// renderers, etc.) should own one instance of this class sized to its content
/// region and call <see cref="Render"/> once per frame.
/// </summary>
public sealed class VeldridBitmapRenderSurface : IDisposable
{
    public GraphicsDevice GraphicsDevice { get; }
    public ResourceFactory ResourceFactory => GraphicsDevice.ResourceFactory;

    public Texture ColorTarget { get; private set; } = null!;
    public Texture DepthTarget { get; private set; } = null!;
    public Framebuffer Framebuffer { get; private set; } = null!;

    private Texture _stagingTexture = null!;
    private WriteableBitmap? _bitmap;

    public uint Width { get; private set; }
    public uint Height { get; private set; }

    /// <param name="backend">
    /// Defaults to Direct3D11 on Windows: unlike Veldrid's OpenGL backend, D3D11
    /// (and Vulkan) can create a fully headless <see cref="GraphicsDevice"/> with
    /// no native window/context required at all, which is exactly what an
    /// offscreen-only render target needs.
    /// </param>
    public VeldridBitmapRenderSurface(uint width, uint height, GraphicsBackend backend = GraphicsBackend.Direct3D11)
    {
        var options = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: null,
            syncToVerticalBlank: false,
            resourceBindingModel: ResourceBindingModel.Improved);

        GraphicsDevice = backend switch
        {
            GraphicsBackend.Direct3D11 => GraphicsDevice.CreateD3D11(options),
            GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(options),
            _ => throw new NotSupportedException(
                $"{backend} requires a native window/context and cannot be created headless; use Direct3D11 or Vulkan for offscreen rendering.")
        };

        Resize(Math.Max(1, width), Math.Max(1, height));
    }

    /// <summary>Recreates the color/depth targets and framebuffer at a new size.
    /// Cheap to call every frame with an unchanged size (it's a no-op then).</summary>
    public void Resize(uint width, uint height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height && Framebuffer != null)
            return;

        Width = width;
        Height = height;

        ColorTarget?.Dispose();
        DepthTarget?.Dispose();
        Framebuffer?.Dispose();
        _stagingTexture?.Dispose();

        ColorTarget = ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.RenderTarget | TextureUsage.Sampled));

        DepthTarget = ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1,
            PixelFormat.D24_UNorm_S8_UInt,
            TextureUsage.DepthStencil));

        Framebuffer = ResourceFactory.CreateFramebuffer(new FramebufferDescription(DepthTarget, ColorTarget));

        _stagingTexture = ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Staging));

        _bitmap?.Dispose();
        _bitmap = new WriteableBitmap(
            new PixelSize((int)width, (int)height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);
    }

    /// <summary>
    /// Call after the caller has recorded and submitted draw calls against
    /// <see cref="Framebuffer"/> for this frame. Copies the color target back
    /// to the CPU and returns an up-to-date bitmap ready to assign to an
    /// Avalonia &lt;Image Source="..."/&gt;.
    /// </summary>
    public WriteableBitmap ReadBack()
    {
        using CommandList commandList = ResourceFactory.CreateCommandList();
        commandList.Begin();
        commandList.CopyTexture(ColorTarget, _stagingTexture);
        commandList.End();
        GraphicsDevice.SubmitCommands(commandList);
        GraphicsDevice.WaitForIdle();

        MappedResource mapped = GraphicsDevice.Map(_stagingTexture, MapMode.Read);
        try
        {
            using ILockedFramebuffer fb = _bitmap!.Lock();

            uint rowBytes = Width * 4;
            if (mapped.RowPitch == rowBytes && fb.RowBytes == rowBytes)
            {
                unsafe
                {
                    Buffer.MemoryCopy((void*)mapped.Data, (void*)fb.Address, rowBytes * Height, rowBytes * Height);
                }
            }
            else
            {
                // Row pitches differ (GPU-side row alignment padding) - copy row by row.
                unsafe
                {
                    byte* src = (byte*)mapped.Data;
                    byte* dst = (byte*)fb.Address;
                    for (int y = 0; y < Height; y++)
                    {
                        Buffer.MemoryCopy(src + y * mapped.RowPitch, dst + y * fb.RowBytes, rowBytes, rowBytes);
                    }
                }
            }
        }
        finally
        {
            GraphicsDevice.Unmap(_stagingTexture);
        }

        return _bitmap!;
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _stagingTexture?.Dispose();
        Framebuffer?.Dispose();
        DepthTarget?.Dispose();
        ColorTarget?.Dispose();
        GraphicsDevice.Dispose();
    }
}
