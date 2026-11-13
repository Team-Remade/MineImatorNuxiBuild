using System.Reflection;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemade.core.world;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static unsafe void WaterTerrainGraphics(bool testVeldrid)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Water terrain regression", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            using var fixture = new Fixture();
            uint Texture(string key, byte r, byte g, byte b, byte a)
            {
                uint texture = gl.GenTexture();
                gl.BindTexture(GLEnum.Texture2D, texture);
                byte[] pixels = [r, g, b, a];
                fixed (byte* p = pixels)
                    gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0,
                        GLEnum.Rgba, GLEnum.UnsignedByte, p);
                gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
                gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
                TerrainAtlas.Textures[key] = texture;
                TerrainAtlas.TilePixels[key] = pixels;
                return texture;
            }

            uint waterTexture = Texture("water_still", 0, 0, 255, 128);
            uint flowTexture = Texture("water_flow", 0, 0, 255, 128);
            uint stoneTexture = Texture("stone", 255, 0, 0, 255);
            try
            {
                const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic;
                string version = Path.Combine(ParticleDataRoot(), "minecraft", "versions", "26.3");
                typeof(BlockRegistry).GetProperty(nameof(BlockRegistry.VersionRoot))!.SetValue(null, version);
                var blocks = (List<string>)typeof(BlockRegistry).GetField("_blocks", staticFlags)!.GetValue(null)!;
                var parse = typeof(BlockRegistry).GetMethod("ParseBlockstate", staticFlags,
                    [typeof(string), typeof(JsonObject), typeof(string)])!;
                foreach (string name in new[] { "water", "stone" })
                {
                    parse.Invoke(null, [name, JsonNode.Parse(File.ReadAllText(
                        Path.Combine(version, "blockstates", name + ".json")))!.AsObject(), ""]);
                    blocks.Add(name);
                }
                var viewport = new Viewport { Gl = gl };
                var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
                var volume = new WorldVolume(new WorldBounds(0, 0, 0, 1, 0, 0));
                volume.Set(0, 0, 0, "minecraft:water[level=0]");
                volume.Set(1, 0, 0, "minecraft:stone");
                string path = fixture.NewPath(".schem");
                volume.SaveSchematic(path);
                var terrain = spawn.SpawnSchematicFromPath(path) ??
                    throw new InvalidOperationException("Water/terrain import failed");
                try
                {
                    void CheckBatches()
                    {
                        Check(terrain.Visuals.Any(m => m.TextureId == waterTexture && m.RequiresTranslucentPass),
                            "Imported water retains its translucent batch");
                        Check(terrain.Visuals.Any(m => m.TextureId == stoneTexture && !m.RequiresTranslucentPass),
                            "Opaque terrain remains separate from water");
                    }
                    CheckBatches();
                    Check(spawn.ApplyResourcePackToSpawnedObject(terrain, ""), "Terrain rebuild succeeds");
                    CheckBatches();
                }
                finally { foreach (var mesh in terrain.Visuals) mesh.Dispose(); }

                using var water = new PlaneMesh(gl, 3, 3, PlaneOrientation.XY)
                    { TextureId = waterTexture, IsTranslucent = true, Unlit = true, DoubleSided = true };
                using var stone = new PlaneMesh(gl, 3, 3, PlaneOrientation.XY)
                    { TextureId = stoneTexture, Unlit = true, DoubleSided = true };
                using var foreground = new PlaneMesh(gl, 3, 3, PlaneOrientation.XY)
                    { BlendColor = new vec4(0, 1, 0, 1), Unlit = true, DoubleSided = true };
                using var clone = water.Clone();
                Check(clone.RequiresTranslucentPass, "Cloning preserves water classification");
                clone.AlphaMode = MaterialAlphaMode.Clip;
                Check(!clone.RequiresTranslucentPass, "Explicit alpha clipping retains depth-writing behavior");

                var camera = new Camera { Target = vec3.Zero, Pitch = 0, Yaw = 0, Distance = 4 };
                var behind = mat4.Translate(new vec3(0, 0, -1));
                var ahead = mat4.Translate(new vec3(0, 0, 1));
                using var preview = new PreviewRenderer(gl, 64) { AutoRotateSpeed = 0 };
                const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                var render = typeof(Viewport).GetMethod("RenderDepthOrdered", instanceFlags)!;
                var lights = new List<(vec3, vec3, float, float, int, vec3, float, float, float)>();
                byte[] Read(uint texture)
                {
                    byte[] pixels = new byte[64 * 64 * 4];
                    gl.BindTexture(GLEnum.Texture2D, texture);
                    fixed (byte* p = pixels)
                        gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
                    return pixels;
                }
                int center = (32 * 64 + 32) * 4;
                float depth = 0;
                bool splitAoPasses = false;
                byte[] Draw(params (Mesh mesh, mat4 model)[] draws)
                {
                    preview.Render([], "clear", 0, camera: camera);
                    uint fbo = (uint)typeof(PreviewRenderer).GetField("_fbo", instanceFlags)!.GetValue(preview)!;
                    gl.BindFramebuffer(GLEnum.Framebuffer, fbo);
                    var items = draws.Select(d => (d.model, d.mesh, d.mesh.SortDepth, false, false, false)).ToList();
                    if (splitAoPasses)
                    {
                        var participants = items.Where(d => d.mesh != stone).ToList();
                        var excluded = items.Where(d => d.mesh == stone).ToList();
                        render.Invoke(viewport, [camera.GetViewMatrix(), camera.GetProjectionMatrix(1),
                            participants, lights, true, false]);
                        render.Invoke(viewport, [camera.GetViewMatrix(), camera.GetProjectionMatrix(1),
                            excluded, lights, true, false]);
                        render.Invoke(viewport, [camera.GetViewMatrix(), camera.GetProjectionMatrix(1),
                            items, lights, true, true]);
                    }
                    else
                        render.Invoke(viewport, [camera.GetViewMatrix(), camera.GetProjectionMatrix(1), items, lights, true, null]);
                    Check(gl.GetBoolean(GLEnum.DepthWritemask), "Scene pass restores depth writes");
                    float value;
                    gl.ReadPixels(32, 32, 1, 1, GLEnum.DepthComponent, GLEnum.Float, &value);
                    depth = value;
                    gl.BindFramebuffer(GLEnum.Framebuffer, 0);
                    return Read(preview.ColorTexture);
                }
                void Composite(byte[] pixels, string label)
                {
                    Check(pixels[center] is >= 120 and <= 135 && pixels[center + 2] is >= 120 and <= 135,
                        label + ": water blends over terrain, not the background");
                    Equal((byte)255, pixels[center + 3], label + ": terrain remains fully covered");
                }

                byte[] solid = Draw((stone, behind));
                Check(solid[center] > 245 && solid[center + 2] < 5, "Opaque terrain baseline is red");
                float terrainDepth = depth;
                byte[] waterFirst = Draw((water, mat4.Identity), (stone, behind));
                Composite(waterFirst, "Water submitted before terrain");
                Near(terrainDepth, depth, "Water does not replace terrain depth");
                Check(waterFirst.SequenceEqual(Draw((stone, behind), (water, mat4.Identity))),
                    "Water/terrain pixels do not depend on batch insertion order");
                splitAoPasses = true;
                Check(waterFirst.SequenceEqual(Draw((water, mat4.Identity), (stone, behind))),
                    "Terrain excluded from AO still renders before translucent water");
                splitAoPasses = false;
                byte[] frontOnly = Draw((foreground, ahead));
                Check(frontOnly.SequenceEqual(Draw((water, mat4.Identity), (stone, behind), (foreground, ahead))),
                    "Opaque foreground still occludes water");
                water.IsTranslucent = false;
                byte[] layerOnly = Draw((water, mat4.Identity));
                Check(layerOnly.SequenceEqual(Draw((water, mat4.Identity), (stone, behind))),
                    "Ordinary alpha-blended model layers keep their depth-writing order");
                water.IsTranslucent = true;
                using var farWater = new PlaneMesh(gl, 3, 3, PlaneOrientation.XY)
                    { BlendColor = new vec4(0, 1, 0, 1), Alpha = .5f, IsTranslucent = true, Unlit = true, DoubleSided = true };
                var middle = mat4.Translate(new vec3(0, 0, -.5f));
                byte[] layers = Draw((water, mat4.Identity), (farWater, middle), (stone, behind));
                Check(Math.Abs(layers[center] - 64) <= 2 && Math.Abs(layers[center + 1] - 64) <= 2 &&
                    Math.Abs(layers[center + 2] - 128) <= 2, "Translucent batches blend back to front using world-space centers");
                Check(layers.SequenceEqual(Draw((farWater, middle), (stone, behind), (water, mat4.Identity))),
                    "Translucent batch sorting is independent of submission order");

                var root = new SceneObject();
                var waterNode = new SceneObject();
                waterNode.AddMesh(water);
                var stoneNode = new SceneObject { Position = new vec3(0, 0, -1) };
                stoneNode.AddMesh(stone);
                root.AddChild(waterNode);
                root.AddChild(stoneNode);
                preview.Render([], "hierarchy", 0, sceneRoot: root, camera: camera);
                Composite(Read(preview.ColorTexture), "World browser hierarchy");
                using var flatStone = stone.Clone();
                for (int i = 0; i < flatStone.Vertices.Count; i++)
                    flatStone.Vertices[i] += new vec3(0, 0, -1);
                flatStone.Upload();
                preview.Render([water, flatStone], "flat", 0, camera: camera);
                Composite(Read(preview.ColorTexture), "Flat spawn preview");

                if (testVeldrid)
                {
                    using var device = Veldrid.GraphicsDevice.CreateD3D11(new Veldrid.GraphicsDeviceOptions(
                        debug: false, swapchainDepthFormat: null, syncToVerticalBlank: false,
                        resourceBindingModel: Veldrid.ResourceBindingModel.Improved,
                        preferDepthRangeZeroToOne: true, preferStandardClipSpaceYDirection: true));
                    using var renderer = new VeldridSceneRenderer(device, gl);
                    byte[] DrawVeldrid(params (Mesh mesh, mat4 model)[] draws)
                    {
                        renderer.BeginFrame(64, 64, camera.GetViewMatrix(), camera.GetProjectionMatrix(1),
                            new System.Numerics.Vector4(.12f, .12f, .14f, 1));
                        foreach (var draw in draws.OrderBy(d =>
                                     d.mesh.GetRenderSortKey(d.model, camera.Position, d.mesh.SortDepth, 0)))
                            renderer.Draw(draw.mesh, draw.model, true);
                        renderer.EndFrame(preview.ColorTexture);
                        return Read(preview.ColorTexture);
                    }
                    Composite(DrawVeldrid((water, mat4.Identity), (stone, behind)), "Veldrid");
                    byte[] occluded = DrawVeldrid((water, mat4.Identity), (stone, behind), (foreground, ahead));
                    Check(occluded[center + 1] > 245 && occluded[center + 2] < 5,
                        "Veldrid opaque foreground occludes water");
                    renderer.BeginFrame(64, 64, camera.GetViewMatrix(), camera.GetProjectionMatrix(1),
                        System.Numerics.Vector4.Zero);
                    renderer.Draw(water, mat4.Identity, true);
                    renderer.Draw(stone, behind, true);
                    renderer.EndFrame(preview.ColorTexture);
                    Check(Read(preview.ColorTexture)[center] > 245,
                        "Veldrid water pipeline does not write depth even when submitted first");
                }
                Equal(GLEnum.NoError, gl.GetError(), "No OpenGL errors");
            }
            finally
            {
                foreach (string key in new[] { "water_still", "water_flow", "stone" })
                {
                    TerrainAtlas.Textures.Remove(key);
                    TerrainAtlas.TilePixels.Remove(key);
                }
                gl.DeleteTexture(waterTexture);
                gl.DeleteTexture(flowTexture);
                gl.DeleteTexture(stoneTexture);
            }
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
