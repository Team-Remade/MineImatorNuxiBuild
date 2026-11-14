using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemade.core.mdl.material.materials;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static void MaterialColorPersistence(Fixture fixture)
    {
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", statics)!;
        var libraryClone = typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", statics)!;
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        var timeline = new Timeline();
        var set = typeof(Timeline).GetMethod("SetPropertyValue", instance)!;
        var get = typeof(Timeline).GetMethod("GetPropertyValue", instance)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", instance)!;
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectSceneObjectEntry)!;
        legacy.HasMaterialOverrides = true;
        foreach (bool cameraMaterial in new[] { false, true })
        {
            SceneObject obj = cameraMaterial ? new CameraSceneObject() : new SceneObject();
            obj.SetExplicitMaterialSettings();
            apply.Invoke(null, [obj, legacy, null]);
            Check((cameraMaterial ? ((CameraSceneObject)obj).ColorAdjustments
                : obj.MaterialSettings!.ColorAdjustments).IsNeutral, "Legacy colors remain neutral");
            obj.SetExplicitMaterialSettings();
            var value = new MaterialColorAdjustments
            {
                MultiplyHsv = new vec3(0.7f, 0.8f, 0.9f),
                AddRgb = new vec3(0.1f, 0.2f, 0.3f),
                AddHsv = new vec3(0.2f, 0.3f, 0.4f),
                SubtractRgb = new vec3(0.3f, 0.2f, 0.1f),
                SubtractHsv = new vec3(0.4f, 0.3f, 0.2f)
            };
            if (obj is CameraSceneObject camera) camera.ColorAdjustments = value;
            else obj.MaterialSettings!.ColorAdjustments = value;
            var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(obj);
            var json = JsonSerializer.Serialize(snapshot, AppJsonContext.Default.ProjectSceneObjectEntry);
            var saved = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectSceneObjectEntry)!;
            var (archive, _) = fixture.Roundtrip(obj);
            var library = (ProjectSceneObjectEntry)libraryClone.Invoke(null, [saved])!;
            var duplicate = (SceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate", instance)!
                .Invoke(new SceneTree(), [obj, new Dictionary<string, string>()])!;
            Equal(value, cameraMaterial ? ((CameraSceneObject)duplicate).ColorAdjustments
                : duplicate.MaterialSettings!.ColorAdjustments, "Duplicate keeps adjustments");
            foreach (var entry in new[] { saved, archive.Root, library })
            {
                SceneObject loaded = cameraMaterial ? new CameraSceneObject() : new SceneObject();
                apply.Invoke(null, [loaded, entry, null]);
                Equal(value, cameraMaterial ? ((CameraSceneObject)loaded).ColorAdjustments
                    : loaded.MaterialSettings!.ColorAdjustments, "Project/archive/library keeps adjustments");
            }
            library.ColorAdjustments.AddRgb.X = 1f;
            library.CameraColorAdjustments.AddRgb.X = 1f;
            Near(0.1f, (cameraMaterial ? saved.CameraColorAdjustments : saved.ColorAdjustments).AddRgb.X,
                "Library colors do not share storage");
            var paths = ((IEnumerable<string>)typeof(Timeline).GetMethod("GetKeyframePropertyPaths", statics)!
                .Invoke(null, [obj])!).ToArray();
            ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { obj }])!;
            ulong original = Hash();
            var panel = new PropertiesPanel { Timeline = timeline };
            typeof(PropertiesPanel).GetField("_currentObject", instance)!.SetValue(panel, obj);
            foreach (var (key, _, _) in MaterialColorAdjustments.Channels)
            foreach (string component in new[] { "r", "g", "b" })
            {
                string path = $"{(cameraMaterial ? "camera" : "material")}.{key}.{component}";
                Check(paths.Contains(path), "Timeline discovers " + path);
                Near(key == "multiply_hsv" ? 1f : 0f, Convert.ToSingle(typeof(PropertiesPanel)
                    .GetMethod("GetContextPropertyValue", instance)!.Invoke(panel, [path, true])), "Reset default " + path);
                set.Invoke(timeline, [obj, path, 1f]);
                Check(Hash() != original, "Dirty detection includes " + path);
                apply.Invoke(null, [obj, snapshot, null]);
                Equal(original, Hash(), "Undo restores " + path);
                var keys = new List<TimelineKeyframe>
                {
                    new() { Frame = 0, Value = 0f }, new() { Frame = 10, Value = 1f }
                };
                foreach (int frame in new[] { 0, 5, 10, 5, 0 })
                {
                    float sampled = Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, frame]));
                    set.Invoke(timeline, [obj, path, sampled]);
                    Near(frame / 10f, Convert.ToSingle(get.Invoke(timeline, [obj, path])), "Seek " + path);
                }
                set.Invoke(timeline, [obj, path, -1f]);
                Near(0f, Convert.ToSingle(get.Invoke(timeline, [obj, path])), "Lower bound " + path);
                set.Invoke(timeline, [obj, path, 2f]);
                Near(1f, Convert.ToSingle(get.Invoke(timeline, [obj, path])), "Upper bound " + path);
                apply.Invoke(null, [obj, snapshot, null]);
            }
        }
        var parent = new SceneObject();
        parent.SetExplicitMaterialSettings();
        parent.MaterialSettings!.ColorAdjustments = new()
        {
            MultiplyHsv = new vec3(0.5f), AddRgb = new vec3(0.2f), SubtractHsv = new vec3(0.1f)
        };
        var child = new SceneObject { UsePropertyInheritance = true, InheritColor = true };
        parent.AddChild(child);
        child.SetExplicitMaterialSettings();
        child.MaterialSettings!.ColorAdjustments = parent.MaterialSettings.ColorAdjustments;
        var effective = child.GetEffectiveMaterialSettings().ColorAdjustments;
        Equal(new vec3(0.25f), effective.MultiplyHsv, "Inherited multipliers multiply");
        Equal(new vec3(0.4f), effective.AddRgb, "Inherited additions accumulate");
        Equal(new vec3(0.2f), effective.SubtractHsv, "Inherited subtractions accumulate");
        child.InheritColor = false;
        Equal(child.MaterialSettings.ColorAdjustments, child.GetEffectiveMaterialSettings().ColorAdjustments,
            "Inherit Color disables inherited operations");
    }

    private static unsafe void MaterialColorControls(Fixture fixture)
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
            var first = new CameraSceneObject();
            var second = new CameraSceneObject();
            first.ColorAdjustments.AddHsv = second.ColorAdjustments.AddHsv = new vec3(0.37f, 0.25f, 0.75f);
            first.ColorAdjustments.SubtractHsv = second.ColorAdjustments.SubtractHsv = new vec3(0.63f, 0.4f, 0.8f);
            var panel = new PropertiesPanel();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PropertiesPanel).GetField("_currentObject", flags)!.SetValue(panel, first);
            var draw = typeof(PropertiesPanel).GetMethod("DrawCameraMaterialControls", flags)!;
            SelectionManager.Instance.SelectObject(first);
            SelectionManager.Instance.SelectObject(second);
            foreach (var (key, _, _) in MaterialColorAdjustments.Channels)
            {
                var target = new System.Numerics.Vector2();
                void Frame(bool click = false, bool rightClick = false, bool targetSwatch = false,
                    System.Numerics.Vector2? pickerTarget = null)
                {
                    var mouse = pickerTarget ?? target;
                    if (targetSwatch) mouse.X -= 30f;
                    io.AddMousePosEvent(mouse.X, mouse.Y);
                    io.AddMouseButtonEvent(0, click);
                    io.AddMouseButtonEvent(1, rightClick);
                    ImGui.NewFrame();
                    ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                    ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600), ImGuiCond.Always);
                    ImGui.Begin("Material controls");
                    var start = ImGui.GetCursorScreenPos();
                    float row = ImGui.GetFrameHeightWithSpacing();
                    int index = Array.FindIndex(MaterialColorAdjustments.Channels, channel => channel.Key == key);
                    target = new System.Numerics.Vector2(start.X + 40f, start.Y + row * (index + 1) + row * 0.4f);
                    draw.Invoke(panel, [first]);
                    Check(ImGui.GetCursorScreenPos().Y - start.Y >= row * 8f - 1f, "All five operations are visible");
                    ImGui.End();
                    ImGui.Render();
                }
                vec3 original = first.ColorAdjustments[key];
                Frame();
                Frame();
                Equal(original, first.ColorAdjustments[key], "Drawing picker preserves exact HSV values, including hue 1");
                Frame(rightClick: true);
                var paths = (string[])typeof(PropertiesPanel).GetField("_propertyContextPaths", flags)!.GetValue(panel)!;
                Check(paths.SequenceEqual(new[] { $"camera.{key}.r", $"camera.{key}.g", $"camera.{key}.b" }),
                    "Right-click targets all channels for " + key);
                Frame();
                if (!key.EndsWith("_hsv", StringComparison.Ordinal)) continue;
                Frame(click: true, targetSwatch: true);
                Frame(targetSwatch: true);
                Frame(targetSwatch: true);
                Check(context.OpenPopupStack.Size > 0, "HSV swatch opens picker for " + key);
                var popup = context.OpenPopupStack[context.OpenPopupStack.Size - 1].Window;
                Check(popup != null, "HSV picker has a window");
                var pickerTarget = popup->DC.CursorStartPos + new System.Numerics.Vector2(40f, 75f);
                Frame(click: true, pickerTarget: pickerTarget);
                Frame(pickerTarget: pickerTarget);
                Check(first.ColorAdjustments[key] != original, "HSV picker edits " + key);
                Near(original.x, first.ColorAdjustments[key].x, "Picking saturation/value preserves stored hue for " + key);
                Equal(first.ColorAdjustments[key], second.ColorAdjustments[key], "Camera multi-selection edits " + key);
                var outside = new System.Numerics.Vector2(570f, 550f);
                Frame(click: true, pickerTarget: outside);
                Frame(pickerTarget: outside);
                Frame();
                Check(context.OpenPopupStack.Size == 0, "Picker closes on an outside click");
            }
        }
        finally
        {
            SelectionManager.Instance.ClearSelection();
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void MaterialColorGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Material color regression", null, null);
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
            const uint size = 64;
            var viewport = new Viewport { Gl = gl, OverlaysEnabled = false };
            viewport.InitFramebuffer(size, size);
            var preview = new Viewport
            {
                Gl = gl, IsPreviewViewport = true, MainViewport = viewport, OverlaysEnabled = false
            };
            preview.InitPreviewViewport(size, size);
            try
            {
                viewport.PropertiesPanel = new PropertiesPanel
                {
                    TransparentBackground = true, ShadowsEnabled = false, FogEnabled = false,
                    ColorManagementEnabled = false, AntiAliasingEnabled = false,
                    AmbientOcclusionEnabled = false, IndirectLightingEnabled = false
                };
                using var cube = new CubeMesh(gl) { Unlit = true, Albedo = new vec3(0.8f, 0.4f, 0.2f) };
                var obj = new SceneObject();
                obj.Visuals.Add(cube);
                viewport.SceneObjects.Add(obj);
                var camera = new CameraSceneObject();
                var cases = new (MaterialColorAdjustments Value, vec3 Expected)[]
                {
                    (new(), new vec3(204, 102, 51)),
                    (new() { MultiplyHsv = new vec3(1f, 0.5f, 0.5f) }, new vec3(102, 77, 64)),
                    (new() { MultiplyHsv = new vec3(0f, 0f, 1f) }, new vec3(204, 204, 204)),
                    (new() { AddRgb = new vec3(0.1f, 0.2f, 0.3f) }, new vec3(230, 153, 128)),
                    (new() { AddHsv = new vec3(0.5f, 0.1f, 0.1f) }, new vec3(34, 164, 230)),
                    (new() { SubtractRgb = new vec3(0.1f, 0.5f, 0.3f) }, new vec3(179, 0, 0)),
                    (new() { SubtractHsv = new vec3(0.2f, 0.25f, 0.3f) }, new vec3(128, 64, 119)),
                    (new() { AddHsv = new vec3(1f, 0f, 0f) }, new vec3(204, 102, 51)),
                    (new() { AddHsv = new vec3(0f, 1f, 1f) }, new vec3(255, 85, 0)),
                    (new() { SubtractHsv = new vec3(0f, 1f, 1f) }, vec3.Zero),
                    (new()
                    {
                        MultiplyHsv = new vec3(1f, 0.5f, 0.5f), AddRgb = new vec3(0.1f, 0f, 0f),
                        AddHsv = new vec3(1f / 3f, 0f, 0f), SubtractRgb = new vec3(0f, 0.1f, 0f),
                        SubtractHsv = new vec3(0f, 0f, 0.1f)
                    }, new vec3(48, 77, 57))
                };
                byte[] Read(uint texture)
                {
                    byte[] pixels = new byte[size * size * 4];
                    gl.BindTexture(GLEnum.Texture2D, texture);
                    fixed (byte* data = pixels)
                        gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
                    return pixels;
                }
                void AssertColor(byte[] pixels, vec3 expected, string message)
                {
                    int pixel = -1;
                    for (int i = 3; i < pixels.Length; i += 4)
                        if (pixels[i] == 255) { pixel = i - 3; break; }
                    Check(pixel >= 0, "Rendered geometry exists");
                    for (int channel = 0; channel < 3; channel++)
                        Check(Math.Abs(pixels[pixel + channel] - expected[channel]) <= 2,
                            $"{message} channel {channel}: expected {expected[channel]}, got {pixels[pixel + channel]}");
                }
                foreach (bool cameraMaterial in new[] { false, true })
                foreach (var (value, expected) in cases)
                {
                    camera.ColorAdjustments = cameraMaterial ? value : new();
                    cube.ColorAdjustments = cameraMaterial ? new() : value;
                    foreach (var (quality, effects) in new[] { (false, false), (true, false), (false, true) })
                    {
                        preview.RenderScenePublic(viewport.Camera, camera, size, size,
                            highQuality: quality, applyCameraEffects: effects);
                        AssertColor(Read(preview.ColorTexture), expected, $"Camera={cameraMaterial}, quality={quality}, effects={effects}");
                    }
                }
                using var clone = cube.Clone();
                Equal(cube.ColorAdjustments, clone.ColorAdjustments, "Mesh clone keeps color operations");
                camera.ColorAdjustments = cases[^1].Value;
                camera.MixColor = new vec4(0f, 1f, 0f, 1f);
                preview.RenderScenePublic(viewport.Camera, camera, size, size);
                AssertColor(Read(preview.ColorTexture), new vec3(0, 255, 0), "Mix runs after all operations");

                // Exercise fractional alpha without relying on rasterized edge coverage.
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                uint Field(string name) => (uint)typeof(Viewport).GetField(name, flags)!.GetValue(viewport)!;
                byte[] input = new byte[size * size * 4];
                for (int i = 0; i < input.Length; i += 4)
                {
                    input[i] = 204; input[i + 1] = 102; input[i + 2] = 51; input[i + 3] = (byte)(i / 4 % 256);
                }
                camera.MixColor = vec4.Zero;
                gl.BindTexture(GLEnum.Texture2D, Field("_colorTex"));
                fixed (byte* data = input)
                    gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, size, size, GLEnum.Rgba, GLEnum.UnsignedByte, data);
                typeof(Viewport).GetMethod("ApplyCameraMaterialPass", flags)!.Invoke(viewport,
                    [Field("_fbo"), Field("_glowFbo"), Field("_glowTex"), size, size, camera]);
                byte[] output = Read(Field("_colorTex"));
                for (int i = 3; i < input.Length; i += 4)
                    Equal(input[i], output[i], "Stack preserves exact fractional alpha");
                string path = fixture.NewPath(".png");
                new MineImatorSimplyRemade.core.render.RenderExporter(preview)
                    .ExportImageFromRgb((int)size, (int)size, "png", path, output, rgba: true);
                using var stream = System.IO.File.OpenRead(path);
                var image = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
                Check(output.SequenceEqual(image.Data), "PNG export preserves adjusted colors and alpha");

                camera.ColorAdjustments = new();
                cube.Unlit = false;
                cube.PbrEnabled = true;
                foreach (var (value, expected) in cases)
                {
                    cube.Albedo = new vec3(0.8f, 0.4f, 0.2f);
                    cube.ColorAdjustments = value;
                    preview.RenderScenePublic(viewport.Camera, camera, size, size, highQuality: true);
                    byte[] adjusted = Read(preview.ColorTexture);
                    cube.Albedo = expected / 255f;
                    cube.ColorAdjustments = new();
                    preview.RenderScenePublic(viewport.Camera, camera, size, size, highQuality: true);
                    byte[] authored = Read(preview.ColorTexture);
                    Check(adjusted.Where((channel, i) => Math.Abs(channel - authored[i]) > 2).Count() == 0,
                        "PBR operations match equivalent authored display colors");
                }
                cube.Unlit = true;
                cube.PbrEnabled = false;
                cube.Albedo = new vec3(0.8f, 0.4f, 0.2f);
                using var device = Veldrid.GraphicsDevice.CreateD3D11(new Veldrid.GraphicsDeviceOptions(
                    debug: false, swapchainDepthFormat: null, syncToVerticalBlank: false,
                    resourceBindingModel: Veldrid.ResourceBindingModel.Improved,
                    preferDepthRangeZeroToOne: true, preferStandardClipSpaceYDirection: true));
                using var renderer = new VeldridSceneRenderer(device, gl);
                cube.DoubleSided = true;
                foreach (var (value, expected) in cases)
                {
                    cube.ColorAdjustments = value;
                    renderer.BeginFrame(size, size, mat4.Identity, mat4.Identity, System.Numerics.Vector4.Zero);
                    renderer.Draw(cube, mat4.Identity, forceUnshaded: true);
                    renderer.EndFrame(Field("_colorTex"));
                    AssertColor(Read(Field("_colorTex")), expected, "D3D11 material operations");
                }
                Equal(GLEnum.NoError, gl.GetError(), "Material operations have no GL errors");
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
