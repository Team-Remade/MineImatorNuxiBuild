using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using GlmSharp;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

[StructLayout(LayoutKind.Sequential)]
public struct GizmoLineVertex
{
    public vec3 Position;
    public vec4 Color;

    public static readonly VertexLayoutDescription LayoutDescription = new(
        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
        new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
    );

    public GizmoLineVertex(vec3 position, vec4 color)
    {
        Position = position;
        Color = color;
    }
}

public class VeldridGizmoRenderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private Pipeline? _pipeline;
    private ResourceLayout? _resourceLayout;
    private ResourceSet? _resourceSet;
    private DeviceBuffer? _projectionViewBuffer;
    private DeviceBuffer? _vertexBuffer;

    private readonly List<GizmoLineVertex> _lines = new();
    private uint _maxVertexCount = 2048;

    public VeldridGizmoRenderer(GraphicsDevice gd, OutputDescription outputDesc)
    {
        _gd = gd;
        Initialize(outputDesc);
    }

    private void Initialize(OutputDescription outputDesc)
    {
        var factory = _gd.ResourceFactory;

        _projectionViewBuffer = factory.CreateBuffer(new BufferDescription(
            64, // mat4 = 64 bytes
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        _resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ViewProjBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _projectionViewBuffer));

        _vertexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(_maxVertexCount * Marshal.SizeOf<GizmoLineVertex>()),
            BufferUsage.VertexBuffer | BufferUsage.Dynamic));

        string vertCode = @"
#version 450
layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;

layout(set = 0, binding = 0) uniform ViewProjBuffer
{
    mat4 ViewProj;
};

layout(location = 0) out vec4 vColor;

void main()
{
    vColor = Color;
    gl_Position = ViewProj * vec4(Position, 1.0);
}
";

        string fragCode = @"
#version 450
layout(location = 0) in vec4 vColor;
layout(location = 0) out vec4 FragColor;

void main()
{
    FragColor = vColor;
}
";

        var shaders = VeldridShader.CompileFromGlsl(_gd, vertCode, fragCode);

        var pipelineDesc = new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.None,
                PolygonFillMode.Solid,
                FrontFace.Clockwise,
                true,
                false),
            PrimitiveTopology = PrimitiveTopology.LineList,
            ResourceLayouts = [_resourceLayout],
            ShaderSet = new ShaderSetDescription(
                [GizmoLineVertex.LayoutDescription],
                shaders),
            Outputs = outputDesc
        };

        _pipeline = factory.CreateGraphicsPipeline(pipelineDesc);
    }

    public void ClearLines()
    {
        _lines.Clear();
    }

    public void AddLine(vec3 start, vec3 end, vec4 color)
    {
        _lines.Add(new GizmoLineVertex(start, color));
        _lines.Add(new GizmoLineVertex(end, color));
    }

    public void AddCircle(vec3 center, vec3 axis, float radius, vec4 color, int segments = 32)
    {
        vec3 perp = MathF.Abs(axis.y) < 0.99f ? vec3.Cross(axis, vec3.UnitY).Normalized : vec3.Cross(axis, vec3.UnitZ).Normalized;
        vec3 perp2 = vec3.Cross(axis, perp).Normalized;

        vec3 prev = center + perp * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = (i / (float)segments) * MathF.PI * 2f;
            vec3 curr = center + (perp * MathF.Cos(angle) + perp2 * MathF.Sin(angle)) * radius;
            AddLine(prev, curr, color);
            prev = curr;
        }
    }

    public void Render(CommandList cl, mat4 viewProjMatrix)
    {
        if (_lines.Count == 0 || _pipeline == null || _resourceSet == null)
            return;

        float[] matrixData =
        {
            viewProjMatrix.m00, viewProjMatrix.m01, viewProjMatrix.m02, viewProjMatrix.m03,
            viewProjMatrix.m10, viewProjMatrix.m11, viewProjMatrix.m12, viewProjMatrix.m13,
            viewProjMatrix.m20, viewProjMatrix.m21, viewProjMatrix.m22, viewProjMatrix.m23,
            viewProjMatrix.m30, viewProjMatrix.m31, viewProjMatrix.m32, viewProjMatrix.m33,
        };
        _gd.UpdateBuffer(_projectionViewBuffer, 0, matrixData);

        uint count = (uint)_lines.Count;
        if (count > _maxVertexCount)
        {
            _maxVertexCount = count + 512;
            _vertexBuffer?.Dispose();
            _vertexBuffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)(_maxVertexCount * Marshal.SizeOf<GizmoLineVertex>()),
                BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        }

        var lineArray = _lines.ToArray();
        _gd.UpdateBuffer(_vertexBuffer, 0, lineArray);

        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _resourceSet);
        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.Draw((uint)lineArray.Length, 1, 0, 0);
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _resourceSet?.Dispose();
        _resourceLayout?.Dispose();
        _projectionViewBuffer?.Dispose();
        _vertexBuffer?.Dispose();
    }
}
