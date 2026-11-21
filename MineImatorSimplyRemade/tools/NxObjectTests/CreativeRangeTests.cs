using System.Reflection;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static void CreativeRangeProperties(Fixture fixture)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;

        var scaleObject = new SceneObject();
        var panel = new PropertiesPanel();
        typeof(PropertiesPanel).GetField("_currentObject", instance)!.SetValue(panel, scaleObject);
        typeof(PropertiesPanel).GetMethod("ApplyScale", instance)!.Invoke(panel, [new vec3(-2f, -3f, -4f)]);
        Near(-2f, scaleObject.LocalScale.x, "Properties scale accepts a negative X value");
        Near(-3f, scaleObject.LocalScale.y, "Properties scale accepts a negative Y value");
        Near(-4f, scaleObject.LocalScale.z, "Properties scale accepts a negative Z value");

        var timeline = new Timeline();
        var set = typeof(Timeline).GetMethod("SetPropertyValue", instance)!;
        var get = typeof(Timeline).GetMethod("GetPropertyValue", instance)!;
        set.Invoke(timeline, [scaleObject, "scale.x", -5f]);
        Near(-5f, Convert.ToSingle(get.Invoke(timeline, [scaleObject, "scale.x"])), "Timeline preserves negative scale");
        var scaleSnapshot = ProjectSceneSerializer.SerializeObjectForLibrary(scaleObject);
        var restoredScale = new SceneObject();
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", statics)!
            .Invoke(null, [restoredScale, scaleSnapshot, null]);
        Near(-5f, restoredScale.LocalScale.x, "Project persistence preserves negative scale");

        var heat = new HeatDistortSettings();
        heat.SetValue("strength", -250f);
        Near(-250f, heat.Strength, "Heat distortion accepts negative strength");
        heat.SetValue("speed", 2500f);
        Near(2500f, heat.Speed, "Heat distortion accepts values beyond the slider range");
        heat.SetValue("scale", -2000f);
        Near(-2000f, heat.Scale, "Heat distortion accepts negative scale");
        ExpectNonFiniteRejected(value => heat.SetValue("strength", value), "Heat distortion rejects non-finite strength");

        var distort = new DistortSettings();
        distort.SetValue("distort_amount", 250f);
        Near(250f, distort.DistortAmount, "Distortion accepts values beyond 100 percent");
        distort.SetValue("zoom_amount", 2000f);
        Near(2000f, distort.ZoomAmount, "Distortion zoom accepts values beyond its slider range");
        distort.SetValue("zoom_amount", -250f);
        Near(-250f, distort.ZoomAmount, "Distortion preserves negative zoom values");
        ExpectNonFiniteRejected(value => distort.SetValue("distort_amount", value), "Distortion rejects non-finite amount");

        var aberration = new ChromaticAberrationSettings();
        aberration.SetValue("blur_amount", 250f);
        Near(250f, aberration.BlurAmount, "Chromatic blur accepts values beyond 100 percent");
        aberration.SetValue("red_offset", -250f);
        Near(-250f, aberration.RedOffset, "Chromatic offsets accept values beyond 100 percent");
        ExpectNonFiniteRejected(value => aberration.SetValue("blur_amount", value), "Chromatic aberration rejects non-finite blur");

        var bloom = new BloomSettings();
        bloom.SetValue("radius", 250f);
        Near(250f, bloom.Radius, "Bloom radius accepts values beyond 100 percent");
        bloom.SetValue("intensity", 1500f);
        Near(1500f, bloom.Intensity, "Bloom intensity accepts values beyond its slider range");
        bloom.SetValue("threshold", -50f);
        Near(-50f, bloom.Threshold, "Bloom threshold accepts negative values");
        bloom.SetValue("anamorphic_ratio", -250f);
        Near(250f, bloom.AnamorphicRatio, "Bloom ratio keeps its divisor positive while accepting negative input");
        ExpectNonFiniteRejected(value => bloom.SetValue("intensity", value), "Bloom rejects non-finite intensity");

        var grainCamera = new CameraSceneObject();
        grainCamera.AddEffect(CameraEffectType.FilmGrain);
        var grain = grainCamera.Effects[0].FilmGrain;
        grain.Strength = -0.75f;
        grain.Saturation = 1.25f;
        grain.Size = 12f;
        set.Invoke(timeline, [grainCamera, "camera.effect.0.film_grain.strength", -0.75f]);
        Near(-0.75f, Convert.ToSingle(get.Invoke(timeline,
            [grainCamera, "camera.effect.0.film_grain.strength"])), "Film grain timeline preserves negative strength");
        var (grainArchive, _) = fixture.Roundtrip(grainCamera);
        Near(-0.75f, grainArchive.Root.CameraEffects[0].FilmGrain.Strength, "Film grain archive preserves negative strength");
        Near(1.25f, grainArchive.Root.CameraEffects[0].FilmGrain.Saturation, "Film grain archive preserves extended saturation");
        Near(12f, grainArchive.Root.CameraEffects[0].FilmGrain.Size, "Film grain archive preserves extended size");
        ExpectNonFiniteRejected(value => grain.SetValue("strength", value), "Film grain rejects non-finite strength");
        CreativeRangeNumericEntry();
    }

    private static void ExpectNonFiniteRejected(Action<float> setValue, string message)
    {
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool rejected = false;
            try { setValue(invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, message);
        }
    }

    private static unsafe void CreativeRangeNumericEntry()
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(800f, 300f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var editor = typeof(PropertiesPanel).GetMethod("InputFloatEditor", BindingFlags.Static | BindingFlags.NonPublic)!;
            float value = 1f;
            System.Numerics.Vector2 input = default;
            bool changed = false;

            void Frame(bool mouseDown = false)
            {
                io.AddMousePosEvent(input.X, input.Y);
                io.AddMouseButtonEvent(0, mouseDown);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(System.Numerics.Vector2.Zero, ImGuiCond.Always);
                ImGui.SetNextWindowSize(io.DisplaySize, ImGuiCond.Always);
                ImGui.Begin("Creative range numeric input");
                object?[] parameters = ["Scale", value, 0.01f, float.MinValue, float.MaxValue, null];
                changed |= (bool)editor.Invoke(null, parameters)!;
                value = (float)parameters[1]!;
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                if (input == default)
                    input = (min + max) * 0.5f;
                ImGui.End();
                ImGui.Render();
            }

            Frame();
            Frame();
            io.AddKeyEvent(ImGuiKey.ModCtrl, true);
            Frame(mouseDown: true);
            Frame();
            io.AddKeyEvent(ImGuiKey.ModCtrl, false);
            Frame();
            Check(io.WantTextInput, "Scale field enters numeric text editing");
            io.AddInputCharactersUTF8("-250");
            Frame();
            io.AddKeyEvent(ImGuiKey.Enter, true);
            Frame();
            io.AddKeyEvent(ImGuiKey.Enter, false);
            Frame();
            Near(-250f, value, "Typed negative scale is not clamped");
            Check(changed, "Typed scale entry reports an edit");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void CreativeRangeGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Creative range regression", null, null);
        Check(window != null, "OpenGL context");
        var previousTimeline = typeof(Timeline).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!.GetValue(null);
        var previousContext = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DeltaTime = 1f / 60f;
            glfw.MakeContextCurrent(window);
            using var gl = Silk.NET.OpenGL.GL.GetApi(glfw.GetProcAddress);
            const uint width = 96, height = 64;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var viewport = new Viewport { Gl = gl, OverlaysEnabled = false };
            viewport.InitFramebuffer(width, height);
            try
            {
                uint Field(string name) => (uint)typeof(Viewport).GetField(name, flags)!.GetValue(viewport)!;
                int Pixel(int x, int y) => (y * (int)width + x) * 4;
                byte[] source = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int p = Pixel(x, y);
                        source[p] = (byte)(x * 2);
                        source[p + 1] = (byte)(y * 3);
                        source[p + 2] = 128;
                        source[p + 3] = 255;
                    }
                byte[] Read(uint texture)
                {
                    byte[] pixels = new byte[source.Length];
                    gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, texture);
                    fixed (byte* data = pixels)
                        gl.GetTexImage(Silk.NET.OpenGL.GLEnum.Texture2D, 0,
                            Silk.NET.OpenGL.PixelFormat.Rgba, Silk.NET.OpenGL.PixelType.UnsignedByte, data);
                    return pixels;
                }
                var timeline = new Timeline();
                typeof(Timeline).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!.SetValue(null, timeline);
                var frameField = typeof(Timeline).GetField("_currentFrame", flags)!;
                void UploadSource()
                {
                    gl.ActiveTexture(Silk.NET.OpenGL.GLEnum.Texture0);
                    gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, Field("_colorTex"));
                    fixed (byte* data = source)
                        gl.TexSubImage2D(Silk.NET.OpenGL.GLEnum.Texture2D, 0, 0, 0, width, height,
                            Silk.NET.OpenGL.PixelFormat.Rgba, Silk.NET.OpenGL.PixelType.UnsignedByte, data);
                }

                var grainCamera = new CameraSceneObject();
                grainCamera.AddEffect(CameraEffectType.FilmGrain);
                var grainPass = typeof(Viewport).GetMethod("ApplyFilmGrainPass", flags)!;
                byte[] RenderGrain(float strength, int frame)
                {
                    frameField.SetValue(timeline, frame);
                    grainCamera.Effects[0].FilmGrain.Strength = strength;
                    grainCamera.Effects[0].FilmGrain.Size = 2f;
                    grainCamera.Effects[0].FilmGrain.Saturation = 0.5f;
                    UploadSource();
                    grainPass.Invoke(viewport,
                        [Field("_fbo"), Field("_colorTex"), Field("_glowFbo"), Field("_glowTex"), width, height, grainCamera]);
                    return Read(Field("_colorTex"));
                }

                byte[] grainBaseline = RenderGrain(0f, 0);
                byte[] darkened = RenderGrain(-0.75f, 0);
                bool anyDarker = false;
                for (int p = 0; p < darkened.Length; p += 4)
                    for (int channel = 0; channel < 3; channel++)
                    {
                        Check(darkened[p + channel] <= grainBaseline[p + channel], "Negative grain only darkens scene pixels");
                        anyDarker |= darkened[p + channel] < grainBaseline[p + channel];
                    }
                Check(anyDarker, "Negative grain changes rendered pixels");
                Check(darkened.SequenceEqual(RenderGrain(-0.75f, 0)), "Negative grain redraw is deterministic");
                Check(!darkened.SequenceEqual(RenderGrain(-0.75f, 8)), "Film grain advances with timeline frames");
                byte[] positive = RenderGrain(0.75f, 0);
                Check(positive.Where((_, i) => i % 4 != 3).Zip(grainBaseline.Where((_, i) => i % 4 != 3))
                    .Any(pair => pair.First > pair.Second), "Positive grain retains its brightening response");

                var heatCamera = new CameraSceneObject();
                heatCamera.AddEffect(CameraEffectType.HeatDistort);
                var heat = heatCamera.Effects[0].HeatDistort;
                var heatPass = typeof(Viewport).GetMethod("ApplyHeatDistortPass", flags)!;
                byte[] RenderHeat(float strength, int frame)
                {
                    frameField.SetValue(timeline, frame);
                    heat.Strength = strength;
                    heat.Speed = 1300f;
                    heat.Scale = 2000f;
                    UploadSource();
                    heatPass.Invoke(viewport,
                        [Field("_fbo"), Field("_glowFbo"), Field("_glowTex"), width, height, heatCamera]);
                    return Read(Field("_colorTex"));
                }

                byte[] ordinaryHeat = RenderHeat(100f, 12);
                byte[] expandedHeat = RenderHeat(250f, 12);
                Check(!ordinaryHeat.SequenceEqual(expandedHeat), "Heat distortion responds above its old percentage cap");
                Check(expandedHeat.SequenceEqual(RenderHeat(250f, 12)), "Expanded heat distortion is deterministic while paused");

                var bloomCamera = new CameraSceneObject();
                bloomCamera.AddEffect(CameraEffectType.Bloom);
                var bloom = bloomCamera.Effects[0].Bloom;
                bloom.SetValue("radius", -250f);
                bloom.SetValue("threshold", -50f);
                bloom.SetValue("anamorphic_ratio", -250f);
                var bloomPass = typeof(Viewport).GetMethod("ApplyBloomPass", flags)!;
                byte[] RenderBloom(float intensity)
                {
                    bloom.SetValue("intensity", intensity);
                    UploadSource();
                    bloomPass.Invoke(viewport,
                        [Field("_fbo"), Field("_colorTex"), Field("_glowFbo"), Field("_glowTex"),
                            Field("_glowPingFbo"), Field("_glowPingTex"), width, height, bloomCamera]);
                    return Read(Field("_colorTex"));
                }

                byte[] ordinaryBloom = RenderBloom(100f);
                byte[] expandedBloom = RenderBloom(250f);
                Check(!ordinaryBloom.SequenceEqual(expandedBloom), "Bloom intensity responds above its old percentage cap");
                Check(expandedBloom.SequenceEqual(RenderBloom(250f)), "Expanded bloom rendering is deterministic");
                Equal(Silk.NET.OpenGL.GLEnum.NoError, gl.GetError(), "Creative-range graphics OpenGL errors");
            }
            finally
            {
                viewport.DisposeFramebufferResources();
            }
        }
        finally
        {
            typeof(Timeline).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!.SetValue(null, previousTimeline);
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previousContext);
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}