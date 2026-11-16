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
    private static void CinematicBarsPersistence(Fixture fixture)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectCameraEffectEntry)!;
        Check(legacy.CinematicBars.Size > 0f, "Older camera entries retain usable bar defaults");
        var camera = new CameraSceneObject();
        camera.AddEffect(CameraEffectType.CinematicBars);
        float[] values = [18f, 35f, -12f, 0.2f, 0.4f, 0.8f];
        for (int i = 0; i < values.Length; i++)
            camera.Effects[0].CinematicBars.SetValue(CinematicBarsSettings.Controls[i].Key, values[i]);
        var settings = camera.Effects[0].CinematicBars;
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
        foreach (var copy in new[] { saved.CameraEffects[0].CinematicBars, archive.Root.CameraEffects[0].CinematicBars,
                     library.CameraEffects[0].CinematicBars, loaded.Effects[0].CinematicBars,
                     duplicate.Effects[0].CinematicBars, CameraSceneObject.CloneEffect(camera.Effects[0]).CinematicBars })
        {
            Check(!ReferenceEquals(settings, copy), "Copies own independent bar settings");
            foreach (var control in CinematicBarsSettings.Controls)
            {
                Near(settings.GetValue(control.Key), copy.GetValue(control.Key), $"Saved/copied {control.Key}");
                copy.SetValue(control.Key, control.Min);
            }
            Near(18f, settings.Size, "Copy edits preserve source size");
            Near(0.8f, settings.ColorB, "Copy edits preserve source color");
        }
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { camera }])!;
        ulong original = Hash();
        foreach (var control in CinematicBarsSettings.Controls)
        {
            settings = camera.Effects[0].CinematicBars;
            settings.SetValue(control.Key, settings.GetValue(control.Key) + 0.1f);
            Check(Hash() != original, $"{control.Key} marks scene dirty");
            apply.Invoke(null, [camera, snapshot, null]);
            Equal(original, Hash(), $"Undo restores {control.Key}");
        }

        var paths = (IEnumerable<string>)typeof(Timeline).GetMethod("GetKeyframePropertyPaths", statics)!
            .Invoke(null, [camera])!;
        var timeline = new Timeline();
        var get = typeof(Timeline).GetMethod("GetPropertyValue", instance)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", instance)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", instance)!;
        foreach (var control in CinematicBarsSettings.Controls)
        {
            string path = $"camera.effect.0.cinematic_bars.{control.Key}";
            Check(paths.Contains(path), $"Timeline discovers {control.Key}");
            float start = control.Min + (control.Max - control.Min) * 0.25f;
            float end = control.Min + (control.Max - control.Min) * 0.75f;
            var keys = new List<TimelineKeyframe>
            {
                new() { Frame = 0, Value = start }, new() { Frame = 10, Value = end }
            };
            foreach (int frame in new[] { 0, 5, 10, 5, 0 })
            {
                float value = Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, frame]));
                set.Invoke(timeline, [camera, path, value]);
                Near(start + (end - start) * frame / 10f, Convert.ToSingle(get.Invoke(timeline, [camera, path])),
                    $"{control.Key} animates and reverse seeks");
            }
            settings = camera.Effects[0].CinematicBars;
            settings.SetValue(control.Key, -10000f);
            Near(control.Min, settings.GetValue(control.Key), $"{control.Key} lower limit");
            settings.SetValue(control.Key, 10000f);
            Near(control.Max, settings.GetValue(control.Key), $"{control.Key} upper limit");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                bool rejected = false;
                try { settings.SetValue(control.Key, invalid); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, $"Nonfinite {control.Key} rejected");
            }
        }
    }

    private static unsafe void CinematicBarsControls(Fixture fixture)
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
            camera.AddEffect(CameraEffectType.CinematicBars);
            var panel = new PropertiesPanel();
            var draw = typeof(PropertiesPanel).GetMethod("DrawCinematicBarsControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
            System.Numerics.Vector2 input = default, start = default;
            float pitch = 0f;
            bool pickerOpen = false;
            void Frame(bool down = false)
            {
                io.AddMousePosEvent(input.X, input.Y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 400), ImGuiCond.Always);
                ImGui.Begin("Cinematic bars controls");
                start = ImGui.GetCursorScreenPos();
                pitch = ImGui.GetFrameHeightWithSpacing();
                draw.Invoke(panel, [camera, 0]);
                Near(pitch * 4f, ImGui.GetCursorScreenPos().Y - start.Y, "Three sliders and one color picker");
                pickerOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId);
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            for (int i = 0; i < 3; i++)
            {
                var control = CinematicBarsSettings.Controls[i];
                float before = camera.Effects[0].CinematicBars.GetValue(control.Key);
                input = start + new System.Numerics.Vector2(250f, pitch * (i + 0.5f));
                Frame();
                Frame(true);
                Frame(false);
                Check(camera.Effects[0].CinematicBars.GetValue(control.Key) != before,
                    $"Actual ImGui {control.Key} slider edits settings");
            }
            input = start + new System.Numerics.Vector2(8f, pitch * 3f + 8f);
            Frame();
            Frame(true);
            Frame(false);
            Check(pickerOpen, "Actual color button opens its picker");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void CinematicBarsGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Cinematic bars regression", null, null);
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
            const uint width = 128, height = 96;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var viewport = new Viewport { Gl = gl, OverlaysEnabled = false };
            viewport.InitFramebuffer(width, height);
            var preview = new Viewport { Gl = gl, IsPreviewViewport = true, MainViewport = viewport, OverlaysEnabled = false };
            preview.InitPreviewViewport(width, height);
            try
            {
                uint Field(string name) => (uint)typeof(Viewport).GetField(name, flags)!.GetValue(viewport)!;
                var pass = typeof(Viewport).GetMethod("ApplyCinematicBarsPass", flags)!;
                byte[] input = new byte[width * height * 4];
                for (int p = 0; p < input.Length; p += 4)
                {
                    input[p] = 180;
                    input[p + 1] = 120;
                    input[p + 2] = 60;
                    input[p + 3] = (byte)(p / 4 % 256);
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
                byte[] Render(byte[]? source = null)
                {
                    gl.BindTexture(GLEnum.Texture2D, Field("_colorTex"));
                    fixed (byte* data = source ?? input)
                        gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    gl.Enable(GLEnum.DepthTest);
                    gl.Enable(GLEnum.CullFace);
                    gl.Enable(GLEnum.Blend);
                    pass.Invoke(viewport, [Field("_fbo"), width, height, camera]);
                    Check(gl.IsEnabled(GLEnum.DepthTest) && gl.IsEnabled(GLEnum.CullFace) && gl.IsEnabled(GLEnum.Blend),
                        "GL enable state preserved");
                    return Read(Field("_colorTex"));
                }
                Check(input.SequenceEqual(Render()), "No effect leaves exact original pixels");
                camera.AddEffect(CameraEffectType.CinematicBars);
                var settings = camera.Effects[0].CinematicBars;
                settings.Size = 0f;
                settings.Rotation = 90f;
                Check(input.SequenceEqual(Render()), "Zero size bypasses even rotated bars");
                settings.Size = 12.5f;
                settings.Rotation = 0f;
                byte[] horizontal = Render();
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int p = (y * (int)width + x) * 4;
                        bool bar = y < 12 || y >= height - 12;
                        for (int c = 0; c < 4; c++)
                            Equal(bar ? (byte)(c == 3 ? 255 : 0) : input[p + c], horizontal[p + c],
                                "12.5% gives exactly 12 rows on each edge, leaving opening RGBA intact");
                    }
                settings.ColorR = 0.2f;
                settings.ColorG = 0.4f;
                settings.ColorB = 0.8f;
                foreach (var (rotation, offset) in new[] { (0f, 12.5f), (90f, 0f), (35f, -10f) })
                {
                    settings.Rotation = rotation;
                    settings.Offset = offset;
                    byte[] output = Render();
                    double radians = rotation * Math.PI / 180;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            double distance = (-(x + 0.5 - width / 2.0) * Math.Sin(radians) +
                                (height / 2.0 - y - 0.5) * Math.Cos(radians)) / height - offset / 100.0;
                            if (Math.Abs(Math.Abs(distance) - 0.375) < 0.00001) continue;
                            bool bar = Math.Abs(distance) >= 0.375;
                            int p = (y * (int)width + x) * 4;
                            byte[] color = [51, 102, 204, 255];
                            for (int c = 0; c < 4; c++)
                                Equal(bar ? color[c] : input[p + c], output[p + c], "Rotated/offset colored bar pixels");
                        }
                }
                settings.Size = 50f;
                Check(Render().Where((_, i) => i % 4 == 3).All(a => a == 255), "50% completely covers transparent image");
                settings.Size = 12.5f;
                settings.Rotation = settings.Offset = 0f;
                byte[] first = Render();
                camera.AddEffect(CameraEffectType.CinematicBars);
                camera.Effects[1].CinematicBars.Rotation = 90f;
                byte[] stacked = Render();
                camera.Effects.RemoveAt(0);
                Check(stacked.SequenceEqual(Render(first)), "Multiple bar effects stack in order");
                camera.Effects.Clear();
                camera.AddEffect(CameraEffectType.CinematicBars);

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
                sceneCamera.AddEffect(CameraEffectType.CinematicBars);
                sceneCamera.ViewCamera.Target = viewport.Camera.Target;
                sceneCamera.ViewCamera.Yaw = viewport.Camera.Yaw;
                sceneCamera.ViewCamera.Pitch = viewport.Camera.Pitch;
                sceneCamera.ViewCamera.Distance = viewport.Camera.Distance;
                sceneCamera.SyncTransformFromCamera();
                viewport.SceneObjects.Add(sceneCamera);
                var modeField = typeof(Viewport).GetField("_viewportRenderMode", flags)!;
                var setMode = typeof(Viewport).GetMethod("SetRenderMode", flags)!;
                foreach (object mode in Enum.GetValues(modeField.FieldType))
                {
                    setMode.Invoke(preview, [mode]);
                    byte[] Scene(float size, bool feed = false)
                    {
                        sceneCamera.Effects[0].CinematicBars.Size = size;
                        preview.RenderScenePublic(sceneCamera.ViewCamera, sceneCamera, width, height, applyCameraEffects: feed);
                        return Read(preview.ColorTexture);
                    }
                    byte[] baseline = Scene(0f);
                    byte[] effected = Scene(12.5f);
                    Check(!baseline.SequenceEqual(effected), $"Bars visible in {mode} without forcing rendered effects");
                    Check(Render(baseline).SequenceEqual(effected), $"{mode} preview matches exact bar pass");
                    Check(Render(Scene(0f, true)).SequenceEqual(Scene(12.5f, true)), $"{mode} camera feed includes bars");
                }
                foreach (bool highQuality in new[] { false, true })
                {
                    sceneCamera.Effects[0].CinematicBars.Size = 0f;
                    Check(preview.CaptureCurrentView(width, height, highQuality, out _, rgba: true), "Baseline capture succeeds");
                    byte[] baseline = Read(preview.ColorTexture);
                    sceneCamera.Effects[0].CinematicBars.Size = 12.5f;
                    string path = fixture.NewPath(".png");
                    new RenderExporter(preview).ExportImage((int)width, (int)height, "png", path, highQuality);
                    byte[] captured = Read(preview.ColorTexture);
                    Check(Render(baseline).SequenceEqual(captured), "Export uses exact final bar composition");
                    using var stream = System.IO.File.OpenRead(path);
                    var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            for (int c = 0; c < 4; c++)
                                Equal(captured[(((int)height - y - 1) * (int)width + x) * 4 + c],
                                    image.Data[(y * (int)width + x) * 4 + c], "PNG matches composed pixels");
                }
                Equal(GLEnum.NoError, gl.GetError(), "No OpenGL errors");
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
