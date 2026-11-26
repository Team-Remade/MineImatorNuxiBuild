using System.Reflection;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using GL = Silk.NET.OpenGL.GL;
using GLEnum = Silk.NET.OpenGL.GLEnum;
using PanelViewport = MineImatorSimplyRemade.core.ui.Panels.Viewport;
using PropertiesPanel = MineImatorSimplyRemade.core.ui.Panels.PropertiesPanel;

internal static partial class Program
{
    /// <summary>OpenGL framebuffers the viewport's effect passes read and write.</summary>
    private readonly record struct GlPostTargets(uint Fbo, uint ColorTexture, uint GlowFbo, uint GlowTexture,
        uint GlowPingFbo, uint GlowPingTexture);

    /// <summary>
    /// A hidden OpenGL window and a real <c>Viewport</c>, used as the reference renderer: it runs the
    /// production <c>Apply*Pass</c> methods on an uploaded image and reads the result back.
    /// </summary>
    private sealed unsafe class GlPostOracle : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly Silk.NET.GLFW.Glfw _glfw;
        private readonly Silk.NET.GLFW.WindowHandle* _window;
        private readonly ImGuiContextPtr _previousContext;
        private readonly ImGuiContextPtr _context;

        public GlPostOracle(uint width, uint height)
        {
            Width = width;
            Height = height;
            _glfw = Silk.NET.GLFW.Glfw.GetApi();
            Check(_glfw.Init(), "GLFW initialization");
            _glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
            _glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
            _glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
            _glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
            _window = _glfw.CreateWindow(64, 64, "Veldrid parity oracle", null, null);
            Check(_window != null, "OpenGL context");
            _previousContext = ImGui.GetCurrentContext();
            _context = ImGui.CreateContext();
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DeltaTime = 1f / 60f;
            _glfw.MakeContextCurrent(_window);
            Gl = GL.GetApi(_glfw.GetProcAddress);
            MineImatorSimplyRemade.core.mdl.mineImator.MineImatorLoader.Instance.Initialize(Gl);
            Panel = new PropertiesPanel
            {
                TransparentBackground = false, ShadowsEnabled = false, FogEnabled = false,
                ColorManagementEnabled = false, AntiAliasingEnabled = false
            };
            Viewport = new PanelViewport { Gl = Gl, OverlaysEnabled = false, PropertiesPanel = Panel };
            Viewport.InitFramebuffer(width, height);
        }

        public GL Gl { get; }
        public PanelViewport Viewport { get; }
        public PropertiesPanel Panel { get; }
        public uint Width { get; }
        public uint Height { get; }

        private uint Field(string name) => (uint)typeof(PanelViewport).GetField(name, Flags)!.GetValue(Viewport)!;

        public object? Call(string method, params object?[] arguments)
        {
            var target = typeof(PanelViewport).GetMethod(method, Flags) ?? throw new MissingMethodException(nameof(PanelViewport), method);
            try
            {
                return target.Invoke(Viewport, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        /// <summary>Uploads <paramref name="input"/> as the scene image, runs a production pass and returns the result.</summary>
        public byte[] Run(byte[] input, Action<GlPostTargets> pass)
        {
            var targets = new GlPostTargets(Field("_fbo"), Field("_colorTex"), Field("_glowFbo"), Field("_glowTex"),
                Field("_glowPingFbo"), Field("_glowPingTex"));
            Gl.ActiveTexture(GLEnum.Texture0);
            Gl.BindTexture(GLEnum.Texture2D, targets.ColorTexture);
            fixed (byte* data = input)
                Gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, Width, Height, GLEnum.Rgba, GLEnum.UnsignedByte, data);
            Gl.Enable(GLEnum.DepthTest);
            Gl.Enable(GLEnum.CullFace);
            Gl.Enable(GLEnum.Blend);
            pass(targets);
            return Read(targets.ColorTexture, Width, Height);
        }

        public void SetDepth(float depth)
        {
            Gl.BindFramebuffer(GLEnum.Framebuffer, Field("_fbo"));
            Gl.ClearDepth(depth);
            Gl.Clear(Silk.NET.OpenGL.ClearBufferMask.DepthBufferBit);
        }

        /// <summary>Clears the viewport framebuffer and draws the production sky and background image into it.</summary>
        public byte[] RenderBackdrop(Camera camera, float[] clear)
        {
            Gl.BindFramebuffer(GLEnum.Framebuffer, Field("_fbo"));
            Gl.DrawBuffer(GLEnum.ColorAttachment0);
            Gl.Viewport(0, 0, Width, Height);
            Gl.Enable(GLEnum.DepthTest);
            Gl.DepthFunc(GLEnum.Less);
            Gl.Enable(GLEnum.CullFace);
            Gl.CullFace(GLEnum.Back);
            Gl.Disable(GLEnum.Blend);
            Gl.ClearColor(clear[0], clear[1], clear[2], clear[3]);
            Gl.Clear(Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit | Silk.NET.OpenGL.ClearBufferMask.DepthBufferBit);
            Viewport.RenderSkyPublic(camera, Width, Height);
            Viewport.RenderBackgroundPlanePublic(Width, Height);
            return Read(Field("_colorTex"), Width, Height);
        }

        public void SetField(string name, object value) =>
            (typeof(PanelViewport).GetField(name, Flags) ?? throw new MissingFieldException(nameof(PanelViewport), name))
                .SetValue(Viewport, value);

        public unsafe uint CreateTexture(byte[] rgba, int width, int height, bool linear, bool repeat)
        {
            uint texture = Gl.GenTexture();
            Gl.BindTexture(GLEnum.Texture2D, texture);
            fixed (byte* data = rgba)
                Gl.TexImage2D(GLEnum.Texture2D, 0, Silk.NET.OpenGL.InternalFormat.Rgba8, (uint)width, (uint)height, 0,
                    GLEnum.Rgba, GLEnum.UnsignedByte, data);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)(linear ? GLEnum.Linear : GLEnum.Nearest));
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)(linear ? GLEnum.Linear : GLEnum.Nearest));
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)(repeat ? GLEnum.Repeat : GLEnum.ClampToEdge));
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)(repeat ? GLEnum.Repeat : GLEnum.ClampToEdge));
            return texture;
        }

        public byte[] Read(uint texture, uint width, uint height)
        {
            byte[] pixels = new byte[width * height * 4];
            Gl.BindTexture(GLEnum.Texture2D, texture);
            fixed (byte* data = pixels)
                Gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
            return pixels;
        }

        public void Dispose()
        {
            Viewport.DisposeFramebufferResources();
            MineImatorSimplyRemade.core.mdl.mineImator.MineImatorLoader.Instance.Initialize(null!);
            Gl.Dispose();
            ImGui.DestroyContext(_context);
            ImGui.SetCurrentContext(_previousContext);
            _glfw.DestroyWindow(_window);
            _glfw.Terminate();
            _glfw.Dispose();
        }
    }

    /// <summary>Compares two RGBA images; <c>Outliers</c> counts pixels whose worst channel exceeds <paramref name="tolerance"/>.</summary>
    private static (int Max, double Mean, int Differing, int Outliers) ComparePixels(byte[] expected, byte[] actual, int tolerance)
    {
        Equal(expected.Length, actual.Length, "Image sizes match");
        int max = 0, differing = 0, outliers = 0;
        long total = 0;
        for (int i = 0; i < expected.Length; i += 4)
        {
            int pixelMax = 0;
            for (int c = 0; c < 4; c++)
            {
                int difference = Math.Abs(expected[i + c] - actual[i + c]);
                total += difference;
                pixelMax = Math.Max(pixelMax, difference);
            }
            max = Math.Max(max, pixelMax);
            if (pixelMax > 0) differing++;
            if (pixelMax > tolerance) outliers++;
        }
        return (max, total / (double)expected.Length, differing, outliers);
    }

    /// <summary>A gradient with hard edges and fractional alpha so every effect has something to change.</summary>
    private static byte[] MakeParityImage(uint width, uint height)
    {
        byte[] image = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int p = (y * (int)width + x) * 4;
                image[p] = (byte)(x * 255 / (width - 1));
                image[p + 1] = (byte)(y * 255 / (height - 1));
                image[p + 2] = (x / 8 + y / 8) % 2 == 0 ? (byte)220 : (byte)40;
                image[p + 3] = y < height / 2 ? (byte)255 : (byte)(128 + x * 127 / (width - 1));
            }
        return image;
    }

    private static CameraSceneObject CameraWithEffect(CameraEffectType type, Action<CameraEffect> configure)
    {
        var camera = new CameraSceneObject();
        camera.AddEffect(type);
        configure(camera.Effects[0]);
        return camera;
    }

    /// <summary>
    /// Draws a red quad in the upper-left corner through the OpenGL preview renderer and the Veldrid scene
    /// renderer. Both images must put red in the same place, which a centre-pixel check cannot prove.
    /// </summary>
    private static void VeldridSceneOrientation(VeldridPostHarness harness, GlPostOracle oracle)
    {
        var gl = oracle.Gl;
        using var renderer = new VeldridSceneRenderer(harness.Device, gl);
        using var preview = new PreviewRenderer(gl, 64) { AutoRotateSpeed = 0 };
        var camera = new Camera { Target = vec3.Zero, Pitch = 0, Yaw = 0, Distance = 4 };
        using var quad = new PlaneMesh(gl, 3, 3, PlaneOrientation.XY)
            { BlendColor = new vec4(1, 0, 0, 1), Unlit = true, DoubleSided = true };
        for (int i = 0; i < quad.Vertices.Count; i++)
            quad.Vertices[i] += new vec3(-1.5f, 1.5f, 0f);
        quad.Upload();

        preview.Render([quad], "orientation", 0, camera: camera);
        byte[] openGl = oracle.Read(preview.ColorTexture, 64, 64);
        renderer.BeginFrame(64, 64, camera.GetViewMatrix(), camera.GetProjectionMatrix(1), new System.Numerics.Vector4(0, 0, 0, 1));
        renderer.Draw(quad, mat4.Identity, true);
        renderer.EndFrame(preview.ColorTexture);
        byte[] veldrid = oracle.Read(preview.ColorTexture, 64, 64);
        var geometryCache = (System.Collections.IDictionary)typeof(VeldridSceneRenderer)
            .GetField("_staticGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        VeldridMesh CachedQuad() => ((ValueTuple<int, VeldridMesh>)geometryCache[quad]!).Item2;
        var firstGpuGeometry = CachedQuad();
        renderer.BeginFrame(64, 64, camera.GetViewMatrix(), camera.GetProjectionMatrix(1), new System.Numerics.Vector4(0, 0, 0, 1));
        renderer.Draw(quad, mat4.Identity, true);
        renderer.EndFrame(preview.ColorTexture);
        Check(ReferenceEquals(firstGpuGeometry, CachedQuad()), "Static geometry reuses its Veldrid buffers between frames");
        quad.Vertices[0] += new vec3(0, 0, 0.01f);
        quad.Upload();
        renderer.BeginFrame(64, 64, camera.GetViewMatrix(), camera.GetProjectionMatrix(1), new System.Numerics.Vector4(0, 0, 0, 1));
        renderer.Draw(quad, mat4.Identity, true);
        renderer.EndFrame(preview.ColorTexture);
        Check(!ReferenceEquals(firstGpuGeometry, CachedQuad()), "GeometryVersion changes replace cached Veldrid buffers");

        static bool Red(byte[] pixels, int index) => pixels[index] > 200 && pixels[index + 1] < 60 && pixels[index + 2] < 60;
        int glRed = 0, veldridRed = 0, both = 0;
        double veldridX = 0, veldridY = 0;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                int index = (y * 64 + x) * 4;
                bool a = Red(openGl, index), b = Red(veldrid, index);
                if (a) glRed++;
                if (b) { veldridRed++; veldridX += x; veldridY += y; }
                if (a && b) both++;
            }
        Check(glRed > 100 && veldridRed > 100, $"The quad is visible in both renderers (OpenGL {glRed}, Veldrid {veldridRed} red pixels)");
        Check(both >= glRed * 0.9 && both >= veldridRed * 0.9,
            $"Veldrid and OpenGL place the quad identically ({both} shared of {glRed}/{veldridRed} red pixels)");
        // OpenGL row order: row zero is the bottom, so an upper-left quad has a high mean row and a low mean column.
        Check(veldridY / veldridRed > 36 && veldridX / veldridRed < 28,
            $"Veldrid output is stored bottom row first (mean x {veldridX / veldridRed:F1}, mean row {veldridY / veldridRed:F1})");

        // Back-face culling: the double-sided quad above cannot show whether the winding survived the row-order
        // change. Of two single-sided quads (the second one mirrored, so its winding is reversed) exactly one
        // faces the camera, and Veldrid must keep the same one as OpenGL.
        using var faceA = new PlaneMesh(gl, 3, 3, PlaneOrientation.XY)
            { BlendColor = new vec4(1, 0, 0, 1), Unlit = true, DoubleSided = false };
        for (int i = 0; i < faceA.Vertices.Count; i++)
            faceA.Vertices[i] += new vec3(-1.5f, 1.5f, 0f);
        faceA.Upload();
        using var faceB = new PlaneMesh(gl, 3, 3, PlaneOrientation.XY)
            { BlendColor = new vec4(0, 0, 1, 1), Unlit = true, DoubleSided = false };
        for (int i = 0; i < faceB.Vertices.Count; i++)
            faceB.Vertices[i] = new vec3(1.5f - faceB.Vertices[i].x, faceB.Vertices[i].y - 1.5f, faceB.Vertices[i].z);
        faceB.Upload();
        var culled = new[] { faceA, faceB };
        preview.Render([.. culled], "culling", 0, camera: camera);
        byte[] glFaces = oracle.Read(preview.ColorTexture, 64, 64);
        renderer.BeginFrame(64, 64, camera.GetViewMatrix(), camera.GetProjectionMatrix(1), new System.Numerics.Vector4(0, 0, 0, 1));
        foreach (var face in culled) renderer.Draw(face, mat4.Identity, true);
        renderer.EndFrame(preview.ColorTexture);
        byte[] veldridFaces = oracle.Read(preview.ColorTexture, 64, 64);

        static int Count(byte[] pixels, bool red)
        {
            int count = 0;
            for (int i = 0; i < pixels.Length; i += 4)
                if (red ? pixels[i] > 200 && pixels[i + 1] < 60 && pixels[i + 2] < 60
                        : pixels[i + 2] > 200 && pixels[i] < 60 && pixels[i + 1] < 60)
                    count++;
            return count;
        }
        int glFront = Count(glFaces, true), glBack = Count(glFaces, false);
        int veldridFront = Count(veldridFaces, true), veldridBack = Count(veldridFaces, false);
        Check((glFront > 100) != (glBack > 100),
            $"OpenGL draws exactly one of the two single-sided quads (red {glFront}, blue {glBack} pixels)");
        bool redFacesCamera = glFront > 100;
        Check(redFacesCamera ? veldridFront > 100 && veldridBack <= 5 : veldridBack > 100 && veldridFront <= 5,
            $"Veldrid culls the same face as OpenGL (OpenGL red {glFront}/blue {glBack}, Veldrid red {veldridFront}/blue {veldridBack} pixels)");
        Console.WriteLine($"  Culling: OpenGL red {glFront}/blue {glBack}, Veldrid red {veldridFront}/blue {veldridBack} pixels");

        var oldSceneBuffer = Mesh.SceneUBO;
        var oldAmbient = Mesh.GlobalAmbientColor;
        float oldAmbientStrength = Mesh.GlobalAmbientStrength;
        var oldFillColor = Mesh.GlobalFillLightColor;
        float oldFillStrength = Mesh.GlobalFillLightStrength;
        var oldSunColor = Mesh.GlobalSunFillLightColor;
        var oldMoonColor = Mesh.GlobalMoonFillLightColor;
        bool oldShadows = Mesh.ShadowsEnabled;
        uint oldShadowMapTexture = Mesh.ShadowMapTexture;
        bool oldFog = Mesh.FogEnabled, oldHeightFog = Mesh.HeightFogEnabled;
        bool oldIndirectWorld = Mesh.IndirectWorldEnabled;
        var oldPointLights = Mesh.PointLights.ToArray();
        try
        {
            var ambient = new vec3(0.12f, 0.1f, 0.08f);
            var lightColor = new vec3(0.7f, 0.72f, 0.75f);
            var lightDirection = new vec3(1f, 1f, 1f).Normalized;
            Mesh.GlobalAmbientColor = ambient;
            Mesh.GlobalAmbientStrength = 1f;
            Mesh.GlobalFillLightColor = lightColor;
            Mesh.GlobalFillLightStrength = 1f;
            Mesh.GlobalSunFillLightColor = Mesh.GlobalMoonFillLightColor = vec3.Zero;
            Mesh.ShadowsEnabled = false;
            Mesh.ShadowMapTexture = 0;
            Mesh.FogEnabled = Mesh.HeightFogEnabled = false;
            Mesh.IndirectWorldEnabled = false;
            Mesh.PointLights.Clear();
            using var sceneData = new SceneUniformBuffer(gl);
            sceneData.Upload(mat4.Identity, ambient, lightDirection, lightColor,
                vec3.UnitY, vec3.Zero, vec3.UnitY, vec3.Zero, 0, false, false, false);
            Mesh.SceneUBO = sceneData;
            using var litQuad = new PlaneMesh(gl, 3, 3, PlaneOrientation.XY)
                { BlendColor = new vec4(0.7f, 0.6f, 0.5f, 1f), Unlit = false, DoubleSided = true };
            preview.Render([litQuad], "lit-scene-parity", 0, camera: camera);
            byte[] glLit = oracle.Read(preview.ColorTexture, 64, 64);
            renderer.BeginFrame(64, 64, camera.GetViewMatrix(), camera.GetProjectionMatrix(1), new System.Numerics.Vector4(0.12f, 0.12f, 0.14f, 1));
            renderer.Draw(litQuad, mat4.Identity);
            renderer.EndFrame(preview.ColorTexture);
            byte[] veldridLit = oracle.Read(preview.ColorTexture, 64, 64);
            int maxDifference = 0;
            long totalDifference = 0;
            for (int i = 0; i < glLit.Length; i++)
            {
                int difference = Math.Abs(glLit[i] - veldridLit[i]);
                maxDifference = Math.Max(maxDifference, difference);
                totalDifference += difference;
            }
            double meanDifference = totalDifference / (double)glLit.Length;
            Check(maxDifference <= 3 && meanDifference < 0.2,
                $"Lit production scene shader matches OpenGL (max {maxDifference}, mean {meanDifference:F3} channel levels)");

            Mesh.PointLights.Add((new vec3(0f, 0f, 2f), new vec3(0.9f, 0.82f, 0.7f), 8f, 1f,
                -1, vec3.Zero, 0f, 0f, 0f, 0.5f, false));
            preview.Render([litQuad], "lit-point-light-parity", 0, camera: camera);
            glLit = oracle.Read(preview.ColorTexture, 64, 64);
            renderer.BeginFrame(64, 64, camera.GetViewMatrix(), camera.GetProjectionMatrix(1), new System.Numerics.Vector4(0.12f, 0.12f, 0.14f, 1));
            renderer.Draw(litQuad, mat4.Identity);
            renderer.EndFrame(preview.ColorTexture);
            veldridLit = oracle.Read(preview.ColorTexture, 64, 64);
            maxDifference = 0;
            totalDifference = 0;
            for (int i = 0; i < glLit.Length; i++)
            {
                int difference = Math.Abs(glLit[i] - veldridLit[i]);
                maxDifference = Math.Max(maxDifference, difference);
                totalDifference += difference;
            }
            meanDifference = totalDifference / (double)glLit.Length;
            Check(maxDifference <= 3 && meanDifference < 0.2,
                $"Point-lit production scene shader matches OpenGL (max {maxDifference}, mean {meanDifference:F3} channel levels)");
        }
        finally
        {
            Mesh.SceneUBO = oldSceneBuffer;
            Mesh.GlobalAmbientColor = oldAmbient;
            Mesh.GlobalAmbientStrength = oldAmbientStrength;
            Mesh.GlobalFillLightColor = oldFillColor;
            Mesh.GlobalFillLightStrength = oldFillStrength;
            Mesh.GlobalSunFillLightColor = oldSunColor;
            Mesh.GlobalMoonFillLightColor = oldMoonColor;
            Mesh.ShadowsEnabled = oldShadows;
            Mesh.ShadowMapTexture = oldShadowMapTexture;
            Mesh.FogEnabled = oldFog;
            Mesh.HeightFogEnabled = oldHeightFog;
            Mesh.IndirectWorldEnabled = oldIndirectWorld;
            Mesh.PointLights.Clear();
            Mesh.PointLights.AddRange(oldPointLights);
        }
    }

    /// <summary>
    /// The sky and the background image must match the production OpenGL passes. Synthetic textures are injected into
    /// the viewport (as if the loaders had read them) so the test does not depend on installed game assets.
    /// </summary>
    private static void VeldridBackdropParity(VeldridPostHarness harness, GlPostOracle oracle)
    {
        uint width = oracle.Width, height = oracle.Height;
        using var renderer = new VeldridSceneRenderer(harness.Device, oracle.Gl);
        uint target = oracle.CreateTexture(new byte[width * height * 4], (int)width, (int)height, false, false);
        var panel = oracle.Panel;
        float[] clear = [0.18f, 0.18f, 0.18f, 1f];
        var camera = new Camera { Target = vec3.Zero, Pitch = 25, Yaw = 40, Distance = 6 };

        byte[] Pattern(int size, Func<int, int, byte[]> pixel)
        {
            byte[] data = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixel(x, y).CopyTo(data, (y * size + x) * 4);
            return data;
        }
        byte[] sun = Pattern(16, (x, y) => Math.Sqrt((x - 7.5) * (x - 7.5) + (y - 7.5) * (y - 7.5)) < 7
            ? [255, 230, 120, 255] : [0, 0, 0, 0]);
        byte[] moon = Pattern(16, (x, y) => Math.Sqrt((x - 7.5) * (x - 7.5) + (y - 7.5) * (y - 7.5)) < 6
            ? [(byte)(180 + x * 4), 190, 210, 255] : [0, 0, 0, 0]);
        byte[] clouds = Pattern(32, (x, y) => (x / 4 + y / 4) % 3 == 0 ? [255, 255, 255, 230] : [255, 255, 255, 0]);
        byte[] environment = Pattern(32, (x, y) => [(byte)(x * 8), (byte)(y * 8), (byte)(255 - x * 4 - y * 4), 255]);
        byte[] picture = Pattern(32, (x, y) => [(byte)(x * 8), (byte)(255 - y * 8), (byte)((x ^ y) * 8), 255]);

        oracle.SetField("_sunTexture", oracle.CreateTexture(sun, 16, 16, false, false));
        oracle.SetField("_loadedSunPath", panel.SunTexture);
        oracle.SetField("_moonTexture", oracle.CreateTexture(moon, 16, 16, false, false));
        oracle.SetField("_loadedMoonPath", panel.MoonTexture);
        oracle.SetField("_cloudTexture", oracle.CreateTexture(clouds, 32, 32, false, true));
        oracle.SetField("_loadedCloudPath", panel.CloudTexture);
        oracle.SetField("_attemptedEnvironmentPath", "");
        panel.UseSky = true;
        panel.TransparentBackground = false;

        void Case(string name, int tolerance, double outlierFraction)
        {
            byte[] expected = oracle.RenderBackdrop(camera, clear);
            var environmentState = oracle.Call("PrepareSceneEnvironment", camera)!;
            renderer.BeginFrame(width, height, camera.GetViewMatrix(), camera.GetProjectionMatrix((float)width / height),
                new System.Numerics.Vector4(clear[0], clear[1], clear[2], clear[3]));
            oracle.Call("DrawVeldridBackdrop", renderer, harness.Processor, camera, environmentState, width, height);
            renderer.EndFrame(target, harness.Processor, Array.Empty<IVeldridPostEffect>());
            byte[] actual = oracle.Read(target, width, height);
            var flat = new byte[expected.Length];
            for (int i = 0; i < flat.Length; i += 4)
            {
                flat[i] = (byte)(clear[0] * 255 + 0.5f); flat[i + 1] = (byte)(clear[1] * 255 + 0.5f);
                flat[i + 2] = (byte)(clear[2] * 255 + 0.5f); flat[i + 3] = 255;
            }
            Check(!expected.SequenceEqual(flat), $"{name}: OpenGL draws something over the clear colour");
            var (max, mean, differing, outliers) = ComparePixels(expected, actual, tolerance);
            Check(outliers <= outlierFraction * width * height,
                $"{name}: {outliers} of {width * height} pixels differ from OpenGL by more than {tolerance} levels (max {max}, mean {mean:F3})");
            Console.WriteLine($"  {name}: max {max}, mean {mean:F3}, {differing} differing pixels, {outliers} outliers");
        }

        foreach (float skyTime in new[] { 0f, 5f, 12f })
        {
            panel.SkyTime = skyTime;
            Case($"Procedural sky (time {skyTime})", 3, 0.01);
        }
        panel.SkyTime = 0f;

        oracle.SetField("_environmentTexture", oracle.CreateTexture(environment, 32, 32, true, false));
        foreach (string mode in new[] { "sphere", "box" })
        {
            panel.SkyMode = mode;
            Case($"Textured sky ({mode})", 3, 0.01);
        }
        panel.SkyMode = "minecraft";

        oracle.SetField("_backgroundTexture", oracle.CreateTexture(picture, 32, 32, true, false));
        oracle.SetField("_backgroundImageWidth", 32);
        oracle.SetField("_backgroundImageHeight", 32);
        oracle.SetField("_backgroundUserScale", 0.8f);
        oracle.SetField("_backgroundRotationRadians", 0.3f);
        oracle.SetField("_backgroundUserOffset", new System.Numerics.Vector2(0.1f, -0.05f));
        foreach (int mode in new[] { 0, 1, 2 })
        {
            oracle.SetField("_backgroundRenderMode", mode);
            Case($"Sky and background image (mode {mode})", 3, 0.01);
        }
    }

    /// <summary>
    /// The list assembled by <c>Viewport.BuildVeldridEffects</c> must equal the production OpenGL passes applied in the
    /// main viewport's order. Single effects can match while a mis-ordered chain still differs, because effects do not
    /// commute, so a reversed chain proves that this comparison would notice.
    /// </summary>
    private static void VeldridChainParity(VeldridPostHarness harness, GlPostOracle oracle)
    {
        uint width = oracle.Width, height = oracle.Height;
        byte[] input = MakeParityImage(width, height);
        var panel = oracle.Panel;
        var camera = new CameraSceneObject
            { BlendColor = new vec3(1f, 0.9f, 0.8f), MixColor = new vec4(0.2f, 0.4f, 0.6f, 0.25f) };
        CameraEffect Add(CameraEffectType type)
        {
            camera.AddEffect(type);
            return camera.Effects.Last();
        }
        var correction = Add(CameraEffectType.ColorCorrection).ColorCorrection;
        correction.Contrast = 120f; correction.Saturation = 130f; correction.Hue = 15f;
        var vignette = Add(CameraEffectType.Vignette).Vignette;
        vignette.Radius = 30f; vignette.Softness = 60f; vignette.Strength = 80f;
        var aberration = Add(CameraEffectType.ChromaticAberration).ChromaticAberration;
        aberration.RedOffset = 10f; aberration.BlueOffset = -10f;
        var distort = Add(CameraEffectType.Distort).Distort;
        distort.ZoomAmount = 80f; distort.DistortAmount = 30f;
        var heat = Add(CameraEffectType.HeatDistort).HeatDistort;
        heat.Strength = 30f; heat.Speed = 50f; heat.Scale = 60f;
        var grain = Add(CameraEffectType.FilmGrain).FilmGrain;
        grain.Strength = 0.3f; grain.Saturation = 0.5f; grain.Size = 2f;
        var pixelate = Add(CameraEffectType.Pixelate).Pixelate;
        pixelate.Size = 5f;
        var bars = Add(CameraEffectType.CinematicBars).CinematicBars;
        bars.Size = 12f; bars.Rotation = 5f; bars.ColorR = 0.1f;
        panel.ColorManagementEnabled = true;
        panel.ColorManagementToneMapper = 1;
        panel.ColorManagementLook = 0;
        panel.ColorManagementExposure = 0.5f;
        panel.AntiAliasingEnabled = true;

        foreach (bool transparent in new[] { false, true })
        {
            panel.TransparentBackground = transparent;
            string label = transparent ? "transparent background" : "opaque background";
            // Passes in the order of the main viewport's Rendered/Combined branch.
            byte[] expected = oracle.Run(input, t =>
            {
                oracle.Call("ResolveSceneAlpha", t.Fbo, t.GlowFbo, t.GlowTexture, width, height);
                oracle.Call("ApplyColorManagementPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, panel, camera);
                oracle.Call("ApplyColorCorrectionPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, camera);
                oracle.Call("ApplyVignettePass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, camera);
                oracle.Call("ApplyChromaticAberrationPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, camera);
                oracle.Call("ApplyDistortPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, camera);
                oracle.Call("ApplyHeatDistortPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, camera);
                oracle.Call("ApplyAntiAliasingPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, panel);
                oracle.Call("ApplyFilmGrainPass", t.Fbo, t.ColorTexture, t.GlowFbo, t.GlowTexture, width, height, camera);
                oracle.Call("ApplyPixelatePass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, camera);
                oracle.Call("ApplyWatermarkPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, panel);
                oracle.Call("ApplyCinematicBarsPass", t.Fbo, width, height, camera);
            });
            Check(!expected.SequenceEqual(input), $"Chain ({label}): the OpenGL passes change the image");

            object?[] arguments = [camera, new Camera(), null!, width, height, true, true, false];
            var effects = (List<IVeldridPostEffect>)oracle.Call("BuildVeldridEffects", arguments)!;
            Check(!(bool)arguments[7]!, $"Chain ({label}): no OpenGL tail without a watermark");
            // Colour management, colour correction, camera material, vignette, chromatic aberration, distort, heat
            // distort, FXAA, film grain, pixelate, Glow, Depth of Field, Bloom, Lens Dirt, Watermark and cinematic
            // bars, plus the alpha resolve on a transparent background.
            Check(effects.Count == (transparent ? 12 : 11),
                $"Chain ({label}): every effect is assembled (got {effects.Count} Veldrid effects)");
            byte[] actual = harness.Run(input, width, height, effects);
            var (max, mean, differing, outliers) = ComparePixels(expected, actual, 6);
            Check(outliers <= 0.03 * width * height,
                $"Chain ({label}): {outliers} of {width * height} pixels differ from the OpenGL chain by more than 6 levels (max {max}, mean {mean:F3})");

            effects.Reverse();
            var (_, _, _, reversedOutliers) = ComparePixels(expected, harness.Run(input, width, height, effects), 6);
            Check(reversedOutliers > 0.05 * width * height,
                $"Chain ({label}): a reversed chain must be detected, but only {reversedOutliers} pixels differ");
            Console.WriteLine($"  Effect chain ({label}): max {max}, mean {mean:F3}, {differing} differing pixels, {outliers} outliers; reversed chain {reversedOutliers} outliers");
        }
        panel.TransparentBackground = false;
        panel.ColorManagementEnabled = false;
        panel.AntiAliasingEnabled = false;
    }

    /// <summary>Every ported effect must match the production OpenGL pass on the same image.</summary>
    private static void VeldridEffectParity(VeldridPostHarness harness, GlPostOracle oracle)
    {
        uint width = oracle.Width, height = oracle.Height;
        byte[] input = MakeParityImage(width, height);
        // outlierFraction > 0 is only for effects driven by chaotic arithmetic hashes (film grain): OpenGL and
        // Direct3D round a few intermediate values differently, which flips the noise at fract() wrap-arounds.
        void Case(string name, int tolerance, Func<byte[]> openGl, Func<IReadOnlyList<IVeldridPostEffect>> veldrid,
            double outlierFraction = 0)
        {
            byte[] expected = openGl();
            Check(!expected.SequenceEqual(input), $"{name}: the OpenGL pass changes the image, so the comparison is meaningful");
            var effects = veldrid();
            Check(effects.Count > 0, $"{name}: Veldrid builds at least one effect");
            byte[] actual = harness.Run(input, width, height, effects);
            var (max, mean, differing, outliers) = ComparePixels(expected, actual, tolerance);
            Check(outliers <= outlierFraction * width * height,
                $"{name}: {outliers} of {width * height} pixels differ from OpenGL by more than {tolerance} levels " +
                $"(max {max}, mean {mean:F3}); allowed {outlierFraction:P2}");
            Console.WriteLine($"  {name}: max {max}, mean {mean:F3}, {differing} differing pixels, {outliers} outliers");
        }

        var vignette = CameraWithEffect(CameraEffectType.Vignette, e =>
        {
            e.Vignette.Radius = 30f; e.Vignette.Softness = 60f; e.Vignette.Strength = 80f;
            e.Vignette.ColorR = 0.2f; e.Vignette.ColorG = 0.1f; e.Vignette.ColorB = 0.4f;
        });
        Case("Vignette", 2,
            () => oracle.Run(input, t => oracle.Call("ApplyVignettePass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, vignette)),
            () => VeldridCameraEffects.Vignette(vignette));

        foreach (bool distortRgb in new[] { false, true })
        {
            var aberration = CameraWithEffect(CameraEffectType.ChromaticAberration, e =>
            {
                e.ChromaticAberration.BlurAmount = distortRgb ? 20f : 0f;
                e.ChromaticAberration.DistortRgb = distortRgb;
                e.ChromaticAberration.RedOffset = 10f; e.ChromaticAberration.GreenOffset = 0f; e.ChromaticAberration.BlueOffset = -10f;
            });
            Case($"Chromatic aberration (radial {distortRgb})", 3,
                () => oracle.Run(input, t => oracle.Call("ApplyChromaticAberrationPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, aberration)),
                () => VeldridCameraEffects.ChromaticAberration(aberration));
        }

        foreach (bool repeat in new[] { false, true })
        {
            var distort = CameraWithEffect(CameraEffectType.Distort, e =>
            {
                e.Distort.RepeatImage = repeat; e.Distort.ZoomAmount = 80f; e.Distort.DistortAmount = 30f;
            });
            Case($"Distort (repeat {repeat})", 3,
                () => oracle.Run(input, t => oracle.Call("ApplyDistortPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, distort)),
                () => VeldridCameraEffects.Distort(distort));
        }

        var heat = CameraWithEffect(CameraEffectType.HeatDistort, e =>
        {
            e.HeatDistort.Strength = 30f; e.HeatDistort.Speed = 50f; e.HeatDistort.Scale = 60f;
        });
        float heatTime = Convert.ToSingle(oracle.Call("GetTimelineEffectTimeSeconds"));
        Case("Heat distort", 3,
            () => oracle.Run(input, t => oracle.Call("ApplyHeatDistortPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, heat)),
            () => VeldridCameraEffects.HeatDistort(heat, heatTime));

        var bars = CameraWithEffect(CameraEffectType.CinematicBars, e =>
        {
            e.CinematicBars.Size = 20f; e.CinematicBars.Rotation = 15f; e.CinematicBars.Offset = 10f;
            e.CinematicBars.ColorR = 0.1f; e.CinematicBars.ColorG = 0.2f; e.CinematicBars.ColorB = 0.3f;
        });
        Case("Cinematic bars", 1,
            () => oracle.Run(input, t => oracle.Call("ApplyCinematicBarsPass", t.Fbo, width, height, bars)),
            () => VeldridCameraEffects.CinematicBars(bars));

        var grain = CameraWithEffect(CameraEffectType.FilmGrain, e =>
        {
            e.FilmGrain.Strength = 0.3f; e.FilmGrain.Saturation = 0.5f; e.FilmGrain.Size = 2f;
        });
        Case("Film grain", 3,
            () => oracle.Run(input, t => oracle.Call("ApplyFilmGrainPass", t.Fbo, t.ColorTexture, t.GlowFbo, t.GlowTexture, width, height, grain)),
            () => [VeldridCameraEffects.FilmGrain(grain, 0)!], outlierFraction: 0.01);

        var correction = CameraWithEffect(CameraEffectType.ColorCorrection, e =>
        {
            var c = e.ColorCorrection;
            c.Contrast = 120f; c.Brightness = 90f; c.Saturation = 130f; c.Vibrance = 20f; c.Hue = 15f;
            c.Invert = true; c.InvertStrength = 25f; c.ColorBurn = 10f;
            c.BurnColorR = 0.9f; c.BurnColorG = 0.7f; c.BurnColorB = 0.5f;
        });
        correction.BlendColor = new vec3(1f, 0.9f, 0.8f);
        correction.MixColor = new vec4(0.2f, 0.4f, 0.6f, 0.25f);
        Case("Colour correction with camera material", 2,
            () => oracle.Run(input, t => oracle.Call("ApplyColorCorrectionPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, correction)),
            () => VeldridCameraEffects.ColorCorrection(correction));

        var materialOnly = new CameraSceneObject { BlendColor = new vec3(0.6f, 0.8f, 1f), MixColor = new vec4(0.5f, 0.1f, 0.1f, 0.3f) };
        Case("Camera material", 2,
            () => oracle.Run(input, t => oracle.Call("ApplyCameraMaterialPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, materialOnly)),
            () => VeldridCameraEffects.CameraMaterial(materialOnly));

        foreach (var (mapper, look) in new[] { (0, 0), (1, 0), (3, 2), (5, 4) })
        {
            oracle.Panel.ColorManagementEnabled = true;
            oracle.Panel.ColorManagementToneMapper = mapper;
            oracle.Panel.ColorManagementLook = look;
            oracle.Panel.ColorManagementExposure = 0.5f;
            var camera = new CameraSceneObject();
            Case($"Colour management (tone mapper {mapper}, look {look})", 2,
                () => oracle.Run(input, t => oracle.Call("ApplyColorManagementPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, oracle.Panel, camera)),
                () => [VeldridCameraEffects.ColorManagement(true, oracle.Panel.ColorManagementToneMapper,
                    oracle.Panel.ColorManagementExposure, oracle.Panel.ColorManagementGamma, oracle.Panel.ColorManagementLook)!]);
        }
        oracle.Panel.ColorManagementEnabled = false;

        oracle.Panel.AntiAliasingEnabled = true;
        // FXAA branches on luminance thresholds, so a rounding difference flips the filter on a few edge pixels.
        Case("Anti-aliasing (FXAA)", 2,
            () => oracle.Run(input, t => oracle.Call("ApplyAntiAliasingPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, oracle.Panel)),
            () => [VeldridCameraEffects.AntiAliasing(true, oracle.Panel.AntiAliasingSubpixel,
                oracle.Panel.AntiAliasingEdgeThreshold, oracle.Panel.AntiAliasingMinimumThreshold)!], outlierFraction: 0.01);
        oracle.Panel.AntiAliasingEnabled = false;

        oracle.Panel.TransparentBackground = true;
        Case("Alpha resolve", 2,
            () => oracle.Run(input, t => oracle.Call("ResolveSceneAlpha", t.Fbo, t.GlowFbo, t.GlowTexture, width, height)),
            () => [VeldridCameraEffects.AlphaResolve()]);
        oracle.Panel.TransparentBackground = false;

        const float depth = 0.7f, near = 0.1f, far = 100f;
        var dof = CameraWithEffect(CameraEffectType.DepthOfField, e =>
        {
            e.DepthOfField.Depth = 0.1f; e.DepthOfField.FocusRange = 0.02f; e.DepthOfField.FadeSize = 0.2f;
            e.DepthOfField.BlurSize = 88f; e.DepthOfField.AnamorphicRatio = 1.7f;
            e.DepthOfField.EdgeHighlight = 0.7f; e.DepthOfField.HighlightThreshold = 0.25f;
            e.DepthOfField.HighlightStrength = 1.8f; e.DepthOfField.ColorFringe = true;
            e.DepthOfField.RedAngle = 8f; e.DepthOfField.GreenAngle = 137f; e.DepthOfField.BlueAngle = 248f;
            e.DepthOfField.RedOffset = 5f; e.DepthOfField.GreenOffset = -3f; e.DepthOfField.BlueOffset = 7f;
        });
        dof.BladeCount = 7;
        dof.BladeAngle = 31f;
        dof.BladeStretch = -18f;
        byte[] depthOfField = input;
        byte[] expectedDepthOfField = oracle.Run(depthOfField, t =>
        {
            oracle.SetDepth(depth);
            oracle.Call("ApplyDepthOfFieldPass", t.Fbo, t.ColorTexture, oracle.Viewport.GetType()
                .GetField("_depthTex", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(oracle.Viewport),
                t.GlowFbo, t.GlowTexture, width, height, near, far, dof);
        });
        Check(!expectedDepthOfField.SequenceEqual(depthOfField), "Depth of Field OpenGL pass changes the depth-dependent image");
        var actualDepthOfField = harness.Run(depthOfField, width, height,
            VeldridCameraEffects.DepthOfField(dof, near, far), depth);
        var (dofMax, dofMean, dofDifferent, dofOutliers) = ComparePixels(expectedDepthOfField, actualDepthOfField, 2);
        Check(dofOutliers == 0,
            $"Depth of Field: {dofOutliers} pixels differ from OpenGL by more than 2 levels (max {dofMax}, mean {dofMean:F3})");
        Console.WriteLine($"  Depth of Field: max {dofMax}, mean {dofMean:F3}, {dofDifferent} differing pixels, {dofOutliers} outliers");
        oracle.SetDepth(1f);

        var pixelate = CameraWithEffect(CameraEffectType.Pixelate, e => e.Pixelate.Size = 7f);
        Case("Pixelate", 0,
            () => oracle.Run(input, t => oracle.Call("ApplyPixelatePass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, pixelate)),
            () => VeldridCameraEffects.Pixelate(pixelate));

        oracle.Panel.GlowEnabled = true;
        oracle.Panel.GlowStrength = 0.8f;
        oracle.Panel.GlowSize = 8f;
        var renderedPassMode = Enum.Parse(oracle.Viewport.GetType().GetNestedType("RenderedPassMode",
            BindingFlags.NonPublic)!, "Combined");
        byte[] expectedGlow = oracle.Run(input, t => oracle.Call("ApplyGlowPassGeneric", t.Fbo, t.ColorTexture,
            t.GlowFbo, t.GlowTexture, t.GlowPingFbo, t.GlowPingTexture, width, height, oracle.Panel, renderedPassMode, true));
        Check(!expectedGlow.SequenceEqual(input), "Glow OpenGL pass changes the bright regions");
        var glowOnly = VeldridCameraEffects.Glow(oracle.Panel, capture: false);
        Case("Glow", 2, () => expectedGlow, () => glowOnly == null ? [] : [glowOnly]);

        var bloom = CameraWithEffect(CameraEffectType.Bloom, e =>
        {
            e.Bloom.Radius = 72f; e.Bloom.Intensity = 85f; e.Bloom.Threshold = 22f;
            e.Bloom.AnamorphicRatio = 145f; e.Bloom.BlendMode = BloomBlendMode.Screen;
            e.Bloom.BlendColorR = 0.9f; e.Bloom.BlendColorG = 0.7f; e.Bloom.BlendColorB = 1f;
        });
        foreach (bool transparent in new[] { false, true })
        {
            oracle.Panel.TransparentBackground = transparent;
            Case($"Bloom (transparent {transparent})", 2,
                () => oracle.Run(input, t => oracle.Call("ApplyBloomPass", t.Fbo, t.ColorTexture,
                    t.GlowFbo, t.GlowTexture, t.GlowPingFbo, t.GlowPingTexture, width, height, bloom)),
                () => VeldridCameraEffects.Bloom(bloom, transparent));
        }
        oracle.Panel.TransparentBackground = false;

        uint dirtWidth = 13, dirtHeight = 9;
        var dirtPixels = new byte[dirtWidth * dirtHeight * 4];
        for (int y = 0; y < dirtHeight; y++)
        for (int x = 0; x < dirtWidth; x++)
        {
            int offset = (y * (int)dirtWidth + x) * 4;
            dirtPixels[offset] = 128;
            dirtPixels[offset + 1] = 170;
            dirtPixels[offset + 2] = 220;
            dirtPixels[offset + 3] = (byte)((x + y) % 4 == 0 ? 160 : 255);
        }
        string dirtPath = Path.Combine(AppContext.BaseDirectory, "veldrid-lens-dirt-parity.png");
        PanelViewport.WritePng(dirtPath, dirtPixels, (int)dirtWidth, (int)dirtHeight, bottomUp: true, rgba: true);
        var dirtCamera = CameraWithEffect(CameraEffectType.LensDirt, effect =>
        {
            effect.LensDirt.TexturePath = dirtPath;
            effect.LensDirt.Intensity = 68f;
            effect.LensDirt.Power = 145f;
            effect.LensDirt.Radius = 48f;
            effect.LensDirt.AffectedByBloom = true;
            effect.LensDirt.AffectedByGlow = true;
        });
        try
        {
            MineImatorSimplyRemade.core.mdl.mineImator.MineImatorLoader.Instance.Initialize(oracle.Gl);
            oracle.Call("PrepareLensDirt", dirtCamera, width, height);
            byte[] expectedDirt = oracle.Run(input, t =>
            {
                oracle.Call("PrepareLensDirt", dirtCamera, width, height);
                oracle.Call("ApplyGlowPassGeneric", t.Fbo, t.ColorTexture, t.GlowFbo, t.GlowTexture,
                    t.GlowPingFbo, t.GlowPingTexture, width, height, oracle.Panel, renderedPassMode, true);
                oracle.Call("ApplyBloomPass", t.Fbo, t.ColorTexture, t.GlowFbo, t.GlowTexture,
                    t.GlowPingFbo, t.GlowPingTexture, width, height, bloom);
                oracle.Call("ApplyLensDirtPass", t.Fbo, t.ColorTexture, t.GlowFbo, t.GlowTexture,
                    t.GlowPingFbo, t.GlowPingTexture, width, height, dirtCamera);
            });
            Check(!expectedDirt.SequenceEqual(input), "Lens Dirt OpenGL chain changes the captured highlights");
            using var dirt = VeldridTexture.CreateFromRgbaBytes(harness.Device, dirtPixels, dirtWidth, dirtHeight);
            var glowForDirt = VeldridCameraEffects.Glow(oracle.Panel, capture: true);
            var lensDirtEffects = new List<IVeldridPostEffect>();
            if (glowForDirt != null) lensDirtEffects.Add(glowForDirt);
            lensDirtEffects.AddRange(VeldridCameraEffects.Bloom(bloom, transparent: false, captureLensDirt: true));
            lensDirtEffects.Add(VeldridCameraEffects.LensDirt(dirtCamera.Effects[0].LensDirt, dirt,
                oracle.Panel.GlowStrength, transparent: false, useBloom: true, useGlow: true));
            var actualDirt = harness.Run(input, width, height, lensDirtEffects);
            var (dirtMax, dirtMean, dirtDifferent, dirtOutliers) = ComparePixels(expectedDirt, actualDirt, 2);
            Check(dirtOutliers == 0,
                $"Lens Dirt: {dirtOutliers} pixels differ from OpenGL by more than 2 levels (max {dirtMax}, mean {dirtMean:F3})");
            Console.WriteLine($"  Lens Dirt: max {dirtMax}, mean {dirtMean:F3}, {dirtDifferent} differing pixels, {dirtOutliers} outliers");
        }
        finally
        {
            File.Delete(dirtPath);
            oracle.Panel.GlowEnabled = false;
            oracle.Panel.GlowStrength = 0f;
        }

        uint markWidth = 13, markHeight = 9;
        var markPixels = new byte[markWidth * markHeight * 4];
        for (int y = 0; y < markHeight; y++)
        for (int x = 0; x < markWidth; x++)
        {
            int offset = (y * (int)markWidth + x) * 4;
            markPixels[offset] = (byte)(x * 19);
            markPixels[offset + 1] = (byte)(y * 27);
            markPixels[offset + 2] = (byte)((x + y) * 13);
            markPixels[offset + 3] = (byte)((x + y) % 4 == 0 ? 160 : 255);
        }
        string watermarkPath = Path.Combine(AppContext.BaseDirectory, "veldrid-watermark-parity.png");
        PanelViewport.WritePng(watermarkPath, markPixels, (int)markWidth, (int)markHeight, bottomUp: true, rgba: true);
        oracle.Panel.WatermarkEnabled = true;
        oracle.Panel.WatermarkImagePath = watermarkPath;
        oracle.Panel.WatermarkScale = 0.8f;
        oracle.Panel.WatermarkPadding = 7f;
        oracle.Panel.WatermarkHorizontalAlignment = 0;
        oracle.Panel.WatermarkVerticalAlignment = 2;
        oracle.Panel.WatermarkOpacity = 0.65f;
        try
        {
            MineImatorSimplyRemade.core.mdl.mineImator.MineImatorLoader.Instance.Initialize(oracle.Gl);
            Check(File.Exists(watermarkPath), "Watermark parity image exists before the OpenGL loader reads it");
            using var watermark = VeldridTexture.CreateFromRgbaBytes(harness.Device, markPixels, markWidth, markHeight);
            Case("Watermark", 1,
                () => oracle.Run(input, t => oracle.Call("ApplyWatermarkPass", t.Fbo, t.GlowFbo, t.GlowTexture, width, height, oracle.Panel)),
                () => [VeldridCameraEffects.Watermark(watermark, width, height, oracle.Panel.WatermarkScale,
                    oracle.Panel.WatermarkPadding, oracle.Panel.WatermarkHorizontalAlignment,
                    oracle.Panel.WatermarkVerticalAlignment, oracle.Panel.WatermarkOpacity)]);
        }
        finally
        {
            oracle.Panel.WatermarkEnabled = false;
            oracle.Panel.WatermarkImagePath = "";
            File.Delete(watermarkPath);
        }
    }
}
