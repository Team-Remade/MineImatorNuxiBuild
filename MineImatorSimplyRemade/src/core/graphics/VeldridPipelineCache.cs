using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// Immutable descriptor representation used as a cache key for Veldrid GraphicsPipeline creation.
/// Compares blend, depth-stencil, rasterizer, primitive topology, resource layouts, and shader stages.
/// </summary>
public readonly struct PipelineStateDescription : IEquatable<PipelineStateDescription>
{
    public BlendStateDescription BlendState { get; init; }
    public DepthStencilStateDescription DepthStencilState { get; init; }
    public RasterizerStateDescription RasterizerState { get; init; }
    public PrimitiveTopology PrimitiveTopology { get; init; }
    public OutputDescription OutputDescription { get; init; }
    public ShaderSetDescription ShaderSet { get; init; }
    public ResourceLayout[] ResourceLayouts { get; init; }

    public PipelineStateDescription(
        BlendStateDescription blendState,
        DepthStencilStateDescription depthStencilState,
        RasterizerStateDescription rasterizerState,
        PrimitiveTopology primitiveTopology,
        ShaderSetDescription shaderSet,
        ResourceLayout[] resourceLayouts,
        OutputDescription outputDescription)
    {
        BlendState = blendState;
        DepthStencilState = depthStencilState;
        RasterizerState = rasterizerState;
        PrimitiveTopology = primitiveTopology;
        ShaderSet = shaderSet;
        ResourceLayouts = resourceLayouts ?? Array.Empty<ResourceLayout>();
        OutputDescription = outputDescription;
    }

    public GraphicsPipelineDescription ToGraphicsPipelineDescription()
    {
        return new GraphicsPipelineDescription(
            BlendState,
            DepthStencilState,
            RasterizerState,
            PrimitiveTopology,
            ShaderSet,
            ResourceLayouts,
            OutputDescription
        );
    }

    public bool Equals(PipelineStateDescription other)
    {
        if (!BlendState.Equals(other.BlendState) ||
            !DepthStencilState.Equals(other.DepthStencilState) ||
            !RasterizerState.Equals(other.RasterizerState) ||
            PrimitiveTopology != other.PrimitiveTopology ||
            !OutputDescription.Equals(other.OutputDescription))
        {
            return false;
        }

        // Compare ShaderSet
        if (ShaderSet.Shaders == null && other.ShaderSet.Shaders != null) return false;
        if (ShaderSet.Shaders != null && other.ShaderSet.Shaders == null) return false;
        if (ShaderSet.Shaders != null && other.ShaderSet.Shaders != null)
        {
            if (ShaderSet.Shaders.Length != other.ShaderSet.Shaders.Length) return false;
            for (int i = 0; i < ShaderSet.Shaders.Length; i++)
            {
                if (!ReferenceEquals(ShaderSet.Shaders[i], other.ShaderSet.Shaders[i]))
                    return false;
            }
        }

        // Compare VertexLayouts
        if (ShaderSet.VertexLayouts == null && other.ShaderSet.VertexLayouts != null) return false;
        if (ShaderSet.VertexLayouts != null && other.ShaderSet.VertexLayouts == null) return false;
        if (ShaderSet.VertexLayouts != null && other.ShaderSet.VertexLayouts != null)
        {
            if (ShaderSet.VertexLayouts.Length != other.ShaderSet.VertexLayouts.Length) return false;
            for (int i = 0; i < ShaderSet.VertexLayouts.Length; i++)
            {
                if (!ShaderSet.VertexLayouts[i].Equals(other.ShaderSet.VertexLayouts[i]))
                    return false;
            }
        }

        // Compare ResourceLayouts
        if (ResourceLayouts.Length != other.ResourceLayouts.Length) return false;
        for (int i = 0; i < ResourceLayouts.Length; i++)
        {
            if (!ReferenceEquals(ResourceLayouts[i], other.ResourceLayouts[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is PipelineStateDescription other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BlendState);
        hash.Add(DepthStencilState);
        hash.Add(RasterizerState);
        hash.Add((int)PrimitiveTopology);
        hash.Add(OutputDescription);

        if (ShaderSet.Shaders != null)
        {
            foreach (var s in ShaderSet.Shaders)
            {
                hash.Add(s?.GetHashCode() ?? 0);
            }
        }

        if (ShaderSet.VertexLayouts != null)
        {
            foreach (var vl in ShaderSet.VertexLayouts)
            {
                hash.Add(vl);
            }
        }

        if (ResourceLayouts != null)
        {
            foreach (var rl in ResourceLayouts)
            {
                hash.Add(rl?.GetHashCode() ?? 0);
            }
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(PipelineStateDescription left, PipelineStateDescription right) => left.Equals(right);
    public static bool operator !=(PipelineStateDescription left, PipelineStateDescription right) => !left.Equals(right);
}

/// <summary>
/// Pre-baked common rasterizer, blend, and depth-stencil states for rendering passes.
/// </summary>
public static class CommonPipelineStates
{
    public static readonly BlendStateDescription Opaque = BlendStateDescription.SingleOverrideBlend;

    public static readonly BlendStateDescription AlphaBlend = BlendStateDescription.SingleAlphaBlend;

    public static readonly BlendStateDescription AdditiveBlend = BlendStateDescription.SingleAdditiveBlend;

    public static readonly DepthStencilStateDescription DepthDefault = DepthStencilStateDescription.DepthOnlyLessEqual;

    public static readonly DepthStencilStateDescription DepthReadEqual = new(
        depthTestEnabled: true,
        depthWriteEnabled: false,
        comparisonKind: ComparisonKind.Equal
    );

    public static readonly DepthStencilStateDescription DepthReadLessEqual = DepthStencilStateDescription.DepthOnlyLessEqualRead;

    public static readonly DepthStencilStateDescription DepthNone = DepthStencilStateDescription.Disabled;

    public static readonly RasterizerStateDescription CullBack = new(
        cullMode: FaceCullMode.Back,
        fillMode: PolygonFillMode.Solid,
        frontFace: FrontFace.Clockwise,
        depthClipEnabled: true,
        scissorTestEnabled: false
    );

    public static readonly RasterizerStateDescription CullNone = RasterizerStateDescription.CullNone;

    public static readonly RasterizerStateDescription CullFront = new(
        cullMode: FaceCullMode.Front,
        fillMode: PolygonFillMode.Solid,
        frontFace: FrontFace.Clockwise,
        depthClipEnabled: true,
        scissorTestEnabled: false
    );

    public static readonly RasterizerStateDescription Wireframe = new(
        cullMode: FaceCullMode.None,
        fillMode: PolygonFillMode.Wireframe,
        frontFace: FrontFace.Clockwise,
        depthClipEnabled: true,
        scissorTestEnabled: false
    );
}

/// <summary>
/// Immutable Pipeline State Object (PSO) and cache manager.
/// Manages thread-safe creation, caching, and reuse of Veldrid Pipeline objects,
/// eliminating runtime shader recompilations and pipeline recreation overhead.
/// </summary>
public class VeldridPipelineCache : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ConcurrentDictionary<PipelineStateDescription, Pipeline> _graphicsPipelineCache = new();
    private readonly ConcurrentDictionary<ComputePipelineDescriptionKey, Pipeline> _computePipelineCache = new();

    public GraphicsDevice GraphicsDevice => _gd;
    public int CachedGraphicsPipelineCount => _graphicsPipelineCache.Count;
    public int CachedComputePipelineCount => _computePipelineCache.Count;

    public VeldridPipelineCache(GraphicsDevice gd)
    {
        _gd = gd ?? throw new ArgumentNullException(nameof(gd));
    }

    /// <summary>
    /// Gets or creates an immutable graphics pipeline from a full description.
    /// </summary>
    public Pipeline GetOrCreatePipeline(in PipelineStateDescription desc)
    {
        return _graphicsPipelineCache.GetOrAdd(desc, key =>
        {
            var gpDesc = key.ToGraphicsPipelineDescription();
            return _gd.ResourceFactory.CreateGraphicsPipeline(ref gpDesc);
        });
    }

    /// <summary>
    /// Gets or creates a graphics pipeline using standard builder parameters.
    /// </summary>
    public Pipeline GetOrCreatePipeline(
        ShaderSetDescription shaderSet,
        ResourceLayout[] resourceLayouts,
        OutputDescription outputDescription,
        BlendStateDescription? blendState = null,
        DepthStencilStateDescription? depthStencilState = null,
        RasterizerStateDescription? rasterizerState = null,
        PrimitiveTopology primitiveTopology = PrimitiveTopology.TriangleList)
    {
        var desc = new PipelineStateDescription(
            blendState ?? CommonPipelineStates.Opaque,
            depthStencilState ?? CommonPipelineStates.DepthDefault,
            rasterizerState ?? CommonPipelineStates.CullBack,
            primitiveTopology,
            shaderSet,
            resourceLayouts,
            outputDescription
        );

        return GetOrCreatePipeline(in desc);
    }

    /// <summary>
    /// Gets or creates a compute pipeline for compute shader passes.
    /// </summary>
    public Pipeline GetOrCreateComputePipeline(
        Shader computeShader,
        ResourceLayout[] resourceLayouts,
        uint threadGroupSizeX,
        uint threadGroupSizeY,
        uint threadGroupSizeZ)
    {
        var key = new ComputePipelineDescriptionKey(computeShader, resourceLayouts, threadGroupSizeX, threadGroupSizeY, threadGroupSizeZ);
        return _computePipelineCache.GetOrAdd(key, k =>
        {
            var desc = new ComputePipelineDescription(
                k.ComputeShader,
                k.ResourceLayouts,
                k.ThreadGroupSizeX,
                k.ThreadGroupSizeY,
                k.ThreadGroupSizeZ
            );
            return _gd.ResourceFactory.CreateComputePipeline(ref desc);
        });
    }

    public void Dispose()
    {
        foreach (var pipeline in _graphicsPipelineCache.Values)
        {
            pipeline.Dispose();
        }
        _graphicsPipelineCache.Clear();

        foreach (var pipeline in _computePipelineCache.Values)
        {
            pipeline.Dispose();
        }
        _computePipelineCache.Clear();
    }

    private readonly struct ComputePipelineDescriptionKey : IEquatable<ComputePipelineDescriptionKey>
    {
        public Shader ComputeShader { get; }
        public ResourceLayout[] ResourceLayouts { get; }
        public uint ThreadGroupSizeX { get; }
        public uint ThreadGroupSizeY { get; }
        public uint ThreadGroupSizeZ { get; }

        public ComputePipelineDescriptionKey(
            Shader computeShader,
            ResourceLayout[] resourceLayouts,
            uint threadGroupSizeX,
            uint threadGroupSizeY,
            uint threadGroupSizeZ)
        {
            ComputeShader = computeShader;
            ResourceLayouts = resourceLayouts ?? Array.Empty<ResourceLayout>();
            ThreadGroupSizeX = threadGroupSizeX;
            ThreadGroupSizeY = threadGroupSizeY;
            ThreadGroupSizeZ = threadGroupSizeZ;
        }

        public bool Equals(ComputePipelineDescriptionKey other)
        {
            if (!ReferenceEquals(ComputeShader, other.ComputeShader) ||
                ThreadGroupSizeX != other.ThreadGroupSizeX ||
                ThreadGroupSizeY != other.ThreadGroupSizeY ||
                ThreadGroupSizeZ != other.ThreadGroupSizeZ ||
                ResourceLayouts.Length != other.ResourceLayouts.Length)
            {
                return false;
            }

            for (int i = 0; i < ResourceLayouts.Length; i++)
            {
                if (!ReferenceEquals(ResourceLayouts[i], other.ResourceLayouts[i]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is ComputePipelineDescriptionKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ComputeShader);
            hash.Add(ThreadGroupSizeX);
            hash.Add(ThreadGroupSizeY);
            hash.Add(ThreadGroupSizeZ);
            foreach (var rl in ResourceLayouts)
            {
                hash.Add(rl?.GetHashCode() ?? 0);
            }
            return hash.ToHashCode();
        }
    }
}
