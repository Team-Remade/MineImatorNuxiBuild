using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static class Program
{
    private static readonly MethodInfo ExtractMethod = typeof(NxObjectArchive).GetMethod("Extract",
        BindingFlags.Static | BindingFlags.NonPublic) ?? throw new MissingMethodException("NxObjectArchive.Extract");

    private static int Main()
    {
        SelectionManager.Initialize();
        (string Name, Action<Fixture> Run)[] tests =
        [
            ("Subtree, transforms, keyframes and material preservation", Subtree),
            ("Same-basename resource collision and deduplication", BasenameCollision),
            ("JSON model and external mimodel texture closure", JsonClosure),
            ("JSON object-form texture dependency closure", JsonObjectReferences),
            ("JSON object-form texture dependency traversal rejection", JsonObjectReferenceTraversal),
            ("glTF buffer/image URI remapping and embedded data", GltfClosure),
            ("GLB external image closure preserves binary chunks", GlbClosure),
            ("OBJ/MTL dependency closure with texture options", ObjClosure),
            ("Missing direct resource preserves atomic destination", MissingResource),
            ("Missing transitive resource preserves atomic destination", MissingDependency),
            ("Opaque model export rejects atomically with conversion guidance", UnsupportedExport),
            ("Opaque models in crafted archives rejected", UnsupportedArchive),
            ("Safe STL geometry remains supported", StlGeometry),
            ("Archive format/version rejection", Versions),
            ("ZIP traversal and Windows path rejection", ZipTraversal),
            ("Manifest resource traversal rejection", ManifestTraversal),
            ("Duplicate ZIP entries including case aliases rejected", DuplicateEntries),
            ("ZIP symbolic link entries rejected", SymbolicLinkEntry),
            ("Missing manifest/resource rejection", MissingArchiveEntries),
            ("Model dependency traversal and remote URI rejection", DependencyTraversal),
            ("Child SourceNodePath persistence and standalone selection", SourceNodePaths),
            ("Stable source selection survives sibling deletion and reparenting", StableSourceNodePath),
            ("Exact source hierarchy restoration preserves deletions and node identity", ExactSourceHierarchy),
            ("Particle library transitive/cyclic closure", ParticleClosure),
            ("Overlapping particle library roots deduplicated in both discovery orders", ParticleOverlappingRoots),
            ("Missing particle library entry preserves destination", MissingParticleLibrary),
            ("Repeated extraction has independent resources and DTOs", IndependentExtraction),
            ("GPU-free repeated import remaps IDs and internal references", IndependentImport),
            ("Animated material texture resource closure", AnimatedTexture),
            ("Missing animated texture preserves atomic destination", MissingAnimatedTexture),
            ("Animated texture manifest path validation", AnimatedTextureTraversal),
            ("Animated font dependency closure and built-in sentinel", AnimatedFont)
        ];
        int failures = 0;
        foreach (var test in tests)
        {
            using var fixture = new Fixture();
            var library = ProjectManager.Instance.Manifest.ObjectLibrary;
            var original = library.ToArray();
            library.Clear();
            try
            {
                test.Run(fixture);
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL {test.Name}: {exception.GetType().Name}: {exception.Message}");
                Console.WriteLine(exception.StackTrace);
            }
            finally
            {
                library.Clear();
                library.AddRange(original);
            }
        }
        Console.WriteLine($"RESULT {tests.Length - failures}/{tests.Length} passed; {failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void ExactSourceHierarchy(Fixture f)
    {
        var parent = Node("source root");
        parent.RestoreExactChildren = true;
        var first = Node("same name");
        first.SourceNodePath = [0];
        var second = Node("same name");
        second.SourceNodePath = [1];
        var helper = Node("runtime helper");
        helper.IsRuntimeTransient = true;
        parent.AddChild(first);
        parent.AddChild(second);
        parent.AddChild(helper);
        var match = typeof(ProjectSceneSerializer).GetMethod("FindMatchingChild", BindingFlags.Static | BindingFlags.NonPublic)!;
        var selected = new ProjectSceneObjectEntry { Name = "same name", SourceNodePath = [1], RestoreExactChildren = true };
        Check(ReferenceEquals(second, match.Invoke(null, [parent, selected, new HashSet<SceneObject>()])),
            "Matching must use source identity instead of ambiguous names or types.");
        selected.SourceNodePath = null;
        Check(match.Invoke(null, [parent, selected, new HashSet<SceneObject>()]) == null,
            "An authored node must not reuse an unrelated source-generated node.");

        var restore = typeof(ProjectSceneSerializer).GetMethod("RestoreChildren", BindingFlags.Static | BindingFlags.NonPublic)!;
        var restoreState = Activator.CreateInstance(restore.GetParameters()[5].ParameterType, nonPublic: true)!;
        var viewport = new Viewport();
        restore.Invoke(null, [new ProjectSceneObjectEntry { RestoreExactChildren = true }, parent, viewport,
            new SpawnMenu { Viewport = viewport }, vec3.Zero, restoreState]);
        Equal(1, parent.Children.Count, "Source children absent from the saved hierarchy must stay deleted");
        Check(ReferenceEquals(helper, parent.Children[0]), "Runtime helpers must be retained.");
        Check(ProjectSceneSerializer.SerializeObjectForLibrary(parent).RestoreExactChildren,
            "Exact hierarchy restoration must survive project serialization.");
    }

    private static SceneObject Node(string name) => new()
    {
        Name = name, ObjectId = Guid.NewGuid().ToString("N"), ObjectType = "Folder"
    };

    private static NxObjectManifest Extract(string archive, string directory)
    {
        try
        {
            return (NxObjectManifest)ExtractMethod.Invoke(null, [archive, directory])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected '{expected}', got '{actual}'.");

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException or JsonException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static string Resource(string directory, string relative)
    {
        Check(relative.StartsWith("resources/", StringComparison.Ordinal), $"Resource was not remapped: {relative}");
        string full = Path.GetFullPath(Path.Combine(directory, relative));
        Check(full.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            $"Resource escapes extraction: {relative}");
        Check(File.Exists(full), $"Resource missing: {relative}");
        return full;
    }

    private static string Dependency(string owner, string relative, string root, bool uri = false)
    {
        if (uri) relative = Uri.UnescapeDataString(relative);
        Check(!Path.IsPathRooted(relative) && !relative.Contains(':'), $"Dependency not relative: {relative}");
        string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(owner)!, relative));
        Check(full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            $"Dependency escaped extraction: {relative}");
        Check(File.Exists(full), $"Dependency missing: {relative}");
        return full;
    }

    private static void Subtree(Fixture f)
    {
        var outside = Node("not exported");
        var root = Node("selected root");
        var child = Node("primitive child");
        child.SpawnCategory = "Primitives";
        var grandchild = Node("nested folder");
        outside.AddChild(root);
        outside.AddChild(Node("sibling not exported"));
        root.AddChild(child);
        child.AddChild(grandchild);
        root.Position = new vec3(1, 2, 3);
        child.Rotation = new vec3(10, 20, 30);
        child.Scale = new vec3(2, 3, 4);
        root.LibrarySourceId = "external-library-link";
        root.CameraTextureObjectId = child.ObjectId;
        grandchild.CameraTextureObjectId = outside.ObjectId;
        child.Keyframes["position.x"] =
        [
            new() { Frame = 20, Value = 8.5f, InterpolationType = "ease_in" },
            new() { Frame = 2, Value = -2f, InterpolationType = "linear" }
        ];
        child.Keyframes["visible"] = [new() { Frame = 5, Value = false, InterpolationType = "constant" }];
        child.SetExplicitMaterialSettings();
        child.MaterialSettings.Metallic = 0.75f;
        child.MaterialSettings.Roughness = 0.25f;
        child.MaterialSettings.PbrEnabled = true;
        child.MaterialSettings.EmissionEnabled = true;
        child.MaterialSettings.EmissionEnergy = 3.5f;
        child.MaterialSettings.AlphaMode = MaterialAlphaMode.Clip;
        child.MaterialSettings.DoubleSided = true;
        child.AlbedoTexturePath = f.File("textures/albedo.png", "albedo");
        child.NormalTexturePath = f.File("textures/normal.png", "normal");
        child.EmissionTexturePath = f.File("textures/emission.png", "emission");
        child.SurfaceTexturePath = f.File("textures/surface.png", "surface");
        child.OcclusionTexturePath = f.File("textures/occlusion.png", "occlusion");
        child.RoughnessTexturePath = f.File("textures/roughness.png", "roughness");
        child.MetallicTexturePath = f.File("textures/metallic.png", "metallic");
        child.HeightTexturePath = f.File("textures/height.png", "height");
        var (manifest, directory) = f.Roundtrip(root);
        Equal("selected root", manifest.Root.Name, "Selected root");
        Equal(1, manifest.Root.Children.Count, "Root child count");
        Equal("nested folder", manifest.Root.Children[0].Children[0].Name, "Grandchild");
        Equal(2f, manifest.Root.Position.Y, "Root position");
        Equal(child.ObjectId, manifest.Root.CameraTextureObjectId, "Internal reference");
        Equal("", manifest.Root.Children[0].Children[0].CameraTextureObjectId, "External reference cleared");
        Equal("", manifest.Root.LibrarySourceId, "External library association cleared");
        var saved = manifest.Root.Children[0];
        Equal(20f, saved.Rotation.Y, "Child rotation");
        Equal(4f, saved.Scale.Z, "Child scale");
        Check(saved.HasMaterialOverrides && saved.PbrEnabled && saved.EmissionEnabled && saved.DoubleSided, "Material flags lost");
        Equal(0.75f, saved.Metallic, "Metallic");
        Equal(0.25f, saved.Roughness, "Roughness");
        Equal(3.5f, saved.EmissionEnergy, "Emission energy");
        Equal(MaterialAlphaMode.Clip, saved.AlphaMode, "Alpha mode");
        Equal(2, saved.Keyframes["position.x"][0].Frame, "Keyframes sorted");
        Equal(8.5f, ((JsonElement)saved.Keyframes["position.x"][1].Value).GetSingle(), "Numeric keyframe");
        Equal("ease_in", saved.Keyframes["position.x"][1].InterpolationType, "Interpolation");
        Check(!((JsonElement)saved.Keyframes["visible"][0].Value).GetBoolean(), "Boolean keyframe lost");
        foreach (string path in new[] { saved.AlbedoTexturePath, saved.NormalTexturePath, saved.EmissionTexturePath,
            saved.SurfaceTexturePath, saved.OcclusionTexturePath!, saved.RoughnessTexturePath!, saved.MetallicTexturePath!, saved.HeightTexturePath! })
            Resource(directory, path);
        Equal("external-library-link", root.LibrarySourceId, "Export mutated source library link");
        Equal(outside.ObjectId, grandchild.CameraTextureObjectId, "Export mutated source object reference");
    }

    private static void BasenameCollision(Fixture f)
    {
        var root = Node("collision");
        root.AlbedoTexturePath = f.File("a/texture.png", "first texture");
        root.NormalTexturePath = f.File("b/texture.png", "second texture");
        root.EmissionTexturePath = root.AlbedoTexturePath;
        f.File("a/texture.png.mcmeta", "{\"animation\":{}}");
        var (manifest, directory) = f.Roundtrip(root);
        Check(manifest.Root.AlbedoTexturePath != manifest.Root.NormalTexturePath, "Same basename collided");
        Equal(manifest.Root.AlbedoTexturePath, manifest.Root.EmissionTexturePath, "Same source was not deduplicated");
        Equal("first texture", File.ReadAllText(Resource(directory, manifest.Root.AlbedoTexturePath)), "First texture bytes");
        Equal("second texture", File.ReadAllText(Resource(directory, manifest.Root.NormalTexturePath)), "Second texture bytes");
        Check(File.Exists(Resource(directory, manifest.Root.AlbedoTexturePath) + ".mcmeta"), "Animation sidecar missing");
    }

    private static void JsonClosure(Fixture f)
    {
        string texture = f.File("external textures/skin.png", "external skin");
        string model = f.File("models/part.mimodel", JsonSerializer.Serialize(new
        {
            textures = new { skin = texture }, parts = new[] { new { texture = "default" } }
        }));
        string source = f.File("objects/root.miobject", JsonSerializer.Serialize(new
        {
            resources = new[] { new { id = "part-id", filename = model } },
            model = "part-id", texture = texture
        }));
        var root = Node("json");
        root.SourceAssetPath = source;
        var (manifest, directory) = f.Roundtrip(root);
        string extracted = Resource(directory, manifest.Root.SourceAssetPath);
        var json = JsonNode.Parse(File.ReadAllText(extracted))!;
        Equal("part-id", json["model"]!.GetValue<string>(), "Resource ID must not be rewritten");
        string nested = Dependency(extracted, json["resources"]![0]!["filename"]!.GetValue<string>(), directory);
        var nestedJson = JsonNode.Parse(File.ReadAllText(nested))!;
        string extractedTexture = Dependency(nested, nestedJson["textures"]!["skin"]!.GetValue<string>(), directory);
        Equal("external skin", File.ReadAllText(extractedTexture), "External mimodel texture");
        Equal(extractedTexture, Dependency(extracted, json["texture"]!.GetValue<string>(), directory), "Shared dependency target");
        Equal("default", nestedJson["parts"]![0]!["texture"]!.GetValue<string>(), "Default texture sentinel");
        Equal(source, root.SourceAssetPath, "Source path mutated");
        Check(File.ReadAllText(model).Contains(texture.Replace("\\", "\\\\"), StringComparison.Ordinal), "Original model was rewritten");
    }

    private static void GltfClosure(Fixture f)
    {
        f.File("buffers/mesh data.bin", "buffer bytes");
        f.File("images/skin #1.png", "image bytes");
        var root = Node("gltf");
        root.SourceAssetPath = f.File("models/model.gltf", """
            {"asset":{"version":"2.0"},"buffers":[{"uri":"../buffers/mesh%20data.bin","byteLength":12}],
            "images":[{"uri":"../images/skin%20%231.png"},{"uri":"data:image/png;base64,AA=="}]}
            """);
        var (manifest, directory) = f.Roundtrip(root);
        string model = Resource(directory, manifest.Root.SourceAssetPath);
        var json = JsonNode.Parse(File.ReadAllText(model))!;
        Equal("buffer bytes", File.ReadAllText(Dependency(model, json["buffers"]![0]!["uri"]!.GetValue<string>(), directory, true)), "glTF buffer");
        Equal("image bytes", File.ReadAllText(Dependency(model, json["images"]![0]!["uri"]!.GetValue<string>(), directory, true)), "glTF image");
        Equal("data:image/png;base64,AA==", json["images"]![1]!["uri"]!.GetValue<string>(), "Embedded URI");
    }

    private static void JsonObjectReferences(Fixture f)
    {
        string texture = f.File("object-form/skin.png", "object-form texture");
        foreach (string property in new[] { "model_tex", "model_tex_normal", "model_tex_material" })
        {
            var document = new JsonObject { [property] = new JsonObject { ["path"] = texture } };
            var root = Node(property);
            root.SourceAssetPath = f.File(property + ".miobject", document.ToJsonString());
            var (manifest, directory) = f.Roundtrip(root);
            string model = Resource(directory, manifest.Root.SourceAssetPath);
            var json = JsonNode.Parse(File.ReadAllText(model))!;
            string reference = json[property]!["path"]!.GetValue<string>();
            Equal("object-form texture", File.ReadAllText(Dependency(model, reference, directory)), property + " object reference");
        }
    }

    private static void JsonObjectReferenceTraversal(Fixture f)
    {
        foreach (string property in new[] { "model_tex", "model_tex_normal", "model_tex_material" })
        {
            foreach (string key in new[] { "path", "file", "id", "resource" })
            {
                var hostile = new JsonObject { [property] = new JsonObject { [key] = "../../outside.png" } };
                f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.miobject" } },
                    [("resources/model.miobject", hostile.ToJsonString())], $"Accepted {property}.{key} traversal");
            }
        }
    }

    private static void ObjClosure(Fixture f)
    {
        f.File("images/obj skin.png", "obj skin");
        f.File("materials/surface.mtl", "newmtl test\nmap_Kd -s 1 1 1 \"../images/obj skin.png\"\n");
        var root = Node("obj");
        root.SourceAssetPath = f.File("models/model.obj", "mtllib ../materials/surface.mtl\nv 0 0 0\n");
        var (manifest, directory) = f.Roundtrip(root);
        string model = Resource(directory, manifest.Root.SourceAssetPath);
        string materialReference = File.ReadAllLines(model)[0]["mtllib ".Length..].Trim('"');
        string material = Dependency(model, materialReference, directory);
        string textureReference = File.ReadAllLines(material)[1]["map_Kd -s 1 1 1 ".Length..].Trim('"');
        Equal("obj skin", File.ReadAllText(Dependency(material, textureReference, directory)), "OBJ material texture");
    }

    private static void GlbClosure(Fixture f)
    {
        f.File("glb/image.png", "GLB external image");
        string source = f.NewPath(".glb");
        byte[] json = Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"images\":[{\"uri\":\"glb/image.png\"}]}");
        int paddedLength = (json.Length + 3) & ~3;
        byte[] binary = [1, 2, 3, 4, 5, 6, 7, 8];
        using (var writer = new BinaryWriter(File.Create(source)))
        {
            writer.Write(0x46546C67u);
            writer.Write(2u);
            writer.Write((uint)(20 + paddedLength + 8 + binary.Length));
            writer.Write((uint)paddedLength);
            writer.Write(0x4E4F534Au);
            writer.Write(json);
            for (int i = json.Length; i < paddedLength; i++) writer.Write((byte)' ');
            writer.Write((uint)binary.Length);
            writer.Write(0x004E4942u);
            writer.Write(binary);
        }
        var root = Node("glb");
        root.SourceAssetPath = source;
        var (manifest, directory) = f.Roundtrip(root);
        string model = Resource(directory, manifest.Root.SourceAssetPath);
        using var reader = new BinaryReader(File.OpenRead(model));
        Equal(0x46546C67u, reader.ReadUInt32(), "GLB magic");
        Equal(2u, reader.ReadUInt32(), "GLB version");
        Equal((uint)reader.BaseStream.Length, reader.ReadUInt32(), "Rewritten GLB size");
        int length = (int)reader.ReadUInt32();
        Equal(0, length % 4, "GLB JSON padding");
        Equal(0x4E4F534Au, reader.ReadUInt32(), "GLB JSON chunk type");
        var rewritten = JsonNode.Parse(reader.ReadBytes(length))!;
        string image = Dependency(model, rewritten["images"]![0]!["uri"]!.GetValue<string>(), directory, true);
        Equal("GLB external image", File.ReadAllText(image), "GLB image bytes");
        Equal((uint)binary.Length, reader.ReadUInt32(), "GLB binary chunk length");
        Equal(0x004E4942u, reader.ReadUInt32(), "GLB binary chunk type");
        Check(reader.ReadBytes(binary.Length).SequenceEqual(binary), "GLB binary chunk changed");
    }

    private static void AtomicFailure(Fixture f, SceneObject root)
    {
        string destination = f.File("existing.nxObj", "existing destination must survive");
        Reject(() => NxObjectArchive.Export(destination, root), "Export accepted missing resource");
        Equal("existing destination must survive", File.ReadAllText(destination), "Atomic destination overwritten");
        Equal(0, Directory.GetFiles(f.Root, "*.tmp").Length, "Temporary archive leaked");
    }

    private static void MissingResource(Fixture f)
    {
        var root = Node("missing");
        root.NormalTexturePath = Path.Combine(f.Root, "absent.png");
        AtomicFailure(f, root);
    }

    private static void MissingDependency(Fixture f)
    {
        var root = Node("missing dependency");
        root.SourceAssetPath = f.File("broken.mimodel", "{\"texture\":\"absent.png\"}");
        AtomicFailure(f, root);
    }

    private static void Unsupported(Action action, string extension)
    {
        try { action(); }
        catch (NotSupportedException exception)
        {
            Check(exception.Message.Contains("convert", StringComparison.OrdinalIgnoreCase) &&
                exception.Message.Contains("GLB", StringComparison.OrdinalIgnoreCase), "Unsupported format error omitted conversion guidance");
            return;
        }
        throw new InvalidOperationException($"Accepted unsupported opaque model {extension}");
    }

    private static void UnsupportedExport(Fixture f)
    {
        foreach (string extension in new[] { ".fbx", ".dae", ".3ds", ".ply", ".x3d" })
        {
            var root = Node("unsupported " + extension);
            string opaqueSource = f.File("source" + extension, "opaque model with unchecked external dependencies");
            root.SourceAssetPath = opaqueSource;
            string destination = f.File("existing" + extension + ".nxObj", "keep existing destination");
            Unsupported(() => NxObjectArchive.Export(destination, root), extension);
            Equal("keep existing destination", File.ReadAllText(destination), "Unsupported export replaced destination");
            Equal(0, Directory.GetFiles(f.Root, "*.tmp").Length, "Unsupported export leaked temporary archive");
            root.SourceAssetPath = f.File("wrapper" + extension + ".miobject", JsonSerializer.Serialize(new { model = opaqueSource }));
            Unsupported(() => NxObjectArchive.Export(destination, root), extension);
            Equal("keep existing destination", File.ReadAllText(destination), "Unsupported nested model export replaced destination");
            Equal(0, Directory.GetFiles(f.Root, "*.tmp").Length, "Unsupported nested model export leaked temporary archive");
        }
    }

    private static void UnsupportedArchive(Fixture f)
    {
        foreach (string extension in new[] { ".fbx", ".dae", ".3ds", ".ply", ".x3d" })
        {
            string resource = "resources/source" + extension;
            string archive = f.Zip(new() { Root = new() { SourceAssetPath = resource } },
                [(resource, "opaque model with unchecked external dependencies")]);
            string directory = f.NewPath();
            Unsupported(() => Extract(archive, directory), extension);
            Check(!Directory.Exists(directory), "Rejected opaque model archive left extracted files");
            string nestedArchive = f.Zip(new() { Root = new() { SourceAssetPath = "resources/wrapper.miobject" } },
                [("resources/wrapper.miobject", JsonSerializer.Serialize(new { model = "source" + extension })),
                (resource, "opaque nested model")]);
            string nestedDirectory = f.NewPath();
            Unsupported(() => Extract(nestedArchive, nestedDirectory), extension);
            Check(!Directory.Exists(nestedDirectory), "Rejected nested opaque model archive left extracted files");
        }
    }

    private static void StlGeometry(Fixture f)
    {
        const string geometry = "solid triangle\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid triangle\n";
        var root = Node("safe STL");
        root.SourceAssetPath = f.File("geometry.stl", geometry);
        var (manifest, directory) = f.Roundtrip(root);
        Equal(geometry, File.ReadAllText(Resource(directory, manifest.Root.SourceAssetPath)), "STL geometry bytes changed");
        string archive = f.Zip(new() { Root = new() { SourceAssetPath = "resources/triangle.stl" } },
            [("resources/triangle.stl", geometry)]);
        string craftedDirectory = f.NewPath();
        var crafted = Extract(archive, craftedDirectory);
        Equal(geometry, File.ReadAllText(Resource(craftedDirectory, crafted.Root.SourceAssetPath)), "Crafted safe STL rejected or changed");
    }

    private static void Versions(Fixture f)
    {
        foreach (int version in new[] { -1, 0, 2, int.MaxValue })
            f.RejectArchive(new NxObjectManifest { Version = version }, [], $"Accepted version {version}");
        f.RejectArchive(new NxObjectManifest { Format = "nxProj" }, [], "Accepted wrong format");
        foreach (string json in new[] { "{}", "{\"format\":\"nxObj\",\"root\":{}}", "{\"version\":1,\"root\":{}}",
            "{\"format\":\"nxObj\",\"version\":1}", "{\"format\":\"nxObj\",\"version\":1,\"root\":null}" })
        {
            string archive = f.Zip(null, [("manifest.json", json)]);
            Reject(() => Extract(archive, f.NewPath()), $"Accepted missing required manifest fields: {json}");
        }
    }

    private static void ZipTraversal(Fixture f)
    {
        foreach (string path in new[] { "../escaped.txt", "resources/../../escaped.txt", "/absolute.txt", "C:/absolute.txt",
            "resources\\evil.txt", "resources/./evil.txt", "resources//evil.txt", "resources/file.txt:stream", "resources/trailing./file" })
            f.RejectArchive(new(), [(path, "hostile")], $"Accepted ZIP path {path}");
        Check(!File.Exists(Path.Combine(f.Root, "escaped.txt")), "Traversal wrote outside extraction directory");
    }

    private static void ManifestTraversal(Fixture f)
    {
        foreach (string path in new[] { "../escaped.png", "resources/../../escaped.png", "C:/outside.png", "/outside.png",
            "resources\\safe.png", "manifest.json", "resources/absent.png" })
            f.RejectArchive(new() { Root = new() { AlbedoTexturePath = path } }, [("resources/safe.png", "safe")],
                $"Accepted manifest resource path {path}");
    }

    private static void DuplicateEntries(Fixture f)
    {
        f.RejectArchive(new(), [("resources/a.png", "first"), ("resources/a.png", "second")], "Accepted duplicate ZIP path");
        f.RejectArchive(new(), [("resources/a.png", "first"), ("resources/A.png", "second")], "Accepted case-alias ZIP path");
        f.RejectArchive(new(), [("manifest.json", "{}")], "Accepted duplicate manifest");
    }

    private static void SymbolicLinkEntry(Fixture f)
    {
        string archive = f.Zip(new(), [("resources/link.png", "../../outside.png")]);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
            zip.GetEntry("resources/link.png")!.ExternalAttributes = unchecked((int)0xA1FF0000);
        string directory = f.NewPath();
        Reject(() => Extract(archive, directory), "Accepted ZIP symbolic link");
        Check(!Directory.Exists(directory), "Rejected symlink archive left extracted files");
    }

    private static void MissingArchiveEntries(Fixture f)
    {
        string archive = f.Zip(null, [("resources/a.png", "present")]);
        Reject(() => Extract(archive, f.NewPath()), "Accepted archive without manifest");
        f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/absent.mimodel" } }, [], "Accepted missing resource");
        f.RejectArchive(new() { ResourcePacks = new() { ["pack"] = "resources/absent.zip" } }, [], "Accepted missing resource pack");
        f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.mimodel" } },
            [("resources/model.mimodel", "{\"texture\":\"absent.png\"}")], "Accepted missing transitive model texture");
    }

    private static void DependencyTraversal(Fixture f)
    {
        foreach (string path in new[] { "../../outside.png", "C:/outside.png", "https://example.invalid/skin.png" })
            f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.mimodel" } },
                [("resources/model.mimodel", JsonSerializer.Serialize(new { texture = path }))], $"Accepted JSON dependency {path}");
        foreach (string uri in new[] { "../../outside.bin", "%2E%2E/%2E%2E/outside.bin", "file:///C:/outside.bin", "https://example.invalid/buffer.bin" })
            f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.gltf" } },
                [("resources/model.gltf", JsonSerializer.Serialize(new { buffers = new[] { new { uri } } }))], $"Accepted glTF dependency {uri}");
        f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.obj" } },
            [("resources/model.obj", "mtllib ../../outside.mtl")], "Accepted OBJ dependency traversal");
        f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.obj" } },
            [("resources/model.obj", "mtllib material.mtl"), ("resources/material.mtl", "map_Kd ../../outside.png")],
            "Accepted MTL dependency traversal");
    }

    private static void SourceNodePaths(Fixture f)
    {
        var root = Node("whole source");
        root.SourceAssetPath = f.File("source.mimodel", "{}");
        root.SpawnCategory = "Custom Models";
        root.SourceNodePath = [4];
        root.AddChild(Node("first"));
        var child = Node("selected part");
        child.SpawnCategory = "Custom Models";
        child.SourceAssetPath = root.SourceAssetPath;
        child.SourceNodePath = [4, 1];
        root.AddChild(child);
        var (whole, _) = f.Roundtrip(root);
        Check(whole.Root.Children[1].SourceNodePath!.SequenceEqual([4, 1]), "Child SourceNodePath not persisted");
        var (part, directory) = f.Roundtrip(child);
        Check(part.Root.SourceNodePath!.SequenceEqual([4, 1]), "Standalone part original source path not preserved");
        Resource(directory, part.Root.SourceAssetPath);
        Equal("selected part", part.Root.Name, "Standalone part selected wrong root");
        Equal(0, part.Root.Children.Count, "Standalone part included siblings");
        Check(root.SourceNodePath.SequenceEqual([4]) && child.SourceNodePath.SequenceEqual([4, 1]), "Export mutated source node paths");
    }

    private static void StableSourceNodePath(Fixture f)
    {
        string source = f.File("stable.mimodel", "{}");
        var originalParent = Node("original imported hierarchy");
        originalParent.SourceAssetPath = source;
        originalParent.SpawnCategory = "Custom Models";
        var child = Node("original second source child");
        child.SpawnCategory = "Custom Models";
        child.SourceAssetPath = source;
        child.SourceNodePath = [1];
        originalParent.AddChild(child);
        Equal(0, originalParent.Children.ToList().IndexOf(child), "Fixture must differ from original source index");
        var (selected, directory) = f.Roundtrip(child);
        Check(selected.Root.SourceNodePath!.SequenceEqual([1]), "Export replaced original source index 1 with live child index 0");
        Resource(directory, selected.Root.SourceAssetPath);
        var newParent = Node("new source-owning parent");
        newParent.SourceAssetPath = source;
        newParent.SpawnCategory = "Custom Models";
        originalParent.RemoveChild(child);
        newParent.AddChild(child);
        var (reparented, reparentedDirectory) = f.Roundtrip(child);
        Check(reparented.Root.SourceNodePath!.SequenceEqual([1]), "Reparenting changed original source selection");
        Resource(reparentedDirectory, reparented.Root.SourceAssetPath);
        Check(child.SourceNodePath.SequenceEqual([1]), "Export mutated stable source annotation");
    }

    private static void ParticleClosure(Fixture f)
    {
        string texture = f.File("particle/texture.png", "particle texture");
        var first = new ProjectSceneObjectEntry
        {
            LibraryEntryId = "first", Name = "first", AlbedoTexturePath = texture,
            Children = [new() { ParticleLibraryEntryId = "second" }]
        };
        var second = new ProjectSceneObjectEntry
        {
            LibraryEntryId = "second", Name = "second", ParticleLibraryEntryId = "first", NormalTexturePath = texture
        };
        var library = ProjectManager.Instance.Manifest.ObjectLibrary;
        library.AddRange([first, second, new() { LibraryEntryId = "unused", Name = "not included" }]);
        var root = new ParticleSpawnerSceneObject { Name = "emitter", ObjectId = "emitter", ParticleLibraryEntryId = "first", Amount = 19 };
        var (manifest, directory) = f.Roundtrip(root);
        Equal(2, manifest.ObjectLibrary.Count, "Transitive cycle was not deduplicated");
        Check(manifest.ObjectLibrary.Select(entry => entry.LibraryEntryId).ToHashSet().SetEquals(["first", "second"]), "Wrong library closure");
        Equal(19, manifest.Root.ParticleAmount, "Particle settings");
        var savedFirst = manifest.ObjectLibrary.Single(entry => entry.LibraryEntryId == "first");
        var savedSecond = manifest.ObjectLibrary.Single(entry => entry.LibraryEntryId == "second");
        Equal("second", savedFirst.Children[0].ParticleLibraryEntryId, "Nested library dependency");
        Equal(savedFirst.AlbedoTexturePath, savedSecond.NormalTexturePath, "Library resource deduplication");
        Equal("particle texture", File.ReadAllText(Resource(directory, savedFirst.AlbedoTexturePath)), "Library texture missing");
        Equal(texture, first.AlbedoTexturePath, "Export mutated live library");
        Equal(3, library.Count, "Export mutated live library count");
    }

    private static void MissingParticleLibrary(Fixture f)
    {
        AtomicFailure(f, new ParticleSpawnerSceneObject { Name = "broken emitter", ParticleLibraryEntryId = "absent" });
    }

    private static void ParticleOverlappingRoots(Fixture f)
    {
        string texture = f.File("overlap/descendant.png", "descendant resource");
        var descendant = new ProjectSceneObjectEntry
        {
            ObjectId = "object-B", LibraryEntryId = "B", Name = "descendant B", AlbedoTexturePath = texture
        };
        var ancestor = new ProjectSceneObjectEntry
        {
            ObjectId = "object-A", LibraryEntryId = "A", Name = "ancestor A", Children = [descendant]
        };
        ProjectManager.Instance.Manifest.ObjectLibrary.Add(ancestor);
        foreach (string[] order in new[] { new[] { "A", "B" }, new[] { "B", "A" } })
        {
            var root = Node("overlap " + string.Join(" then ", order));
            foreach (string id in order)
                root.AddChild(new ParticleSpawnerSceneObject
                {
                    Name = "emitter " + id, ObjectId = Guid.NewGuid().ToString("N"), ParticleLibraryEntryId = id
                });
            var (manifest, directory) = f.Roundtrip(root);
            Equal(1, manifest.ObjectLibrary.Count, "Ancestor and descendant were exported as overlapping library roots");
            Equal("A", manifest.ObjectLibrary[0].LibraryEntryId, "Wrong nonoverlapping library root selected");
            var entries = manifest.ObjectLibrary.SelectMany(entry => new[] { entry }.Concat(entry.Children)).ToList();
            Equal(1, entries.Count(entry => entry.LibraryEntryId == "B"), "Descendant library template B exported more than once");
            Equal(1, entries.Count(entry => entry.ObjectId == "object-B"), "Descendant object B exported more than once");
            Equal("descendant resource", File.ReadAllText(Resource(directory,
                entries.Single(entry => entry.LibraryEntryId == "B").AlbedoTexturePath)), "Descendant resource missing");
            Check(manifest.Root.Children.Select(entry => entry.ParticleLibraryEntryId).SequenceEqual(order), "Emitter references changed");
        }
        Equal(texture, descendant.AlbedoTexturePath, "Export mutated original descendant library template");
        Equal(1, ProjectManager.Instance.Manifest.ObjectLibrary.Count, "Export changed live library roots");
    }

    private static void IndependentExtraction(Fixture f)
    {
        var root = Node("independent");
        root.AlbedoTexturePath = f.File("independent.png", "original");
        string archive = f.NewPath(".nxObj");
        NxObjectArchive.Export(archive, root);
        string firstDirectory = f.NewPath();
        string secondDirectory = f.NewPath();
        var first = Extract(archive, firstDirectory);
        var second = Extract(archive, secondDirectory);
        string firstTexture = Resource(firstDirectory, first.Root.AlbedoTexturePath);
        string secondTexture = Resource(secondDirectory, second.Root.AlbedoTexturePath);
        Check(firstTexture != secondTexture, "Extractions shared a directory");
        File.WriteAllText(firstTexture, "changed");
        first.Root.Name = "changed";
        Equal("original", File.ReadAllText(secondTexture), "Second extraction resource was shared");
        Equal("independent", second.Root.Name, "Second extraction DTO was shared");
        Directory.Delete(firstDirectory, true);
        Check(File.Exists(secondTexture), "Removing first extraction damaged second");
    }

    private static void AnimatedTexture(Fixture f)
    {
        var root = Node("animated texture");
        string texture = f.File("keyframes/animated.png", "animated texture bytes");
        root.Keyframes["material.texture"] = [new() { Frame = 12, Value = texture }];
        var (manifest, directory) = f.Roundtrip(root);
        object value = manifest.Root.Keyframes["material.texture"][0].Value;
        string saved = value is JsonElement element ? element.GetString()! : (string)value;
        Equal("animated texture bytes", File.ReadAllText(Resource(directory, saved)), "Animated texture bytes");
        Equal(texture, (string)root.Keyframes["material.texture"][0].Value, "Source texture keyframe mutated");
    }

    private static void IndependentImport(Fixture f)
    {
        var manager = ProjectManager.Instance;
        var folderProperty = typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFolder))!;
        var fileProperty = typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFilePath))!;
        string originalFolder = manager.ProjectFolder;
        string originalFile = manager.ProjectFilePath;
        bool originalDirty = manager.IsDirty;
        try
        {
            folderProperty.SetValue(manager, f.Root);
            fileProperty.SetValue(manager, Path.Combine(f.Root, "isolated.nxProj"));
            Check(manager.HasProject, "Isolated project was not active");
            var root = Node("import root");
            var child = Node("import child");
            root.AddChild(child);
            root.CameraTextureObjectId = child.ObjectId;
            child.CameraTextureObjectId = "external-object";
            child.Keyframes["position.x"] = [new() { Frame = 3, Value = 2.5f }];
            string archive = f.NewPath(".nxObj");
            NxObjectArchive.Export(archive, root);
            var viewport = new Viewport();
            var spawnMenu = new SpawnMenu { Viewport = viewport };
            var first = NxObjectArchive.Import(archive, viewport, spawnMenu);
            var second = NxObjectArchive.Import(archive, viewport, spawnMenu);
            Equal(2, viewport.SceneObjects.Count, "Import root count");
            Equal(1, first.Children.Count, "First import child count");
            Equal(1, second.Children.Count, "Second import child count");
            Check(new[] { root.ObjectId, child.ObjectId, first.ObjectId, first.Children[0].ObjectId,
                second.ObjectId, second.Children[0].ObjectId }.Distinct().Count() == 6, "Imports reused object IDs");
            Equal(first.Children[0].ObjectId, first.CameraTextureObjectId, "First internal reference remap");
            Equal(second.Children[0].ObjectId, second.CameraTextureObjectId, "Second internal reference remap");
            Equal("", first.Children[0].CameraTextureObjectId, "First external reference cleared");
            Equal("", second.Children[0].CameraTextureObjectId, "Second external reference cleared");
            Equal(3, second.Children[0].Keyframes["position.x"][0].Frame, "Imported keyframe");
            Check(ReferenceEquals(first, first.Children[0].Parent), "Imported parent link missing");
            first.Children[0].Name = "changed";
            Equal("import child", second.Children[0].Name, "Repeated import shared objects");
            Equal(2, Directory.GetDirectories(Path.Combine(manager.ModelsFolder, "nxobjects")).Length,
                "Import extraction directories were not independent");
        }
        finally
        {
            folderProperty.SetValue(manager, originalFolder);
            fileProperty.SetValue(manager, originalFile);
            manager.SetDirty(originalDirty);
        }
    }

    private static void MissingAnimatedTexture(Fixture f)
    {
        var root = Node("missing animated texture");
        root.Keyframes["material.texture"] = [new() { Frame = 1, Value = Path.Combine(f.Root, "absent.png") }];
        AtomicFailure(f, root);
    }

    private static void AnimatedTextureTraversal(Fixture f)
    {
        foreach (string path in new[] { "../../outside.png", "C:/outside.png", "resources/absent.png" })
            f.RejectArchive(new()
            {
                Root = new()
                {
                    Keyframes = new() { ["material.texture"] = [new() { Frame = 1, Value = path }] }
                }
            }, [], $"Accepted animated texture manifest path {path}");
    }

    private static void AnimatedFont(Fixture f)
    {
        var root = Node("animated font");
        root.Keyframes["text.font"] =
        [
            new() { Frame = 0, Value = "minecraftia" },
            new() { Frame = 12, Value = f.File("fonts/custom.ttf", "mock font bytes") }
        ];
        var (manifest, directory) = f.Roundtrip(root);
        string Text(object value) => value is JsonElement element ? element.GetString()! : (string)value;
        Equal("minecraftia", Text(manifest.Root.Keyframes["text.font"][0].Value), "Built-in font sentinel");
        Equal("mock font bytes", File.ReadAllText(Resource(directory, Text(manifest.Root.Keyframes["text.font"][1].Value))), "Font dependency");
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "kilo", "NxObjectTests", Guid.NewGuid().ToString("N"));

        public Fixture() => Directory.CreateDirectory(Root);

        public string NewPath(string suffix = "") => Path.Combine(Root, Guid.NewGuid().ToString("N") + suffix);

        public string File(string relative, string content)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        public (NxObjectManifest Manifest, string Directory) Roundtrip(SceneObject root)
        {
            string archive = NewPath(".nxObj");
            string directory = NewPath();
            NxObjectArchive.Export(archive, root);
            return (Extract(archive, directory), directory);
        }

        public string Zip(NxObjectManifest? manifest, (string Path, string Content)[] entries)
        {
            string path = NewPath(".nxObj");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            if (manifest != null)
            {
                using var output = archive.CreateEntry("manifest.json").Open();
                JsonSerializer.Serialize(output, manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(entry.Path).Open());
                writer.Write(entry.Content);
            }
            return path;
        }

        public void RejectArchive(NxObjectManifest manifest, (string Path, string Content)[] entries, string message)
        {
            string archive = Zip(manifest, entries);
            string directory = NewPath();
            Reject(() => Extract(archive, directory), message);
            Check(!System.IO.Directory.Exists(directory), "Rejected archive left extracted files behind");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
