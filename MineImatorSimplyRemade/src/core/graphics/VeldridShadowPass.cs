using System;
using System.Numerics;
using System.Runtime.InteropServices;
using GlmSharp;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

[StructLayout(LayoutKind.Sequential)]
public struct ShadowPassUniformData
{
    public mat4 LightSpaceMVP;
    public vec2 TexOffset;
    public float TexScaleV;
    public float AlphaCutoff;
    public vec2 TexUvOffset;
    public vec2 TexUvRepeat;
    public vec2 TexUvMirror;
    public uint IsSkinned;
    public float Pad0;
}

public class VeldridShadowMap : IDisposable
{
    private readonly GraphicsDevice _gd;

    public uint Resolution { get; private set; }
    public Texture DepthTexture { get; private set; }
    public TextureView DepthTextureView { get; private set; }
    public Framebuffer Framebuffer { get; private set; }
    public Sampler ComparisonSampler { get; private set; }
    public Sampler StandardSampler { get; private set; }

    public PixelFormat DepthFormat { get; }

    public VeldridShadowMap(
        GraphicsDevice gd,
        uint resolution = 2048,
        PixelFormat depthFormat = PixelFormat.D32_Float_S8_UInt)
    {
        _gd = gd;
        Resolution = Math.Max(128, resolution);
        DepthFormat = depthFormat;

        // Create Depth Texture with DepthStencil + Sampled usage
        var depthDesc = new TextureDescription(
            Resolution,
            Resolution,
            1,
            1,
            1,
            DepthFormat,
            TextureUsage.DepthStencil | TextureUsage.Sampled,
            TextureType.Texture2D);

        DepthTexture = _gd.ResourceFactory.CreateTexture(depthDesc);
        DepthTextureView = _gd.ResourceFactory.CreateTextureView(DepthTexture);

        // Framebuffer with only depth attachment
        var fbDesc = new FramebufferDescription(
            new FramebufferAttachmentDescription(DepthTexture, 0),
            Array.Empty<FramebufferAttachmentDescription>());
        Framebuffer = _gd.ResourceFactory.CreateFramebuffer(fbDesc);

        // Hardware shadow comparison sampler (PCF hardware support)
        ComparisonSampler = _gd.ResourceFactory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear,
            ComparisonKind.LessEqual,
            0,
            0,
            0,
            0,
            SamplerBorderColor.OpaqueWhite));

        // Standard point/linear depth sampler
        StandardSampler = _gd.ResourceFactory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear,
            null,
            0,
            0,
            0,
            0,
            SamplerBorderColor.OpaqueWhite));
    }

    public void Resize(uint newResolution)
    {
        if (newResolution == Resolution)
            return;

        Resolution = Math.Max(128, newResolution);

        Framebuffer.Dispose();
        DepthTextureView.Dispose();
        DepthTexture.Dispose();

        var depthDesc = new TextureDescription(
            Resolution,
            Resolution,
            1,
            1,
            1,
            DepthFormat,
            TextureUsage.DepthStencil | TextureUsage.Sampled,
            TextureType.Texture2D);

        DepthTexture = _gd.ResourceFactory.CreateTexture(depthDesc);
        DepthTextureView = _gd.ResourceFactory.CreateTextureView(DepthTexture);

        var fbDesc = new FramebufferDescription(
            new FramebufferAttachmentDescription(DepthTexture, 0),
            Array.Empty<FramebufferAttachmentDescription>());
        Framebuffer = _gd.ResourceFactory.CreateFramebuffer(fbDesc);
    }

    public void Dispose()
    {
        ComparisonSampler.Dispose();
        StandardSampler.Dispose();
        Framebuffer.Dispose();
        DepthTextureView.Dispose();
        DepthTexture.Dispose();
    }
}

public class VeldridShadowPass : IDisposable
{
    private readonly GraphicsDevice _gd;

    public VeldridShadowMap ShadowMap { get; }
    public mat4 LightSpaceMatrix { get; private set; } = mat4.Identity;

    public DeviceBuffer ShadowUniformBuffer { get; private set; }
    public ResourceLayout ShadowUniformLayout { get; private set; }
    public ResourceLayout ShadowTextureLayout { get; private set; }
    public ResourceSet ShadowUniformResourceSet { get; private set; }

    private Shader[]? _shaders;
    private Pipeline? _opaquePipeline;
    private Pipeline? _alphaTestPipeline;

    private const string ShadowVertexShaderSource = @"#version 450
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inTexCoord;
layout(location = 3) in uvec4 inBoneIndices;
layout(location = 4) in vec4 inBoneWeights;

layout(set = 0, binding = 0) uniform ShadowUniforms
{
    mat4 LightSpaceMVP;
    vec2 TexOffset;
    float TexScaleV;
    float AlphaCutoff;
    vec2 TexUvOffset;
    vec2 TexUvRepeat;
    vec2 TexUvMirror;
    uint IsSkinned;
    float Pad0;
};

layout(location = 0) out vec2 fragTexCoord;

vec2 applyUvTransform(vec2 uv)
{
    vec2 repeated = uv * TexUvRepeat;

    if (TexUvMirror.x > 0.5)
        repeated.x = abs(fract(repeated.x * 0.5) * 2.0 - 1.0);

    if (TexUvMirror.y > 0.5)
        repeated.y = abs(fract(repeated.y * 0.5) * 2.0 - 1.0);

    return repeated + TexUvOffset;
}

void main()
{
    vec2 baseUv = vec2(inTexCoord.x, inTexCoord.y * TexScaleV + TexOffset.y);
    fragTexCoord = applyUvTransform(baseUv);

    gl_Position = LightSpaceMVP * vec4(inPosition, 1.0);
}
";

    private const string ShadowFragmentShaderSource = @"#version 450
layout(location = 0) in vec2 fragTexCoord;

layout(set = 0, binding = 0) uniform ShadowUniforms
{
    mat4 LightSpaceMVP;
    vec2 TexOffset;
    float TexScaleV;
    float AlphaCutoff;
    vec2 TexUvOffset;
    vec2 TexUvRepeat;
    vec2 TexUvMirror;
    uint IsSkinned;
    float Pad0;
};

layout(set = 1, binding = 0) uniform texture2D DiffuseTexture;
layout(set = 1, binding = 1) uniform sampler DiffuseSampler;

void main()
{
    if (AlphaCutoff > 0.0)
    {
        vec4 color = texture(sampler2D(DiffuseTexture, DiffuseSampler), fragTexCoord);
        if (color.a < AlphaCutoff)
        {
            discard;
        }
    }
    // Depth is automatically written to gl_FragCoord.z
}
";

    public VeldridShadowPass(GraphicsDevice gd, uint resolution = 2048)
    {
        _gd = gd;
        ShadowMap = new VeldridShadowMap(gd, resolution);

        uint uniformSize = (uint)Marshal.SizeOf<ShadowPassUniformData>();
        uniformSize = (uniformSize + 15) & ~15u;

        ShadowUniformBuffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(
            uniformSize,
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        ShadowUniformLayout = _gd.ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ShadowUniforms", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)
        ));

        ShadowTextureLayout = _gd.ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("DiffuseTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("DiffuseSampler", ResourceKind.Sampler, ShaderStages.Fragment)
        ));

        ShadowUniformResourceSet = _gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            ShadowUniformLayout,
            ShadowUniformBuffer));

        InitializePipelines();
    }

    private void InitializePipelines()
    {
        _shaders = VeldridShader.CompileFromGlsl(
            _gd,
            ShadowVertexShaderSource,
            ShadowFragmentShaderSource);

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("inPosition", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("inNormal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
            new VertexElementDescription("inTexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("inBoneIndices", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4),
            new VertexElementDescription("inBoneWeights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
        );

        var rasterizerState = new RasterizerStateDescription(
            FaceCullMode.Back,
            PolygonFillMode.Solid,
            FrontFace.Clockwise,
            true, // Depth clip enabled
            false // Scissor test disabled
        );

        var depthStencilState = new DepthStencilStateDescription(
            true,  // Depth test enabled
            true,  // Depth write enabled
            ComparisonKind.LessEqual
        );

        var shaderSetDesc = new ShaderSetDescription([vertexLayout], _shaders);

        var opaquePipelineDesc = new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled,
            depthStencilState,
            rasterizerState,
            PrimitiveTopology.TriangleList,
            shaderSetDesc,
            [ShadowUniformLayout, ShadowTextureLayout],
            ShadowMap.Framebuffer.OutputDescription
        );

        _opaquePipeline = _gd.ResourceFactory.CreateGraphicsPipeline(opaquePipelineDesc);

        // Alpha-testing (cutout) pipeline with CullNone
        var alphaTestRasterizer = new RasterizerStateDescription(
            FaceCullMode.None,
            PolygonFillMode.Solid,
            FrontFace.Clockwise,
            true,
            false
        );

        var alphaPipelineDesc = new GraphicsPipelineDescription(
            BlendStateDescription.SingleDisabled,
            depthStencilState,
            alphaTestRasterizer,
            PrimitiveTopology.TriangleList,
            shaderSetDesc,
            [ShadowUniformLayout, ShadowTextureLayout],
            ShadowMap.Framebuffer.OutputDescription
        );

        _alphaTestPipeline = _gd.ResourceFactory.CreateGraphicsPipeline(alphaPipelineDesc);
    }

    /// <summary>
    /// Calculates orthographic light-space matrix focused on a target center point and radius.
    /// </summary>
    public mat4 CalculateDirectionalLightMatrix(vec3 lightDir, vec3 targetCenter, float orthoSize = 40.0f, float nearPlane = 1.0f, float farPlane = 200.0f)
    {
        vec3 lightDirection = lightDir.Normalized;
        vec3 lightPos = targetCenter - lightDirection * (farPlane * 0.5f);

        vec3 up = Math.Abs(lightDirection.y) > 0.99f ? new vec3(0, 0, 1) : new vec3(0, 1, 0);
        mat4 lightView = mat4.LookAt(lightPos, targetCenter, up);

        float halfSize = orthoSize * 0.5f;
        mat4 lightProj = mat4.Ortho(-halfSize, halfSize, -halfSize, halfSize, nearPlane, farPlane);

        LightSpaceMatrix = lightProj * lightView;
        return LightSpaceMatrix;
    }

    /// <summary>
    /// Begins recording shadow depth rendering pass.
    /// </summary>
    public void Begin(CommandList cl)
    {
        cl.SetFramebuffer(ShadowMap.Framebuffer);
        cl.SetFullViewport(0);
        cl.SetFullScissorRect(0);
        cl.ClearDepthStencil(1.0f);
    }

    /// <summary>
    /// Renders a single mesh into the shadow map.
    /// </summary>
    public void RenderMesh(
        CommandList cl,
        VeldridMesh mesh,
        mat4 modelMatrix,
        ResourceSet? materialTextureResourceSet = null,
        bool isAlphaTested = false,
        float alphaCutoff = 0.5f)
    {
        if (_opaquePipeline == null || _alphaTestPipeline == null)
            return;

        var pipeline = isAlphaTested ? _alphaTestPipeline : _opaquePipeline;
        cl.SetPipeline(pipeline);

        mat4 mvp = LightSpaceMatrix * modelMatrix;
        var uniformData = new ShadowPassUniformData
        {
            LightSpaceMVP = mvp,
            TexOffset = vec2.Zero,
            TexScaleV = 1.0f,
            AlphaCutoff = isAlphaTested ? alphaCutoff : 0.0f,
            TexUvOffset = vec2.Zero,
            TexUvRepeat = new vec2(1f, 1f),
            TexUvMirror = vec2.Zero,
            IsSkinned = 0
        };

        cl.UpdateBuffer(ShadowUniformBuffer, 0, ref uniformData);

        cl.SetGraphicsResourceSet(0, ShadowUniformResourceSet);
        if (materialTextureResourceSet != null)
        {
            cl.SetGraphicsResourceSet(1, materialTextureResourceSet);
        }

        mesh.Draw(cl);
    }

    public void Dispose()
    {
        _opaquePipeline?.Dispose();
        _alphaTestPipeline?.Dispose();
        if (_shaders != null)
        {
            foreach (var s in _shaders) s.Dispose();
            _shaders = null;
        }

        ShadowUniformResourceSet.Dispose();
        ShadowTextureLayout.Dispose();
        ShadowUniformLayout.Dispose();
        ShadowUniformBuffer.Dispose();
        ShadowMap.Dispose();
    }
}
