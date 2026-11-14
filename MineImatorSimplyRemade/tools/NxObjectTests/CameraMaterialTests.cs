using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;
using StbImageSharp;

internal static partial class Program
{
    private static void CameraMaterialPersistence(Fixture fixture)
    {
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", statics)!;
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectSceneObjectEntry)!;
        var camera = new CameraSceneObject();
        apply.Invoke(null, [camera, legacy, null]);
        Equal(vec3.Ones, camera.BlendColor, "Old projects have neutral camera blend");
        Equal(vec4.Zero, camera.MixColor, "Old projects have no camera mix");
        camera.BlendColor = new vec3(0.2f, 0.4f, 0.6f);
        camera.MixColor = new vec4(0.8f, 0.3f, 0.1f, 0.5f);
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(camera);
        string json = JsonSerializer.Serialize(snapshot, AppJsonContext.Default.ProjectSceneObjectEntry);
        var saved = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectSceneObjectEntry)!;
        var (archive, _) = fixture.Roundtrip(camera);
        var library = (ProjectSceneObjectEntry)typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", statics)!
            .Invoke(null, [saved])!;
        foreach (var entry in new[] { saved, archive.Root, library })
        {
            var loaded = new CameraSceneObject();
            apply.Invoke(null, [loaded, entry, null]);
            Equal(camera.BlendColor, loaded.BlendColor, "Blend survives project/archive/library");
            Equal(camera.MixColor, loaded.MixColor, "Mix survives project/archive/library");
        }
        var duplicate = (CameraSceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate", instance)!
            .Invoke(new SceneTree(), [camera, new Dictionary<string, string>()])!;
        Equal(camera.BlendColor, duplicate.BlendColor, "Duplicate blend");
        Equal(camera.MixColor, duplicate.MixColor, "Duplicate mix");
        library.CameraMixColor.W = 0f;
        Near(0.5f, saved.CameraMixColor.W, "Library clone does not share color data");

        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { camera }])!;
        ulong original = Hash();
        var timeline = new Timeline();
        var get = typeof(Timeline).GetMethod("GetPropertyValue", instance)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", instance)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", instance)!;
        var paths = ((IEnumerable<string>)typeof(Timeline).GetMethod("GetKeyframePropertyPaths", statics)!
            .Invoke(null, [camera])!).ToArray();
        foreach (string channel in new[] { "blend_r", "blend_g", "blend_b", "mix_r", "mix_g", "mix_b", "mix_amount" })
        {
            string path = "camera." + channel;
            Check(paths.Contains(path), "Timeline discovers " + path);
            set.Invoke(timeline, [camera, path, 1f]);
            Check(Hash() != original, "Dirty detection includes " + path);
            apply.Invoke(null, [camera, snapshot, null]);
            Equal(original, Hash(), "Undo restores " + path);
            var keys = new List<TimelineKeyframe>
            {
                new() { Frame = 0, Value = 0f }, new() { Frame = 10, Value = 1f }
            };
            foreach (int frame in new[] { 0, 5, 10, 5, 0 })
            {
                float value = Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, frame]));
                set.Invoke(timeline, [camera, path, value]);
                Near(frame / 10f, Convert.ToSingle(get.Invoke(timeline, [camera, path])), "Seek " + path);
            }
            set.Invoke(timeline, [camera, path, -1f]);
            Near(0f, Convert.ToSingle(get.Invoke(timeline, [camera, path])), "Lower bound " + path);
            set.Invoke(timeline, [camera, path, 2f]);
            Near(1f, Convert.ToSingle(get.Invoke(timeline, [camera, path])), "Upper bound " + path);
            apply.Invoke(null, [camera, snapshot, null]);
        }
    }

    private static unsafe void CameraMaterialControls(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        SelectionManager.Instance.ClearSelection();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(800, 700);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var camera = new CameraSceneObject();
            var second = new CameraSceneObject { MixColor = new vec4(1f, 0f, 0f, 0f) };
            var panel = new PropertiesPanel();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PropertiesPanel).GetField("_currentObject", flags)!.SetValue(panel, camera);
            var draw = typeof(PropertiesPanel).GetMethod("DrawCameraMaterialControls", flags)!;
            SelectionManager.Instance.SelectObject(camera);
            SelectionManager.Instance.SelectObject(second);
            System.Numerics.Vector2 input = default;
            void Frame(bool down = false)
            {
                io.AddMousePosEvent(input.X, input.Y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600), ImGuiCond.Always);
                ImGui.Begin("Camera material controls");
                float start = ImGui.GetCursorPosY();
                draw.Invoke(panel, [camera]);
                Check(ImGui.GetCursorPosY() - start >= ImGui.GetFrameHeightWithSpacing() * 3f - 1f,
                    "Two color selectors and mix slider are visible");
                var min = ImGui.GetItemRectMin();
                input = new System.Numerics.Vector2(min.X + 200f, (min.Y + ImGui.GetItemRectMax().Y) / 2f);
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            Frame();
            Frame(true);
            Frame(false);
            Check(camera.MixColor.w > 0f, "Mix slider edits camera");
            Near(camera.MixColor.w, second.MixColor.w, "Slider edits selected cameras");
            Equal(new vec3(1f, 0f, 0f), second.MixColor.xyz, "Amount preserves each camera's color");
            Check(!camera.HasExplicitMaterialSettings, "Camera controls do not change model materials");
        }
        finally
        {
            SelectionManager.Instance.ClearSelection();
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void CameraMaterialGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Camera material regression", null, null);
        Check(window != null, "OpenGL context");
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DeltaTime = 1f / 60f;
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            var viewport = new Viewport { Gl = gl, OverlaysEnabled = false };
            const uint width = 96, height = 64;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            viewport.InitFramebuffer(width, height);
            var preview = new Viewport { Gl = gl, IsPreviewViewport = true, MainViewport = viewport, OverlaysEnabled = false };
            preview.InitPreviewViewport(width, height);
            try
            {
                uint Field(string name) => (uint)typeof(Viewport).GetField(name, flags)!.GetValue(viewport)!;
                var pass = typeof(Viewport).GetMethod("ApplyColorCorrectionPass", flags)!;
                var camera = new CameraSceneObject();
                byte[] input = new byte[width * height * 4];
                for (int i = 0; i < input.Length; i += 4)
                {
                    input[i] = 200; input[i + 1] = 100; input[i + 2] = 50;
                    input[i + 3] = (byte)(i / 4 % 256);
                }
                byte[] Read(uint texture)
                {
                    byte[] output = new byte[input.Length];
                    gl.BindTexture(GLEnum.Texture2D, texture);
                    fixed (byte* data = output)
                        gl.GetTexImage(GLEnum.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    return output;
                }
                byte[] Render(CameraSceneObject target)
                {
                    gl.ActiveTexture(GLEnum.Texture0);
                    gl.BindTexture(GLEnum.Texture2D, Field("_colorTex"));
                    fixed (byte* data = input)
                        gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    gl.Enable(GLEnum.DepthTest);
                    gl.Enable(GLEnum.CullFace);
                    gl.Enable(GLEnum.Blend);
                    pass.Invoke(viewport, [Field("_fbo"), Field("_glowFbo"), Field("_glowTex"), width, height, target]);
                    Check(gl.IsEnabled(GLEnum.DepthTest) && gl.IsEnabled(GLEnum.CullFace) && gl.IsEnabled(GLEnum.Blend),
                        "Material pass restores GL state");
                    byte[] output = Read(Field("_colorTex"));
                    for (int i = 3; i < input.Length; i += 4)
                        Equal(input[i], output[i], "Material preserves exact alpha");
                    return output;
                }
                Check(input.SequenceEqual(Render(camera)), "Default material is pixel-identical");
                camera.BlendColor = new vec3(0.5f, 1f, 0.2f);
                camera.MixColor = new vec4(0f, 0f, 1f, 0.25f);
                byte[] tinted = Render(camera);
                foreach (var (channel, expected) in new[] { (0, 75), (1, 75), (2, 71) })
                    Check(Math.Abs(tinted[channel] - expected) <= 1, "Blend then mix channel " + channel);
                Check(input.SequenceEqual(Render(new CameraSceneObject())), "Tint does not leak to other cameras");
                camera.AddEffect(CameraEffectType.ColorCorrection);
                camera.Effects[0].ColorCorrection.Brightness = 50f;
                Check(Render(camera)[0] < tinted[0] - 30, "Color correction composes before material tint");
                camera.Effects.Clear();
                Check(tinted.SequenceEqual(Render(camera)), "Repeated rendering is deterministic");

                viewport.PropertiesPanel = new PropertiesPanel
                {
                    TransparentBackground = true, ShadowsEnabled = false, FogEnabled = false,
                    ColorManagementEnabled = false, AntiAliasingEnabled = false
                };
                using var cube = new CubeMesh(gl) { Unlit = true, BlendColor = new vec4(0.8f, 0.6f, 0.4f, 1f) };
                var obj = new SceneObject();
                obj.Visuals.Add(cube);
                viewport.SceneObjects.Add(obj);
                foreach (var (highQuality, applyEffects) in new[] { (false, false), (true, false), (false, true) })
                {
                    camera.BlendColor = vec3.Ones;
                    camera.MixColor = vec4.Zero;
                    preview.RenderScenePublic(viewport.Camera, camera, width, height, highQuality: highQuality, applyCameraEffects: applyEffects);
                    byte[] baseline = Read(preview.ColorTexture);
                    camera.BlendColor = new vec3(0.5f, 0.25f, 1f);
                    preview.RenderScenePublic(viewport.Camera, camera, width, height, highQuality: highQuality, applyCameraEffects: applyEffects);
                    byte[] blended = Read(preview.ColorTexture);
                    for (int i = 0; i < blended.Length; i += 4)
                    {
                        Check(Math.Abs(blended[i] - baseline[i] * 0.5f) <= 1f, "Preview applies red blend");
                        Check(Math.Abs(blended[i + 1] - baseline[i + 1] * 0.25f) <= 1f, "Preview applies green blend");
                        Equal(baseline[i + 2], blended[i + 2], "Preview preserves blue blend");
                        Equal(baseline[i + 3], blended[i + 3], "Blend preserves preview coverage");
                    }
                    camera.MixColor = new vec4(0f, 1f, 0f, 1f);
                    preview.RenderScenePublic(viewport.Camera, camera, width, height, highQuality: highQuality, applyCameraEffects: applyEffects);
                    byte[] output = Read(preview.ColorTexture);
                    Check(baseline.Where((_, i) => i % 4 == 3).Any(value => value == 255), "Preview has rendered geometry");
                    for (int i = 0; i < output.Length; i += 4)
                    {
                        Equal((byte)0, output[i], "Preview mix removes red");
                        Equal((byte)255, output[i + 1], "Preview mix supplies green");
                        Equal((byte)0, output[i + 2], "Preview mix removes blue");
                        Equal(baseline[i + 3], output[i + 3], "Preview keeps coverage");
                    }
                    string path = fixture.NewPath(".png");
                    new MineImatorSimplyRemade.core.render.RenderExporter(preview)
                        .ExportImageFromRgb((int)width, (int)height, "png", path, output, rgba: true);
                    using var stream = System.IO.File.OpenRead(path);
                    var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                    Check(image.Data.Where((_, i) => i % 4 == 1).All(value => value == 255), "Export keeps green mix");
                    Check(image.Data.Where((_, i) => i % 4 == 3).Any(value => value == 0) &&
                          image.Data.Where((_, i) => i % 4 == 3).Any(value => value == 255), "Export keeps transparency");
                }
                camera.BlendColor = new vec3(0.5f, 0.25f, 1f);
                camera.MixColor = vec4.Zero;
                preview.RenderScenePublic(viewport.Camera, camera, width, height);
                byte[] shaded = Read(preview.ColorTexture);
                camera.AddEffect(CameraEffectType.ColorCorrection);
                camera.Effects[0].ColorCorrection.Brightness = 0f;
                preview.RenderScenePublic(viewport.Camera, camera, width, height);
                Check(shaded.SequenceEqual(Read(preview.ColorTexture)),
                    "Default shaded preview applies material colors without enabling color correction effects");
                Equal(GLEnum.NoError, gl.GetError(), "Camera material rendering has no GL errors");
            }
            finally
            {
                preview.DisposeFramebufferResources();
                viewport.DisposeFramebufferResources();
            }
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
