using Veldrid;
using Sampler = Veldrid.Sampler;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>One image-to-image step of the Veldrid camera-effect chain.</summary>
public interface IVeldridPostEffect
{
    void Apply(VeldridPostContext context);
}

/// <summary>
/// Hands an effect the current image, a spare image to write into and the helpers to draw.
/// Most effects read <see cref="Current"/> and call <see cref="DrawToOther"/>, which writes the
/// spare image and makes it current; effects that composite over the picture call
/// <see cref="DrawOverCurrent"/> instead.
/// </summary>
public sealed class VeldridPostContext
{
    internal VeldridPostContext(VeldridPostProcessor processor, CommandList commands,
        VeldridFramebuffer scene, VeldridFramebuffer spare)
    {
        Processor = processor;
        Commands = commands;
        Scene = scene;
        Current = scene;
        Other = spare;
    }

    public VeldridPostProcessor Processor { get; }
    public GraphicsDevice Device => Processor.Device;
    public CommandList Commands { get; }
    /// <summary>The rendered scene target. It stays valid for its depth buffer after later passes.</summary>
    public VeldridFramebuffer Scene { get; }
    /// <summary>Holds the image produced so far.</summary>
    public VeldridFramebuffer Current { get; private set; }
    /// <summary>Spare image of the same size; its contents are undefined.</summary>
    public VeldridFramebuffer Other { get; private set; }
    public uint Width => Scene.Width;
    public uint Height => Scene.Height;
    public VeldridFramebuffer Scratch(int index, PixelFormat? colorFormat = null) =>
        Processor.GetScratchTarget(index, Scene, colorFormat ?? Scene.ColorFormat);

    public void Swap() => (Current, Other) = (Other, Current);

    /// <summary>Draws into the spare image, then makes it current.</summary>
    public void DrawToOther(VeldridFullscreenPass pass, VeldridFullscreenPass.BlendMode blend = VeldridFullscreenPass.BlendMode.Opaque)
    {
        pass.Draw(Commands, Other.Framebuffer, blend);
        Swap();
    }

    public void DrawTo(VeldridFullscreenPass pass, VeldridFramebuffer target,
        VeldridFullscreenPass.BlendMode blend = VeldridFullscreenPass.BlendMode.Opaque) =>
        pass.Draw(Commands, target.Framebuffer, blend);

    public void Copy(VeldridFramebuffer source, VeldridFramebuffer target)
    {
        if (source.Width != target.Width || source.Height != target.Height || source.ColorFormat != target.ColorFormat)
            throw new ArgumentException("Post-process copy targets must have matching dimensions and formats.", nameof(target));
        Commands.CopyTexture(source.ColorTexture, target.ColorTexture);
    }

    /// <summary>Draws over the current image. The pass must not sample the current image.</summary>
    public void DrawOverCurrent(VeldridFullscreenPass pass, VeldridFullscreenPass.BlendMode blend)
        => pass.Draw(Commands, Current.Framebuffer, blend);
}

/// <summary>
/// Owns the fullscreen passes and the spare render target for the Veldrid camera-effect chain.
/// Effects run in list order against OpenGL row order images, so each one reproduces its
/// OpenGL counterpart in <c>Viewport</c>.
/// </summary>
public sealed class VeldridPostProcessor : IDisposable
{
    private readonly Dictionary<string, VeldridFullscreenPass> _passes = new(StringComparer.Ordinal);
    private readonly Dictionary<(SamplerFilter, SamplerAddressMode, SamplerAddressMode), Sampler> _samplers = new();
    private readonly List<VeldridFramebuffer> _scratchTargets = new();
    private VeldridFramebuffer? _spare;
    private DeviceBuffer? _backdropQuad;
    private bool _disposed;

    /// <summary>Vertex layout of the shared backdrop quad when a shader reads only <c>aPos</c> (location 0).</summary>
    public static readonly VertexLayoutDescription QuadPositionLayout = new(16,
        new VertexElementDescription("aPos", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

    /// <summary>Vertex layout of the shared backdrop quad when a shader reads <c>aPos</c> and <c>aUV</c> (locations 0 and 1).</summary>
    public static readonly VertexLayoutDescription QuadPositionUvLayout = new(16,
        new VertexElementDescription("aPos", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
        new VertexElementDescription("aUV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

    /// <summary>Six vertices (two triangles) of position and UV covering clip space, the same quad the OpenGL backdrop uses.</summary>
    public DeviceBuffer BackdropQuad
    {
        get
        {
            if (_backdropQuad != null) return _backdropQuad;
            float[] vertices =
            [
                -1f, -1f, 0f, 0f,   1f, -1f, 1f, 0f,   1f, 1f, 1f, 1f,
                -1f, -1f, 0f, 0f,   1f, 1f, 1f, 1f,   -1f, 1f, 0f, 1f
            ];
            _backdropQuad = Device.ResourceFactory.CreateBuffer(
                new BufferDescription((uint)(vertices.Length * sizeof(float)), BufferUsage.VertexBuffer));
            Device.UpdateBuffer(_backdropQuad, 0, vertices);
            return _backdropQuad;
        }
    }

    /// <summary>A cached sampler with the given filter and per-axis addressing (no mipmaps, no anisotropy).</summary>
    public Sampler GetSampler(SamplerFilter filter, SamplerAddressMode addressU, SamplerAddressMode addressV)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = (filter, addressU, addressV);
        if (!_samplers.TryGetValue(key, out var sampler))
        {
            sampler = Device.ResourceFactory.CreateSampler(new SamplerDescription(
                addressU, addressV, SamplerAddressMode.Clamp, filter, null, 0, 0, 16, 0, SamplerBorderColor.TransparentBlack));
            _samplers.Add(key, sampler);
        }
        return sampler;
    }

    public GraphicsDevice Device { get; }
    public Sampler LinearClamp { get; }
    public Sampler PointClamp { get; }

    internal VeldridFramebuffer GetScratchTarget(int index, VeldridFramebuffer scene, PixelFormat colorFormat)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        while (_scratchTargets.Count <= index)
            _scratchTargets.Add(new VeldridFramebuffer(Device, scene.Width, scene.Height, hasDepth: false, colorFormat: colorFormat));
        var target = _scratchTargets[index];
        if (target.Width != scene.Width || target.Height != scene.Height || target.ColorFormat != colorFormat)
        {
            target.Dispose();
            target = new VeldridFramebuffer(Device, scene.Width, scene.Height, hasDepth: false, colorFormat: colorFormat);
            _scratchTargets[index] = target;
        }
        return target;
    }

    public VeldridPostProcessor(GraphicsDevice device)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        var factory = device.ResourceFactory;
        LinearClamp = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipPoint, null, 0, 0, 16, 0, SamplerBorderColor.TransparentBlack));
        PointClamp = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinPoint_MagPoint_MipPoint, null, 0, 0, 16, 0, SamplerBorderColor.TransparentBlack));
    }

    /// <summary>
    /// Returns the pass for an embedded fragment shader, compiling it on first use. Passes drawn from the shared quad
    /// give their vertex shader and the matching <see cref="QuadPositionLayout"/> or <see cref="QuadPositionUvLayout"/>.
    /// </summary>
    public VeldridFullscreenPass GetPass(string fragmentShader, string vertexShader = "edge.vert",
        VertexLayoutDescription? vertexLayout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string key = vertexShader + "|" + fragmentShader;
        if (!_passes.TryGetValue(key, out var pass))
        {
            pass = new VeldridFullscreenPass(Device, fragmentShader, vertexShader, vertexLayout);
            _passes.Add(key, pass);
        }
        pass.ClearUniforms();
        return pass;
    }

    /// <summary>
    /// Records every effect against <paramref name="scene"/> and returns the target holding the result.
    /// That is <paramref name="scene"/> itself when the list is empty.
    /// </summary>
    public VeldridFramebuffer Run(CommandList commands, VeldridFramebuffer scene, IReadOnlyList<IVeldridPostEffect> effects)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(scene);
        if (effects.Count == 0) return scene;
        if (_spare == null || _spare.Width != scene.Width || _spare.Height != scene.Height ||
            _spare.ColorFormat != scene.ColorFormat)
        {
            _spare?.Dispose();
            _spare = new VeldridFramebuffer(Device, scene.Width, scene.Height, hasDepth: false, colorFormat: scene.ColorFormat);
        }
        var context = new VeldridPostContext(this, commands, scene, _spare);
        foreach (var effect in effects) effect.Apply(context);
        return context.Current;
    }

    /// <summary>Releases per-frame resources. Only call after the GPU finished the recorded commands.</summary>
    public void ReleaseFrame()
    {
        foreach (var pass in _passes.Values) pass.ReleaseFrame();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var pass in _passes.Values) pass.Dispose();
        _passes.Clear();
        _spare?.Dispose();
        _spare = null;
        foreach (var target in _scratchTargets) target.Dispose();
        _scratchTargets.Clear();
        _backdropQuad?.Dispose();
        _backdropQuad = null;
        foreach (var sampler in _samplers.Values) sampler.Dispose();
        _samplers.Clear();
        LinearClamp.Dispose();
        PointClamp.Dispose();
    }
}
