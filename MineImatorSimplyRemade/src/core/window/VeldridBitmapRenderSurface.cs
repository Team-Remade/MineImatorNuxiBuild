using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MineImatorSimplyRemade.core.render;
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

    /// <summary>
    /// The shared per-frame scene uniform buffer (sun/moon fill lights, ambient,
    /// shadow flags - see <see cref="SceneDataUniforms"/>). One instance per
    /// render surface, independent of size, updated once per frame via
    /// <see cref="UpdateSceneData"/> before any mesh draw calls.
    /// </summary>
    public DeviceBuffer SceneDataBuffer { get; }

    /// <summary>
    /// Shared per-frame point-light array (see <see cref="PointLightUniforms"/>).
    /// One instance per render surface, independent of size, updated once per
    /// frame via <see cref="UpdatePointLights"/> before any mesh draw calls.
    /// </summary>
    public DeviceBuffer PointLightBuffer { get; }

    /// <summary>Formats/sample-count description of <see cref="Framebuffer"/>, stable
    /// across <see cref="Resize"/> calls - pass this to <c>VeldridMesh.Upload</c>.</summary>
    public OutputDescription OutputDescription => Framebuffer.OutputDescription;

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

        SceneDataBuffer = ResourceFactory.CreateBuffer(new BufferDescription(
            AlignTo16((uint)System.Runtime.InteropServices.Marshal.SizeOf<SceneDataUniforms>()),
            BufferUsage.UniformBuffer));
        UpdateSceneData(SceneDataUniforms.Default);

        PointLightBuffer = ResourceFactory.CreateBuffer(new BufferDescription(
            PointLightUniforms.SizeInBytes, BufferUsage.UniformBuffer));
        UpdatePointLights(PointLightUniforms.Empty);

        Resize(Math.Max(1, width), Math.Max(1, height));
    }

    private static uint AlignTo16(uint size) => (size + 15) / 16 * 16;

    /// <summary>Uploads new scene-wide lighting data. Call once per frame before
    /// any mesh draws that reference <see cref="SceneDataBuffer"/>.</summary>
    public void UpdateSceneData(SceneDataUniforms data) => GraphicsDevice.UpdateBuffer(SceneDataBuffer, 0, ref data);

    /// <summary>Uploads the current frame's point-light array. Call once per
    /// frame before any mesh draws that reference <see cref="PointLightBuffer"/>.</summary>
    public void UpdatePointLights(PointLightUniforms lights) => lights.WriteTo(GraphicsDevice, PointLightBuffer);

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
    /// Convenience wrapper for the full per-frame sequence: begin a command list,
    /// bind+clear <see cref="Framebuffer"/>, let <paramref name="recordDrawCalls"/>
    /// record whatever mesh/pass draw calls it needs (typically one or more
    /// <c>VeldridMesh.Render(...)</c> calls), submit, and read the result back to
    /// a bitmap. This is the single entry point callers (Viewport, CameraWindow
    /// preview, thumbnail renderers) should use once they have real geometry to
    /// draw - <see cref="ReadBack"/> alone is still available for callers that
    /// need to manage their own command list lifetime instead.
    /// </summary>
    public WriteableBitmap RenderFrame(RgbaFloat clearColor, Action<CommandList> recordDrawCalls)
    {
        using CommandList commandList = ResourceFactory.CreateCommandList();
        commandList.Begin();
        commandList.SetFramebuffer(Framebuffer);
        commandList.ClearColorTarget(0, clearColor);
        commandList.ClearDepthStencil(1f);

        recordDrawCalls(commandList);

        commandList.End();
        GraphicsDevice.SubmitCommands(commandList);
        GraphicsDevice.WaitForIdle();

        return ReadBack();
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
        SceneDataBuffer?.Dispose();
        PointLightBuffer?.Dispose();
        GraphicsDevice.Dispose();
    }
}
