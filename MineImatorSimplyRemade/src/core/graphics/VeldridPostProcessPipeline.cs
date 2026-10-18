using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using GlmSharp;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

[StructLayout(LayoutKind.Sequential)]
public struct VeldridPostProcessParams
{
    public float Exposure;
    public float Gamma;
    public float VignetteIntensity;
    public float VignetteRoundness;

    public vec4 ColorFilter; // RGB multiplier + blend factor in W
    public vec4 ToneMapParams; // x: ToneMapMode (0=None, 1=Reinhard, 2=ACES), y: Contrast, z: Saturation, w: Brightness
    public vec4 BloomParams; // x: BloomIntensity, y: BloomThreshold, z: BlurRadius, w: Reserved

    public static VeldridPostProcessParams Default => new()
    {
        Exposure = 1.0f,
        Gamma = 2.2f,
        VignetteIntensity = 0.0f,
        VignetteRoundness = 0.5f,
        ColorFilter = new vec4(1f, 1f, 1f, 0f),
        ToneMapParams = new vec4(1f, 1.0f, 1.0f, 1.0f), // ACES/Reinhard default
        BloomParams = new vec4(0.0f, 1.0f, 1.0f, 0.0f)
    };
}

public class VeldridPostProcessPipeline : IDisposable
{
    private readonly GraphicsDevice _gd;

    public DeviceBuffer QuadVertexBuffer { get; private set; }
    public DeviceBuffer QuadIndexBuffer { get; private set; }
    public DeviceBuffer ParamsBuffer { get; private set; }

    public ResourceLayout TextureLayout { get; private set; }
    public ResourceLayout ParamsLayout { get; private set; }
    public Sampler PointSampler { get; private set; }
    public Sampler LinearSampler { get; private set; }

    private Shader[]? _shaders;
    private Pipeline? _pipeline;
    private ResourceSet? _paramsResourceSet;

    public VeldridPostProcessParams Parameters = VeldridPostProcessParams.Default;

    // Standard Fullscreen Triangle / Quad Vertices: Position (XY), UV (ZW)
    private static readonly float[] QuadVertices =
    [
        -1.0f, -1.0f, 0.0f, 1.0f,
         1.0f, -1.0f, 1.0f, 1.0f,
         1.0f,  1.0f, 1.0f, 0.0f,
        -1.0f,  1.0f, 0.0f, 0.0f,
    ];

    private static readonly ushort[] QuadIndices = [0, 1, 2, 0, 2, 3];

    private const string VertexShaderSource = @"#version 450
layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec2 inTexCoord;

layout(location = 0) out vec2 fragTexCoord;

void main()
{
    fragTexCoord = inTexCoord;
    gl_Position = vec4(inPosition, 0.0, 1.0);
}
";

    private const string FragmentShaderSource = @"#version 450
layout(location = 0) in vec2 fragTexCoord;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform texture2D InputTexture;
layout(set = 0, binding = 1) uniform sampler InputSampler;

layout(set = 1, binding = 0) uniform PostProcessParams
{
    float Exposure;
    float Gamma;
    float VignetteIntensity;
    float VignetteRoundness;
    vec4 ColorFilter;
    vec4 ToneMapParams; // x: mode, y: contrast, z: saturation, w: brightness
    vec4 BloomParams;
};

vec3 ReinhardToneMap(vec3 color)
{
    return color / (color + vec3(1.0));
}

vec3 AcesToneMap(vec3 x)
{
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main()
{
    vec4 texColor = texture(sampler2D(InputTexture, InputSampler), fragTexCoord);
    vec3 color = texColor.rgb * Exposure;

    // Tone Mapping
    int toneMapMode = int(ToneMapParams.x);
    if (toneMapMode == 1)
    {
        color = ReinhardToneMap(color);
    }
    else if (toneMapMode == 2)
    {
        color = AcesToneMap(color);
    }

    // Color balance / Contrast / Brightness / Saturation
    float contrast = ToneMapParams.y;
    float saturation = ToneMapParams.z;
    float brightness = ToneMapParams.w;

    color = (color - 0.5) * contrast + 0.5;
    color = color * brightness;

    float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(vec3(luminance), color, saturation);

    // Color filter / tint
    if (ColorFilter.w > 0.0)
    {
        color = mix(color, color * ColorFilter.rgb, ColorFilter.w);
    }

    // Vignette
    if (VignetteIntensity > 0.0)
    {
        vec2 uv = fragTexCoord - 0.5;
        float dist = length(uv);
        float vignette = smoothstep(0.8, VignetteRoundness * 0.799, dist * (1.0 + VignetteIntensity));
        color *= vignette;
    }

    // Gamma correction
    if (Gamma > 0.0)
    {
        color = pow(max(color, vec3(0.0)), vec3(1.0 / Gamma));
    }

    outColor = vec4(color, texColor.a);
}
";

    public VeldridPostProcessPipeline(GraphicsDevice gd)
    {
        _gd = gd;

        // Buffers
        QuadVertexBuffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(
            (uint)(QuadVertices.Length * sizeof(float)),
            BufferUsage.VertexBuffer));
        _gd.UpdateBuffer(QuadVertexBuffer, 0, QuadVertices);

        QuadIndexBuffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(
            (uint)(QuadIndices.Length * sizeof(ushort)),
            BufferUsage.IndexBuffer));
        _gd.UpdateBuffer(QuadIndexBuffer, 0, QuadIndices);

        uint paramsSize = (uint)Marshal.SizeOf<VeldridPostProcessParams>();
        // Ensure 16-byte alignment
        paramsSize = (paramsSize + 15) & ~15u;
        ParamsBuffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(
            paramsSize,
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        // Samplers
        PointSampler = _gd.PointSampler;
        LinearSampler = _gd.LinearSampler;

        // Resource layouts
        TextureLayout = _gd.ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("InputTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("InputSampler", ResourceKind.Sampler, ShaderStages.Fragment)
        ));

        ParamsLayout = _gd.ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("PostProcessParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ));

        _paramsResourceSet = _gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            ParamsLayout,
            ParamsBuffer
        ));

        InitializePipeline();
    }

    private void InitializePipeline()
    {
        _shaders = VeldridShader.CompileFromGlsl(
            _gd,
            VertexShaderSource,
            FragmentShaderSource
        );

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("inPosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("inTexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
        );

        var shaderSetDesc = new ShaderSetDescription(
            [vertexLayout],
            _shaders
        );

        var pipelineDesc = new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            shaderSetDesc,
            [TextureLayout, ParamsLayout],
            _gd.MainSwapchain.Framebuffer.OutputDescription
        );

        _pipeline = _gd.ResourceFactory.CreateGraphicsPipeline(pipelineDesc);
    }

    public ResourceSet CreateTextureResourceSet(TextureView sourceTextureView, Sampler? sampler = null)
    {
        return _gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            TextureLayout,
            sourceTextureView,
            sampler ?? LinearSampler
        ));
    }

    public void UpdateParameters(VeldridPostProcessParams? newParams = null)
    {
        if (newParams.HasValue)
        {
            Parameters = newParams.Value;
        }

        _gd.UpdateBuffer(ParamsBuffer, 0, Parameters);
    }

    public void Render(
        CommandList cl,
        Framebuffer targetFramebuffer,
        ResourceSet sourceTextureResourceSet)
    {
        if (_pipeline == null || _paramsResourceSet == null)
            return;

        UpdateParameters();

        cl.SetFramebuffer(targetFramebuffer);
        cl.SetFullViewport(0);
        cl.SetFullScissorRect(0);

        cl.SetPipeline(_pipeline);
        cl.SetVertexBuffer(0, QuadVertexBuffer);
        cl.SetIndexBuffer(QuadIndexBuffer, IndexFormat.UInt16);

        cl.SetGraphicsResourceSet(0, sourceTextureResourceSet);
        cl.SetGraphicsResourceSet(1, _paramsResourceSet);

        cl.DrawIndexed(6, 1, 0, 0, 0);
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _paramsResourceSet?.Dispose();
        if (_shaders != null)
        {
            foreach (var s in _shaders) s.Dispose();
            _shaders = null;
        }
        ParamsLayout?.Dispose();
        TextureLayout?.Dispose();
        ParamsBuffer?.Dispose();
        QuadIndexBuffer?.Dispose();
        QuadVertexBuffer?.Dispose();
    }
}
