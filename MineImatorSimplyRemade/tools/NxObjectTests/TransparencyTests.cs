using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
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
    private static void TransparencySettings(Fixture fixture)
    {
        var defaults = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectRenderSettings)!;
        Check(!defaults.TransparentBackground, "Old projects retain opaque backgrounds");
        var panel = new PropertiesPanel { TransparentBackground = true, SkyMode = "sphere" };
        var manifest = new ProjectManifest();
        panel.WriteProjectSettingsToManifest(manifest);
        string json = JsonSerializer.Serialize(manifest, AppJsonContext.Default.ProjectManifest);
        var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectManifest)!;
        var loaded = new PropertiesPanel();
        loaded.LoadProjectSettingsFromManifest(restored);
        Check(loaded.TransparentBackground, "Transparency survives project persistence");
        Check(loaded.UseSky, "Transparency does not disable environment lighting");
        Equal("sphere", loaded.SkyMode, "Transparency does not overwrite sky settings");
        loaded.LoadProjectSettingsFromManifest(new ProjectManifest());
        Check(!loaded.TransparentBackground, "Loading an opaque project resets transparency");
        var normalize = typeof(MineImatorSimplyRemade.core.window.windows.MainWindow)
            .GetMethod("NormalizeOutputExtension", BindingFlags.Static | BindingFlags.NonPublic)!;
        Equal("mov", (string)normalize.Invoke(null, ["mov"])!, "MOV save dialog keeps MOV extension");
    }

    private static unsafe void TransparencyGraphics()
    {
        using var fixture = new Fixture();
        TransparencySettings(fixture);
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Transparency regression", null, null);
        Check(window != null, "OpenGL context");
        var oldContext = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            var io = ImGui.GetIO();
            io.DeltaTime = 1f / 60f;
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(500, 400);
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var settings = new PropertiesPanel { TransparentBackground = true, ShadowsEnabled = false, FogEnabled = false };
            var mainView = new Viewport { Gl = gl, PropertiesPanel = settings, OverlaysEnabled = false };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            mainView.InitFramebuffer(64, 64);
            var preview = new Viewport { Gl = gl, IsPreviewViewport = true, MainViewport = mainView, OverlaysEnabled = false };
            preview.InitPreviewViewport(64, 64);
            try
            {
                byte[] Capture(bool highQuality = true, uint width = 64, uint height = 64)
                {
                    Check(preview.CaptureCurrentView(width, height, highQuality, out var pixels, rgba: true),
                        "RGBA capture succeeds");
                    Equal((int)(width * height * 4), pixels.Length, "RGBA frame shape");
                    return pixels;
                }

                void Empty(byte[] pixels, string message)
                {
                    for (int i = 3; i < pixels.Length; i += 4)
                        Equal((byte)0, pixels[i], message);
                }

                var writePng = typeof(Viewport).GetMethod("WritePng", BindingFlags.Static | BindingFlags.NonPublic)!;
                string background = fixture.NewPath(".png");
                writePng.Invoke(null, [background, new byte[] { 255, 0, 0, 255 }, 1, 1, false, true]);
                settings.SkyTexture = background;
                mainView.SetBackgroundImage(background, "stretch", 1f, 0f, System.Numerics.Vector2.Zero);
                foreach (string sky in new[] { "minecraft", "sphere", "box" })
                {
                    settings.SkyMode = sky;
                    Empty(Capture(), sky + " sky removed, including AO and GI");
                    Empty(Capture(false), sky + " sky removed in fast preview");
                }
                settings.SkyMode = "minecraft";
                settings.TransparentBackground = false;
                byte[] opaque = Capture();
                for (int i = 3; i < opaque.Length; i += 4)
                    Equal((byte)255, opaque[i], "Normal sky remains opaque");
                Equal((byte)255, opaque[0], "Opaque background image still renders");
                Equal((byte)0, opaque[1], "Background image has expected color");
                settings.TransparentBackground = true;

                ImGui.NewFrame();
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(500, 400));
                mainView.Render();
                ImGui.Render();
                uint mainTexture = (uint)typeof(Viewport).GetField("_colorTex", flags)!.GetValue(mainView)!;
                gl.BindTexture(GLEnum.Texture2D, mainTexture);
                gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureWidth, out int editorWidth);
                gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureHeight, out int editorHeight);
                byte[] editorPixels = new byte[editorWidth * editorHeight * 4];
                fixed (byte* p = editorPixels) gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
                Empty(editorPixels, "Main editor removes sky and background image");

                using var cube = new CubeMesh(gl) { Unlit = true, BlendColor = new vec4(0.8f, 0.3f, 0.1f, 1f) };
                var obj = new SceneObject { Name = "Transparency subject" };
                obj.Visuals.Add(cube);
                mainView.SceneObjects.Add(obj);
                byte[] solid = Capture();
                int center = (32 * 64 + 32) * 4;
                Equal((byte)255, solid[center + 3], "Opaque geometry retains full coverage");
                Equal((byte)0, solid[3], "Background behind geometry remains transparent");
                cube.Alpha = 0.5f;
                byte[] partial = Capture();
                Check(Math.Abs(partial[center + 3] - 128) <= 1, "Half opacity is not squared by blending/AO/GI");
                for (int c = 0; c < 3; c++)
                    Check(Math.Abs(partial[center + c] - solid[center + c]) <= 3,
                        "Straight-alpha foreground color is not darkened");

                settings.ColorManagementEnabled = true;
                settings.AntiAliasingEnabled = true;
                byte[] hdr = Capture(width: 80, height: 48);
                Equal((byte)0, hdr[3], "HDR, FXAA and resizing preserve background alpha");
                settings.ColorManagementEnabled = false;
                settings.AntiAliasingEnabled = false;
                Equal((byte)0, Capture()[3], "Switching back to LDR preserves alpha");

                cube.Alpha = 1f;
                cube.BlendColor = vec4.Ones;
                byte[] noGlow = Capture();
                settings.GlowEnabled = true;
                byte[] glow = Capture();
                Check(Enumerable.Range(0, 64 * 64).Any(i => noGlow[i * 4 + 3] == 0 && glow[i * 4 + 3] > 0),
                    "Glow has visible alpha outside the geometry");
                settings.GlowEnabled = false;

                var camera = new CameraSceneObject();
                camera.Effects.Add(new CameraEffect { Type = CameraEffectType.Bloom });
                camera.Effects[0].Bloom.Threshold = 10f;
                preview.RenderScenePublic(mainView.Camera, camera, 64, 64, highQuality: true);
                byte[] bloom = new byte[64 * 64 * 4];
                gl.BindTexture(GLEnum.Texture2D, preview.ColorTexture);
                fixed (byte* p = bloom) gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
                Check(Enumerable.Range(0, 64 * 64).Any(i => noGlow[i * 4 + 3] == 0 && bloom[i * 4 + 3] > 0),
                    "Camera bloom has visible alpha outside geometry");

                var exporter = new RenderExporter(preview);
                foreach (string format in new[] { "png", "webp" })
                {
                    string image = exporter.ExportImageFromRgb(64, 64, format, fixture.NewPath("." + format), partial, rgba: true);
                    byte[] decoded = DecodeTransparencyFile(image);
                    Equal(partial.Length, decoded.Length, format + " decoded frame size");
                    Check(partial.SequenceEqual(decoded), format + " retains exact colors, orientation and alpha");
                }
                RejectOpaqueFormat(() => exporter.ExportImageFromRgb(64, 64, "jpg", fixture.NewPath(".jpg"), partial, rgba: true),
                    "JPEG must not silently discard alpha");
                RejectOpaqueFormat(() => exporter.CreateVideoSession(64, 64, 30, 12000, "mp4", fixture.NewPath(".mp4"), rgba: true),
                    "MP4 must not silently discard alpha");
                string movie;
                using (var video = exporter.CreateVideoSession(64, 64, 30, 12000, "mov", fixture.NewPath(".mov"), rgba: true))
                {
                    video.AppendFrame(partial);
                    video.AppendFrame(solid);
                    video.Complete();
                    movie = video.OutputPath;
                }
                Check(Path.GetExtension(movie) == ".mov", "MOV is not normalized to MP4");
                byte[] frames = DecodeTransparencyFile(movie);
                Equal(partial.Length * 2, frames.Length, "MOV contains both animation frames");
                VerifyMovieAlpha(partial, frames.AsSpan(0, partial.Length));
                VerifyMovieAlpha(solid, frames.AsSpan(partial.Length));

                string sequence = fixture.NewPath();
                Directory.CreateDirectory(sequence);
                writePng.Invoke(null, [Path.Combine(sequence, "frame_000000.png"), partial, 64, 64, false, true]);
                writePng.Invoke(null, [Path.Combine(sequence, "frame_000001.png"), solid, 64, 64, false, true]);
                string sequenceMovie = RenderExporter.EncodePpmSequenceToVideo(sequence, 30, 12000, "mov",
                    fixture.NewPath(".mov"), null, 0, 2, rgba: true);
                Check(frames.SequenceEqual(DecodeTransparencyFile(sequenceMovie)),
                    "UI PNG staging and streamed MOV exports agree");
                using var png = File.OpenRead(Path.Combine(sequence, "frame_000000.png"));
                Check(partial.SequenceEqual(ImageResult.FromStream(png, ColorComponents.RedGreenBlueAlpha).Data),
                    "PNG sequences preserve exact RGBA pixels");
                Equal(GLEnum.NoError, gl.GetError(), "Transparency rendering has no GL errors");
            }
            finally
            {
                preview.DisposeFramebufferResources();
                mainView.DisposeFramebufferResources();
                Mesh.SceneUBO = null;
            }
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(oldContext);
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    private static void VerifyMovieAlpha(byte[] expected, ReadOnlySpan<byte> actual)
    {
        for (int i = 3; i < expected.Length; i += 4)
            Check(Math.Abs(expected[i] - actual[i]) <= 1, $"ProRes alpha at pixel {i / 4}");
    }

    private static void RejectOpaqueFormat(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("transparen", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static byte[] DecodeTransparencyFile(string file)
    {
        var start = new ProcessStartInfo(FfmpegBootstrap.GetFfmpegExecutablePath())
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (string arg in new[] { "-v", "error", "-i", file, "-f", "rawvideo", "-pix_fmt", "rgba", "-" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        using var output = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(output);
        process.WaitForExit();
        Check(process.ExitCode == 0, "Decode export: " + error.GetAwaiter().GetResult());
        return output.ToArray();
    }
}
