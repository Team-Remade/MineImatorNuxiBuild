using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.window.windows;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using StbImageSharp;

internal static partial class Program
{
    private static unsafe void StartupLogoGraphics()
    {
        using var glfw = Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(WindowHintBool.Visible, false);
        glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
        var window = new StartupProgressWindow(560, 280, "Startup logo test", glfw);
        Check(window.WindowHandle != null, "Startup OpenGL context");
        glfw.MakeContextCurrent(window.WindowHandle);
        using var gl = GL.GetApi(glfw.GetProcAddress);
        window.SetGL(gl);
        var context = ImGui.CreateContext();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(StartupProgressWindow);
        var textures = (IList)type.GetField("_loadingGifFrames", flags)!.GetValue(window)!;
        try
        {
            var expected = new List<(int Width, int Height, int Delay, string Hash)>();
            using (var stream = type.Assembly.GetManifestResourceStream("MineImatorSimplyRemade.assets.img.loading.gif")!)
                foreach (var frame in ImageResult.AnimatedGifFramesFromStream(stream, ColorComponents.RedGreenBlueAlpha))
                    expected.Add((frame.Width, frame.Height, frame.DelayInMs > 0 ? frame.DelayInMs : 80,
                        Convert.ToHexString(SHA256.HashData(frame.Data))));
            Check(expected.Select(frame => frame.Hash).Distinct().Count() > 1, "Bundled logo has distinct animation frames");

            type.GetMethod("EnsureLoadingGifTexture", flags)!.Invoke(window, null);
            Equal(expected.Count, textures.Count, "Every GIF frame is uploaded");
            T Value<T>(object frame, string name) => (T)frame.GetType().GetProperty(name)!.GetValue(frame)!;
            for (int i = 0; i < expected.Count; i++)
            {
                object frame = textures[i]!;
                Equal(expected[i].Width, Value<int>(frame, "Width"), "GIF frame width");
                Equal(expected[i].Height, Value<int>(frame, "Height"), "GIF frame height");
                Equal(expected[i].Delay, Value<int>(frame, "DelayMs"), "GIF frame delay");
                gl.BindTexture(GLEnum.Texture2D, Value<uint>(frame, "TextureId"));
                byte[] pixels = new byte[expected[i].Width * expected[i].Height * 4];
                fixed (byte* pointer = pixels)
                    gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
                Equal(expected[i].Hash, Convert.ToHexString(SHA256.HashData(pixels)), $"GIF frame {i} GPU pixels");
            }

            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new Vector2(560, 280);
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var renderUi = type.GetMethod("RenderUi", flags)!;
            void Draw(float delta, int expectedIndex)
            {
                io.DeltaTime = delta;
                ImGui.NewFrame();
                renderUi.Invoke(window, null);
                ImGui.Render();
                Equal(expectedIndex, (int)type.GetField("_loadingGifFrameIndex", flags)!.GetValue(window)!,
                    "GIF advances according to elapsed frame delays");
                ulong texture = Value<uint>(textures[expectedIndex]!, "TextureId");
                var data = ImGui.GetDrawData();
                bool drawsFrame = false;
                for (int i = 0; i < data.CmdListsCount; i++)
                    for (int j = 0; j < data.CmdLists[i].CmdBuffer.Size; j++)
                        drawsFrame |= data.CmdLists[i].CmdBuffer[j].TexRef.TexID == texture;
                Check(drawsFrame, "Startup UI draws the current GIF texture");
            }
            Draw(0.001f, 0);
            Draw(expected[0].Delay / 2000f, 0);
            Draw(expected[0].Delay / 2000f + 0.001f, 1);
            for (int i = 2; i < expected.Count; i++)
                Draw(expected[i - 1].Delay / 1000f, i);
            Draw(expected[^1].Delay / 1000f, 0);
            Equal(expected.Count, textures.Count, "Redraws reuse the uploaded animation");
            Equal(GLEnum.NoError, gl.GetError(), "Startup GIF OpenGL errors");
        }
        finally
        {
            foreach (object frame in textures)
                gl.DeleteTexture((uint)frame.GetType().GetProperty("TextureId")!.GetValue(frame)!);
            ImGui.DestroyContext(context);
            window.Dispose();
            glfw.Terminate();
        }
    }
}
