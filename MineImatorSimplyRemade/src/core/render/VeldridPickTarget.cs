using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>Offscreen color-ID target used for click selection.</summary>
public sealed class VeldridPickTarget : IDisposable
{
    private readonly GraphicsDevice _device;
    private Texture _colorTarget = null!;
    private Texture _depthTarget = null!;
    private Texture _stagingTarget = null!;

    public Framebuffer Framebuffer { get; private set; } = null!;
    public OutputDescription OutputDescription => Framebuffer.OutputDescription;
    public uint Width { get; private set; }
    public uint Height { get; private set; }

    public VeldridPickTarget(GraphicsDevice device, uint width, uint height)
    {
        _device = device;
        Resize(width, height);
    }

    public void Resize(uint width, uint height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height && Framebuffer != null)
            return;

        Width = width;
        Height = height;
        Framebuffer?.Dispose();
        _colorTarget?.Dispose();
        _depthTarget?.Dispose();
        _stagingTarget?.Dispose();

        _colorTarget = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        _depthTarget = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.D24_UNorm_S8_UInt, TextureUsage.DepthStencil));
        _stagingTarget = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
        Framebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(_depthTarget, _colorTarget));
    }

    public int ReadPickId(uint x, uint y, Action<CommandList> draw)
    {
        x = Math.Min(x, Width - 1);
        y = Math.Min(y, Height - 1);

        using CommandList commandList = _device.ResourceFactory.CreateCommandList();
        commandList.Begin();
        commandList.SetFramebuffer(Framebuffer);
        commandList.ClearColorTarget(0, RgbaFloat.Black);
        commandList.ClearDepthStencil(1f);
        draw(commandList);
        commandList.CopyTexture(_colorTarget, _stagingTarget);
        commandList.End();
        _device.SubmitCommands(commandList);
        _device.WaitForIdle();

        MappedResource mapped = _device.Map(_stagingTarget, MapMode.Read);
        try
        {
            unsafe
            {
                byte* pixel = (byte*)mapped.Data + y * mapped.RowPitch + x * 4;
                return pixel[0] | (pixel[1] << 8) | (pixel[2] << 16);
            }
        }
        finally
        {
            _device.Unmap(_stagingTarget);
        }
    }

    public void Dispose()
    {
        Framebuffer?.Dispose();
        _colorTarget?.Dispose();
        _depthTarget?.Dispose();
        _stagingTarget?.Dispose();
    }
}