using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Screen-space indirect (bounce) lighting: samples the already-shaded scene
/// color at nearby screen positions behind the current surface (see
/// <c>indirect_lighting.frag</c>'s migration note on why the read/write hazard
/// forces a scratch texture), blurs the noisy result, and additively
/// composites it onto the main scene.
///
/// Usage per frame:
///   1. <see cref="RenderRaw"/> - writes into an internal scratch texture, self-contained.
///   2. <see cref="CompositeDenoised"/> - caller must have already bound the
///      main scene framebuffer on the command list (same convention as
///      <see cref="VeldridAmbientOcclusionPass.Render"/>).
/// </summary>
public sealed class VeldridIndirectLightingPass : IDisposable
{
    private readonly GraphicsDevice _device;

    private Texture? _scratchTexture;
    private TextureView? _scratchView;
    private Framebuffer? _scratchFramebuffer;
    private uint _width, _height;

    private DeviceBuffer? _rawUniformBuffer;
    private ResourceLayout? _rawResourceLayout;
    private ResourceSet? _rawResourceSet;
    private Pipeline? _rawPipeline;
    private Sampler? _pointSampler;
    private TextureView? _boundSceneView;
    private TextureView? _boundDepthViewRaw;

    private DeviceBuffer? _denoiseUniformBuffer;
    private ResourceLayout? _denoiseResourceLayout;
    private ResourceSet? _denoiseResourceSet;
    private Pipeline? _denoisePipeline;
    private OutputDescription? _denoiseOutputDescription;
    private TextureView? _boundDepthViewDenoise;

    public float Precision { get; set; } = 0.5f;
    public float RayStep { get; set; } = 8f;
    public int SampleCount { get; set; } = 16;
    public float DenoiseStrength { get; set; } = 40f;

    public VeldridIndirectLightingPass(GraphicsDevice device)
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
        _scratchView?.Dispose();
        _scratchTexture?.Dispose();

        _scratchTexture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _scratchView = _device.ResourceFactory.CreateTextureView(_scratchTexture);
        _scratchFramebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _scratchTexture));

        // Scratch framebuffer's format changed - both pipelines (raw writes to
        // it directly, denoise's Outputs must still just match the *main*
        // scene framebuffer it composites into, so only the raw pipeline needs
        // invalidating here).
        _rawPipeline?.Dispose();
        _rawPipeline = null;
    }

    /// <summary>Step 1: renders the raw (noisy) indirect bounce into the internal
    /// scratch texture. Self-contained - binds its own framebuffer.</summary>
    public void RenderRaw(CommandList commandList, TextureView sceneColorView, TextureView depthView, float nearPlane, float farPlane)
    {
        if (_scratchFramebuffer == null)
            return;

        EnsureRawPipeline();
        if (_rawPipeline == null || _rawUniformBuffer == null)
            return;

        EnsureRawResourceSet(sceneColorView, depthView);

        var uniforms = IndirectLightingUniforms.Default;
        uniforms.TexelSize = new System.Numerics.Vector2(1f / _width, 1f / _height);
        uniforms.Near = nearPlane;
        uniforms.Far = farPlane;
        uniforms.Precision = Precision;
        uniforms.RayStep = RayStep;
        uniforms.SampleCount = SampleCount;
        commandList.UpdateBuffer(_rawUniformBuffer, 0, ref uniforms);

        commandList.SetFramebuffer(_scratchFramebuffer);
        VeldridFullScreenPass.Draw(commandList, _rawPipeline, _rawResourceSet!);
    }

    /// <summary>Step 2: blurs the scratch texture and additively composites it
    /// onto whatever framebuffer is currently bound (the caller's main scene
    /// framebuffer - matches <see cref="VeldridAmbientOcclusionPass.Render"/>'s convention).</summary>
    public void CompositeDenoised(CommandList commandList, TextureView depthView, float nearPlane, float farPlane, OutputDescription targetOutputDescription)
    {
        EnsureDenoisePipeline(targetOutputDescription);
        if (_denoisePipeline == null || _denoiseUniformBuffer == null || _scratchView == null)
            return;

        EnsureDenoiseResourceSet(depthView);

        var uniforms = IndirectDenoiseUniforms.Default;
        uniforms.TexelSize = new System.Numerics.Vector2(1f / _width, 1f / _height);
        uniforms.DenoiseStrength = DenoiseStrength;
        uniforms.Near = nearPlane;
        uniforms.Far = farPlane;
        commandList.UpdateBuffer(_denoiseUniformBuffer, 0, ref uniforms);

        VeldridFullScreenPass.Draw(commandList, _denoisePipeline, _denoiseResourceSet!);
    }

    private void EnsureRawPipeline()
    {
        if (_rawPipeline != null)
            return;

        ResourceFactory factory = _device.ResourceFactory;
        _rawResourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("IndirectUniforms", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uSceneTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uSceneSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uDepthTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uDepthSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _rawUniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            (uint)((System.Runtime.InteropServices.Marshal.SizeOf<IndirectLightingUniforms>() + 15) / 16 * 16),
            BufferUsage.UniformBuffer));

        _pointSampler ??= factory.CreateSampler(SamplerDescription.Point);

        _rawPipeline = VeldridFullScreenPass.CreatePipeline(_device, "indirect_lighting.frag", _rawResourceLayout,
            _scratchFramebuffer!.OutputDescription, BlendStateDescription.SingleDisabled);

        _rawResourceSet?.Dispose();
        _rawResourceSet = null;
    }

    private void EnsureRawResourceSet(TextureView sceneColorView, TextureView depthView)
    {
        if (_rawResourceSet != null && _boundSceneView == sceneColorView && _boundDepthViewRaw == depthView)
            return;

        _rawResourceSet?.Dispose();
        _rawResourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _rawResourceLayout, _rawUniformBuffer, sceneColorView, _pointSampler, depthView, _pointSampler));
        _boundSceneView = sceneColorView;
        _boundDepthViewRaw = depthView;
    }

    private void EnsureDenoisePipeline(OutputDescription outputDescription)
    {
        if (_denoisePipeline != null && _denoiseOutputDescription != null && _denoiseOutputDescription.Value.Equals(outputDescription))
            return;

        ResourceFactory factory = _device.ResourceFactory;
        _denoiseResourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("DenoiseUniforms", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uIndirectTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uIndirectSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uDepthTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uDepthSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _denoiseUniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            (uint)((System.Runtime.InteropServices.Marshal.SizeOf<IndirectDenoiseUniforms>() + 15) / 16 * 16),
            BufferUsage.UniformBuffer));

        _pointSampler ??= factory.CreateSampler(SamplerDescription.Point);

        _denoisePipeline?.Dispose();
        _denoisePipeline = VeldridFullScreenPass.CreatePipeline(_device, "indirect_denoise.frag", _denoiseResourceLayout,
            outputDescription, BlendStateDescription.SingleAdditiveBlend);
        _denoiseOutputDescription = outputDescription;

        _denoiseResourceSet?.Dispose();
        _denoiseResourceSet = null;
    }

    private void EnsureDenoiseResourceSet(TextureView depthView)
    {
        if (_denoiseResourceSet != null && _boundDepthViewDenoise == depthView)
            return;

        _denoiseResourceSet?.Dispose();
        _denoiseResourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _denoiseResourceLayout, _denoiseUniformBuffer, _scratchView, _pointSampler, depthView, _pointSampler));
        _boundDepthViewDenoise = depthView;
    }

    public void Dispose()
    {
        _rawPipeline?.Dispose();
        _rawResourceSet?.Dispose();
        _rawResourceLayout?.Dispose();
        _rawUniformBuffer?.Dispose();
        _denoisePipeline?.Dispose();
        _denoiseResourceSet?.Dispose();
        _denoiseResourceLayout?.Dispose();
        _denoiseUniformBuffer?.Dispose();
        _pointSampler?.Dispose();
        _scratchFramebuffer?.Dispose();
        _scratchView?.Dispose();
        _scratchTexture?.Dispose();
    }
}
