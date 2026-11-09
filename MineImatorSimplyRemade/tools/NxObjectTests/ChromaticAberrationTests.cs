using System.Reflection;
using System.Text.Json;
using Hexa.NET.ImGui;
using GlmSharp;
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
    private static void ChromaticAberrationPersistence(Fixture fixture)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectCameraEffectEntry)!;
        Near(0f, legacy.ChromaticAberration.BlurAmount, "Legacy blur default");
        Check(legacy.ChromaticAberration.DistortRgb, "Default radial distortion");
        Near(1f, legacy.ChromaticAberration.RedOffset, "Default red offset");
        Near(0f, legacy.ChromaticAberration.GreenOffset, "Default green offset");
        Near(-1f, legacy.ChromaticAberration.BlueOffset, "Default blue offset");
        Equal(6, (int)CameraEffectType.Vignette, "Existing serialized effect IDs unchanged");
        var camera = new CameraSceneObject();
        camera.AddEffect(CameraEffectType.ChromaticAberration);
        var settings = camera.Effects[0].ChromaticAberration;
        settings.BlurAmount = 37f;
        settings.DistortRgb = false;
        settings.RedOffset = 12f;
        settings.GreenOffset = -7f;
        settings.BlueOffset = 23f;
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
        foreach (var copy in new[] { saved.CameraEffects[0].ChromaticAberration, archive.Root.CameraEffects[0].ChromaticAberration,
                     library.CameraEffects[0].ChromaticAberration, loaded.Effects[0].ChromaticAberration,
                     duplicate.Effects[0].ChromaticAberration, CameraSceneObject.CloneEffect(camera.Effects[0]).ChromaticAberration })
        {
            Check(!ReferenceEquals(settings, copy), "Copies own independent settings");
            foreach (var control in ChromaticAberrationSettings.Controls)
                Near(settings.GetValue(control.Key), copy.GetValue(control.Key), "Persists " + control.Key);
        }
        library.CameraEffects[0].ChromaticAberration.BlurAmount = 100f;
        Near(37f, saved.CameraEffects[0].ChromaticAberration.BlurAmount, "Library edits do not mutate source");
        duplicate.Effects[0].ChromaticAberration.RedOffset = 100f;
        Near(12f, settings.RedOffset, "Duplicate edits do not mutate source");
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { camera }])!;
        ulong original = Hash();
        foreach (var control in ChromaticAberrationSettings.Controls)
        {
            camera.Effects[0].ChromaticAberration.SetValue(control.Key, control.Max);
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
        settings = camera.Effects[0].ChromaticAberration;
        foreach (var control in ChromaticAberrationSettings.Controls)
        {
            string path = "camera.effect.0.chromatic_aberration." + control.Key;
            Check(paths.Contains(path), "Timeline discovers " + path);
            var keys = new List<TimelineKeyframe>
            {
                new() { Frame = 0, Value = control.Min }, new() { Frame = 10, Value = control.Max }
            };
            foreach (int frame in new[] { 0, 5, 9, 10, 5, 0 })
            {
                float value = Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, frame, false]));
                set.Invoke(timeline, [camera, path, value]);
                float expected = control.Key == "distort_rgb" ? (frame == 10 ? 1f : 0f)
                    : control.Min + (control.Max - control.Min) * frame / 10f;
                Near(expected, Convert.ToSingle(get.Invoke(timeline, [camera, path])), "Interpolation and reverse seeking " + path);
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

    private static unsafe void ChromaticAberrationControls(Fixture fixture)
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
            camera.AddEffect(CameraEffectType.ChromaticAberration);
            var panel = new PropertiesPanel();
            var draw = typeof(PropertiesPanel).GetMethod("DrawChromaticAberrationControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
            System.Numerics.Vector2 input = default;
            int row = 0;
            void Frame(bool down = false)
            {
                io.AddMousePosEvent(input.X, input.Y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600), ImGuiCond.Always);
                ImGui.Begin("Chromatic aberration controls");
                var start = ImGui.GetCursorScreenPos();
                float rowHeight = ImGui.GetFrameHeightWithSpacing();
                var originalGrab = ImGui.GetStyle().Colors[(int)ImGuiCol.SliderGrab];
                var drawList = ImGui.GetWindowDrawList();
                int firstVertex = drawList.VtxBuffer.Size;
                draw.Invoke(panel, [camera, 0]);
                Check(ImGui.GetCursorScreenPos().Y - start.Y >= rowHeight * 5f - 1f,
                    "Four percentage sliders and checkbox are visible");
                Equal(originalGrab, ImGui.GetStyle().Colors[(int)ImGuiCol.SliderGrab], "Channel tint restores style");
                foreach (var tint in new[]
                {
                    new System.Numerics.Vector4(0.90f, 0.25f, 0.25f, 1f),
                    new System.Numerics.Vector4(0.20f, 0.75f, 0.35f, 1f),
                    new System.Numerics.Vector4(0.25f, 0.50f, 0.95f, 1f)
                })
                {
                    uint packed = ImGui.GetColorU32(tint);
                    uint activePacked = ImGui.GetColorU32(System.Numerics.Vector4.Lerp(tint, System.Numerics.Vector4.One, 0.20f));
                    bool found = false;
                    for (int vertex = firstVertex; vertex < drawList.VtxBuffer.Size; vertex++)
                        found |= drawList.VtxBuffer[vertex].Col == packed || drawList.VtxBuffer[vertex].Col == activePacked;
                    if (drawList.VtxBuffer.Size > firstVertex)
                        Check(found, "RGB sliders render with their channel colors: " + tint);
                }
                input = start + new System.Numerics.Vector2(row == 1 ? 8 : 350, row * rowHeight + ImGui.GetFrameHeight() / 2f);
                ImGui.End();
                ImGui.Render();
            }
            for (row = 0; row < 5; row++)
            {
                var control = ChromaticAberrationSettings.Controls[row];
                float before = camera.Effects[0].ChromaticAberration.GetValue(control.Key);
                Frame();
                Frame();
                Frame(true);
                Frame(false);
                Check(before != camera.Effects[0].ChromaticAberration.GetValue(control.Key), "UI edits " + control.Key);
            }
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void ChromaticAberrationGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Chromatic aberration regression", null, null);
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
                var pass = typeof(Viewport).GetMethod("ApplyChromaticAberrationPass", flags)!;
                var camera = new CameraSceneObject();
                byte[] input = new byte[width * height * 4];
                int Pixel(int x, int y) => (y * (int)width + x) * 4;
                void Gradient()
                {
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            int i = Pixel(x, y);
                            input[i] = input[i + 1] = input[i + 2] = (byte)(x * 2 + y / 2);
                            input[i + 3] = 255;
                        }
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
                        "Pass preserves GL enable state");
                    byte[] output = Read(Field("_colorTex"));
                    Equal(GLEnum.NoError, gl.GetError(), "No OpenGL errors");
                    for (int i = 3; i < input.Length; i += 4)
                        Equal(input[i], output[i], "Exact alpha preservation");
                    return output;
                }
                Gradient();
                Check(input.SequenceEqual(Render()), "No effect is exact identity");
                camera.AddEffect(CameraEffectType.ChromaticAberration);
                var settings = camera.Effects[0].ChromaticAberration;
                settings.RedOffset = settings.GreenOffset = settings.BlueOffset = 0f;
                Check(input.SequenceEqual(Render()), "Zero offsets and blur are exact identity");
                settings.DistortRgb = false;
                for (int channel = 0; channel < 3; channel++)
                {
                    string key = ChromaticAberrationSettings.Controls[channel + 2].Key;
                    settings.SetValue(key, 10f);
                    byte[] shifted = Render();
                    int i = Pixel(40, 40);
                    for (int c = 0; c < 3; c++)
                        Check(Math.Abs(shifted[i + c] - input[i + c] - (c == channel ? 20 : 0)) <= 1,
                            "Independent positive offset channel " + channel);
                    settings.SetValue(key, -10f);
                    Check(Math.Abs(Render()[i + channel] - input[i + channel] + 20) <= 1, "Negative offset");
                    settings.SetValue(key, 0f);
                }
                settings.RedOffset = 50f;
                byte[] horizontal = Render();
                settings.DistortRgb = true;
                byte[] radial = Render();
                Check(Math.Abs(radial[Pixel(50, 40)] - input[Pixel(50, 40)]) <= 1, "Radial center stays anchored");
                Check(radial[Pixel(10, 40)] < input[Pixel(10, 40)] - 5, "Radial left scales outward");
                Check(radial[Pixel(80, 40)] > input[Pixel(80, 40)] + 20, "Radial right scales outward");
                Check(!horizontal.SequenceEqual(radial), "Distort RGB changes sampling geometry");
                settings.RedOffset = 100f;
                settings.DistortRgb = false;
                Equal(input[Pixel(99, 40)], Render()[Pixel(0, 40)], "Offsets clamp to image edge without wrapping");
                settings.RedOffset = 0f;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int i = Pixel(x, y);
                        input[i] = input[i + 1] = input[i + 2] = (byte)(x < 50 ? 0 : 255);
                        input[i + 3] = 255;
                    }
                settings.BlurAmount = 0f;
                byte[] sharp = Render();
                settings.BlurAmount = 100f;
                byte[] blurred = Render();
                Check(blurred[Pixel(49, 40)] > sharp[Pixel(49, 40)] + 40, "Blur softens dark side of edge");
                Check(blurred[Pixel(50, 40)] < sharp[Pixel(50, 40)] - 40, "Blur softens bright side of edge");
                for (int i = 0; i < input.Length; i += 4)
                {
                    input[i] = input[i + 1] = input[i + 2] = 73;
                    input[i + 3] = (byte)(i / 4 % 256);
                    if (input[i + 3] == 0) input[i] = 255;
                }
                byte[] alphaBlur = Render();
                Check(Math.Abs(alphaBlur[Pixel(50, 40)] - 73) <= 1, "Hidden RGB does not bleed into blur");
                Gradient();
                settings.BlurAmount = 0f;
                settings.DistortRgb = false;
                settings.RedOffset = 5f;
                settings.BlueOffset = -5f;
                camera.AddEffect(CameraEffectType.ChromaticAberration);
                var second = camera.Effects[1].ChromaticAberration;
                second.RedOffset = 5f;
                second.BlueOffset = -5f;
                second.DistortRgb = false;
                byte[] stacked = Render();
                Check(Math.Abs(stacked[Pixel(40, 40)] - input[Pixel(40, 40)] - 20) <= 1, "Repeated effects stack");
                second.DistortRgb = true;
                byte[] ordered = Render();
                camera.Effects.Reverse();
                Check(!ordered.SequenceEqual(Render()), "Stacking preserves list order");
                Check(Render().SequenceEqual(Render()), "Rendering is deterministic");
                var shader = (Shader)typeof(Viewport).GetField("_chromaticAberrationShader", flags)!.GetValue(viewport)!;
                gl.GetProgram(shader.ShaderProgram, GLEnum.LinkStatus, out int linked);
                Equal(1, linked, "Embedded shader compiles and links");

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
                sceneCamera.AddEffect(CameraEffectType.ChromaticAberration);
                var sceneSettings = sceneCamera.Effects[0].ChromaticAberration;
                sceneSettings.DistortRgb = false;
                sceneSettings.BlueOffset = 0f;
                byte[] Scene(bool highQuality, bool apply)
                {
                    preview.RenderScenePublic(viewport.Camera, sceneCamera, width, height,
                        highQuality: highQuality, applyCameraEffects: apply);
                    return Read(preview.ColorTexture);
                }
                foreach (bool highQuality in new[] { false, true })
                {
                    sceneSettings.RedOffset = 0f;
                    byte[] baseline = Scene(highQuality, true);
                    sceneSettings.RedOffset = 15f;
                    byte[] effected = Scene(highQuality, true);
                    Check(!baseline.SequenceEqual(effected), "Effect wired to fast/high-quality preview");
                    for (int i = 3; i < baseline.Length; i += 4)
                        Equal(baseline[i], effected[i], "Preview alpha unchanged");
                    string path = fixture.NewPath(".png");
                    new MineImatorSimplyRemade.core.render.RenderExporter(preview)
                        .ExportImageFromRgb((int)width, (int)height, "png", path, effected, rgba: true);
                    using var stream = System.IO.File.OpenRead(path);
                    var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                    Equal((int)width, image.Width, "Export width");
                    Equal((int)height, image.Height, "Export height");
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            for (int c = 0; c < 4; c++)
                                Equal(effected[Pixel(x, y) + c], image.Data[Pixel(x, y) + c],
                                    "Export pixels and alpha match preview");
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
