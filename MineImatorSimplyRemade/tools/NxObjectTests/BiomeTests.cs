using System.Reflection;
using System.Text.Json;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static void BiomeSettings(Fixture fixture)
    {
        string previousBiome = BiomeEnvironment.Biome;
        string previousRoot = BlockRegistry.VersionRoot;
        typeof(BlockRegistry).GetProperty(nameof(BlockRegistry.VersionRoot))!
            .SetValue(null, Path.Combine(ParticleDataRoot(), "minecraft", "versions", "26.3"));
        var defaults = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectRenderSettings)!;
        Equal("default", defaults.Biome, "Old projects keep their existing colors");
        try
        {
            Equal(67, BiomeEnvironment.Presets.Count, "Complete Java 26.3 biome catalog");
            Equal(67, BiomeEnvironment.Presets.Select(p => p.Id).Distinct().Count(), "Stable unique biome IDs");
            foreach (var preset in BiomeEnvironment.Presets)
            {
                BiomeEnvironment.Apply(preset.Id, new BiomeColors());
                foreach (var kind in Enum.GetValues<BiomeTintKind>())
                {
                    var tint = BiomeEnvironment.GetTint(kind);
                    Check(tint.x >= 0 && tint.x <= 1 && tint.y >= 0 && tint.y <= 1 && tint.z >= 0 && tint.z <= 1,
                        preset.Id + ": valid " + kind + " color");
                }
                Equal(0x619961, BiomeColors.Pack(BiomeEnvironment.GetTint(BiomeTintKind.Spruce)), "Fixed vanilla spruce");
                Equal(0x80a755, BiomeColors.Pack(BiomeEnvironment.GetTint(BiomeTintKind.Birch)), "Fixed vanilla birch");
                Equal(0x92c648, BiomeColors.Pack(BiomeEnvironment.GetTint(BiomeTintKind.Mangrove)), "Fixed vanilla mangrove");
            }
            foreach (var (id, grass, foliage, water) in new[]
            {
                ("badlands", 0x90814d, 0x9e814d, 0x3f76e4),
                ("cherry_grove", 0xb6db61, 0xb6db61, 0x5db7ef),
                ("dappled_forest", 0xdf6827, 0xe68e30, 0x375154),
                ("pale_garden", 0x778272, 0x878d76, 0x76889d),
                ("swamp", 0x6a7039, 0x6a7039, 0x617b64)
            })
            {
                BiomeEnvironment.Apply(id, new BiomeColors());
                Equal(grass, BiomeColors.Pack(BiomeEnvironment.GetTint(BiomeTintKind.Grass)), id + ": grass override");
                Equal(foliage, BiomeColors.Pack(BiomeEnvironment.GetTint(BiomeTintKind.Foliage)), id + ": foliage override");
                Equal(water, BiomeColors.Pack(BiomeEnvironment.GetTint(BiomeTintKind.Water)), id + ": water override");
            }
            var colors = new BiomeColors();
            foreach (var kind in Enum.GetValues<BiomeTintKind>())
                if (kind != BiomeTintKind.None)
                    colors.SetColor(kind, (int)kind * 0x10203);
            var panel = new PropertiesPanel { Biome = "custom", CustomBiomeColors = colors };
            var manifest = new ProjectManifest();
            panel.WriteProjectSettingsToManifest(manifest);
            colors.Grass = 0;
            string json = JsonSerializer.Serialize(manifest, AppJsonContext.Default.ProjectManifest);
            var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectManifest)!;
            var loaded = new PropertiesPanel();
            loaded.LoadProjectSettingsFromManifest(restored);
            Equal("custom", loaded.Biome, "Custom selection persists");
            foreach (var kind in Enum.GetValues<BiomeTintKind>())
                if (kind != BiomeTintKind.None)
                    Equal((int)kind * 0x10203, BiomeColors.Pack(BiomeEnvironment.GetTint(kind)), kind + ": independent persisted color");
            loaded.CustomBiomeColors.Water = 0;
            Equal(0x30609, restored.Settings.CustomBiomeColors.Water, "Loaded panel does not alias manifest colors");
            loaded.LoadProjectSettingsFromManifest(new ProjectManifest());
            Equal("default", BiomeEnvironment.Biome, "New project resets global biome");
            foreach (var preset in BiomeEnvironment.Presets)
            {
                panel.Biome = preset.Id;
                panel.WriteProjectSettingsToManifest(manifest);
                loaded.LoadProjectSettingsFromManifest(manifest);
                Equal(preset.Id, BiomeEnvironment.Biome, "Preset applies on project load");
            }
        }
        finally
        {
            BiomeEnvironment.Apply(previousBiome, new BiomeColors());
            typeof(BlockRegistry).GetProperty(nameof(BlockRegistry.VersionRoot))!.SetValue(null, previousRoot);
        }
    }

    private static unsafe void BiomeGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Biome regression", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            using var fixture = new Fixture();
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var blocks = (List<string>)typeof(BlockRegistry).GetField("_blocks", flags)!.GetValue(null)!;
            var variants = (Dictionary<string, List<BlockVariantEntry>>)typeof(BlockRegistry).GetField("_variants", flags)!.GetValue(null)!;
            var models = (Dictionary<(string, string, string), ResolvedBlockModel>)typeof(BlockRegistry).GetField("_resolvedCache", flags)!.GetValue(null)!;
            var textures = new List<uint>();
            foreach (string key in new[] { "biome_shared", "water_still", "water_flow" })
            {
                uint texture = gl.GenTexture();
                textures.Add(texture);
                gl.BindTexture(GLEnum.Texture2D, texture);
                byte[] white = [255, 255, 255, 255];
                fixed (byte* pixels = white)
                    gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
                gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
                TerrainAtlas.Textures.Add(key, texture);
                TerrainAtlas.TilePixels.Add(key, white);
            }
            var cases = new (string Block, BiomeTintKind Kind)[]
            {
                ("grass_block", BiomeTintKind.Grass), ("vine", BiomeTintKind.Foliage),
                ("oak_leaves", BiomeTintKind.Oak), ("spruce_leaves", BiomeTintKind.Spruce),
                ("birch_leaves", BiomeTintKind.Birch), ("jungle_leaves", BiomeTintKind.Jungle),
                ("acacia_leaves", BiomeTintKind.Acacia), ("dark_oak_leaves", BiomeTintKind.DarkOak),
                ("mangrove_leaves", BiomeTintKind.Mangrove), ("water", BiomeTintKind.Water),
                ("stone", BiomeTintKind.None), ("cherry_leaves", BiomeTintKind.None),
                ("pale_oak_leaves", BiomeTintKind.None)
            };
            var palette = new BiomeColors();
            foreach (var kind in Enum.GetValues<BiomeTintKind>())
                if (kind != BiomeTintKind.None)
                    palette.SetColor(kind, (int)kind * 0x10203);
            BiomeEnvironment.Apply("custom", palette);
            Equal(BiomeTintKind.Water, MinecraftModelMesh.GetTextureTintKind("resourcepack:test:block/water_still"),
                "Resource-pack water floor retains water category");
            Equal(BiomeTintKind.None, MinecraftModelMesh.GetTextureTintKind("stone"), "Untinted floor texture");
            var viewport = new Viewport { Gl = gl };
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            var meshes = new List<Mesh>();
            try
            {
                foreach (var (block, kind) in cases)
                {
                    blocks.Add(block);
                    variants.Add(block, [new BlockVariantEntry { ModelPath = "minecraft:block/" + block }]);
                    var model = new ResolvedBlockModel();
                    var element = new BlockModelElement();
                    if (block == "vine") { element.From = [2, 0, 2]; element.To = [14, 16, 14]; }
                    foreach (string face in new[] { "up", "down", "north", "south", "west", "east" })
                        element.Faces.Add(face, new BlockModelFace
                        {
                            Texture = "block/biome_shared",
                            TintIndex = block == "stone" || (block == "grass_block" && face != "up") ? -1 : 0
                        });
                    model.Elements.Add(element);
                    models.Add(("", "", "block/" + block), model);
                    var built = MinecraftModelMesh.Build(gl, model, blockName: block);
                    meshes.AddRange(built);
                    Check(built.All(m => m.EffectiveWindKind == WindSettings.ClassifyTexture(block)),
                        block + ": standalone wind classification survives shared custom textures");
                    Check(built.Any(m => m.BiomeTint == kind), block + ": standalone tint category");
                    if (block == "grass_block")
                        Check(built.Any(m => m.BiomeTint == BiomeTintKind.None), "Untinted grass dirt faces stay separate");
                }

                var assembly = typeof(SpawnMenu).Assembly;
                var boundsType = assembly.GetType("MineImatorSimplyRemade.core.world.WorldBounds", true)!;
                var volumeType = assembly.GetType("MineImatorSimplyRemade.core.world.WorldVolume", true)!;
                var bounds = Activator.CreateInstance(boundsType, [0, 0, 0, cases.Length * 2 - 1, 0, 0])!;
                var volume = Activator.CreateInstance(volumeType, [bounds])!;
                for (int i = 0; i < cases.Length; i++)
                    volumeType.GetMethod("Set")!.Invoke(volume, [i * 2, 0, 0, "minecraft:" + cases[i].Block]);
                string path = fixture.NewPath(".schem");
                volumeType.GetMethod("SaveSchematic")!.Invoke(volume, [path, null]);
                var root = spawn.SpawnSchematicFromPath(path) ?? throw new InvalidOperationException("Biome terrain import failed");
                meshes.AddRange(root.Visuals);
                Check(root.Visuals.Any(m => m.BiomeTint == BiomeTintKind.None && m.EffectiveWindKind == 1) &&
                    root.Visuals.Any(m => m.BiomeTint == BiomeTintKind.None && m.EffectiveWindKind == 0),
                    "Shared-texture untinted leaves and stone retain independent terrain wind batches");
                Check(root.Visuals.Any(m => m.EffectiveWindKind == 2) && root.Visuals.Any(m => m.EffectiveWindKind == 3),
                    "Merged terrain preserves water ripples and rooted plant wind");
                foreach (var kind in Enum.GetValues<BiomeTintKind>())
                    Check(root.Visuals.Any(m => m.BiomeTint == kind), kind + ": merged scenery retains category, including shared-texture leaves");
                root.SetExplicitMaterialSettings();
                root.MaterialSettings.AlbedoColor = new vec4(.5f, .75f, 1f, 1f);
                root.ApplyMaterialSettingsToMeshes();
                foreach (var mesh in root.Visuals)
                    Near(mesh.Albedo * palette.GetTint(mesh.BiomeTint), mesh.RenderAlbedo, "Biome multiplies editable material color");

                viewport.InitGroundPlane();
                Check(viewport.SetGroundPlaneTexture("block", "water_still"), "Water ground texture applies");
                var ground = (Mesh)typeof(Viewport).GetField("_groundPlane", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(viewport)!;
                meshes.Add(ground);
                Equal(BiomeTintKind.Water, ground.BiomeTint, "Ground keeps live water category");
                palette.Water = 0xff00ff;
                BiomeEnvironment.Apply("custom", palette);
                Near(new vec3(1, 0, 1), ground.RenderAlbedo, "Ground updates live without reselecting its texture");

                var oak = meshes.First(m => m.BiomeTint == BiomeTintKind.Oak);
                using var clone = oak.Clone();
                Equal(oak.EffectiveWindKind, clone.EffectiveWindKind, "Duplicate preserves wind classification");
                Equal(BiomeTintKind.Oak, clone.BiomeTint, "Duplicate preserves tint category");
                palette.Oak = 0;
                BiomeEnvironment.Apply("custom", palette);
                Near(vec3.Zero, oak.RenderAlbedo, "Black tint applies without geometry rebuild");
                palette.Oak = 0xff0000;
                BiomeEnvironment.Apply("custom", palette);
                Near(new vec3(1, 0, 0), clone.RenderAlbedo, "Duplicate updates live after leaving black");
                using var preview = new PreviewRenderer(gl, 64) { AutoRotateSpeed = 0 };
                oak.Unlit = true;
                byte[] Draw()
                {
                    preview.Render([oak], "biome", 0, centerMeshes: true, isolatedLighting: true);
                    byte[] pixels = new byte[64 * 64 * 4];
                    gl.BindTexture(GLEnum.Texture2D, preview.ColorTexture);
                    fixed (byte* p = pixels) gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
                    return pixels;
                }
                byte[] red = Draw();
                palette.Oak = 0x0000ff;
                BiomeEnvironment.Apply("custom", palette);
                byte[] blue = Draw();
                int changed = 0;
                for (int i = 0; i < red.Length; i += 4)
                    if (red[i] > red[i + 2] + 30 && blue[i + 2] > blue[i] + 30) changed++;
                Check(changed > 100, "Existing GPU geometry changes from red to blue in actual rendered pixels");
                Equal(GLEnum.NoError, gl.GetError(), "No OpenGL errors");
            }
            finally
            {
                foreach (var mesh in meshes) mesh.Dispose();
                foreach (uint texture in textures) gl.DeleteTexture(texture);
                TerrainAtlas.Textures.Clear();
                TerrainAtlas.TilePixels.Clear();
                BiomeEnvironment.Apply("default", new BiomeColors());
            }
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
