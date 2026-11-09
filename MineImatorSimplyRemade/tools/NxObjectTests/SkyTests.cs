using System.Globalization;
using System.Reflection;
using System.Text.Json;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using Silk.NET.OpenGL;
using Shader = MineImatorSimplyRemade.core.mdl.Shader;

internal static partial class Program
{
    private static void SkySettings(Fixture fixture)
    {
        var old = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectRenderSettings)!;
        Equal("minecraft", old.SkyMode, "Old projects keep Minecraft sky");
        Equal("", old.SkyTexture, "Old projects have no environment texture");
        Check(!old.SkyboxMapped, "Unmapped by default");
        var saved = ProjectManager.Instance.Manifest.Settings;
        try
        {
            var panel = new PropertiesPanel { SkyMode = "box", SkyTexture = "images\\sky.png", SkyboxMapped = true };
            panel.SkyRotation[0] = 15f;
            panel.SkyRotation[1] = -70f;
            panel.SkyRotation[2] = 33f;
            var manifest = new ProjectManifest();
            panel.WriteProjectSettingsToManifest(manifest);
            string json = JsonSerializer.Serialize(manifest, AppJsonContext.Default.ProjectManifest);
            var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectManifest)!;
            var loaded = new PropertiesPanel();
            loaded.LoadProjectSettingsFromManifest(restored);
            Equal("box", loaded.SkyMode, "Sky mode round trip");
            Equal(panel.SkyTexture, loaded.SkyTexture, "Sky image round trip");
            Check(loaded.SkyboxMapped, "Mapped round trip");
            for (int i = 0; i < 3; i++) Near(panel.SkyRotation[i], loaded.SkyRotation[i], "Rotation round trip");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            ProjectManager.Instance.Manifest.Settings = restored.Settings;
            var add = typeof(PropertiesPanel).GetMethod("AddBackgroundKeyframe", flags)!;
            foreach (string property in new[] { "SkyMode", "SkyTexture", "SkyboxMapped", "SkyRotation.0", "SkyRotation.1", "SkyRotation.2" })
            {
                add.Invoke(loaded, [property, 0]);
                if (property.StartsWith("SkyRotation.", StringComparison.Ordinal))
                    loaded.SkyRotation[int.Parse(property[^1..], CultureInfo.InvariantCulture)] += 90f;
                else if (property == "SkyMode") loaded.SkyMode = "sphere";
                else if (property == "SkyTexture") loaded.SkyTexture = "images\\panorama.png";
                else loaded.SkyboxMapped = false;
                add.Invoke(loaded, [property, 10]);
            }
            typeof(PropertiesPanel).GetMethod("ApplyBackgroundAnimation", flags)!.Invoke(loaded, [5]);
            Near(60f, loaded.SkyRotation[0], "Rotation interpolates");
            Near(-25f, loaded.SkyRotation[1], "Yaw interpolates");
            Near(78f, loaded.SkyRotation[2], "Roll interpolates");
            Equal("box", loaded.SkyMode, "Mode uses stepped keys");
            Check(loaded.SkyboxMapped, "Mapped uses stepped keys");
            typeof(PropertiesPanel).GetMethod("ApplyBackgroundAnimation", flags)!.Invoke(loaded, [10]);
            Equal("sphere", loaded.SkyMode, "Mode switches at key");
            Equal("images\\panorama.png", loaded.SkyTexture, "Texture switches at key");
            Check(!loaded.SkyboxMapped, "Mapped switches at key");
            var properties = (string[])typeof(PropertiesPanel).GetField("BackgroundKeyframeProperties",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Check(properties.Contains("SkyRotation.2") && properties.Contains("SkyTexture") &&
                  properties.Contains("SkyMode") && properties.Contains("SkyboxMapped"), "All sky tracks discoverable");
        }
        finally
        {
            ProjectManager.Instance.Manifest.Settings = saved;
        }
    }

    private static unsafe void SkyGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(65, 65, "Sky regression", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            using var fixture = new Fixture();
            const int width = 192, height = 128;
            byte[] image = new byte[width * height * 3];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 3;
                    image[i] = (byte)(20 + x);
                    image[i + 1] = (byte)(30 + y);
                    image[i + 2] = (byte)(40 + (x / 64 + y / 64 * 3) * 30);
                }
            string texturePath = fixture.NewPath(".png");
            typeof(Viewport).GetMethod("WritePng24", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [texturePath, image, width, height, false]);
            var panel = new PropertiesPanel { SkyMode = "box", SkyTexture = texturePath, FogEnabled = false };
            var viewport = new Viewport { Gl = gl, PropertiesPanel = panel };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Viewport).GetMethod("InitBackgroundRenderer", flags)!.Invoke(viewport, null);
            typeof(Viewport).GetMethod("InitSkyRenderer", flags)!.Invoke(viewport, null);
            var shader = (Shader)typeof(Viewport).GetField("_skyShader", flags)!.GetValue(viewport)!;
            gl.GetProgram(shader.ShaderProgram, GLEnum.LinkStatus, out int linked);
            Equal(1, linked, "Sky shader compiles and links");
            using var cube = new CubeMesh(gl);
            var camera = new Camera { Distance = 5f, FovY = 0.1f };
            gl.Viewport(0, 0, 65, 65);
            gl.Disable(GLEnum.Blend);
            byte[] Draw(vec3 direction, int px = 32, int py = 32)
            {
                direction = direction.Normalized;
                camera.Yaw = MathF.Atan2(-direction.x, -direction.z);
                camera.Pitch = -MathF.Asin(direction.y);
                gl.ClearColor(0.1f, 0.2f, 0.3f, 1f);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                viewport.RenderSkyPublic(camera, 65, 65);
                byte[] pixel = new byte[4];
                fixed (byte* ptr = pixel) gl.ReadPixels(px, py, 1, 1, GLEnum.Rgba, GLEnum.UnsignedByte, ptr);
                return pixel;
            }
            void Expect(byte[] actual, vec2 uv, string message)
            {
                int x = Math.Clamp((int)(uv.x * width), 0, width - 1);
                int y = Math.Clamp((int)(uv.y * height), 0, height - 1);
                int i = (y * width + x) * 3;
                for (int c = 0; c < 3; c++)
                    Check(Math.Abs(actual[c] - image[i + c]) <= 2, $"{message}, channel {c}: {actual[c]} vs {image[i + c]}");
            }
            vec3[] rays =
            [
                new(0.22f, -0.36f, 1f), new(-0.22f, 0.36f, -1f),
                new(0.22f, 1f, -0.36f), new(-0.22f, -1f, 0.36f),
                new(1f, -0.36f, 0.22f), new(-1f, 0.36f, -0.22f)
            ];
            foreach (bool mapped in new[] { false, true })
            {
                panel.SkyboxMapped = mapped;
                cube.SetMapped(mapped);
                for (int face = 0; face < rays.Length; face++)
                {
                    vec3 hit = rays[face] * 0.5f;
                    int offset = face * 6;
                    vec3 p0 = cube.Vertices[offset];
                    vec3 e1 = cube.Vertices[offset + 1] - p0, e2 = cube.Vertices[offset + 5] - p0;
                    float s = vec3.Dot(hit - p0, e1) / e1.LengthSqr;
                    float t = vec3.Dot(hit - p0, e2) / e2.LengthSqr;
                    vec2 uv = cube.TexCoords[offset] + s * (cube.TexCoords[offset + 1] - cube.TexCoords[offset]) +
                              t * (cube.TexCoords[offset + 5] - cube.TexCoords[offset]);
                    Expect(Draw(rays[face]), uv, $"Cube face {face}, mapped={mapped}");
                }
            }
            panel.SkyMode = "sphere";
            vec3 sample = new vec3(0.6f, 0.4f, -1f).Normalized;
            vec2 SphereUv(vec3 d) => new(MathF.Atan2(d.z, d.x) / (2f * MathF.PI) + 0.5f, MathF.Acos(d.y) / MathF.PI);
            Expect(Draw(sample), SphereUv(sample), "Sphere orientation and RGB preservation");
            foreach (int axis in new[] { 0, 1, 2 })
            {
                panel.SkyRotation[axis] = 90f;
                vec3 rotated = axis == 0 ? new(sample.x, sample.z, -sample.y) :
                    axis == 1 ? new(-sample.z, sample.y, sample.x) : new(sample.y, -sample.x, sample.z);
                Expect(Draw(sample), SphereUv(rotated), $"Sphere rotation axis {axis}");
                panel.SkyMode = "box";
                byte[] boxRotated = Draw(sample);
                panel.SkyRotation[axis] = 0f;
                Check(boxRotated.SequenceEqual(Draw(rotated)), $"Skybox rotation axis {axis}");
                panel.SkyMode = "sphere";
            }
            panel.SkyRotation[0] = 27f; panel.SkyRotation[1] = -42f; panel.SkyRotation[2] = 63f;
            vec3 combined = sample;
            for (int axis = 2; axis >= 0; axis--)
            {
                float a = -panel.SkyRotation[axis] * MathF.PI / 180f;
                float c = MathF.Cos(a), s = MathF.Sin(a);
                combined = axis == 0 ? new(combined.x, c * combined.y - s * combined.z, s * combined.y + c * combined.z) :
                    axis == 1 ? new(c * combined.x + s * combined.z, combined.y, -s * combined.x + c * combined.z) :
                    new(c * combined.x - s * combined.y, s * combined.x + c * combined.y, combined.z);
            }
            Expect(Draw(sample), SphereUv(combined), "Combined XYZ inverse rotation order");
            Array.Clear(panel.SkyRotation);
            byte[] before = Draw(sample);
            camera.Target = new vec3(100f, 200f, -300f);
            Check(before.SequenceEqual(Draw(sample)), "Sky ignores camera translation");
            camera.Roll = 0.7f;
            byte[] rolled = Draw(sample, 60, 32);
            camera.Roll = 0f;
            Check(!rolled.SequenceEqual(Draw(sample, 60, 32)), "Sky respects camera roll");
            panel.FogEnabled = panel.SkyFog = panel.CustomFogColor = true;
            panel.FogColor[0] = 1f; panel.FogColor[1] = panel.FogColor[2] = 0f;
            byte[] fog = Draw(-vec3.UnitZ);
            Check(fog[0] > 250 && fog[1] < 3 && fog[2] < 3, "Sky fog applies at horizon");
            panel.FogEnabled = false;
            Draw(sample);
            float depth;
            gl.ReadPixels(32, 32, 1, 1, GLEnum.DepthComponent, GLEnum.Float, &depth);
            Near(1f, depth, "Sky stays at far depth");
            panel.SkyTexture = fixture.NewPath(".png");
            Draw(sample);
            Check(viewport.SkyTextureError.Length > 0, "Missing textures report an error");
            Equal(0u, (uint)typeof(Viewport).GetField("_environmentTexture", flags)!.GetValue(viewport)!, "Missing image clears stale texture");
            panel.SkyTexture = texturePath;
            Check(before.SequenceEqual(Draw(sample)), "Texture recovers after missing image");
            panel.UseSky = false;
            byte[] disabled = Draw(sample);
            Check(Math.Abs(disabled[0] - 26) <= 1 && disabled[1] == 51 && Math.Abs(disabled[2] - 77) <= 1,
                "Disabled sky leaves underlying background visible");
            panel.UseSky = true;
            panel.SkyTexture = "";
            Draw(sample);
            Equal(0u, (uint)typeof(Viewport).GetField("_environmentTexture", flags)!.GetValue(viewport)!, "Clearing image frees texture");
            Equal(GLEnum.NoError, gl.GetError(), "No OpenGL errors");
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
