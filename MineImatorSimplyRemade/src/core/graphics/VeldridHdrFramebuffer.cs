using System;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// Defines HDR precision format options for offscreen render targets.
/// </summary>
public enum HdrPrecision
{
    /// <summary>
    /// Standard 8-bit dynamic range (UNorm).
    /// </summary>
    Sdr8Bit = 0,

    /// <summary>
    /// Half-precision 16-bit floating point per channel (RGBA16F). Recommended for real-time HDR.
    /// </summary>
    Hdr16BitFloat = 1,

    /// <summary>
    /// Full-precision 32-bit floating point per channel (RGBA32F). High precision compute / scientific.
    /// </summary>
    Hdr32BitFloat = 2
}

/// <summary>
/// High Dynamic Range (HDR) Framebuffer container managing high precision floating-point
/// color attachments, multi-sample anti-aliasing resolve targets, and HDR-to-LDR tone mapped display views.
/// </summary>
public class VeldridHdrFramebuffer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private VeldridFramebuffer _sceneFramebuffer;
    private VeldridFramebuffer? _ldrResolvedFramebuffer;

    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public HdrPrecision Precision { get; private set; }
    public TextureSampleCount SampleCount { get; private set; }
    public bool HasDepth { get; }

    public PixelFormat HdrColorFormat => Precision switch
    {
        HdrPrecision.Hdr16BitFloat => PixelFormat.R16_G16_B16_A16_Float,
        HdrPrecision.Hdr32BitFloat => PixelFormat.R32_G32_B32_A32_Float,
        _ => PixelFormat.R8_G8_B8_A8_UNorm
    };

    public PixelFormat LdrColorFormat => PixelFormat.R8_G8_B8_A8_UNorm;

    /// <summary>
    /// The primary HDR scene framebuffer (where 3D geometry and HDR lighting are drawn).
    /// </summary>
    public Framebuffer SceneFramebuffer => _sceneFramebuffer.Framebuffer;

    /// <summary>
    /// Raw HDR color texture view for post-processing / tone mapping inputs.
    /// </summary>
    public TextureView HdrColorTextureView => _sceneFramebuffer.SampleableTextureView;

    /// <summary>
    /// Depth texture view (if depth buffer is enabled).
    /// </summary>
    public TextureView? DepthTextureView => _sceneFramebuffer.DepthTextureView;

    /// <summary>
    /// Optional LDR 8-bit framebuffer target (output of post-processing / tone mapping for ImGui / display).
    /// </summary>
    public VeldridFramebuffer? LdrFramebuffer => _ldrResolvedFramebuffer;

    /// <summary>
    /// Display texture view to bind to UI / ImGui.
    /// </summary>
    public TextureView DisplayTextureView => _ldrResolvedFramebuffer != null
        ? _ldrResolvedFramebuffer.SampleableTextureView
        : _sceneFramebuffer.SampleableTextureView;

    public VeldridHdrFramebuffer(
        GraphicsDevice gd,
        uint width,
        uint height,
        HdrPrecision precision = HdrPrecision.Hdr16BitFloat,
        TextureSampleCount sampleCount = TextureSampleCount.Count1,
        bool hasDepth = true,
        bool createLdrTarget = true)
    {
        _gd = gd ?? throw new ArgumentNullException(nameof(gd));
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Precision = precision;
        SampleCount = sampleCount;
        HasDepth = hasDepth;

        _sceneFramebuffer = new VeldridFramebuffer(
            _gd,
            Width,
            Height,
            hasDepth: HasDepth,
            sampleCount: SampleCount,
            colorFormat: HdrColorFormat
        );

        if (createLdrTarget)
        {
            _ldrResolvedFramebuffer = new VeldridFramebuffer(
                _gd,
                Width,
                Height,
                hasDepth: false,
                sampleCount: TextureSampleCount.Count1,
                colorFormat: LdrColorFormat
            );
        }
    }

    /// <summary>
    /// Resizes the HDR framebuffer and auxiliary render targets.
    /// </summary>
    public void Resize(uint width, uint height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (width == Width && height == Height)
            return;

        Width = width;
        Height = height;

        _sceneFramebuffer.Resize(_gd, Width, Height);
        _ldrResolvedFramebuffer?.Resize(_gd, Width, Height);
    }

    /// <summary>
    /// Updates the HDR precision format and recreates the scene buffers.
    /// </summary>
    public void SetPrecision(HdrPrecision precision)
    {
        if (Precision == precision)
            return;

        Precision = precision;
        _sceneFramebuffer.Dispose();
        _sceneFramebuffer = new VeldridFramebuffer(
            _gd,
            Width,
            Height,
            hasDepth: HasDepth,
            sampleCount: SampleCount,
            colorFormat: HdrColorFormat
        );
    }

    /// <summary>
    /// Resolves multisampled HDR render target to single-sampled HDR texture if MSAA is enabled.
    /// </summary>
    public void Resolve(CommandList cl)
    {
        _sceneFramebuffer.Resolve(cl);
    }

    public void Dispose()
    {
        _sceneFramebuffer.Dispose();
        _ldrResolvedFramebuffer?.Dispose();
    }
}
