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
            var output = origin + new System.Numerics.Vector2(28 + 180, 60 + 39) * UiTheme.Scale;
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
            const int columns = 3, rows = 2;
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
            CopyTile(baseline, 5);
            var rendered = new List<byte[]>();
            foreach (var preset in presets)
            {
                var graph = ShaderGraphPresets.Create(preset);
                byte[] pixels = Render(graph);
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
                    byte[] dielectric = Render(graph with { Links = graph.Links.RemoveAll(link => link.Target == 1 && link.Input == 2) });
                    Check(interior.Count(i => Math.Abs(pixels[i * 4] - dielectric[i * 4]) > 12) > interior.Length / 4,
                        $"{preset} Metallic socket changes real pixels");
                }
                byte[] quick = Render(graph, quality: false);
                Check(interior.Count(i => quick[i * 4 + 3] == 255 && !quick.AsSpan(i * 4, 3)
                    .SequenceEqual(baselineQuick.AsSpan(i * 4, 3))) > interior.Length / 2, $"{preset} renders in non-HQ preview");
                CopyTile(pixels, rendered.Count);
                rendered.Add(pixels);
                Check(baseline.SequenceEqual(Render(graph with { Enabled = false })), $"{preset} disable restores the standard material");
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
