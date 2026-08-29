using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Offscreen single-channel mask (1.0 = covered by a selected object, 0.0
/// elsewhere) that <c>VeldridMesh.RenderSilhouette</c> draws into and
/// <see cref="VeldridEdgeOutlinePass"/> reads from to draw a selection outline.
/// </summary>
public sealed class VeldridSilhouetteMask : IDisposable
{
    private readonly GraphicsDevice _device;

    public Texture Texture { get; private set; } = null!;
    public TextureView TextureView { get; private set; } = null!;
    public Framebuffer Framebuffer { get; private set; } = null!;

    private Texture _depthTarget = null!;
    public uint Width { get; private set; }
    public uint Height { get; private set; }

    public VeldridSilhouetteMask(GraphicsDevice device, uint width, uint height)
    {
        _device = device;
        Resize(width, height);
    }

    public void Resize(uint width, uint height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height && Texture != null)
            return;

        Width = width;
        Height = height;

        Framebuffer?.Dispose();
        TextureView?.Dispose();
        Texture?.Dispose();
        _depthTarget?.Dispose();

        Texture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _depthTarget = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.D24_UNorm_S8_UInt, TextureUsage.DepthStencil));

        TextureView = _device.ResourceFactory.CreateTextureView(Texture);
        Framebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(_depthTarget, Texture));
    }

    /// <summary>Binds this mask's framebuffer and clears it to 0 (nothing selected). Call
    /// before any <c>VeldridMesh.RenderSilhouette</c> calls for this frame.</summary>
    public void Clear(CommandList commandList)
    {
        commandList.SetFramebuffer(Framebuffer);
        commandList.ClearColorTarget(0, new RgbaFloat(0, 0, 0, 1));
        commandList.ClearDepthStencil(1f);
    }

    public void Dispose()
    {
        Framebuffer?.Dispose();
        TextureView?.Dispose();
        Texture?.Dispose();
        _depthTarget?.Dispose();
    }
}
