using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using GlmSharp;
using Veldrid;
using Veldrid.SPIRV;
using Sampler = Veldrid.Sampler;
using Shader = Veldrid.Shader;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// Runs one of the project's fragment shaders over a whole render target on a Veldrid device.
/// The GLSL comes from the same embedded resource the OpenGL renderer compiles, converted by
/// <see cref="VeldridGlslAdapter"/>, so uniforms and samplers keep their original names:
/// <c>pass.Set("uSize", 4f)</c> and <c>pass.SetTexture("uScene", view, sampler)</c>.
/// Targets use OpenGL row order (see <see cref="VeldridGlslAdapter"/>), so shaders that read
/// <c>gl_FragCoord</c> or call <c>texelFetch</c> match the OpenGL output pixel for pixel.
/// Each <see cref="Draw"/> snapshots the uniform values into its own buffer. Call
/// <see cref="ReleaseFrame"/> once the submitted commands have finished.
/// Passes without vertex inputs draw one oversized triangle from <c>gl_VertexID</c>; passes constructed
/// with a <see cref="VertexLayoutDescription"/> draw from the vertex buffer given to <see cref="Draw"/>.
/// </summary>
public sealed class VeldridFullscreenPass : IDisposable
{
    public enum BlendMode
    {
        /// <summary>The pass output replaces the target.</summary>
        Opaque,
        /// <summary>Straight alpha over the target: SrcAlpha/InvSrcAlpha colour, One/InvSrcAlpha alpha.</summary>
        Alpha,
        /// <summary>Premultiplied alpha over the target.</summary>
        Premultiplied,
        /// <summary>Additive colour accumulation without changing the target alpha.</summary>
        Additive,
        /// <summary>Source-alpha weighted additive colour, used by scene materials.</summary>
        AdditiveAlpha,
        /// <summary>Source-alpha weighted subtractive colour, used by scene materials.</summary>
        Subtract,
        /// <summary>Destination-colour multiply blend, used by scene materials.</summary>
        Multiply,
        /// <summary>Inverse-destination-colour screen blend, used by scene materials.</summary>
        Screen
    }

    private readonly GraphicsDevice _device;
    private readonly VertexLayoutDescription? _vertexLayout;
    private readonly VeldridGlslAdapter.Program _program;
    private readonly Shader[] _shaders;
    private readonly ResourceLayout _layout;
    private readonly byte[] _uniformData;
    private readonly Dictionary<string, VeldridGlslAdapter.UniformMember> _uniforms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VeldridGlslAdapter.SamplerMember> _samplerMembers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeviceBuffer?> _uniformBlockBuffers = new(StringComparer.Ordinal);
    private readonly TextureView?[] _textures;
    private readonly Sampler?[] _samplers;
    private readonly Dictionary<(OutputDescription Output, BlendMode Blend, bool DepthTest, bool DepthWrite,
        FaceCullMode Cull, PolygonFillMode Fill, bool WriteAuxiliaryAttachments), Pipeline> _pipelines = new();
    private readonly List<DeviceBuffer> _uniformBuffers = new();
    private readonly List<ResourceSet> _frameSets = new();
    private int _uniformBuffersUsed;
    private bool _disposed;

    public string Name { get; }

    /// <summary>Whether render targets must be stored bottom row first for this device (always true today).</summary>
    public static bool FlipClipY(GraphicsDevice device) => device.IsClipSpaceYInverted != device.IsUvOriginTopLeft;

    public VeldridFullscreenPass(GraphicsDevice device, string fragmentShader, string vertexShader = "edge.vert",
        VertexLayoutDescription? vertexLayout = null, bool disableShaderGraphs = false, bool remapClipZ = false)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _vertexLayout = vertexLayout;
        Name = fragmentShader;
        string vertexSource = MineImatorSimplyRemade.core.mdl.Shader.LoadShaderSource(vertexShader);
        string fragmentSource = MineImatorSimplyRemade.core.mdl.Shader.LoadShaderSource(fragmentShader);
        _program = VeldridGlslAdapter.Adapt(vertexSource, fragmentSource, FlipClipY(device), remapClipZ,
            disableShaderGraphs);
        _shaders = CompileShaders(device, _program, fragmentShader);
        try
        {
            _layout = device.ResourceFactory.CreateResourceLayout(_program.CreateLayoutDescription());
        }
        catch
        {
            foreach (var shader in _shaders) shader.Dispose();
            throw;
        }
        _uniformData = new byte[Math.Max(16, _program.UniformSize)];
        foreach (var member in _program.Uniforms) _uniforms[member.Name] = member;
        ApplyDefaults();
        foreach (var block in _program.UniformBlocks)
            if (!_uniformBlockBuffers.ContainsKey(block.Name)) _uniformBlockBuffers.Add(block.Name, null);
        _textures = new TextureView?[_program.Samplers.Sum(sampler => sampler.ArrayLength)];
        _samplers = new Sampler?[_program.Samplers.Count];
        for (int i = 0; i < _program.Samplers.Count; i++) _samplerMembers[_program.Samplers[i].Name] = _program.Samplers[i];
    }

    /// <summary>Compiles an adapted program; failures report the converted source with line numbers.</summary>
    internal static Shader[] CompileShaders(GraphicsDevice device, VeldridGlslAdapter.Program program, string name)
    {
        try
        {
            return device.ResourceFactory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(program.VertexSource), "main"),
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(program.FragmentSource), "main"),
                new CrossCompileOptions());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Veldrid could not compile '{name}': {ex.Message}\n--- vertex ---\n{Numbered(program.VertexSource)}\n--- fragment ---\n{Numbered(program.FragmentSource)}", ex);
        }
    }

    internal static string Numbered(string source)
    {
        var lines = source.Split('\n');
        var text = new StringBuilder();
        for (int i = 0; i < lines.Length; i++) text.Append($"{i + 1,4}: {lines[i]}\n");
        return text.ToString();
    }

    public bool HasUniform(string name) => _uniforms.ContainsKey(name);

    public bool HasTexture(string name) => _samplerMembers.ContainsKey(name);

    /// <summary>
    /// Resets every uniform to its GLSL initializer (zero when it has none), so a reused pass never
    /// leaks values from an earlier draw and unset uniforms behave as they do in OpenGL.
    /// </summary>
    public void ClearUniforms()
    {
        Array.Clear(_uniformData);
        ApplyDefaults();
    }

    private void ApplyDefaults()
    {
        foreach (var member in _program.Uniforms)
        {
            if (member.Default is not { } values) continue;
            for (int component = 0; component < values.Length; component++)
            {
                if (member.IsInteger || member.IsUnsigned || member.IsBool)
                    WriteInt(member, 0, component, (int)values[component]);
                else
                    WriteFloat(member, 0, component, (float)values[component]);
            }
        }
    }

    private VeldridGlslAdapter.UniformMember Require(string name, params string[] types)
    {
        if (!_uniforms.TryGetValue(name, out var member))
            throw new ArgumentException($"Shader '{Name}' declares no uniform '{name}'.", nameof(name));
        if (types.Length > 0 && Array.IndexOf(types, member.GlslType) < 0)
            throw new ArgumentException(
                $"Uniform '{name}' of shader '{Name}' is {member.GlslType}, not one of {string.Join("/", types)}.", nameof(name));
        return member;
    }

    private void WriteFloat(VeldridGlslAdapter.UniformMember member, int element, int component, float value)
        => Unsafe.WriteUnaligned(ref _uniformData[member.Offset + element * member.Stride + component * 4], value);

    private void WriteInt(VeldridGlslAdapter.UniformMember member, int element, int component, int value)
        => Unsafe.WriteUnaligned(ref _uniformData[member.Offset + element * member.Stride + component * 4], value);

    public void Set(string name, float value)
        => WriteFloat(Require(name, "float"), 0, 0, value);

    public void Set(string name, int value)
    {
        var member = Require(name, "int", "uint", "bool");
        WriteInt(member, 0, 0, member.IsBool ? (value != 0 ? 1 : 0) : value);
    }

    public void Set(string name, bool value)
        => WriteInt(Require(name, "bool", "int", "uint"), 0, 0, value ? 1 : 0);

    public void Set(string name, Vector2 value) => SetFloats(name, [value.X, value.Y], "vec2");
    public void Set(string name, Vector3 value) => SetFloats(name, [value.X, value.Y, value.Z], "vec3");
    public void Set(string name, Vector4 value) => SetFloats(name, [value.X, value.Y, value.Z, value.W], "vec4");
    public void Set(string name, vec2 value) => SetFloats(name, [value.x, value.y], "vec2");
    public void Set(string name, vec3 value) => SetFloats(name, [value.x, value.y, value.z], "vec3");
    public void Set(string name, vec4 value) => SetFloats(name, [value.x, value.y, value.z, value.w], "vec4");
    public void Set(string name, mat4 value) => SetFloats(name, value.ToArray(), "mat4");

    public void Set(string name, ivec2 value)
    {
        var member = Require(name, "ivec2");
        WriteInt(member, 0, 0, value.x);
        WriteInt(member, 0, 1, value.y);
    }

    /// <summary>
    /// Writes a float, vecN or matrix uniform from flat components. Arrays take every element in order;
    /// each element's components are contiguous in <paramref name="values"/>.
    /// </summary>
    public void SetFloats(string name, ReadOnlySpan<float> values, params string[] expectedTypes)
    {
        var member = Require(name, expectedTypes);
        if (member.IsInteger || member.IsUnsigned || member.IsBool)
            throw new ArgumentException($"Uniform '{name}' of shader '{Name}' holds integers.", nameof(name));
        int components = member.Components;
        int elements = Math.Max(1, member.ArrayLength);
        if (values.Length != components * elements)
            throw new ArgumentException(
                $"Uniform '{name}' of shader '{Name}' needs {components * elements} floats, got {values.Length}.", nameof(values));
        for (int element = 0; element < elements; element++)
            for (int component = 0; component < components; component++)
                WriteMatrixAwareFloat(member, element, component, values[element * components + component]);
    }

    private void WriteMatrixAwareFloat(VeldridGlslAdapter.UniformMember member, int element, int component, float value)
    {
        // std140 matrices are columns padded to vec4, so a 3x3 matrix skips one float per column.
        if (member.GlslType is "mat3" or "mat2")
        {
            int rows = member.GlslType == "mat3" ? 3 : 2;
            int column = component / rows, row = component % rows;
            Unsafe.WriteUnaligned(ref _uniformData[member.Offset + element * member.Stride + column * 16 + row * 4], value);
            return;
        }
        WriteFloat(member, element, component, value);
    }

    /// <summary>Writes an int, uint or ivecN uniform (or array) from flat components.</summary>
    public void SetInts(string name, ReadOnlySpan<int> values)
    {
        var member = Require(name);
        if (!member.IsInteger && !member.IsUnsigned)
            throw new ArgumentException($"Uniform '{name}' of shader '{Name}' holds floats.", nameof(name));
        int components = member.Components;
        int elements = Math.Max(1, member.ArrayLength);
        if (values.Length != components * elements)
            throw new ArgumentException(
                $"Uniform '{name}' of shader '{Name}' needs {components * elements} ints, got {values.Length}.", nameof(values));
        for (int element = 0; element < elements; element++)
            for (int component = 0; component < components; component++)
                WriteInt(member, element, component, values[element * components + component]);
    }

    public void SetTexture(string name, TextureView view, Sampler sampler)
    {
        if (!_samplerMembers.TryGetValue(name, out var member))
            throw new ArgumentException($"Shader '{Name}' declares no sampler '{name}'.", nameof(name));
        if (member.ArrayLength != 1)
            throw new ArgumentException($"Sampler '{name}' of shader '{Name}' is an array; specify an element index.", nameof(name));
        SetTexture(name, 0, view, sampler);
    }

    public void SetTexture(string name, int element, TextureView view, Sampler sampler)
    {
        if (!_samplerMembers.TryGetValue(name, out var member))
            throw new ArgumentException($"Shader '{Name}' declares no sampler '{name}'.", nameof(name));
        if ((uint)element >= (uint)member.ArrayLength)
            throw new ArgumentOutOfRangeException(nameof(element), $"Sampler '{name}' has {member.ArrayLength} element(s).");
        if (_samplers[member.SamplerSlot] is { } existing && !ReferenceEquals(existing, sampler))
            throw new ArgumentException($"Sampler array '{name}' shares one sampler across all elements.", nameof(sampler));
        _textures[member.TextureSlot + element] = view ?? throw new ArgumentNullException(nameof(view));
        _samplers[member.SamplerSlot] = sampler ?? throw new ArgumentNullException(nameof(sampler));
    }

    public void SetUniformBlock(string name, DeviceBuffer buffer)
    {
        if (!_uniformBlockBuffers.ContainsKey(name))
            throw new ArgumentException($"Shader '{Name}' declares no uniform block '{name}'.", nameof(name));
        _uniformBlockBuffers[name] = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    /// <summary>
    /// Records one fullscreen triangle into <paramref name="target"/>, or <paramref name="vertexCount"/> vertices
    /// from <paramref name="vertexBuffer"/> when the pass was built with a vertex layout.
    /// </summary>
    public void Draw(CommandList commands, Framebuffer target, BlendMode blend = BlendMode.Opaque,
        DeviceBuffer? vertexBuffer = null, uint vertexCount = 3, bool depthTest = false, bool depthWrite = false,
        FaceCullMode cull = FaceCullMode.None, PolygonFillMode fill = PolygonFillMode.Solid,
        bool writeAuxiliaryAttachments = true, bool issueDraw = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((_vertexLayout != null) != (vertexBuffer != null))
            throw new InvalidOperationException($"Shader '{Name}' {(_vertexLayout != null ? "needs" : "takes no")} a vertex buffer.");
        var resources = new List<BindableResource>(1 + _program.UniformBlocks.Count + _textures.Length + _samplers.Length);
        if (_program.HasUniforms)
        {
            var buffer = RentUniformBuffer();
            _device.UpdateBuffer(buffer, 0, _uniformData);
            resources.Add(buffer);
        }
        foreach (var block in _program.UniformBlocks)
        {
            if (_uniformBlockBuffers[block.Name] == null)
                throw new InvalidOperationException($"Shader '{Name}' uniform block '{block.Name}' has no buffer bound.");
            resources.Add(_uniformBlockBuffers[block.Name]!);
        }
        foreach (var sampler in _program.Samplers)
        {
            for (int element = 0; element < sampler.ArrayLength; element++)
            {
                var texture = _textures[sampler.TextureSlot + element];
                if (texture == null)
                    throw new InvalidOperationException($"Shader '{Name}' sampler '{sampler.Name}' element {element} has no texture bound.");
                resources.Add(texture);
            }
            var boundSampler = _samplers[sampler.SamplerSlot];
            if (boundSampler == null)
                throw new InvalidOperationException($"Shader '{Name}' sampler '{sampler.Name}' has no sampler bound.");
            resources.Add(boundSampler);
        }
        var set = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_layout, resources.ToArray()));
        _frameSets.Add(set);
        commands.SetFramebuffer(target);
        commands.SetViewport(0, new Viewport(0, 0, target.Width, target.Height, 0, 1));
        commands.SetScissorRect(0, 0, 0, target.Width, target.Height);
        commands.SetPipeline(GetPipeline(target.OutputDescription, blend, depthTest, depthWrite, cull, fill,
            writeAuxiliaryAttachments));
        if (vertexBuffer != null) commands.SetVertexBuffer(0, vertexBuffer);
        commands.SetGraphicsResourceSet(0, set);
        if (issueDraw) commands.Draw(vertexCount, 1, 0, 0);
    }

    private DeviceBuffer RentUniformBuffer()
    {
        if (_uniformBuffersUsed == _uniformBuffers.Count)
            _uniformBuffers.Add(_device.ResourceFactory.CreateBuffer(
                new BufferDescription((uint)_uniformData.Length, BufferUsage.UniformBuffer)));
        return _uniformBuffers[_uniformBuffersUsed++];
    }

    private Pipeline GetPipeline(OutputDescription output, BlendMode blend, bool depthTest, bool depthWrite,
        FaceCullMode cull, PolygonFillMode fill, bool writeAuxiliaryAttachments)
    {
        var key = (output, blend, depthTest, depthWrite, cull, fill, writeAuxiliaryAttachments);
        if (_pipelines.TryGetValue(key, out var existing)) return existing;
        var attachment = blend switch
        {
            BlendMode.Alpha => new BlendAttachmentDescription(true, BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha,
                BlendFunction.Add, BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFunction.Add),
            BlendMode.Premultiplied => new BlendAttachmentDescription(true, BlendFactor.One, BlendFactor.InverseSourceAlpha,
                BlendFunction.Add, BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFunction.Add),
            BlendMode.Additive => new BlendAttachmentDescription(true, BlendFactor.One, BlendFactor.One,
                BlendFunction.Add, BlendFactor.Zero, BlendFactor.One, BlendFunction.Add),
            BlendMode.AdditiveAlpha => new BlendAttachmentDescription(true, BlendFactor.SourceAlpha, BlendFactor.One,
                BlendFunction.Add, BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFunction.Add),
            BlendMode.Subtract => new BlendAttachmentDescription(true, BlendFactor.SourceAlpha, BlendFactor.One,
                BlendFunction.ReverseSubtract, BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFunction.Add),
            BlendMode.Multiply => new BlendAttachmentDescription(true, BlendFactor.DestinationColor,
                BlendFactor.InverseSourceAlpha, BlendFunction.Add, BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFunction.Add),
            BlendMode.Screen => new BlendAttachmentDescription(true, BlendFactor.InverseDestinationColor,
                BlendFactor.One, BlendFunction.Add, BlendFactor.One, BlendFactor.InverseSourceAlpha, BlendFunction.Add),
            _ => BlendAttachmentDescription.OverrideBlend
        };
        var attachments = new BlendAttachmentDescription[Math.Max(1, output.ColorAttachments.Length)];
        Array.Fill(attachments, BlendAttachmentDescription.OverrideBlend);
        attachments[0] = attachment;
        if (!writeAuxiliaryAttachments)
            for (int i = 1; i < attachments.Length; i++)
                attachments[i] = new BlendAttachmentDescription(false,
                    (ColorWriteMask)0, BlendFactor.One, BlendFactor.Zero, BlendFunction.Add,
                    BlendFactor.One, BlendFactor.Zero, BlendFunction.Add);
        var pipeline = _device.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = new BlendStateDescription(RgbaFloat.Black, attachments),
            DepthStencilState = new DepthStencilStateDescription(depthTest, depthWrite, ComparisonKind.Less),
            RasterizerState = new RasterizerStateDescription(cull, fill,
                FlipClipY(_device) ? FrontFace.Clockwise : FrontFace.CounterClockwise,
                depthClipEnabled: depthTest || depthWrite, scissorTestEnabled: false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = [_layout],
            ShaderSet = new ShaderSetDescription(_vertexLayout is { } layout ? [layout] : Array.Empty<VertexLayoutDescription>(), _shaders),
            Outputs = output
        });
        _pipelines.Add(key, pipeline);
        return pipeline;
    }

    /// <summary>Releases per-draw resource sets. Only call after the GPU finished the recorded commands.</summary>
    public void ReleaseFrame()
    {
        foreach (var set in _frameSets) set.Dispose();
        _frameSets.Clear();
        _uniformBuffersUsed = 0;
        Array.Clear(_textures);
        Array.Clear(_samplers);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseFrame();
        foreach (var pipeline in _pipelines.Values) pipeline.Dispose();
        _pipelines.Clear();
        foreach (var buffer in _uniformBuffers) buffer.Dispose();
        _uniformBuffers.Clear();
        _layout.Dispose();
        foreach (var shader in _shaders) shader.Dispose();
    }
}
