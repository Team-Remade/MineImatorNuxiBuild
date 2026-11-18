using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.material;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.render;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static ShaderGraph WithOutput(ShaderGraph graph, string input, float x, float? y = null, float? z = null)
    {
        var output = graph.Nodes.Single(n => n.Kind == ShaderNodeKind.Output);
        return graph with { Nodes = graph.Nodes.Replace(output, ShaderGraph.WithInputValue(output, input, x, y, z)) };
    }
    private static void ShaderGraphPersistence(Fixture fixture)
    {
        var graph = ShaderGraphPresets.Create(ShaderGraphPreset.Magma);
        Equal<string?>(null, graph.ValidationError, "Preset is valid");
        var disconnectedCycle = graph with
        {
            Nodes = graph.Nodes.Add(new ShaderNode(40, ShaderNodeKind.Add, 0, 0))
                               .Add(new ShaderNode(41, ShaderNodeKind.Add, 0, 0)),
            Links = graph.Links.Add(new ShaderLink(40, 41, 0)).Add(new ShaderLink(41, 40, 0))
        };
        Check(disconnectedCycle.ValidationError?.Contains("cycle") == true, "Reject disconnected cycle");
        Check((graph with { Links = graph.Links.Add(graph.Links[0]) }).ValidationError != null, "Reject duplicate socket");
        Check((graph with { Links = graph.Links.Add(new ShaderLink(999, 1, 2)) }).ValidationError != null, "Reject dangling source");

        var obj = new SceneObject();
        obj.SetExplicitMaterialSettings();
        obj.MaterialSettings.CustomShader = graph;
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(obj);
        var (archive, _) = fixture.Roundtrip(obj);
        var loaded = archive.Root.CustomShader!;
        Check(loaded.Enabled && graph.Nodes.SequenceEqual(loaded.Nodes) && graph.Links.SequenceEqual(loaded.Links),
            "Archive keeps graph mode, node parameters, positions and links");
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectSceneObjectEntry)!;
        Check(legacy.CustomShader == null, "Old projects use the standard material");

        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", statics)!;
        var libraryClone = typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", statics)!;
        var library = (ProjectSceneObjectEntry)libraryClone.Invoke(null, [archive.Root])!;
        var copy = new SceneObject();
        apply.Invoke(null, [copy, library, null]);
        var duplicate = new SceneObject();
        duplicate.CopyMaterialSettingsFrom(copy);
        duplicate.MaterialSettings.CustomShader = loaded with { Enabled = false };
        Check(copy.MaterialSettings.CustomShader?.Enabled == true, "Editing a duplicate does not change the original");
        apply.Invoke(null, [duplicate, snapshot, null]);
        Check(duplicate.MaterialSettings.CustomShader?.Links.SequenceEqual(graph.Links) == true &&
            duplicate.MaterialSettings.CustomShader.Enabled, "Snapshot application restores graph and mode");

        // Socket values, dropdowns, toggles and multi-output links survive saving.
        var time = ShaderGraph.CreateNode(2, ShaderNodeKind.Time, 0, 0);
        var math = ShaderGraph.WithInputValue(ShaderGraph.CreateNode(3, ShaderNodeKind.Math, 200, 0) with
            { Mode = Array.IndexOf(ShaderGraph.MathOperations, "Sine"), Clamp = true }, "B", 0.25f);
        var noise = ShaderGraph.CreateNode(4, ShaderNodeKind.Noise, 200, 200);
        var surface = ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(
            ShaderGraph.CreateNode(1, ShaderNodeKind.Output, 500, 0), "IOR", 1.33f), "Transmission", 0.8f);
        var rich = new ShaderGraph
        {
            Enabled = true, Nodes = [surface, time, math, noise],
            Links = [new(2, 3, 0), new(3, 4, 5), new(4, 1, 0, 1), new(4, 1, ShaderGraph.OutputInput("Roughness"))]
        };
        Equal<string?>(null, rich.ValidationError, "Time, Math and multi-output graph is valid");
        var richObj = new SceneObject();
        richObj.SetExplicitMaterialSettings();
        richObj.MaterialSettings.CustomShader = rich;
        var richLoaded = fixture.Roundtrip(richObj).Item1.Root.CustomShader!;
        Check(rich.Nodes.SequenceEqual(richLoaded.Nodes) && rich.Links.SequenceEqual(richLoaded.Links),
            "Archive keeps socket values, modes, clamp and output socket indices");
        Check(ShaderGraph.InputValue(richLoaded.Nodes[0], ShaderGraph.OutputInput("IOR")).X == 1.33f,
            "Archive keeps the Principled IOR value");
        Check(rich.Program.Transmissive && rich.Program.Translucent, "Transmission routes the surface through refraction");
        Check((rich with { Links = [] }).Program.Transmissive, "Unlinked transmission value still transmits");
        var opaque = new ShaderGraph { Enabled = true, Nodes = [ShaderGraph.CreateNode(1, ShaderNodeKind.Output, 0, 0)] };
        Check(!opaque.Program.Translucent, "Default Principled output is opaque");
        Check((rich with { Links = rich.Links.Add(new ShaderLink(2, 1, ShaderGraph.OutputInput("Alpha"), 2)) }).ValidationError != null,
            "Reject links from output sockets a node does not have");

        // Color outputs feeding scalar inputs are converted to luminance; scalar sources are not.
        var data = rich.Program.Data;
        int instruction = 1, math0 = -1, roughness;
        for (int i = 0; i < rich.Program.Count; i++)
        {
            int kind = (int)data[instruction * 4], inputs = (int)data[instruction * 4 + 3] & 255;
            if (kind == (int)ShaderNodeKind.Math) math0 = (int)data[(instruction + 4) * 4];
            instruction += 4 + inputs;
        }
        roughness = (int)data[(rich.Program.OutputStart + ShaderGraph.OutputInput("Roughness")) * 4];
        Check(math0 is >= 0 and < 1024 && roughness is >= 0 and < 1024,
            "Scalar outputs feed scalar inputs without conversion");
        var colorToScalar = rich with { Links = [new(4, 1, ShaderGraph.OutputInput("Roughness"), 1)] };
        Check((int)colorToScalar.Program.Data[(colorToScalar.Program.OutputStart + ShaderGraph.OutputInput("Roughness")) * 4] >= 1024,
            "Color outputs feeding scalar inputs are converted");

        // Graphs saved before socket values existed keep their parameters.
        var legacyWave = ShaderGraph.Normalize(new ShaderNode(5, ShaderNodeKind.Wave, 0, 0, 17f, 4f));
        Check(Math.Abs(ShaderGraph.InputValue(legacyWave, 1).X - 0.85f) < 1e-6f &&
              ShaderGraph.InputValue(legacyWave, 2).X == 2f &&
              Math.Abs(ShaderGraph.InputValue(legacyWave, 6).X - MathF.PI / 2) < 1e-6f,
            "Legacy wave scale and distortion become equivalent socket values");
        var legacyMapping = ShaderGraph.Normalize(new ShaderNode(6, ShaderNodeKind.Mapping, 0, 0, 2f, 1f, 2f, 3f));
        Check(ShaderGraph.InputValue(legacyMapping, 1) == (1f, 2f, 3f) && ShaderGraph.InputValue(legacyMapping, 3) == (2f, 2f, 2f),
            "Legacy mapping offset and scale become Location and Scale");
        var legacyMix = ShaderGraph.Normalize(new ShaderNode(7, ShaderNodeKind.Mix, 0, 0));
        Check(ShaderGraph.InputValue(legacyMix, 0).X == 0 && ShaderGraph.InputValue(legacyMix, 2) == (1f, 1f, 1f),
            "Legacy mix keeps its unlinked factor and colors");
        Check(ShaderGraph.Normalize(new ShaderNode(8, ShaderNodeKind.Voronoi, 0, 0, 10f)).Mode == 3,
            "Legacy Voronoi keeps its cell-edge pattern");
        var legacyJson = """{"Enabled":true,"Nodes":[{"Id":1,"Kind":0,"X":0,"Y":0},{"Id":2,"Kind":5,"X":0,"Y":0,"A":8}],"Links":[{"Source":2,"Target":1,"Input":0}]}""";
        var legacyGraph = JsonSerializer.Deserialize(legacyJson, AppJsonContext.Default.ShaderGraph)!;
        Check(legacyGraph.ValidationError == null && legacyGraph.Links[0].Output == 0 &&
              ShaderGraph.InputValue(legacyGraph.Nodes[1], 1).X == 8f, "Old saved graph JSON loads and compiles");
    }

    private static unsafe void ShaderGraphControls(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(800, 600);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var obj = new SceneObject();
            var panel = new PropertiesPanel();
            var draw = typeof(PropertiesPanel).GetMethod("DrawShaderCanvas", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var graph = new ShaderGraph
            {
                Enabled = true,
                Nodes = [new(1, ShaderNodeKind.Output, 360, 60), new(2, ShaderNodeKind.Color, 28, 60, 0.4f, 0.5f, 0.3f)]
            };
            System.Numerics.Vector2 origin = default;
            var mouse = new System.Numerics.Vector2(30, 80);
            void Frame(bool left = false, bool right = false)
            {
                io.AddMousePosEvent(mouse.X, mouse.Y);
                io.AddMouseButtonEvent(0, left);
                io.AddMouseButtonEvent(1, right);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new(760, 550), ImGuiCond.Always);
                ImGui.Begin("Shader socket test", ImGuiWindowFlags.NoScrollbar);
                origin = ImGui.GetCursorScreenPos();
                graph = (ShaderGraph)draw.Invoke(panel, [obj, graph])!;
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            var output = origin + new System.Numerics.Vector2(28 + 200, 60 + 39) * UiTheme.Scale;
            var input = origin + new System.Numerics.Vector2(360, 60 + 39) * UiTheme.Scale;
            mouse = output;
            Frame(); Frame(left: true); Frame();
            mouse = input;
            Frame(); Frame(left: true); Frame();
            Check(graph.Links.SequenceEqual([new ShaderLink(2, 1, 0)]) &&
                ReferenceEquals(graph, obj.MaterialSettings.CustomShader), "Socket clicks create a live link");
            Frame(right: true);
            Check(graph.Links.IsEmpty && ReferenceEquals(graph, obj.MaterialSettings.CustomShader),
                "Right-click removes the input link");
            Frame();
            mouse = output;
            Frame(); Frame(left: true);
            mouse = input;
            Frame(left: true); Frame();
            Check(graph.Links.SequenceEqual([new ShaderLink(2, 1, 0)]), "Dragging from an output to an input links them");
            Frame(left: true);
            mouse = origin + new System.Numerics.Vector2(300, 420) * UiTheme.Scale;
            Frame(left: true); Frame();
            Check(graph.Links.IsEmpty && ReferenceEquals(graph, obj.MaterialSettings.CustomShader),
                "Dragging a wire off an input and releasing on empty canvas disconnects it");
            Frame();

            graph = new ShaderGraph
            {
                Enabled = true,
                Nodes = [ShaderGraph.CreateNode(1, ShaderNodeKind.Output, 360, 60), ShaderGraph.CreateNode(2, ShaderNodeKind.Noise, 28, 60)]
            };
            Frame();
            mouse = origin + new System.Numerics.Vector2(28 + 200, 60 + 28 + 22 * 1.5f) * UiTheme.Scale;
            Frame(); Frame(left: true); Frame();
            mouse = origin + new System.Numerics.Vector2(360, 60 + 28 + 22 * 3.5f) * UiTheme.Scale;
            Frame(); Frame(left: true); Frame();
            Check(graph.Links.SequenceEqual([new ShaderLink(2, 1, ShaderGraph.OutputInput("Emission"), 1)]),
                "A node's second output socket links its Color output to Emission");
            Frame();
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void ShaderGraphShortcuts(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(1280, 800);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            var obj = new SceneObject();
            obj.SetExplicitMaterialSettings();
            obj.MaterialSettings.CustomShader = new ShaderGraph
            {
                Enabled = true,
                Nodes = [new(1, ShaderNodeKind.Output, 360, 60), new(2, ShaderNodeKind.Color, 28, 60, 0.4f, 0.5f, 0.3f)],
                Links = [new(2, 1, 0)]
            };
            var panel = new PropertiesPanel();
            typeof(PropertiesPanel).GetField("_currentObject", instance)!.SetValue(panel, obj);
            typeof(PropertiesPanel).GetField("_shaderEditorOpen", instance)!.SetValue(panel, true);
            var render = typeof(PropertiesPanel).GetMethod("RenderShaderGraphEditor", instance)!;
            var selected = typeof(PropertiesPanel).GetField("_shaderSelected", instance)!;
            bool Frame(params ImGuiKey[] keys)
            {
                foreach (var key in keys) io.AddKeyEvent(key, true);
                ImGui.NewFrame();
                ImGui.SetNextWindowFocus();
                render.Invoke(panel, null);
                bool handled = panel.HandleShaderEditorShortcuts();
                ImGui.Render();
                foreach (var key in keys) io.AddKeyEvent(key, false);
                return handled;
            }
            Frame(); Frame();
            Check(panel.ShaderEditorHasFocus, "Focused shader editor owns edit shortcuts");

            selected.SetValue(panel, 2);
            Check(Frame(ImGuiKey.ModCtrl, ImGuiKey.D), "Duplicate shortcut is consumed by the editor");
            var graph = obj.MaterialSettings.CustomShader!;
            var copy = graph.Nodes.Single(n => n.Id == 3);
            Check(copy.Kind == ShaderNodeKind.Color && copy.A == 0.4f && (int)selected.GetValue(panel)! == 3,
                "Duplicate shortcut copies the selected node and selects it");
            Frame();

            Check(Frame(ImGuiKey.Delete), "Delete shortcut is consumed so scene objects are not deleted");
            graph = obj.MaterialSettings.CustomShader!;
            Check(graph.Nodes.Select(n => n.Id).SequenceEqual([1, 2]) && graph.Links.SequenceEqual([new ShaderLink(2, 1, 0)]),
                "Delete shortcut removes only the selected node");
            Frame();

            selected.SetValue(panel, 2);
            Frame(ImGuiKey.Delete);
            graph = obj.MaterialSettings.CustomShader!;
            Check(graph.Nodes.Length == 1 && graph.Links.IsEmpty, "Deleting a node removes its links");
            Frame();
            selected.SetValue(panel, 1);
            Check(Frame(ImGuiKey.Delete) && obj.MaterialSettings.CustomShader!.Nodes.Length == 1,
                "Material Output cannot be deleted");
            Frame();

            typeof(PropertiesPanel).GetField("_shaderEditorOpen", instance)!.SetValue(panel, false);
            Check(!Frame(ImGuiKey.Delete) && !panel.ShaderEditorHasFocus, "Closed editor leaves Delete to the scene");
            Frame();

            // Every node kind draws on the canvas and in the inspector.
            var kinds = Enum.GetValues<ShaderNodeKind>().Where(kind => kind != ShaderNodeKind.Output).ToArray();
            obj.MaterialSettings.CustomShader = new ShaderGraph
            {
                Enabled = true,
                Nodes = [ShaderGraph.CreateNode(1, ShaderNodeKind.Output, 0, 0),
                    ..kinds.Select((kind, i) => ShaderGraph.CreateNode(i + 2, kind, 260 * (i % 6), 300 * (i / 6)))]
            };
            Equal<string?>(null, obj.MaterialSettings.CustomShader.ValidationError, "A graph with every node kind is valid");
            typeof(PropertiesPanel).GetField("_shaderEditorOpen", instance)!.SetValue(panel, true);
            for (int id = 1; id <= kinds.Length + 1; id++)
            {
                selected.SetValue(panel, id);
                Frame();
            }
            Check(obj.MaterialSettings.CustomShader!.Nodes.Length == kinds.Length + 1, "Drawing every node kind leaves the graph unchanged");
            Frame();
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void ShaderGraphGraphics(string? contactSheet)
    {
        using var glfw = Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(WindowHintBool.Visible, false);
        glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
        const int size = 240;
        var window = glfw.CreateWindow(size, size, "Shader graph sphere regression", null, null);
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
            var viewport = new Viewport { Gl = gl, OverlaysEnabled = false };
            viewport.InitFramebuffer(size, size);
            var preview = new Viewport
            {
                Gl = gl, IsPreviewViewport = true, MainViewport = viewport, OverlaysEnabled = false
            };
            preview.InitPreviewViewport(size, size);
            viewport.PropertiesPanel = new PropertiesPanel
            {
                TransparentBackground = true, ShadowsEnabled = false, FogEnabled = false,
                ColorManagementEnabled = false, AntiAliasingEnabled = false, AmbientOcclusionEnabled = false,
                IndirectLightingEnabled = false
            };
            viewport.Camera.Distance = 1.24f;
            using var sphere = new SphereMesh(gl, segments: 72, rings: 48);
            var obj = new SceneObject { PivotOffset = vec3.Zero };
            obj.AddMesh(sphere);
            viewport.SceneObjects.Add(obj);
            obj.SetExplicitMaterialSettings();
            obj.MaterialSettings.AlbedoColor = new vec4(0.35f, 0.63f, 0.25f, 1f);
            byte[] Render(ShaderGraph? graph, bool quality = true)
            {
                obj.MaterialSettings.CustomShader = graph;
                obj.ApplyMaterialSettingsToMeshes();
                preview.RenderScenePublic(viewport.Camera, null, size, size, highQuality: quality);
                var pixels = new byte[size * size * 4];
                gl.BindTexture(GLEnum.Texture2D, preview.ColorTexture);
                fixed (byte* data = pixels) gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
                Equal(GLEnum.NoError, gl.GetError(), "Graph draw/readback GL status");
                return pixels;
            }

            var presets = Enum.GetValues<ShaderGraphPreset>();
            const int columns = 4, rows = 2;
            Check(presets.Length + 1 <= columns * rows, "Every preset fits on the contact sheet");
            var sheet = new byte[size * columns * size * rows * 4];
            byte[] baseline = Render(null);
            byte[] baselineQuick = Render(null, quality: false);
            var interior = Enumerable.Range(0, size * size).Where(i => baseline[i * 4 + 3] == 255).ToArray();
            Check(interior.Length > 10_000 && interior.All(i =>
                i % size > 12 && i % size < size - 12 && i / size > 12 && i / size < size - 12),
                "The full sphere fits inside its transparent tile");
            void CopyTile(byte[] pixels, int tile)
            {
                for (int y = 0; y < size; y++)
                    Array.Copy(pixels, (size - y - 1) * size * 4, sheet,
                        ((tile / columns * size + y) * size * columns + tile % columns * size) * 4, size * 4);
            }
            CopyTile(baseline, columns * rows - 1);
            int Differing(byte[] a, byte[] b, int threshold) => interior.Count(i => Math.Abs(a[i * 4] - b[i * 4]) +
                Math.Abs(a[i * 4 + 1] - b[i * 4 + 1]) + Math.Abs(a[i * 4 + 2] - b[i * 4 + 2]) >= threshold);
            var rendered = new List<byte[]>();
            foreach (var preset in presets)
            {
                var graph = ShaderGraphPresets.Create(preset);
                Equal<string?>(null, graph.ValidationError, $"{preset} is valid");
                byte[] pixels = Render(graph);
                if (graph.Program.Transmissive)
                    Check(interior.Count(i => pixels[i * 4 + 3] < 200) > interior.Length / 2,
                        $"{preset} is see-through over a transparent background");
                else
                    Check(pixels.Where((_, i) => i % 4 == 3).SequenceEqual(baseline.Where((_, i) => i % 4 == 3)),
                        $"{preset} keeps exact sphere coverage");
                Check(interior.Count(i => Math.Abs(pixels[i * 4] - baseline[i * 4]) +
                    Math.Abs(pixels[i * 4 + 1] - baseline[i * 4 + 1]) +
                    Math.Abs(pixels[i * 4 + 2] - baseline[i * 4 + 2]) >= 20) > interior.Length / 2,
                    $"{preset} changes the rendered surface");
                foreach (var other in rendered)
                    Check(interior.Count(i => Math.Abs(pixels[i * 4] - other[i * 4]) +
                        Math.Abs(pixels[i * 4 + 1] - other[i * 4 + 1]) +
                        Math.Abs(pixels[i * 4 + 2] - other[i * 4 + 2]) >= 16) > interior.Length / 8,
                        $"{preset} renders distinctly from previous presets");
                if (preset == ShaderGraphPreset.Water)
                {
                    Check(interior.Count(i => pixels[i * 4 + 2] > pixels[i * 4] + 20) > interior.Length / 2,
                        "Water is blue");
                    byte[] flat = Render(graph with { Links = graph.Links.RemoveAll(link => link.Target == 1 && link.Input == 4) });
                    Check(interior.Count(i => Math.Abs(flat[i * 4 + 2] - pixels[i * 4 + 2]) > 4) > interior.Length / 20,
                        "Water bump changes lighting without changing geometry");
                }
                if (preset == ShaderGraphPreset.Magma)
                {
                    byte[] noEmission = Render(graph with { Links = graph.Links.RemoveAll(link => link.Target == 1 && link.Input == 3) });
                    Check(interior.Count(i => pixels[i * 4] > noEmission[i * 4] + 20) > interior.Length / 10,
                        "Magma emission brightens the cracks");
                }
                if (preset is ShaderGraphPreset.BrushedSteel or ShaderGraphPreset.Copper)
                {
                    byte[] dielectric = Render(WithOutput(graph, "Metallic", 0));
                    Check(interior.Count(i => Math.Abs(pixels[i * 4] - dielectric[i * 4]) > 12) > interior.Length / 4,
                        $"{preset} Metallic value changes real pixels");
                }
                byte[] quick = Render(graph, quality: false);
                Check(interior.Count(i => (graph.Program.Transmissive || quick[i * 4 + 3] == 255) && !quick.AsSpan(i * 4, 3)
                    .SequenceEqual(baselineQuick.AsSpan(i * 4, 3))) > interior.Length / 2, $"{preset} renders in non-HQ preview");
                CopyTile(pixels, rendered.Count);
                rendered.Add(pixels);
                Check(baseline.SequenceEqual(Render(graph with { Enabled = false })), $"{preset} disable restores the standard material");
            }

            var plain = new ShaderGraph { Enabled = true, Nodes = [ShaderGraph.CreateNode(1, ShaderNodeKind.Output, 0, 0)] };
            // Each node kind executes on the GPU, including every output socket.
            foreach (var kind in Enum.GetValues<ShaderNodeKind>().Where(kind => kind != ShaderNodeKind.Output))
            {
                for (int output = 0; output < ShaderGraph.OutputCount(kind); output++)
                {
                    byte[] pixels = Render(plain with
                    {
                        Nodes = plain.Nodes.Add(ShaderGraph.CreateNode(2, kind, 0, 0)),
                        Links = [new(2, 1, 0, output)]
                    });
                    Check(pixels.Where((_, i) => i % 4 == 3).SequenceEqual(baseline.Where((_, i) => i % 4 == 3)),
                        $"{ShaderGraph.Label(kind)} output {output} renders with full coverage");
                }
            }
            var add = ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(ShaderGraph.CreateNode(2, ShaderNodeKind.Math, 0, 0), "A", 0.25f), "B", 0.5f);
            byte[] emissive = Render(WithOutput(plain with { Nodes = plain.Nodes.Add(add), Links = [new(2, 1, ShaderGraph.OutputInput("Emission"))] },
                "Base Color", 0f));
            byte[] unlit = Render(WithOutput(plain, "Base Color", 0f));
            Check(interior.Count(i => emissive[i * 4] > unlit[i * 4] + 60) > interior.Length * 9 / 10,
                "Math Add result drives emission");
            byte[] half = Render(WithOutput(plain, "Alpha", 0.5f));
            Check(interior.Count(i => half[i * 4 + 3] is > 10 and < 245) > interior.Length * 9 / 10,
                "Principled Alpha makes the surface partially transparent");

            // Time animates graphs from the timeline frame; the same frame always renders the same pixels.
            var timelineProperty = typeof(Timeline).GetProperty(nameof(Timeline.Instance))!;
            var previousTimeline = Timeline.Instance;
            try
            {
                var timeline = new Timeline();
                timelineProperty.SetValue(null, timeline);
                timeline.SetFrameRate(20);
                var frameField = typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var lava = ShaderGraphPresets.Create(ShaderGraphPreset.LavaFlow);
                frameField.SetValue(timeline, 0);
                byte[] start = Render(lava);
                frameField.SetValue(timeline, 60);
                byte[] later = Render(lava);
                Check(Differing(start, later, 20) > interior.Length / 4, "Time node animates Lava Flow across timeline frames");
                Check(later.SequenceEqual(Render(lava)), "Time-driven graphs are reproducible at a frame");
                var frameColor = new ShaderGraph
                {
                    Enabled = true,
                    Nodes = [ShaderGraph.CreateNode(1, ShaderNodeKind.Output, 0, 0), ShaderGraph.CreateNode(2, ShaderNodeKind.Time, 0, 0),
                        ShaderGraph.WithInputValue(ShaderGraph.CreateNode(3, ShaderNodeKind.Math, 0, 0) with
                            { Mode = Array.IndexOf(ShaderGraph.MathOperations, "Divide") }, "B", 100f)],
                    Links = [new(2, 3, 0, 1), new(3, 1, ShaderGraph.OutputInput("Emission"))]
                };
                byte[] frame60 = Render(frameColor);
                frameField.SetValue(timeline, 0);
                byte[] frame0 = Render(frameColor);
                Check(interior.Count(i => frame60[i * 4] > frame0[i * 4] + 40) > interior.Length * 9 / 10,
                    "Time Frame output follows the timeline frame");
            }
            finally { timelineProperty.SetValue(null, previousTimeline); }

            // Transmission refracts a checkered backdrop surrounding the scene.
            using var backdropMesh = new SphereMesh(gl, radius: 4f, segments: 48, rings: 32);
            var backdrop = new SceneObject { PivotOffset = vec3.Zero };
            backdrop.AddMesh(backdropMesh);
            backdrop.SetExplicitMaterialSettings();
            backdrop.MaterialSettings.DoubleSided = true;
            var checker = ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(
                ShaderGraph.CreateNode(2, ShaderNodeKind.Checker, 0, 0), "Scale", 6f), 1, 0.9f, 0.1f, 0.1f), 2, 0.1f, 0.2f, 0.9f);
            backdrop.MaterialSettings.CustomShader = new ShaderGraph
            {
                Enabled = true, Nodes = [ShaderGraph.CreateNode(1, ShaderNodeKind.Output, 0, 0), checker],
                Links = [new(2, 1, 0), new(2, 1, ShaderGraph.OutputInput("Emission"))]
            };
            backdrop.ApplyMaterialSettingsToMeshes();
            viewport.SceneObjects.Insert(0, backdrop);
            try
            {
                var clear = WithOutput(WithOutput(WithOutput(WithOutput(WithOutput(plain, "Transmission", 1), "IOR", 1),
                    "Specular", 0), "Roughness", 0.045f), "Base Color", 1);
                viewport.SceneObjects.Remove(obj);
                byte[] behind = Render(null);
                viewport.SceneObjects.Add(obj);
                byte[] passThrough = Render(clear);
                Check(interior.Count(i => passThrough[i * 4 + 3] == 255) == interior.Length,
                    "Transmission over an opaque backdrop stays opaque");
                Check(Differing(passThrough, behind, 40) < interior.Length / 10,
                    "IOR 1 transmission shows the scene behind unchanged");
                byte[] glass = Render(WithOutput(clear, "IOR", 1.5f));
                Check(Differing(glass, passThrough, 40) > interior.Length / 5, "IOR 1.5 refracts the scene behind");
                byte[] tinted = Render(WithOutput(clear, "Base Color", 0f, 1f, 0f));
                Check(interior.Count(i => tinted[i * 4 + 1] >= tinted[i * 4] && tinted[i * 4 + 1] >= tinted[i * 4 + 2]) >
                    interior.Length * 3 / 4, "Base Color tints transmitted light");
                byte[] solid = Render(WithOutput(clear, "Transmission", 0));
                Check(Differing(solid, passThrough, 40) > interior.Length / 2, "Transmission 0 restores an opaque surface");
            }
            finally
            {
                viewport.SceneObjects.Remove(backdrop);
                if (!viewport.SceneObjects.Contains(obj)) viewport.SceneObjects.Add(obj);
            }
            if (!string.IsNullOrWhiteSpace(contactSheet))
                new RenderExporter(preview).ExportImageFromRgb(size * columns, size * rows, "png", contactSheet, sheet, rgba: true);
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
