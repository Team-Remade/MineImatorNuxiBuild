using System.Numerics;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Replacement for <c>core.mdl.Mesh</c>'s GPU-resident triangle mesh, targeting
/// Veldrid instead of Silk.NET.OpenGL.
///
/// MIGRATION STATUS - subsystem pass 2/N ("lighting uniforms"): pass 1 ported
/// geometry upload (position/normal/UV, optional index buffer) and a minimal
/// unlit/flat-shaded draw call; this pass adds the shared per-frame SceneData
/// (sun/moon fill lights, ambient) and a simplified point-light array (see
/// <see cref="PointLightUniforms"/>) as a second bound resource set (set = 1),
/// sourced from <c>VeldridBitmapRenderSurface.SceneDataBuffer</c>/<c>PointLightBuffer</c>
/// so every mesh drawn into the same surface shares one lighting state. Still
/// NOT ported (each is its own follow-up subsystem pass):
///   - skinning (bone indices/weights) and per-instance matrices
///   - spot-light cones, per-light shadow-cubemap indices, directional+point
///     shadow sampling, subsurface scattering, fog/height-fog
///   - shape keys / morph targets
///   - animated texture atlas sampling
/// See <c>core.mdl.Mesh</c> (the old GL version, still present elsewhere in the
/// codebase until every caller is migrated) for the full feature set each of
/// those passes needs to restore.
/// </summary>
public sealed class VeldridMesh : IDisposable
{
    private readonly GraphicsDevice _device;

    private DeviceBuffer? _vertexBuffer;
    private DeviceBuffer? _indexBuffer;
    private uint _indexCount;
    private uint _vertexCount;

    private DeviceBuffer? _meshUniformBuffer;
    private DeviceBuffer? _materialUniformBuffer;
    private ResourceSet? _meshResourceSet;
    private ResourceLayout? _meshResourceLayout;

    private ResourceLayout? _sceneResourceLayout;
    private ResourceSet? _sceneResourceSet;
    private DeviceBuffer? _boundSceneDataBuffer;
    private DeviceBuffer? _boundPointLightBuffer;

    private Pipeline? _pipeline;
    private OutputDescription _outputDescription;

    public List<Vector3> Vertices { get; } = new();
    public List<Vector3> Normals { get; } = new();
    public List<Vector2> TexCoords { get; } = new();
    public uint[]? Indices { get; set; }

    public Vector3 Albedo { get; set; } = Vector3.One;
    public float Alpha { get; set; } = 1f;
    public bool Unlit { get; set; }
    public bool EmissionEnabled { get; set; }
    public Vector3 EmissionColor { get; set; }
    public float EmissionEnergy { get; set; }

    /// <summary>Bound texture, or null to render with the flat <see cref="Albedo"/> color.</summary>
    public Texture? AlbedoTexture { get; set; }
    private TextureView? _albedoTextureView;
    private Sampler? _albedoSampler;

    private struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;
    }

    public VeldridMesh(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>
    /// Uploads <see cref="Vertices"/>/<see cref="Normals"/>/<see cref="TexCoords"/>/<see cref="Indices"/>
    /// to GPU buffers and (re)builds the draw pipeline. Call again after geometry changes.
    /// </summary>
    /// <param name="outputDescription">
    /// Describes the color/depth attachment formats of whatever <see cref="Framebuffer"/>
    /// this mesh will be drawn into - pass <c>VeldridBitmapRenderSurface.Framebuffer.OutputDescription</c>.
    /// Required because this device is headless (no swapchain), so there is no
    /// implicit "the window's framebuffer" the way GL/GLFW used to provide.
    /// </param>
    public void Upload(OutputDescription outputDescription)
    {
        DisposeGpuResources();

        if (Vertices.Count == 0)
            return;

        _outputDescription = outputDescription;

        bool hasNormals = Normals.Count == Vertices.Count;
        bool hasUVs = TexCoords.Count == Vertices.Count;

        var vertexData = new Vertex[Vertices.Count];
        for (int i = 0; i < Vertices.Count; i++)
        {
            vertexData[i] = new Vertex
            {
                Position = Vertices[i],
                Normal = hasNormals ? Normals[i] : Vector3.UnitY,
                TexCoord = hasUVs ? TexCoords[i] : Vector2.Zero,
            };
        }

        ResourceFactory factory = _device.ResourceFactory;

        _vertexCount = (uint)vertexData.Length;
        _vertexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(vertexData.Length * VertexSizeHelper.SizeInBytes()), BufferUsage.VertexBuffer));
        _device.UpdateBuffer(_vertexBuffer, 0, vertexData);

        if (Indices is { Length: > 0 })
        {
            _indexCount = (uint)Indices.Length;
            _indexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)(Indices.Length * sizeof(uint)), BufferUsage.IndexBuffer));
            _device.UpdateBuffer(_indexBuffer, 0, Indices);
        }

        _meshUniformBuffer = factory.CreateBuffer(new BufferDescription(
            AlignTo16((uint)System.Runtime.InteropServices.Marshal.SizeOf<MeshUniforms>()),
            BufferUsage.UniformBuffer));
        _materialUniformBuffer = factory.CreateBuffer(new BufferDescription(
            AlignTo16((uint)System.Runtime.InteropServices.Marshal.SizeOf<MeshMaterialUniforms>()),
            BufferUsage.UniformBuffer));

        _albedoSampler = factory.CreateSampler(SamplerDescription.Linear);

        BuildPipelineAndResources();
    }

    private static uint AlignTo16(uint size) => (size + 15) / 16 * 16;

    private void BuildPipelineAndResources()
    {
        ResourceFactory factory = _device.ResourceFactory;
        var (vertexShader, fragmentShader) = VeldridShaderCache.GetOrCompile(_device, "simple.vert", "simple.frag");

        // Matches simple.frag's `set = 0` bindings: binding 0 = MeshUniforms,
        // binding 1 = uTextureSampler, binding 2 = MeshMaterial. Order here
        // must match the shader's declaration order exactly.
        _meshResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("MeshUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uTextureSampler", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uTextureSamplerState", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MeshMaterial", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

        // Matches simple.vert/simple.frag's `set = 1` bindings: binding 0 =
        // SceneData (read by both stages - simple.vert samples uLightSpaceMatrix),
        // binding 1 = PointLightData (fragment only).
        _sceneResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SceneData", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PointLightData", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _meshResourceLayout, _sceneResourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vertexShader, fragmentShader }),
            Outputs = _outputDescription,
        });

        RebuildResourceSet();
    }

    private void RebuildResourceSet()
    {
        if (_meshResourceLayout == null || _meshUniformBuffer == null || _materialUniformBuffer == null || _albedoSampler == null)
            return;

        _meshResourceSet?.Dispose();

        TextureView view = GetOrCreateAlbedoView();
        _meshResourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _meshResourceLayout, _meshUniformBuffer, view, _albedoSampler, _materialUniformBuffer));
    }

    /// <summary>
    /// Builds (or rebuilds, if the caller passed different buffer instances than
    /// last time - e.g. a different render surface) the set = 1 resource set.
    /// Cheap to call every frame: content updates to the buffers themselves
    /// (<c>UpdateBuffer</c>) don't require recreating the <see cref="ResourceSet"/>,
    /// only a change of which buffer objects are bound does.
    /// </summary>
    private void EnsureSceneResourceSet(DeviceBuffer sceneDataBuffer, DeviceBuffer pointLightBuffer)
    {
        if (_sceneResourceLayout == null)
            return;

        if (_sceneResourceSet != null && _boundSceneDataBuffer == sceneDataBuffer && _boundPointLightBuffer == pointLightBuffer)
            return;

        _sceneResourceSet?.Dispose();
        _sceneResourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _sceneResourceLayout, sceneDataBuffer, pointLightBuffer));
        _boundSceneDataBuffer = sceneDataBuffer;
        _boundPointLightBuffer = pointLightBuffer;
    }

    private TextureView GetOrCreateAlbedoView()
    {
        if (AlbedoTexture == null)
        {
            // No texture bound - fall back to a shared 1x1 white texture so the
            // resource set is always valid (uUseTexture=0 means the shader
            // ignores its contents anyway).
            _albedoTextureView?.Dispose();
            Texture placeholder = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
            _device.UpdateTexture(placeholder, new byte[] { 255, 255, 255, 255 }, 0, 0, 0, 1, 1, 1, 0, 0);
            _albedoTextureView = _device.ResourceFactory.CreateTextureView(placeholder);
            return _albedoTextureView;
        }

        _albedoTextureView?.Dispose();
        _albedoTextureView = _device.ResourceFactory.CreateTextureView(AlbedoTexture);
        return _albedoTextureView;
    }

    /// <summary>Draws the mesh into whatever framebuffer <paramref name="commandList"/> currently has bound.</summary>
    /// <param name="sceneDataBuffer">Typically <c>VeldridBitmapRenderSurface.SceneDataBuffer</c>.</param>
    /// <param name="pointLightBuffer">Typically <c>VeldridBitmapRenderSurface.PointLightBuffer</c>.</param>
    public void Render(CommandList commandList, Matrix4x4 model, Matrix4x4 view, Matrix4x4 proj,
        DeviceBuffer sceneDataBuffer, DeviceBuffer pointLightBuffer)
    {
        if (_pipeline == null || _vertexBuffer == null || _meshUniformBuffer == null || _materialUniformBuffer == null)
            return;

        EnsureSceneResourceSet(sceneDataBuffer, pointLightBuffer);

        var meshUniforms = MeshUniforms.Default;
        meshUniforms.Model = model;
        meshUniforms.MVP = model * view * proj;
        commandList.UpdateBuffer(_meshUniformBuffer, 0, ref meshUniforms);

        var materialUniforms = MeshMaterialUniforms.Default;
        materialUniforms.Albedo = Albedo;
        materialUniforms.Alpha = Alpha;
        materialUniforms.UseTexture = AlbedoTexture != null ? 1 : 0;
        materialUniforms.IsUnlit = Unlit ? 1 : 0;
        materialUniforms.EmissionEnabled = EmissionEnabled ? 1 : 0;
        materialUniforms.EmissionColor = EmissionColor;
        materialUniforms.EmissionEnergy = EmissionEnergy;
        commandList.UpdateBuffer(_materialUniformBuffer, 0, ref materialUniforms);

        commandList.SetPipeline(_pipeline);
        commandList.SetGraphicsResourceSet(0, _meshResourceSet);
        commandList.SetGraphicsResourceSet(1, _sceneResourceSet);
        commandList.SetVertexBuffer(0, _vertexBuffer);

        if (_indexBuffer != null)
        {
            commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);
            commandList.DrawIndexed(_indexCount);
        }
        else
        {
            commandList.Draw(_vertexCount);
        }
    }

    private void DisposeGpuResources()
    {
        _pipeline?.Dispose();
        _meshResourceSet?.Dispose();
        _meshResourceLayout?.Dispose();
        _sceneResourceSet?.Dispose();
        _sceneResourceLayout?.Dispose();
        _albedoTextureView?.Dispose();
        _albedoSampler?.Dispose();
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _meshUniformBuffer?.Dispose();
        _materialUniformBuffer?.Dispose();

        _pipeline = null;
        _meshResourceSet = null;
        _meshResourceLayout = null;
        _sceneResourceSet = null;
        _sceneResourceLayout = null;
        _boundSceneDataBuffer = null;
        _boundPointLightBuffer = null;
        _albedoTextureView = null;
        _albedoSampler = null;
        _vertexBuffer = null;
        _indexBuffer = null;
        _meshUniformBuffer = null;
        _materialUniformBuffer = null;
    }

    public void Dispose() => DisposeGpuResources();
}

internal static class VertexSizeHelper
{
    public static int SizeInBytes() => sizeof(float) * (3 + 3 + 2);
}
