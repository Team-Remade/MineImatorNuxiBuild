using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;
using StbImageSharp;

internal static partial class Program
{
    private static unsafe void MobHeadGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Mob head test", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            using var fixture = new Fixture();
            string version = Path.Combine(ParticleDataRoot(), "minecraft", "versions", "26.3");
            typeof(BlockRegistry).GetProperty(nameof(BlockRegistry.VersionRoot))!.SetValue(null, version);
            var viewport = new Viewport();
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            var blocks = (List<string>)typeof(BlockRegistry).GetField("_blocks",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var assembly = typeof(SpawnMenu).Assembly;
            var boundsType = assembly.GetType("MineImatorSimplyRemade.core.world.WorldBounds", true)!;
            var volumeType = assembly.GetType("MineImatorSimplyRemade.core.world.WorldVolume", true)!;
            var detector = assembly.GetType("MineImatorSimplyRemade.core.world.WorldSpecialBlocks", true)!;
            var importerType = assembly.GetType("MineImatorSimplyRemade.core.world.WorldSpecialBlockImporter", true)!;

            void DisposeObject(SceneObject root)
            {
                viewport.SceneObjects.Remove(root);
                foreach (var node in new[] { root }.Concat(root.GetAllDescendants()))
                {
                    foreach (var mesh in node.Visuals) mesh.Dispose();
                    if (node is MineImatorSimplyRemadeNuxi.core.objs.sceneObjects.BoneSceneObject bone)
                        bone.IndicatorMesh?.Dispose();
                }
            }

            List<(vec3 Position, vec3 Normal, vec2 Uv)> Geometry(SceneObject root) =>
                new[] { root }.Concat(root.GetAllDescendants()).SelectMany(node =>
                {
                    var transform = node.GetWorldMatrix();
                    var normals = transform.Inverse.Transposed;
                    return node.Visuals.SelectMany(mesh => mesh.Vertices.Select((vertex, index) =>
                        ((transform * new vec4(vertex, 1)).xyz,
                         (normals * new vec4(mesh.Normals[index], 0)).xyz.Normalized, mesh.TexCoords[index])));
                }).ToList();

            void SameGeometry(SceneObject expected, SceneObject actual, string label)
            {
                var a = Geometry(expected);
                var b = Geometry(actual);
                Equal(a.Count, b.Count, label + ": vertex count");
                for (int i = 0; i < a.Count; i++)
                {
                    Near(a[i].Position, b[i].Position, label + ": position");
                    Near(a[i].Normal, b[i].Normal, label + ": normal");
                    Check((a[i].Uv - b[i].Uv).Length < 0.00001f, label + ": UV");
                }
            }

            int scenarios = 0;
            foreach (string floor in new[] { "skeleton_skull", "wither_skeleton_skull", "zombie_head",
                         "creeper_head", "piglin_head", "dragon_head", "player_head" })
            {
                string wall = floor.Replace("_skull", "_wall_skull").Replace("_head", "_wall_head");
                blocks.Add(floor);
                blocks.Add(wall);
                Equal(16, BlockRegistry.GetVariants(floor).Count, floor + ": floor variants");
                Equal(4, BlockRegistry.GetVariants(wall).Count, floor + ": wall variants");
                var baseObject = spawn.SpawnBlockObject(floor, BlockRegistry.GetVariants(floor)[0])!;
                var baseGeometry = Geometry(baseObject);
                try
                {
                    Check(baseObject.Visuals.Count > 0, floor + ": head geometry");
                    Check(baseObject.Visuals.All(mesh => mesh.TextureId != 0 &&
                        mesh.MinecraftTextureKey.StartsWith("entity/", StringComparison.Ordinal)), floor + ": entity textures");
                    Check(baseObject.Children.Count == 0, floor + ": block renderer has no body bones");
                    if (floor == "piglin_head") Check(baseObject.Visuals.Count >= 5, "Piglin ears included");
                    if (floor == "dragon_head") Check(baseObject.Visuals.Count >= 8, "Dragon jaw included");
                    if (floor == "player_head") Equal(2, baseObject.Visuals.Count, "Player hat included");
                    if (floor is "skeleton_skull" or "wither_skeleton_skull" or "creeper_head")
                    {
                        var core = baseObject.Visuals[0].Vertices.Select(vertex =>
                            (baseObject.GetWorldMatrix() * new vec4(vertex, 1)).xyz).ToArray();
                        Near(new vec3(-0.25f, 0, -0.25f), new vec3(core.Min(v => v.x),
                            core.Min(v => v.y), core.Min(v => v.z)), floor + ": core head minimum");
                        Near(new vec3(0.25f, 0.5f, 0.25f), new vec3(core.Max(v => v.x),
                            core.Max(v => v.y), core.Max(v => v.z)), floor + ": core head maximum");
                    }
                    using var settings = JsonDocument.Parse($$$"""{"name":"{{{floor}}}","state":{"rotation":"0"}}""");
                    var importedBlock = MiObjectTemplateFactory.Create(MineImatorLoader.Instance, gl,
                        new MiTemplate { Type = "block", Block = settings.RootElement.Clone() },
                        new MiTimeline(), new Dictionary<string, MiResource>(), version)!;
                    try { SameGeometry(baseObject, importedBlock, floor + ": Mine-imator block import"); }
                    finally { DisposeObject(importedBlock); }
                    var tiled = spawn.SpawnBlockObject(floor, BlockRegistry.GetVariants(floor)[0], tileX: 2, tileY: 2)!;
                    try
                    {
                        Equal(baseObject.Visuals.Count, tiled.Visuals.Count, floor + ": tiled meshes stay batched");
                        Equal(baseObject.Visuals.Sum(mesh => mesh.Vertices.Count) * 4,
                            tiled.Visuals.Sum(mesh => mesh.Vertices.Count), floor + ": repeated vertices");
                        var original = baseObject.Visuals[0].Vertices;
                        var repeated = tiled.Visuals[0].Vertices;
                        for (int i = 0; i < original.Count; i++)
                        {
                            Near(original[i] + vec3.UnitX, repeated[original.Count + i], floor + ": X tile");
                            Near(original[i] + vec3.UnitY, repeated[original.Count * 2 + i], floor + ": Y tile");
                        }
                    }
                    finally { DisposeObject(tiled); }
                }
                finally { DisposeObject(baseObject); }

                foreach (string name in new[] { floor, wall })
                foreach (var variant in BlockRegistry.GetVariants(name))
                {
                    var bounds = Activator.CreateInstance(boundsType, [0, 0, 0, 0, 0, 0])!;
                    var volume = Activator.CreateInstance(volumeType, [bounds])!;
                    string state = $"minecraft:{name}[{variant.VariantKey}]";
                    volumeType.GetMethod("Set")!.Invoke(volume, [0, 0, 0, state]);
                    string path = fixture.NewPath(".schem");
                    volumeType.GetMethod("SaveSchematic")!.Invoke(volume, [path, null]);
                    var terrain = spawn.SpawnSchematicFromPath(path)!;
                    var placed = spawn.SpawnBlockObject(name, variant)!;
                    try
                    {
                        var terrainGeometry = Geometry(terrain);
                        var placedGeometry = Geometry(placed);
                        float yaw;
                        vec3 mounting = vec3.Zero;
                        if (name == floor)
                            yaw = -int.Parse(variant.VariantKey.Split('=')[1]) * 22.5f;
                        else
                        {
                            (yaw, mounting) = variant.VariantKey switch
                            {
                                "facing=north" => (180f, new vec3(0, 0.25f, 0.25f)),
                                "facing=east" => (90f, new vec3(-0.25f, 0.25f, 0)),
                                "facing=south" => (0f, new vec3(0, 0.25f, -0.25f)),
                                _ => (270f, new vec3(0.25f, 0.25f, 0))
                            };
                        }
                        var rotation = mat4.RotateY(yaw * MathF.PI / 180);
                        for (int i = 0; i < placedGeometry.Count; i++)
                        {
                            Near((rotation * new vec4(baseGeometry[i].Position, 1)).xyz + mounting,
                                placedGeometry[i].Position, state + ": exact mounting position");
                            Near((rotation * new vec4(baseGeometry[i].Normal, 0)).xyz,
                                placedGeometry[i].Normal, state + ": exact rotated normal");
                        }
                        Equal(placedGeometry.Count, terrainGeometry.Count, state + ": scenery vertex count");
                        // Scenery batches by texture, so compare the complete vertex set rather than mesh order.
                        foreach (var vertex in placedGeometry)
                            Check(terrainGeometry.Any(other => (vertex.Position - other.Position).Length < 0.0001f &&
                                (vertex.Normal - other.Normal).Length < 0.0001f && (vertex.Uv - other.Uv).Length < 0.0001f),
                                state + ": scenery matches placed head");
                        var saved = ProjectSceneSerializer.SerializeObjectForLibrary(placed);
                        var restored = ProjectSceneSerializer.SpawnObjectFromEntry(saved, viewport, spawn)!;
                        try { SameGeometry(placed, restored, state + ": project reload"); }
                        finally { DisposeObject(restored); }
                        Check(spawn.RebuildBlockMeshes(placed), state + ": rebuilding");
                        var plan = detector.GetMethod("Detect")!.Invoke(null, [volume])!;
                        var detected = ((IEnumerable)plan.GetType().GetProperty("Blocks")!.GetValue(plan)!).Cast<object>().Single();
                        using var importer = (IDisposable)Activator.CreateInstance(importerType,
                            [spawn, "", new List<ProjectAssetEntry>()])!;
                        byte[] embedded = (byte[])importerType.GetMethod("Embed")!.Invoke(importer,
                            [CreateTypedArray(detected)])!;
                        var converted = new SceneObject { PivotOffset = vec3.Zero };
                        try
                        {
                            importerType.GetMethod("AddEmbedded")!.Invoke(importer, [embedded, converted]);
                            var convertedGeometry = Geometry(converted);
                            Equal(placedGeometry.Count, convertedGeometry.Count, state + ": embedded head vertex count");
                            foreach (var vertex in placedGeometry)
                                Check(convertedGeometry.Any(other => (vertex.Position - other.Position).Length < 0.0001f &&
                                    (vertex.Normal - other.Normal).Length < 0.0001f),
                                    state + ": world conversion matches head");
                        }
                        finally { DisposeObject(converted); }
                        scenarios++;
                    }
                    finally { DisposeObject(terrain); DisposeObject(placed); }
                }
            }

            string textureKey = "entity/piglin/piglin";
            uint overrideTexture = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, overrideTexture);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            byte[] pixel = [255, 0, 255, 255];
            fixed (byte* data = pixel)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba,
                    PixelType.UnsignedByte, data);
            string pack = MinecraftDataLoader.GetResourcePackId("mob-head-test");
            string packedKey = MinecraftDataLoader.BuildResourcePackTextureKeyFromId(pack, textureKey);
            TerrainAtlas.Textures[packedKey] = overrideTexture;
            var overridden = spawn.SpawnBlockObject("piglin_head", BlockRegistry.GetVariants("piglin_head")[0], pack)!;
            try
            {
                Check(overridden.Visuals.All(mesh => mesh.TextureId == overrideTexture &&
                    mesh.MinecraftTextureKey == packedKey), "Pack overrides head and ear textures without model JSON");
                Check(overridden.Visuals.All(mesh => mesh.TexCoords.Count == mesh.Vertices.Count), "Authored UVs retained");
                gl.Viewport(0, 0, 64, 64);
                gl.Enable(EnableCap.DepthTest);
                gl.ClearColor(0, 0, 0, 1);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                var view = mat4.LookAt(new vec3(0, 0.4f, 2), new vec3(0, 0.4f, 0), vec3.UnitY);
                var projection = mat4.Perspective(MathF.PI / 3, 1, 0.1f, 10);
                foreach (var mesh in overridden.Visuals)
                {
                    mesh.Unlit = true;
                    mesh.IncludeInFog = false;
                    mesh.Render(overridden.GetWorldMatrix(), view, projection, useSceneLighting: false);
                }
                byte[] rendered = new byte[64 * 64 * 4];
                fixed (byte* data = rendered)
                    gl.ReadPixels(0, 0, 64, 64, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                int magenta = Enumerable.Range(0, 64 * 64).Count(index =>
                    rendered[index * 4] > 200 && rendered[index * 4 + 1] < 20 && rendered[index * 4 + 2] > 200);
                Check(magenta > 100, $"Head renderer produces textured pixels ({magenta})");
            }
            finally
            {
                DisposeObject(overridden);
                TerrainAtlas.Textures.Remove(packedKey);
                gl.DeleteTexture(overrideTexture);
            }
            var skin = ImageResult.FromMemory(File.ReadAllBytes(Path.Combine(version, "textures", "entity",
                "skeleton", "skeleton.png")), ColorComponents.RedGreenBlueAlpha);
            Equal(64, skin.Width, "Rectangular skin width");
            Equal(32, skin.Height, "Rectangular skin height");
            uint rectangular = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, rectangular);
            fixed (byte* data = skin.Data)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)skin.Width, (uint)skin.Height,
                    0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
            string rectangleKey = MinecraftDataLoader.BuildResourcePackTextureKeyFromId(pack, "entity/skeleton/skeleton");
            var native = spawn.SpawnBlockObject("skeleton_skull", BlockRegistry.GetVariants("skeleton_skull")[0])!;
            TerrainAtlas.Textures[rectangleKey] = rectangular;
            var rectangularHead = spawn.SpawnBlockObject("skeleton_skull", BlockRegistry.GetVariants("skeleton_skull")[0], pack)!;
            try
            {
                var originalUvs = native.Visuals[0].TexCoords;
                var packedUvs = rectangularHead.Visuals[0].TexCoords;
                for (int i = 0; i < originalUvs.Count; i++)
                    Check((packedUvs[i] - originalUvs[i] * new vec2(1, 2)).Length < 0.00001f,
                        "Rectangular atlas UVs match square-padded model skin");
            }
            finally
            {
                DisposeObject(native);
                DisposeObject(rectangularHead);
                TerrainAtlas.Textures.Remove(rectangleKey);
                gl.DeleteTexture(rectangular);
            }
            Check(MobHeadBlockMesh.GetModelName("sample:zombie_head") == null, "Mod namespaces untouched");
            Check(MobHeadBlockMesh.GetModelName("piston_head") == null, "Piston geometry untouched");
            Equal(GLEnum.NoError, gl.GetError(), "GPU uploads have no GL errors");
            Console.WriteLine($"PASS Mob head subtrees, textures, tiling, reload, scenery and embedded world conversion ({scenarios} states)");
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }

        static Array CreateTypedArray(object value)
        {
            var result = Array.CreateInstance(value.GetType(), 1);
            result.SetValue(value, 0);
            return result;
        }
    }
}
