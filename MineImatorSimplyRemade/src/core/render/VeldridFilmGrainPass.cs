using System.Numerics;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Film grain: adds per-pixel noise to the scene. Renders scene -&gt; an
/// internal scratch texture (can't read and write the same texture in one
/// draw), then copies the scratch texture back over the main color target.
/// </summary>
public sealed class VeldridFilmGrainPass : IDisposable
{
    private readonly GraphicsDevice _device;

    private Texture? _scratchTexture;
    private Framebuffer? _scratchFramebuffer;
    private uint _width, _height;

    private DeviceBuffer? _uniformBuffer;
    private ResourceLayout? _resourceLayout;
    private ResourceSet? _resourceSet;
    private Pipeline? _pipeline;
    private Sampler? _sampler;
    private TextureView? _boundSceneView;

    public float Strength { get; set; } = 0.03f;
    public float Saturation { get; set; } = 0.3f;
    public float GrainSize { get; set; } = 1.5f;
    public float Frame { get; set; }

    public VeldridFilmGrainPass(GraphicsDevice device)
    {
        _device = device;
    }

    public void Resize(uint width, uint height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height && _scratchTexture != null)
            return;

        _width = width;
        _height = height;

        _scratchFramebuffer?.Dispose();
        _scratchTexture?.Dispose();

        _scratchTexture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _scratchFramebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _scratchTexture));

        _pipeline?.Dispose();
        _pipeline = null;
    }

    /// <summary>Applies grain to <paramref name="colorTarget"/> (typically
    /// <c>VeldridBitmapRenderSurface.ColorTarget</c>) in place.</summary>
    public void Render(CommandList commandList, Texture colorTarget, TextureView colorTargetView)
    {
        EnsurePipeline();
        if (_pipeline == null || _uniformBuffer == null || _scratchFramebuffer == null)
            return;

        EnsureResourceSet(colorTargetView);

        var uniforms = new FilmGrainUniforms
        {
            Resolution = new Vector2(_width, _height),
            Frame = Frame,
            Strength = Strength,
            Saturation = Saturation,
            Size = GrainSize,
        };
        commandList.UpdateBuffer(_uniformBuffer, 0, ref uniforms);

        commandList.SetFramebuffer(_scratchFramebuffer);
        VeldridFullScreenPass.Draw(commandList, _pipeline, _resourceSet!);

        commandList.CopyTexture(_scratchTexture, colorTarget);
    }

    private void EnsurePipeline()
    {
        if (_pipeline != null)
            return;

        ResourceFactory factory = _device.ResourceFactory;
        _resourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("FilmGrainUniforms", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uSceneTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uSceneSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _uniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            (uint)((System.Runtime.InteropServices.Marshal.SizeOf<FilmGrainUniforms>() + 15) / 16 * 16), BufferUsage.UniformBuffer));

        _sampler ??= factory.CreateSampler(SamplerDescription.Point);

        _pipeline = VeldridFullScreenPass.CreatePipeline(_device, "film_grain.frag", _resourceLayout,
            _scratchFramebuffer!.OutputDescription, BlendStateDescription.SingleDisabled);

        _resourceSet?.Dispose();
        _resourceSet = null;
    }

    private void EnsureResourceSet(TextureView colorTargetView)
    {
        if (_resourceSet != null && _boundSceneView == colorTargetView)
            return;

        _resourceSet?.Dispose();
        _resourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _uniformBuffer, colorTargetView, _sampler));
        _boundSceneView = colorTargetView;
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _resourceSet?.Dispose();
        _resourceLayout?.Dispose();
        _uniformBuffer?.Dispose();
        _sampler?.Dispose();
        _scratchFramebuffer?.Dispose();
        _scratchTexture?.Dispose();
    }
}
