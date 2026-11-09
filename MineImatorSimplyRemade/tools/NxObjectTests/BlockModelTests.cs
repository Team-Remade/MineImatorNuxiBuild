using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private const string CustomBlockParent = """
        {
          "textures":{"side":"sample:block/old","indirect":"#side","top":"sample:block/top"},
          "elements":[{"from":[0,0,0],"to":[16,16,16],
            "faces":{
              "north":{"texture":"#indirect","uv":[0,0,16,16],"rotation":90},
              "south":{"texture":"#side"},
              "up":{"texture":"#top"}
            }}]
        }
        """;

    private static (string Model, string Side, string Top) CustomBlockFiles(Fixture f)
    {
        string side = f.File("pack\\assets\\sample\\textures\\block\\side.png", "side pixels");
        string top = f.File("pack\\assets\\sample\\textures\\block\\top.png", "top pixels");
        f.File("pack\\assets\\sample\\textures\\block\\old.png", "old pixels");
        f.File("pack\\assets\\sample\\textures\\block\\side.png.mcmeta", """{"animation":{"frametime":3}}""");
        f.File("pack\\assets\\sample\\models\\block\\parent.json", CustomBlockParent);
        string model = f.File("pack\\assets\\sample\\models\\block\\child.json",
            """{"parent":"sample:block/parent","textures":{"side":"sample:block/side"}}""");
        return (model, side, top);
    }

    private static void BlockModelResolution(Fixture f)
    {
        var files = CustomBlockFiles(f);
        var warnings = new List<string>();
        var document = BlockModelLoader.ReadDocument(files.Model, warnings.Add);
        Equal(0, warnings.Count, "All namespaced textures found");
        Check(document["parent"] == null, "Parent flattened");
        Equal(1, document["elements"]!.AsArray().Count, "Inherited elements");
        Equal(files.Side, document["textures"]!["side"]!.GetValue<string>(), "Child texture override");
        Equal("#side", document["textures"]!["indirect"]!.GetValue<string>(), "Inherited alias kept until child merge");
        Equal(files.Top, document["textures"]!["top"]!.GetValue<string>(), "Second per-face texture");
        Equal(90, document["elements"]![0]!["faces"]!["north"]!["rotation"]!.GetValue<int>(), "UV rotation");
        Equal(ProjectAssetType.Model, ProjectManager.DetectImportAssetType("CUSTOM.JSON"), "Import classification");
        string other = f.File("pack\\assets\\other\\textures\\block\\side.png", "other namespace pixels");
        string namespaced = f.File("pack\\assets\\sample\\models\\block\\namespaced.json",
            """{"parent":"sample:block/parent","textures":{"side":"other:block/side"}}""");
        Equal(other, BlockModelLoader.ReadDocument(namespaced)["textures"]!["side"]!.GetValue<string>(),
            "Explicit namespace wins over same-named texture in current namespace");
        string dotted = f.File("pack\\assets\\sample\\textures\\block\\side.variant.png", "dotted name pixels");
        string dottedModel = f.File("pack\\assets\\sample\\models\\block\\dotted.json",
            """{"parent":"sample:block/parent","textures":{"side":"sample:block/side.variant"}}""");
        Equal(dotted, BlockModelLoader.ReadDocument(dottedModel)["textures"]!["side"]!.GetValue<string>(),
            "Dots in resource names are not file extensions");

        string sibling = f.File("loose\\side.png", "loose pixels");
        string loose = f.File("loose\\model.json", CustomBlockParent.Replace("sample:block/old", "side")
            .Replace("sample:block/top", "side"));
        var resolved = BlockModelLoader.ReadDocument(loose, warnings.Add);
        Equal(sibling, resolved["textures"]!["side"]!.GetValue<string>(), "Loose sibling texture");
        f.File("pack\\assets\\sample\\models\\block\\empty.json",
            """{"parent":"sample:block/parent","elements":[]}""");
        Reject(() => BlockModelLoader.ReadDocument(Path.Combine(Path.GetDirectoryName(files.Model)!, "empty.json")),
            "Explicit empty elements incorrectly inherit parent geometry");

        string previousVersion = BlockRegistry.VersionRoot;
        var versionProperty = typeof(BlockRegistry).GetProperty(nameof(BlockRegistry.VersionRoot))!;
        try
        {
            string version = Path.Combine(ParticleDataRoot(), "minecraft", "versions", "26.3");
            versionProperty.SetValue(null, version);
            Check(File.Exists(Path.Combine(version, "models", "block", "cube_all.json")), "Bundled parent fixture");
            string vanilla = f.File("vanilla.json",
                """{"parent":"minecraft:block/cube_all","textures":{"all":"minecraft:block/stone"}}""");
            var vanillaDocument = BlockModelLoader.ReadDocument(vanilla, warnings.Add);
            Equal(6, vanillaDocument["elements"]![0]!["faces"]!.AsObject().Count, "Bundled cube parent");
            Check(File.Exists(vanillaDocument["textures"]!["all"]!.GetValue<string>()), "Bundled stone texture");
        }
        finally { versionProperty.SetValue(null, previousVersion); }
    }

    private static void BlockModelPersistence(Fixture f)
    {
        var files = CustomBlockFiles(f);
        string stored = "";
        WithTextureSheetProject(f, manager =>
        {
            var asset = manager.AddAsset(files.Model, ProjectAssetType.Model);
            stored = manager.GetAssetFullPath(asset);
            var copied = BlockModelLoader.ReadDocument(stored);
            Check(copied["parent"] == null, "Project copy flattened");
            string side = copied["textures"]!["side"]!.GetValue<string>();
            Equal("side pixels", File.ReadAllText(side), "Project texture bytes");
            Check(side.StartsWith(manager.ModelsFolder + Path.DirectorySeparatorChar), "Texture is project-contained");
            Check(File.Exists(side + ".mcmeta"), "Project animation metadata");
            manager.EnsureModelAssetIntegrity(asset);
            Equal(side, BlockModelLoader.ReadDocument(stored)["textures"]!["side"]!.GetValue<string>(),
                "Integrity copy uses stable dependency names");
        });
        // Remove only known fixture sources to prove the copied model is independent.
        foreach (string file in new[] { files.Model, files.Side, files.Top,
                     Path.Combine(Path.GetDirectoryName(files.Model)!, "parent.json") })
            File.Delete(file);
        var root = new SceneObject { Name = "Custom block", SourceAssetPath = stored };
        var (manifest, directory) = f.Roundtrip(root);
        string extractedModel = Resource(directory, manifest.Root.SourceAssetPath);
        var extracted = BlockModelLoader.ReadDocument(extractedModel);
        Equal(1, extracted["elements"]!.AsArray().Count, "Copied model works without original parent");
        foreach (string slot in new[] { "side", "top" })
        {
            string texture = extracted["textures"]![slot]!.GetValue<string>();
            Check(texture.StartsWith(directory + Path.DirectorySeparatorChar), "Archive texture contained");
            Equal(slot + " pixels", File.ReadAllText(texture), "Archived texture bytes");
        }
        Check(File.Exists(extracted["textures"]!["side"]!.GetValue<string>() + ".mcmeta"),
            "Archived animation metadata");
    }

    private static void BlockModelValidation(Fixture f)
    {
        foreach (string json in new[]
                 {
                     "{}", "[]", """{"parent":"absent:block/parent"}""",
                     """{"parent":"builtin/generated"}""",
                     """{"elements":[{"from":[0,0],"to":[16,16,16],"faces":{"up":{"texture":"#a"}}}]}""",
                     """{"elements":[{"from":[0,0,0],"to":[16,16,16],"faces":{"invalid":{"texture":"#a"}}}]}""",
                     """{"elements":[{"from":[0,0,0],"to":[16,16,16],"faces":{"up":{"texture":"#a","rotation":45}}}]}"""
                 })
            Reject(() => BlockModelLoader.ReadDocument(f.File("invalid.json", json)), "Invalid block JSON accepted");
        string cycle = f.File("cycle.json", """{"parent":"cycle"}""");
        Reject(() => BlockModelLoader.ReadDocument(cycle), "Parent cycle accepted");
        string missing = f.File("missing.json", CustomBlockParent.Replace("sample:block/old", "missing:block/nope")
            .Replace("sample:block/top", "missing:block/nope"));
        var warnings = new List<string>();
        var document = BlockModelLoader.ReadDocument(missing, warnings.Add);
        Equal(1, document["elements"]!.AsArray().Count, "Missing textures preserve geometry");
        Check(warnings.Count > 0, "Missing texture warning");
        f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.json" } },
            [("resources/model.json", CustomBlockParent.Replace("sample:block/old", "../../../outside.png"))],
            "Block JSON dependencies escaping archive accepted");
        f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.json" } },
            [("resources/model.json", """{"parent":"minecraft:block/cube_all"}""")],
            "Unflattened archive parent accepted");
    }

    private static unsafe void BlockModelGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Block model test", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            MineImatorLoader.Instance.Initialize(gl);
            using var f = new Fixture();
            var files = CustomBlockFiles(f);
            File.Copy(SheetImage(f), files.Side, true);
            File.Copy(SheetImage(f), files.Top, true);
            File.Delete(files.Side + ".mcmeta");
            var viewport = new Viewport();
            var spawn = new SpawnMenu { Gl = gl, Viewport = viewport };
            var root = spawn.SpawnCustomModelFromPath(files.Model, registerAsset: false) ??
                throw new InvalidOperationException("Custom importer JSON dispatch failed");
            try
            {
                Equal(2, root.Visuals.Count, "Two texture draw groups");
                Equal(18, root.Visuals.Sum(m => m.Vertices.Count), "Three faces, six vertices each");
                var side = root.Visuals.Single(m => MineImatorLoader.Instance.GetTexturePath(m.TextureId) == files.Side);
                Equal(12, side.Vertices.Count, "Child side override resolves inherited alias");
                Check(side.Vertices.All(v => v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1 && v.z >= 0 && v.z <= 1),
                    "Minecraft coordinates scaled to block units");
                Near(0, side.TexCoords[0].x, "90-degree north UV u");
                Near(1, side.TexCoords[0].y, "90-degree north UV v");
                gl.BindTexture(GLEnum.Texture2D, side.TextureId);
                byte[] pixels = new byte[6 * 4 * 4];
                fixed (byte* pointer = pixels)
                    gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
                Equal((byte)0, pixels[1], "Top-first PNG row");
                Equal((byte)150, pixels[(3 * 6) * 4 + 1], "Bottom PNG row");
                Equal((byte)120, pixels[3], "Texture alpha retained");
                Check(root.Visuals.All(m => m.MinecraftTextureKey == ""), "Custom textures not rebound by resource packs");
                var rotatedDocument = BlockModelLoader.ReadDocument(files.Model);
                rotatedDocument["elements"]![0]!["rotation"] = JsonNode.Parse(
                    """{"origin":[8,8,8],"axis":"y","angle":45,"rescale":false}""");
                var aliases = rotatedDocument["textures"]!.AsObject();
                aliases["side"] = "#alias0";
                for (int i = 0; i < 20; i++) aliases["alias" + i] = i == 19 ? files.Side : "#alias" + (i + 1);
                string rotatedPath = f.File("rotated.json", rotatedDocument.ToJsonString());
                var rotated = BlockModelLoader.Load(gl, rotatedPath);
                try
                {
                    Equal(18, rotated.Visuals.Sum(m => m.Vertices.Count), "Rotated model retains every face");
                    var rotatedSide = rotated.Visuals.Single(m =>
                        MineImatorLoader.Instance.GetTexturePath(m.TextureId) == files.Side);
                    Equal(12, rotatedSide.Vertices.Count, "Long texture alias chain resolves");
                    Near(MathF.Sqrt(0.5f), MathF.Abs(rotatedSide.Normals[0].x), "Element rotation rotates normal x");
                    Near(MathF.Sqrt(0.5f), MathF.Abs(rotatedSide.Normals[0].z), "Element rotation rotates normal z");
                    Check(rotatedSide.Vertices.Any(v => v.x < 0 || v.x > 1), "Element rotates about authored origin");
                }
                finally { foreach (var mesh in rotated.Visuals) mesh.Dispose(); }
                rotatedDocument["textures"]!["side"] = "#absent_slot";
                string missingPath = f.File("missing-slot.json", rotatedDocument.ToJsonString());
                var warnings = new List<string>();
                var missing = BlockModelLoader.Load(gl, missingPath, warning: warnings.Add);
                try
                {
                    Equal(18, missing.Visuals.Sum(m => m.Vertices.Count), "Missing slot does not drop faces");
                    Equal(12, missing.Visuals.Single(m => m.TextureId == 0).Vertices.Count, "Untextured faces retained");
                    Check(warnings.Count > 0, "Missing slot surfaced to caller");
                }
                finally { foreach (var mesh in missing.Visuals) mesh.Dispose(); }
                string archive = f.NewPath(".nxObj");
                NxObjectArchive.Export(archive, root);
                var imported = NxObjectArchive.Import(archive, viewport, spawn);
                try
                {
                    Equal(2, imported.Visuals.Count, "Archive restores per-face textures");
                    Equal(18, imported.Visuals.Sum(m => m.Vertices.Count), "Archive restores geometry");
                }
                finally { foreach (var mesh in imported.Visuals) mesh.Dispose(); }
                var overridden = spawn.SpawnCustomModelFromPath(files.Model, files.Top, registerAsset: false)!;
                try
                {
                    Check(overridden.Visuals.All(m => MineImatorLoader.Instance.GetTexturePath(m.TextureId) == files.Top),
                        "Texture override applies to every face");
                }
                finally { foreach (var mesh in overridden.Visuals) mesh.Dispose(); }
            }
            finally { foreach (var mesh in root.Visuals) mesh.Dispose(); }
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
