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
    private static void PixelatePersistence(Fixture fixture)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectCameraEffectEntry)!;
        Near(8f, legacy.Pixelate.Size, "Older entries get valid pixelate defaults");
        var camera = new CameraSceneObject();
        camera.AddEffect(CameraEffectType.Pixelate);
        var settings = camera.Effects[0].Pixelate;
        settings.Size = 17f;
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
        foreach (var copy in new[] { saved.CameraEffects[0].Pixelate, archive.Root.CameraEffects[0].Pixelate,
                     library.CameraEffects[0].Pixelate, loaded.Effects[0].Pixelate,
                     duplicate.Effects[0].Pixelate, CameraSceneObject.CloneEffect(camera.Effects[0]).Pixelate })
        {
            Near(17f, copy.Size, "Size survives serialization and copying");
            Check(!ReferenceEquals(settings, copy), "Copies own independent settings");
            copy.Size = 31f;
            Near(17f, settings.Size, "Copy edits leave original unchanged");
        }
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { camera }])!;
        ulong original = Hash();
        settings.Size = 32f;
        Check(Hash() != original, "Size edit changes dirty fingerprint");
        apply.Invoke(null, [camera, snapshot, null]);
        Equal(original, Hash(), "Undo restores pixelate state");

        const string path = "camera.effect.0.pixelate.size";
        var paths = (IEnumerable<string>)typeof(Timeline).GetMethod("GetKeyframePropertyPaths", statics)!
            .Invoke(null, [camera])!;
        Check(paths.Contains(path), "Timeline discovers size");
        var timeline = new Timeline();
        var get = typeof(Timeline).GetMethod("GetPropertyValue", instance)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", instance)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", instance)!;
        var keys = new List<TimelineKeyframe>
        {
            new() { Frame = 0, Value = 1f }, new() { Frame = 10, Value = 21f }
        };
        foreach (int frame in new[] { 0, 5, 10, 5, 0 })
        {
            float value = Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, frame]));
            set.Invoke(timeline, [camera, path, value]);
            Near(1f + frame * 2f, Convert.ToSingle(get.Invoke(timeline, [camera, path])), "Size animates and reverse seeks");
        }
        settings = camera.Effects[0].Pixelate;
        settings.SetValue("size", -1f);
        Near(1f, settings.Size, "Size lower limit");
        settings.SetValue("size", 1000f);
        Near(256f, settings.Size, "Size upper limit");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool rejected = false;
            try { settings.SetValue("size", invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Nonfinite sizes rejected");
        }
    }

    private static unsafe void PixelateControls(Fixture fixture)
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
            camera.AddEffect(CameraEffectType.Pixelate);
            var panel = new PropertiesPanel();
            var draw = typeof(PropertiesPanel).GetMethod("DrawPixelateControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
            System.Numerics.Vector2 input = default;
            void Frame(bool down = false)
            {
                io.AddMousePosEvent(input.X, input.Y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 300), ImGuiCond.Always);
                ImGui.Begin("Pixelate controls");
                var start = ImGui.GetCursorScreenPos();
                draw.Invoke(panel, [camera, 0]);
                Check(ImGui.GetCursorScreenPos().Y > start.Y, "Pixel-size slider is visible");
                input = start + new System.Numerics.Vector2(200f, ImGui.GetFrameHeight() / 2f);
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            Frame();
            Frame(true);
            Frame(false);
            Check(camera.Effects[0].Pixelate.Size > 8f, "Dragging slider edits pixel size");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void PixelateGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Pixelate regression", null, null);
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
                var pass = typeof(Viewport).GetMethod("ApplyPixelatePass", flags)!;
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
                byte[] Read(uint texture)
                {
                    byte[] output = new byte[input.Length];
                    gl.BindTexture(GLEnum.Texture2D, texture);
                    fixed (byte* data = output)
                        gl.GetTexImage(GLEnum.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    return output;
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
                        "GL enable state preserved");
                    return Read(Field("_colorTex"));
                }
                Check(input.SequenceEqual(Render()), "No effect leaves exact original pixels");
                camera.AddEffect(CameraEffectType.Pixelate);
                foreach (float size in new[] { 1f, 2f, 7f, 7.49f, 7.5f, 256f })
                {
                    camera.Effects[0].Pixelate.Size = size;
                    Check(Expected(input, (int)MathF.Floor(size + 0.5f)).SequenceEqual(Render()),
                        $"Exact {size}px blocks, center samples, alpha and partial edge blocks");
                }
                camera.Effects[0].Pixelate.Size = 7f;
                camera.AddEffect(CameraEffectType.Pixelate);
                camera.Effects[1].Pixelate.Size = 11f;
                Check(Expected(Expected(input, 7), 11).SequenceEqual(Render()), "Repeated pixelate effects stack in order");
                Check(Render().SequenceEqual(Render()), "Deterministic output");
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
                sceneCamera.AddEffect(CameraEffectType.Pixelate);
                sceneCamera.ViewCamera.Target = viewport.Camera.Target;
                sceneCamera.ViewCamera.Yaw = viewport.Camera.Yaw;
                sceneCamera.ViewCamera.Pitch = viewport.Camera.Pitch;
                sceneCamera.ViewCamera.Distance = viewport.Camera.Distance;
                sceneCamera.SyncTransformFromCamera();
                viewport.SceneObjects.Add(sceneCamera);
                foreach (bool highQuality in new[] { false, true })
                {
                    byte[] Scene(float size)
                    {
                        sceneCamera.Effects[0].Pixelate.Size = size;
                        preview.RenderScenePublic(sceneCamera.ViewCamera, sceneCamera, width, height,
                            highQuality: highQuality, applyCameraEffects: true);
                        return Read(preview.ColorTexture);
                    }
                    byte[] baseline = Scene(1f);
                    byte[] effected = Scene(7f);
                    Check(!baseline.SequenceEqual(effected), "Pixelate changes actual scene pixels");
                    Check(Expected(baseline, 7).SequenceEqual(effected), "Fast/high-quality preview matches exact pixel blocks");
                    string path = fixture.NewPath(".png");
                    preview.ToggleHighQualityPreview();
                    sceneCamera.Effects[0].Pixelate.Size = 1f;
                    Check(preview.CaptureCurrentView(width, height, highQuality, out _, rgba: true),
                        "Baseline export capture succeeds");
                    byte[] exportBaseline = Read(preview.ColorTexture);
                    sceneCamera.Effects[0].Pixelate.Size = 7f;
                    new RenderExporter(preview).ExportImage((int)width, (int)height, "png", path, highQuality);
                    preview.ToggleHighQualityPreview();
                    byte[] captured = Read(preview.ColorTexture);
                    Check(!exportBaseline.SequenceEqual(captured), "Pixelate changes actual export pixels");
                    Check(Expected(exportBaseline, 7).SequenceEqual(captured), "Real export capture includes exact pixel blocks");
                    using var stream = System.IO.File.OpenRead(path);
                    var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                    Equal((int)width, image.Width, "PNG width");
                    Equal((int)height, image.Height, "PNG height");
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            for (int c = 0; c < 4; c++)
                                Equal(captured[Pixel(x, (int)height - y - 1) + c], image.Data[Pixel(x, y) + c],
                                    "Exported PNG matches captured pixelated RGB and alpha");
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
        }
    }
}
