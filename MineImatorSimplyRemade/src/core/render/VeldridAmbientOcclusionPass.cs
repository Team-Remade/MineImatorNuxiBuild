using System.Numerics;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Screen-space ambient occlusion, drawn as a full-screen alpha-blended pass
/// directly onto the main scene's color framebuffer (see <c>ambient_occlusion.frag</c>'s
/// migration note) - must run after every opaque mesh in the same frame's
/// command list, into the same <see cref="Framebuffer"/> those meshes rendered into.
/// </summary>
public sealed class VeldridAmbientOcclusionPass : IDisposable
{
    private readonly GraphicsDevice _device;

    private DeviceBuffer? _uniformBuffer;
    private ResourceLayout? _resourceLayout;
    private ResourceSet? _resourceSet;
    private Pipeline? _pipeline;
    private OutputDescription? _outputDescription;
    private Sampler? _depthSampler;
    private TextureView? _boundDepthView;

    public float Radius { get; set; } = 6f;
    public float Strength { get; set; } = 1f;
    public Vector3 Color { get; set; } = Vector3.Zero;
    public float Ratio { get; set; } = 0.5f;
    public float RatioBalance { get; set; } = 0.5f;
    public int SampleCount { get; set; } = 16;

    /// <summary>1 = output the raw AO mask as a debug grayscale image instead of
    /// alpha-blending it onto the scene.</summary>
    public bool DebugOutputMask { get; set; }

    public VeldridAmbientOcclusionPass(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>Draws the AO pass. Caller must have already bound
    /// <paramref name="targetFramebuffer"/> (the main scene framebuffer) on
    /// <paramref name="commandList"/> before calling.</summary>
    public void Render(CommandList commandList, TextureView depthView, uint width, uint height,
        float nearPlane, float farPlane, OutputDescription targetOutputDescription)
    {
        EnsurePipeline(targetOutputDescription);
        if (_pipeline == null || _uniformBuffer == null)
            return;

        EnsureResourceSet(depthView);

        var uniforms = AmbientOcclusionUniforms.Default;
        uniforms.TexelSize = new Vector2(1f / Math.Max(1u, width), 1f / Math.Max(1u, height));
        uniforms.Near = nearPlane;
        uniforms.Far = farPlane;
        uniforms.Radius = Radius;
        uniforms.Strength = Strength;
        uniforms.Color = Color;
        uniforms.Ratio = Ratio;
        uniforms.RatioBalance = RatioBalance;
        uniforms.SampleCount = SampleCount;
        uniforms.OutputMode = DebugOutputMask ? 1 : 0;
        commandList.UpdateBuffer(_uniformBuffer, 0, ref uniforms);

        VeldridFullScreenPass.Draw(commandList, _pipeline, _resourceSet!);
    }

    private void EnsurePipeline(OutputDescription outputDescription)
    {
        if (_pipeline != null && _outputDescription != null && _outputDescription.Value.Equals(outputDescription))
            return;

        ResourceFactory factory = _device.ResourceFactory;
        _resourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("AOUniforms", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uDepthTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uDepthSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _uniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            (uint)((System.Runtime.InteropServices.Marshal.SizeOf<AmbientOcclusionUniforms>() + 15) / 16 * 16),
            BufferUsage.UniformBuffer));

        _depthSampler ??= factory.CreateSampler(SamplerDescription.Point);

        // Darkening blend: dst = dst * (1 - srcAlpha) + srcColor * srcAlpha.
        _pipeline?.Dispose();
        _pipeline = VeldridFullScreenPass.CreatePipeline(_device, "ambient_occlusion.frag", _resourceLayout,
            outputDescription, BlendStateDescription.SingleAlphaBlend);
        _outputDescription = outputDescription;

        _resourceSet?.Dispose();
        _resourceSet = null;
        _boundDepthView = null;
    }

    private void EnsureResourceSet(TextureView depthView)
    {
        if (_resourceSet != null && _boundDepthView == depthView)
            return;

        _resourceSet?.Dispose();
        _resourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _resourceLayout, _uniformBuffer, depthView, _depthSampler));
        _boundDepthView = depthView;
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _resourceSet?.Dispose();
        _resourceLayout?.Dispose();
        _uniformBuffer?.Dispose();
        _depthSampler?.Dispose();
    }
}
