using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using Shader = MineImatorSimplyRemade.core.mdl.Shader;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;
using StbImageSharp;

internal static partial class Program
{
    private static void VignettePersistence(Fixture fixture)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectCameraEffectEntry)!;
        Near(50f, legacy.Vignette.Radius, "Legacy radius default");
        Near(50f, legacy.Vignette.Softness, "Legacy softness default");
        Near(50f, legacy.Vignette.Strength, "Legacy strength default");
        Near(0f, legacy.Vignette.ColorR + legacy.Vignette.ColorG + legacy.Vignette.ColorB, "Default color is black");
        var camera = new CameraSceneObject();
        camera.AddEffect(CameraEffectType.Vignette);
        var settings = camera.Effects[0].Vignette;
        for (int i = 0; i < VignetteSettings.Controls.Length; i++)
        {
            var control = VignetteSettings.Controls[i];
            settings.SetValue(control.Key, (control.Min + control.Max) * (i + 1) / 7f);
        }
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(camera);
        string json = JsonSerializer.Serialize(snapshot, AppJsonContext.Default.ProjectSceneObjectEntry);
        var saved = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectSceneObjectEntry)!;
        var (archive, _) = fixture.Roundtrip(camera);
        var library = (ProjectSceneObjectEntry)typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", statics)!
            .Invoke(null, [saved])!;
        var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", statics)!;
        var loaded = new CameraSceneObject();
        apply.Invoke(null, [loaded, saved, null]);
        var duplicate = (CameraSceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate", instance)!
            .Invoke(new SceneTree(), [camera, new Dictionary<string, string>()])!;
        foreach (var copy in new[] { saved.CameraEffects[0].Vignette, archive.Root.CameraEffects[0].Vignette, library.CameraEffects[0].Vignette,
                     loaded.Effects[0].Vignette, duplicate.Effects[0].Vignette })
        {
            Check(!ReferenceEquals(settings, copy), "Copied settings are independent");
            foreach (var control in VignetteSettings.Controls)
                Near(settings.GetValue(control.Key), copy.GetValue(control.Key), "Persists " + control.Key);
        }
        library.CameraEffects[0].Vignette.ColorR = 1f;
        Near(settings.ColorR, saved.CameraEffects[0].Vignette.ColorR, "Library clone does not mutate source");
        duplicate.Effects[0].Vignette.Strength = 100f;
        Near(snapshot.CameraEffects[0].Vignette.Strength, settings.Strength, "Duplication does not mutate source");
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { camera }])!;
        ulong original = Hash();
        foreach (var control in VignetteSettings.Controls)
        {
            camera.Effects[0].Vignette.SetValue(control.Key, control.Max);
            Check(Hash() != original, "Dirty detection includes " + control.Key);
            apply.Invoke(null, [camera, snapshot, null]);
            Equal(original, Hash(), "Undo restores " + control.Key);
        }
        var timeline = new Timeline();
        var paths = ((IEnumerable<string>)typeof(Timeline).GetMethod("GetKeyframePropertyPaths", statics)!
            .Invoke(null, [camera])!).ToArray();
        var get = typeof(Timeline).GetMethod("GetPropertyValue", instance)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", instance)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", instance)!;
        foreach (var control in VignetteSettings.Controls)
        {
            string path = "camera.effect.0.vignette." + control.Key;
            Check(paths.Contains(path), "Timeline discovers " + path);
            var keys = new List<TimelineKeyframe>
            {
                new() { Frame = 0, Value = control.Min }, new() { Frame = 10, Value = control.Max }
            };
            foreach (int frame in new[] { 0, 5, 10, 5, 0 })
            {
                float value = Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, frame, false]));
                set.Invoke(timeline, [camera, path, value]);
                Near(control.Min + (control.Max - control.Min) * frame / 10f,
                    Convert.ToSingle(get.Invoke(timeline, [camera, path])), "Interpolation/reverse seeking " + path);
            }
            settings.SetValue(control.Key, control.Min - 1f);
            Near(control.Min, settings.GetValue(control.Key), "Lower bound " + control.Key);
            settings.SetValue(control.Key, control.Max + 1f);
            Near(control.Max, settings.GetValue(control.Key), "Upper bound " + control.Key);
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                bool rejected = false;
                try { settings.SetValue(control.Key, invalid); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "Rejects non-finite " + control.Key);
            }
        }
    }

    private static unsafe void VignetteControls(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(800, 700);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var camera = new CameraSceneObject();
            camera.AddEffect(CameraEffectType.Vignette);
            var panel = new PropertiesPanel();
            var draw = typeof(PropertiesPanel).GetMethod("DrawVignetteControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
            System.Numerics.Vector2 input = default;
            void Frame(bool down = false)
            {
                io.AddMousePosEvent(input.X, input.Y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600), ImGuiCond.Always);
                ImGui.Begin("Vignette controls");
                var start = ImGui.GetCursorScreenPos();
                float rowHeight = ImGui.GetFrameHeightWithSpacing();
                draw.Invoke(panel, [camera, 0]);
                Check(ImGui.GetCursorScreenPos().Y - start.Y >= rowHeight * 4f - 1f,
                    "Three percentage sliders and color picker are visible");
                input = start + new System.Numerics.Vector2(350, rowHeight + ImGui.GetFrameHeight() / 2f);
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            Frame();
            Frame(true);
            Frame(false);
            Check(camera.Effects[0].Vignette.Softness > 50f, "Softness percentage slider edits settings");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void VignetteGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Vignette regression", null, null);
        Check(window != null, "OpenGL context");
        var previousContext = ImGui.GetCurrentContext();
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
                var pass = typeof(Viewport).GetMethod("ApplyVignettePass", flags)!;
                var camera = new CameraSceneObject();
                byte[] input = new byte[width * height * 4];
                for (int i = 0; i < input.Length; i += 4)
                {
                    input[i] = input[i + 1] = input[i + 2] = 200;
                    input[i + 3] = (byte)(i / 4 % 256);
                }
                byte[] Render()
                {
                    gl.ActiveTexture(GLEnum.Texture0);
                    gl.BindTexture(GLEnum.Texture2D, Field("_colorTex"));
                    fixed (byte* data = input)
                        gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    gl.Enable(GLEnum.DepthTest);
                    gl.Enable(GLEnum.CullFace);
                    gl.Enable(GLEnum.Blend);
                    pass.Invoke(viewport, [Field("_fbo"), Field("_glowFbo"), Field("_glowTex"), width, height, camera]);
                    Check(gl.IsEnabled(GLEnum.DepthTest) && gl.IsEnabled(GLEnum.CullFace) && gl.IsEnabled(GLEnum.Blend),
                        "Vignette preserves GL enable state");
                    byte[] output = new byte[input.Length];
                    gl.BindTexture(GLEnum.Texture2D, Field("_colorTex"));
                    fixed (byte* data = output)
                        gl.GetTexImage(GLEnum.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    Equal(GLEnum.NoError, gl.GetError(), "Vignette has no GL errors");
                    for (int i = 3; i < input.Length; i += 4)
                        Equal(input[i], output[i], "Exact alpha preservation");
                    return output;
                }
                Check(input.SequenceEqual(Render()), "No effect retains exact pixels");
                camera.AddEffect(CameraEffectType.Vignette);
                var settings = camera.Effects[0].Vignette;
                int center = ((int)(height / 2) * (int)width + (int)(width / 2)) * 4;
                int transition = (int)(height / 2) * (int)width * 4;
                byte[] defaults = Render();
                Equal((byte)200, defaults[center], "Center stays untouched");
                Check(Math.Abs(defaults[0] - 100) <= 1, "Default strength halves corner brightness");
                Equal(defaults[0], defaults[((int)width - 1) * 4], "Horizontal symmetry");
                Equal(defaults[0], defaults[((int)height - 1) * (int)width * 4], "Vertical symmetry");
                settings.Strength = 0f;
                Check(input.SequenceEqual(Render()), "Zero strength is exact identity");
                settings.Strength = 100f;
                settings.Radius = 100f;
                Check(input.SequenceEqual(Render()), "Full radius is exact identity");
                settings.Radius = 50f;
                settings.Softness = 0f;
                byte[] hard = Render();
                Equal((byte)0, hard[transition], "Zero softness has a hard outer edge");
                Equal((byte)200, hard[center], "Hard edge leaves the center");
                settings.Softness = 100f;
                byte[] soft = Render();
                Check(soft[transition] > hard[transition] + 50 && soft[transition] < 200, "Softness creates a measurable transition");
                settings.Radius = 75f;
                Equal((byte)200, Render()[transition], "Increasing radius expands unaffected region");
                settings.Radius = 0f;
                settings.Softness = 0f;
                settings.ColorR = 1f;
                settings.ColorG = 0.25f;
                byte[] colored = Render();
                Equal((byte)255, colored[0], "Color red channel");
                Check(Math.Abs(colored[1] - 64) <= 1, "Color green channel");
                Equal((byte)0, colored[2], "Color blue channel");
                settings.Strength = 50f;
                camera.AddEffect(CameraEffectType.Vignette);
                var second = camera.Effects[1].Vignette;
                second.Radius = second.Softness = 0f;
                second.ColorB = 1f;
                byte[] stacked = Render();
                camera.Effects.Reverse();
                byte[] reversed = Render();
                Check(stacked[0] < reversed[0] - 40 && stacked[2] > reversed[2] + 40, "Multiple effects retain list order");
                Check(reversed.SequenceEqual(Render()), "Repeated rendering is deterministic");
                var shader = (Shader)typeof(Viewport).GetField("_vignetteShader", flags)!.GetValue(viewport)!;
                gl.GetProgram(shader.ShaderProgram, GLEnum.LinkStatus, out int linked);
                Equal(1, linked, "Vignette shader compiles and links");

                var panel = new PropertiesPanel
                {
                    TransparentBackground = true, ShadowsEnabled = false, FogEnabled = false,
                    ColorManagementEnabled = false, AntiAliasingEnabled = false
                };
                viewport.PropertiesPanel = panel;
                using var cube = new CubeMesh(gl) { Unlit = true, BlendColor = new vec4(0.8f, 0.6f, 0.4f, 1f) };
                var obj = new SceneObject();
                obj.Visuals.Add(cube);
                viewport.SceneObjects.Add(obj);
                byte[] Scene(bool highQuality, CameraSceneObject? effectCamera)
                {
                    preview.RenderScenePublic(viewport.Camera, effectCamera, width, height, highQuality: highQuality,
                        applyCameraEffects: true);
                    byte[] output = new byte[input.Length];
                    gl.BindTexture(GLEnum.Texture2D, preview.ColorTexture);
                    fixed (byte* data = output)
                        gl.GetTexImage(GLEnum.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    return output;
                }
                var fullCamera = new CameraSceneObject();
                fullCamera.AddEffect(CameraEffectType.Vignette);
                fullCamera.Effects[0].Vignette.Radius = fullCamera.Effects[0].Vignette.Softness = 0f;
                fullCamera.Effects[0].Vignette.Strength = 100f;
                foreach (bool highQuality in new[] { false, true })
                {
                    fullCamera.Effects[0].Vignette.Strength = 0f;
                    byte[] baseline = Scene(highQuality, fullCamera);
                    fullCamera.Effects[0].Vignette.Strength = 100f;
                    byte[] vignette = Scene(highQuality, fullCamera);
                    Check(baseline[center] > 50 && vignette[center] == 0, "Camera effect is wired to fast/high-quality previews");
                    for (int i = 3; i < baseline.Length; i += 4)
                        Equal(baseline[i], vignette[i], "Preview retains coverage");
                    string path = fixture.NewPath(".png");
                    new MineImatorSimplyRemade.core.render.RenderExporter(preview)
                        .ExportImageFromRgb((int)width, (int)height, "png", path, vignette, rgba: true);
                    using var stream = System.IO.File.OpenRead(path);
                    var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                    Equal((int)width, image.Width, "Export width");
                    Equal((int)height, image.Height, "Export height");
                    Check(image.Data.Where((_, i) => i % 4 != 3).All(value => value == 0), "Export retains vignette RGB");
                    Check(image.Data.Where((_, i) => i % 4 == 3).Any(value => value == 0) &&
                          image.Data.Where((_, i) => i % 4 == 3).Any(value => value == 255), "Export retains transparent and opaque pixels");
                }
                Equal(GLEnum.NoError, gl.GetError(), "Preview/export have no GL errors");
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
            ImGui.SetCurrentContext(previousContext);
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
