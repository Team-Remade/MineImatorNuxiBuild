using System.Reflection;
using System.Text.Json;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;
using Shader = MineImatorSimplyRemade.core.mdl.Shader;

internal static partial class Program
{
    private static void WindPersistence(Fixture fixture)
    {
        var defaults = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectRenderSettings)!;
        Check(!defaults.WindEnabled, "Old projects keep wind disabled");
        Near(.1f, defaults.WindSpeed, "Default speed is 10%");
        Near(.5f, defaults.WindStrength, "Default strength");
        Near(45f, defaults.WindDirection, "Default direction in degrees");
        Near(.2f, defaults.WindDirectionalSpeed, "Default directional speed is 20%");
        Near(1.5f, defaults.WindDirectionalStrength, "Default directional strength");
        var saved = ProjectManager.Instance.Manifest.Settings;
        try
        {
            var panel = new PropertiesPanel
            {
                WindEnabled = true, WindSpeed = .37f, WindStrength = .8f,
                WindDirection = -73f, WindDirectionalSpeed = .62f, WindDirectionalStrength = 2.4f
            };
            var manifest = new ProjectManifest();
            panel.WriteProjectSettingsToManifest(manifest);
            var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(manifest, AppJsonContext.Default.ProjectManifest),
                AppJsonContext.Default.ProjectManifest)!;
            var loaded = new PropertiesPanel();
            loaded.LoadProjectSettingsFromManifest(restored);
            Check(loaded.WindEnabled, "Enabled persists");
            string[] properties = ["WindSpeed", "WindStrength", "WindDirection", "WindDirectionalSpeed", "WindDirectionalStrength"];
            foreach (string property in properties)
                Equal(typeof(PropertiesPanel).GetField(property)!.GetValue(panel)!,
                    typeof(PropertiesPanel).GetField(property)!.GetValue(loaded)!, property + " persists");
            ProjectManager.Instance.Manifest.Settings = restored.Settings;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var add = typeof(PropertiesPanel).GetMethod("AddBackgroundKeyframe", flags)!;
            foreach (string property in properties.Append("WindEnabled"))
            {
                add.Invoke(loaded, [property, 0]);
                var field = typeof(PropertiesPanel).GetField(property)!;
                field.SetValue(loaded, field.FieldType == typeof(bool) ? false : (object)((float)field.GetValue(loaded)! + .2f));
                add.Invoke(loaded, [property, 10]);
            }
            typeof(PropertiesPanel).GetMethod("ApplyBackgroundAnimation", flags)!.Invoke(loaded, [5]);
            Check(loaded.WindEnabled, "Enabled uses stepped animation");
            foreach (string property in properties)
                Near((float)typeof(PropertiesPanel).GetField(property)!.GetValue(panel)! + .1f,
                    (float)typeof(PropertiesPanel).GetField(property)!.GetValue(loaded)!, property + " interpolates");
            loaded.LoadProjectSettingsFromManifest(new ProjectManifest());
            Check(!loaded.WindEnabled, "New projects reset wind");
            Near(.1f, loaded.WindSpeed, "New projects reset speed");
        }
        finally { ProjectManager.Instance.Manifest.Settings = saved; }

        var obj = new SceneObject { AffectedByWind = true, TerrainAffectedByWind = false };
        var (archive, _) = fixture.Roundtrip(obj);
        Check(archive.Root.AffectedByWind && !archive.Root.TerrainAffectedByWind, "Archive preserves both toggles");
        var cloneEntry = (ProjectSceneObjectEntry)typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [archive.Root])!;
        Check(cloneEntry.AffectedByWind && !cloneEntry.TerrainAffectedByWind, "Library preserves toggles");
        var duplicate = (SceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(new SceneTree(), [obj, new Dictionary<string, string>()])!;
        Check(duplicate.AffectedByWind && !duplicate.TerrainAffectedByWind, "Duplicate preserves toggles");
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(obj);
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { obj }])!;
        ulong before = Hash();
        obj.AffectedByWind = false;
        Check(before != Hash(), "Whole-mesh wind participates in dirty detection");
        obj.AffectedByWind = true;
        obj.TerrainAffectedByWind = true;
        Check(before != Hash(), "Terrain wind participates in dirty detection");
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [obj, snapshot, null]);
        Equal(before, Hash(), "Undo restores wind flags");
        foreach (string key in new[] { "oak_leaves", "pack:block/cherry_leaves", "leaves_oak", "short_grass",
            "fern", "poppy", "water_still", "water_flow", "kelp", "wheat_stage7", "double_plant_rose_top" })
            Check(WindSettings.ClassifyTexture(key) != 0, key + " receives terrain wind");
        foreach (string key in new[] { "stone", "grass_block_top", "grass_side", "oak_log", "glass", "oak_leaves_n", "water_still_s" })
            Equal(0, WindSettings.ClassifyTexture(key), key + " remains solid");
        Equal(1, WindSettings.ClassifyBlock("minecraft:oak_leaves", "pack:custom/shared"), "Custom leaf textures retain block semantics");
        Equal(0, WindSettings.ClassifyBlock("minecraft:stone", "pack:custom/shared"), "Shared solid texture stays solid");
        Equal(4, WindSettings.ClassifyBlock("tall_grass", "tall_grass_top"), "Tall plant halves retain continuous weights");

        var savedWind = Mesh.Wind;
        try
        {
            var panel = new PropertiesPanel { WindEnabled = true };
            var viewport = new Viewport { PropertiesPanel = panel };
            var root = new SceneObject { AffectedByWind = true, TerrainAffectedByWind = false };
            var child = new SceneObject { TerrainAffectedByWind = true };
            var wholeMesh = new Mesh(null!);
            var terrainMesh = new Mesh(null!) { MinecraftTextureKey = "water_still" };
            root.AddMesh(wholeMesh);
            child.AddMesh(terrainMesh);
            root.AddChild(child);
            viewport.SceneObjects.Add(root);
            typeof(Viewport).GetMethod("PrepareSceneEnvironment", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(viewport, [new Camera()]);
            Check(Mesh.Wind.Enabled && wholeMesh.WindActive && terrainMesh.WindActive,
                "Scene preparation applies global and independent child wind settings");
            Check(wholeMesh.AffectedByWind && !wholeMesh.TerrainAffectedByWind &&
                !terrainMesh.AffectedByWind && terrainMesh.TerrainAffectedByWind, "Per-object toggles are not conflated");
            var signature = typeof(Viewport).GetMethod("ComputeShadowCacheSignature", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (bool pointLights in new[] { false, true })
            {
                Mesh.Wind = Mesh.Wind with { Time = 0f };
                int first = (int)signature.Invoke(viewport, [viewport.SceneObjects, pointLights])!;
                Mesh.Wind = Mesh.Wind with { Time = 2f };
                Check(first != (int)signature.Invoke(viewport, [viewport.SceneObjects, pointLights])!,
                    "Wind time invalidates directional and point shadow caches");
                Mesh.Wind = Mesh.Wind with { Time = 0f };
                Equal(first, (int)signature.Invoke(viewport, [viewport.SceneObjects, pointLights])!,
                    "Seeking back reproduces shadow cache state");
            }
        }
        finally { Mesh.Wind = savedWind; }
    }

    private static unsafe void WindGraphics(bool testVeldrid = false)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(128, 128, "Wind regression", null, null);
        Check(window != null, "OpenGL context");
        var previousWind = Mesh.Wind;
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            using var mesh = new Mesh(gl) { TerrainAffectedByWind = true };
            mesh.Vertices.AddRange([new(.1f, 0, 0), new(.6f, 0, 0), new(.1f, 1, 0)]);
            mesh.Normals.AddRange([vec3.UnitZ, vec3.UnitZ, vec3.UnitZ]);
            mesh.TexCoords.AddRange([new(0, 1), new(1, 1), new(0, 0)]);
            mesh.Upload();
            Mesh.Wind = new(true, .1f, .5f, 45f, .2f, 1.5f, 1.25f);
            mat4 model = mat4.Translate(new vec3(2f, .3f, -1f)) * mat4.Scale(new vec3(1.3f, .8f, .7f));
            var programs = new Dictionary<string, uint>();
            uint buffer = gl.GenBuffer();
            try
            {
                foreach (var (vertex, fragment) in new[] { ("simple.vert", "simple.frag"), ("pick.vert", "pick.frag"),
                    ("pick.vert", "silhouette.frag"), ("shadow_depth.vert", "shadow_depth.frag"),
                    ("point_shadow_depth.vert", "point_shadow_depth.frag") })
                {
                    using var material = new Shader(gl);
                    material.CompileShader(vertex, fragment);
                    gl.GetProgram(material.ShaderProgram, GLEnum.LinkStatus, out int linked);
                    Equal(1, linked, vertex + "/" + fragment + " links");
                    if (programs.ContainsKey(vertex)) continue;
                    string source = (string)typeof(Shader).GetMethod("LoadShaderSource", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [vertex])!;
                    uint shader = gl.CreateShader(GLEnum.VertexShader);
                    gl.ShaderSource(shader, source);
                    gl.CompileShader(shader);
                    uint program = gl.CreateProgram();
                    gl.AttachShader(program, shader);
                    gl.TransformFeedbackVaryings(program, 1, new[] { "gl_Position" }, GLEnum.InterleavedAttribs);
                    gl.LinkProgram(program);
                    gl.GetProgram(program, GLEnum.LinkStatus, out linked);
                    Check(linked == 1, vertex + " transform feedback links: " + gl.GetProgramInfoLog(program));
                    gl.DeleteShader(shader);
                    programs.Add(vertex, program);
                }

                float[] Capture(string vertex)
                {
                    uint program = programs[vertex];
                    gl.UseProgram(program);
                    void Matrix(string name, mat4 value)
                    {
                        int loc = gl.GetUniformLocation(program, name);
                        if (loc < 0) return;
                        float[] values = value.ToArray();
                        fixed (float* data = values) gl.UniformMatrix4(loc, 1, false, data);
                    }
                    Matrix("uMVP", model);
                    Matrix("uModel", model);
                    Matrix("uLightViewProj", mat4.Identity);
                    using var proxy = new Shader(gl);
                    typeof(Shader).GetProperty(nameof(Shader.ShaderProgram))!.SetValue(proxy, program);
                    mesh.ApplySkinningUniforms(proxy);
                    gl.BindBuffer(GLEnum.TransformFeedbackBuffer, buffer);
                    gl.BufferData(GLEnum.TransformFeedbackBuffer, 12 * sizeof(float), null, GLEnum.StreamRead);
                    gl.BindBufferBase(GLEnum.TransformFeedbackBuffer, 0, buffer);
                    gl.Enable(GLEnum.RasterizerDiscard);
                    gl.BeginTransformFeedback(GLEnum.Triangles);
                    mesh.RenderPickPass(proxy, model);
                    gl.EndTransformFeedback();
                    gl.Disable(GLEnum.RasterizerDiscard);
                    float[] result = new float[12];
                    fixed (float* data = result) gl.GetBufferSubData(GLEnum.TransformFeedbackBuffer, 0, 12 * sizeof(float), data);
                    // The proxy does not own the transform-feedback program.
                    typeof(Shader).GetProperty(nameof(Shader.ShaderProgram))!.SetValue(proxy, 0u);
                    return result;
                }
                void Same(float[] expected, float[] actual, string label)
                {
                    for (int i = 0; i < expected.Length; i++) Near(expected[i], actual[i], label);
                }
                float Difference(float[] a, float[] b) => a.Zip(b, (x, y) => MathF.Abs(x - y)).Sum();

                foreach (string texture in new[] { "stone", "oak_leaves", "short_grass", "water_still" })
                {
                    mesh.MinecraftTextureKey = texture;
                    Mesh.Wind = Mesh.Wind with { Enabled = false };
                    float[] still = Capture("simple.vert");
                    Mesh.Wind = Mesh.Wind with { Enabled = true };
                    float[] terrain = Capture("simple.vert");
                    if (texture == "stone") Same(still, terrain, "Solid terrain stays still");
                    else Check(Difference(still, terrain) > .001f, texture + " visibly deforms on the GPU");
                    if (texture == "short_grass")
                        for (int i = 0; i < 8; i++) Near(still[i], terrain[i], "Plant roots stay anchored");
                    foreach (string pass in programs.Keys)
                        Same(terrain, Capture(pass), texture + ": color, picking, silhouette and shadows match");

                    mesh.AffectedByWind = true;
                    float[] both = Capture("simple.vert");
                    mesh.TerrainAffectedByWind = false;
                    Same(both, Capture("simple.vert"), "Both toggles equal whole-mesh wind alone");
                    Check(Difference(still, both) > .001f, "Whole-mesh wind affects every material, including stone");
                    foreach (string pass in programs.Keys)
                        Same(both, Capture(pass), "Whole-mesh wind matches every pass");
                    Mesh.Wind = Mesh.Wind with { Time = 9f };
                    Check(Difference(both, Capture("simple.vert")) > .001f, "Timeline motion changes deformation");
                    Mesh.Wind = Mesh.Wind with { Time = 1.25f };
                    Same(both, Capture("simple.vert"), "Seeking back reproduces exact deformation");
                    mesh.AffectedByWind = false;
                    mesh.TerrainAffectedByWind = true;
                }
                mesh.AffectedByWind = true;
                Mesh.Wind = new(true, .1f, 0f, 0f, .2f, 1.5f, 1.25f);
                float[] alongX = Capture("simple.vert");
                Mesh.Wind = Mesh.Wind with { Direction = 90f };
                float[] alongZ = Capture("simple.vert");
                Mesh.Wind = Mesh.Wind with { Enabled = false };
                float[] rest = Capture("simple.vert");
                Check(MathF.Abs(alongX[0] - rest[0]) > .001f && MathF.Abs(alongZ[2] - rest[2]) > .001f,
                    "Direction controls the world-space X/Z wind heading");
                Near(rest[2], alongX[2], "Zero-degree wind has no Z displacement");
                Near(rest[0], alongZ[0], "Ninety-degree wind has no X displacement");
                Mesh.Wind = Mesh.Wind with { Enabled = true, Strength = .5f, DirectionalStrength = 0f };
                float[] flutter = Capture("simple.vert");
                Mesh.Wind = Mesh.Wind with { Speed = .8f };
                Check(Difference(flutter, Capture("simple.vert")) > .001f, "Speed controls flutter frequency");
                Mesh.Wind = Mesh.Wind with { Strength = 0f, DirectionalStrength = 1.5f };
                float[] gust = Capture("simple.vert");
                Mesh.Wind = Mesh.Wind with { DirectionalSpeed = .7f };
                Check(Difference(gust, Capture("simple.vert")) > .001f, "Directional speed controls gust travel");
                Mesh.Wind = Mesh.Wind with { Strength = 0f, DirectionalStrength = 0f };
                float[] zero = Capture("simple.vert");
                Mesh.Wind = Mesh.Wind with { Enabled = false };
                Same(zero, Capture("simple.vert"), "Zero strengths are an exact no-op");
                using (var preview = new PreviewRenderer(gl, 128) { AutoRotateSpeed = 0 })
                {
                    mesh.Unlit = true;
                    byte[] DrawOpenGl(bool enabled)
                    {
                        Mesh.Wind = new(enabled, .1f, .5f, 45f, .2f, 1.5f, 1.25f);
                        preview.Render([mesh], "wind", 0, centerMeshes: true, isolatedLighting: true);
                        byte[] pixels = new byte[128 * 128 * 4];
                        gl.BindTexture(GLEnum.Texture2D, preview.ColorTexture);
                        fixed (byte* data = pixels) gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
                        return pixels;
                    }
                    byte[] still = DrawOpenGl(false);
                    byte[] moving = DrawOpenGl(true);
                    Check(still.Where((value, i) => value != moving[i]).Count() > 20,
                        "OpenGL wind changes actual rendered pixels");
                    Check(moving.SequenceEqual(DrawOpenGl(true)), "OpenGL rendering is repeatable");
                }
                if (testVeldrid)
                {
                    using var device = Veldrid.GraphicsDevice.CreateD3D11(new Veldrid.GraphicsDeviceOptions(
                        debug: false, swapchainDepthFormat: null, syncToVerticalBlank: false,
                        resourceBindingModel: Veldrid.ResourceBindingModel.Improved,
                        preferDepthRangeZeroToOne: true, preferStandardClipSpaceYDirection: true));
                    using var renderer = new VeldridSceneRenderer(device, gl);
                    uint output = gl.GenTexture();
                    gl.BindTexture(GLEnum.Texture2D, output);
                    gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, 128, 128, 0,
                        GLEnum.Rgba, GLEnum.UnsignedByte, null);
                    try
                    {
                        mesh.DoubleSided = true;
                        byte[] DrawVeldrid(bool enabled)
                        {
                            Mesh.Wind = new(enabled, .1f, .5f, 45f, .2f, 1.5f, 1.25f);
                            renderer.BeginFrame(128, 128, mat4.Identity, mat4.Identity, System.Numerics.Vector4.Zero);
                            renderer.Draw(mesh, mat4.Identity, forceUnshaded: true);
                            renderer.EndFrame(output);
                            byte[] pixels = new byte[128 * 128 * 4];
                            gl.BindTexture(GLEnum.Texture2D, output);
                            fixed (byte* data = pixels) gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
                            return pixels;
                        }
                        byte[] still = DrawVeldrid(false);
                        byte[] moving = DrawVeldrid(true);
                        Check(still.Where((value, i) => value != moving[i]).Count() > 20,
                            "Veldrid wind changes actual rendered pixels");
                        Check(moving.SequenceEqual(DrawVeldrid(true)), "Veldrid rendering is repeatable");
                        mesh.AffectedByWind = false;
                        mesh.TerrainAffectedByWind = true;
                        mesh.MinecraftTextureKey = "short_grass";
                        byte[] plant = DrawVeldrid(true);
                        mesh.TextureOffset = new vec2(.23f, .7f);
                        mesh.TextureRepeat = new vec2(3f, 6f);
                        mesh.TextureMirror = new bvec2(true, true);
                        Check(plant.SequenceEqual(DrawVeldrid(true)),
                            "Veldrid plant anchoring is independent of texture transforms");
                    }
                    finally { gl.DeleteTexture(output); }
                }
                Equal(GLEnum.NoError, gl.GetError(), "No OpenGL errors");
            }
            finally
            {
                foreach (uint program in programs.Values) gl.DeleteProgram(program);
                gl.DeleteBuffer(buffer);
            }
        }
        finally
        {
            Mesh.Wind = previousWind;
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
