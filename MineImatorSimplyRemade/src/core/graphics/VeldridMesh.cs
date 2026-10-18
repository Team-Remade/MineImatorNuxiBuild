using System;
using System.Runtime.InteropServices;
using GlmSharp;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

[StructLayout(LayoutKind.Sequential)]
public struct VeldridVertex
{
    public vec3 Position;
    public vec3 Normal;
    public vec2 TexCoord;

    public static readonly VertexLayoutDescription LayoutDescription = new(
        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
        new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
        new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
    );
}

[StructLayout(LayoutKind.Sequential)]
public struct VeldridSkinningData
{
    public ivec4 BoneIndices;
    public vec4 BoneWeights;

    public static readonly VertexLayoutDescription LayoutDescription = new(
        new VertexElementDescription("BoneIndices", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Int4),
        new VertexElementDescription("BoneWeights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
    );
}

public class VeldridMesh : IDisposable
{
    public DeviceBuffer? VertexBuffer { get; private set; }
    public DeviceBuffer? IndexBuffer { get; private set; }
    public DeviceBuffer? SkinningBuffer { get; private set; }

    public uint VertexCount { get; private set; }
    public uint IndexCount { get; private set; }
    public bool IsIndexed => IndexBuffer != null && IndexCount > 0;
    public bool IsSkinned => SkinningBuffer != null;

    public void UploadVertices(GraphicsDevice gd, VeldridVertex[] vertices)
    {
        VertexBuffer?.Dispose();
        VertexCount = (uint)vertices.Length;
        if (VertexCount == 0) return;

        uint sizeInBytes = (uint)(VertexCount * Marshal.SizeOf<VeldridVertex>());
        VertexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription(
            sizeInBytes,
            BufferUsage.VertexBuffer));

        gd.UpdateBuffer(VertexBuffer, 0, vertices);
    }

    public void UploadIndices(GraphicsDevice gd, uint[] indices)
    {
        IndexBuffer?.Dispose();
        IndexCount = (uint)indices.Length;
        if (IndexCount == 0) return;

        uint sizeInBytes = (uint)(IndexCount * sizeof(uint));
        IndexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription(
            sizeInBytes,
            BufferUsage.IndexBuffer));

        gd.UpdateBuffer(IndexBuffer, 0, indices);
    }

    public void UploadSkinning(GraphicsDevice gd, VeldridSkinningData[] skinning)
    {
        SkinningBuffer?.Dispose();
        if (skinning.Length == 0) return;

        uint sizeInBytes = (uint)(skinning.Length * Marshal.SizeOf<VeldridSkinningData>());
        SkinningBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription(
            sizeInBytes,
            BufferUsage.VertexBuffer));

        gd.UpdateBuffer(SkinningBuffer, 0, skinning);
    }

    public void Draw(CommandList cl, uint instanceCount = 1)
    {
        if (VertexBuffer == null || VertexCount == 0) return;

        cl.SetVertexBuffer(0, VertexBuffer);
        if (IsSkinned && SkinningBuffer != null)
        {
            cl.SetVertexBuffer(1, SkinningBuffer);
        }

        if (IsIndexed && IndexBuffer != null)
        {
            cl.SetIndexBuffer(IndexBuffer, IndexFormat.UInt32);
            cl.DrawIndexed(IndexCount, instanceCount, 0, 0, 0);
        }
        else
        {
            cl.Draw(VertexCount, instanceCount, 0, 0);
        }
    }

    public void Dispose()
    {
        VertexBuffer?.Dispose();
        VertexBuffer = null;
        IndexBuffer?.Dispose();
        IndexBuffer = null;
        SkinningBuffer?.Dispose();
        SkinningBuffer = null;
    }
}
