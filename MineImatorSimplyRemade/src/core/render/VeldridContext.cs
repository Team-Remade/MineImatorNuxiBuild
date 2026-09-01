using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Owns the single shared, headless Veldrid <see cref="GraphicsDevice"/> used by
/// the whole application - textures, meshes, and atlases created against one
/// <see cref="GraphicsDevice"/> are not usable with another (each D3D11/Vulkan
/// device is an independent GPU context), so anything that needs to be shared
/// across multiple viewports/windows (block/item/CTM texture atlases, loaded
/// model meshes, etc.) must be created against this one device rather than each
/// owning a private one.
///
/// <see cref="VeldridBitmapRenderSurface"/> uses this by default; pass an
/// explicit device to its constructor only for tests/tools that intentionally
/// want an isolated device (e.g. <see cref="VeldridSmokeTest"/>).
/// </summary>
public static class VeldridContext
{
    private static GraphicsDevice? _device;

    public static GraphicsDevice Device => _device ??= CreateHeadlessDevice();

    public static ResourceFactory ResourceFactory => Device.ResourceFactory;

    /// <summary>
    /// Every <see cref="MineImatorSimplyRemade.core.window.VeldridBitmapRenderSurface"/>
    /// in the app uses this exact color/depth format combination (see its
    /// <c>Resize</c>), so decorative/shared meshes (<c>CubeMesh</c>, <c>PlaneMesh</c>,
    /// light indicator meshes, etc.) that are built once - independent of any
    /// specific viewport - and reused across every viewport's render calls can
    /// build their pipeline against this fixed description instead of needing a
    /// live <see cref="Framebuffer"/> reference at construction time.
    /// </summary>
    public static readonly OutputDescription StandardOutputDescription = new(
        new OutputAttachmentDescription(PixelFormat.D24_UNorm_S8_UInt),
        new OutputAttachmentDescription(PixelFormat.R8_G8_B8_A8_UNorm));

    private static GraphicsDevice CreateHeadlessDevice()
    {
        var options = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: null,
            syncToVerticalBlank: false,
            resourceBindingModel: ResourceBindingModel.Improved);

        // See VeldridBitmapRenderSurface's constructor doc for why D3D11 (not
        // OpenGL) is used for every headless/offscreen Veldrid device in this
        // app: it needs no native window/context to create, unlike GL.
        return GraphicsDevice.CreateD3D11(options);
    }
}
