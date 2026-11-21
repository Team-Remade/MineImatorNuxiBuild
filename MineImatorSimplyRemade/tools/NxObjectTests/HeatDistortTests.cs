using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.render;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;
using StbImageSharp;

internal static partial class Program
{
    private static void HeatDistortPersistence(Fixture fixture)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectCameraEffectEntry)!;
        Check(float.IsFinite(legacy.HeatDistort.Strength) && legacy.HeatDistort.Scale > 0f,
            "Older camera entries retain usable defaults");
        var camera = new CameraSceneObject();
        camera.AddEffect(CameraEffectType.HeatDistort);
        var settings = camera.Effects[0].HeatDistort;
        settings.Strength = 47f;
        settings.Speed = 175f;
        settings.Scale = 220f;
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(camera);
        var saved = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(snapshot, AppJsonContext.Default.ProjectSceneObjectEntry),
            AppJsonContext.Default.ProjectSceneObjectEntry)!;
        var (archive, _) = fixture.Roundtrip(camera);
        var library = (ProjectSceneObjectEntry)typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", statics)!
            .Invoke(null, [saved])!;
        var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", statics)!;
        var loaded = new CameraSceneObject();
        apply.Invoke(null, [loaded, saved, null]);
        var duplicate = (CameraSceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate", instance)!
            .Invoke(new SceneTree(), [camera, new Dictionary<string, string>()])!;
        foreach (var copy in new[] { saved.CameraEffects[0].HeatDistort, archive.Root.CameraEffects[0].HeatDistort,
                     library.CameraEffects[0].HeatDistort, loaded.Effects[0].HeatDistort,
                     duplicate.Effects[0].HeatDistort, CameraSceneObject.CloneEffect(camera.Effects[0]).HeatDistort })
        {
            foreach (var control in HeatDistortSettings.Controls)
                Near(settings.GetValue(control.Key), copy.GetValue(control.Key), "Saved/copied heat distortion control");
            Check(!ReferenceEquals(settings, copy), "Copies own independent settings");
            copy.Strength = 1f;
            copy.Speed = 2f;
            copy.Scale = 3f;
            Near(47f, settings.Strength, "Copy edits preserve source strength");
            Near(175f, settings.Speed, "Copy edits preserve source speed");
            Near(220f, settings.Scale, "Copy edits preserve source scale");
        }
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { camera }])!;
        ulong original = Hash();
        foreach (var control in HeatDistortSettings.Controls)
        {
            settings = camera.Effects[0].HeatDistort;
            settings.SetValue(control.Key, settings.GetValue(control.Key) + 1f);
            Check(Hash() != original, $"{control.Key} changes dirty fingerprint");
            apply.Invoke(null, [camera, snapshot, null]);
            Equal(original, Hash(), $"Undo restores {control.Key}");
        }

        var paths = (IEnumerable<string>)typeof(Timeline).GetMethod("GetKeyframePropertyPaths", statics)!
            .Invoke(null, [camera])!;
        var timeline = new Timeline();
        var get = typeof(Timeline).GetMethod("GetPropertyValue", instance)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", instance)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", instance)!;
        foreach (var control in HeatDistortSettings.Controls)
        {
            string path = $"camera.effect.0.heat_distort.{control.Key}";
            Check(paths.Contains(path), $"Timeline discovers {control.Key}");
            var keys = new List<TimelineKeyframe>
            {
                new() { Frame = 0, Value = 5f }, new() { Frame = 10, Value = 55f }
            };
            foreach (int frame in new[] { 0, 5, 10, 5, 0 })
            {
                float value = Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, frame]));
                set.Invoke(timeline, [camera, path, value]);
                Near(5f + frame * 5f, Convert.ToSingle(get.Invoke(timeline, [camera, path])),
                    $"{control.Key} animates and reverse seeks");
            }
            settings = camera.Effects[0].HeatDistort;
            settings.SetValue(control.Key, -100f);
            Near(-100f, settings.GetValue(control.Key), $"{control.Key} accepts negative values");
            settings.SetValue(control.Key, 10000f);
            Near(10000f, settings.GetValue(control.Key), $"{control.Key} accepts values beyond its slider range");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                bool rejected = false;
                try { settings.SetValue(control.Key, invalid); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, $"Nonfinite {control.Key} rejected");
            }
        }
    }

    private static unsafe void HeatDistortControls(Fixture fixture)
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
            camera.AddEffect(CameraEffectType.HeatDistort);
            var panel = new PropertiesPanel();
            var draw = typeof(PropertiesPanel).GetMethod("DrawHeatDistortControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
            System.Numerics.Vector2 input = default, start = default;
            float pitch = 0f;
            void Frame(bool down = false)
            {
                io.AddMousePosEvent(input.X, input.Y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 300), ImGuiCond.Always);
                ImGui.Begin("Heat distort controls");
                start = ImGui.GetCursorScreenPos();
                pitch = ImGui.GetFrameHeightWithSpacing();
                draw.Invoke(panel, [camera, 0]);
                Near(pitch * 3f, ImGui.GetCursorScreenPos().Y - start.Y, "Three visible percentage sliders");
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            for (int i = 0; i < HeatDistortSettings.Controls.Length; i++)
            {
                var control = HeatDistortSettings.Controls[i];
                float before = camera.Effects[0].HeatDistort.GetValue(control.Key);
                input = start + new System.Numerics.Vector2(200f, pitch * (i + 0.5f));
                Frame();
                Frame(true);
                Frame(false);
                Check(camera.Effects[0].HeatDistort.GetValue(control.Key) != before,
                    $"Actual ImGui {control.Key} slider edits settings");
            }
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void HeatDistortGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Heat distort regression", null, null);
        Check(window != null, "OpenGL context");
        var singleton = typeof(Timeline).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!;
        var previousTimeline = singleton.GetValue(null);
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
            const uint width = 128, height = 96;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var viewport = new Viewport { Gl = gl, OverlaysEnabled = false };
            viewport.InitFramebuffer(width, height);
            var preview = new Viewport { Gl = gl, IsPreviewViewport = true, MainViewport = viewport, OverlaysEnabled = false };
            preview.InitPreviewViewport(width, height);
            try
            {
                uint Field(string name) => (uint)typeof(Viewport).GetField(name, flags)!.GetValue(viewport)!;
                var pass = typeof(Viewport).GetMethod("ApplyHeatDistortPass", flags)!;
                var timeline = new Timeline();
                singleton.SetValue(null, timeline);
                var frameField = typeof(Timeline).GetField("_currentFrame", flags)!;
                int Pixel(int x, int y) => (y * (int)width + x) * 4;
                byte[] input = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int p = Pixel(x, y);
                        input[p] = input[p + 3] = (byte)(x * 2);
                        input[p + 1] = (byte)((x / 4 + y / 4) % 2 * 255);
                        input[p + 2] = (byte)(y * 2);
                    }
                byte[] Read(uint texture)
                {
                    byte[] output = new byte[input.Length];
                    gl.BindTexture(GLEnum.Texture2D, texture);
                    fixed (byte* data = output)
                        gl.GetTexImage(GLEnum.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    return output;
                }
                var camera = new CameraSceneObject();
                byte[] Render(int frame = 0, byte[]? source = null)
                {
                    frameField.SetValue(timeline, frame);
                    gl.ActiveTexture(GLEnum.Texture0);
                    gl.BindTexture(GLEnum.Texture2D, Field("_colorTex"));
                    fixed (byte* data = source ?? input)
                        gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    gl.Enable(GLEnum.DepthTest);
                    gl.Enable(GLEnum.CullFace);
                    gl.Enable(GLEnum.Blend);
                    pass.Invoke(viewport, [Field("_fbo"), Field("_glowFbo"), Field("_glowTex"), width, height, camera]);
                    Check(gl.IsEnabled(GLEnum.DepthTest) && gl.IsEnabled(GLEnum.CullFace) && gl.IsEnabled(GLEnum.Blend),
                        "GL enable state preserved");
                    return Read(Field("_colorTex"));
                }
                Check(input.SequenceEqual(Render()), "No effect leaves exact original pixels");
                camera.AddEffect(CameraEffectType.HeatDistort);
                var settings = camera.Effects[0].HeatDistort;
                settings.Strength = 0f;
                Check(input.SequenceEqual(Render()), "Zero strength is an exact bypass");
                settings.Strength = 100f;
                byte[] distorted = Render();
                Check(!input.SequenceEqual(distorted), "Heat distortion changes actual pixels");
                for (int p = 0; p < distorted.Length; p += 4)
                {
                    Equal(distorted[p], distorted[p + 3], "Alpha is displaced with RGB, not overwritten");
                    Check(Math.Abs(distorted[p] - input[p]) <= 6, "Horizontal displacement stays within 2.5% of shorter side");
                    Check(distorted[p + 2] <= 190, "Border samples clamp without wrapping");
                }
                Check(!distorted.SequenceEqual(Render(30)), "Timeline advances shimmer");
                Check(distorted.SequenceEqual(Render(0)), "Backward seeking exactly reproduces pixels");
                Check(Render(15).SequenceEqual(Render(15)), "Paused redraw is deterministic");
                settings.Speed = 0f;
                Check(Render(0).SequenceEqual(Render(90)), "Zero speed freezes animation");
                settings.Speed = 200f;
                byte[] doubleSpeed = Render(15);
                settings.Speed = 100f;
                Check(doubleSpeed.SequenceEqual(Render(30)), "Speed percentage scales timeline time");
                settings.Scale = 200f;
                Check(!distorted.SequenceEqual(Render()), "Scale changes ripple size");
                settings.Scale = 100f;
                settings.Strength = 25f;
                byte[] weak = Render();
                Check(!weak.SequenceEqual(distorted), "Strength changes displacement");
                for (int p = 0; p < weak.Length; p += 4)
                    Check(Math.Abs(weak[p] - input[p]) <= 2, "25% strength reduces displacement bound");
                settings.Strength = 100f;
                byte[] twice = Render(0, distorted);
                camera.AddEffect(CameraEffectType.HeatDistort);
                camera.Effects[1].HeatDistort.Strength = 100f;
                Check(twice.SequenceEqual(Render()), "Multiple heat effects stack");
                camera.Effects.RemoveAt(1);
                Equal(GLEnum.NoError, gl.GetError(), "No OpenGL errors");

                viewport.PropertiesPanel = new PropertiesPanel
                {
                    TransparentBackground = true, ShadowsEnabled = false, FogEnabled = false,
                    ColorManagementEnabled = false, AntiAliasingEnabled = false
                };
                using var cube = new CubeMesh(gl) { Unlit = true, BlendColor = new vec4(0.8f, 0.6f, 0.4f, 1f) };
                var obj = new SceneObject();
                obj.Visuals.Add(cube);
                viewport.SceneObjects.Add(obj);
                var sceneCamera = new CameraSceneObject();
                sceneCamera.AddEffect(CameraEffectType.HeatDistort);
                sceneCamera.ViewCamera.Target = viewport.Camera.Target;
                sceneCamera.ViewCamera.Yaw = viewport.Camera.Yaw;
                sceneCamera.ViewCamera.Pitch = viewport.Camera.Pitch;
                sceneCamera.ViewCamera.Distance = viewport.Camera.Distance;
                sceneCamera.SyncTransformFromCamera();
                viewport.SceneObjects.Add(sceneCamera);
                frameField.SetValue(timeline, 0);
                foreach (bool highQuality in new[] { false, true })
                {
                    byte[] Scene(float strength)
                    {
                        sceneCamera.Effects[0].HeatDistort.Strength = strength;
                        preview.RenderScenePublic(sceneCamera.ViewCamera, sceneCamera, width, height,
                            highQuality: highQuality, applyCameraEffects: true);
                        return Read(preview.ColorTexture);
                    }
                    byte[] baseline = Scene(0f);
                    byte[] effected = Scene(100f);
                    Check(!baseline.SequenceEqual(effected), "Heat distortion affects fast/high-quality scene preview");
                    Check(Render(0, baseline).SequenceEqual(effected), "Preview matches the heat distortion pass exactly");
                    Check(effected.SequenceEqual(Scene(100f)), "Repeated preview pixels are identical");
                    preview.ToggleHighQualityPreview();
                    sceneCamera.Effects[0].HeatDistort.Strength = 0f;
                    Check(preview.CaptureCurrentView(width, height, highQuality, out _, rgba: true),
                        "Baseline export capture succeeds");
                    byte[] exportBaseline = Read(preview.ColorTexture);
                    sceneCamera.Effects[0].HeatDistort.Strength = 100f;
                    string path = fixture.NewPath(".png");
                    new RenderExporter(preview).ExportImage((int)width, (int)height, "png", path, highQuality);
                    preview.ToggleHighQualityPreview();
                    byte[] captured = Read(preview.ColorTexture);
                    Check(!exportBaseline.SequenceEqual(captured), "Heat distortion affects actual export pixels");
                    Check(Render(0, exportBaseline).SequenceEqual(captured), "Export matches the preview heat distortion pass");
                    using var stream = System.IO.File.OpenRead(path);
                    var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                    Equal((int)width, image.Width, "PNG width");
                    Equal((int)height, image.Height, "PNG height");
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            for (int c = 0; c < 4; c++)
                                Equal(captured[Pixel(x, (int)height - y - 1) + c], image.Data[Pixel(x, y) + c],
                                    "PNG matches captured distorted RGB and alpha");
                }
                Equal(GLEnum.NoError, gl.GetError(), "Preview/export OpenGL errors");
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
            singleton.SetValue(null, previousTimeline);
        }
    }
}
