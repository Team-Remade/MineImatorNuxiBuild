using System.Numerics;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Camera-facing textured quad, used for light icon billboards. Owns a static
/// unit-quad vertex buffer built once ([-0.5,0.5] local space, matching
/// billboard.vert's expectations) and the billboard.vert/frag pipeline.
/// </summary>
public sealed class VeldridBillboardRenderer : IDisposable
{
    private readonly GraphicsDevice _device;

    private DeviceBuffer? _quadVertexBuffer;
    private DeviceBuffer? _uniformBuffer;
    private ResourceLayout? _resourceLayout;
    private Pipeline? _pipeline;
    private OutputDescription? _outputDescription;
    private Sampler? _sampler;
    private readonly Dictionary<Texture, ResourceSet> _resourceSetsByTexture = new();

    private struct QuadVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;
    }

    public VeldridBillboardRenderer(GraphicsDevice device)
    {
        _device = device;
        BuildQuad();
    }

    private void BuildQuad()
    {
        // Two triangles covering [-0.5,0.5]^2, matching aPos.xy usage in billboard.vert.
        QuadVertex[] verts =
        {
            new() { Position = new Vector3(-0.5f, -0.5f, 0), TexCoord = new Vector2(0, 1) },
            new() { Position = new Vector3(0.5f, -0.5f, 0), TexCoord = new Vector2(1, 1) },
            new() { Position = new Vector3(0.5f, 0.5f, 0), TexCoord = new Vector2(1, 0) },
            new() { Position = new Vector3(-0.5f, -0.5f, 0), TexCoord = new Vector2(0, 1) },
            new() { Position = new Vector3(0.5f, 0.5f, 0), TexCoord = new Vector2(1, 0) },
            new() { Position = new Vector3(-0.5f, 0.5f, 0), TexCoord = new Vector2(0, 0) },
        };

        _quadVertexBuffer = _device.ResourceFactory.CreateBuffer(new BufferDescription((uint)(verts.Length * 32), BufferUsage.VertexBuffer));
        _device.UpdateBuffer(_quadVertexBuffer, 0, verts);
    }

    public void Render(CommandList commandList, Matrix4x4 view, Matrix4x4 proj, Vector3 worldPos, float size,
        Vector4 tint, TextureView textureView, Texture textureKey, OutputDescription outputDescription)
    {
        EnsurePipeline(outputDescription);
        if (_pipeline == null || _uniformBuffer == null)
            return;

        ResourceSet resourceSet = EnsureResourceSet(textureView, textureKey);

        var uniforms = new BillboardUniforms { View = view, Proj = proj, WorldPos = worldPos, Size = size, Tint = tint };
        commandList.UpdateBuffer(_uniformBuffer, 0, ref uniforms);

        commandList.SetPipeline(_pipeline);
        commandList.SetGraphicsResourceSet(0, resourceSet);
        commandList.SetVertexBuffer(0, _quadVertexBuffer);
        commandList.Draw(6);
    }

    private void EnsurePipeline(OutputDescription outputDescription)
    {
        if (_pipeline != null && _outputDescription != null && _outputDescription.Value.Equals(outputDescription))
            return;

        ResourceFactory factory = _device.ResourceFactory;
        _resourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("BillboardUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uBillboardTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uBillboardSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _uniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            (uint)((System.Runtime.InteropServices.Marshal.SizeOf<BillboardUniforms>() + 15) / 16 * 16), BufferUsage.UniformBuffer));

        _sampler ??= factory.CreateSampler(SamplerDescription.Linear);

        var (vertexShader, fragmentShader) = VeldridShaderCache.GetOrCompile(_device, "billboard.vert", "billboard.frag");
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        _pipeline?.Dispose();
        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _resourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vertexShader, fragmentShader }),
            Outputs = outputDescription,
        });
        _outputDescription = outputDescription;

        foreach (ResourceSet set in _resourceSetsByTexture.Values) set.Dispose();
        _resourceSetsByTexture.Clear();
    }

    private ResourceSet EnsureResourceSet(TextureView textureView, Texture textureKey)
    {
        if (_resourceSetsByTexture.TryGetValue(textureKey, out ResourceSet? existing))
            return existing;

        ResourceSet resourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _resourceLayout, _uniformBuffer, textureView, _sampler));
        _resourceSetsByTexture[textureKey] = resourceSet;
        return resourceSet;
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        foreach (ResourceSet set in _resourceSetsByTexture.Values) set.Dispose();
        _resourceLayout?.Dispose();
        _uniformBuffer?.Dispose();
        _sampler?.Dispose();
        _quadVertexBuffer?.Dispose();
    }
}
