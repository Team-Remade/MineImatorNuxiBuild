using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GlmSharp;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

[StructLayout(LayoutKind.Sequential)]
public struct VeldridInstanceData
{
    public vec4 ModelRow0;
    public vec4 ModelRow1;
    public vec4 ModelRow2;
    public vec4 ModelRow3;
    public vec4 ColorTint;
    public vec4 UvTransform; // x = u_offset, y = v_offset, z = u_scale, w = v_scale

    public static readonly VertexLayoutDescription LayoutDescription = new(
        1, // instance step rate
        new VertexElementDescription("InstanceModel0", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
        new VertexElementDescription("InstanceModel1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
        new VertexElementDescription("InstanceModel2", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
        new VertexElementDescription("InstanceModel3", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
        new VertexElementDescription("InstanceColorTint", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
        new VertexElementDescription("InstanceUvTransform", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
    );

    public static VeldridInstanceData FromMatrix(mat4 modelMatrix, vec4? colorTint = null, vec4? uvTransform = null)
    {
        return new VeldridInstanceData
        {
            ModelRow0 = new vec4(modelMatrix.m00, modelMatrix.m10, modelMatrix.m20, modelMatrix.m30),
            ModelRow1 = new vec4(modelMatrix.m01, modelMatrix.m11, modelMatrix.m21, modelMatrix.m31),
            ModelRow2 = new vec4(modelMatrix.m02, modelMatrix.m12, modelMatrix.m22, modelMatrix.m32),
            ModelRow3 = new vec4(modelMatrix.m03, modelMatrix.m13, modelMatrix.m23, modelMatrix.m33),
            ColorTint = colorTint ?? vec4.Ones,
            UvTransform = uvTransform ?? new vec4(0f, 0f, 1f, 1f)
        };
    }
}

public class VeldridInstancedBatch : IDisposable
{
    private readonly GraphicsDevice _gd;
    public VeldridMesh Mesh { get; set; }
    public DeviceBuffer? InstanceBuffer { get; private set; }

    private readonly List<VeldridInstanceData> _instances = new();
    private uint _maxInstanceCapacity = 256;
    private bool _isDirty = false;

    public uint InstanceCount => (uint)_instances.Count;

    public VeldridInstancedBatch(GraphicsDevice gd, VeldridMesh mesh, uint initialCapacity = 256)
    {
        _gd = gd;
        Mesh = mesh;
        _maxInstanceCapacity = Math.Max(16, initialCapacity);

        RecreateInstanceBuffer();
    }

    private void RecreateInstanceBuffer()
    {
        InstanceBuffer?.Dispose();
        uint sizeInBytes = (uint)(_maxInstanceCapacity * Marshal.SizeOf<VeldridInstanceData>());
        InstanceBuffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(
            sizeInBytes,
            BufferUsage.VertexBuffer | BufferUsage.Dynamic));
    }

    public void Clear()
    {
        _instances.Clear();
        _isDirty = true;
    }

    public void AddInstance(VeldridInstanceData instance)
    {
        _instances.Add(instance);
        _isDirty = true;
    }

    public void AddInstance(mat4 modelMatrix, vec4? colorTint = null, vec4? uvTransform = null)
    {
        _instances.Add(VeldridInstanceData.FromMatrix(modelMatrix, colorTint, uvTransform));
        _isDirty = true;
    }

    public void AddInstances(ReadOnlySpan<VeldridInstanceData> instances)
    {
        for (int i = 0; i < instances.Length; i++)
        {
            _instances.Add(instances[i]);
        }
        _isDirty = true;
    }

    public void Flush()
    {
        if (!_isDirty || _instances.Count == 0)
            return;

        uint count = (uint)_instances.Count;
        if (count > _maxInstanceCapacity)
        {
            _maxInstanceCapacity = Math.Max(count + 128, _maxInstanceCapacity * 2);
            RecreateInstanceBuffer();
        }

        if (InstanceBuffer != null)
        {
            var array = _instances.ToArray();
            _gd.UpdateBuffer(InstanceBuffer, 0, array);
        }

        _isDirty = false;
    }

    public void Render(CommandList cl)
    {
        if (_instances.Count == 0 || Mesh.VertexBuffer == null)
            return;

        if (_isDirty)
        {
            Flush();
        }

        if (InstanceBuffer == null)
            return;

        cl.SetVertexBuffer(0, Mesh.VertexBuffer);
        cl.SetVertexBuffer(1, InstanceBuffer);

        if (Mesh.IsIndexed && Mesh.IndexBuffer != null)
        {
            cl.SetIndexBuffer(Mesh.IndexBuffer, IndexFormat.UInt32);
            cl.DrawIndexed(Mesh.IndexCount, InstanceCount, 0, 0, 0);
        }
        else
        {
            cl.Draw(Mesh.VertexCount, InstanceCount, 0, 0);
        }
    }

    public void Dispose()
    {
        InstanceBuffer?.Dispose();
        InstanceBuffer = null;
        _instances.Clear();
    }
}
