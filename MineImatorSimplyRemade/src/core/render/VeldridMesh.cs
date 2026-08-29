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
    private DeviceBuffer? _boundEnvironmentBuffer;
    private TextureView? _boundShadowMapView;
    private Sampler? _boundShadowMapSampler;
    private Texture? _placeholderShadowTexture;
    private TextureView? _placeholderShadowView;
    private Sampler? _placeholderShadowSampler;

    private ResourceLayout? _pointShadowResourceLayout;
    private ResourceSet? _pointShadowResourceSet;
    private VeldridPointShadowMap?[] _boundPointShadowMaps = new VeldridPointShadowMap?[MaxPointShadows];
    private Texture? _placeholderCubeTexture;
    private TextureView? _placeholderCubeView;
    private Sampler? _placeholderCubeSampler;

    private const int MaxPointShadows = 8;

    private Pipeline? _pipeline;
    private OutputDescription _outputDescription;

    // Depth-only shadow-caster pipeline (separate from the main color pipeline
    // above - different shader pair, resource layout, and output description).
    private DeviceBuffer? _shadowUniformBuffer;
    private ResourceLayout? _shadowResourceLayout;
    private ResourceSet? _shadowResourceSet;
    private Pipeline? _shadowPipeline;
    private OutputDescription? _shadowOutputDescription;
    private Sampler? _shadowCasterSampler;

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

    /// <summary>Per-mesh subsurface-scattering amount [0..1], 0 disables it.</summary>
    public float Subsurface { get; set; }
    public Vector3 SubsurfaceRadius { get; set; } = new(0.42f, 0.24f, 0.14f);
    public Vector3 SubsurfaceColor { get; set; } = Vector3.One;
    public float SubsurfaceHighlight { get; set; }
    public float SubsurfaceHighlightStrength { get; set; }

    /// <summary>When false, this mesh is excluded from fog even when global fog is enabled.</summary>
    public bool IncludeInFog { get; set; } = true;

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
        // binding 1 = PointLightData (fragment only), binding 2/3 = the
        // directional shadow map's depth texture/sampler (fragment only),
        // binding 4 = SceneEnvironment (SSS/fog globals, fragment only).
        _sceneResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SceneData", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PointLightData", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uShadowMapTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uShadowMapSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneEnvironment", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

        // Matches simple.frag's `set = 2` bindings: 8 explicit (textureCube,
        // sampler) pairs for point-light shadow cubemaps - see the migration
        // note above uPointShadowCubeTexture0 in simple.frag for why these are
        // explicit named bindings instead of a real GLSL sampler array.
        var pointShadowElements = new ResourceLayoutElementDescription[MaxPointShadows * 2];
        for (int i = 0; i < MaxPointShadows; i++)
        {
            pointShadowElements[i * 2] = new ResourceLayoutElementDescription(
                $"uPointShadowCubeTexture{i}", ResourceKind.TextureReadOnly, ShaderStages.Fragment);
            pointShadowElements[i * 2 + 1] = new ResourceLayoutElementDescription(
                $"uPointShadowCubeSampler{i}", ResourceKind.Sampler, ShaderStages.Fragment);
        }
        _pointShadowResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(pointShadowElements));

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
            ResourceLayouts = new[] { _meshResourceLayout, _sceneResourceLayout, _pointShadowResourceLayout },
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
    private void EnsureSceneResourceSet(DeviceBuffer sceneDataBuffer, DeviceBuffer pointLightBuffer,
        TextureView shadowView, Sampler shadowSampler, DeviceBuffer environmentBuffer)
    {
        if (_sceneResourceLayout == null)
            return;

        if (_sceneResourceSet != null
            && _boundSceneDataBuffer == sceneDataBuffer
            && _boundPointLightBuffer == pointLightBuffer
            && _boundShadowMapView == shadowView
            && _boundShadowMapSampler == shadowSampler
            && _boundEnvironmentBuffer == environmentBuffer)
            return;

        _sceneResourceSet?.Dispose();
        _sceneResourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _sceneResourceLayout, sceneDataBuffer, pointLightBuffer, shadowView, shadowSampler, environmentBuffer));
        _boundSceneDataBuffer = sceneDataBuffer;
        _boundPointLightBuffer = pointLightBuffer;
        _boundShadowMapView = shadowView;
        _boundShadowMapSampler = shadowSampler;
        _boundEnvironmentBuffer = environmentBuffer;
    }

    /// <summary>
    /// Builds (or rebuilds, if which point-shadow maps are bound changed) the
    /// set = 2 resource set from up to <see cref="MaxPointShadows"/> maps.
    /// Missing/null slots get a shared 1x1 "always fully lit" placeholder cube.
    /// </summary>
    private void EnsurePointShadowResourceSet(IReadOnlyList<VeldridPointShadowMap?>? pointShadowMaps)
    {
        if (_pointShadowResourceLayout == null)
            return;

        bool unchanged = _pointShadowResourceSet != null;
        for (int i = 0; i < MaxPointShadows && unchanged; i++)
        {
            VeldridPointShadowMap? current = pointShadowMaps != null && i < pointShadowMaps.Count ? pointShadowMaps[i] : null;
            if (_boundPointShadowMaps[i] != current)
                unchanged = false;
        }
        if (unchanged)
            return;

        TextureView placeholderView = GetOrCreatePlaceholderCubeView();
        Sampler placeholderSampler = GetOrCreatePlaceholderCubeSampler();

        var resources = new BindableResource[MaxPointShadows * 2];
        for (int i = 0; i < MaxPointShadows; i++)
        {
            VeldridPointShadowMap? map = pointShadowMaps != null && i < pointShadowMaps.Count ? pointShadowMaps[i] : null;
            resources[i * 2] = map?.CubeTextureView ?? placeholderView;
            resources[i * 2 + 1] = map?.CubeSampler ?? placeholderSampler;
            _boundPointShadowMaps[i] = map;
        }

        _pointShadowResourceSet?.Dispose();
        _pointShadowResourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_pointShadowResourceLayout, resources));
    }

    private TextureView GetOrCreatePlaceholderCubeView()
    {
        if (_placeholderCubeView != null)
            return _placeholderCubeView;

        _placeholderCubeTexture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            1, 1, 1, 6, PixelFormat.R32_Float, TextureUsage.Sampled | TextureUsage.Cubemap));
        for (uint face = 0; face < 6; face++)
            _device.UpdateTexture(_placeholderCubeTexture, new float[] { 1f }, 0, 0, 0, 1, 1, 1, 0, face);
        _placeholderCubeView = _device.ResourceFactory.CreateTextureView(_placeholderCubeTexture);
        return _placeholderCubeView;
    }

    private Sampler GetOrCreatePlaceholderCubeSampler() =>
        _placeholderCubeSampler ??= _device.ResourceFactory.CreateSampler(SamplerDescription.Point);

    private TextureView GetOrCreatePlaceholderShadowView()
    {
        if (_placeholderShadowView != null)
            return _placeholderShadowView;

        _placeholderShadowTexture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            1, 1, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled));
        _device.UpdateTexture(_placeholderShadowTexture, new float[] { 1f }, 0, 0, 0, 1, 1, 1, 0, 0);
        _placeholderShadowView = _device.ResourceFactory.CreateTextureView(_placeholderShadowTexture);
        return _placeholderShadowView;
    }

    private Sampler GetOrCreatePlaceholderShadowSampler() =>
        _placeholderShadowSampler ??= _device.ResourceFactory.CreateSampler(SamplerDescription.Point);

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
    /// <param name="shadowMap">
    /// The directional shadow map to sample, or null to render fully unshadowed
    /// (matches <c>SceneDataUniforms.UseShadowMap == 0</c> - the shader still
    /// needs *some* bound texture/sampler even when unused, so a 1x1 placeholder
    /// is bound automatically when this is null).
    /// </param>
    /// <param name="environmentBuffer">Typically <c>VeldridBitmapRenderSurface.EnvironmentBuffer</c>.</param>
    public void Render(CommandList commandList, Matrix4x4 model, Matrix4x4 view, Matrix4x4 proj,
        DeviceBuffer sceneDataBuffer, DeviceBuffer pointLightBuffer, DeviceBuffer environmentBuffer,
        VeldridShadowMap? shadowMap = null, IReadOnlyList<VeldridPointShadowMap?>? pointShadowMaps = null)
    {
        if (_pipeline == null || _vertexBuffer == null || _meshUniformBuffer == null || _materialUniformBuffer == null)
            return;

        TextureView shadowView = shadowMap?.DepthTextureView ?? GetOrCreatePlaceholderShadowView();
        Sampler shadowSampler = shadowMap?.DepthSampler ?? GetOrCreatePlaceholderShadowSampler();
        EnsureSceneResourceSet(sceneDataBuffer, pointLightBuffer, shadowView, shadowSampler, environmentBuffer);
        EnsurePointShadowResourceSet(pointShadowMaps);

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
        materialUniforms.Subsurface = Subsurface;
        materialUniforms.SubsurfaceRadius = SubsurfaceRadius;
        materialUniforms.SubsurfaceColor = SubsurfaceColor;
        materialUniforms.SubsurfaceHighlight = SubsurfaceHighlight;
        materialUniforms.SubsurfaceHighlightStrength = SubsurfaceHighlightStrength;
        materialUniforms.IncludeInFog = IncludeInFog ? 1 : 0;
        commandList.UpdateBuffer(_materialUniformBuffer, 0, ref materialUniforms);

        commandList.SetPipeline(_pipeline);
        commandList.SetGraphicsResourceSet(0, _meshResourceSet);
        commandList.SetGraphicsResourceSet(1, _sceneResourceSet);
        commandList.SetGraphicsResourceSet(2, _pointShadowResourceSet);
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

    /// <summary>
    /// Renders this mesh depth-only into a <see cref="VeldridShadowMap"/>'s
    /// framebuffer from a light's point of view. Call once per shadow-casting
    /// mesh inside <see cref="VeldridShadowMap.RenderShadowPass"/>'s callback.
    /// </summary>
    public void RenderDepthOnly(CommandList commandList, Matrix4x4 lightMvp, OutputDescription shadowOutputDescription)
    {
        if (_vertexBuffer == null)
            return;

        EnsureShadowPipeline(shadowOutputDescription);
        if (_shadowPipeline == null || _shadowUniformBuffer == null || _shadowResourceSet == null)
            return;

        var uniforms = ShadowDepthUniforms.Default;
        uniforms.MVP = lightMvp;
        uniforms.Alpha = Alpha;
        uniforms.UseTexture = AlbedoTexture != null ? 1 : 0;
        commandList.UpdateBuffer(_shadowUniformBuffer, 0, ref uniforms);

        commandList.SetPipeline(_shadowPipeline);
        commandList.SetGraphicsResourceSet(0, _shadowResourceSet);
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

    // Point-light shadow-caster pipeline (separate again - different shader
    // pair/uniforms since it needs uLightPos/uFarPlane instead of a light-space MVP).
    private DeviceBuffer? _pointShadowUniformBuffer;
    private ResourceLayout? _pointShadowCasterResourceLayout;
    private ResourceSet? _pointShadowCasterResourceSet;
    private Pipeline? _pointShadowCasterPipeline;
    private OutputDescription? _pointShadowCasterOutputDescription;
    private Sampler? _pointShadowCasterSampler;
    private Texture? _pointShadowResourceSetAlbedoTexture;

    /// <summary>
    /// Renders this mesh depth-only into one face of a <see cref="VeldridPointShadowMap"/>.
    /// Call once per shadow-casting mesh, per face (6x), inside
    /// <see cref="VeldridPointShadowMap.RenderFace"/>'s callback.
    /// </summary>
    public void RenderPointShadowDepthOnly(CommandList commandList, Matrix4x4 model, Matrix4x4 faceViewProj,
        Vector3 lightPos, float farPlane, OutputDescription faceOutputDescription)
    {
        if (_vertexBuffer == null)
            return;

        EnsurePointShadowCasterPipeline(faceOutputDescription);
        if (_pointShadowCasterPipeline == null || _pointShadowUniformBuffer == null || _pointShadowCasterResourceSet == null)
            return;

        var uniforms = PointShadowDepthUniforms.Default;
        uniforms.LightViewProj = faceViewProj;
        uniforms.Model = model;
        uniforms.Alpha = Alpha;
        uniforms.UseTexture = AlbedoTexture != null ? 1 : 0;
        uniforms.LightPos = lightPos;
        uniforms.FarPlane = farPlane;
        commandList.UpdateBuffer(_pointShadowUniformBuffer, 0, ref uniforms);

        commandList.SetPipeline(_pointShadowCasterPipeline);
        commandList.SetGraphicsResourceSet(0, _pointShadowCasterResourceSet);
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

    private void EnsurePointShadowCasterPipeline(OutputDescription faceOutputDescription)
    {
        if (_pointShadowCasterPipeline != null && _pointShadowCasterOutputDescription != null
            && _pointShadowCasterOutputDescription.Value.Equals(faceOutputDescription))
        {
            RebuildPointShadowCasterResourceSetIfNeeded();
            return;
        }

        ResourceFactory factory = _device.ResourceFactory;
        var (vertexShader, fragmentShader) = VeldridShaderCache.GetOrCompile(_device, "point_shadow_depth.vert", "point_shadow_depth.frag");

        _pointShadowCasterResourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("PointShadowDepthUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uTextureSampler", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uTextureSamplerState", ResourceKind.Sampler, ShaderStages.Fragment)));

        _pointShadowUniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            AlignTo16((uint)System.Runtime.InteropServices.Marshal.SizeOf<PointShadowDepthUniforms>()),
            BufferUsage.UniformBuffer));

        _pointShadowCasterSampler ??= factory.CreateSampler(SamplerDescription.Linear);

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        _pointShadowCasterPipeline?.Dispose();
        _pointShadowCasterPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleDisabled,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _pointShadowCasterResourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vertexShader, fragmentShader }),
            Outputs = faceOutputDescription,
        });
        _pointShadowCasterOutputDescription = faceOutputDescription;

        RebuildPointShadowCasterResourceSetIfNeeded(force: true);
    }

    private void RebuildPointShadowCasterResourceSetIfNeeded(bool force = false)
    {
        if (_pointShadowCasterResourceLayout == null || _pointShadowUniformBuffer == null || _pointShadowCasterSampler == null)
            return;

        if (!force && _pointShadowCasterResourceSet != null && _pointShadowResourceSetAlbedoTexture == AlbedoTexture)
            return;

        _pointShadowCasterResourceSet?.Dispose();
        TextureView view = GetOrCreateAlbedoView();
        _pointShadowCasterResourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _pointShadowCasterResourceLayout, _pointShadowUniformBuffer, view, _pointShadowCasterSampler));
        _pointShadowResourceSetAlbedoTexture = AlbedoTexture;
    }

    // Pick/silhouette pipelines (subsystem pass 5/N) - share pick.vert's vertex
    // uniforms/layout, differ only in fragment shader/output.
    private DeviceBuffer? _pickVertexUniformBuffer;
    private DeviceBuffer? _pickMaterialUniformBuffer;
    private ResourceLayout? _pickResourceLayout;
    private ResourceSet? _pickResourceSet;
    private Pipeline? _pickColorPipeline;
    private Pipeline? _silhouettePipeline;
    private OutputDescription? _pickColorOutputDescription;
    private OutputDescription? _silhouetteOutputDescription;
    private Sampler? _pickSampler;
    private Texture? _pickResourceSetAlbedoTexture;

    /// <summary>Renders this mesh with a flat "pick color" for CPU-readback object
    /// picking - see <c>pick.frag</c>. Draw each pickable mesh with a distinct
    /// color into an offscreen buffer, then read back the pixel under the cursor.</summary>
    public void RenderPick(CommandList commandList, Matrix4x4 mvp, Vector3 pickColor, OutputDescription outputDescription)
    {
        if (_vertexBuffer == null)
            return;

        EnsurePickPipelines(outputDescription, null);
        if (_pickColorPipeline == null)
            return;

        UpdatePickUniforms(commandList, mvp, pickColor, forceOpaque: true);
        DrawWithPipeline(commandList, _pickColorPipeline, _pickResourceSet!);
    }

    /// <summary>Renders this mesh's alpha-tested silhouette mask (1.0 = covered)
    /// - see <c>silhouette.frag</c>. Used as the input to <see cref="VeldridEdgeOutlinePass"/>.</summary>
    public void RenderSilhouette(CommandList commandList, Matrix4x4 mvp, OutputDescription outputDescription)
    {
        if (_vertexBuffer == null)
            return;

        EnsurePickPipelines(null, outputDescription);
        if (_silhouettePipeline == null)
            return;

        UpdatePickUniforms(commandList, mvp, Vector3.Zero, forceOpaque: false);
        DrawWithPipeline(commandList, _silhouettePipeline, _pickResourceSet!);
    }

    private void UpdatePickUniforms(CommandList commandList, Matrix4x4 mvp, Vector3 pickColor, bool forceOpaque)
    {
        var vertexUniforms = PickVertexUniforms.Default;
        vertexUniforms.MVP = mvp;
        commandList.UpdateBuffer(_pickVertexUniformBuffer, 0, ref vertexUniforms);

        var materialUniforms = PickMaterialUniforms.Default;
        materialUniforms.PickColor = pickColor;
        materialUniforms.Alpha = Alpha;
        materialUniforms.UseTexture = AlbedoTexture != null ? 1 : 0;
        materialUniforms.ForceOpaque = forceOpaque ? 1 : 0;
        commandList.UpdateBuffer(_pickMaterialUniformBuffer, 0, ref materialUniforms);
    }

    private void DrawWithPipeline(CommandList commandList, Pipeline pipeline, ResourceSet resourceSet)
    {
        commandList.SetPipeline(pipeline);
        commandList.SetGraphicsResourceSet(0, resourceSet);
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

    private void EnsurePickPipelines(OutputDescription? pickColorOutput, OutputDescription? silhouetteOutput)
    {
        ResourceFactory factory = _device.ResourceFactory;

        _pickResourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("PickUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex),
            new ResourceLayoutElementDescription("PickMaterial", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uTextureTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uTextureSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uAlphaMaskTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uAlphaMaskSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        _pickVertexUniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            AlignTo16((uint)System.Runtime.InteropServices.Marshal.SizeOf<PickVertexUniforms>()), BufferUsage.UniformBuffer));
        _pickMaterialUniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            AlignTo16((uint)System.Runtime.InteropServices.Marshal.SizeOf<PickMaterialUniforms>()), BufferUsage.UniformBuffer));
        _pickSampler ??= factory.CreateSampler(SamplerDescription.Linear);

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        bool rebuildResourceSet = _pickResourceSet == null || _pickResourceSetAlbedoTexture != AlbedoTexture;

        if (pickColorOutput != null && (_pickColorPipeline == null || _pickColorOutputDescription == null || !_pickColorOutputDescription.Value.Equals(pickColorOutput.Value)))
        {
            var (vs, fs) = VeldridShaderCache.GetOrCompile(_device, "pick.vert", "pick.frag");
            _pickColorPipeline?.Dispose();
            _pickColorPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleDisabled,
                DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
                RasterizerState = new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _pickResourceLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vs, fs }),
                Outputs = pickColorOutput.Value,
            });
            _pickColorOutputDescription = pickColorOutput;
        }

        if (silhouetteOutput != null && (_silhouettePipeline == null || _silhouetteOutputDescription == null || !_silhouetteOutputDescription.Value.Equals(silhouetteOutput.Value)))
        {
            var (vs, fs) = VeldridShaderCache.GetOrCompile(_device, "pick.vert", "silhouette.frag");
            _silhouettePipeline?.Dispose();
            _silhouettePipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleDisabled,
                DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
                RasterizerState = new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new[] { _pickResourceLayout },
                ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vs, fs }),
                Outputs = silhouetteOutput.Value,
            });
            _silhouetteOutputDescription = silhouetteOutput;
        }

        if (rebuildResourceSet)
        {
            _pickResourceSet?.Dispose();
            TextureView view = GetOrCreateAlbedoView();
            // Both pick.frag and silhouette.frag declare the same alpha-mask
            // binding slot even though nothing in this migration pass sets a
            // real alpha mask texture yet - reuse the placeholder (fully opaque)
            // path via the same albedo view/sampler so the resource set is valid.
            _pickResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _pickResourceLayout, _pickVertexUniformBuffer, _pickMaterialUniformBuffer, view, _pickSampler, view, _pickSampler));
            _pickResourceSetAlbedoTexture = AlbedoTexture;
        }
    }

    private void EnsureShadowPipeline(OutputDescription shadowOutputDescription)
    {
        if (_shadowPipeline != null && _shadowOutputDescription != null && _shadowOutputDescription.Value.Equals(shadowOutputDescription))
        {
            RebuildShadowResourceSetIfNeeded();
            return;
        }

        ResourceFactory factory = _device.ResourceFactory;
        var (vertexShader, fragmentShader) = VeldridShaderCache.GetOrCompile(_device, "shadow_depth.vert", "shadow_depth.frag");

        _shadowResourceLayout ??= factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ShadowDepthUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uTextureSampler", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uTextureSamplerState", ResourceKind.Sampler, ShaderStages.Fragment)));

        _shadowUniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            AlignTo16((uint)System.Runtime.InteropServices.Marshal.SizeOf<ShadowDepthUniforms>()),
            BufferUsage.UniformBuffer));

        _shadowCasterSampler ??= factory.CreateSampler(SamplerDescription.Linear);

        // Same vertex layout as the main pipeline - shadow_depth.vert declares
        // the same 3 interleaved attributes (position/normal/uv) even though it
        // only reads position+uv, so this reuses the same vertex buffer.
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        _shadowPipeline?.Dispose();
        _shadowPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleDisabled,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _shadowResourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { vertexShader, fragmentShader }),
            Outputs = shadowOutputDescription,
        });
        _shadowOutputDescription = shadowOutputDescription;

        RebuildShadowResourceSetIfNeeded(force: true);
    }

    private Texture? _shadowResourceSetAlbedoTexture;

    private void RebuildShadowResourceSetIfNeeded(bool force = false)
    {
        if (_shadowResourceLayout == null || _shadowUniformBuffer == null || _shadowCasterSampler == null)
            return;

        if (!force && _shadowResourceSet != null && _shadowResourceSetAlbedoTexture == AlbedoTexture)
            return;

        _shadowResourceSet?.Dispose();
        TextureView view = GetOrCreateAlbedoView();
        _shadowResourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _shadowResourceLayout, _shadowUniformBuffer, view, _shadowCasterSampler));
        _shadowResourceSetAlbedoTexture = AlbedoTexture;
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
        _placeholderShadowView?.Dispose();
        _placeholderShadowTexture?.Dispose();
        _placeholderShadowSampler?.Dispose();
        _shadowPipeline?.Dispose();
        _shadowResourceSet?.Dispose();
        _shadowResourceLayout?.Dispose();
        _shadowUniformBuffer?.Dispose();
        _shadowCasterSampler?.Dispose();
        _pointShadowResourceSet?.Dispose();
        _pointShadowResourceLayout?.Dispose();
        _placeholderCubeView?.Dispose();
        _placeholderCubeTexture?.Dispose();
        _placeholderCubeSampler?.Dispose();
        _pointShadowCasterPipeline?.Dispose();
        _pointShadowCasterResourceSet?.Dispose();
        _pointShadowCasterResourceLayout?.Dispose();
        _pointShadowUniformBuffer?.Dispose();
        _pointShadowCasterSampler?.Dispose();
        _pickColorPipeline?.Dispose();
        _silhouettePipeline?.Dispose();
        _pickResourceSet?.Dispose();
        _pickResourceLayout?.Dispose();
        _pickVertexUniformBuffer?.Dispose();
        _pickMaterialUniformBuffer?.Dispose();
        _pickSampler?.Dispose();

        _pipeline = null;
        _meshResourceSet = null;
        _meshResourceLayout = null;
        _sceneResourceSet = null;
        _sceneResourceLayout = null;
        _boundSceneDataBuffer = null;
        _boundPointLightBuffer = null;
        _boundShadowMapView = null;
        _boundShadowMapSampler = null;
        _albedoTextureView = null;
        _albedoSampler = null;
        _vertexBuffer = null;
        _indexBuffer = null;
        _meshUniformBuffer = null;
        _materialUniformBuffer = null;
        _placeholderShadowView = null;
        _placeholderShadowTexture = null;
        _placeholderShadowSampler = null;
        _shadowPipeline = null;
        _shadowResourceSet = null;
        _shadowResourceLayout = null;
        _shadowUniformBuffer = null;
        _shadowCasterSampler = null;
        _shadowOutputDescription = null;
        _shadowResourceSetAlbedoTexture = null;
        _pointShadowResourceSet = null;
        _pointShadowResourceLayout = null;
        _placeholderCubeView = null;
        _placeholderCubeTexture = null;
        _placeholderCubeSampler = null;
        Array.Clear(_boundPointShadowMaps);
        _pointShadowCasterPipeline = null;
        _pointShadowCasterResourceSet = null;
        _pointShadowCasterResourceLayout = null;
        _pointShadowUniformBuffer = null;
        _pointShadowCasterSampler = null;
        _pointShadowCasterOutputDescription = null;
        _pointShadowResourceSetAlbedoTexture = null;
        _pickColorPipeline = null;
        _silhouettePipeline = null;
        _pickResourceSet = null;
        _pickResourceLayout = null;
        _pickVertexUniformBuffer = null;
        _pickMaterialUniformBuffer = null;
        _pickSampler = null;
        _pickColorOutputDescription = null;
        _silhouetteOutputDescription = null;
        _pickResourceSetAlbedoTexture = null;
    }

    public void Dispose() => DisposeGpuResources();
}

internal static class VertexSizeHelper
{
    public static int SizeInBytes() => sizeof(float) * (3 + 3 + 2);
}
