using System.Numerics;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Camera-facing ring outline showing a light's range, drawn as a line loop.
/// Owns a static unit-circle (radius 1, XZ plane) vertex buffer built once;
/// lightring.vert scales it by the light's actual range per draw.
/// </summary>
public sealed class VeldridLightRingRenderer : IDisposable
{
    private const int Segments = 48;

    private readonly GraphicsDevice _device;

    private DeviceBuffer? _ringVertexBuffer;
    private DeviceBuffer? _uniformBuffer;
    private ResourceLayout? _resourceLayout;
    private ResourceSet? _resourceSet;
    private Pipeline? _pipeline;
    private OutputDescription? _outputDescription;

    public VeldridLightRingRenderer(GraphicsDevice device)
    {
        _device = device;
        BuildRing();
    }

    private void BuildRing()
    {
        // Segments+1 points, last one repeating the first, drawn as a LineStrip
        // (matches the old GL_LINE_LOOP rendering of a closed ring).
        var points = new Vector3[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            float t = (float)i / Segments * MathF.Tau;
            points[i] = new Vector3(MathF.Cos(t), 0f, MathF.Sin(t));
        }

        _ringVertexBuffer = _device.ResourceFactory.CreateBuffer(new BufferDescription((uint)(points.Length * 12), BufferUsage.VertexBuffer));
        _device.UpdateBuffer(_ringVertexBuffer, 0, points);
    }

    public void Render(CommandList commandList, Matrix4x4 view, Matrix4x4 proj, Vector3 worldPos, float range,
        Vector4 color, OutputDescription outputDescription)
    {
        EnsurePipeline(outputDescription);
        if (_pipeline == null || _uniformBuffer == null || _resourceSet == null)
            return;

        var uniforms = new LightRingUniforms { View = view, Proj = proj, WorldPos = worldPos, Range = range, Color = color };
        commandList.UpdateBuffer(_uniformBuffer, 0, ref uniforms);

        commandList.SetPipeline(_pipeline);
        commandList.SetGraphicsResourceSet(0, _resourceSet);
        commandList.SetVertexBuffer(0, _ringVertexBuffer);
        commandList.Draw(Segments + 1);
    }

    private void EnsurePipeline(OutputDescription outputDescription)
    {
        if (_pipeline != null && _outputDescription != null && _outputDescription.Value.Equals(outputDescription))
            return;

        ResourceFactory factory = _device.ResourceFactory;
        _resourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("LightRingUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

        _uniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            (uint)((System.Runtime.InteropServices.Marshal.SizeOf<LightRingUniforms>() + 15) / 16 * 16), BufferUsage.UniformBuffer));

        _resourceSet ??= factory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _uniformBuffer));

        var (vertexShader, fragmentShader) = VeldridShaderCache.GetOrCompile(_device, "lightring.vert", "lightring.frag");
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));

        _pipeline?.Dispose();
        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.LineStrip,
            ResourceLayouts = new[] { _resourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vertexShader, fragmentShader }),
            Outputs = outputDescription,
        });
        _outputDescription = outputDescription;
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _resourceSet?.Dispose();
        _resourceLayout?.Dispose();
        _uniformBuffer?.Dispose();
        _ringVertexBuffer?.Dispose();
    }
}
