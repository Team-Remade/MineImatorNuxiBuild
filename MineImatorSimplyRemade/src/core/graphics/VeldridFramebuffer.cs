using System;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

public class VeldridFramebuffer : IDisposable
{
    public uint Width { get; private set; }
    public uint Height { get; private set; }

    public Texture ColorTexture { get; private set; } = null!;
    public TextureView ColorTextureView { get; private set; } = null!;
    public Texture? DepthTexture { get; private set; }
    public TextureView? DepthTextureView { get; private set; }
    public Framebuffer Framebuffer { get; private set; } = null!;

    public bool HasDepth { get; }
    public PixelFormat ColorFormat { get; }
    public PixelFormat DepthFormat { get; }

    public VeldridFramebuffer(
        GraphicsDevice gd,
        uint width,
        uint height,
        bool hasDepth = true,
        PixelFormat colorFormat = PixelFormat.R8_G8_B8_A8_UNorm,
        PixelFormat depthFormat = PixelFormat.D24_UNorm_S8_UInt)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        HasDepth = hasDepth;
        ColorFormat = colorFormat;
        DepthFormat = depthFormat;

        Recreate(gd, Width, Height);
    }

    public void Resize(GraphicsDevice gd, uint width, uint height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (width == Width && height == Height && Framebuffer != null)
            return;

        Dispose();
        Width = width;
        Height = height;
        Recreate(gd, Width, Height);
    }

    private void Recreate(GraphicsDevice gd, uint width, uint height)
    {
        var colorDesc = TextureDescription.Texture2D(
            width,
            height,
            1,
            1,
            ColorFormat,
            TextureUsage.RenderTarget | TextureUsage.Sampled);

        ColorTexture = gd.ResourceFactory.CreateTexture(colorDesc);
        ColorTextureView = gd.ResourceFactory.CreateTextureView(ColorTexture);

        FramebufferAttachmentDescription[] colorAttachments = [
            new FramebufferAttachmentDescription(ColorTexture, 0)
        ];

        FramebufferAttachmentDescription? depthAttachment = null;
        if (HasDepth)
        {
            var depthDesc = TextureDescription.Texture2D(
                width,
                height,
                1,
                1,
                DepthFormat,
                TextureUsage.DepthStencil);

            DepthTexture = gd.ResourceFactory.CreateTexture(depthDesc);
            DepthTextureView = gd.ResourceFactory.CreateTextureView(DepthTexture);
            depthAttachment = new FramebufferAttachmentDescription(DepthTexture, 0);
        }

        var fbDesc = new FramebufferDescription(depthAttachment, colorAttachments);
        Framebuffer = gd.ResourceFactory.CreateFramebuffer(fbDesc);
    }

    public void Dispose()
    {
        Framebuffer?.Dispose();
        DepthTextureView?.Dispose();
        DepthTexture?.Dispose();
        ColorTextureView?.Dispose();
        ColorTexture?.Dispose();
    }
}
