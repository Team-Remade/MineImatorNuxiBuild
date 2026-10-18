using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// A reusable, mapped CPU-to-GPU staging buffer ring allocator designed for high-frequency
/// dynamic updates (e.g. skinning matrices, particle streams, procedural vertices, instance batches).
/// Eliminates CPU-GPU pipeline stalls by sub-allocating from staging memory and scheduling GPU copies.
/// </summary>
public class VeldridStagingBuffer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly uint _capacityInBytes;
    private DeviceBuffer _stagingBuffer;
    private uint _currentOffset;

    public uint CapacityInBytes => _capacityInBytes;
    public uint CurrentOffset => _currentOffset;
    public uint RemainingBytes => _capacityInBytes - _currentOffset;
    public DeviceBuffer DeviceBuffer => _stagingBuffer;

    public VeldridStagingBuffer(GraphicsDevice gd, uint capacityInBytes = 4 * 1024 * 1024) // Default 4MB
    {
        _gd = gd ?? throw new ArgumentNullException(nameof(gd));
        _capacityInBytes = Math.Max(capacityInBytes, 64 * 1024);

        var bufferDesc = new BufferDescription(
            _capacityInBytes,
            BufferUsage.Staging
        );

        _stagingBuffer = _gd.ResourceFactory.CreateBuffer(bufferDesc);
        _currentOffset = 0;
    }

    /// <summary>
    /// Resets the ring allocation pointer for the next frame / pass.
    /// </summary>
    public void Reset()
    {
        _currentOffset = 0;
    }

    /// <summary>
    /// Writes unmanaged data to staging memory and copies it to the destination GPU buffer via CommandList.
    /// </summary>
    public unsafe bool StageAndCopy<T>(
        CommandList cl,
        DeviceBuffer destinationBuffer,
        uint destinationOffsetInBytes,
        ReadOnlySpan<T> data,
        out uint stagedSourceOffset) where T : unmanaged
    {
        uint dataSizeInBytes = (uint)(data.Length * sizeof(T));
        if (dataSizeInBytes == 0)
        {
            stagedSourceOffset = 0;
            return true;
        }

        // Align offset to 256 bytes (standard uniform / copy alignment)
        uint alignedOffset = (_currentOffset + 255u) & ~255u;

        if (alignedOffset + dataSizeInBytes > _capacityInBytes)
        {
            // Overflow: fallback to direct GraphicsDevice.UpdateBuffer or reset if at start
            stagedSourceOffset = 0;
            return false;
        }

        // Map and copy into staging buffer
        var mapped = _gd.Map(_stagingBuffer, MapMode.Write, 0);
        try
        {
            byte* ptr = (byte*)mapped.Data.ToPointer() + alignedOffset;
            fixed (T* srcPtr = data)
            {
                Buffer.MemoryCopy(srcPtr, ptr, _capacityInBytes - alignedOffset, dataSizeInBytes);
            }
        }
        finally
        {
            _gd.Unmap(_stagingBuffer);
        }

        // Record GPU copy command from staging to target device buffer
        cl.CopyBuffer(_stagingBuffer, alignedOffset, destinationBuffer, destinationOffsetInBytes, dataSizeInBytes);

        stagedSourceOffset = alignedOffset;
        _currentOffset = alignedOffset + dataSizeInBytes;
        return true;
    }

    public void Dispose()
    {
        _stagingBuffer?.Dispose();
    }
}

/// <summary>
/// A dynamically-sized GPU buffer with double-buffered / staging-assisted fast update mechanics
/// for high-performance per-frame vertex, index, uniform, or structured buffer updates.
/// </summary>
/// <typeparam name="T">Unmanaged data element type.</typeparam>
public class VeldridDynamicBuffer<T> : IDisposable where T : unmanaged
{
    private readonly GraphicsDevice _gd;
    private readonly BufferUsage _usage;
    private readonly uint _structureByteStride;
    private DeviceBuffer _deviceBuffer;
    private uint _capacityCount;

    public DeviceBuffer DeviceBuffer => _deviceBuffer;
    public uint CapacityCount => _capacityCount;
    public uint SizeInBytes => (uint)(_capacityCount * Unsafe.SizeOf<T>());
    public BufferUsage Usage => _usage;

    public VeldridDynamicBuffer(
        GraphicsDevice gd,
        BufferUsage usage,
        uint initialCapacity = 64,
        uint structureByteStride = 0)
    {
        _gd = gd ?? throw new ArgumentNullException(nameof(gd));
        _usage = usage | BufferUsage.Dynamic;
        _capacityCount = Math.Max(initialCapacity, 16);
        _structureByteStride = structureByteStride;

        _deviceBuffer = AllocateGpuBuffer(_capacityCount);
    }

    private DeviceBuffer AllocateGpuBuffer(uint count)
    {
        uint byteSize = (uint)(count * Unsafe.SizeOf<T>());
        var desc = new BufferDescription(byteSize, _usage, _structureByteStride);
        return _gd.ResourceFactory.CreateBuffer(desc);
    }

    /// <summary>
    /// Ensures the GPU buffer has sufficient capacity, reallocating if needed.
    /// </summary>
    public void EnsureCapacity(uint requiredCount)
    {
        if (requiredCount <= _capacityCount)
            return;

        uint newCapacity = Math.Max(_capacityCount * 2, requiredCount);
        _deviceBuffer?.Dispose();
        _capacityCount = newCapacity;
        _deviceBuffer = AllocateGpuBuffer(_capacityCount);
    }

    /// <summary>
    /// Updates the buffer using fast direct mapped write or GraphicsDevice.UpdateBuffer.
    /// Automatically expands capacity if needed.
    /// </summary>
    public void UpdateData(ReadOnlySpan<T> data, uint destinationOffsetElements = 0)
    {
        if (data.IsEmpty)
            return;

        uint requiredCount = destinationOffsetElements + (uint)data.Length;
        EnsureCapacity(requiredCount);

        uint elementSize = (uint)Unsafe.SizeOf<T>();
        uint destOffsetBytes = destinationOffsetElements * elementSize;

        _gd.UpdateBuffer(_deviceBuffer, destOffsetBytes, data);
    }

    /// <summary>
    /// Updates the buffer using a staging buffer and records a copy command into the provided CommandList.
    /// Recommended for large datasets (e.g. mesh vertex morphing, bulk instance buffers).
    /// </summary>
    public void UpdateDataStaged(
        VeldridStagingBuffer staging,
        CommandList cl,
        ReadOnlySpan<T> data,
        uint destinationOffsetElements = 0)
    {
        if (data.IsEmpty)
            return;

        uint requiredCount = destinationOffsetElements + (uint)data.Length;
        EnsureCapacity(requiredCount);

        uint elementSize = (uint)Unsafe.SizeOf<T>();
        uint destOffsetBytes = destinationOffsetElements * elementSize;

        if (staging != null && staging.StageAndCopy(cl, _deviceBuffer, destOffsetBytes, data, out _))
        {
            return;
        }

        // Fallback if staging buffer is full
        _gd.UpdateBuffer(_deviceBuffer, destOffsetBytes, data);
    }

    public void Dispose()
    {
        _deviceBuffer?.Dispose();
    }
}

/// <summary>
/// Thread-safe upload manager coordinating dynamic staging pools and asynchronous buffer transfers.
/// </summary>
public class VeldridBufferUploadManager : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ConcurrentBag<VeldridStagingBuffer> _stagingPool = new();
    private readonly List<VeldridStagingBuffer> _allStagingBuffers = new();
    private readonly uint _defaultStagingSize;
    private readonly object _lock = new();

    public GraphicsDevice GraphicsDevice => _gd;

    public VeldridBufferUploadManager(GraphicsDevice gd, uint defaultStagingSize = 4 * 1024 * 1024)
    {
        _gd = gd ?? throw new ArgumentNullException(nameof(gd));
        _defaultStagingSize = defaultStagingSize;
    }

    /// <summary>
    /// Rents a staging buffer for staging copies.
    /// </summary>
    public VeldridStagingBuffer RentStagingBuffer()
    {
        if (_stagingPool.TryTake(out var staging))
        {
            staging.Reset();
            return staging;
        }

        lock (_lock)
        {
            var newBuffer = new VeldridStagingBuffer(_gd, _defaultStagingSize);
            _allStagingBuffers.Add(newBuffer);
            return newBuffer;
        }
    }

    /// <summary>
    /// Returns a staging buffer back to the pool.
    /// </summary>
    public void ReturnStagingBuffer(VeldridStagingBuffer staging)
    {
        if (staging == null) return;
        _stagingPool.Add(staging);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            while (_stagingPool.TryTake(out _)) { }
            foreach (var buffer in _allStagingBuffers)
            {
                buffer.Dispose();
            }
            _allStagingBuffers.Clear();
        }
    }
}
