using System.Numerics;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Bloom/glow: threshold-extract the scene's bright pixels into a scratch
/// texture, blur it (2-pass separable Gaussian, ping-ponging between two
/// scratch textures), then additively composite the result onto the main
/// scene framebuffer. Mirrors the old renderer's 3-shader-call glow.frag
/// (modes 0/1/2) but as a single reusable class.
/// </summary>
public sealed class VeldridGlowPass : IDisposable
{
    private readonly GraphicsDevice _device;

    private Texture? _scratchA, _scratchB;
    private TextureView? _scratchAView, _scratchBView;
    private Framebuffer? _scratchAFramebuffer, _scratchBFramebuffer;
    private uint _width, _height;

    private DeviceBuffer? _uniformBuffer;
    private ResourceLayout? _resourceLayout;
    private Pipeline? _opaquePipeline; // extract/blur (writes to a scratch texture)
    private Pipeline? _additivePipeline; // composite (writes onto the main framebuffer)
    private OutputDescription? _mainOutputDescription;
    private Sampler? _sampler;

    private ResourceSet? _resourceSetFromScene, _resourceSetFromA, _resourceSetFromB;

    public float Strength { get; set; } = 0.6f;
    public float BlurSize { get; set; } = 2f;

    public VeldridGlowPass(GraphicsDevice device)
    {
        _device = device;
    }

    public void Resize(uint width, uint height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height && _scratchA != null)
            return;

        _width = width;
        _height = height;

        _scratchAFramebuffer?.Dispose();
        _scratchBFramebuffer?.Dispose();
        _scratchAView?.Dispose();
        _scratchBView?.Dispose();
        _scratchA?.Dispose();
        _scratchB?.Dispose();

        _scratchA = CreateScratch(width, height);
        _scratchB = CreateScratch(width, height);
        _scratchAView = _device.ResourceFactory.CreateTextureView(_scratchA);
        _scratchBView = _device.ResourceFactory.CreateTextureView(_scratchB);
        _scratchAFramebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _scratchA));
        _scratchBFramebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _scratchB));

        _opaquePipeline?.Dispose();
        _opaquePipeline = null;
        DisposeResourceSets();
    }

    private Texture CreateScratch(uint width, uint height) =>
        _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));

    /// <summary>Renders the full bloom chain and composites additively onto
    /// whichever framebuffer is currently bound (caller's main scene framebuffer -
    /// same convention as <see cref="VeldridAmbientOcclusionPass"/>).</summary>
    public void Render(CommandList commandList, TextureView sceneColorView, OutputDescription mainOutputDescription)
    {
        EnsurePipelines(mainOutputDescription);
        if (_opaquePipeline == null || _additivePipeline == null || _uniformBuffer == null)
            return;

        var texelSize = new Vector2(1f / _width, 1f / _height);

        // 1) Extract bright pixels: scene -> scratchA
        DrawPass(commandList, _scratchAFramebuffer!, _opaquePipeline, EnsureResourceSet(ref _resourceSetFromScene, sceneColorView),
            new GlowUniforms { TexelSize = texelSize, Mode = 0 });

        // 2) Blur horizontal: scratchA -> scratchB
        DrawPass(commandList, _scratchBFramebuffer!, _opaquePipeline, EnsureResourceSet(ref _resourceSetFromA, _scratchAView!),
            new GlowUniforms { TexelSize = texelSize, Mode = 1, Size = BlurSize, Direction = new Vector2(1, 0) });

        // 3) Blur vertical: scratchB -> scratchA
        DrawPass(commandList, _scratchAFramebuffer!, _opaquePipeline, EnsureResourceSet(ref _resourceSetFromB, _scratchBView!),
            new GlowUniforms { TexelSize = texelSize, Mode = 1, Size = BlurSize, Direction = new Vector2(0, 1) });

        // 4) Composite additively: scratchA -> main framebuffer (caller-bound)
        DrawPass(commandList, null, _additivePipeline, EnsureResourceSet(ref _resourceSetFromA, _scratchAView!),
            new GlowUniforms { TexelSize = texelSize, Mode = 2, Strength = Strength });
    }

    private void DrawPass(CommandList commandList, Framebuffer? framebuffer, Pipeline pipeline, ResourceSet resourceSet, GlowUniforms uniforms)
    {
        if (framebuffer != null)
            commandList.SetFramebuffer(framebuffer);

        commandList.UpdateBuffer(_uniformBuffer, 0, ref uniforms);
        VeldridFullScreenPass.Draw(commandList, pipeline, resourceSet);
    }

    private ResourceSet EnsureResourceSet(ref ResourceSet? cached, TextureView sourceView)
    {
        cached ??= _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _uniformBuffer, sourceView, _sampler));
        return cached;
    }

    private void EnsurePipelines(OutputDescription mainOutputDescription)
    {
        ResourceFactory factory = _device.ResourceFactory;
        _resourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("GlowUniforms", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uSceneTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uSceneSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _uniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            (uint)((System.Runtime.InteropServices.Marshal.SizeOf<GlowUniforms>() + 15) / 16 * 16), BufferUsage.UniformBuffer));

        _sampler ??= factory.CreateSampler(SamplerDescription.Linear);

        _opaquePipeline ??= VeldridFullScreenPass.CreatePipeline(_device, "glow.frag", _resourceLayout,
            _scratchAFramebuffer!.OutputDescription, BlendStateDescription.SingleDisabled);

        if (_additivePipeline != null && _mainOutputDescription != null && _mainOutputDescription.Value.Equals(mainOutputDescription))
            return;

        _additivePipeline?.Dispose();
        _additivePipeline = VeldridFullScreenPass.CreatePipeline(_device, "glow.frag", _resourceLayout,
            mainOutputDescription, BlendStateDescription.SingleAdditiveBlend);
        _mainOutputDescription = mainOutputDescription;
    }

    private void DisposeResourceSets()
    {
        _resourceSetFromScene?.Dispose();
        _resourceSetFromA?.Dispose();
        _resourceSetFromB?.Dispose();
        _resourceSetFromScene = _resourceSetFromA = _resourceSetFromB = null;
    }

    public void Dispose()
    {
        _opaquePipeline?.Dispose();
        _additivePipeline?.Dispose();
        _resourceLayout?.Dispose();
        _uniformBuffer?.Dispose();
        _sampler?.Dispose();
        DisposeResourceSets();
        _scratchAFramebuffer?.Dispose();
        _scratchBFramebuffer?.Dispose();
        _scratchAView?.Dispose();
        _scratchBView?.Dispose();
        _scratchA?.Dispose();
        _scratchB?.Dispose();
    }
}
