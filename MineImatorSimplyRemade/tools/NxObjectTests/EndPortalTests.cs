using System.Reflection;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using Silk.NET.OpenGL;
using Shader = MineImatorSimplyRemade.core.mdl.Shader;

internal static partial class Program
{
    private static unsafe void EndPortalGraphics(bool testVeldrid)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(128, 128, "End portal regression", null, null);
        Check(window != null, "OpenGL context");
        float previousTime = Mesh.EffectTime;
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            using var fixture = new Fixture();
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            string version = Path.Combine(ParticleDataRoot(), "minecraft", "versions", "26.3");
            typeof(BlockRegistry).GetProperty(nameof(BlockRegistry.VersionRoot))!.SetValue(null, version);
            var blocks = (List<string>)typeof(BlockRegistry).GetField("_blocks", flags)!.GetValue(null)!;
            var parse = typeof(BlockRegistry).GetMethod("ParseBlockstate", flags,
                [typeof(string), typeof(JsonObject), typeof(string)])!;
            foreach (string name in new[] { "end_portal", "end_gateway", "stone" })
            {
                parse.Invoke(null, [name, JsonNode.Parse(File.ReadAllText(
                    Path.Combine(version, "blockstates", name + ".json")))!.AsObject(), ""]);
                blocks.Add(name);
            }
            using var portal = MinecraftModelMesh.Build(gl, new ResolvedBlockModel(), blockName: "end_portal").Single();
            using var gateway = MinecraftModelMesh.Build(gl, new ResolvedBlockModel(), blockName: "end_gateway").Single();
            using var ordinary = MinecraftModelMesh.Build(gl, new ResolvedBlockModel(), blockName: "stone").Single();
            Equal(6, portal.Vertices.Count, "Portal is one surface, not a fallback cube");
            Check(portal.Vertices.All(v => v.y == .75f), "Portal surface is at 12/16 height");
            Check(portal.Normals.All(n => n == vec3.UnitY), "Portal faces upward");
            Equal(36, gateway.Vertices.Count, "Gateway has six faces");
            Check(portal.EndPortalSurface && gateway.EndPortalSurface && !ordinary.EndPortalSurface,
                "Special surface classification does not leak to ordinary blocks");
            using var clone = portal.Clone();
            Check(clone.EndPortalSurface, "Duplication preserves shader identity");
            using var tiled = MinecraftModelMesh.Build(gl, new ResolvedBlockModel(), blockName: "end_portal",
                tileX: 3, tileY: 2, tileZ: 2).Single();
            Equal(72, tiled.Vertices.Count, "Tiling preserves every portal plane, including vertical repeats");
            using var tiledGateway = MinecraftModelMesh.Build(gl, new ResolvedBlockModel(), blockName: "end_gateway",
                tileX: 2).Single();
            Equal(60, tiledGateway.Vertices.Count, "Tiled gateway removes touching internal faces");

            var viewport = new Viewport { Gl = gl };
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            var timelineProperty = typeof(Timeline).GetProperty(nameof(Timeline.Instance))!;
            var previousTimeline = Timeline.Instance;
            try
            {
                var timeline = new Timeline();
                timelineProperty.SetValue(null, timeline);
                timeline.SetFrameRate(20);
                var frameField = typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
                frameField.SetValue(timeline, 40);
                typeof(Viewport).GetMethod("PrepareSceneEnvironment", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(viewport, [new Camera()]);
                Equal(2f, Mesh.EffectTime, "Scene preparation derives shader time from timeline frame and framerate");
                frameField.SetValue(timeline, 0);
                typeof(Viewport).GetMethod("PrepareSceneEnvironment", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(viewport, [new Camera()]);
                Equal(0f, Mesh.EffectTime, "Backward timeline seek resets shader time");
            }
            finally { timelineProperty.SetValue(null, previousTimeline); }
            foreach (string name in new[] { "end_portal", "end_gateway" })
            {
                var spawned = spawn.CreateBlockObject(name, BlockRegistry.GetVariants(name)[0], "") ??
                    throw new InvalidOperationException("Could not spawn " + name);
                try
                {
                    Check(spawned.Visuals.All(m => m.EndPortalSurface), name + ": spawned shader identity");
                    Check(spawn.RebuildBlockMeshes(spawned), name + ": standalone rebuild");
                    Check(spawned.Visuals.All(m => m.EndPortalSurface), name + ": rebuilt shader identity");
                }
                finally
                {
                    viewport.SceneObjects.Remove(spawned);
                    foreach (var mesh in spawned.Visuals) mesh.Dispose();
                }
            }
            var assembly = typeof(SpawnMenu).Assembly;
            var boundsType = assembly.GetType("MineImatorSimplyRemade.core.world.WorldBounds", true)!;
            var volumeType = assembly.GetType("MineImatorSimplyRemade.core.world.WorldVolume", true)!;
            var bounds = Activator.CreateInstance(boundsType, [0, 0, 0, 4, 0, 0])!;
            var volume = Activator.CreateInstance(volumeType, [bounds])!;
            for (int x = 0; x < 5; x += 2)
                volumeType.GetMethod("Set")!.Invoke(volume, [x, 0, 0,
                    "minecraft:" + (x == 0 ? "end_portal" : x == 2 ? "end_gateway" : "stone")]);
            string path = fixture.NewPath(".schem");
            volumeType.GetMethod("SaveSchematic")!.Invoke(volume, [path, null]);
            var terrain = spawn.SpawnSchematicFromPath(path) ?? throw new InvalidOperationException("Portal terrain import failed");
            try
            {
                Equal(42, terrain.Visuals.Where(m => m.EndPortalSurface).Sum(m => m.Vertices.Count),
                    "Schematic preserves portal/gateway geometry and shader batches");
                Check(terrain.Visuals.Any(m => !m.EndPortalSurface), "Ordinary terrain remains in a separate batch");
                Check(spawn.ApplyResourcePackToSpawnedObject(terrain, ""), "Schematic rebuild succeeds");
                Equal(42, terrain.Visuals.Where(m => m.EndPortalSurface).Sum(m => m.Vertices.Count),
                    "Shader identity survives rebuilding");
            }
            finally { foreach (var mesh in terrain.Visuals) mesh.Dispose(); }

            using var shader = new Shader(gl);
            shader.CompileShader("simple.vert", "simple.frag");
            gl.GetProgram(shader.ShaderProgram, GLEnum.LinkStatus, out int linked);
            Equal(1, linked, "Portal shader compiles and links");
            using var preview = new PreviewRenderer(gl, 128) { AutoRotateSpeed = 0 };
            var camera = new Camera { Target = new vec3(.5f, .75f, .5f), Pitch = 1.2f, Yaw = .3f, Distance = 1.4f };
            byte[] Read(uint texture)
            {
                byte[] pixels = new byte[128 * 128 * 4];
                gl.BindTexture(GLEnum.Texture2D, texture);
                fixed (byte* data = pixels)
                    gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
                return pixels;
            }
            byte[] Draw(Mesh mesh, bool studio = true)
            {
                preview.Render([mesh], "portal", 0, camera: camera, isolatedLighting: studio);
                return Read(preview.ColorTexture);
            }
            int Difference(byte[] a, byte[] b) => a.Where((value, i) => value != b[i]).Count();
            void CheckStars(byte[] pixels, string label)
            {
                int bright = 0, dark = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 3] < 250) continue;
                    if (pixels[i + 1] > 35 && pixels[i + 1] > pixels[i] + 8) bright++;
                    if (pixels[i + 1] < 12) dark++;
                }
                Check(bright > 100 && dark > 100, label + ": colored stars over a dark opaque field");
            }
            Mesh.EffectTime = 0;
            byte[] first = Draw(portal);
            CheckStars(first, "OpenGL portal");
            Check(first.SequenceEqual(Draw(portal)), "Paused frames are identical");
            Mesh.EffectTime = 5;
            Check(Difference(first, Draw(portal)) > 500, "Timeline animation changes rendered pixels");
            Mesh.EffectTime = 0;
            Check(first.SequenceEqual(Draw(portal)), "Backward seek reproduces exact pixels");
            using (var darkLighting = new SceneUniformBuffer(gl))
            {
                var previousLighting = Mesh.SceneUBO;
                var previousFog = (Mesh.FogEnabled, Mesh.FogDistance, Mesh.FogFadeSize, Mesh.FogColor);
                try
                {
                    darkLighting.Upload(mat4.Identity, vec3.Zero, vec3.UnitY, vec3.Zero,
                        vec3.UnitY, vec3.Zero, vec3.UnitY, vec3.Zero, 0, false, false, false);
                    Mesh.SceneUBO = darkLighting;
                    Mesh.FogEnabled = false;
                    Check(first.SequenceEqual(Draw(portal, false)), "Portal stays self-lit with zero ambient and direct light");
                    portal.EmissionEnabled = true;
                    portal.EmissionColor = vec3.Ones;
                    portal.EmissionEnergy = 2;
                    Check(Difference(first, Draw(portal, false)) > 500, "Emission brightens actual portal pixels");
                    portal.EmissionEnabled = false;
                    Mesh.FogEnabled = true;
                    Mesh.FogDistance = 1;
                    Mesh.FogFadeSize = 1;
                    Mesh.FogColor = new vec3(1, 0, 0);
                    byte[] fogged = Draw(portal, false);
                    int red = 0;
                    for (int i = 0; i < fogged.Length; i += 4)
                        if (fogged[i] > 245 && fogged[i + 1] < 5 && fogged[i + 2] < 5) red++;
                    Check(red > 100, "Scene fog applies to the self-lit portal");
                }
                finally
                {
                    Mesh.SceneUBO = previousLighting;
                    (Mesh.FogEnabled, Mesh.FogDistance, Mesh.FogFadeSize, Mesh.FogColor) = previousFog;
                }
            }
            portal.TextureRepeat = new vec2(4, 3);
            portal.TextureOffset = new vec2(.2f, .6f);
            Check(first.SequenceEqual(Draw(portal)), "Star projection is independent of surface UVs");
            camera.Yaw += .2f;
            Check(Difference(first, Draw(portal)) > 500, "Camera movement changes the layered projection");
            camera.Yaw -= .2f;
            portal.Albedo = new vec3(.2f, .2f, .2f);
            Check(Difference(first, Draw(portal)) > 500, "Material tint applies");
            portal.Albedo = vec3.Ones;
            portal.Alpha = .5f;
            byte[] translucent = Draw(portal);
            int halfBlended = 0;
            for (int i = 0; i < first.Length; i += 4)
                if (Math.Abs(first[i + 1] - 31) > 10 &&
                    Math.Abs(translucent[i + 1] - (first[i + 1] + 31) * .5f) < 2)
                    halfBlended++;
            Check(halfBlended > 100, "Half opacity blends procedural pixels halfway with the preview background");
            portal.Alpha = 1;
            camera.Target = new vec3(.5f);
            camera.Pitch = .3f;
            CheckStars(Draw(gateway), "OpenGL gateway");
            gateway.EndPortalSurface = false;
            byte[] regular = Draw(gateway);
            gateway.EndPortalSurface = true;
            Check(Difference(regular, Draw(gateway)) > 500, "Per-draw shader flag switches correctly");

            if (testVeldrid)
            {
                using var device = Veldrid.GraphicsDevice.CreateD3D11(new Veldrid.GraphicsDeviceOptions(
                    debug: false, swapchainDepthFormat: null, syncToVerticalBlank: false,
                    resourceBindingModel: Veldrid.ResourceBindingModel.Improved,
                    preferDepthRangeZeroToOne: true, preferStandardClipSpaceYDirection: true));
                using var renderer = new VeldridSceneRenderer(device, gl);
                uint output = gl.GenTexture();
                try
                {
                    gl.BindTexture(GLEnum.Texture2D, output);
                    gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, 128, 128, 0,
                        GLEnum.Rgba, GLEnum.UnsignedByte, null);
                    byte[] DrawVeldrid()
                    {
                        renderer.BeginFrame(128, 128, camera.GetViewMatrix(), camera.GetProjectionMatrix(1),
                            System.Numerics.Vector4.Zero);
                        renderer.Draw(gateway, mat4.Identity);
                        renderer.EndFrame(output);
                        return Read(output);
                    }
                    Mesh.EffectTime = 0;
                    byte[] still = DrawVeldrid();
                    CheckStars(still, "Veldrid gateway");
                    Mesh.EffectTime = 5;
                    Check(Difference(still, DrawVeldrid()) > 500, "Veldrid timeline animation changes pixels");
                    Mesh.EffectTime = 0;
                    Check(still.SequenceEqual(DrawVeldrid()), "Veldrid backward seek is reproducible");
                }
                finally { gl.DeleteTexture(output); }
            }
            Equal(GLEnum.NoError, gl.GetError(), "No OpenGL errors");
        }
        finally
        {
            Mesh.EffectTime = previousTime;
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
