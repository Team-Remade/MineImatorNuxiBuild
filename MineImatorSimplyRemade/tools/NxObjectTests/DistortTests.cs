using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;
using StbImageSharp;

internal static partial class Program
{
    private static void DistortPersistence(Fixture fixture)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectCameraEffectEntry)!;
        Near(100f, legacy.Distort.ZoomAmount, "Legacy neutral zoom");
        Near(0f, legacy.Distort.DistortAmount, "Legacy neutral distortion");
        Check(!legacy.Distort.RepeatImage, "Repeat defaults off");
        Equal(7, (int)CameraEffectType.ChromaticAberration, "Existing effect IDs unchanged");
        var camera = new CameraSceneObject();
        camera.AddEffect(CameraEffectType.Distort);
        var settings = camera.Effects[0].Distort;
        settings.RepeatImage = true;
        settings.ZoomAmount = 175f;
        settings.DistortAmount = -63f;
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
        foreach (var copy in new[] { saved.CameraEffects[0].Distort, archive.Root.CameraEffects[0].Distort,
                     library.CameraEffects[0].Distort, loaded.Effects[0].Distort,
                     duplicate.Effects[0].Distort, CameraSceneObject.CloneEffect(camera.Effects[0]).Distort })
        {
            Check(!ReferenceEquals(settings, copy), "Copies own independent distort settings");
            foreach (var control in DistortSettings.Controls)
                Near(settings.GetValue(control.Key), copy.GetValue(control.Key), "Persists " + control.Key);
        }
        duplicate.Effects[0].Distort.DistortAmount = 100f;
        Near(-63f, settings.DistortAmount, "Duplicate edits do not mutate source");
        library.CameraEffects[0].Distort.ZoomAmount = 900f;
        Near(175f, saved.CameraEffects[0].Distort.ZoomAmount, "Library edits do not mutate source");
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { camera }])!;
        ulong original = Hash();
        foreach (var control in DistortSettings.Controls)
        {
            camera.Effects[0].Distort.SetValue(control.Key, control.Min);
            Check(Hash() != original, "Dirty detection includes " + control.Key);
            apply.Invoke(null, [camera, snapshot, null]);
            Equal(original, Hash(), "Undo restores " + control.Key);
        }
        settings = camera.Effects[0].Distort;
        var timeline = new Timeline();
        var paths = ((IEnumerable<string>)typeof(Timeline).GetMethod("GetKeyframePropertyPaths", statics)!
            .Invoke(null, [camera])!).ToArray();
        var get = typeof(Timeline).GetMethod("GetPropertyValue", instance)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", instance)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", instance)!;
        foreach (var control in DistortSettings.Controls)
        {
            string path = "camera.effect.0.distort." + control.Key;
            Check(paths.Contains(path), "Timeline discovers " + path);
            var keys = new List<TimelineKeyframe>
            {
                new() { Frame = 0, Value = control.Min }, new() { Frame = 10, Value = control.Max }
            };
            foreach (int frame in new[] { 0, 5, 9, 10, 5, 0 })
            {
                float value = Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, frame, false]));
                set.Invoke(timeline, [camera, path, value]);
                float expected = control.Key == "repeat_image" ? (frame == 10 ? 1f : 0f)
                    : control.Min + (control.Max - control.Min) * frame / 10f;
                Near(expected, Convert.ToSingle(get.Invoke(timeline, [camera, path])), "Interpolation/seek " + path);
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

    private static unsafe void DistortControls(Fixture fixture)
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
            camera.AddEffect(CameraEffectType.Distort);
            var panel = new PropertiesPanel();
            var draw = typeof(PropertiesPanel).GetMethod("DrawDistortControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
            System.Numerics.Vector2 input = default;
            int row = 0;
            void Frame(bool down = false)
            {
                io.AddMousePosEvent(input.X, input.Y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600), ImGuiCond.Always);
                ImGui.Begin("Distort controls");
                var start = ImGui.GetCursorScreenPos();
                float rowHeight = ImGui.GetFrameHeightWithSpacing();
                draw.Invoke(panel, [camera, 0]);
                Check(ImGui.GetCursorScreenPos().Y - start.Y >= rowHeight * 3f - 1f,
                    "Two percentage sliders and repeat checkbox are visible");
                input = start + new System.Numerics.Vector2(row == 0 ? 8 : 350,
                    row * rowHeight + ImGui.GetFrameHeight() / 2f);
                ImGui.End();
                ImGui.Render();
            }
            for (row = 0; row < 3; row++)
            {
                var control = DistortSettings.Controls[row];
                float before = camera.Effects[0].Distort.GetValue(control.Key);
                Frame();
                Frame();
                Frame(true);
                Frame(false);
                Check(before != camera.Effects[0].Distort.GetValue(control.Key), "UI edits " + control.Key);
            }
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void DistortGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Distort regression", null, null);
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
            const uint width = 100, height = 80;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var viewport = new Viewport { Gl = gl, OverlaysEnabled = false };
            viewport.InitFramebuffer(width, height);
            var preview = new Viewport { Gl = gl, IsPreviewViewport = true, MainViewport = viewport, OverlaysEnabled = false };
            preview.InitPreviewViewport(width, height);
            try
            {
                uint Field(string name) => (uint)typeof(Viewport).GetField(name, flags)!.GetValue(viewport)!;
                var pass = typeof(Viewport).GetMethod("ApplyDistortPass", flags)!;
                var camera = new CameraSceneObject();
                byte[] input = new byte[width * height * 4];
                int Pixel(int x, int y) => (y * (int)width + x) * 4;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int i = Pixel(x, y);
                        input[i] = (byte)(x * 2);
                        input[i + 1] = (byte)(y * 3);
                        input[i + 2] = 73;
                        input[i + 3] = 255;
                    }
                byte[] Read(uint texture)
                {
                    byte[] output = new byte[input.Length];
                    gl.BindTexture(GLEnum.Texture2D, texture);
                    fixed (byte* data = output)
                        gl.GetTexImage(GLEnum.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    return output;
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
                        "Pass restores GL enable state");
                    byte[] output = Read(Field("_colorTex"));
                    Equal(GLEnum.NoError, gl.GetError(), "No OpenGL errors");
                    return output;
                }
                Check(input.SequenceEqual(Render()), "No effect is exact identity");
                camera.AddEffect(CameraEffectType.Distort);
                var settings = camera.Effects[0].Distort;
                Check(input.SequenceEqual(Render()), "Neutral effect is exact identity");
                settings.RepeatImage = true;
                Check(input.SequenceEqual(Render()), "Repeat alone is identity");
                settings.RepeatImage = false;
                settings.ZoomAmount = 200f;
                byte[] zoom = Render();
                Check(Math.Abs(zoom[Pixel(10, 40)] - 59) <= 1, "200% zoom samples half the offset from center");
                settings.ZoomAmount = 100f;
                settings.DistortAmount = 100f;
                byte[] positive = Render();
                Check(positive[Pixel(10, 40)] > input[Pixel(10, 40)] + 15, "Positive distortion bulges left side");
                Check(positive[Pixel(85, 40)] < input[Pixel(85, 40)] - 10, "Positive distortion bulges right side");
                settings.DistortAmount = -100f;
                byte[] negative = Render();
                Equal((byte)0, negative[Pixel(0, 0) + 3], "Negative distortion exposes transparent corners");
                Equal((byte)0, negative[Pixel(0, 0)], "Exposed corners have black RGB");
                Check(negative[Pixel(20, 40)] < input[Pixel(20, 40)] - 10, "Negative distortion pulls left side inward");
                Check(Math.Abs(negative[Pixel(50, 40)] - input[Pixel(50, 40)]) <= 1, "Center stays anchored");
                settings.RepeatImage = true;
                byte[] repeated = Render();
                for (int i = 3; i < repeated.Length; i += 4)
                    Equal((byte)255, repeated[i], "Repeat fills exposed pixels");
                Check(repeated[Pixel(0, 0)] > 50, "Negative coordinates wrap rather than clamp");
                settings.ZoomAmount = 1f;
                settings.DistortAmount = 0f;
                byte[] tiny = Render();
                // At 1% zoom, this output pixel samples x=99.5,y=9.5 after wrapping.
                Check(Math.Abs(tiny[Pixel(0, 0)] - 99) <= 1, "Repeat interpolates across tile seam");
                Check(Math.Abs(tiny[Pixel(0, 0) + 1] - 28.5f) <= 1, "Repeat wraps multiple tiles vertically");
                settings.ZoomAmount = 50f;
                settings.RepeatImage = false;
                Equal((byte)0, Render()[Pixel(0, 0) + 3], "Zoom out also exposes transparent corners");
                settings.ZoomAmount = 100f;
                settings.DistortAmount = -30f;
                for (int i = 0; i < input.Length; i += 4)
                {
                    input[i] = input[i + 1] = input[i + 2] = 73;
                    input[i + 3] = (byte)((i / 4) % 256);
                    if (input[i + 3] == 0) input[i] = 255;
                }
                byte[] alpha = Render();
                Check(!input.SequenceEqual(alpha), "Alpha moves with distorted geometry");
                for (int i = 0; i < alpha.Length; i += 4)
                    if (alpha[i + 3] > 0)
                        Check(Math.Abs(alpha[i] - 73) <= 1, "Hidden RGB cannot bleed into warped image");
                settings.RepeatImage = true;
                settings.ZoomAmount = 140f;
                settings.DistortAmount = 35f;
                camera.AddEffect(CameraEffectType.Distort);
                camera.Effects[1].Distort.ZoomAmount = 75f;
                camera.Effects[1].Distort.DistortAmount = -70f;
                camera.Effects[1].Distort.RepeatImage = true;
                byte[] stacked = Render();
                camera.Effects.Reverse();
                Check(!stacked.SequenceEqual(Render()), "Repeated distort effects preserve list order");
                Check(Render().SequenceEqual(Render()), "Distortion is deterministic");
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
                sceneCamera.AddEffect(CameraEffectType.Distort);
                byte[] Scene(bool highQuality, bool apply)
                {
                    preview.RenderScenePublic(viewport.Camera, sceneCamera, width, height,
                        highQuality: highQuality, applyCameraEffects: apply);
                    return Read(preview.ColorTexture);
                }
                foreach (bool highQuality in new[] { false, true })
                {
                    sceneCamera.Effects[0].Distort.ZoomAmount = 100f;
                    byte[] baseline = Scene(highQuality, true);
                    sceneCamera.Effects[0].Distort.ZoomAmount = 170f;
                    sceneCamera.Effects[0].Distort.DistortAmount = -65f;
                    byte[] effected = Scene(highQuality, true);
                    Check(!baseline.SequenceEqual(effected), "Effect wired to fast/high-quality preview");
                    bool alphaChanged = false;
                    for (int i = 3; i < baseline.Length; i += 4)
                        alphaChanged |= baseline[i] != effected[i];
                    Check(alphaChanged, "Preview alpha follows warped geometry");
                    sceneCamera.Effects[0].Distort.DistortAmount = 0f;
                    string path = fixture.NewPath(".png");
                    new MineImatorSimplyRemade.core.render.RenderExporter(preview)
                        .ExportImageFromRgb((int)width, (int)height, "png", path, effected, rgba: true);
                    using var stream = File.OpenRead(path);
                    var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                    Equal((int)width, image.Width, "Export width");
                    Equal((int)height, image.Height, "Export height");
                    Check(effected.SequenceEqual(image.Data), "Export pixels and alpha match preview");
                }
                Equal(GLEnum.NoError, gl.GetError(), "Preview and export have no OpenGL errors");
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
