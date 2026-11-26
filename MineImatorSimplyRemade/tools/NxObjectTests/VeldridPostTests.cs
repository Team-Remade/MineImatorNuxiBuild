using System.Text;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Veldrid;
using Veldrid.SPIRV;
using CoreShader = MineImatorSimplyRemade.core.mdl.Shader;

internal static partial class Program
{
    /// <summary>Fragment shaders the OpenGL renderer draws with edge.vert over a whole image.</summary>
    private static readonly string[] VeldridFullscreenShaders =
    [
        "alpha_resolve.frag", "ambient_occlusion.frag", "bloom.frag", "chromatic_aberration.frag",
        "cinematic_bars.frag", "color_correction.frag", "color_management.frag", "depth_of_field.frag",
        "distort.frag", "edge.frag", "film_grain.frag", "fxaa.frag", "glow.frag", "heat_distort.frag",
        "indirect_denoise.frag", "indirect_lighting.frag", "lens_dirt.frag", "nuxt_composite.frag",
        "outline.frag", "pixelate.frag", "vignette.frag", "watermark.frag"
    ];

    /// <summary>Vertex/fragment pairs drawn without the shared fullscreen vertex shader.</summary>
    private static readonly (string Vertex, string Fragment)[] VeldridBackdropShaders =
    [
        ("sky.vert", "sky.frag"), ("background.vert", "background.frag")
    ];

    private static readonly (string Vertex, string Fragment)[] VeldridSceneShaders =
    [
        ("simple.vert", "simple.frag"),
        ("shadow_depth.vert", "shadow_depth.frag"), ("point_shadow_depth.vert", "point_shadow_depth.frag")
    ];

    /// <summary>CPU only: every registered fullscreen, backdrop, scene and shadow shader converts and compiles to SPIR-V.</summary>
    private static void VeldridGlslAdapterCompile(Fixture fixture)
    {
        var pairs = VeldridFullscreenShaders.Select(name => (Vertex: "edge.vert", Fragment: name))
            .Concat(VeldridBackdropShaders).Concat(VeldridSceneShaders).ToArray();
        var failures = new List<string>();
        foreach (var (vertexName, name) in pairs)
        {
            try
            {
                var program = VeldridGlslAdapter.Adapt(CoreShader.LoadShaderSource(vertexName),
                    CoreShader.LoadShaderSource(name), flipClipY: true, disableShaderGraphs: vertexName == "simple.vert");
                try
                {
                    SpirvCompilation.CompileGlslToSpirv(program.VertexSource, vertexName, ShaderStages.Vertex, new GlslCompileOptions(false));
                    SpirvCompilation.CompileGlslToSpirv(program.FragmentSource, name, ShaderStages.Fragment, new GlslCompileOptions(false));
                }
                catch (Exception ex)
                {
                    failures.Add($"{name}: {ex.Message}\n{VeldridFullscreenPass.Numbered(program.FragmentSource)}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.Message}");
            }
        }
        Check(failures.Count == 0, $"{failures.Count} of {pairs.Length} shaders failed:\n{string.Join("\n", failures)}");

        // std140 layout: vec3 followed by a float shares one 16-byte slot, arrays use a 16-byte stride, bools are uints.
        var layout = VeldridGlslAdapter.Adapt(
            "#version 330 core\nvoid main() { gl_Position = vec4(0.0); }\n",
            "#version 330 core\nout vec4 FragColor;\nuniform vec3 uColor;\nuniform float uAmount;\nuniform bool uFlag;\n" +
            "uniform vec2 uPair;\nuniform float uWeights[3];\nuniform mat4 uMatrix;\nuniform sampler2D uScene;\n" +
            "void main() { FragColor = texture(uScene, vec2(0.5)) * uAmount + vec4(uColor, uFlag ? uPair.x : uWeights[2] + uMatrix[0][0]); }\n",
            flipClipY: false);
        int OffsetOf(string name) => layout.Uniforms.First(member => member.Name == name).Offset;
        Equal(0, OffsetOf("uColor"), "vec3 offset");
        Equal(12, OffsetOf("uAmount"), "float packs after a vec3");
        Equal(16, OffsetOf("uFlag"), "bool offset");
        Equal(24, OffsetOf("uPair"), "vec2 aligns to eight bytes");
        Equal(32, OffsetOf("uWeights"), "float arrays start on 16 bytes");
        Equal(80, OffsetOf("uMatrix"), "matrices follow the 16-byte array stride");
        Equal(144, layout.UniformSize, "block size");
        Equal(1, layout.Samplers.Count, "sampler count");
        Equal(1, layout.Samplers[0].TextureBinding, "texture binding follows the uniform block");
        Equal(2, layout.Samplers[0].SamplerBinding, "sampler binding follows its texture");

        var preprocessing = VeldridGlslAdapter.Adapt(
            "#version 330 core\n#define KEEP_UNIFORM\n#ifdef KEEP_UNIFORM\nuniform float uIncluded;\n#else\nuniform float uExcluded;\n#endif\nuniform bool uFlag; uniform vec3 uColor;\nvoid main() { gl_Position = vec4(uIncluded + float(uFlag) + uColor.x); }\n",
            "#version 330 core\nout vec4 FragColor;\nvoid main() { FragColor = vec4(1.0); }\n",
            flipClipY: true, remapClipZ: true);
        Check(preprocessing.Uniforms.Any(member => member.Name == "uIncluded"), "active preprocessor branch uniform was collected");
        Check(preprocessing.Uniforms.All(member => member.Name != "uExcluded"), "inactive preprocessor branch uniform was omitted");
        Check(preprocessing.Uniforms.Any(member => member.Name == "uFlag") && preprocessing.Uniforms.Any(member => member.Name == "uColor"),
            "multiple loose uniforms on one source line were collected");
        Check(preprocessing.VertexSource.Contains("gl_Position.z = (gl_Position.z + gl_Position.w) * 0.5;", StringComparison.Ordinal),
            "zero-to-one clip depth remap was emitted");

        var scene = VeldridGlslAdapter.Adapt(CoreShader.LoadShaderSource("simple.vert"),
            CoreShader.LoadShaderSource("simple.frag"), flipClipY: true, remapClipZ: true, disableShaderGraphs: true);
        Equal("SceneData", scene.UniformBlocks.Single().Name, "named scene data block");
        Equal(192, scene.UniformBlocks.Single().Size, "std140 scene data block size");
        Equal(64, scene.UniformBlocks.Single().Members.Single(member => member.Name == "uAmbient").Offset,
            "named block std140 member offset");
        Equal(16, scene.Uniforms.Single(member => member.Name == "uPointLightPos").Stride,
            "vec3 uniform arrays use 16-byte std140 strides");
        Equal(32, scene.Uniforms.Single(member => member.Name == "uPointLightPos").ArrayLength,
            "scene point-light uniform array length");
        Equal(8, scene.Samplers.Single(sampler => sampler.Name == "uPointShadowMaps").ArrayLength, "point shadow texture array length");
        Check(scene.Samplers.Single(sampler => sampler.Name == "uPointShadowMaps").SamplerBinding -
            scene.Samplers.Single(sampler => sampler.Name == "uPointShadowMaps").TextureBinding == 8,
            "sampler array shares one sampler binding after its texture bindings");
        Check(scene.FragmentSource.Contains("uPointShadowMaps_tex_7", StringComparison.Ordinal) &&
            !scene.FragmentSource.Contains("uPointShadowMaps[", StringComparison.Ordinal),
            "literal sampler-array indices were expanded to distinct texture resources");
    }

    /// <summary>Runs Veldrid effects over an RGBA image in OpenGL row order and returns the result.</summary>
    private sealed class VeldridPostHarness : IDisposable
    {
        private readonly CommandList _commands;

        public VeldridPostHarness(GraphicsBackend backend)
        {
            // Same options the application uses for its offscreen scene device.
            var options = new GraphicsDeviceOptions(
                debug: false, swapchainDepthFormat: null, syncToVerticalBlank: false,
                resourceBindingModel: ResourceBindingModel.Improved,
                preferDepthRangeZeroToOne: true, preferStandardClipSpaceYDirection: true);
            Device = backend == GraphicsBackend.Vulkan
                ? GraphicsDevice.CreateVulkan(options)
                : GraphicsDevice.CreateD3D11(options);
            Processor = new VeldridPostProcessor(Device);
            _commands = Device.ResourceFactory.CreateCommandList();
            foreach (var (vertexName, fragmentName) in VeldridSceneShaders)
            {
                var program = VeldridGlslAdapter.Adapt(CoreShader.LoadShaderSource(vertexName),
                    CoreShader.LoadShaderSource(fragmentName), VeldridFullscreenPass.FlipClipY(Device), remapClipZ: true,
                    disableShaderGraphs: vertexName == "simple.vert");
                foreach (var shader in VeldridFullscreenPass.CompileShaders(Device, program, fragmentName)) shader.Dispose();
            }
        }

        public GraphicsDevice Device { get; }
        public VeldridPostProcessor Processor { get; }

        public byte[] Run(byte[] input, uint width, uint height, IReadOnlyList<IVeldridPostEffect> effects, float depth = 1f)
        {
            using var scene = new VeldridFramebuffer(Device, width, height, depthFormat: PixelFormat.R32_Float);
            Device.UpdateTexture(scene.ColorTexture, input, 0, 0, 0, width, height, 1, 0, 0);
            using var staging = Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
            _commands.Begin();
            _commands.SetFramebuffer(scene.Framebuffer);
            _commands.ClearDepthStencil(depth);
            var result = Processor.Run(_commands, scene, effects);
            _commands.CopyTexture(result.ColorTexture, staging);
            _commands.End();
            Device.SubmitCommands(_commands);
            Device.WaitForIdle();
            byte[] output = new byte[width * height * 4];
            var mapped = Device.Map(staging, MapMode.Read);
            try
            {
                for (uint y = 0; y < height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        IntPtr.Add(mapped.Data, checked((int)(y * mapped.RowPitch))), output, checked((int)(y * width * 4)), checked((int)width * 4));
            }
            finally { Device.Unmap(staging); }
            Processor.ReleaseFrame();
            return output;
        }

        public void Dispose()
        {
            Device.WaitForIdle();
            _commands.Dispose();
            Processor.Dispose();
            Device.Dispose();
        }
    }

    /// <summary>Runs every Veldrid pixel check on each backend the machine supports (D3D11 is Windows-only).</summary>
    private static void VeldridPostGraphics(Fixture fixture)
    {
        int ran = 0;
        // One OpenGL context for every backend: the application caches compiled programs in statics that belong to
        // the context that created them, so a second context in the same process would reuse dead program ids.
        using var oracle = new GlPostOracle(96, 64);
        foreach (var backend in new[] { GraphicsBackend.Direct3D11, GraphicsBackend.Vulkan })
        {
            if (!GraphicsDevice.IsBackendSupported(backend))
            {
                Console.WriteLine($"  {backend}: not supported on this machine, skipped");
                continue;
            }
            Console.WriteLine($"  == {backend} ==");
            using var harness = new VeldridPostHarness(backend);
            VeldridPixelate(harness);
            VeldridSceneOrientation(harness, oracle);
            VeldridEffectParity(harness, oracle);
            VeldridChainParity(harness, oracle);
            VeldridBackdropParity(harness, oracle);
            ran++;
        }
        Check(ran > 0, "At least one Veldrid backend (Direct3D 11 or Vulkan) must be available to run this check");
    }

    private static void VeldridPixelate(VeldridPostHarness harness)
    {
        const uint width = 100, height = 80;
        int Pixel(int x, int y) => (y * (int)width + x) * 4;
        byte[] input = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int p = Pixel(x, y);
                input[p] = (byte)(x * 2);
                input[p + 1] = (byte)(y * 3);
                input[p + 2] = (byte)(x + y);
                input[p + 3] = (byte)((x * 7 + y * 11) % 256);
            }
        byte[] Expected(byte[] source, int size)
        {
            byte[] result = new byte[source.Length];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int left = x / size * size, bottom = y / size * size;
                    int sx = (left + Math.Min(left + size, (int)width)) / 2;
                    int sy = (bottom + Math.Min(bottom + size, (int)height)) / 2;
                    Array.Copy(source, Pixel(sx, sy), result, Pixel(x, y), 4);
                }
            return result;
        }
        var camera = new CameraSceneObject();
        byte[] Render() => harness.Run(input, width, height, VeldridCameraEffects.Pixelate(camera));
        Check(input.SequenceEqual(Render()), "Veldrid: no effect leaves exact original pixels");
        camera.AddEffect(CameraEffectType.Pixelate);
        foreach (float size in new[] { 1f, 2f, 7f, 7.49f, 7.5f, 256f })
        {
            camera.Effects[0].Pixelate.Size = size;
            Check(Expected(input, (int)MathF.Floor(size + 0.5f)).SequenceEqual(Render()),
                $"Veldrid: exact {size}px blocks, center samples, alpha and partial edge blocks");
        }
        camera.Effects[0].Pixelate.Size = 7f;
        camera.AddEffect(CameraEffectType.Pixelate);
        camera.Effects[1].Pixelate.Size = 11f;
        Check(Expected(Expected(input, 7), 11).SequenceEqual(Render()), "Veldrid: repeated pixelate effects stack in order");
        Check(Render().SequenceEqual(Render()), "Veldrid: deterministic output");
    }
}
