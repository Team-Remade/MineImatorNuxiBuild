using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// High-performance uniform buffer ring allocator and pool for dynamic per-frame and per-draw uniform updates.
/// Sub-allocates uniform blocks aligned to device minimum uniform buffer offset alignment (typically 256 bytes)
/// using dynamic uniform offsets (or sliced buffer ranges), preventing GPU pipeline stalls and buffer allocation overhead.
/// </summary>
public class VeldridUniformBufferPool : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly uint _minUniformOffsetAlignment;
    private readonly uint _defaultBlockSize;

    private readonly List<DeviceBuffer> _allocatedBuffers = new();
    private readonly object _lock = new();

    private DeviceBuffer _currentBuffer;
    private uint _currentOffset;
    private uint _currentBufferSize;

    public uint MinAlignment => _minUniformOffsetAlignment;
    public int AllocatedBufferCount
    {
        get
        {
            lock (_lock) return _allocatedBuffers.Count;
        }
    }

    public VeldridUniformBufferPool(GraphicsDevice gd, uint defaultBlockSize = 65536)
    {
        _gd = gd ?? throw new ArgumentNullException(nameof(gd));
        _minUniformOffsetAlignment = Math.Max(256u, gd.UniformBufferMinOffsetAlignment);
        _defaultBlockSize = AlignUp(defaultBlockSize, _minUniformOffsetAlignment);

        AllocateNewBuffer(_defaultBlockSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint AlignUp(uint value, uint alignment)
    {
        if (alignment == 0) return value;
        return (value + alignment - 1) & ~(alignment - 1);
    }

    private void AllocateNewBuffer(uint size)
    {
        uint bufferSize = Math.Max(_defaultBlockSize, AlignUp(size, _minUniformOffsetAlignment));
        var buffer = _gd.ResourceFactory.CreateBuffer(new BufferDescription(
            bufferSize,
            BufferUsage.UniformBuffer | BufferUsage.Dynamic
        ));

        _allocatedBuffers.Add(buffer);
        _currentBuffer = buffer;
        _currentOffset = 0;
        _currentBufferSize = bufferSize;
    }

    /// <summary>
    /// Allocates uniform memory for a value type and writes the data to the buffer.
    /// Returns the buffer range descriptor (DeviceBuffer, aligned offset, and size).
    /// </summary>
    public UniformBufferAllocation Allocate<T>(ref T data) where T : unmanaged
    {
        uint dataSize = (uint)Unsafe.SizeOf<T>();
        uint alignedSize = AlignUp(dataSize, _minUniformOffsetAlignment);

        lock (_lock)
        {
            if (_currentOffset + alignedSize > _currentBufferSize)
            {
                AllocateNewBuffer(Math.Max(_defaultBlockSize, alignedSize));
            }

            uint offset = _currentOffset;
            _currentOffset += alignedSize;

            _gd.UpdateBuffer(_currentBuffer, offset, ref data);

            return new UniformBufferAllocation(_currentBuffer, offset, dataSize, alignedSize);
        }
    }

    /// <summary>
    /// Allocates uniform memory for a value type and writes the data to the buffer.
    /// </summary>
    public UniformBufferAllocation Allocate<T>(T data) where T : unmanaged
    {
        return Allocate(ref data);
    }

    /// <summary>
    /// Allocates a uniform block of raw bytes and uploads data.
    /// </summary>
    public UniformBufferAllocation Allocate(IntPtr sourceData, uint size)
    {
        uint alignedSize = AlignUp(size, _minUniformOffsetAlignment);

        lock (_lock)
        {
            if (_currentOffset + alignedSize > _currentBufferSize)
            {
                AllocateNewBuffer(Math.Max(_defaultBlockSize, alignedSize));
            }

            uint offset = _currentOffset;
            _currentOffset += alignedSize;

            _gd.UpdateBuffer(_currentBuffer, offset, sourceData, size);

            return new UniformBufferAllocation(_currentBuffer, offset, size, alignedSize);
        }
    }

    /// <summary>
    /// Resets the allocation offset to the beginning of the initial buffer at frame start.
    /// Call this once per frame after GPU has finished processing previous frame commands.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            if (_allocatedBuffers.Count > 0)
            {
                _currentBuffer = _allocatedBuffers[0];
                _currentOffset = 0;
                _currentBufferSize = _currentBuffer.SizeInBytes;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var buf in _allocatedBuffers)
            {
                buf.Dispose();
            }
            _allocatedBuffers.Clear();
        }
    }
}

/// <summary>
/// Descriptor pointing to a sub-allocated region within a uniform buffer.
/// </summary>
public readonly struct UniformBufferAllocation : IEquatable<UniformBufferAllocation>
{
    public DeviceBuffer Buffer { get; }
    public uint Offset { get; }
    public uint Size { get; }
    public uint AlignedSize { get; }

    public UniformBufferAllocation(DeviceBuffer buffer, uint offset, uint size, uint alignedSize)
    {
        Buffer = buffer;
        Offset = offset;
        Size = size;
        AlignedSize = alignedSize;
    }

    public DeviceBufferRange AsBufferRange() => new(Buffer, Offset, Size);

    public bool Equals(UniformBufferAllocation other)
    {
        return ReferenceEquals(Buffer, other.Buffer) && Offset == other.Offset && Size == other.Size;
    }

    public override bool Equals(object? obj) => obj is UniformBufferAllocation other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Buffer, Offset, Size);

    public static bool operator ==(UniformBufferAllocation left, UniformBufferAllocation right) => left.Equals(right);
    public static bool operator !=(UniformBufferAllocation left, UniformBufferAllocation right) => !left.Equals(right);
}

/// <summary>
/// Dynamic ResourceSet cache and pool.
/// Caches ResourceSet instances by ResourceLayout and bound resources (Buffers, Textures, Samplers)
/// to avoid continuous allocations, GC pressure, and driver descriptor heap re-allocations during draw loops.
/// </summary>
public class VeldridResourceSetPool : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ConcurrentDictionary<ResourceSetKey, ResourceSet> _resourceSets = new();

    public GraphicsDevice GraphicsDevice => _gd;
    public int CachedSetCount => _resourceSets.Count;

    public VeldridResourceSetPool(GraphicsDevice gd)
    {
        _gd = gd ?? throw new ArgumentNullException(nameof(gd));
    }

    /// <summary>
    /// Gets or creates a cached ResourceSet from layout and bound resources.
    /// </summary>
    public ResourceSet GetOrCreate(ResourceLayout layout, params BindableResource[] resources)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        if (resources == null) throw new ArgumentNullException(nameof(resources));

        var key = new ResourceSetKey(layout, resources);
        return _resourceSets.GetOrAdd(key, k =>
        {
            var desc = new ResourceSetDescription(k.Layout, k.Resources);
            return _gd.ResourceFactory.CreateResourceSet(ref desc);
        });
    }

    /// <summary>
    /// Gets or creates a cached single-buffer ResourceSet.
    /// </summary>
    public ResourceSet GetOrCreate(ResourceLayout layout, DeviceBuffer buffer)
    {
        return GetOrCreate(layout, (BindableResource)buffer);
    }

    /// <summary>
    /// Gets or creates a cached single-range ResourceSet.
    /// </summary>
    public ResourceSet GetOrCreate(ResourceLayout layout, DeviceBufferRange range)
    {
        return GetOrCreate(layout, (BindableResource)range);
    }

    /// <summary>
    /// Gets or creates a cached texture + sampler ResourceSet.
    /// </summary>
    public ResourceSet GetOrCreate(ResourceLayout layout, TextureView textureView, Sampler sampler)
    {
        return GetOrCreate(layout, textureView, sampler);
    }

    public void Clear()
    {
        foreach (var set in _resourceSets.Values)
        {
            set.Dispose();
        }
        _resourceSets.Clear();
    }

    public void Dispose()
    {
        Clear();
    }

    private readonly struct ResourceSetKey : IEquatable<ResourceSetKey>
    {
        public ResourceLayout Layout { get; }
        public BindableResource[] Resources { get; }

        public ResourceSetKey(ResourceLayout layout, BindableResource[] resources)
        {
            Layout = layout;
            Resources = resources;
        }

        public bool Equals(ResourceSetKey other)
        {
            if (!ReferenceEquals(Layout, other.Layout)) return false;
            if (Resources.Length != other.Resources.Length) return false;

            for (int i = 0; i < Resources.Length; i++)
            {
                var rA = Resources[i];
                var rB = other.Resources[i];

                if (rA is DeviceBufferRange rangeA && rB is DeviceBufferRange rangeB)
                {
                    if (!ReferenceEquals(rangeA.Buffer, rangeB.Buffer) || rangeA.Offset != rangeB.Offset || rangeA.SizeInBytes != rangeB.SizeInBytes)
                        return false;
                }
                else if (!ReferenceEquals(rA, rB))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is ResourceSetKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Layout);
            foreach (var res in Resources)
            {
                if (res is DeviceBufferRange range)
                {
                    hash.Add(range.Buffer);
                    hash.Add(range.Offset);
                    hash.Add(range.SizeInBytes);
                }
                else
                {
                    hash.Add(res?.GetHashCode() ?? 0);
                }
            }
            return hash.ToHashCode();
        }
    }
}
