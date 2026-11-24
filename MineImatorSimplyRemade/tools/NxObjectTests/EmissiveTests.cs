using System.IO.Compression;
using System.Reflection;
using Cyotek.Data.Nbt;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static unsafe void LightSpecularGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Light specular regression", null, null);
        Check(window != null, "OpenGL context");
        var previousUbo = Mesh.SceneUBO;
        var previousCamera = Mesh.GlobalCameraPosition;
        var previousLights = Mesh.PointLights.ToArray();
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            using var lighting = new SceneUniformBuffer(gl);
            Mesh.SceneUBO = lighting;
            Mesh.GlobalCameraPosition = new vec3(0f, 0f, 2f);
            lighting.Upload(mat4.Identity, vec3.Zero, vec3.UnitZ, vec3.Zero,
                vec3.UnitZ, vec3.Zero, vec3.UnitZ, vec3.Zero, 0, false, false, false);
            using var mesh = new Mesh(gl,
                [new vec3(-1f, -1f, 0f), new vec3(1f, -1f, 0f), new vec3(0f, 1f, 0f)],
                [vec3.UnitZ, vec3.UnitZ, vec3.UnitZ], [0, 1, 2])
            {
                Albedo = new vec3(0.3f), Roughness = 0.4f
            };
            mesh.Upload();
            uint framebuffer = gl.GenFramebuffer();
            uint[] textures = new uint[3];
            try
            {
                gl.BindFramebuffer(GLEnum.Framebuffer, framebuffer);
                for (int i = 0; i < textures.Length; i++)
                {
                    textures[i] = gl.GenTexture();
                    gl.BindTexture(GLEnum.Texture2D, textures[i]);
                    gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba32f, 64, 64, 0,
                        PixelFormat.Rgba, PixelType.Float, null);
                    gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
                    gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
                    gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0 + i,
                        GLEnum.Texture2D, textures[i], 0);
                }
                gl.DrawBuffers([GLEnum.ColorAttachment0, GLEnum.ColorAttachment1, GLEnum.ColorAttachment2]);
                Equal(GLEnum.FramebufferComplete, gl.CheckFramebufferStatus(GLEnum.Framebuffer), "Light test framebuffer");
                gl.Viewport(0, 0, 64, 64);
                gl.Disable(GLEnum.DepthTest);
                gl.Disable(GLEnum.Blend);
                var viewport = new Viewport();
                var light = new LightSceneObject
                {
                    Position = new vec3(0f, 0f, 0.5f), Rotation = new vec3(0f, MathF.PI, 0f),
                    PivotOffset = vec3.Zero,
                    LightEnergy = 0.3f, LightRange = 5f
                };
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var collect = typeof(Viewport).GetMethod("CollectPointLights", flags)!;
                object renderMode = Enum.Parse(collect.GetParameters()[1].ParameterType, "Rendered");
                var set = typeof(Timeline).GetMethod("SetPropertyValue", flags)!;
                var timeline = new Timeline();

                (float color, float bounce) Sample(float specular)
                {
                    set.Invoke(timeline, [light, "light.specular", specular]);
                    Mesh.PointLights.Clear();
                    collect.Invoke(viewport, [new List<SceneObject> { light }, renderMode,
                        new Dictionary<LightSceneObject, int>(), true, Mesh.PointLights]);
                    mesh.Render(mat4.Identity, mat4.Identity, mat4.Identity);
                    float* pixel = stackalloc float[4];
                    gl.ReadBuffer(GLEnum.ColorAttachment0);
                    gl.ReadPixels(32, 32, 1, 1, PixelFormat.Rgba, PixelType.Float, pixel);
                    Check(pixel[3] > 0.99f, "Light test draws geometry");
                    float color = pixel[0];
                    gl.ReadBuffer(GLEnum.ColorAttachment2);
                    gl.ReadPixels(32, 32, 1, 1, PixelFormat.Rgba, PixelType.Float, pixel);
                    return (color, pixel[0]);
                }

                foreach (var type in new[] { LightType.Point, LightType.Spot })
                foreach (bool pbr in new[] { false, true })
                foreach (float metallic in pbr ? new[] { 0f, 1f } : new[] { 0f })
                {
                    light.Type = type;
                    mesh.PbrEnabled = pbr;
                    mesh.Metallic = metallic;
                    var off = Sample(0f);
                    var normal = Sample(0.5f);
                    var full = Sample(1f);
                    Check(normal.color > off.color + 0.01f && full.color > normal.color + 0.01f,
                        $"{type}, PBR={pbr}, metallic={metallic}: animated specular changes real pixels " +
                        $"(off={off.color}, normal={normal.color}, full={full.color}, light={Mesh.PointLights[0]})");
                    float Linear(float value) => pbr ? MathF.Pow(value, 2.2f) : value;
                    Check(Math.Abs(Linear(normal.color) - (Linear(off.color) + Linear(full.color)) * 0.5f) < 0.001f,
                        "Specular scales only the highlight, linearly");
                    Near(off.bounce, normal.bounce, "Specular does not affect diffuse GI");
                    Near(off.bounce, full.bounce, "Full specular does not leak into GI");
                    light.LightEnergy = 0f;
                    Near(Sample(0f).color, Sample(1f).color, "Zero-energy lights have no highlight");
                    light.LightEnergy = 0.3f;
                    light.LightRange = 0.1f;
                    Near(Sample(0f).color, Sample(1f).color, "Highlights respect light range");
                    light.LightRange = 5f;
                    if (type == LightType.Spot)
                    {
                        light.Rotation = vec3.Zero;
                        Near(Sample(0f).color, Sample(1f).color, "Spot highlights respect the cone");
                        light.Rotation = new vec3(0f, MathF.PI, 0f);
                    }
                    mesh.Unlit = true;
                    Near(Sample(0f).color, Sample(1f).color, "Unlit materials ignore light specular");
                    mesh.Unlit = false;
                }
                Equal(GLEnum.NoError, gl.GetError(), "Light specular GL errors");
            }
            finally
            {
                gl.BindFramebuffer(GLEnum.Framebuffer, 0);
                foreach (uint texture in textures) gl.DeleteTexture(texture);
                gl.DeleteFramebuffer(framebuffer);
            }
        }
        finally
        {
            Mesh.SceneUBO = previousUbo;
            Mesh.GlobalCameraPosition = previousCamera;
            Mesh.PointLights.Clear();
            Mesh.PointLights.AddRange(previousLights);
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    private static unsafe void EmissiveGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Emissive terrain test", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            const BindingFlags registryFlags = BindingFlags.Static | BindingFlags.NonPublic;
            var blocks = (List<string>)typeof(BlockRegistry).GetField("_blocks", registryFlags)!.GetValue(null)!;
            var variants = (Dictionary<string, List<BlockVariantEntry>>)typeof(BlockRegistry)
                .GetField("_variants", registryFlags)!.GetValue(null)!;
            var models = (Dictionary<(string, string, string), ResolvedBlockModel>)typeof(BlockRegistry)
                .GetField("_resolvedCache", registryFlags)!.GetValue(null)!;
            Check(blocks.Count == 0 && variants.Count == 0 && models.Count == 0 && TerrainAtlas.Textures.Count == 0,
                "Test registry starts empty");
            foreach (string block in new[] { "glowstone", "torch", "lava", "sea_lantern", "stone" })
            {
                blocks.Add(block);
                variants.Add(block, [new BlockVariantEntry { ModelPath = "minecraft:block/" + block }]);
                var model = new ResolvedBlockModel();
                var element = new BlockModelElement();
                if (block == "torch")
                {
                    element.From = [6f, 0f, 6f];
                    element.To = [10f, 10f, 10f];
                }
                foreach (string face in new[] { "up", "down", "north", "south", "west", "east" })
                {
                    string texture = block + (face == "up" ? "_top" : "");
                    element.Faces.Add(face, new BlockModelFace { Texture = "block/" + texture });
                    if (!TerrainAtlas.Textures.ContainsKey(texture))
                        TerrainAtlas.Textures.Add(texture, gl.GenTexture());
                }
                model.Elements.Add(element);
                models.Add(("", "", "block/" + block), model);
            }
            TerrainAtlas.Textures.Add("lava_still", gl.GenTexture());
            TerrainAtlas.Textures.Add("lava_flow", gl.GenTexture());
            var viewport = new Viewport();
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            using var fixture = new Fixture();
            var collect = typeof(Viewport).GetMethod("AddEmissivePointLightsForObject",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var lights = new List<(vec3 pos, vec3 color, float range, float energy, int shadowIndex,
                vec3 direction, float indirectEnergy, float spotCosOuterAngle, float spotCosInnerAngle, float specular, bool realisticFalloff)>();

            void Collect(SceneObject obj)
            {
                lights.Clear();
                typeof(Viewport).GetMethod("ResetModifierEvaluation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewport, null);
                collect.Invoke(viewport, [obj, lights]);
            }

            void ExpectPositions(SceneObject obj, string label)
            {
                Collect(obj);
                Equal(2, lights.Count, label + ": one light per block, not per texture or face");
                mat4 world = obj.GetWorldMatrix();
                foreach (float x in new[] { -20f, 20f })
                {
                    vec3 expected = (world * new vec4(x, 1.5f, 0f, 1f)).xyz;
                    Check(lights.Any(light => (light.pos - expected).Length < 0.0001f),
                        label + ": light at the emitting block");
                }
                Check(lights.All(light => light.shadowIndex == -1), label + ": proxy remains unshadowed");
                Check(lights.All(light => light.specular == 0f), label + ": proxy adds no specular highlights");
            }

            foreach (var (block, legacy) in new[]
            {
                ("glowstone", false), ("glowstone", true), ("torch", false),
                ("lava", false), ("sea_lantern", false)
            })
            {
                string path = WriteEmissiveSchematic(fixture, block, legacy);
                var root = spawn.SpawnSchematicFromPath(path) ??
                    throw new InvalidOperationException("Failed importing " + block);
                try
                {
                    root.SetExplicitMaterialSettings();
                    root.ApplyMaterialSettingsToMeshes();
                    ExpectPositions(root, block);
                    Check(lights.All(light => light.range > 3f && light.range < 8f),
                        "Range is block-sized, independent of the 40-block distance between emitters");
                    if (block == "glowstone")
                    {
                        Check(root.Visuals.Where(mesh => mesh.AutoEmissionCenters.Count > 0)
                            .All(mesh => mesh.AutoEmissionCenters.Count == 2),
                            "Separated glowstone blocks remain merged within each texture batch");
                        Check(root.Visuals.Where(mesh => mesh.AutoEmissionLevel == 0)
                            .All(mesh => mesh.AutoEmissionCenters.Count == 0),
                            "Non-emissive terrain has no proxy sources");
                    }

                    root.SetLocalPosition(new vec3(7f, -3f, 11f));
                    root.Rotation = new vec3(15f, 70f, -10f);
                    root.Scale = new vec3(2f, 0.75f, 1.5f);
                    ExpectPositions(root, block + " transformed");
                    var parent = new SceneObject { Position = new vec3(-6f, 4f, 8f) };
                    parent.AddChild(root);
                    ExpectPositions(root, block + " parented");
                    parent.RemoveChild(root);

                    var duplicate = new SceneObject();
                    foreach (var mesh in root.Visuals)
                        duplicate.AddMesh(mesh.Clone());
                    try
                    {
                        ExpectPositions(duplicate, block + " cloned");
                        foreach (var mesh in duplicate.Visuals) mesh.AutoEmissionCenters.Clear();
                        ExpectPositions(root, block + " independent clone data");
                    }
                    finally
                    {
                        foreach (var mesh in duplicate.Visuals) mesh.Dispose();
                    }

                    root.SetExplicitMaterialSettings();
                    root.MaterialSettings.EmissionIndirectOnly = true;
                    root.ApplyMaterialSettingsToMeshes();
                    Collect(root);
                    Check(lights.Count == 2 && lights.All(light => light.energy == 0f && light.indirectEnergy > 0f),
                        "Indirect-only emission preserves block positions without direct energy");
                    root.MaterialSettings.AutoEmission = false;
                    root.ApplyMaterialSettingsToMeshes();
                    Collect(root);
                    Equal(0, lights.Count, "Auto-emission off removes imported block lights");
                    root.MaterialSettings.EmissionEnabled = true;
                    root.MaterialSettings.EmissionColor = new vec4(1f);
                    root.MaterialSettings.EmissionEnergy = 1f;
                    root.ApplyMaterialSettingsToMeshes();
                    Collect(root);
                    Equal(root.Visuals.Count, lights.Count, "Manual emission retains one light per mesh");
                    foreach (var mesh in root.Visuals)
                    {
                        vec3 expected = (root.GetWorldMatrix() * new vec4(mesh.BoundingSphereCenter, 1f)).xyz;
                        Check(lights.Any(light => (light.pos - expected).Length < 0.0001f),
                            "Manual emission retains the mesh-centered fallback");
                    }
                    root.MaterialSettings.AutoEmission = true;
                    root.MaterialSettings.EmissionEnabled = false;
                    root.ApplyMaterialSettingsToMeshes();
                    ExpectPositions(root, block + " re-enabled");
                    var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(root);
                    var restored = ProjectSceneSerializer.SpawnObjectFromEntry(snapshot, viewport, spawn) ??
                        throw new InvalidOperationException("Failed restoring emissive terrain");
                    try
                    {
                        ExpectPositions(restored, block + " restored from saved source");
                    }
                    finally
                    {
                        viewport.SceneObjects.Remove(restored);
                        foreach (var mesh in restored.Visuals) mesh.Dispose();
                    }
                    root.Modifiers = [new() { Count = 2, Distance = 100f }];
                    Collect(root);
                    Equal(4, lights.Count, "Array instances each retain both block lights");
                }
                finally
                {
                    viewport.SceneObjects.Remove(root);
                    foreach (var mesh in root.Visuals) mesh.Dispose();
                }
            }
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    private static string WriteEmissiveSchematic(Fixture fixture, string block, bool legacy)
    {
        const int width = 41, height = 3, length = 3;
        var blocks = new byte[width * height * length];
        blocks[(1 * length + 1) * width] = legacy ? (byte)89 : (byte)1;
        blocks[(1 * length + 1) * width + 40] = legacy ? (byte)89 : (byte)1;
        blocks[width + 20] = legacy ? (byte)1 : (byte)2;
        var root = new TagCompound("Schematic");
        root.Value.Add(new TagShort("Width", width));
        root.Value.Add(new TagShort("Height", height));
        root.Value.Add(new TagShort("Length", length));
        if (legacy)
        {
            root.Value.Add(new TagString("Materials", "Alpha"));
            root.Value.Add(new TagByteArray("Blocks", blocks));
            root.Value.Add(new TagByteArray("Data", new byte[blocks.Length]));
        }
        else
        {
            root.Value.Add(new TagInt("Version", 2));
            var palette = new TagCompound("Palette");
            palette.Value.Add(new TagInt("minecraft:air", 0));
            palette.Value.Add(new TagInt("minecraft:" + block, 1));
            palette.Value.Add(new TagInt("minecraft:stone", 2));
            root.Value.Add(palette);
            root.Value.Add(new TagByteArray("BlockData", blocks));
        }
        string path = fixture.File($"{block}-{legacy}.schem", "");
        using var output = File.Create(path);
        using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        new NbtDocument(root).Save(gzip);
        return path;
    }
}
