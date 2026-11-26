using System.Numerics;
using System.Text.Json;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemade.core.mdl.material;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static ShaderNode FogValue(ShaderNode node, string input, float x, float? y = null, float? z = null) =>
        ShaderGraph.WithInputValue(node, input, x, y ?? x, z ?? y ?? x);

    /// <summary>A Fog Volume Output graph; <paramref name="configure"/> edits the output node, links go to node id 1.</summary>
    private static ShaderGraph NuxTFogGraph(Func<ShaderNode, ShaderNode> configure,
        IEnumerable<ShaderNode>? nodes = null, IEnumerable<ShaderLink>? links = null) => new()
    {
        Enabled = true,
        Nodes = [configure(ShaderGraph.CreateNode(1, ShaderNodeKind.FogVolumeOutput, 0, 0)), ..nodes ?? []],
        Links = [..links ?? []]
    };

    private static void NuxTVolumeSettingsChecks()
    {
        NuxTSettings clamped = new NuxTSettings(VolumeStepSize: 99f, VolumeMaxSteps: 5000, VolumeLightSamples: -3).Normalized();
        NuxTAssert(clamped.VolumeStepSize == 2f && clamped.VolumeMaxSteps == 512 && clamped.VolumeLightSamples == 0,
            "Fog quality settings must clamp to their supported ranges.");
        NuxTSettings tiny = new NuxTSettings(VolumeStepSize: 0f, VolumeMaxSteps: 0, VolumeLightSamples: 99).Normalized();
        NuxTAssert(tiny.VolumeStepSize == 0.02f && tiny.VolumeMaxSteps == 8 && tiny.VolumeLightSamples == 32,
            "Fog quality settings must keep a minimum step size and step count.");
        NuxTAssert(new NuxTSettings(VolumeStepSize: float.NaN).Normalized().VolumeStepSize == NuxTSettings.DefaultVolumeStepSize,
            "A non-finite fog step size must fall back to the default.");
        // A record struct's parameterless constructor zeroes everything, so the defaults come from named arguments.
        NuxTSettings defaults = new NuxTSettings(Samples: 32);
        NuxTAssert(defaults.VolumeShadows && defaults.VolumeStepSize == NuxTSettings.DefaultVolumeStepSize &&
                   defaults.VolumeMaxSteps == NuxTSettings.DefaultVolumeMaxSteps &&
                   defaults.VolumeLightSamples == NuxTSettings.DefaultVolumeLightSamples &&
                   defaults.Normalized() == defaults.Normalized().Normalized(),
            "Fog quality defaults match the saved defaults and normalization is stable.");

        var manifest = new ProjectManifest();
        NuxTAssert(manifest.Settings.NuxTVolumeStepSize == 0.25f && manifest.Settings.NuxTVolumeMaxSteps == 96 &&
                   manifest.Settings.NuxTVolumeLightSamples == 4 && manifest.Settings.NuxTVolumeShadows,
            "Projects must start with the default fog quality.");
        manifest.Settings.NuxTVolumeStepSize = 0.125f;
        manifest.Settings.NuxTVolumeMaxSteps = 200;
        manifest.Settings.NuxTVolumeLightSamples = 9;
        manifest.Settings.NuxTVolumeShadows = false;
        string json = JsonSerializer.Serialize(manifest, AppJsonContext.Default.ProjectManifest);
        ProjectManifest restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectManifest)!;
        var panel = new PropertiesPanel();
        panel.LoadProjectSettingsFromManifest(restored);
        NuxTAssert(panel.NuxTVolumeStepSize == 0.125f && panel.NuxTVolumeMaxSteps == 200 &&
                   panel.NuxTVolumeLightSamples == 9 && !panel.NuxTVolumeShadows,
            "Fog quality settings must survive project serialization and load into the render settings panel.");
        var saved = new ProjectManifest();
        panel.WriteProjectSettingsToManifest(saved);
        NuxTAssert(saved.Settings.NuxTVolumeStepSize == 0.125f && saved.Settings.NuxTVolumeMaxSteps == 200 &&
                   saved.Settings.NuxTVolumeLightSamples == 9 && !saved.Settings.NuxTVolumeShadows,
            "The render settings panel must write fog quality back to project settings.");
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectManifest)!;
        NuxTAssert(legacy.Settings.NuxTVolumeMaxSteps == 96 && legacy.Settings.NuxTVolumeShadows,
            "Projects saved before fog settings existed must use the defaults.");

        // The renderer is an app preference rather than a project setting, so it must not depend on the project.
        NuxTAssert(!JsonSerializer.Deserialize("{}", AppJsonContext.Default.PreferencesState)!.UseNuxTRenderer,
            "Existing preferences keep the OpenGL renderer.");
        var state = new PreferencesState { UseNuxTRenderer = true };
        var restoredState = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(state, AppJsonContext.Default.PreferencesState),
            AppJsonContext.Default.PreferencesState)!;
        NuxTAssert(restoredState.UseNuxTRenderer, "The path tracer renderer choice must persist in the app preferences.");
        NuxTAssert(!new PreferencesPanel().UseNuxTRenderer, "The preferences panel defaults to the OpenGL renderer.");
        NuxTAssert(!JsonSerializer.Serialize(manifest, AppJsonContext.Default.ProjectManifest).Contains("UseNuxTRenderer",
                StringComparison.OrdinalIgnoreCase),
            "The renderer choice must not be stored in project files.");

        var viewport = new Viewport();
        NuxTAssert(viewport.UseNuxTRenderer == Viewport.StartWithNuxT, "A viewport starts with the saved renderer.");
        viewport.UseNuxTRenderer = true;
        NuxTAssert(viewport.UseNuxTRenderer, "The viewport must switch to the path tracer.");
        viewport.UseNuxTRenderer = false;
        NuxTAssert(!viewport.UseNuxTRenderer, "The viewport must switch back to OpenGL.");
    }

    private static void ShaderGraphFogVolume(Fixture fixture)
    {
        var spec = ShaderGraph.Spec(ShaderNodeKind.FogVolumeOutput);
        string[] expectedInputs =
        [
            "Color", "Extinction Color", "Density", "Scattering", "Absorption", "Anisotropy", "Edge Softness",
            "Emission Color", "Emission Strength", "Path Visibility"
        ];
        Check(spec.Inputs.Select(socket => socket.Name).SequenceEqual(expectedInputs), "Fog Volume Output exposes every requested input");
        Check(spec.Inputs[ShaderGraph.FogInput("Density")] is { Percent: true, Min: 0, Max: 1 },
            "Fog density is a 0 to 100 percent socket");
        Check(spec.Parameters.Select(parameter => parameter.Name).SequenceEqual(["Voxel Size", "Voxel Size Randomness", "Top Cutoff"]),
            "Fog Volume Output exposes voxel size, randomness and top cutoff");
        Check(spec.Parameters[1].Percent && spec.Parameters[2].Percent, "Randomness and cutoff are percentages");
        Check(spec.ClampLabel == "Ignore Object Scale" && !ShaderGraph.CreateNode(1, ShaderNodeKind.FogVolumeOutput, 0, 0).Clamp,
            "Fog Volume Output has an Ignore Object Scale option that starts off");
        float IgnoreScaleFlag(ShaderGraph graph) => graph.Program.Data[(graph.Program.OutputStart + expectedInputs.Length) * 4 + 3];
        Check(IgnoreScaleFlag(NuxTFogGraph(node => node)) == 0f && IgnoreScaleFlag(NuxTFogGraph(node => node with { Clamp = true })) == 1f,
            "Ignore Object Scale is packed into the volume settings the shaders read");
        Check(ShaderGraph.IsOutput(ShaderNodeKind.FogVolumeOutput) && ShaderGraph.IsOutput(ShaderNodeKind.Output) &&
              !ShaderGraph.IsOutput(ShaderNodeKind.Path), "Both output kinds are outputs and the Path node is not");

        var preset = ShaderGraphPresets.CreateFogVolume();
        Equal<string?>(null, preset.ValidationError, "The fog volume starter graph is valid");
        Check(preset.Program.Volume && preset.Program.Translucent && (preset.Program.Flags & ShaderGraphProgram.VolumeOutput) != 0,
            "A Fog Volume Output compiles to a translucent volume");
        Check(!ShaderGraphPresets.Create(ShaderGraphPreset.Water).Program.Volume, "Surface graphs are not volumes");
        Equal<string?>(null, NuxTFogGraph(node => node).ValidationError, "A lone Fog Volume Output is valid");

        // Density accepts a texture.
        var image = ShaderGraph.CreateNode(2, ShaderNodeKind.ImageTexture, 0, 0);
        var textured = NuxTFogGraph(node => node, [image], [new(2, 1, ShaderGraph.FogInput("Density"), 0)]);
        Equal<string?>(null, textured.ValidationError, "An image texture can drive fog density");
        Check(textured.Program.Count == 1, "The density texture node is part of the compiled graph");

        // A graph has one output; swapping keeps the other nodes and drops links into the old output.
        var magma = ShaderGraphPresets.Create(ShaderGraphPreset.Magma);
        var swapped = ShaderGraph.WithOutputKind(magma, ShaderNodeKind.FogVolumeOutput);
        Check(swapped.Nodes.Count(node => ShaderGraph.IsOutput(node.Kind)) == 1 &&
              swapped.Nodes.Single(node => ShaderGraph.IsOutput(node.Kind)).Kind == ShaderNodeKind.FogVolumeOutput &&
              swapped.Nodes.Length == magma.Nodes.Length && swapped.Links.All(link => link.Target != 1) &&
              swapped.Links.Length < magma.Links.Length,
            "Replacing the output keeps the other nodes and drops links into the old output");
        Equal<string?>(null, swapped.ValidationError, "A graph with a replaced output is valid");
        Check(ShaderGraph.WithOutputKind(swapped, ShaderNodeKind.Output).Nodes
                .Single(node => ShaderGraph.IsOutput(node.Kind)).Kind == ShaderNodeKind.Output,
            "The output can be swapped back to the principled material");
        Check(ReferenceEquals(swapped, ShaderGraph.WithOutputKind(swapped, ShaderNodeKind.FogVolumeOutput)),
            "Choosing the current output kind changes nothing");
        Check((swapped with { Nodes = swapped.Nodes.Add(ShaderGraph.CreateNode(60, ShaderNodeKind.Output, 0, 0)) }).ValidationError != null,
            "Two outputs in one graph are rejected");

        // Path nodes: slots follow the distinct targets and the number of paths is bounded.
        ShaderNode PathNode(int id, string? target) => ShaderGraph.CreateNode(id, ShaderNodeKind.Path, 0, 0) with { Target = target };
        var pathGraph = NuxTFogGraph(node => node, [PathNode(2, "a"), PathNode(3, "b"), PathNode(4, "a")],
            [new(2, 1, ShaderGraph.FogInput("Path Visibility"), 0), new(3, 1, ShaderGraph.FogInput("Density"), 0)]);
        Equal<string?>(null, pathGraph.ValidationError, "Path nodes feed fog inputs");
        Check(pathGraph.Program.PathTargets.Order().SequenceEqual(["a", "b"]),
            "Path nodes compile one slot per distinct path");
        var unused = NuxTFogGraph(node => node, [PathNode(2, "a")], []);
        Check(unused.Program.PathTargets.Length == 0, "Path nodes that do not reach the output are not compiled");
        var tooMany = NuxTFogGraph(node => node,
            Enumerable.Range(0, ShaderGraph.MaxPaths + 1).Select(i => PathNode(2 + i, $"path{i}")),
            Enumerable.Range(0, ShaderGraph.MaxPaths + 1).Select(i => new ShaderLink(2 + i, 1, i, 0)));
        Check(tooMany.ValidationError?.Contains("path") == true, "A graph cannot follow more than the supported number of paths");

        var resolved = NuxTShaderGraph.Create(pathGraph, Vector3.Zero, Vector3.One, 0f, 0f,
            id => id == "a" ? [new Vector3(0, 0, 0), new Vector3(1, 0, 0)] : null);
        int slotA = Array.IndexOf(pathGraph.Program.PathTargets, "a");
        Check(resolved is { IsVolume: true } && resolved.Paths.Count == 2 && slotA >= 0 &&
              resolved.Paths[slotA]?.Length == NuxTShaderGraph.PathSamples && resolved.Paths[1 - slotA] == null,
            "The tracer resolves known paths into their slots and leaves missing ones unset");
        Check(NuxTShaderGraph.Create(pathGraph with { Enabled = false }, Vector3.Zero, Vector3.One, 0f, 0f, _ => null) == null &&
              NuxTShaderGraph.Create(null, Vector3.Zero, Vector3.One, 0f, 0f, _ => null) == null &&
              NuxTShaderGraph.Create(pathGraph with { Links = [new(9, 1, 0)] }, Vector3.Zero, Vector3.One, 0f, 0f, _ => null) == null,
            "Disabled, absent or invalid graphs are not sent to the tracer");
        var same = NuxTShaderGraph.Create(pathGraph, Vector3.Zero, Vector3.One, 0f, 0f,
            id => id == "a" ? [new Vector3(0, 0, 0), new Vector3(1, 0, 0)] : null);
        var moved = NuxTShaderGraph.Create(pathGraph, Vector3.Zero, Vector3.One, 0f, 0f,
            id => id == "a" ? [new Vector3(0, 0, 0), new Vector3(2, 0, 0)] : null);
        Check(resolved!.Equals(same) && resolved.GetHashCode() == same!.GetHashCode() && !resolved.Equals(moved),
            "Tracer graphs compare by content so unchanged scenes keep their fingerprint");

        // Path resampling is evenly spaced along the length.
        Vector3[] polyline = [Vector3.Zero, new Vector3(10, 0, 0), new Vector3(10, 10, 0)];
        var samples = NuxTShaderGraph.ResamplePath(polyline);
        float spacing = 20f / (NuxTShaderGraph.PathSamples - 1);
        float minimum = float.MaxValue, maximum = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            float step = Vector3.Distance(samples[i - 1], samples[i]);
            minimum = Math.Min(minimum, step);
            maximum = Math.Max(maximum, step);
        }
        Check(samples.Length == NuxTShaderGraph.PathSamples && samples[0] == Vector3.Zero &&
              Vector3.Distance(samples[^1], new Vector3(10, 10, 0)) < 1e-4f && maximum <= spacing + 1e-3f && minimum > spacing * 0.7f,
            "Path resampling keeps the endpoints and spaces samples evenly");
        Check(NuxTShaderGraph.ResamplePath([new Vector3(1, 2, 3)]).All(point => point == new Vector3(1, 2, 3)),
            "A single-point path resamples to that point");

        // Saved graphs keep outputs, parameters, percent values and path targets.
        var saved = NuxTFogGraph(node => FogValue(FogValue(FogValue(node with { A = 1.5f, B = 0.4f, C = 0.75f },
                "Density", 0.6f), "Anisotropy", 0.3f), "Emission Color", 1f, 0.5f, 0.25f),
            [PathNode(2, "path-id")], [new(2, 1, ShaderGraph.FogInput("Path Visibility"), 0)]);
        var obj = new SceneObject();
        obj.SetExplicitMaterialSettings();
        obj.MaterialSettings.CustomShader = saved;
        var loaded = fixture.Roundtrip(obj).Item1.Root.CustomShader!;
        Check(saved.Nodes.SequenceEqual(loaded.Nodes) && saved.Links.SequenceEqual(loaded.Links) &&
              loaded.Nodes.Single(node => node.Kind == ShaderNodeKind.Path).Target == "path-id" &&
              loaded.Nodes.Single(node => node.Kind == ShaderNodeKind.FogVolumeOutput) is { A: 1.5f, B: 0.4f, C: 0.75f } &&
              Math.Abs(ShaderGraph.InputValue(loaded.Nodes.Single(node => node.Kind == ShaderNodeKind.FogVolumeOutput),
                  ShaderGraph.FogInput("Density")).X - 0.6f) < 1e-6f,
            "Archive keeps the fog output, its settings and the followed path");
        obj.MaterialSettings.CustomShader = NuxTFogGraph(node => node with { Clamp = true });
        var loadedIgnoring = fixture.Roundtrip(obj).Item1.Root.CustomShader!;
        Check(loadedIgnoring.Nodes.Single(node => node.Kind == ShaderNodeKind.FogVolumeOutput).Clamp &&
              IgnoreScaleFlag(loadedIgnoring) == 1f && IgnoreScaleFlag(loaded) == 0f,
            "Archive keeps Ignore Object Scale and old fog outputs stay scaled");
        var legacy = JsonSerializer.Deserialize("{\"Id\":3,\"Kind\":41,\"X\":0,\"Y\":0}", AppJsonContext.Default.ShaderNode);
        Check(legacy is { Kind: ShaderNodeKind.Path, Target: null }, "A Path node without a target loads without one");
    }

    private static NuxTTriangle[] NuxTCube(Vector3 min, Vector3 max, NuxTMaterial material)
    {
        var triangles = new List<NuxTTriangle>();
        Vector3 P(int x, int y, int z) => new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
        void Face(Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            triangles.Add(new NuxTTriangle(a, b, c, normal, normal, normal, Vector2.Zero, Vector2.UnitX, Vector2.One, material));
            triangles.Add(new NuxTTriangle(a, c, d, normal, normal, normal, Vector2.Zero, Vector2.One, Vector2.UnitY, material));
        }
        Face(Vector3.UnitZ, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1));
        Face(-Vector3.UnitZ, P(1, 0, 0), P(0, 0, 0), P(0, 1, 0), P(1, 1, 0));
        Face(Vector3.UnitX, P(1, 0, 1), P(1, 0, 0), P(1, 1, 0), P(1, 1, 1));
        Face(-Vector3.UnitX, P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0));
        Face(Vector3.UnitY, P(0, 1, 1), P(1, 1, 1), P(1, 1, 0), P(0, 1, 0));
        Face(-Vector3.UnitY, P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1));
        return [..triangles];
    }

    private static unsafe void NuxTShaderGraphGraphics(GL gl)
    {
        const int width = 16, height = 16;
        var camera = new NuxTCamera(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, Vector3.UnitY, 0.2f);
        var settings = new NuxTSettings(8, 0, 1f, 1f, 5);
        var cubeMin = new Vector3(-1f, -1f, -1.5f);
        var cubeMax = new Vector3(1f, 1f, -0.5f);
        var cubeSize = cubeMax - cubeMin;
        var wall = NuxTTriangleAtZ(-3f, new NuxTMaterial(Vector3.One, Vector3.One, Unlit: true));

        NuxTMaterial GraphMaterial(ShaderGraph graph, Func<string, IReadOnlyList<Vector3>?>? paths = null) =>
            new(Vector3.One, Vector3.Zero, ShaderGraph: NuxTShaderGraph.Create(graph, cubeMin, cubeSize, 0f, 0f,
                paths ?? (_ => null)) ?? throw new InvalidOperationException(graph.ValidationError ?? "Graph is disabled."));

        byte[] Render(IEnumerable<NuxTTriangle> triangles, NuxTSettings? overrideSettings = null, Vector3 ambient = default,
            IEnumerable<NuxTLight>? lights = null) =>
            RenderScene(new NuxTScene(triangles, Vector3.Zero, lights, ambientLight: ambient), overrideSettings);

        byte[] RenderScene(NuxTScene scene, NuxTSettings? overrideSettings = null)
        {
            using var renderer = new NuxTComputeRenderer(gl);
            NuxTAssert(renderer.Render(scene, camera, width, height, overrideSettings ?? settings, completeImmediately: true),
                $"GPU tracing must render the shader graph scene: {renderer.Status}.");
            NuxTAssert(renderer.Status.Contains("GPU", StringComparison.Ordinal), $"Shader graph scenes must trace on the GPU: {renderer.Status}.");
            byte[] pixels = new byte[width * height * 4];
            gl.BindTexture(GLEnum.Texture2D, renderer.OutputTexture);
            fixed (byte* pointer = pixels)
                gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
            gl.BindTexture(GLEnum.Texture2D, 0);
            NuxTAssert(gl.GetError() == GLEnum.NoError, "Shader graph tracing must leave OpenGL error-free.");
            return pixels;
        }

        // The tracer tone maps with x / (1 + x) before the sRGB encoding; undo both to compare radiance. The bright
        // unlit wall has radiance 2 and a fully emissive channel 1.
        float Channel(byte[] pixels, int x, int y, int channel)
        {
            float encoded = pixels[(y * width + x) * 4 + channel] / 255f;
            float linear = encoded <= 0.04045f ? encoded / 12.92f : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
            linear = Math.Min(linear, 0.999f);
            return linear / (1f - linear);
        }
        float Luma(byte[] pixels, int x, int y) => (Channel(pixels, x, y, 0) + Channel(pixels, x, y, 1) + Channel(pixels, x, y, 2)) / 3f;
        const float Bright = 1.8f, Dark = 0.05f;
        IEnumerable<NuxTTriangle> Cube(ShaderGraph graph, Func<string, IReadOnlyList<Vector3>?>? paths = null) =>
            NuxTCube(cubeMin, cubeMax, GraphMaterial(graph, paths));
        ShaderNode Dense(ShaderNode node) => FogValue(FogValue(FogValue(FogValue(FogValue(node,
            "Color", 0f), "Extinction Color", 0f), "Density", 1f), "Scattering", 0f), "Absorption", 8f) with { C = 1f };
        ShaderNode Sharp(ShaderNode node) => FogValue(node, "Edge Softness", 0f);

        // A custom surface graph shades the path-traced hit: emissive green on a surface that is otherwise black.
        var surface = new ShaderGraph
        {
            Enabled = true,
            Nodes = [ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(
                ShaderGraph.CreateNode(1, ShaderNodeKind.Output, 0, 0), "Base Color", 0f, 0f, 0f), "Emission", 0f, 1f, 0f)]
        };
        var plainPlane = new NuxTMaterial(Vector3.One, Vector3.Zero);
        byte[] plain = Render([NuxTTriangleAtZ(0f, plainPlane)]);
        byte[] graphPlane = Render([NuxTTriangleAtZ(0f, GraphMaterial(surface))]);
        NuxTAssert(Luma(plain, 8, 8) < 0.02f, "The standard unlit-by-lights surface must trace black.");
        NuxTAssert(Math.Abs(Channel(graphPlane, 8, 8, 1) - 1f) < 0.1f && Channel(graphPlane, 8, 8, 0) < 0.02f && Channel(graphPlane, 8, 8, 2) < 0.02f,
            $"A surface shader graph must drive the path-traced color (got {Channel(graphPlane, 8, 8, 0)},{Channel(graphPlane, 8, 8, 1)},{Channel(graphPlane, 8, 8, 2)}).");

        // Procedural nodes execute with object-space positions.
        var checker = ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(
            ShaderGraph.CreateNode(2, ShaderNodeKind.Checker, 0, 0), "Scale", 40f), 1, 0.9f, 0.1f, 0.1f), 2, 0.1f, 0.2f, 0.9f);
        var checkerGraph = new ShaderGraph
        {
            Enabled = true,
            Nodes = [ShaderGraph.WithInputValue(ShaderGraph.CreateNode(1, ShaderNodeKind.Output, 0, 0), "Base Color", 0f, 0f, 0f), checker],
            Links = [new(2, 1, ShaderGraph.OutputInput("Emission"))]
        };
        byte[] checkered = Render([NuxTTriangleAtZ(0f, GraphMaterial(checkerGraph))]);
        int reds = 0, blues = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (Channel(checkered, x, y, 0) > 0.5f && Channel(checkered, x, y, 2) < 0.1f) reds++;
            if (Channel(checkered, x, y, 2) > 0.5f && Channel(checkered, x, y, 0) < 0.1f) blues++;
        }
        NuxTAssert(reds > width * height / 6 && blues > width * height / 6,
            $"A procedural Checker node must vary across the path-traced surface (red {reds}, blue {blues}).");

        // Fog volumes scatter and emit light.
        var glowGraph = NuxTFogGraph(node => FogValue(FogValue(FogValue(FogValue(node, "Density", 1f),
            "Emission Color", 1f, 0f, 0f), "Emission Strength", 4f), "Scattering", 0f));
        byte[] glow = Render(Cube(glowGraph));
        NuxTAssert(Channel(glow, 8, 8, 0) > 0.5f && Channel(glow, 8, 8, 1) < Dark && Channel(glow, 8, 8, 2) < Dark,
            $"Emissive fog must glow in its emission color (got {Channel(glow, 8, 8, 0)},{Channel(glow, 8, 8, 1)},{Channel(glow, 8, 8, 2)}).");
        byte[] reference = Render([wall]);
        NuxTAssert(Luma(reference, 1, 1) > Bright && Luma(reference, 14, 14) > Bright,
            $"The reference wall must be fully bright (got {Luma(reference, 1, 1)}).");
        byte[] ambientFog = Render(Cube(NuxTFogGraph(node => FogValue(FogValue(node, "Density", 1f), "Scattering", 1f))),
            ambient: Vector3.One);
        NuxTAssert(Luma(ambientFog, 8, 8) > 0.5f, $"Scattering fog must be lit by ambient light (got {Luma(ambientFog, 8, 8)}).");
        NuxTAssert(Luma(Render(Cube(NuxTFogGraph(node => FogValue(FogValue(node, "Density", 1f), "Scattering", 1f)))), 8, 8) < 0.02f,
            "Scattering fog without light stays dark.");

        // Absorption hides what is behind the volume; extinction color tints what gets through.
        byte[] dimmed = Render(Cube(NuxTFogGraph(Dense)).Append(wall));
        NuxTAssert(Luma(dimmed, 8, 8) < Dark, $"Dense absorbing fog must hide a bright wall (got {Luma(dimmed, 8, 8)}).");
        var thin = NuxTFogGraph(node => FogValue(FogValue(FogValue(FogValue(FogValue(node, "Color", 0f), "Extinction Color", 1f, 0f, 0f),
            "Density", 0.25f), "Scattering", 0f), "Absorption", 1f));
        byte[] tinted = Render(Cube(thin).Append(wall));
        NuxTAssert(Channel(tinted, 8, 8, 0) > Bright && Channel(tinted, 8, 8, 0) > Channel(tinted, 8, 8, 1) * 1.5f &&
                   Channel(tinted, 8, 8, 0) > Channel(tinted, 8, 8, 2) * 1.5f,
            $"A red extinction color must let red light through (got {Channel(tinted, 8, 8, 0)},{Channel(tinted, 8, 8, 1)},{Channel(tinted, 8, 8, 2)}).");
        byte[] empty = Render(Cube(NuxTFogGraph(node => FogValue(node, "Density", 0f))).Append(wall));
        NuxTAssert(Luma(empty, 8, 8) > Bright, "Zero-density fog must leave the scene unchanged.");

        // Top cutoff trims the volume at a fraction of its height.
        byte[] noFog = Render(Cube(NuxTFogGraph(node => Sharp(Dense(node)) with { C = 0f })).Append(wall));
        NuxTAssert(Luma(noFog, 8, 8) > Bright, "A top cutoff of zero removes the volume.");
        byte[] halfFog = Render(Cube(NuxTFogGraph(node => Sharp(Dense(node)) with { C = 0.5f })).Append(wall));
        NuxTAssert(Luma(halfFog, 8, 2) < Dark && Luma(halfFog, 8, 13) > Bright,
            $"A top cutoff of 50% keeps only the lower half (bottom {Luma(halfFog, 8, 2)}, top {Luma(halfFog, 8, 13)}).");

        // Density can come from the graph; voxel size snaps it to a grid.
        var densityNodes = new[]
        {
            ShaderGraph.CreateNode(2, ShaderNodeKind.TextureCoordinate, 0, 0), ShaderGraph.CreateNode(3, ShaderNodeKind.SeparateXyz, 0, 0)
        };
        ShaderLink[] densityLinks = [new(2, 3, 0, 3), new(3, 1, ShaderGraph.FogInput("Density"), 0)];
        byte[] smooth = Render(Cube(NuxTFogGraph(node => Sharp(Dense(node)) with { A = 0f }, densityNodes, densityLinks)).Append(wall));
        byte[] voxels = Render(Cube(NuxTFogGraph(node => Sharp(Dense(node)) with { A = 2f }, densityNodes, densityLinks)).Append(wall));
        NuxTAssert(Luma(voxels, 2, 8) > Bright && Luma(voxels, 13, 8) < Dark && Luma(voxels, 10, 8) < Dark,
            $"A large voxel size must give each voxel one density (left {Luma(voxels, 2, 8)}, right {Luma(voxels, 13, 8)}, near center {Luma(voxels, 10, 8)}).");
        NuxTAssert(Luma(smooth, 10, 8) > 0.1f && Luma(smooth, 2, 8) > Bright,
            $"Voxel size zero must keep the density smooth (near center {Luma(smooth, 10, 8)}, left {Luma(smooth, 2, 8)}).");

        // A Path node limits where the fog is visible.
        ShaderGraph PathFog(float start, float end, string target) => NuxTFogGraph(node => Sharp(Dense(node)),
            [ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(
                ShaderGraph.CreateNode(2, ShaderNodeKind.Path, 0, 0) with { Target = target }, "Radius", 1f), "Falloff", 0f),
                "Start", start), "End", end)],
            [new(2, 1, ShaderGraph.FogInput("Path Visibility"), 0)]);
        IReadOnlyList<Vector3>? Line(string id) => id switch
        {
            "line" => [new Vector3(-5f, 0f, -1f), new Vector3(5f, 0f, -1f)],
            "far" => [new Vector3(10f, 10f, 10f), new Vector3(11f, 10f, 10f)],
            _ => null
        };
        byte[] onPath = Render(Cube(PathFog(0f, 1f, "line"), Line).Append(wall));
        byte[] offPath = Render(Cube(PathFog(0f, 1f, "far"), Line).Append(wall));
        byte[] missing = Render(Cube(PathFog(0f, 1f, "missing"), Line).Append(wall));
        NuxTAssert(Luma(onPath, 8, 8) < Dark, "Fog on the followed path must be visible.");
        NuxTAssert(Luma(offPath, 8, 8) > Bright, "Fog away from the followed path must be hidden.");
        NuxTAssert(Luma(missing, 8, 8) < Dark, "A path missing from the scene must leave the whole volume visible.");
        byte[] outsideWindow = Render(Cube(PathFog(0.8f, 1f, "line"), Line).Append(wall));
        byte[] insideWindow = Render(Cube(PathFog(0.4f, 0.6f, "line"), Line).Append(wall));
        NuxTAssert(Luma(outsideWindow, 8, 8) > Bright && Luma(insideWindow, 8, 8) < Dark,
            $"The Path node's Start and End must limit the followed section (outside {Luma(outsideWindow, 8, 8)}, inside {Luma(insideWindow, 8, 8)}).");

        // Anisotropy: a light behind the fog is seen through it by forward scattering only.
        var behindLight = new[] { new NuxTLight(new Vector3(0f, 0f, -2.5f), Vector3.UnitZ, Vector3.One, 3f, 12f) };
        ShaderGraph Scatter(float anisotropy) => NuxTFogGraph(node => FogValue(FogValue(FogValue(FogValue(
            node, "Density", 0.5f), "Scattering", 1f), "Anisotropy", anisotropy), "Edge Softness", 0f));
        byte[] forward = Render(Cube(Scatter(0.8f)), lights: behindLight);
        byte[] backward = Render(Cube(Scatter(-0.8f)), lights: behindLight);
        byte[] unlit = Render(Cube(Scatter(0.8f)));
        NuxTAssert(Luma(forward, 8, 8) > 0.5f && Luma(forward, 8, 8) > Luma(backward, 8, 8) * 8f && Luma(unlit, 8, 8) < 0.02f,
            $"Forward scattering must brighten fog toward a light behind it (forward {Luma(forward, 8, 8)}, backward {Luma(backward, 8, 8)}, unlit {Luma(unlit, 8, 8)}).");

        // Quality settings change the march without breaking the result.
        var coarse = settings with { VolumeStepSize = 2f, VolumeMaxSteps = 8, VolumeLightSamples = 0, VolumeShadows = false };
        byte[] coarseGlow = Render(Cube(glowGraph), coarse.Normalized());
        NuxTAssert(Channel(coarseGlow, 8, 8, 0) > 0.3f, "The lowest fog quality must still render the glow.");

        // Fog between a light and a surface dims that light, but only the fog quality setting decides whether it does.
        // The block sits above the camera's view, on the line from the wall to the light.
        var sunLight = new[] { new NuxTLight(new Vector3(0f, 3f, -1.5f), Vector3.UnitZ, Vector3.One, 20f, 20f, CastShadows: true) };
        var litWall = NuxTTriangleAtZ(-3f, new NuxTMaterial(Vector3.One, Vector3.Zero));
        var blockMin = new Vector3(-0.5f, 1f, -2.5f);
        var blockMax = new Vector3(0.5f, 2f, -2f);
        IEnumerable<NuxTTriangle> Block(ShaderGraph graph) => NuxTCube(blockMin, blockMax, new NuxTMaterial(Vector3.One, Vector3.Zero,
            ShaderGraph: NuxTShaderGraph.Create(graph, blockMin, blockMax - blockMin, 0f, 0f, _ => null)!));
        byte[] litOpen = Render([litWall], lights: sunLight);
        byte[] litThroughEmpty = Render(Block(NuxTFogGraph(node => FogValue(node, "Density", 0f))).Append(litWall), lights: sunLight);
        byte[] shadowed = Render(Block(NuxTFogGraph(node => Sharp(Dense(node)))).Append(litWall), lights: sunLight);
        byte[] unshadowed = Render(Block(NuxTFogGraph(node => Sharp(Dense(node)))).Append(litWall), settings with { VolumeShadows = false },
            lights: sunLight);
        float open = Luma(litOpen, 8, 8);
        NuxTAssert(open > 0.1f, $"The lit wall reference must receive light (got {open}).");
        NuxTAssert(Math.Abs(Luma(litThroughEmpty, 8, 8) - open) < open * 0.15f,
            $"Empty fog volumes must not shadow the surfaces behind them ({Luma(litThroughEmpty, 8, 8)} vs {open}).");
        NuxTAssert(Luma(shadowed, 8, 8) < open * 0.2f,
            $"Dense fog between a light and a surface must dim the light that reaches it ({Luma(shadowed, 8, 8)} vs {open}).");
        NuxTAssert(Math.Abs(Luma(unshadowed, 8, 8) - open) < open * 0.15f,
            $"Turning off fog shadows must let the light through ({Luma(unshadowed, 8, 8)} vs {open}).");

        // Ignore Object Scale: a half-size cube scaled up 2x covers the same world space as the full-size cube. With the
        // option on its fog matches the full-size cube (the graph sees world-sized positions); off, it is stretched.
        // Density here is the object-space X coordinate, so the fog gets denser towards one side.
        var halfMin = new Vector3(-0.5f, -0.5f, -0.75f);
        var halfMax = new Vector3(0.5f, 0.5f, -0.25f);
        ShaderGraph PositionFog(bool ignoreScale) =>
            NuxTFogGraph(node => Sharp(Dense(node)) with { A = 0f, Clamp = ignoreScale }, densityNodes, densityLinks);
        NuxTScene ScaledCube(ShaderGraph graph, Vector3 min, Vector3 max, float scale) => NuxTScene.CreateInstances(
        [
            new NuxTSceneInstance(NuxTCube(min, max, new NuxTMaterial(Vector3.One, Vector3.Zero,
                ShaderGraph: NuxTShaderGraph.Create(graph, min, max - min, 0f, 0f, _ => null)!)),
                Matrix4x4.CreateScale(scale)),
            new NuxTSceneInstance([wall], Matrix4x4.Identity)
        ], Vector3.Zero);
        float MeanDifference(byte[] a, byte[] b)
        {
            float sum = 0f;
            for (int y = 2; y < 14; y++)
            for (int x = 2; x < 14; x++)
                sum += Math.Abs(Luma(a, x, y) - Luma(b, x, y));
            return sum / (12 * 12);
        }
        byte[] worldSize = RenderScene(ScaledCube(PositionFog(false), cubeMin, cubeMax, 1f));
        byte[] unscaledIgnoring = RenderScene(ScaledCube(PositionFog(true), cubeMin, cubeMax, 1f));
        byte[] scaledIgnoring = RenderScene(ScaledCube(PositionFog(true), halfMin, halfMax, 2f));
        byte[] scaledStretched = RenderScene(ScaledCube(PositionFog(false), halfMin, halfMax, 2f));
        NuxTAssert(Luma(worldSize, 2, 8) > Bright && Luma(worldSize, 13, 8) < Dark,
            $"The reference gradient fog must be empty on one side and dense on the other (left {Luma(worldSize, 2, 8)}, right {Luma(worldSize, 13, 8)}).");
        NuxTAssert(MeanDifference(worldSize, unscaledIgnoring) < 0.001f,
            "At scale 1 Ignore Object Scale must change nothing.");
        NuxTAssert(MeanDifference(worldSize, scaledIgnoring) < 0.05f,
            $"Ignore Object Scale must show a scaled cube's fog like an unscaled one (difference {MeanDifference(worldSize, scaledIgnoring)}).");
        NuxTAssert(MeanDifference(worldSize, scaledStretched) > 0.1f,
            $"Without it the fog pattern must stretch with the object (difference {MeanDifference(worldSize, scaledStretched)}).");

        // Generated coordinates cover the object's bounds. With the option on they grow outward from the bounds' centre
        // as the object scales: for the half-size cube scaled 2x (unit X bounds, centred on X = 0) Generated X is the
        // scaled Object X plus 0.5, which the reference graph builds from nodes. Without it Generated stretches. Both feed
        // a sine wave (ten radians per Generated unit) into the density, so stretching shows as a shifted wave.
        ShaderNode[] WaveNodes(float offset) =>
        [
            .. densityNodes,
            ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(
                ShaderGraph.CreateNode(4, ShaderNodeKind.Math, 0, 0) with { Mode = 4 }, "B", 10f), "C", offset),
            ShaderGraph.CreateNode(5, ShaderNodeKind.Math, 0, 0) with { Mode = 29 }
        ];
        ShaderGraph GeneratedFog(bool ignoreScale) => NuxTFogGraph(node => Sharp(Dense(node)) with { A = 0f, Clamp = ignoreScale },
            WaveNodes(0f), [new(2, 3, 0, 0), new(3, 4, 0), new(4, 5, 0), new(5, 1, ShaderGraph.FogInput("Density"), 0)]);
        ShaderGraph OffsetFog() => NuxTFogGraph(node => Sharp(Dense(node)) with { A = 0f, Clamp = true },
            WaveNodes(5f), [new(2, 3, 0, 3), new(3, 4, 0), new(4, 5, 0), new(5, 1, ShaderGraph.FogInput("Density"), 0)]);
        byte[] generatedReference = RenderScene(ScaledCube(OffsetFog(), halfMin, halfMax, 2f));
        byte[] generatedIgnoring = RenderScene(ScaledCube(GeneratedFog(true), halfMin, halfMax, 2f));
        byte[] generatedStretched = RenderScene(ScaledCube(GeneratedFog(false), halfMin, halfMax, 2f));
        var referenceRow = Enumerable.Range(2, 12).Select(x => Luma(generatedReference, x, 8)).ToArray();
        NuxTAssert(referenceRow.Max() > Bright && referenceRow.Min() < 0.3f,
            $"The world-position reference fog must have empty and dense bands (brightest {referenceRow.Max()}, darkest {referenceRow.Min()}).");
        NuxTAssert(MeanDifference(RenderScene(ScaledCube(GeneratedFog(false), cubeMin, cubeMax, 1f)),
                RenderScene(ScaledCube(GeneratedFog(true), cubeMin, cubeMax, 1f))) < 0.001f,
            "At scale 1 Ignore Object Scale must leave Generated coordinates unchanged.");
        NuxTAssert(MeanDifference(generatedReference, generatedIgnoring) < 0.05f,
            $"Ignore Object Scale must make Generated coordinates grow outward as the cube scales (difference {MeanDifference(generatedReference, generatedIgnoring)}).");
        NuxTAssert(MeanDifference(generatedReference, generatedStretched) > 0.1f,
            $"Without it Generated coordinates must stretch with the object (difference {MeanDifference(generatedReference, generatedStretched)}).");
        NuxTAssert(gl.GetError() == GLEnum.NoError, "Fog volume tracing must leave OpenGL error-free.");
    }
}
