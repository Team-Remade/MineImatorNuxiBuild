using System.Reflection;
using System.Text.Json;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static void LensDirtPersistence(Fixture fixture)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectCameraEffectEntry)!;
        Check(legacy.LensDirt.AffectedByBloom && legacy.LensDirt.AffectedByGlow, "Lens dirt source defaults");
        Equal("", legacy.LensDirt.TexturePath, "No implicit dirt texture");
        var camera = new CameraSceneObject();
        camera.AddEffect(CameraEffectType.LensDirt);
        var dirt = camera.Effects[0].LensDirt;
        dirt.TexturePath = fixture.File("textures\\dirt.png", "lens dirt image bytes");
        dirt.AffectedByGlow = false;
        dirt.Radius = 31f;
        dirt.Intensity = 245f;
        dirt.Power = 175f;
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(camera);
        string json = JsonSerializer.Serialize(snapshot, AppJsonContext.Default.ProjectSceneObjectEntry);
        var saved = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectSceneObjectEntry)!;
        Near(245f, saved.CameraEffects[0].LensDirt.Intensity, "Project JSON persists intensity");
        var (archive, directory) = fixture.Roundtrip(camera);
        string packedPath = archive.Root.CameraEffects[0].LensDirt.TexturePath;
        Check(System.IO.File.Exists(Path.Combine(directory, packedPath)), "nxObj embeds lens dirt texture");
        Equal("lens dirt image bytes", System.IO.File.ReadAllText(Path.Combine(directory, packedPath)), "Exact embedded texture bytes");
        var library = (ProjectSceneObjectEntry)typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", statics)!
            .Invoke(null, [saved])!;
        library.CameraEffects[0].LensDirt.Power++;
        Near(175f, saved.CameraEffects[0].LensDirt.Power, "Library clone owns its settings");
        var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", statics)!;
        var loaded = new CameraSceneObject();
        apply.Invoke(null, [loaded, saved, null]);
        var duplicate = (CameraSceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate", instance)!
            .Invoke(new SceneTree(), [camera, new Dictionary<string, string>()])!;
        foreach (var copy in new[] { loaded, duplicate })
        {
            Equal(dirt.TexturePath, copy.Effects[0].LensDirt.TexturePath, "Texture path persists");
            Check(copy.Effects[0].LensDirt.AffectedByBloom && !copy.Effects[0].LensDirt.AffectedByGlow, "Source toggles persist");
            Near(31f, copy.Effects[0].LensDirt.Radius, "Radius persists");
            Near(175f, copy.Effects[0].LensDirt.Power, "Power persists");
        }
        duplicate.Effects[0].LensDirt.Radius++;
        Near(31f, dirt.Radius, "Duplicate settings are independent");
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { camera }])!;
        ulong original = Hash();
        foreach (Action change in new Action[]
        {
            () => camera.Effects[0].LensDirt.TexturePath = "",
            () => camera.Effects[0].LensDirt.AffectedByBloom = false,
            () => camera.Effects[0].LensDirt.AffectedByGlow = true,
            () => camera.Effects[0].LensDirt.Radius++,
            () => camera.Effects[0].LensDirt.Intensity++,
            () => camera.Effects[0].LensDirt.Power++
        })
        {
            change();
            Check(Hash() != original, "Every dirt setting participates in dirty detection");
            apply.Invoke(null, [camera, snapshot, null]);
            Equal(original, Hash(), "Undo restores dirt settings");
        }
        var timeline = new Timeline();
        var discover = typeof(Timeline).GetMethod("GetKeyframePropertyPaths", statics)!;
        var get = typeof(Timeline).GetMethod("GetPropertyValue", instance)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", instance)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", instance)!;
        var paths = ((IEnumerable<string>)discover.Invoke(null, [camera])!).ToArray();
        foreach (var control in LensDirtSettings.Controls)
        {
            string path = "camera.effect.0.lens_dirt." + control.Key;
            Check(paths.Contains(path), "Timeline discovers " + path);
            bool discrete = control.Key.StartsWith("affected_by_", StringComparison.Ordinal);
            var keys = new List<TimelineKeyframe>
            {
                new() { Frame = 0, Value = control.Min }, new() { Frame = 10, Value = control.Max }
            };
            foreach (int frame in new[] { 0, 5, 10, 5, 0 })
            {
                float value = Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, frame, false]));
                set.Invoke(timeline, [camera, path, value]);
                float expected = discrete && frame < 10 ? control.Min : control.Min + (control.Max - control.Min) * frame / 10f;
                Near(expected, Convert.ToSingle(get.Invoke(timeline, [camera, path])), "Interpolation/reverse seeking " + path);
            }
        }
        dirt.SetValue("radius", -1f);
        Near(0f, dirt.Radius, "Radius lower bound");
        dirt.SetValue("power", 0f);
        Near(1f, dirt.Power, "Power remains positive so black light stays black");
        bool rejected = false;
        try { dirt.SetValue("intensity", float.NaN); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Reject non-finite lens dirt values");
    }

    private static unsafe void LensDirtControls(Fixture fixture)
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
            camera.AddEffect(CameraEffectType.LensDirt);
            var panel = new PropertiesPanel();
            var draw = typeof(PropertiesPanel).GetMethod("DrawLensDirtControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
            System.Numerics.Vector2 input = default;
            void Frame(bool down = false, float offset = 0f)
            {
                io.AddMousePosEvent(input.X + offset, input.Y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600), ImGuiCond.Always);
                ImGui.Begin("Lens dirt controls");
                float start = ImGui.GetCursorPosY();
                draw.Invoke(panel, [camera, 0]);
                input = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2f;
                Check(ImGui.GetCursorPosY() - start > ImGui.GetFrameHeight() * 7f,
                    "Texture selector/import, two toggles and three percentage controls are visible");
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            Frame();
            Frame(true);
            Frame(true, 30f);
            Frame(false);
            Check(camera.Effects[0].LensDirt.Power > 100f, "Power percentage slider edits the camera");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void LensDirtGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Lens dirt regression", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            MineImatorLoader.Instance.Initialize(gl);
            var viewport = new Viewport { Gl = gl };
            const uint size = 64;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            viewport.InitFramebuffer(size, size);
            try
            {
                uint Field(string name) => (uint)typeof(Viewport).GetField(name, flags)!.GetValue(viewport)!;
                var prepare = typeof(Viewport).GetMethod("PrepareLensDirt", flags)!;
                var lens = typeof(Viewport).GetMethod("ApplyLensDirtPass", flags)!;
                var bloomPass = typeof(Viewport).GetMethod("ApplyBloomPass", flags)!;
                var glowPass = typeof(Viewport).GetMethod("ApplyGlowPassGeneric", flags)!;
                var writePng = typeof(Viewport).GetMethod("WritePng", BindingFlags.Static | BindingFlags.NonPublic)!;
                string texture = fixture.NewPath(".png");
                // Top row white, bottom row black; catches flipped/incorrectly tiled camera textures.
                writePng.Invoke(null, [texture, new byte[] { 255, 255, 255, 255, 0, 0, 0, 255 }, 1, 2, false, true]);
                var camera = new CameraSceneObject();
                camera.AddEffect(CameraEffectType.Bloom);
                camera.Effects[0].Bloom.Threshold = 10f;
                camera.Effects[0].Bloom.Radius = 0f;
                camera.Effects[0].Bloom.Intensity = 10f;
                camera.AddEffect(CameraEffectType.LensDirt);
                var dirt = camera.Effects[1].LensDirt;
                dirt.TexturePath = texture;
                dirt.Radius = 0f;
                var settings = new PropertiesPanel { GlowEnabled = true, GlowStrength = 0.05f, GlowSize = 1f };
                viewport.PropertiesPanel = settings;
                object combined = Enum.Parse(typeof(Viewport).GetNestedType("RenderedPassMode", BindingFlags.NonPublic)!, "Combined");
                byte[] input = new byte[size * size * 4];
                for (int i = 0; i < input.Length; i += 4)
                {
                    input[i] = input[i + 1] = input[i + 2] = 160;
                    input[i + 3] = 255;
                }
                byte[] Render(bool enabled = true)
                {
                    float intensity = dirt.Intensity;
                    if (!enabled) dirt.Intensity = 0f;
                    gl.ActiveTexture(GLEnum.Texture0);
                    gl.BindTexture(GLEnum.Texture2D, Field("_colorTex"));
                    fixed (byte* data = input)
                        gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, size, size, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    prepare.Invoke(viewport, [camera, size, size]);
                    glowPass.Invoke(viewport, [Field("_fbo"), Field("_colorTex"), Field("_glowFbo"), Field("_glowTex"),
                        Field("_glowPingFbo"), Field("_glowPingTex"), size, size, settings, combined, true]);
                    bloomPass.Invoke(viewport, [Field("_fbo"), Field("_colorTex"), Field("_glowFbo"), Field("_glowTex"),
                        Field("_glowPingFbo"), Field("_glowPingTex"), size, size, camera]);
                    lens.Invoke(viewport, [Field("_fbo"), Field("_colorTex"), Field("_glowFbo"), Field("_glowTex"),
                        Field("_glowPingFbo"), Field("_glowPingTex"), size, size, camera]);
                    byte[] output = new byte[input.Length];
                    gl.ActiveTexture(GLEnum.Texture0);
                    gl.BindTexture(GLEnum.Texture2D, Field("_colorTex"));
                    fixed (byte* data = output)
                        gl.GetTexImage(GLEnum.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    Equal(GLEnum.NoError, gl.GetError(), "Lens dirt has no GL errors");
                    dirt.Intensity = intensity;
                    return output;
                }
                int Top(byte[] pixels) => pixels[(48 * (int)size + 32) * 4];
                long Difference(byte[] a, byte[] b) => a.Zip(b, (x, y) => (long)Math.Abs(x - y)).Sum();
                byte[] baseline = Render(false);
                byte[] both = Render();
                Check(Top(both) > Top(baseline) + 10, "Both light sources illuminate the dirt");
                Equal(baseline[(16 * (int)size + 32) * 4], both[(16 * (int)size + 32) * 4], "Black bottom texture stays clean and orientation is correct");
                dirt.AffectedByGlow = false;
                byte[] bloomOnly = Render();
                Check(Top(bloomOnly) > Top(baseline) + 5, "Bloom independently lights dirt");
                dirt.AffectedByBloom = false;
                Equal(0L, Difference(baseline, Render()), "Both toggles off preserve exact pixels");
                dirt.AffectedByGlow = true;
                byte[] glowOnly = Render();
                Check(Top(glowOnly) > Top(baseline) + 3, "Glow independently lights dirt");
                settings.GlowEnabled = false;
                Equal(0L, Difference(Render(false), Render()), "Disabled glow does not leak previous-frame light");
                dirt.AffectedByBloom = true;
                camera.Effects[0].Bloom.Intensity = 0f;
                Equal(0L, Difference(Render(false), Render()), "Disabled bloom contributes no dirt");
                camera.Effects[0].Bloom.Intensity = 10f;
                dirt.AffectedByGlow = false;
                byte[] linear = Render();
                dirt.Power = 200f;
                Check(Top(Render()) < Top(linear), "Power increases light-response contrast");
                dirt.Power = 100f;
                dirt.Intensity = 200f;
                Check(Top(Render()) > Top(linear), "Intensity scales dirt brightness");
                dirt.Intensity = 100f;
                byte[] pointInput = (byte[])input.Clone();
                for (int i = 0; i < input.Length; i += 4) input[i] = input[i + 1] = input[i + 2] = 0;
                for (int y = 44; y <= 52; y++)
                for (int x = 28; x <= 36; x++)
                {
                    int index = (y * (int)size + x) * 4;
                    input[index] = input[index + 1] = input[index + 2] = 160;
                }
                byte[] sharp = Render();
                dirt.Radius = 100f;
                byte[] spread = Render();
                int neighbor = (48 * (int)size + 40) * 4;
                Check(spread[neighbor] > sharp[neighbor], "Radius spreads light beyond the source without resizing texture");
                Equal(0L, Difference(spread, Render()), "Repeated/seeked render pixels are deterministic");
                settings.TransparentBackground = true;
                for (int i = 3; i < input.Length; i += 4) input[i] = input[i - 3] == 0 ? (byte)0 : (byte)255;
                byte[] transparentBase = Render(false);
                byte[] transparentDirt = Render();
                Check(transparentDirt[neighbor + 3] > transparentBase[neighbor + 3], "Dirt halo has alpha outside geometry");
                dirt.TexturePath = "";
                Equal(0L, Difference(transparentBase, Render()), "Missing texture preserves transparent output");
                string transparentTexture = fixture.NewPath(".png");
                writePng.Invoke(null, [transparentTexture, new byte[] { 255, 255, 255, 0 }, 1, 1, false, true]);
                dirt.TexturePath = transparentTexture;
                Equal(0L, Difference(transparentBase, Render()), "Transparent dirt texels contribute no light or alpha");
                input = pointInput;
                prepare.Invoke(viewport, [camera, 32u, 16u]);
                dirt.TexturePath = texture;
                prepare.Invoke(viewport, [camera, 32u, 16u]);
                gl.BindTexture(GLEnum.Texture2D, Field("_lensDirtGlowTex"));
                gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureWidth, out int width);
                gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureHeight, out int height);
                Equal(32, width, "Lens dirt source resizes for feeds/exports");
                Equal(16, height, "Lens dirt source supports rectangular output");

                var previousContext = ImGui.GetCurrentContext();
                var context = ImGui.CreateContext();
                var preview = new Viewport { Gl = gl, IsPreviewViewport = true, MainViewport = viewport, OverlaysEnabled = false };
                try
                {
                    var io = ImGui.GetIO();
                    io.IniFilename = null;
                    io.LogFilename = null;
                    io.DisplaySize = new System.Numerics.Vector2(500, 400);
                    io.DeltaTime = 1f / 60f;
                    io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
                    preview.InitPreviewViewport(size, size);
                    settings.TransparentBackground = false;
                    settings.ColorManagementEnabled = false;
                    settings.ShadowsEnabled = false;
                    settings.FogEnabled = false;
                    settings.SkyMode = "minecraft";
                    viewport.OverlaysEnabled = false;
                    string background = fixture.NewPath(".png");
                    writePng.Invoke(null, [background, new byte[] { 160, 160, 160, 255 }, 1, 1, false, true]);
                    viewport.SetBackgroundImage(background, "stretch", 1f, 0f, System.Numerics.Vector2.Zero);
                    byte[] Preview(bool enabled, bool feed)
                    {
                        dirt.Intensity = enabled ? 100f : 0f;
                        preview.RenderScenePublic(viewport.Camera, camera, size, size, highQuality: !feed, applyCameraEffects: feed);
                        byte[] pixels = new byte[size * size * 4];
                        gl.ActiveTexture(GLEnum.Texture0);
                        gl.BindTexture(GLEnum.Texture2D, preview.ColorTexture);
                        fixed (byte* data = pixels)
                            gl.GetTexImage(GLEnum.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                        Equal(GLEnum.NoError, gl.GetError(), "Public preview pipeline has no GL errors");
                        return pixels;
                    }
                    dirt.Radius = 0f;
                    foreach (bool feed in new[] { false, true })
                    {
                        byte[] clean = Preview(false, feed);
                        byte[] dirty = Preview(true, feed);
                        Check(Top(dirty) > Top(clean) + 5, "Lens dirt is wired into " + (feed ? "camera feeds" : "high-quality previews/exports"));
                        settings.GlowEnabled = true;
                        dirt.AffectedByBloom = false;
                        dirt.AffectedByGlow = true;
                        clean = Preview(false, feed);
                        dirty = Preview(true, feed);
                        Check(Top(dirty) > Top(clean) + 3, "Glow independently illuminates dirt in " + (feed ? "shaded camera feeds" : "rendered previews"));
                        settings.GlowEnabled = false;
                        dirt.AffectedByBloom = true;
                        dirt.AffectedByGlow = false;
                    }
                }
                finally
                {
                    preview.DisposeFramebufferResources();
                    ImGui.DestroyContext(context);
                    ImGui.SetCurrentContext(previousContext);
                }
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
}
