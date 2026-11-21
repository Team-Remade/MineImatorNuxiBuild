using System.Reflection;
using System.Text.Json;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static void EnchantmentGlintPersistence(Fixture fixture)
    {
        string texturePath = Path.GetFullPath(fixture.File("mi-glint/glint.png", "custom glint map"));
        var source = MiDocument("""
            {"resources":[{"id":"glint-resource","type":"texture","filename":"glint.png"}],
             "timelines":[{"id":"glint","type":"folder","name":"Imported Glint",
               "glint_mode":2,"glint_scale":2.25,"glint_speed":-0.5,"glint_strength":1.75,
               "glint_tex":"glint-resource"}]}
            """);
        source.DirectoryPath = Path.GetDirectoryName(texturePath)!;
        var warnings = new List<string>();
        var imported = new MineImatorLoader().CreateSceneFromMiObject(source, warning: warnings.Add).Children.Single();
        Equal(MaterialGlintMode.Armor, imported.MaterialSettings.GlintMode, "MI custom armor glint mode");
        Near(2.25f, imported.MaterialSettings.GlintScale, "MI custom glint scale");
        Near(-0.5f, imported.MaterialSettings.GlintSpeed, "MI custom glint speed");
        Near(1.75f, imported.MaterialSettings.GlintStrength, "MI custom glint strength");
        Equal(Path.GetFullPath(texturePath), imported.MaterialSettings.GlintTexturePath,
            "MI glint texture resource resolves relative to the object");
        Equal(0, warnings.Count, "MI glint resource imports without diagnostics");

        var root = new SceneObject { Name = "Glint persistence" };
        root.SetExplicitMaterialSettings();
        root.MaterialSettings.GlintMode = MaterialGlintMode.Item;
        root.MaterialSettings.GlintScale = 3.5f;
        root.MaterialSettings.GlintSpeed = 1.25f;
        root.MaterialSettings.GlintStrength = 0.65f;
        root.MaterialSettings.GlintTexturePath = texturePath;
        var inheritedChild = new SceneObject { Name = "Inherited glint", UsePropertyInheritance = true, InheritSurface = true };
        inheritedChild.SetExplicitMaterialSettings();
        root.AddChild(inheritedChild);
        var effective = inheritedChild.GetEffectiveMaterialSettings();
        Equal(MaterialGlintMode.Item, effective.GlintMode, "Surface inheritance includes glint mode");
        Near(3.5f, effective.GlintScale, "Surface inheritance includes glint scale");
        Near(0.65f, effective.GlintStrength, "Surface inheritance includes glint strength");
        Equal(texturePath, effective.GlintTexturePath, "Surface inheritance includes glint texture");

        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(root);
        string json = JsonSerializer.Serialize(snapshot, AppJsonContext.Default.ProjectSceneObjectEntry);
        var saved = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectSceneObjectEntry)!;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", statics)!;
        var restored = new SceneObject();
        apply.Invoke(null, [restored, saved, null]);
        AssertGlint(restored.MaterialSettings, MaterialGlintMode.Item, 3.5f, 1.25f, 0.65f, texturePath, "Project JSON reload");

        var (archive, directory) = fixture.Roundtrip(root);
        Equal(MaterialGlintMode.Item, archive.Root.GlintMode, "nxObj mode");
        Near(3.5f, archive.Root.GlintScale, "nxObj scale");
        Near(1.25f, archive.Root.GlintSpeed, "nxObj speed");
        Near(0.65f, archive.Root.GlintStrength, "nxObj strength");
        Equal("custom glint map", File.ReadAllText(Resource(directory, archive.Root.GlintTexturePath)), "nxObj glint texture packaged");

        var library = (ProjectSceneObjectEntry)typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", statics)!
            .Invoke(null, [saved])!;
        Equal(saved.GlintMode, library.GlintMode, "Library clone mode");
        Equal(saved.GlintTexturePath, library.GlintTexturePath, "Library clone texture path");
        library.GlintScale = 10f;
        Near(3.5f, saved.GlintScale, "Library glint settings are independent");

        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var duplicate = (SceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate", instance)!
            .Invoke(new SceneTree(), [root, new Dictionary<string, string>()])!;
        AssertGlint(duplicate.MaterialSettings, MaterialGlintMode.Item, 3.5f, 1.25f, 0.65f, texturePath, "Duplicate");
        duplicate.MaterialSettings.GlintStrength = 2f;
        Near(0.65f, root.MaterialSettings.GlintStrength, "Duplicate material settings are independent");

        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { root }])!;
        ulong originalHash = Hash();
        root.MaterialSettings.GlintStrength = 1.5f;
        Check(Hash() != originalHash, "Dirty fingerprint includes glint strength");
        apply.Invoke(null, [root, saved, null]);
        Equal(originalHash, Hash(), "Restoring the saved glint resets dirty fingerprint");
    }

    private static void AssertGlint(MaterialSettings settings, MaterialGlintMode mode,
        float scale, float speed, float strength, string texturePath, string label)
    {
        Equal(mode, settings.GlintMode, label + " mode");
        Near(scale, settings.GlintScale, label + " scale");
        Near(speed, settings.GlintSpeed, label + " speed");
        Near(strength, settings.GlintStrength, label + " strength");
        Equal(texturePath, settings.GlintTexturePath, label + " texture");
    }

    private static unsafe void EnchantmentGlintGraphics(Fixture fixture)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Enchantment glint regression", null, null);
        Check(window != null, "OpenGL context");
        float previousEffectTime = Mesh.EffectTime;
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            using var ubo = new SceneUniformBuffer(gl);
            Mesh.SceneUBO = ubo;
            ubo.Upload(mat4.Identity, new vec3(0.1f), vec3.UnitY, vec3.Zero, vec3.UnitY, vec3.Zero,
                vec3.UnitY, vec3.Zero, 0, false, false, false);
            using var preview = new PreviewRenderer(gl, 160) { Distance = 6f, AutoRotateSpeed = 0f };
            preview.Render([], "enchantment-glint", 0);
            byte[] background = new byte[160 * 160 * 4];
            gl.BindTexture(GLEnum.Texture2D, preview.ColorTexture);
            fixed (byte* pixels = background)
                gl.GetTexImage(GLEnum.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            const int size = 32;
            byte[] pattern = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int index = (y * size + x) * 4;
                bool stripe = (x + y) % 8 < 3;
                pattern[index] = stripe ? (byte)150 : (byte)0;
                pattern[index + 1] = stripe ? (byte)65 : (byte)0;
                pattern[index + 2] = stripe ? (byte)235 : (byte)0;
                pattern[index + 3] = 255;
            }
            uint texture = gl.GenTexture();
            gl.BindTexture(GLEnum.Texture2D, texture);
            fixed (byte* pixels = pattern)
                gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, size, size, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.Repeat);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.Repeat);

            using var mesh = new CubeMesh(gl) { Unlit = true, GlintTexture = texture };
            Check(mesh.TexCoords.Count == mesh.Vertices.Count, "Glint test mesh has complete UVs");
            Check(mesh.GlintTexture == texture, "Glint test texture is assigned");
            byte[] Read()
            {
                byte[] pixels = new byte[160 * 160 * 4];
                gl.BindTexture(GLEnum.Texture2D, preview.ColorTexture);
                fixed (byte* data = pixels)
                    gl.GetTexImage(GLEnum.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                return pixels;
            }
            byte[] Render(float time)
            {
                Mesh.EffectTime = time;
                preview.Render([mesh], "enchantment-glint", 0, centerMeshes: true);
                return Read();
            }

            mesh.GlintMode = MaterialGlintMode.None;
            byte[] disabled = Render(0f);
            Check(!disabled.SequenceEqual(background), "Preview renders the test mesh");
            mesh.GlintMode = MaterialGlintMode.Item;
            mesh.GlintScale = 1f;
            mesh.GlintSpeed = 1f;
            mesh.GlintStrength = 1f;
            byte[] first = Render(0f);
            Equal(MaterialGlintMode.Item, mesh.GlintMode, "Glint mode remains enabled on the mesh");
            var shader = (MineImatorSimplyRemade.core.mdl.Shader)typeof(Mesh)
                .GetField("_shader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mesh)!;
            int enabledLocation = gl.GetUniformLocation(shader.ShaderProgram, "uUseEnchantmentGlint");
            Check(enabledLocation >= 0, "Glint enable uniform is present in the linked shader");
            gl.GetUniform(shader.ShaderProgram, enabledLocation, out int enabledUniform);
            Equal(1, enabledUniform, "Glint uniform is enabled for the mesh draw");
            Check(!disabled.SequenceEqual(first), "Enabled enchant glint changes rendered pixels");
            Check(first.Where((_, index) => index % 4 == 0).Where((value, index) => value != disabled[index * 4]).Count() > 32,
                "Glint affects a visible number of pixels");
            byte[] animated = Render(1.3f);
            Check(!first.SequenceEqual(animated), "Timeline time animates the glint texture");
            Check(first.SequenceEqual(Render(0f)), "Backward seeks reproduce glint pixels");
            for (int i = 3; i < first.Length; i += 4)
                Equal(disabled[i], first[i], "Glint preserves pixel alpha");
            mesh.GlintStrength = 0f;
            Check(disabled.SequenceEqual(Render(0f)), "Zero strength matches disabled rendering");
            mesh.GlintMode = MaterialGlintMode.None;
            Check(disabled.SequenceEqual(Render(0f)), "Disabled glint is pixel-identical");
            Equal(GLEnum.NoError, gl.GetError(), "OpenGL error");
            gl.DeleteTexture(texture);
            Mesh.SceneUBO = null;
        }
        finally
        {
            Mesh.EffectTime = previousEffectTime;
            Mesh.SceneUBO = null;
            if (window != null) glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}