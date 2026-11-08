using System.Reflection;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static unsafe void SpawnPreviewGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Spawn preview test", null, null);
        Check(window != null, "OpenGL context");
        var previousLighting = Mesh.SceneUBO;
        var previousCamera = Mesh.GlobalCameraPosition;
        bool previousFog = Mesh.FogEnabled, previousHeightFog = Mesh.HeightFogEnabled;
        bool previousIndirect = Mesh.IndirectWorldEnabled, previousSss = Mesh.SssEnabled;
        var previousPoints = Mesh.PointLights.ToArray();
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            using var sceneLighting = new SceneUniformBuffer(gl);
            Mesh.SceneUBO = sceneLighting;
            MineImatorLoader.Instance.Initialize(gl);
            var viewport = new Viewport();
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var update = typeof(SpawnMenu).GetMethod("UpdatePreview", flags)!;
            var category = typeof(SpawnMenu).GetField("_selectedCategory", flags)!;
            var index = typeof(SpawnMenu).GetField("_selectedObjectIndex", flags)!;
            var rendererField = typeof(SpawnMenu).GetField("_previewRenderer", flags)!;
            var rootField = typeof(SpawnMenu).GetField("_previewCharacter", flags)!;
            var visible = typeof(SpawnMenu).GetProperty("HasPreviewGeometry", flags)!;
            string native = Path.GetFullPath(Path.Combine("data", "minecraft", "versions", "26.3",
                "characters", "vanilla", "steve", "steve.mimodel"));
            string standard = Path.GetFullPath(Path.Combine("assets", "mesh", "Camera.glb"));
            using var fixture = new Fixture();
            string miObject = fixture.File("preview.miobject", MiPathDocument);
            var injected = new List<List<CharacterEntry>>();
            try
            {
                foreach (string name in new[] { "Characters", "Equipment", "Props" })
                {
                    var catalog = (List<CharacterEntry>)typeof(CharacterRegistry).GetField(
                        "_" + name.ToLowerInvariant(), BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                    Check(catalog.Count == 0, "Test registry starts empty");
                    catalog.Add(new CharacterEntry { Name = "preview fixture", FilePath = native });
                    injected.Add(catalog);
                }
                spawn.RefreshCharactersCategory();
                ModelPartGraphicsTests(spawn, viewport, native,
                    () => update.Invoke(spawn, [0d]),
                    () => ReadPixels((PreviewRenderer)rendererField.GetValue(spawn)!));
                foreach (var (name, customPath) in new[]
                {
                    ("Characters", ""), ("Equipment", ""), ("Props", ""),
                    ("Custom Models", standard), ("Custom Models", native), ("Custom Models", miObject),
                    ("Custom Models", native + ".nxObj")
                })
                {
                    category.SetValue(spawn, name);
                    if (name == "Custom Models")
                    {
                        if (customPath.EndsWith(".nxObj", StringComparison.Ordinal))
                        {
                            var source = spawn.SpawnCustomModelFromPath(native, registerAsset: false)!;
                            viewport.SceneObjects.Remove(source);
                            var snapshots = (Dictionary<string, ProjectSceneObjectEntry>)typeof(SpawnMenu)
                                .GetField("_customArchivePreviews", flags)!.GetValue(spawn)!;
                            snapshots[customPath] = ProjectSceneSerializer.SerializeObjectForLibrary(source);
                            foreach (var mesh in source.GetMeshInstancesRecursively()) mesh.Dispose();
                        }
                        typeof(SpawnMenu).GetMethod("AddToCustomModelHistory", flags)!
                            .Invoke(spawn, [customPath, "preview fixture"]);
                    }
                    index.SetValue(spawn, name == "Custom Models" ? 1 : 0);
                    sceneLighting.Upload(mat4.Identity, new vec3(0.1f), vec3.UnitY, vec3.Zero,
                        vec3.UnitY, vec3.Zero, vec3.UnitY, vec3.Zero, 0, false, false, false);
                    int binding = gl.GetInteger(GLEnum.UniformBufferBinding, 0);
                    update.Invoke(spawn, [0d]);
                    var renderer = (PreviewRenderer)rendererField.GetValue(spawn)!;
                    renderer.AutoRotateSpeed = 0f;
                    Check((bool)visible.GetValue(spawn)!, $"{name} UI must display hierarchy previews");
                    Check(rootField.GetValue(spawn) is SceneObject, $"{name} loads a hierarchy");
                    byte[] first = ReadPixels(renderer);
                    var root = (SceneObject)rootField.GetValue(spawn)!;
                    var originalPosition = root.Position;
                    root.Position += new vec3(200f, -300f, 500f);
                    update.Invoke(spawn, [0d]);
                    byte[] shifted = ReadPixels(renderer);
                    int shiftedPixels = 0;
                    for (int i = 0; i < shifted.Length; i += 4)
                        if (Math.Abs(shifted[i] - 31) > 2 || Math.Abs(shifted[i + 1] - 31) > 2 ||
                            Math.Abs(shifted[i + 2] - 36) > 2) shiftedPixels++;
                    Check(shiftedPixels > 100, "Translated hierarchy must remain framed");
                    root.Position = originalPosition;
                    int changed = 0;
                    for (int i = 0; i < first.Length; i += 4)
                        if (Math.Abs(first[i] - 31) > 2 || Math.Abs(first[i + 1] - 31) > 2 ||
                            Math.Abs(first[i + 2] - 36) > 2) changed++;
                    Check(changed > 100, $"{name} must draw visible model pixels ({changed})");
                    Mesh.FogEnabled = Mesh.HeightFogEnabled = Mesh.IndirectWorldEnabled = Mesh.SssEnabled = true;
                    Mesh.GlobalCameraPosition = new vec3(1000f);
                    Mesh.PointLights.Add((vec3.Zero, new vec3(1f, 0f, 0f), 100f, 100f,
                        -1, vec3.UnitY, 100f, -1f, -1f));
                    sceneLighting.Upload(mat4.Identity, new vec3(10f, 0f, 0f), vec3.UnitZ, new vec3(20f),
                        vec3.UnitY, new vec3(10f), vec3.UnitY, new vec3(10f), 0, true, true, true);
                    update.Invoke(spawn, [0d]);
                    Check(first.SequenceEqual(ReadPixels(renderer)), $"{name} pixels must ignore main-scene lighting and fog");
                    Check(ReferenceEquals(sceneLighting, Mesh.SceneUBO), "Scene lighting restored");
                    Equal(binding, gl.GetInteger(GLEnum.UniformBufferBinding, 0), "Scene UBO binding restored");
                    Equal(new vec3(1000f), Mesh.GlobalCameraPosition, "Scene camera restored");
                    Check(Mesh.FogEnabled && Mesh.HeightFogEnabled && Mesh.IndirectWorldEnabled && Mesh.SssEnabled,
                        "Scene lighting flags preserved");
                    Check(viewport.SceneObjects.Count == 0, "Preview must not insert objects into the scene");
                    renderer.Render([], "world-browser", 0, sceneRoot: root, centerMeshes: true);
                    Check(!first.SequenceEqual(ReadPixels(renderer)), "Default previews still use scene lighting");
                    Equal(GLEnum.NoError, gl.GetError(), "OpenGL error");
                    Mesh.PointLights.Clear();
                    Console.WriteLine($"PASS {name} {Path.GetExtension(customPath)}: {changed} visible pixels; lighting-independent output");
                }
            }
            finally
            {
                typeof(SpawnMenu).GetMethod("DisposePreviewCharacter", flags)!.Invoke(spawn, []);
                (rendererField.GetValue(spawn) as PreviewRenderer)?.Dispose();
                foreach (var catalog in injected) catalog.Clear();
            }

            byte[] ReadPixels(PreviewRenderer renderer)
            {
                byte[] pixels = new byte[renderer.Width * renderer.Height * 4];
                gl.BindTexture(GLEnum.Texture2D, renderer.ColorTexture);
                fixed (byte* pointer = pixels)
                    gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
                gl.BindTexture(GLEnum.Texture2D, 0);
                return pixels;
            }
        }
        finally
        {
            Mesh.SceneUBO = previousLighting;
            Mesh.GlobalCameraPosition = previousCamera;
            Mesh.FogEnabled = previousFog;
            Mesh.HeightFogEnabled = previousHeightFog;
            Mesh.IndirectWorldEnabled = previousIndirect;
            Mesh.SssEnabled = previousSss;
            Mesh.PointLights.Clear();
            Mesh.PointLights.AddRange(previousPoints);
            if (window != null) glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
