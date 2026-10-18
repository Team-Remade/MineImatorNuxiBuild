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

    public TextureSampleCount SampleCount { get; }
    public bool HasDepth { get; }
    public PixelFormat ColorFormat { get; }
    public PixelFormat DepthFormat { get; }

    // Resolve target when MSAA is enabled (SampleCount > Count1)
    public Texture? ResolveColorTexture { get; private set; }
    public TextureView? ResolveColorTextureView { get; private set; }
    public Framebuffer? ResolveFramebuffer { get; private set; }

    public bool IsMultisampled => SampleCount != TextureSampleCount.Count1;

    /// <summary>
    /// Gets the sampleable texture view (resolved view if multisampled, otherwise direct color view).
    /// </summary>
    public TextureView SampleableTextureView => IsMultisampled && ResolveColorTextureView != null
        ? ResolveColorTextureView
        : ColorTextureView;

    public VeldridFramebuffer(
        GraphicsDevice gd,
        uint width,
        uint height,
        bool hasDepth = true,
        TextureSampleCount sampleCount = TextureSampleCount.Count1,
        PixelFormat colorFormat = PixelFormat.R8_G8_B8_A8_UNorm,
        PixelFormat depthFormat = PixelFormat.D24_UNorm_S8_UInt)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        HasDepth = hasDepth;
        SampleCount = sampleCount;
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
        var colorUsage = TextureUsage.RenderTarget;
        if (!IsMultisampled)
        {
            colorUsage |= TextureUsage.Sampled;
        }

        var colorDesc = new TextureDescription(
            width,
            height,
            1,
            1,
            1,
            ColorFormat,
            colorUsage,
            TextureType.Texture2D,
            SampleCount);

        ColorTexture = gd.ResourceFactory.CreateTexture(colorDesc);
        ColorTextureView = gd.ResourceFactory.CreateTextureView(ColorTexture);

        FramebufferAttachmentDescription[] colorAttachments = [
            new FramebufferAttachmentDescription(ColorTexture, 0)
        ];

        FramebufferAttachmentDescription? depthAttachment = null;
        if (HasDepth)
        {
            var depthDesc = new TextureDescription(
                width,
                height,
                1,
                1,
                1,
                DepthFormat,
                TextureUsage.DepthStencil,
                TextureType.Texture2D,
                SampleCount);

            DepthTexture = gd.ResourceFactory.CreateTexture(depthDesc);
            DepthTextureView = gd.ResourceFactory.CreateTextureView(DepthTexture);
            depthAttachment = new FramebufferAttachmentDescription(DepthTexture, 0);
        }

        var fbDesc = new FramebufferDescription(depthAttachment, colorAttachments);
        Framebuffer = gd.ResourceFactory.CreateFramebuffer(fbDesc);

        // If MSAA is active, create single-sampled resolve destination
        if (IsMultisampled)
        {
            var resolveDesc = TextureDescription.Texture2D(
                width,
                height,
                1,
                1,
                ColorFormat,
                TextureUsage.RenderTarget | TextureUsage.Sampled);

            ResolveColorTexture = gd.ResourceFactory.CreateTexture(resolveDesc);
            ResolveColorTextureView = gd.ResourceFactory.CreateTextureView(ResolveColorTexture);

            var resolveFbDesc = new FramebufferDescription(
                null,
                [new FramebufferAttachmentDescription(ResolveColorTexture, 0)]);
            ResolveFramebuffer = gd.ResourceFactory.CreateFramebuffer(resolveFbDesc);
        }
    }

    /// <summary>
    /// Resolves multisampled color buffer into the non-multisampled resolve target texture.
    /// </summary>
    public void Resolve(CommandList cl)
    {
        if (IsMultisampled && ResolveColorTexture != null)
        {
            cl.ResolveTexture(ColorTexture, ResolveColorTexture);
        }
    }

    public void Dispose()
    {
        ResolveFramebuffer?.Dispose();
        ResolveColorTextureView?.Dispose();
        ResolveColorTexture?.Dispose();
        ResolveFramebuffer = null;
        ResolveColorTextureView = null;
        ResolveColorTexture = null;

        Framebuffer?.Dispose();
        DepthTextureView?.Dispose();
        DepthTexture?.Dispose();
        ColorTextureView?.Dispose();
        ColorTexture?.Dispose();
    }
}
