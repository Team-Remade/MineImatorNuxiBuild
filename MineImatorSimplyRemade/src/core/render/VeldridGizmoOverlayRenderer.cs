using System.Numerics;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Flat-colored line/triangle overlay geometry: the manipulation gizmo
/// (translate/rotate/scale handles), bone indicators, and any other
/// "just draw some colored lines/triangles with an MVP" overlay. Caller owns
/// the actual vertex data (arrows, boxes, cones, ...) and uploads it to a
/// <see cref="DeviceBuffer"/> via <see cref="UploadVertices"/>; this class only
/// owns the shared gizmo.vert/gizmo.frag pipelines (one per topology/depth-test
/// combination) and per-draw uniform buffer.
/// </summary>
public sealed class VeldridGizmoOverlayRenderer : IDisposable
{
    private readonly GraphicsDevice _device;

    private DeviceBuffer? _uniformBuffer;
    private ResourceLayout? _resourceLayout;
    private ResourceSet? _resourceSet;
    private readonly Dictionary<(PrimitiveTopology, bool DepthTest, bool DepthWrite), Pipeline> _pipelines = new();
    private OutputDescription? _outputDescription;

    public VeldridGizmoOverlayRenderer(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>Uploads a flat position-only vertex buffer (matches gizmo.vert's
    /// single <c>vec3 aPos</c> input) - callers typically rebuild this whenever
    /// the gizmo's shape/scale changes, which for a manipulator handle is most frames.</summary>
    public DeviceBuffer UploadVertices(Vector3[] positions, DeviceBuffer? reuse = null)
    {
        uint sizeInBytes = (uint)(positions.Length * 12); // 3 floats
        if (reuse == null || reuse.SizeInBytes < sizeInBytes)
        {
            reuse?.Dispose();
            reuse = _device.ResourceFactory.CreateBuffer(new BufferDescription(sizeInBytes, BufferUsage.VertexBuffer));
        }
        _device.UpdateBuffer(reuse, 0, positions);
        return reuse;
    }

    /// <summary>Draws <paramref name="vertexBuffer"/> as flat-colored geometry.
    /// <paramref name="depthTest"/>/<paramref name="depthWrite"/> false lets overlay
    /// geometry (e.g. the manipulator gizmo) always render on top of the scene.</summary>
    public void Render(CommandList commandList, DeviceBuffer vertexBuffer, uint vertexCount, PrimitiveTopology topology,
        Matrix4x4 mvp, Vector4 color, OutputDescription outputDescription, bool depthTest = true, bool depthWrite = true)
    {
        Pipeline pipeline = EnsurePipeline(topology, depthTest, depthWrite, outputDescription);
        if (_uniformBuffer == null || _resourceSet == null)
            return;

        var uniforms = new GizmoUniforms { MVP = mvp, Color = color };
        commandList.UpdateBuffer(_uniformBuffer, 0, ref uniforms);

        commandList.SetPipeline(pipeline);
        commandList.SetGraphicsResourceSet(0, _resourceSet);
        commandList.SetVertexBuffer(0, vertexBuffer);
        commandList.Draw(vertexCount);
    }

    private Pipeline EnsurePipeline(PrimitiveTopology topology, bool depthTest, bool depthWrite, OutputDescription outputDescription)
    {
        ResourceFactory factory = _device.ResourceFactory;
        _resourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("GizmoUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

        _uniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            (uint)((System.Runtime.InteropServices.Marshal.SizeOf<GizmoUniforms>() + 15) / 16 * 16), BufferUsage.UniformBuffer));

        _resourceSet ??= factory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _uniformBuffer));

        var key = (topology, depthTest, depthWrite);
        if (_pipelines.TryGetValue(key, out Pipeline? cached) && _outputDescription != null && _outputDescription.Value.Equals(outputDescription))
            return cached;

        if (_outputDescription != null && !_outputDescription.Value.Equals(outputDescription))
        {
            foreach (Pipeline p in _pipelines.Values) p.Dispose();
            _pipelines.Clear();
        }
        _outputDescription = outputDescription;

        var (vertexShader, fragmentShader) = VeldridShaderCache.GetOrCompile(_device, "gizmo.vert", "gizmo.frag");
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));

        DepthStencilStateDescription depthState = depthTest
            ? new DepthStencilStateDescription(depthWrite, ComparisonKind.LessEqual)
            : DepthStencilStateDescription.Disabled;

        Pipeline pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = depthState,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = topology,
            ResourceLayouts = new[] { _resourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vertexShader, fragmentShader }),
            Outputs = outputDescription,
        });

        _pipelines[key] = pipeline;
        return pipeline;
    }

    public void Dispose()
    {
        foreach (Pipeline p in _pipelines.Values) p.Dispose();
        _pipelines.Clear();
        _resourceSet?.Dispose();
        _resourceLayout?.Dispose();
        _uniformBuffer?.Dispose();
    }
}
