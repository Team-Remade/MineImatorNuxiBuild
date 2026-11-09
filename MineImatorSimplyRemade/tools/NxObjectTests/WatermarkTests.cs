using System.Reflection;
using System.Numerics;
using System.Text.Json;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using Silk.NET.OpenGL;
using StbImageSharp;

internal static partial class Program
{
    private static void WatermarkSettings(Fixture fixture)
    {
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectRenderSettings)!;
        Check(!legacy.WatermarkEnabled, "Old projects have no watermark");
        Equal("", legacy.WatermarkImagePath, "No implicit watermark image");
        var panel = new PropertiesPanel
        {
            WatermarkEnabled = true,
            WatermarkImagePath = fixture.File("textures\\watermark.png", "image"),
            WatermarkHorizontalAlignment = 0,
            WatermarkVerticalAlignment = 1,
            WatermarkPadding = 12f,
            WatermarkScale = 0.5f,
            WatermarkOpacity = 0.75f
        };
        var manifest = new ProjectManifest();
        panel.WriteProjectSettingsToManifest(manifest);
        string json = JsonSerializer.Serialize(manifest, AppJsonContext.Default.ProjectManifest);
        var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectManifest)!;
        var loaded = new PropertiesPanel();
        loaded.LoadProjectSettingsFromManifest(restored);
        Check(loaded.WatermarkEnabled, "Enabled persists");
        Equal(panel.WatermarkImagePath, ProjectManager.Instance.ResolveAssetPath(loaded.WatermarkImagePath), "Image reference resolves");
        Equal(0, loaded.WatermarkHorizontalAlignment, "Horizontal alignment persists");
        Equal(1, loaded.WatermarkVerticalAlignment, "Vertical alignment persists");
        Near(12f, loaded.WatermarkPadding, "Padding persists");
        Near(0.5f, loaded.WatermarkScale, "Scale persists");
        Near(0.75f, loaded.WatermarkOpacity, "Opacity persists");
        loaded.WatermarkEnabled = false;
        loaded.WriteProjectSettingsToManifest(restored);
        panel.LoadProjectSettingsFromManifest(restored);
        Check(!panel.WatermarkEnabled, "Disabled state persists");
        Near(12f, panel.WatermarkPadding, "Disabling retains settings");
        panel.LoadProjectSettingsFromManifest(new ProjectManifest());
        Check(!panel.WatermarkEnabled && panel.WatermarkImagePath == "", "New project resets watermark");
        restored.Settings.WatermarkScale = float.NaN;
        restored.Settings.WatermarkPadding = -1f;
        restored.Settings.WatermarkOpacity = 3f;
        restored.Settings.WatermarkHorizontalAlignment = -1;
        restored.Settings.WatermarkVerticalAlignment = 9;
        panel.LoadProjectSettingsFromManifest(restored);
        Near(0.01f, panel.WatermarkScale, "Non-finite scale is sanitized");
        Near(0f, panel.WatermarkPadding, "Padding cannot be negative");
        Near(1f, panel.WatermarkOpacity, "Opacity is bounded");
        Equal(0, panel.WatermarkHorizontalAlignment, "Horizontal alignment is bounded");
        Equal(2, panel.WatermarkVerticalAlignment, "Vertical alignment is bounded");
    }

    private static unsafe void WatermarkControls(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new Vector2(800, 700);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var panel = new PropertiesPanel();
            var draw = typeof(PropertiesPanel).GetMethod("DrawWatermarkSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Vector2 checkbox = default;
            float Frame(bool down = false)
            {
                io.AddMousePosEvent(checkbox.X, checkbox.Y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(600, 600), ImGuiCond.Always);
                ImGui.Begin("Watermark controls");
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                float start = ImGui.GetCursorPosY();
                draw.Invoke(panel, null);
                if (!panel.WatermarkEnabled)
                    checkbox = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
                float height = ImGui.GetCursorPosY() - start;
                ImGui.End();
                ImGui.Render();
                return height;
            }
            float disabledHeight = Frame();
            Frame(true);
            float enabledHeight = Frame();
            Check(panel.WatermarkEnabled, "Clicking Enabled enables watermark");
            Check(enabledHeight > disabledHeight + ImGui.GetFrameHeight() * 6f, "Enabled shows image and all five settings");
            Frame(true);
            float hiddenHeight = Frame();
            Check(!panel.WatermarkEnabled, "Clicking Enabled again disables watermark");
            Near(disabledHeight, hiddenHeight, "Disabled hides every other watermark setting");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void WatermarkGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 48, "Watermark regression", null, null);
        Check(window != null, "OpenGL context");
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DeltaTime = 1f / 60f;
            io.DisplaySize = new Vector2(800, 700);
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            MineImatorLoader.Instance.Initialize(gl);
            var settings = new PropertiesPanel
            {
                TransparentBackground = true, ShadowsEnabled = false, FogEnabled = false,
                WatermarkEnabled = true, WatermarkPadding = 3f, WatermarkScale = 2f, WatermarkOpacity = 0.5f
            };
            var main = new Viewport { Gl = gl, PropertiesPanel = settings, OverlaysEnabled = false };
            main.InitFramebuffer(64, 48);
            var preview = new Viewport { Gl = gl, MainViewport = main, IsPreviewViewport = true, OverlaysEnabled = false };
            preview.InitPreviewViewport(64, 48);
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var write = typeof(Viewport).GetMethod("WritePng", BindingFlags.Static | BindingFlags.NonPublic)!;
                string image = fixture.NewPath(".png");
                write.Invoke(null, [image, new byte[]
                {
                    255, 0, 0, 255, 255, 0, 0, 255,
                    0, 0, 255, 255, 0, 0, 255, 255
                }, 2, 2, false, true]);
                settings.WatermarkImagePath = image;
                byte[] Capture(bool quality = true, uint width = 64, uint height = 48)
                {
                    Check(preview.CaptureCurrentView(width, height, quality, out var pixels, rgba: true), "Watermarked capture succeeds");
                    Equal((int)(width * height * 4), pixels.Length, "RGBA output shape");
                    return pixels;
                }
                foreach (int horizontal in new[] { 0, 1, 2 })
                foreach (int vertical in new[] { 0, 1, 2 })
                {
                    settings.WatermarkHorizontalAlignment = horizontal;
                    settings.WatermarkVerticalAlignment = vertical;
                    byte[] pixels = Capture();
                    int left = horizontal switch { 0 => 3, 1 => 30, _ => 57 };
                    int top = vertical switch { 0 => 3, 1 => 22, _ => 41 };
                    for (int y = 0; y < 48; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        bool inside = x >= left && x < left + 4 && y >= top && y < top + 4;
                        int alpha = pixels[(y * 64 + x) * 4 + 3];
                        Check(Math.Abs(alpha - (inside ? 128 : 0)) <= 1, "Exact alignment, padding, scale and opacity bounds");
                    }
                    Equal((byte)255, pixels[(top * 64 + left) * 4], "Top of watermark is red");
                    Equal((byte)255, pixels[((top + 3) * 64 + left) * 4 + 2], "Bottom of watermark is blue");
                    Check(pixels.SequenceEqual(Capture(false)), "Fast and high-quality export watermark parity");
                }
                settings.WatermarkEnabled = false;
                byte[] baseline = Capture();
                Check(baseline.Where((_, i) => i % 4 == 3).All(alpha => alpha == 0), "Disabled has no watermark");
                settings.WatermarkEnabled = true;
                settings.WatermarkOpacity = 0f;
                Check(baseline.SequenceEqual(Capture()), "Zero opacity preserves exact pixels");
                settings.WatermarkOpacity = 1f;
                settings.WatermarkScale = 1f;
                settings.WatermarkPadding = 0f;
                byte[] resized = Capture(width: 80, height: 48);
                Equal((byte)255, resized[((46 * 80 + 78) * 4) + 3], "Resizing keeps bottom-right alignment and native scale");
                settings.ColorManagementEnabled = true;
                settings.AntiAliasingEnabled = true;
                Check(resized.SequenceEqual(Capture(width: 80, height: 48)), "Watermark is composed after HDR/color management and FXAA");
                string output = fixture.NewPath(".png");
                write.Invoke(null, [output, resized, 80, 48, false, true]);
                using var stream = File.OpenRead(output);
                Check(resized.SequenceEqual(ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha).Data), "PNG export preserves watermark pixels and alpha");

                settings.ColorManagementEnabled = false;
                settings.AntiAliasingEnabled = false;
                settings.TransparentBackground = false;
                settings.WatermarkOpacity = 0.5f;
                byte[] opaque = Capture();
                Check(opaque.Where((_, i) => i % 4 == 3).All(alpha => alpha == 255), "Watermark preserves opaque background alpha");

                string translucentImage = fixture.NewPath(".png");
                write.Invoke(null, [translucentImage, new byte[] { 255, 0, 0, 128, 255, 0, 0, 128 }, 1, 2, false, true]);
                settings.WatermarkImagePath = translucentImage;
                settings.TransparentBackground = true;
                settings.WatermarkScale = 3f;
                byte[] translucent = Capture();
                for (int y = 0; y < 48; y++)
                for (int x = 0; x < 64; x++)
                    Equal((byte)(x >= 61 && y >= 42 ? 64 : 0), translucent[(y * 64 + x) * 4 + 3],
                        "Rectangular image keeps aspect ratio and source alpha multiplies opacity");
                Equal((byte)255, translucent[(42 * 64 + 61) * 4], "Partial-alpha watermark retains straight red color");

                preview.RenderScenePublic(main.Camera, null, 64, 48, highQuality: true, applyCameraEffects: true);
                uint previewTexture = (uint)typeof(Viewport).GetField("_previewColorTex", flags)!.GetValue(preview)!;
                byte[] feed = new byte[64 * 48 * 4];
                gl.BindTexture(GLEnum.Texture2D, previewTexture);
                fixed (byte* data = feed) gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
                Check(feed.Where((_, i) => i % 4 == 3).All(alpha => alpha == 0), "Live camera feeds do not include project watermark");

                uint Field(string name) => (uint)typeof(Viewport).GetField(name, flags)!.GetValue(main)!;
                var pass = typeof(Viewport).GetMethod("ApplyWatermarkPass", flags)!;
                gl.BindFramebuffer(GLEnum.Framebuffer, Field("_fbo"));
                gl.ClearColor(0f, 0f, 1f, 1f);
                gl.Clear(ClearBufferMask.ColorBufferBit);
                pass.Invoke(main, [Field("_fbo"), Field("_glowFbo"), Field("_glowTex"), 64u, 48u, settings]);
                byte[] mainPixels = new byte[64 * 48 * 4];
                gl.BindTexture(GLEnum.Texture2D, Field("_colorTex"));
                fixed (byte* data = mainPixels) gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
                int markPixel = (0 * 64 + 61) * 4;
                Equal((byte)64, mainPixels[markPixel], "Main rendered viewport composites watermark over scene red");
                Equal((byte)191, mainPixels[markPixel + 2], "Source-over composition preserves remaining scene blue");
                Equal((byte)255, mainPixels[markPixel + 3], "Source-over result stays opaque");
                Equal(GLEnum.NoError, gl.GetError(), "Watermark has no OpenGL errors");
            }
            finally
            {
                preview.DisposeFramebufferResources();
                main.DisposeFramebufferResources();
                Mesh.SceneUBO = null;
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
