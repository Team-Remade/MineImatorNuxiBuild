using System.Numerics;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Sobel-edge-detects a <see cref="VeldridSilhouetteMask"/> and draws the
/// detected outline directly onto the main scene framebuffer with alpha
/// blending - the "selection glow outline" effect. Caller must have already
/// bound the main scene framebuffer (same convention as
/// <see cref="VeldridAmbientOcclusionPass"/>).
/// </summary>
public sealed class VeldridEdgeOutlinePass : IDisposable
{
    private readonly GraphicsDevice _device;

    private DeviceBuffer? _uniformBuffer;
    private ResourceLayout? _resourceLayout;
    private ResourceSet? _resourceSet;
    private Pipeline? _pipeline;
    private OutputDescription? _outputDescription;
    private Sampler? _sampler;
    private TextureView? _boundMaskView;

    public Vector4 EdgeColor { get; set; } = new(1f, 0.65f, 0f, 1f);
    public float Threshold { get; set; } = 0.4f;

    public VeldridEdgeOutlinePass(GraphicsDevice device)
    {
        _device = device;
    }

    public void Render(CommandList commandList, TextureView maskView, uint width, uint height, OutputDescription outputDescription)
    {
        EnsurePipeline(outputDescription);
        if (_pipeline == null || _uniformBuffer == null)
            return;

        EnsureResourceSet(maskView);

        var uniforms = new EdgeUniforms
        {
            TexelSize = new Vector2(1f / Math.Max(1u, width), 1f / Math.Max(1u, height)),
            EdgeColor = EdgeColor,
            Threshold = Threshold,
        };
        commandList.UpdateBuffer(_uniformBuffer, 0, ref uniforms);

        VeldridFullScreenPass.Draw(commandList, _pipeline, _resourceSet!);
    }

    private void EnsurePipeline(OutputDescription outputDescription)
    {
        if (_pipeline != null && _outputDescription != null && _outputDescription.Value.Equals(outputDescription))
            return;

        ResourceFactory factory = _device.ResourceFactory;
        _resourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("EdgeUniforms", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uMaskTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uMaskSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _uniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            (uint)((System.Runtime.InteropServices.Marshal.SizeOf<EdgeUniforms>() + 15) / 16 * 16), BufferUsage.UniformBuffer));

        _sampler ??= factory.CreateSampler(SamplerDescription.Point);

        _pipeline?.Dispose();
        _pipeline = VeldridFullScreenPass.CreatePipeline(_device, "edge.frag", _resourceLayout, outputDescription, BlendStateDescription.SingleAlphaBlend);
        _outputDescription = outputDescription;

        _resourceSet?.Dispose();
        _resourceSet = null;
    }

    private void EnsureResourceSet(TextureView maskView)
    {
        if (_resourceSet != null && _boundMaskView == maskView)
            return;

        _resourceSet?.Dispose();
        _resourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _uniformBuffer, maskView, _sampler));
        _boundMaskView = maskView;
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _resourceSet?.Dispose();
        _resourceLayout?.Dispose();
        _uniformBuffer?.Dispose();
        _sampler?.Dispose();
    }
}
