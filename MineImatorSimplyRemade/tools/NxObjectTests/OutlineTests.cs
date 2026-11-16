using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;
using StbImageSharp;

internal static partial class Program
{
    private static void OutlinePersistence(Fixture fixture)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var camera = new CameraSceneObject();
        camera.AddEffect(CameraEffectType.Outline);
        foreach (var control in OutlineSettings.Controls)
            camera.Effects[0].Outline.SetValue(control.Key, control.Max);
        var settings = camera.Effects[0].Outline;
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(camera);
        var saved = JsonSerializer.Deserialize(JsonSerializer.Serialize(snapshot, AppJsonContext.Default.ProjectSceneObjectEntry),
            AppJsonContext.Default.ProjectSceneObjectEntry)!;
        var (archive, _) = fixture.Roundtrip(camera);
        var library = (ProjectSceneObjectEntry)typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", statics)!
            .Invoke(null, [saved])!;
        var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", statics)!;
        var loaded = new CameraSceneObject();
        apply.Invoke(null, [loaded, saved, null]);
        var duplicate = (CameraSceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate", instance)!
            .Invoke(new SceneTree(), [camera, new Dictionary<string, string>()])!;
        foreach (var copy in new[] { saved.CameraEffects[0].Outline, archive.Root.CameraEffects[0].Outline,
                     library.CameraEffects[0].Outline, loaded.Effects[0].Outline,
                     duplicate.Effects[0].Outline, CameraSceneObject.CloneEffect(camera.Effects[0]).Outline })
        {
            Check(!ReferenceEquals(settings, copy), "Outline copies are independent");
            foreach (var control in OutlineSettings.Controls)
                Near(settings.GetValue(control.Key), copy.GetValue(control.Key), "Persists " + control.Key);
        }
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { camera }])!;
        ulong original = Hash();
        foreach (var control in OutlineSettings.Controls)
        {
            camera.Effects[0].Outline.SetValue(control.Key, control.Min);
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
        foreach (var control in OutlineSettings.Controls)
        {
            string path = "camera.effect.0.outline." + control.Key;
            Check(paths.Contains(path), "Timeline discovers " + path);
            var keys = new List<TimelineKeyframe>
            {
                new() { Frame = 0, Value = control.Min }, new() { Frame = 10, Value = control.Max }
            };
            foreach (int frame in new[] { 0, 5, 9, 10, 5, 0 })
            {
                float value = Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, frame]));
                set.Invoke(timeline, [camera, path, value]);
                float expected = control.Key is "normal_pass" or "mode"
                    ? (frame == 10 ? control.Max : control.Min)
                    : control.Min + (control.Max - control.Min) * frame / 10f;
                Near(expected, Convert.ToSingle(get.Invoke(timeline, [camera, path])), "Interpolation/seek " + path);
            }
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                bool rejected = false;
                try { settings.SetValue(control.Key, invalid); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "Rejects non-finite " + control.Key);
            }
        }
    }

    private static unsafe void OutlineControls(Fixture fixture)
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
            camera.AddEffect(CameraEffectType.Outline);
            var panel = new PropertiesPanel();
            var draw = typeof(PropertiesPanel).GetMethod("DrawOutlineControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
            System.Numerics.Vector2 input = default;
            float Frame(bool down = false)
            {
                io.AddMousePosEvent(input.X, input.Y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600), ImGuiCond.Always);
                ImGui.Begin("Outline controls");
                var start = ImGui.GetCursorScreenPos();
                draw.Invoke(panel, [camera, 0]);
                float rows = (ImGui.GetCursorScreenPos().Y - start.Y) / ImGui.GetFrameHeightWithSpacing();
                input = start + new System.Numerics.Vector2(8,
                    5 * ImGui.GetFrameHeightWithSpacing() + ImGui.GetFrameHeight() / 2f);
                ImGui.End();
                ImGui.Render();
                return rows;
            }
            float hidden = Frame();
            Frame();
            Frame(true);
            float shown = Frame(false);
            Check(camera.Effects[0].Outline.NormalPass, "Normal-pass checkbox enables mapped-normal detection");
            Near(2f, shown - hidden, "Only enabled normal pass exposes threshold and fade");
            Frame(true);
            Frame(false);
            Check(!camera.Effects[0].Outline.NormalPass, "Normal-pass checkbox disables mapped-normal detection");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void OutlineGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Outline regression", null, null);
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
            const uint width = 64, height = 48;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var viewport = new Viewport { Gl = gl, OverlaysEnabled = false };
            viewport.InitFramebuffer(width, height);
            var preview = new Viewport { Gl = gl, IsPreviewViewport = true, MainViewport = viewport, OverlaysEnabled = false };
            preview.InitPreviewViewport(width, height);
            try
            {
                uint Field(string name) => (uint)typeof(Viewport).GetField(name, flags)!.GetValue(viewport)!;
                var pass = typeof(Viewport).GetMethod("ApplyOutlinePass", flags)!;
                var camera = new CameraSceneObject();
                camera.AddEffect(CameraEffectType.Outline);
                var settings = camera.Effects[0].Outline;
                byte[] input = new byte[width * height * 4];
                float[] depths = new float[width * height];
                float[] normals = new float[input.Length];
                float nearPlane = 0.1f, farPlane = 100f;
                int Pixel(int x, int y = 24) => (y * (int)width + x) * 4;
                float Raw(float z) => (farPlane - nearPlane * farPlane / z) / (farPlane - nearPlane);
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = Pixel(x, y);
                    input[i] = 64;
                    input[i + 1] = 128;
                    input[i + 2] = 192;
                    input[i + 3] = (byte)((x + y) % 256);
                    depths[i / 4] = Raw(x < 32 ? 2f : 4f);
                    normals[i] = normals[i + 1] = 0.5f;
                    normals[i + 2] = normals[i + 3] = 1f;
                }
                byte[] Read(uint texture)
                {
                    var output = new byte[input.Length];
                    gl.ActiveTexture(GLEnum.Texture0);
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
                    gl.BindTexture(GLEnum.Texture2D, Field("_depthTex"));
                    fixed (float* data = depths)
                        gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, width, height, PixelFormat.DepthComponent, PixelType.Float, data);
                    gl.BindTexture(GLEnum.Texture2D, Field("_outlineNormalTex"));
                    fixed (float* data = normals)
                        gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.Float, data);
                    gl.Enable(GLEnum.DepthTest);
                    gl.Enable(GLEnum.CullFace);
                    gl.Enable(GLEnum.Blend);
                    pass.Invoke(viewport, [Field("_fbo"), Field("_depthTex"), Field("_outlineNormalTex"),
                        Field("_glowFbo"), Field("_glowTex"), width, height, nearPlane, farPlane, camera]);
                    Check(gl.IsEnabled(GLEnum.DepthTest) && gl.IsEnabled(GLEnum.CullFace) && gl.IsEnabled(GLEnum.Blend),
                        "Outline restores GL enable state");
                    Equal(GLEnum.FramebufferComplete, gl.CheckFramebufferStatus(GLEnum.Framebuffer), "Attachments restored");
                    byte[] output = Read(Field("_colorTex"));
                    Equal(GLEnum.NoError, gl.GetError(), "Outline has no GL errors");
                    for (int i = 3; i < output.Length; i += 4)
                        Equal(input[i], output[i], "Outline preserves alpha");
                    return output;
                }
                settings.Radius = 0f;
                Check(input.SequenceEqual(Render()), "Zero radius is identity");
                settings.Radius = 1f;
                settings.Strength = 0f;
                Check(input.SequenceEqual(Render()), "Zero strength is identity");
                settings.Strength = 100f;
                nearPlane = 0.05f;
                farPlane = 4000f;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    depths[Pixel(x, y) / 4] = Math.Min(1f, 1f - (y - 7f - x * 0.1f) * 0.00005f);
                foreach (float radius in new[] { 1f, 3f, 8f })
                {
                    settings.Radius = radius;
                    byte[] terrain = Render();
                    for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        if (Math.Abs(y - 7f - x * 0.1f) <= radius * 1.1f + 2f)
                            continue;
                        for (int channel = 0; channel < 3; channel++)
                            Equal(input[Pixel(x, y) + channel], terrain[Pixel(x, y) + channel],
                                "Continuous grazing terrain has no thick horizon band or image-border outline");
                    }
                }
                settings.Radius = 1f;
                for (int y = 26; y <= 34; y++)
                for (int x = 28; x <= 36; x++)
                    depths[Pixel(x, y) / 4] = Raw(2f);
                for (int y = 17; y <= 35; y++)
                    depths[Pixel(20, y) / 4] = Raw(2f);
                byte[] foreground = Render();
                Equal((byte)0, foreground[Pixel(28, 30)], "Foreground silhouette against sloping terrain remains outlined");
                Equal(input[Pixel(32, 30)], foreground[Pixel(32, 30)], "Flat foreground interior remains clean");
                Equal((byte)0, foreground[Pixel(20)], "Slope rejection preserves single-pixel foreground geometry");
                nearPlane = 0.1f;
                farPlane = 100f;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    depths[Pixel(x, y) / 4] = Raw(x < 32 ? 2f : 4f);
                byte[] edge = Render();
                Equal((byte)0, edge[Pixel(31)], "Depth discontinuity has outline color");
                Equal(input[Pixel(30)], edge[Pixel(30)], "One-pixel radius is exact");
                settings.Radius = 3f;
                Equal((byte)0, Render()[Pixel(29)], "Radius expands to three pixels");
                settings.Radius = 1f;
                settings.DepthThreshold = 1f;
                Check(input.SequenceEqual(Render()), "Depth threshold suppresses edge below threshold");
                settings.DepthThreshold = 0.5f;
                settings.DepthFade = 1f;
                byte[] faded = Render();
                Check(faded[Pixel(31)] > 10 && faded[Pixel(31)] < 54, "Depth fade softens threshold");
                settings.DepthThreshold = 0.02f;
                settings.DepthFade = 0f;
                settings.ColorR = 0.8f;
                settings.ColorG = 0.3f;
                settings.ColorB = 0.6f;
                foreach (var mode in Enum.GetValues<BloomBlendMode>())
                {
                    settings.BlendMode = mode;
                    settings.Strength = 40f;
                    var output = Render();
                    float[] colors = [settings.ColorR, settings.ColorG, settings.ColorB];
                    for (int channel = 0; channel < 3; channel++)
                    {
                        float b = input[Pixel(31) + channel] / 255f, c = colors[channel];
                        float blended = mode switch
                        {
                            BloomBlendMode.Normal => c,
                            BloomBlendMode.Add => b + c,
                            BloomBlendMode.Screen => 1f - (1f - b) * (1f - c),
                            BloomBlendMode.Multiply => b * c,
                            _ => b < 0.5f ? 2f * b * c : 1f - 2f * (1f - b) * (1f - c)
                        };
                        Check(Math.Abs(output[Pixel(31) + channel] - Math.Clamp((b * 0.6f + blended * 0.4f) * 255f, 0, 255)) <= 1,
                            "Exact strength/color blend for " + mode);
                    }
                }
                settings.BlendMode = BloomBlendMode.Normal;
                settings.ColorR = settings.ColorG = settings.ColorB = 0f;
                settings.Strength = 100f;
                Array.Fill(depths, Raw(2f));
                for (int y = 0; y < height; y++)
                for (int x = 32; x < width; x++)
                {
                    normals[Pixel(x, y)] = 1f;
                    normals[Pixel(x, y) + 2] = 0.5f;
                }
                Check(input.SequenceEqual(Render()), "Depth-only ignores normal discontinuity");
                settings.NormalPass = true;
                Equal((byte)0, Render()[Pixel(31)], "Normal pass detects surface changes without a depth edge");
                settings.NormalThreshold = 1f;
                Check(input.SequenceEqual(Render()), "Normal threshold suppresses normal edge");
                settings.NormalThreshold = 0.25f;
                settings.NormalFade = 0.5f;
                faded = Render();
                Check(faded[Pixel(31)] > 10 && faded[Pixel(31)] < 54, "Normal fade softens threshold");
                Check(Render().SequenceEqual(Render()), "Repeated evaluation is deterministic");

                viewport.PropertiesPanel = new PropertiesPanel
                {
                    TransparentBackground = true, ShadowsEnabled = false, FogEnabled = false,
                    ColorManagementEnabled = false, AntiAliasingEnabled = false
                };
                using var cube = new CubeMesh(gl);
                var obj = new SceneObject();
                obj.Visuals.Add(cube);
                viewport.SceneObjects.Add(obj);
                var sceneCamera = new CameraSceneObject();
                sceneCamera.AddEffect(CameraEffectType.Outline);
                byte[] Scene(bool highQuality)
                {
                    preview.RenderScenePublic(viewport.Camera, sceneCamera, width, height,
                        highQuality: highQuality, applyCameraEffects: true);
                    return Read(preview.ColorTexture);
                }
                foreach (bool highQuality in new[] { false, true })
                {
                    sceneCamera.Effects[0].Outline.Strength = 0f;
                    byte[] baseline = Scene(highQuality);
                    sceneCamera.Effects[0].Outline.Strength = 100f;
                    sceneCamera.Effects[0].Outline.ColorR = 1f;
                    sceneCamera.Effects[0].Outline.Radius = 2f;
                    byte[] outlined = Scene(highQuality);
                    Check(!baseline.SequenceEqual(outlined), "Outline is wired into fast/high-quality previews");
                    for (int i = 3; i < baseline.Length; i += 4)
                        Equal(baseline[i], outlined[i], "Preview preserves transparent background alpha");
                    string path = fixture.NewPath(".png");
                    new MineImatorSimplyRemade.core.render.RenderExporter(preview)
                        .ExportImageFromRgb((int)width, (int)height, "png", path, outlined, rgba: true);
                    using var stream = File.OpenRead(path);
                    Check(outlined.SequenceEqual(ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha).Data),
                        "PNG export preserves outline pixels and alpha");
                }
                MineImatorLoader.Instance.Initialize(gl);
                string normalMap = fixture.NewPath(".png");
                byte[] normalPixels = new byte[64 * 64 * 4];
                for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int i = (y * 64 + x) * 4;
                    normalPixels[i] = x < 32 ? (byte)128 : (byte)255;
                    normalPixels[i + 1] = 128;
                    normalPixels[i + 2] = x < 32 ? (byte)255 : (byte)128;
                    normalPixels[i + 3] = 255;
                }
                typeof(Viewport).GetMethod("WritePng", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [normalMap, normalPixels, 64, 64, false, true]);
                using var plane = new PlaneMesh(gl, 4f, 3f, PlaneOrientation.XY);
                viewport.SceneObjects.Clear();
                var planeObject = new SceneObject { NormalTexturePath = normalMap,
                    MaterialSettings = new MaterialSettings { NormalEnabled = true } };
                planeObject.Visuals.Add(plane);
                viewport.SceneObjects.Add(planeObject);
                viewport.Camera.Yaw = viewport.Camera.Pitch = 0f;
                var outline = sceneCamera.Effects[0].Outline;
                outline.Radius = 1f;
                outline.NormalThreshold = 0.1f;
                outline.NormalFade = 0f;
                foreach (bool pbr in new[] { false, true })
                {
                    planeObject.MaterialSettings.PbrEnabled = pbr;
                    foreach (bool highQuality in new[] { false, true })
                    {
                        outline.NormalPass = false;
                        byte[] depthOnly = Scene(highQuality);
                        outline.NormalPass = true;
                        byte[] mapped = Scene(highQuality);
                        int changed = 0;
                        for (int y = 16; y < 32; y++)
                        for (int x = 28; x < 36; x++)
                            if (mapped[Pixel(x, y)] != depthOnly[Pixel(x, y)]) changed++;
                        Check(changed >= 8, $"Normal-map seam produces interior outlines (PBR={pbr}, HQ={highQuality}, changed={changed})");
                        planeObject.MaterialSettings.NormalEnabled = false;
                        outline.NormalPass = false;
                        byte[] flatDepth = Scene(highQuality);
                        outline.NormalPass = true;
                        Check(flatDepth.SequenceEqual(Scene(highQuality)), "Disabled material normal map adds no planar interior edges");
                        planeObject.MaterialSettings.NormalEnabled = true;
                    }
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
            ImGui.SetCurrentContext(previous);
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
