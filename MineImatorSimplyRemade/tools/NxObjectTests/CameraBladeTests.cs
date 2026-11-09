using System.Reflection;
using System.Text.Json;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static unsafe void CameraBladeGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Camera blades regression", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            var viewport = new Viewport { Gl = gl };
            const uint size = 512;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            viewport.InitFramebuffer(size, size);
            try
            {
                uint Field(string name) => (uint)typeof(Viewport).GetField(name, flags)!.GetValue(viewport)!;
                uint scene = Field("_colorTex");
                uint depth = Field("_depthTex");
                var shader = (MineImatorSimplyRemade.core.mdl.Shader)typeof(Viewport).GetField("_depthOfFieldShader", flags)!.GetValue(viewport)!;
                gl.GetProgram(shader.ShaderProgram, GLEnum.LinkStatus, out int linked);
                Equal(1, linked, "Depth-of-field shader compiles and links");
                Check(shader.GetUniformLocation("uBlades") >= 0, "Blade uniform is active");
                var pass = typeof(Viewport).GetMethod("ApplyDepthOfFieldPass", flags)!;
                var camera = new CameraSceneObject();
                camera.AddEffect(CameraEffectType.DepthOfField);
                var settings = camera.Effects[0].DepthOfField;
                settings.Depth = 0f;
                settings.FocusRange = 0f;
                settings.FadeSize = 1f;
                settings.BlurSize = 100f;
                settings.HighlightStrength = 0f;
                byte[] input = new byte[size * size * 4];
                for (int i = 3; i < input.Length; i += 4) input[i] = 255;
                for (int y = 254; y <= 258; y++)
                for (int x = 254; x <= 258; x++)
                {
                    int index = (y * (int)size + x) * 4;
                    input[index] = input[index + 1] = input[index + 2] = 255;
                }
                float[] depths = Enumerable.Repeat(0.99f, (int)(size * size)).ToArray();
                gl.BindTexture(GLEnum.Texture2D, depth);
                fixed (float* data = depths)
                    gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, size, size, PixelFormat.DepthComponent, PixelType.Float, data);
                byte[] Render(int count, float angle = 0f, float stretch = 0f)
                {
                    camera.BladeCount = count;
                    camera.BladeAngle = angle;
                    camera.BladeStretch = stretch;
                    gl.ActiveTexture(GLEnum.Texture0);
                    gl.BindTexture(GLEnum.Texture2D, scene);
                    fixed (byte* data = input)
                        gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, size, size, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    pass.Invoke(viewport, [Field("_fbo"), scene, depth, Field("_glowFbo"), Field("_glowTex"), size, size, 0.05f, 4000f, camera]);
                    byte[] output = new byte[input.Length];
                    gl.BindTexture(GLEnum.Texture2D, scene);
                    fixed (byte* data = output)
                        gl.GetTexImage(GLEnum.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    Equal(GLEnum.NoError, gl.GetError(), "Aperture rendering has no GL errors");
                    return output;
                }
                long Difference(byte[] a, byte[] b) => a.Zip(b, (x, y) => (long)Math.Abs(x - y)).Sum();
                double Aspect(byte[] pixels)
                {
                    double horizontal = 0, vertical = 0;
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int brightness = pixels[(y * (int)size + x) * 4];
                        horizontal += brightness * Math.Pow(x - 256, 2);
                        vertical += brightness * Math.Pow(y - 256, 2);
                    }
                    Check(vertical > 0 && horizontal > 0, "Visible aperture footprint");
                    return horizontal / vertical;
                }
                byte[] circle = Render(0);
                Check(Difference(input, circle) > 1000, "Depth of field blurs the bright point");
                Equal(0L, Difference(circle, Render(0, 47f)), "Circle is unaffected by blade angle");
                Equal(0L, Difference(circle, Render(1)), "One blade uses circular fallback");
                Equal(0L, Difference(circle, Render(2)), "Two blades use circular fallback");
                byte[] square = Render(4);
                Check(Difference(circle, square) > 1000, "Four blades change the rendered aperture");
                Check(Difference(square, Render(4, 45f)) > 1000, "Blade angle rotates polygonal bokeh");
                Check(Aspect(Render(0, stretch: 100f)) > 2.5, "Positive stretch widens horizontal footprint");
                Check(Aspect(Render(0, stretch: -100f)) < 0.4, "Negative stretch widens vertical footprint");
                for (int count = 3; count <= 16; count++) Render(count);
                Equal(0L, Difference(square, Render(4)), "Seeking back produces identical pixels");
                settings.BlurSize = 0f;
                Equal(0L, Difference(input, Render(16, 45f, 100f)), "Disabled blur preserves scene pixels");
            }
            finally
            {
                viewport.DisposeFramebufferResources();
            }
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    private static void CameraBladePersistence(Fixture fixture)
    {
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectSceneObjectEntry)!;
        Equal(0, legacy.CameraBladeCount, "Legacy blade count defaults to zero");
        Near(0f, legacy.CameraBladeAngle, "Legacy blade angle defaults to zero");
        Near(0f, legacy.CameraBladeStretch, "Legacy blade stretch defaults to zero");
        var defaults = new CameraSceneObject();
        Equal(0, defaults.BladeCount, "New camera blade count");
        Near(0f, defaults.BladeAngle, "New camera blade angle");
        Near(0f, defaults.BladeStretch, "New camera blade stretch");
        var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (int count in Enumerable.Range(0, 17))
        foreach (float stretch in new[] { -100f, 0f, 100f })
        {
            var camera = new CameraSceneObject { BladeCount = count, BladeAngle = -37.5f, BladeStretch = stretch };
            var (archive, _) = fixture.Roundtrip(camera);
            var clone = (ProjectSceneObjectEntry)typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [archive.Root])!;
            var loaded = new CameraSceneObject();
            apply.Invoke(null, [loaded, clone, null]);
            var duplicate = (CameraSceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(new SceneTree(), [camera, new Dictionary<string, string>()])!;
            foreach (var copy in new[] { loaded, duplicate })
            {
                Equal(count, copy.BladeCount, "Every blade count persists through archive/library/duplicate");
                Near(-37.5f, copy.BladeAngle, "Blade angle persists");
                Near(stretch, copy.BladeStretch, "Both stretch endpoints persist");
            }
        }
        var authored = new CameraSceneObject { BladeCount = 6, BladeAngle = 45f, BladeStretch = 25f };
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(authored);
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { authored }])!;
        ulong original = Hash();
        foreach (Action change in new Action[]
        {
            () => authored.BladeCount++, () => authored.BladeAngle++, () => authored.BladeStretch++
        })
        {
            change();
            Check(Hash() != original, "Every blade property participates in dirty detection");
            apply.Invoke(null, [authored, snapshot, null]);
            Equal(original, Hash(), "Undo restores blade properties");
        }
        snapshot.CameraBladeCount = 99;
        snapshot.CameraBladeStretch = -200f;
        apply.Invoke(null, [authored, snapshot, null]);
        Equal(16, authored.BladeCount, "Loaded blade count clamps");
        Near(-100f, authored.BladeStretch, "Loaded blade stretch clamps");
    }

    private static void CameraBladeTimeline(Fixture fixture)
    {
        var camera = new CameraSceneObject();
        var timeline = new Timeline();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var get = typeof(Timeline).GetMethod("GetPropertyValue", flags)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", flags)!;
        var discover = typeof(Timeline).GetMethod("GetKeyframePropertyPaths", BindingFlags.Static | BindingFlags.NonPublic)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", flags)!;
        var paths = ((IEnumerable<string>)discover.Invoke(null, [camera])!).ToArray();
        foreach (var (channel, end) in new (string, float)[]
        {
            ("blade_count", 16f), ("blade_angle", 90f), ("blade_stretch", -100f)
        })
        {
            string path = "camera." + channel;
            Check(paths.Contains(path), "Timeline discovers " + path);
            var keys = new List<TimelineKeyframe>
            {
                new() { Frame = 0, Value = 0f }, new() { Frame = 10, Value = end }
            };
            foreach (int frame in new[] { 0, 5, 10, 5, 0 })
            {
                float value = Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, frame, false]));
                set.Invoke(timeline, [camera, path, value]);
                Near(end * frame / 10f, Convert.ToSingle(get.Invoke(timeline, [camera, path])),
                    "Interpolation and reverse seeking " + path);
            }
        }
        foreach (float input in new[] { -10f, 0f, 2.6f, 16f, 99f })
        {
            set.Invoke(timeline, [camera, "camera.blade_count", input]);
            Equal((int)MathF.Round(Math.Clamp(input, 0f, 16f)), camera.BladeCount, "Count is an integer in 0-16");
        }
        foreach (float input in new[] { -200f, -100f, 0f, 100f, 200f })
        {
            set.Invoke(timeline, [camera, "camera.blade_stretch", input]);
            Near(Math.Clamp(input, -100f, 100f), camera.BladeStretch, "Stretch stays in -100 to 100");
        }
    }

    private static unsafe void CameraBladeControls(Fixture fixture)
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
            var second = new CameraSceneObject();
            var panel = new PropertiesPanel();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PropertiesPanel).GetField("_currentObject", flags)!.SetValue(panel, camera);
            var draw = typeof(PropertiesPanel).GetMethod("DrawCameraBladeControls", flags)!;
            SelectionManager.Instance.SelectObject(camera);
            SelectionManager.Instance.SelectObject(second);
            System.Numerics.Vector2 input = default;
            void Frame(bool down = false, float offset = 0f)
            {
                io.AddMousePosEvent(input.X + offset, input.Y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600), ImGuiCond.Always);
                ImGui.Begin("Camera blades controls");
                float start = ImGui.GetCursorPosY();
                draw.Invoke(panel, [camera]);
                input = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2f;
                Check(ImGui.GetCursorPosY() - start > ImGui.GetFontSize() * 3f, "All three settings are visible at zero blades");
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            Frame();
            Frame(true);
            Frame(true, 30f);
            Frame(false);
            Check(camera.BladeStretch > 0f, "Stretch drag edits camera");
            Near(camera.BladeStretch, second.BladeStretch, "Stretch edits selected cameras together");
        }
        finally
        {
            SelectionManager.Instance.ClearSelection();
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }
}
