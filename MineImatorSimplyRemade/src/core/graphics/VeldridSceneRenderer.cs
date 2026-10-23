using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using Silk.NET.OpenGL;
using Veldrid;
using Veldrid.SPIRV;
using Shader = Veldrid.Shader;
using Sampler = Veldrid.Sampler;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// Experimental native offscreen scene pass. OpenGL is used only to import textures
/// and upload the completed image; the caller owns both devices and presentation.
/// Call on the thread with the source/destination OpenGL context current.
/// Lighting is intentionally limited to ambient, directional diffuse and emission.
/// Shadows, fog, normal maps, SSS, mipmaps and postprocessing are not implemented.
/// Alpha-mask opacity is supported, but independent text outline colors are not.
/// Animated spritesheets share a renderer-local animation clock.
/// </summary>
public sealed class VeldridSceneRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly GL _gl;
    private readonly CommandList _commands;
    private readonly ResourceLayout _layout;
    private readonly Shader[] _shaders;
    private readonly VeldridTexture _white;
    private readonly Sampler _pointSampler;
    private readonly Sampler _linearSampler;
    private readonly Dictionary<(bool Wire, bool Overlay, FaceCullMode Cull), Pipeline> _pipelines = new();
    private readonly Dictionary<uint, VeldridTexture> _textures = new();
    private readonly List<IDisposable> _frameResources = new();
    private VeldridFramebuffer? _target;
    private Veldrid.Texture? _staging;
    private byte[] _pixels = Array.Empty<byte>();
    private mat4 _viewProjection;
    private bool _wireframe, _recording, _disposed, _hasDraws;
    private double _animationTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawUniforms
    {
        public mat4 Mvp, NormalMatrix;
        public vec4 Albedo, Blend, Mix, Emission, Ambient, LightColor, LightDirection, Options;
    }

    public VeldridSceneRenderer(GraphicsDevice device, GL gl)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        var factory = device.ResourceFactory;
        _commands = factory.CreateCommandList();
        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("DrawData", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("AlbedoTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("AlphaTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        const string uniforms = """
            layout(set = 0, binding = 0) uniform DrawData {
                mat4 Mvp;
                mat4 NormalMatrix;
                vec4 Albedo;
                vec4 Blend;
                vec4 Mix;
                vec4 Emission;
                vec4 Ambient;
                vec4 LightColor;
                vec4 LightDirection;
                vec4 Options;
            };
            """;
        string vertex = "#version 450\n" + uniforms + """
            layout(location = 0) in vec3 Position;
            layout(location = 1) in vec3 Normal;
            layout(location = 2) in vec2 TexCoord;
            layout(location = 0) out vec3 vNormal;
            layout(location = 1) out vec2 vUv;
            void main() {
                gl_Position = Mvp * vec4(Position, 1.0);
                if (Options.y > 0.5) gl_Position.z = (gl_Position.z + gl_Position.w) * 0.5;
                gl_Position.y *= Options.z;
                vNormal = mat3(NormalMatrix) * Normal;
                vNormal = length(vNormal) > 0.00001 ? normalize(vNormal) : vec3(0, 1, 0);
                vUv = TexCoord;
            }
            """;
        string fragment = "#version 450\n" + uniforms + """
            layout(set = 0, binding = 1) uniform texture2D AlbedoTexture;
            layout(set = 0, binding = 2) uniform texture2D AlphaTexture;
            layout(set = 0, binding = 3) uniform sampler SurfaceSampler;
            layout(location = 0) in vec3 vNormal;
            layout(location = 1) in vec2 vUv;
            layout(location = 0) out vec4 Color;
            void main() {
                vec4 texel = texture(sampler2D(AlbedoTexture, SurfaceSampler), vUv);
                vec4 mask = texture(sampler2D(AlphaTexture, SurfaceSampler), vUv);
                vec3 base = mix(texel.rgb * Albedo.rgb * Blend.rgb, Mix.rgb, clamp(Mix.a, 0.0, 1.0));
                float alpha = texel.a * mask.a * Albedo.a * Blend.a;
                if (alpha <= 0.0) discard;
                vec3 normal = length(vNormal) > 0.00001 ? normalize(vNormal) : vec3(0, 1, 0);
                if (!gl_FrontFacing) normal = -normal;
                vec3 light = Ambient.rgb + LightColor.rgb * max(dot(normal, normalize(LightDirection.xyz)), 0.0);
                vec3 result = Options.x > 0.5 ? base : base * light + Emission.rgb * texel.rgb;
                Color = vec4(result, alpha);
            }
            """;
        // No automatic clip-space fixups: the shader handles the device conventions explicitly.
        try
        {
            _shaders = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertex), "main"),
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragment), "main"),
                new CrossCompileOptions());
            _white = VeldridTexture.CreateFromRgbaBytes(device, [255, 255, 255, 255], 1, 1);
            var sampler = SamplerDescription.Point;
            sampler.AddressModeU = sampler.AddressModeV = sampler.AddressModeW = SamplerAddressMode.Wrap;
            _pointSampler = factory.CreateSampler(sampler);
            sampler.Filter = SamplerFilter.MinLinear_MagLinear_MipPoint;
            _linearSampler = factory.CreateSampler(sampler);
        }
        catch
        {
            _linearSampler?.Dispose();
            _pointSampler?.Dispose();
            _white?.Dispose();
            if (_shaders != null)
                foreach (var shader in _shaders) shader.Dispose();
            _layout.Dispose();
            _commands.Dispose();
            throw;
        }
    }

    public void BeginFrame(uint width, uint height, mat4 view, mat4 projection, Vector4 clearColor, bool wireframe = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recording) throw new InvalidOperationException("EndFrame must complete before BeginFrame.");
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_target == null || _target.Width != width || _target.Height != height)
        {
            _target?.Dispose();
            _staging?.Dispose();
            _target = new VeldridFramebuffer(_device, width, height, depthFormat: Veldrid.PixelFormat.R32_Float);
            _staging = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1, Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
            _pixels = new byte[checked((int)(width * height * 4))];
        }
        _viewProjection = projection * view;
        _wireframe = wireframe;
        _hasDraws = false;
        if (Mesh.AdvanceAnimatedTextures)
            _animationTime += Mesh.DeltaTime * Mesh.AnimatedTextureSpeedScale;
        _commands.Begin();
        _recording = true;
        _commands.SetFramebuffer(_target.Framebuffer);
        _commands.SetFullViewports();
        _commands.SetFullScissorRects();
        _commands.ClearColorTarget(0, new RgbaFloat(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W));
        _commands.ClearDepthStencil(1f);
    }

    /// <summary>Records triangles in caller order, preserving the editor's alpha-blended depth-writing layers.</summary>
    public void Draw(Mesh mesh, mat4 model, bool forceUnshaded = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_recording) throw new InvalidOperationException("BeginFrame must be called before Draw.");
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.PickOnly || mesh.Vertices.Count == 0 || mesh.Alpha <= 0) return;
        var vertices = BuildVertices(mesh);
        var gpuMesh = new VeldridMesh();
        _frameResources.Add(gpuMesh);
        gpuMesh.UploadVertices(_device, vertices);
        if (mesh.Indices is { Length: > 0 })
        {
            foreach (uint index in mesh.Indices)
                if (index >= vertices.Length) throw new ArgumentException("Mesh index exceeds vertex count.", nameof(mesh));
            gpuMesh.UploadIndices(_device, mesh.Indices);
        }
        bool hasUvs = mesh.TexCoords.Count == vertices.Length;
        var texture = hasUvs ? ImportTexture(mesh.TextureId) : _white;
        var mask = hasUvs ? ImportTexture(mesh.AlphaMaskTextureId) : _white;
        var data = new DrawUniforms
        {
            Mvp = _viewProjection * model,
            NormalMatrix = MathF.Abs(model.Determinant) > 1e-12f ? model.Inverse.Transposed : mat4.Identity,
            Albedo = new vec4(mesh.Albedo, mesh.Alpha),
            Blend = mesh.BlendColor,
            Mix = mesh.MixColor,
            Emission = new vec4(mesh.EmissionEnabled ? mesh.EmissionColor * mesh.EmissionEnergy : vec3.Zero, 0),
            Ambient = new vec4(Mesh.GlobalAmbientColor * Mesh.GlobalAmbientStrength, 0),
            LightColor = new vec4(Mesh.GlobalFillLightColor * Mesh.GlobalFillLightStrength, 0),
            LightDirection = new vec4(1, 1, 1, 0),
            Options = new vec4(forceUnshaded || mesh.Unlit ? 1 : 0,
                _device.IsDepthRangeZeroToOne ? 1 : 0, _device.IsClipSpaceYInverted ? -1 : 1, 0)
        };
        var uniform = _device.ResourceFactory.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<DrawUniforms>(), BufferUsage.UniformBuffer));
        _frameResources.Add(uniform);
        _device.UpdateBuffer(uniform, 0, ref data);
        var set = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            _layout, uniform, texture.TextureView, mask.TextureView, mesh.BlurTexture ? _linearSampler : _pointSampler));
        _frameResources.Add(set);
        FaceCullMode cull = mesh.DoubleSided ? FaceCullMode.None : mesh.CullFrontFaces ? FaceCullMode.Front : FaceCullMode.Back;
        var key = (_wireframe, mesh.DepthTestDisabled, cull);
        if (!_pipelines.TryGetValue(key, out var pipeline))
        {
            pipeline = _device.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = new DepthStencilStateDescription(!mesh.DepthTestDisabled,
                    !mesh.DepthTestDisabled, ComparisonKind.Less),
                RasterizerState = new RasterizerStateDescription(cull,
                    _wireframe ? PolygonFillMode.Wireframe : PolygonFillMode.Solid,
                    _device.IsClipSpaceYInverted ? FrontFace.Clockwise : FrontFace.CounterClockwise, true, false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = [_layout],
                ShaderSet = new ShaderSetDescription([VeldridVertex.LayoutDescription], _shaders),
                Outputs = _target!.Framebuffer.OutputDescription
            });
            _pipelines.Add(key, pipeline);
        }
        _commands.SetPipeline(pipeline);
        _commands.SetGraphicsResourceSet(0, set);
        gpuMesh.Draw(_commands);
        _hasDraws = true;
    }

    private VeldridVertex[] BuildVertices(Mesh mesh)
    {
        var vertices = new VeldridVertex[mesh.Vertices.Count];
        bool morph = mesh.ShapeKeys.Any(key => key.Weight != 0 && key.Deltas.Length >= vertices.Length * 3);
        float scaleV = 1, offsetV = 0;
        if (!string.IsNullOrEmpty(mesh.AnimationKey) &&
            TerrainAtlas.AnimatedTextures.TryGetValue(mesh.AnimationKey, out var animation) &&
            animation.Frames.Length > 0 && animation.TotalFrames > 0 && animation.FrameTime > 0)
        {
            int frame = (int)((_animationTime / (animation.FrameTime / 20.0)) % animation.Frames.Length);
            scaleV = 1f / animation.TotalFrames;
            offsetV = animation.Frames[frame] * scaleV;
        }
        for (int i = 0; i < vertices.Length; i++)
        {
            vec3 position = mesh.Vertices[i];
            if (morph)
                foreach (var key in mesh.ShapeKeys)
                    if (key.Weight != 0 && key.Deltas.Length >= vertices.Length * 3)
                        position += key.Weight * new vec3(key.Deltas[i * 3], key.Deltas[i * 3 + 1], key.Deltas[i * 3 + 2]);
            vec2 uv = mesh.TexCoords.Count == vertices.Length ? mesh.TexCoords[i] : vec2.Zero;
            uv.y = uv.y * scaleV + offsetV;
            uv *= mesh.TextureRepeat;
            if (mesh.TextureMirror.x) uv.x = MathF.Abs((uv.x * 0.5f - MathF.Floor(uv.x * 0.5f)) * 2 - 1);
            if (mesh.TextureMirror.y) uv.y = MathF.Abs((uv.y * 0.5f - MathF.Floor(uv.y * 0.5f)) * 2 - 1);
            vertices[i] = new VeldridVertex
            {
                Position = position,
                Normal = mesh.Normals.Count == vertices.Length ? mesh.Normals[i] : vec3.UnitY,
                TexCoord = uv + mesh.TextureOffset
            };
        }
        if (morph || mesh.Normals.Count != vertices.Length)
        {
            var normals = new vec3[vertices.Length];
            int count = mesh.Indices is { Length: > 0 } ? mesh.Indices.Length : vertices.Length;
            for (int i = 0; i + 2 < count; i += 3)
            {
                int a = mesh.Indices is { Length: > 0 } ? checked((int)mesh.Indices[i]) : i;
                int b = mesh.Indices is { Length: > 0 } ? checked((int)mesh.Indices[i + 1]) : i + 1;
                int c = mesh.Indices is { Length: > 0 } ? checked((int)mesh.Indices[i + 2]) : i + 2;
                if ((uint)a >= vertices.Length || (uint)b >= vertices.Length || (uint)c >= vertices.Length)
                    throw new ArgumentException("Mesh index exceeds vertex count.", nameof(mesh));
                vec3 normal = vec3.Cross(vertices[b].Position - vertices[a].Position, vertices[c].Position - vertices[a].Position);
                normals[a] += normal; normals[b] += normal; normals[c] += normal;
            }
            for (int i = 0; i < vertices.Length; i++)
                if (normals[i].LengthSqr > 1e-12f) vertices[i].Normal = normals[i].Normalized;
        }
        for (int i = 0; i < vertices.Length; i++)
        {
            ref var vertex = ref vertices[i];
            if (mesh.GpuBend is { } bend && bend.Angle.LengthSqr > 0)
            {
                vec3 local = vertex.Position - bend.ShapePosition;
                vec3 source = (bend.InverseShapeTransform * new vec4(local, 1)).xyz;
                float segment = source[bend.SegmentAxis] - bend.AxisStart;
                float weight = segment < bend.BendStart ? 0 : segment >= bend.BendEnd ? 1 :
                    1 - (bend.BendEnd - segment) / bend.BendSize;
                if (bend.InvertWeight) weight = 1 - weight;
                vec3 angle = bend.Sharp ? bend.Angle * weight : BendHelper.GetBendVector(bend.Angle, weight);
                bool initial = MathF.Abs(segment) <= Math.Max(1e-7f, MathF.Abs(bend.AxisStart) * 1e-6f);
                vec3 scale = new(1 + (!bend.Plane && !initial ? weight * 0.005f : 0));
                if (bend.Sharp && (!bend.Plane || initial) && bend.ScaleCorrectionAxis >= 0 && segment > bend.BendStart && segment < bend.BendEnd)
                {
                    float triangle = (weight <= 0.5f ? weight : 1 - weight) * 2;
                    float degrees = MathF.Abs(bend.Angle[bend.ScaleCorrectionAxis]);
                    if (degrees > 90) degrees = 180 - degrees;
                    float correction = MathF.Pow(triangle * Math.Clamp(degrees / 90, 0, 1), 3) / 2.5f;
                    for (int axis = 0; axis < 3; axis++)
                        if (axis != bend.ScaleCorrectionAxis) scale[axis] += correction;
                }
                mat4 rotation = mat4.RotateY(BendHelper.DegToRad(angle.y)) *
                    mat4.RotateX(BendHelper.DegToRad(angle.x)) * mat4.RotateZ(BendHelper.DegToRad(angle.z));
                vertex.Position = bend.Pivot + (rotation * new vec4(scale * (local - bend.Pivot), 0)).xyz + bend.ShapePosition;
                vertex.Normal = (rotation * new vec4(scale * vertex.Normal, 0)).xyz;
            }
            if (mesh.IsSkinned && mesh.BoneWeights.Count == vertices.Length && mesh.BoneMatrices is { Count: > 0 } bones)
            {
                vec4 position = vec4.Zero, normal = vec4.Zero;
                float total = 0;
                for (int j = 0; j < 4; j++)
                {
                    int bone = mesh.BoneIndices[i][j];
                    float weight = mesh.BoneWeights[i][j];
                    if (bone < 0 || bone >= bones.Count || weight <= 0) continue;
                    position += weight * (bones[bone] * new vec4(vertex.Position, 1));
                    normal += weight * (bones[bone] * new vec4(vertex.Normal, 0));
                    total += weight;
                }
                if (total > 0) { vertex.Position = position.xyz / total; vertex.Normal = normal.xyz; }
            }
        }
        return vertices;
    }

    private VeldridTexture ImportTexture(uint id)
    {
        if (id == 0) return _white;
        if (_textures.TryGetValue(id, out var existing)) return existing;
        var (pixels, width, height) = TransferGlTexture(id, true, 0, 0, null);
        var texture = VeldridTexture.CreateFromRgbaBytes(_device, pixels, width, height);
        _textures.Add(id, texture);
        _frameResources.Add(texture);
        return texture;
    }

    public void EndFrame(uint destinationGlTexture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_recording) throw new InvalidOperationException("BeginFrame must be called before EndFrame.");
        try
        {
            if (!_hasDraws)
            {
                // Vulkan defers attachment clears until a render pass starts. An
                // incomplete triangle starts that pass without generating fragments.
                // This CPU-only Mesh never allocates or draws OpenGL resources.
                using var clearMesh = new Mesh(_gl) { DoubleSided = true, Unlit = true };
                clearMesh.Vertices.Add(vec3.Zero);
                Draw(clearMesh, mat4.Identity, forceUnshaded: true);
            }
            _commands.CopyTexture(_target!.ColorTexture, _staging!);
            _commands.End();
            _recording = false;
            _device.SubmitCommands(_commands);
            _device.WaitForIdle();
            var mapped = _device.Map(_staging!, MapMode.Read);
            try
            {
                int rowBytes = checked((int)_target.Width * 4);
                for (uint y = 0; y < _target.Height; y++)
                {
                    // Existing GL viewport consumers expect row zero at the bottom.
                    uint sourceY = _device.IsUvOriginTopLeft ? _target.Height - 1 - y : y;
                    Marshal.Copy(IntPtr.Add(mapped.Data, checked((int)(sourceY * mapped.RowPitch))),
                        _pixels, checked((int)y * rowBytes), rowBytes);
                }
            }
            finally { _device.Unmap(_staging!); }
            TransferGlTexture(destinationGlTexture, false, _target.Width, _target.Height, _pixels);
        }
        finally
        {
            // Wait before disposing even if submission/map/upload failed.
            _device.WaitForIdle();
            ReleaseFrame();
        }
    }

    private unsafe (byte[] Pixels, uint Width, uint Height) TransferGlTexture(uint id, bool read, uint width, uint height, byte[]? pixels)
    {
        if (!_gl.IsTexture(id)) throw new ArgumentException("The bridge requires an existing OpenGL 2D texture.", nameof(id));
        GLEnum bufferTarget = read ? GLEnum.PixelPackBuffer : GLEnum.PixelUnpackBuffer;
        GLEnum bufferBinding = read ? GLEnum.PixelPackBufferBinding : GLEnum.PixelUnpackBufferBinding;
        GLEnum[] stores = read
            ? [GLEnum.PackAlignment, GLEnum.PackRowLength, GLEnum.PackSkipPixels, GLEnum.PackSkipRows]
            : [GLEnum.UnpackAlignment, GLEnum.UnpackRowLength, GLEnum.UnpackSkipPixels, GLEnum.UnpackSkipRows];
        int binding = _gl.GetInteger(GLEnum.TextureBinding2D), buffer = _gl.GetInteger(bufferBinding);
        var previous = new int[stores.Length];
        for (int i = 0; i < stores.Length; i++) previous[i] = _gl.GetInteger(stores[i]);
        try
        {
            _gl.BindTexture(GLEnum.Texture2D, id);
            _gl.BindBuffer(bufferTarget, 0);
            for (int i = 0; i < stores.Length; i++) _gl.PixelStore(stores[i], i == 0 ? 1 : 0);
            _gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureWidth, out int actualWidth);
            _gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureHeight, out int actualHeight);
            if (actualWidth <= 0 || actualHeight <= 0) throw new InvalidOperationException("Bridge texture has no level-zero storage.");
            if (read)
            {
                width = (uint)actualWidth; height = (uint)actualHeight;
                pixels = new byte[checked(actualWidth * actualHeight * 4)];
                fixed (byte* data = pixels) _gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
            }
            else
            {
                if (width != actualWidth || height != actualHeight)
                    throw new InvalidOperationException("Destination GL texture dimensions must match BeginFrame.");
                fixed (byte* data = pixels)
                    _gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, width, height, GLEnum.Rgba, GLEnum.UnsignedByte, data);
            }
            return (pixels!, width, height);
        }
        finally
        {
            for (int i = 0; i < stores.Length; i++) _gl.PixelStore(stores[i], previous[i]);
            _gl.BindBuffer(bufferTarget, (uint)buffer);
            _gl.BindTexture(GLEnum.Texture2D, (uint)binding);
        }
    }

    private void ReleaseFrame()
    {
        for (int i = _frameResources.Count - 1; i >= 0; i--) _frameResources[i].Dispose();
        _frameResources.Clear();
        _textures.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_recording) { _commands.End(); _recording = false; }
        _device.WaitForIdle();
        _commands.Dispose();
        ReleaseFrame();
        foreach (var pipeline in _pipelines.Values) pipeline.Dispose();
        foreach (var shader in _shaders) shader.Dispose();
        _layout.Dispose();
        _white.Dispose();
        _pointSampler.Dispose();
        _linearSampler.Dispose();
        _staging?.Dispose();
        _target?.Dispose();
    }
}
