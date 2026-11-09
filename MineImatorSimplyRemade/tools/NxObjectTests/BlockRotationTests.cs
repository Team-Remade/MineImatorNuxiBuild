using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static unsafe void BlockRotationGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Block rotation test", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            using var fixture = new Fixture();
            const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
            string version = Path.Combine(ParticleDataRoot(), "minecraft", "versions", "26.3");
            typeof(BlockRegistry).GetProperty(nameof(BlockRegistry.VersionRoot))!.SetValue(null, version);
            var blocks = (List<string>)typeof(BlockRegistry).GetField("_blocks", statics)!.GetValue(null)!;
            var parse = typeof(BlockRegistry).GetMethod("ParseBlockstate", statics,
                [typeof(string), typeof(JsonObject), typeof(string)])!;
            foreach (string name in new[] { "stone_button", "oak_button", "oak_trapdoor", "iron_door", "oak_fence" })
            {
                var json = JsonNode.Parse(File.ReadAllText(Path.Combine(version, "blockstates", name + ".json")))!.AsObject();
                parse.Invoke(null, [name, json, ""]);
                blocks.Add(name);
                if (json["variants"] is JsonObject variants)
                {
                    foreach (var entry in BlockRegistry.GetVariants(name).Where(entry => entry.TopHalf == null))
                    {
                        var source = variants[entry.StateKey ?? entry.VariantKey]!;
                        int y = source["y"]?.GetValue<int>() ?? 0;
                        Equal((360 - y) % 360, entry.RotationY, name + ": registry uses mesher coordinates");
                    }
                }
            }

            var viewport = new Viewport();
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            var assembly = typeof(SpawnMenu).Assembly;
            var boundsType = assembly.GetType("MineImatorSimplyRemade.core.world.WorldBounds", true)!;
            var volumeType = assembly.GetType("MineImatorSimplyRemade.core.world.WorldVolume", true)!;
            var detector = assembly.GetType("MineImatorSimplyRemade.core.world.WorldSpecialBlocks", true)!;
            var importerType = assembly.GetType("MineImatorSimplyRemade.core.world.WorldSpecialBlockImporter", true)!;
            using var importer = (IDisposable)Activator.CreateInstance(importerType,
                [spawn, "", new List<ProjectAssetEntry>()])!;
            int scenarios = 0;

            List<(vec3 Position, vec3 Normal)> Geometry(SceneObject root)
            {
                var world = root.GetWorldMatrix();
                var normals = world.Inverse.Transposed;
                return root.Visuals.SelectMany(mesh => mesh.Vertices.Select((vertex, i) =>
                    ((world * new vec4(vertex, 1)).xyz, (normals * new vec4(mesh.Normals[i], 0)).xyz.Normalized)))
                    .ToList();
            }

            void SameGeometry(List<(vec3 Position, vec3 Normal)> expected, SceneObject actual, string label)
            {
                var remaining = Geometry(actual);
                Equal(expected.Count, remaining.Count, label + ": vertex count");
                foreach (var vertex in expected)
                {
                    int index = remaining.FindIndex(other => (vertex.Position - other.Position).Length < 0.0001f &&
                        (vertex.Normal - other.Normal).Length < 0.0001f);
                    Check(index >= 0, label + ": matching world-space vertex and normal");
                    remaining.RemoveAt(index);
                }
            }

            void DisposeObject(SceneObject root)
            {
                viewport.SceneObjects.Remove(root);
                foreach (var mesh in root.Visuals) mesh.Dispose();
            }

            void Scenario(string state, string? upper = null, (string Face, string Facing, bool Powered)? button = null)
            {
                string label = state + (upper == null ? "" : " + upper half");
                var bounds = Activator.CreateInstance(boundsType, [0, 0, 0, 0, upper == null ? 0 : 1, 0])!;
                var volume = Activator.CreateInstance(volumeType, [bounds])!;
                volumeType.GetMethod("Set")!.Invoke(volume, [0, 0, 0, state]);
                if (upper != null) volumeType.GetMethod("Set")!.Invoke(volume, [0, 1, 0, upper]);
                string path = fixture.NewPath(".schem");
                volumeType.GetMethod("SaveSchematic")!.Invoke(volume, [path, null]);
                var terrain = spawn.SpawnSchematicFromPath(path) ??
                    throw new InvalidOperationException("Failed importing " + label);
                try
                {
                    var expected = Geometry(terrain);
                    Check(expected.Count > 0, label + ": terrain geometry");
                    if (button is { } placement)
                    {
                        float depth = placement.Powered ? 1f / 16 : 2f / 16;
                        vec3 min = new(-3f / 16, 0, -2f / 16), max = new(3f / 16, depth, 2f / 16);
                        if (placement.Face == "ceiling") { min.y = 1 - depth; max.y = 1; }
                        if (placement.Facing is "east" or "west")
                        {
                            (min.x, min.z) = (min.z, min.x);
                            (max.x, max.z) = (max.z, max.x);
                        }
                        if (placement.Face == "wall")
                        {
                            min = new(-3f / 16, 6f / 16, -0.5f);
                            max = new(3f / 16, 10f / 16, -0.5f + depth);
                            if (placement.Facing == "north") { min.z = 0.5f - depth; max.z = 0.5f; }
                            if (placement.Facing is "east" or "west")
                            {
                                (min.x, min.z) = (min.z, min.x);
                                (max.x, max.z) = (max.z, max.x);
                                if (placement.Facing == "west") { min.x = 0.5f - depth; max.x = 0.5f; }
                            }
                        }
                        Near(min, new vec3(expected.Min(v => v.Position.x), expected.Min(v => v.Position.y),
                            expected.Min(v => v.Position.z)), label + ": expected mounting surface minimum");
                        Near(max, new vec3(expected.Max(v => v.Position.x), expected.Max(v => v.Position.y),
                            expected.Max(v => v.Position.z)), label + ": expected mounting surface maximum");
                    }

                    var plan = detector.GetMethod("Detect")!.Invoke(null, [volume])!;
                    var detected = ((IEnumerable)plan.GetType().GetProperty("Blocks")!.GetValue(plan)!).Cast<object>().ToArray();
                    Equal(1, detected.Length, label + ": one animatable block");
                    var converted = (SceneObject)importerType.GetMethod("Create")!.Invoke(importer, [detected[0], null])!;
                    try
                    {
                        SameGeometry(expected, converted, label + ": converted world import");
                        Check(spawn.RebuildBlockMeshes(converted), label + ": rebuild succeeds");
                        SameGeometry(expected, converted, label + ": rebuilt mesh");
                        Check(spawn.ApplyResourcePackToSpawnedObject(converted, ""), label + ": resource-pack reapply succeeds");
                        SameGeometry(expected, converted, label + ": resource-pack reapply");
                    }
                    finally { DisposeObject(converted); }
                }
                finally { DisposeObject(terrain); }
                scenarios++;
            }

            foreach (string name in new[] { "stone_button", "oak_button" })
            foreach (string face in new[] { "wall", "floor", "ceiling" })
            foreach (string facing in new[] { "north", "south", "east", "west" })
            foreach (bool powered in new[] { false, true })
                Scenario($"minecraft:{name}[face={face},facing={facing},powered={powered.ToString().ToLowerInvariant()}]",
                    button: (face, facing, powered));

            foreach (int id in new[] { 77, 143 })
            foreach (var (data, face, facing) in new[]
            {
                (0, "ceiling", "north"), (1, "wall", "east"), (2, "wall", "west"),
                (3, "wall", "south"), (4, "wall", "north"), (5, "floor", "north")
            })
            foreach (bool powered in new[] { false, true })
                Scenario($"legacy:{id}:{data + (powered ? 8 : 0)}", button: (face, facing, powered));

            foreach (string facing in new[] { "north", "south", "east", "west" })
            foreach (string open in new[] { "false", "true" })
            {
                foreach (string half in new[] { "bottom", "top" })
                    Scenario($"minecraft:oak_trapdoor[facing={facing},half={half},open={open}]");
                foreach (string hinge in new[] { "left", "right" })
                {
                    string properties = $"facing={facing},hinge={hinge},open={open}";
                    Scenario($"minecraft:iron_door[{properties},half=lower]",
                        $"minecraft:iron_door[{properties},half=upper]");
                }
            }

            var multipart = typeof(BlockRegistry).GetMethod("GetMultipartVariants", statics)!;
            foreach (var (facing, y) in new[] { ("north", 0), ("east", 270), ("south", 180), ("west", 90) })
            {
                var properties = new Dictionary<string, string> { [facing] = "true" };
                var parts = (IReadOnlyList<BlockVariantEntry>)multipart.Invoke(null, ["oak_fence", properties, ""])!;
                Equal(2, parts.Count, "Fence has post and one connected side");
                Equal(y, parts[1].RotationY, "Multipart import retains correct " + facing + " rotation");
                Check(BlockRegistry.GetVariants("oak_fence").Contains(parts[1]),
                    "Multipart browser and import share mesher-coordinate entries");
            }
            Equal(GLEnum.NoError, gl.GetError(), "Block rotation OpenGL error");
            Console.WriteLine($"PASS Block rotations ({scenarios} schematic/world scenarios, rebuilds, resource-pack reapply and multipart)");
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
