using System.Numerics;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Replacement for <c>core.mdl.Mesh</c>'s GPU-resident triangle mesh, targeting
/// Veldrid instead of Silk.NET.OpenGL.
///
/// MIGRATION STATUS - subsystem pass 6/N ("skinning + shape keys"): geometry
/// upload, full forward lighting (point lights, directional + point-light
/// shadows, SSS, fog), skinning (bone indices/weights, up to 64 bones shared
/// across every pipeline this mesh draws through), and shape keys/morph
/// targets (CPU-side deformation + smooth-normal recompute, matching
/// <c>core.mdl.Mesh</c>'s <c>RefreshShapeKeyGeometry</c>) are all ported.
/// Still NOT ported (each is its own follow-up subsystem pass):
///   - per-instance matrices (needs a second, instance-rate vertex buffer that
///     doesn't exist yet; <c>MeshUniforms.UseInstancing</c> always reads 0)
///   - animated texture atlas sampling (depends on the not-yet-ported
///     TerrainAtlas/ItemsAtlas texture-loading system, which still uses
///     Silk.NET.OpenGL)
/// See <c>core.mdl.Mesh</c> (the old GL version, still present elsewhere in the
/// codebase until every caller is migrated) for the full feature set each of
/// those passes needs to restore.
/// </summary>
public class VeldridMesh : IDisposable
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

    private bool _doubleSided;

    /// <summary>When true, disables all face culling for this mesh's pipeline.</summary>
    public bool DoubleSided
    {
        get => _doubleSided;
        set
        {
            if (_doubleSided == value) return;
            _doubleSided = value;
            RebuildRasterizerPipeline();
        }
    }

    private FaceCullMode CullMode => DoubleSided
        ? FaceCullMode.None
        : CullFrontFaces ? FaceCullMode.Front : FaceCullMode.Back;

    /// <summary>Bound texture, or null to render with the flat <see cref="Albedo"/> color.</summary>
    public Texture? AlbedoTexture { get; set; }
    private TextureView? _albedoTextureView;
    private Sampler? _albedoSampler;

    // ── Material data parity with the old GL core.mdl.Mesh ──────────────────
    // These mirror the loose appearance/material fields the old renderer stored
    // (there backed by a StandardMaterial). Added ahead of migrating
    // SceneObject.Visuals / the loaders onto VeldridMesh so those call sites
    // can assign the same values. Types use System.Numerics to match the rest
    // of this class; several are not yet consumed by the render passes above
    // (follow-up subsystem passes) but exist for source-compatibility.

    /// <summary>Multiplicative blend colour (RGBA). Default opaque white = no tint.</summary>
    public Vector4 BlendColor { get; set; } = Vector4.One;

    /// <summary>Mix/overlay colour (RGBA). Alpha is the mix amount.</summary>
    public Vector4 MixColor { get; set; } = Vector4.Zero;

    /// <summary>Metallic response used by the lit material shader.</summary>
    public float Metallic { get; set; }

    /// <summary>Surface roughness used to widen or tighten specular highlights.</summary>
    public float Roughness { get; set; } = 0.5f;

    /// <summary>UV offset applied when sampling the albedo texture.</summary>
    public Vector2 TextureOffset { get; set; } = Vector2.Zero;

    /// <summary>UV repeat/tiling factor. Clamped to a small positive minimum.</summary>
    private Vector2 _textureRepeat = Vector2.One;
    public Vector2 TextureRepeat
    {
        get => _textureRepeat;
        set => _textureRepeat = new Vector2(Math.Max(0.0001f, value.X), Math.Max(0.0001f, value.Y));
    }

    /// <summary>Per-axis UV mirroring (X = U, Y = V). Old renderer's <c>bvec2</c>.</summary>
    public (bool X, bool Y) TextureMirror { get; set; }

    /// <summary>When true, emissive lighting from this mesh is treated as indirect-only.</summary>
    public bool EmissionIndirectOnly { get; set; }

    /// <summary>Per-mesh auto-emission level (0..15) inferred from Minecraft block data.</summary>
    public byte AutoEmissionLevel { get; set; }

    /// <summary>Legacy GL texture handle carried over from the old renderer.
    /// The Veldrid render path uses <see cref="AlbedoTexture"/> instead; kept for
    /// source-compatibility with callers still tracking a numeric id.</summary>
    public uint TextureId { get; set; }

    /// <summary>Optional independent alpha-mask texture (textured flat text).</summary>
    public Texture? AlphaMaskTexture { get; set; }

    /// <summary>True when this mesh is a textured flat-text alpha mask.</summary>
    public bool IsTextAlphaMask { get; set; }

    /// <summary>Outline colour used by textured flat-text alpha masks.</summary>
    public Vector4 TextMaskOutlineColor { get; set; } = new(0f, 0f, 0f, 1f);

    /// <summary>Key into the animated-texture atlas. Empty = static texture.</summary>
    public string AnimationKey { get; set; } = "";

    /// <summary>True when the mesh is genuinely alpha-blended (e.g. water) rather
    /// than merely alpha-cutout. Drives the viewport's translucent render pass.</summary>
    public bool IsTranslucent { get; set; }

    /// <summary>Render ordering hint for coplanar layered meshes. Lower renders first.</summary>
    public float SortDepth { get; set; }

    /// <summary>When true, the mesh is only used for editor helper passes
    /// (colour picking / silhouette) and excluded from normal scene rendering.</summary>
    public bool PickOnly { get; set; }

    /// <summary>When true, depth testing/writes are disabled so the mesh renders on top.</summary>
    public bool DepthTestDisabled { get; set; }

    private bool _blurTexture;
    /// <summary>When true this mesh uses linear texture filtering; else nearest.</summary>
    public bool BlurTexture
    {
        get => _blurTexture;
        set
        {
            if (_blurTexture == value) return;
            _blurTexture = value;
            RebuildAlbedoSampler();
        }
    }

    private bool _textureMipmaps;
    /// <summary>When true this mesh samples with mipmap minification filters.</summary>
    public bool TextureMipmaps
    {
        get => _textureMipmaps;
        set
        {
            if (_textureMipmaps == value) return;
            _textureMipmaps = value;
            RebuildAlbedoSampler();
        }
    }

    private bool _cullFrontFaces;

    /// <summary>When true, culls front faces so only backfaces render.</summary>
    public bool CullFrontFaces
    {
        get => _cullFrontFaces;
        set
        {
            if (_cullFrontFaces == value) return;
            _cullFrontFaces = value;
            RebuildRasterizerPipeline();
        }
    }

    // ── Skinning (subsystem pass 6/N) ───────────────────────────────────────

    private const int MaxBones = 64;

    /// <summary>Per-vertex bone indices (up to 4 per vertex), parallel to <see cref="Vertices"/>.</summary>
    public List<(int X, int Y, int Z, int W)> BoneIndices { get; } = new();

    /// <summary>Per-vertex bone weights (up to 4 per vertex), parallel to <see cref="Vertices"/>.</summary>
    public List<Vector4> BoneWeights { get; } = new();

    /// <summary>Bone names used by this mesh, indexed by <see cref="BoneIndices"/>.</summary>
    public List<string> BoneNames { get; } = new();

    /// <summary>Inverse bind matrices for each bone in <see cref="BoneNames"/>
    /// (mesh space -> bone space in the bind pose).</summary>
    public List<Matrix4x4> BoneInverseBindMatrices { get; } = new();

    /// <summary>Local-space bounding sphere centre (computed during <see cref="Upload"/>).</summary>
    public Vector3 BoundingSphereCenter { get; set; }

    /// <summary>Local-space bounding sphere radius (computed during <see cref="Upload"/>).</summary>
    public float BoundingSphereRadius { get; set; }

    /// <summary>True when this mesh has skinning data uploaded and should be GPU-deformed.</summary>
    public bool IsSkinned => BoneIndices.Count > 0 && BoneIndices.Count == Vertices.Count;

    /// <summary>Current bone matrices, uploaded to the shared bone buffer each
    /// <see cref="Render"/>/<see cref="RenderDepthOnly"/>/etc. call. Up to <see cref="MaxBones"/>
    /// are used; extras are ignored (matches the old renderer's fixed-size array).</summary>
    public List<Matrix4x4>? BoneMatrices { get; set; }

    private DeviceBuffer? _boneMatrixBuffer;
    private readonly Matrix4x4[] _boneMatrixScratch = new Matrix4x4[MaxBones];

    // ── Shape keys / morph targets (subsystem pass 6/N) ─────────────────────

    public sealed class ShapeKey
    {
        public string Name = "";
        public float[] Deltas = Array.Empty<float>(); // length == Vertices.Count * 3
        public float Weight;
    }

    public List<ShapeKey> ShapeKeys { get; } = new();
    public bool HasShapeKeys => ShapeKeys.Count > 0;
    private bool _shapeKeyDirty;
    private float[]? _baseVertexFloats;
    private float[]? _deformedVertexFloats;
    private const int FloatsPerVertex = 8; // px py pz nx ny nz u v (bone data uploaded separately, unaffected by shape keys)

    public void AddShapeKey(string name, float[] deltas)
    {
        if (deltas.Length != Vertices.Count * 3) return;
        ShapeKeys.Add(new ShapeKey { Name = name, Deltas = deltas });
        _shapeKeyDirty = true;
    }

    /// <summary>Sets a shape key's weight in [-1, 1] and marks the mesh for a
    /// vertex-buffer refresh on the next <see cref="Render"/> call.</summary>
    public void SetShapeKeyWeight(int index, float weight)
    {
        if (index < 0 || index >= ShapeKeys.Count) return;
        float clamped = Math.Clamp(weight, -1f, 1f);
        if (ShapeKeys[index].Weight == clamped) return;
        ShapeKeys[index].Weight = clamped;
        _shapeKeyDirty = true;
    }

    /// <summary>Recomputes deformed vertex positions from the base geometry plus
    /// weighted shape-key deltas, recomputes smooth normals for the deformed
    /// surface, and re-uploads the vertex buffer. Mirrors <c>core.mdl.Mesh</c>'s
    /// <c>RefreshShapeKeyGeometry</c>/<c>RecomputeShapeKeyNormals</c> - ported
    /// as-is since this is pure CPU-side geometry, no shader changes needed.</summary>
    private void RefreshShapeKeyGeometry()
    {
        if (_vertexBuffer == null || _baseVertexFloats == null || !_shapeKeyDirty || ShapeKeys.Count == 0)
        {
            _shapeKeyDirty = false;
            return;
        }

        int vertexCount = Vertices.Count;
        if (vertexCount == 0) { _shapeKeyDirty = false; return; }

        int totalFloats = vertexCount * FloatsPerVertex;

        bool anyActive = ShapeKeys.Any(sk => sk.Weight != 0f && sk.Deltas.Length >= vertexCount * 3);

        float[] source;
        if (!anyActive)
        {
            source = _baseVertexFloats;
        }
        else
        {
            if (_deformedVertexFloats == null || _deformedVertexFloats.Length != totalFloats)
                _deformedVertexFloats = new float[totalFloats];
            Array.Copy(_baseVertexFloats, _deformedVertexFloats, totalFloats);

            foreach (ShapeKey sk in ShapeKeys)
            {
                if (sk.Weight == 0f || sk.Deltas.Length < vertexCount * 3) continue;
                float w = sk.Weight;
                for (int v = 0; v < vertexCount; v++)
                {
                    int di = v * 3;
                    int vi = v * FloatsPerVertex;
                    _deformedVertexFloats[vi + 0] += w * sk.Deltas[di + 0];
                    _deformedVertexFloats[vi + 1] += w * sk.Deltas[di + 1];
                    _deformedVertexFloats[vi + 2] += w * sk.Deltas[di + 2];
                }
            }

            RecomputeShapeKeyNormals(_deformedVertexFloats, vertexCount);
            source = _deformedVertexFloats;
        }

        // The GPU vertex buffer's stride includes bone fields interleaved with
        // position/normal/uv (see the Vertex struct), so - unlike the old GL
        // Mesh's plain 8-float stride - a full Vertex[] must be rebuilt here
        // rather than writing the position/normal/uv floats contiguously.
        bool hasSkinning = IsSkinned;
        var vertexData = new Vertex[vertexCount];
        for (int v = 0; v < vertexCount; v++)
        {
            int vi = v * FloatsPerVertex;
            (int bx, int by, int bz, int bw) = hasSkinning ? BoneIndices[v] : (0, 0, 0, 0);
            vertexData[v] = new Vertex
            {
                Position = new Vector3(source[vi + 0], source[vi + 1], source[vi + 2]),
                Normal = new Vector3(source[vi + 3], source[vi + 4], source[vi + 5]),
                TexCoord = new Vector2(source[vi + 6], source[vi + 7]),
                BoneIndex0 = bx,
                BoneIndex1 = by,
                BoneIndex2 = bz,
                BoneIndex3 = bw,
                BoneWeight = hasSkinning ? BoneWeights[v] : Vector4.Zero,
            };
        }
        _device.UpdateBuffer(_vertexBuffer, 0, vertexData);
        _shapeKeyDirty = false;
    }

    private void RecomputeShapeKeyNormals(float[] data, int vertexCount)
    {
        var accum = new Vector3[vertexCount];

        void Accumulate(int i0, int i1, int i2)
        {
            int b0 = i0 * FloatsPerVertex, b1 = i1 * FloatsPerVertex, b2 = i2 * FloatsPerVertex;
            var p0 = new Vector3(data[b0], data[b0 + 1], data[b0 + 2]);
            var p1 = new Vector3(data[b1], data[b1 + 1], data[b1 + 2]);
            var p2 = new Vector3(data[b2], data[b2 + 1], data[b2 + 2]);
            Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0);
            accum[i0] += faceNormal;
            accum[i1] += faceNormal;
            accum[i2] += faceNormal;
        }

        if (Indices is { Length: >= 3 })
        {
            for (int i = 0; i + 2 < Indices.Length; i += 3)
                Accumulate((int)Indices[i], (int)Indices[i + 1], (int)Indices[i + 2]);
        }
        else
        {
            for (int i = 0; i + 2 < vertexCount; i += 3)
                Accumulate(i, i + 1, i + 2);
        }

        for (int v = 0; v < vertexCount; v++)
        {
            Vector3 n = accum[v];
            if (n.LengthSquared() <= 1e-12f) continue;
            Vector3 norm = Vector3.Normalize(n);
            int vi = v * FloatsPerVertex;
            data[vi + 3] = norm.X;
            data[vi + 4] = norm.Y;
            data[vi + 5] = norm.Z;
        }
    }

    private struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;
        public int BoneIndex0, BoneIndex1, BoneIndex2, BoneIndex3;
        public Vector4 BoneWeight;
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
        bool hasSkinning = IsSkinned;

        var vertexData = new Vertex[Vertices.Count];
        for (int i = 0; i < Vertices.Count; i++)
        {
            (int bx, int by, int bz, int bw) = hasSkinning ? BoneIndices[i] : (0, 0, 0, 0);
            Vector4 weights = hasSkinning ? BoneWeights[i] : Vector4.Zero;
            vertexData[i] = new Vertex
            {
                Position = Vertices[i],
                Normal = hasNormals ? Normals[i] : Vector3.UnitY,
                TexCoord = hasUVs ? TexCoords[i] : Vector2.Zero,
                BoneIndex0 = bx,
                BoneIndex1 = by,
                BoneIndex2 = bz,
                BoneIndex3 = bw,
                BoneWeight = weights,
            };
        }

        ResourceFactory factory = _device.ResourceFactory;

        _vertexCount = (uint)vertexData.Length;
        _vertexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(vertexData.Length * VertexSizeHelper.SizeInBytes()), BufferUsage.VertexBuffer));
        _device.UpdateBuffer(_vertexBuffer, 0, vertexData);

        // Cache the interleaved [px py pz nx ny nz u v] floats (ignoring the
        // bone fields, which shape keys never touch) so RefreshShapeKeyGeometry
        // can rebuild just that region without re-deriving it from Vertex[].
        if (ShapeKeys.Count > 0)
        {
            _baseVertexFloats = new float[Vertices.Count * FloatsPerVertex];
            for (int i = 0; i < Vertices.Count; i++)
            {
                int vi = i * FloatsPerVertex;
                _baseVertexFloats[vi + 0] = vertexData[i].Position.X;
                _baseVertexFloats[vi + 1] = vertexData[i].Position.Y;
                _baseVertexFloats[vi + 2] = vertexData[i].Position.Z;
                _baseVertexFloats[vi + 3] = vertexData[i].Normal.X;
                _baseVertexFloats[vi + 4] = vertexData[i].Normal.Y;
                _baseVertexFloats[vi + 5] = vertexData[i].Normal.Z;
                _baseVertexFloats[vi + 6] = vertexData[i].TexCoord.X;
                _baseVertexFloats[vi + 7] = vertexData[i].TexCoord.Y;
            }
            _shapeKeyDirty = ShapeKeys.Any(sk => sk.Weight != 0f);
        }
        else
        {
            _baseVertexFloats = null;
        }

        _boneMatrixBuffer ??= factory.CreateBuffer(new BufferDescription(MaxBones * 64u, BufferUsage.UniformBuffer));

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

        _albedoSampler = VeldridTextureLoader.CreateSampler(
            nearest: !BlurTexture, repeat: true, mipmaps: TextureMipmaps, device: _device);

        BuildPipelineAndResources();
    }

    private static uint AlignTo16(uint size) => (size + 15) / 16 * 16;

    private void RebuildAlbedoSampler()
    {
        if (_meshResourceLayout == null)
            return;

        _albedoSampler?.Dispose();
        _albedoSampler = VeldridTextureLoader.CreateSampler(
            nearest: !BlurTexture, repeat: true, mipmaps: TextureMipmaps, device: _device);
        RebuildResourceSet();
    }

    private void RebuildRasterizerPipeline()
    {
        if (_pipeline == null || _meshResourceLayout == null || _sceneResourceLayout == null)
            return;

        _pipeline.Dispose();
        var (vertexShader, fragmentShader) = VeldridShaderCache.GetOrCompile(_device, "simple.vert", "simple.frag");
        _pipeline = _device.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                CullMode, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _meshResourceLayout, _sceneResourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { MakeStandardVertexLayout() }, new[] { vertexShader, fragmentShader }),
            Outputs = _outputDescription,
        });
    }

    /// <summary>The standard 5-attribute vertex layout (position/normal/uv/
    /// bone-indices/bone-weights) shared by every pipeline this mesh draws
    /// through (main color, shadow depth, point-shadow depth, pick/silhouette) -
    /// they all bind the same <see cref="_vertexBuffer"/>, so their vertex
    /// shaders must all declare the same 5 inputs (even where unused) for
    /// Veldrid's ordinal attribute matching to line up. See each shader's
    /// migration notes for why unused attributes are still declared there.</summary>
    private static VertexLayoutDescription MakeStandardVertexLayout() => new(
        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
        new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
        new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
        new VertexElementDescription("BoneIndices", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Int4),
        new VertexElementDescription("BoneWeights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

    private void BuildPipelineAndResources()
    {
        ResourceFactory factory = _device.ResourceFactory;
        var (vertexShader, fragmentShader) = VeldridShaderCache.GetOrCompile(_device, "simple.vert", "simple.frag");

        // Matches simple.frag's `set = 0` bindings: binding 0 = MeshUniforms,
        // binding 1 = uTextureSampler, binding 2 = MeshMaterial, binding 3 =
        // BoneMatrices (vertex only). Order here must match the shader's
        // declaration order exactly.
        _meshResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("MeshUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uTextureSampler", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("uTextureSamplerState", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MeshMaterial", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("BoneMatrices", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

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

        var vertexLayout = MakeStandardVertexLayout();

        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                CullMode, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
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
            _meshResourceLayout, _meshUniformBuffer, view, _albedoSampler, _materialUniformBuffer, _boneMatrixBuffer));
        _meshResourceSetAlbedoTexture = AlbedoTexture;
    }

    private Texture? _meshResourceSetAlbedoTexture;

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

    /// <summary>Uploads <see cref="BoneMatrices"/> (padded with identity up to
    /// <see cref="MaxBones"/>) to the shared bone-matrix buffer used by every
    /// pipeline this mesh draws through. No-op when not skinned.</summary>
    private void UploadBoneMatrices(CommandList commandList)
    {
        if (_boneMatrixBuffer == null || !IsSkinned)
            return;

        int count = Math.Min(BoneMatrices?.Count ?? 0, MaxBones);
        for (int i = 0; i < count; i++)
            _boneMatrixScratch[i] = BoneMatrices![i];
        for (int i = count; i < MaxBones; i++)
            _boneMatrixScratch[i] = Matrix4x4.Identity;

        commandList.UpdateBuffer(_boneMatrixBuffer, 0, _boneMatrixScratch);
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
    /// <param name="shadowMap">
    /// The directional shadow map to sample, or null to render fully unshadowed
    /// (matches <c>SceneDataUniforms.UseShadowMap == 0</c> - the shader still
    /// needs *some* bound texture/sampler even when unused, so a 1x1 placeholder
    /// is bound automatically when this is null).
    /// </param>
    /// <param name="environmentBuffer">Typically <c>VeldridBitmapRenderSurface.EnvironmentBuffer</c>.</param>
    public void Render(CommandList commandList, Matrix4x4 model, Matrix4x4 view, Matrix4x4 proj,
        DeviceBuffer sceneDataBuffer, DeviceBuffer pointLightBuffer, DeviceBuffer environmentBuffer,
        VeldridShadowMap? shadowMap = null, IReadOnlyList<VeldridPointShadowMap?>? pointShadowMaps = null,
        bool forceUnlit = false)
    {
        if (_pipeline == null || _vertexBuffer == null || _meshUniformBuffer == null || _materialUniformBuffer == null)
            return;

        if (_meshResourceSetAlbedoTexture != AlbedoTexture)
            RebuildResourceSet();
        if (_meshResourceSet == null)
            return;

        if (HasShapeKeys)
            RefreshShapeKeyGeometry();
        UploadBoneMatrices(commandList);

        TextureView shadowView = shadowMap?.DepthTextureView ?? GetOrCreatePlaceholderShadowView();
        Sampler shadowSampler = shadowMap?.DepthSampler ?? GetOrCreatePlaceholderShadowSampler();
        EnsureSceneResourceSet(sceneDataBuffer, pointLightBuffer, shadowView, shadowSampler, environmentBuffer);
        var meshUniforms = MeshUniforms.Default;
        meshUniforms.Model = model;
        meshUniforms.MVP = model * view * proj;
        meshUniforms.IsSkinned = IsSkinned ? 1 : 0;
        commandList.UpdateBuffer(_meshUniformBuffer, 0, ref meshUniforms);

        var materialUniforms = MeshMaterialUniforms.Default;
        materialUniforms.Albedo = Albedo;
        materialUniforms.Alpha = Alpha;
        materialUniforms.BlendColor = BlendColor;
        materialUniforms.MixColor = MixColor;
        materialUniforms.UseTexture = AlbedoTexture != null ? 1 : 0;
        materialUniforms.IsUnlit = Unlit || forceUnlit ? 1 : 0;
        materialUniforms.EmissionEnabled = EmissionEnabled ? 1 : 0;
        materialUniforms.EmissionColor = EmissionColor;
        materialUniforms.EmissionEnergy = EmissionEnergy;
        materialUniforms.Subsurface = Subsurface;
        materialUniforms.SubsurfaceRadius = SubsurfaceRadius;
        materialUniforms.SubsurfaceColor = SubsurfaceColor;
        materialUniforms.SubsurfaceHighlight = SubsurfaceHighlight;
        materialUniforms.SubsurfaceHighlightStrength = SubsurfaceHighlightStrength;
        materialUniforms.Metallic = Metallic;
        materialUniforms.Roughness = Roughness;
        materialUniforms.IncludeInFog = IncludeInFog ? 1 : 0;
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

        if (HasShapeKeys)
            RefreshShapeKeyGeometry();
        UploadBoneMatrices(commandList);

        var uniforms = ShadowDepthUniforms.Default;
        uniforms.MVP = lightMvp;
        uniforms.Alpha = Alpha;
        uniforms.UseTexture = AlbedoTexture != null ? 1 : 0;
        uniforms.IsSkinned = IsSkinned ? 1 : 0;
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

        if (HasShapeKeys)
            RefreshShapeKeyGeometry();
        UploadBoneMatrices(commandList);

        var uniforms = PointShadowDepthUniforms.Default;
        uniforms.LightViewProj = faceViewProj;
        uniforms.Model = model;
        uniforms.Alpha = Alpha;
        uniforms.UseTexture = AlbedoTexture != null ? 1 : 0;
        uniforms.IsSkinned = IsSkinned ? 1 : 0;
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
            new ResourceLayoutElementDescription("uTextureSamplerState", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("BoneMatrices", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _pointShadowUniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            AlignTo16((uint)System.Runtime.InteropServices.Marshal.SizeOf<PointShadowDepthUniforms>()),
            BufferUsage.UniformBuffer));

        _pointShadowCasterSampler ??= factory.CreateSampler(SamplerDescription.Linear);

        var vertexLayout = MakeStandardVertexLayout();

        _pointShadowCasterPipeline?.Dispose();
        _pointShadowCasterPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleDisabled,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                CullMode, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
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
            _pointShadowCasterResourceLayout, _pointShadowUniformBuffer, view, _pointShadowCasterSampler, _boneMatrixBuffer));
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

        if (HasShapeKeys)
            RefreshShapeKeyGeometry();
        UploadBoneMatrices(commandList);

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

        if (HasShapeKeys)
            RefreshShapeKeyGeometry();
        UploadBoneMatrices(commandList);

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
        vertexUniforms.IsSkinned = IsSkinned ? 1 : 0;
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
            new ResourceLayoutElementDescription("uAlphaMaskSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("BoneMatrices", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _pickVertexUniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            AlignTo16((uint)System.Runtime.InteropServices.Marshal.SizeOf<PickVertexUniforms>()), BufferUsage.UniformBuffer));
        _pickMaterialUniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            AlignTo16((uint)System.Runtime.InteropServices.Marshal.SizeOf<PickMaterialUniforms>()), BufferUsage.UniformBuffer));
        _pickSampler ??= factory.CreateSampler(SamplerDescription.Linear);

        var vertexLayout = MakeStandardVertexLayout();

        bool rebuildResourceSet = _pickResourceSet == null || _pickResourceSetAlbedoTexture != AlbedoTexture;

        if (pickColorOutput != null && (_pickColorPipeline == null || _pickColorOutputDescription == null || !_pickColorOutputDescription.Value.Equals(pickColorOutput.Value)))
        {
            var (vs, fs) = VeldridShaderCache.GetOrCompile(_device, "pick.vert", "pick.frag");
            _pickColorPipeline?.Dispose();
            _pickColorPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleDisabled,
                DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
                RasterizerState = new RasterizerStateDescription(CullMode, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
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
                RasterizerState = new RasterizerStateDescription(CullMode, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
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
                _pickResourceLayout, _pickVertexUniformBuffer, _pickMaterialUniformBuffer, view, _pickSampler, view, _pickSampler, _boneMatrixBuffer));
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
            new ResourceLayoutElementDescription("uTextureSamplerState", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("BoneMatrices", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _shadowUniformBuffer ??= factory.CreateBuffer(new BufferDescription(
            AlignTo16((uint)System.Runtime.InteropServices.Marshal.SizeOf<ShadowDepthUniforms>()),
            BufferUsage.UniformBuffer));

        _shadowCasterSampler ??= factory.CreateSampler(SamplerDescription.Linear);

        // Same vertex layout as the main pipeline - shadow_depth.vert declares
        // the same 5 interleaved attributes even though it only reads
        // position+uv+bone data, so this reuses the same vertex buffer.
        var vertexLayout = MakeStandardVertexLayout();

        _shadowPipeline?.Dispose();
        _shadowPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleDisabled,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                CullMode, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
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
            _shadowResourceLayout, _shadowUniformBuffer, view, _shadowCasterSampler, _boneMatrixBuffer));
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
        _boneMatrixBuffer?.Dispose();

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
        _boneMatrixBuffer = null;
        _baseVertexFloats = null;
        _deformedVertexFloats = null;
    }

    public void Dispose() => DisposeGpuResources();
}

internal static class VertexSizeHelper
{
    // Position(3) + Normal(3) + TexCoord(2) floats, plus BoneIndices(4 ints) + BoneWeights(4 floats).
    public static int SizeInBytes() => sizeof(float) * (3 + 3 + 2 + 4) + sizeof(int) * 4;
}
