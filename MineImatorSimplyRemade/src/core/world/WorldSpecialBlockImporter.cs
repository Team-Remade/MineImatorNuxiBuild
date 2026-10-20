using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.world;

internal sealed class WorldSpecialBlockImporter(SpawnMenu spawnMenu, string resourcePack,
    List<ProjectAssetEntry> addedAssets) : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"nuxi-world-blocks-{Guid.NewGuid():N}");
    private readonly Dictionary<string, string> _models = new(StringComparer.Ordinal);
    private string? _embeddedDirectory;
    private Dictionary<string, byte[]>? _packTextures;
    private Dictionary<string, byte[]>? _packMetadata;

    public SceneObject Create(WorldSpecialBlock block, string? embeddedModelPath = null)
    {
        SceneObject? root = null;
        try
        {
            if (block.Model == null)
            {
                var variant = SpawnMenu.PickBestVariantForProperties(BlockRegistry.GetVariants(block.Name),
                    block.Properties, null) ?? throw new InvalidDataException($"No block model is installed for {block.Name}.");
                root = spawnMenu.CreateBlockObject(block.Name, variant, resourcePack) ??
                    throw new InvalidDataException($"Could not create {block.Name}.");
                root.PivotOffset -= block.Pivot;
                root.SetLocalPosition(block.Position + block.Pivot);
            }
            else
            {
                string modelPath = embeddedModelPath ?? PortableModel(block);
                var loader = MineImatorLoader.Instance;
                loader.Initialize(spawnMenu.Gl!);
                var model = loader.LoadModel(modelPath) ??
                    throw new InvalidDataException($"Could not load the animatable model for {block.Name}.");
                root = loader.CreateCharacterFromModel(model) ??
                    throw new InvalidDataException($"The animatable model for {block.Name} has no parts.");
                root.SourceAssetPath = modelPath;
                root.SpawnCategory = "Props";
                root.ResourcePackId = resourcePack;
                root.SetLocalPosition(block.Position);
                root.SetLocalRotation(block.Rotation * (MathF.PI / 180));
                ApplyPose(root, block);
            }
            root.Name = block.Name;
            return root;
        }
        catch
        {
            if (root != null) DisposeScene(root);
            throw;
        }
    }

    public byte[] Embed(IEnumerable<WorldSpecialBlock> blocks)
    {
        var placements = new JsonArray();
        foreach (var block in blocks)
        {
            string path = PortableModel(block, addToProject: false);
            var properties = new JsonObject();
            foreach (var (name, value) in block.Properties) properties[name] = value;
            placements.Add((JsonNode)new JsonObject
            {
                ["name"] = block.Name, ["model"] = block.Model,
                ["path"] = Path.GetRelativePath(_temporaryDirectory, path).Replace('\\', '/'),
                ["properties"] = properties,
                ["position"] = new JsonArray(block.Position.x, block.Position.y, block.Position.z),
                ["rotation"] = new JsonArray(block.Rotation.x, block.Rotation.y, block.Rotation.z)
            });
        }
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string model in _models.Values)
                foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(model)!))
                    archive.CreateEntryFromFile(file,
                        Path.GetRelativePath(_temporaryDirectory, file).Replace('\\', '/'));
            using var manifest = new StreamWriter(archive.CreateEntry("blocks.json").Open());
            manifest.Write(placements.ToJsonString());
        }
        return output.ToArray();
    }

    public void AddEmbedded(byte[] data, SceneObject destination)
    {
        // The loader caches textures by path; identical assets must reuse the same cache keys.
        string directory = _embeddedDirectory = Path.Combine(Path.GetTempPath(),
            "nuxi-world-models-" + Convert.ToHexString(SHA256.HashData(data)));
        Directory.CreateDirectory(directory);
        using var input = new MemoryStream(data, writable: false);
        ZipFile.ExtractToDirectory(input, directory, overwriteFiles: true);
        var blocks = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "blocks.json"))) as JsonArray ??
            throw new InvalidDataException("The embedded scenery models have no placements.");
        foreach (var item in blocks)
        {
            string path = Path.GetFullPath(Path.Combine(directory, item!["path"]!.GetValue<string>()));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid embedded scenery model placement.");
            var properties = item["properties"]!.AsObject().ToDictionary(p => p.Key,
                p => p.Value!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
            var block = new WorldSpecialBlock(item["name"]!.GetValue<string>(), properties,
                item["model"]!.GetValue<string>(), null, [],
                ReadVector(item["position"]), ReadVector(item["rotation"]), vec3.Zero);
            var scene = Create(block, path);
            try { Flatten(scene); }
            finally { DisposeScene(scene); }
        }

        static vec3 ReadVector(JsonNode? value) => value is JsonArray { Count: 3 } array
            ? new vec3(array[0]!.GetValue<float>(), array[1]!.GetValue<float>(), array[2]!.GetValue<float>())
            : throw new InvalidDataException("Invalid embedded scenery model transform.");

        void Flatten(SceneObject node)
        {
            // Bake the complete posed hierarchy, including pivots and parent transforms.
            var transform = node.GetWorldMatrix();
            var normalTransform = transform.Inverse.Transposed;
            foreach (var mesh in node.Visuals.ToArray())
            {
                for (int i = 0; i < mesh.Vertices.Count; i++)
                    mesh.Vertices[i] = (transform * new vec4(mesh.Vertices[i], 1)).xyz;
                for (int i = 0; i < mesh.Normals.Count; i++)
                    mesh.Normals[i] = (normalTransform * new vec4(mesh.Normals[i], 0)).xyz.Normalized;
                mesh.BaseTint = mesh.Albedo;
                mesh.Upload();
                destination.AddMesh(mesh);
                node.Visuals.Remove(mesh);
            }
            foreach (var child in node.Children) Flatten(child);
        }
    }

    private string PortableModel(WorldSpecialBlock block, bool addToProject = true)
    {
        string key = $"{block.Model}|{block.Texture}|{(block.Name == "sticky_piston")}|{block.Property("attached")}";
        if (_models.TryGetValue(key, out string? cached)) return cached;
        var entry = CharacterRegistry.Props.Concat(CharacterRegistry.Characters)
            .FirstOrDefault(e => e.Name == block.Model && Path.GetExtension(e.FilePath) == ".mimodel") ??
            throw new InvalidDataException($"The {block.Model} model is not installed. Install the Minecraft model catalog before importing this block.");
        var document = JsonNode.Parse(File.ReadAllText(entry.FilePath)) as JsonObject ??
            throw new InvalidDataException($"Invalid model file for {block.Name}.");
        string sourceDirectory = Path.GetDirectoryName(entry.FilePath)!;
        string? baseTexture = document["texture"]?.GetValue<string>();
        string? replacement = block.Texture == null ? null : ResolveVariantTexture(entry, block.Texture);
        string snapshotDirectory = Path.Combine(_temporaryDirectory, _models.Count.ToString());
        Directory.CreateDirectory(snapshotDirectory);
        var textures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Snapshot(JsonNode node)
        {
            if (node is JsonArray array)
            {
                foreach (var child in array)
                    if (child != null) Snapshot(child);
                return;
            }
            if (node is not JsonObject obj) return;
            if (block.Model == "copper_golem" && obj["shapes"] is JsonArray shapes)
                for (int i = shapes.Count - 1; i >= 0; i--)
                    if (shapes[i]?["description"]?.GetValue<string>() == "eyes") shapes.RemoveAt(i);
            foreach (var (name, value) in obj.ToArray())
            {
                if (value is JsonValue scalar && scalar.TryGetValue<string>(out string? reference) &&
                    reference.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                    name is "texture" or "texture_material" or "texture_normal")
                {
                    obj[name] = CopyTexture(reference);
                }
                else if (value is JsonObject textureMap && name == "textures")
                {
                    foreach (var (slot, path) in textureMap.ToArray())
                        if (path is JsonValue item && item.TryGetValue<string>(out string? file) &&
                            file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                            textureMap[slot] = CopyTexture(file);
                }
                else if (value != null) Snapshot(value);
            }
        }

        string CopyTexture(string reference)
        {
            string source = Path.GetFullPath(Path.Combine(sourceDirectory, reference));
            if (block.Model is "chest" or "chest_double" && replacement != null)
            {
                string suffix = Path.GetFileNameWithoutExtension(source).EndsWith("_left", StringComparison.Ordinal) ? "_left" :
                    Path.GetFileNameWithoutExtension(source).EndsWith("_right", StringComparison.Ordinal) ? "_right" : "";
                source = Path.Combine(Path.GetDirectoryName(replacement)!,
                    Path.GetFileNameWithoutExtension(replacement) + suffix + ".png");
            }
            else if (replacement != null && reference == baseTexture)
                source = replacement;
            if (block.Name == "sticky_piston" && Path.GetFileName(source) == "piston_top.png")
                source = Path.Combine(Path.GetDirectoryName(source)!, "piston_top_sticky.png");
            if (textures.TryGetValue(source, out string? existing)) return existing;
            string localName = $"texture-{textures.Count}.png";
            string destination = Path.Combine(snapshotDirectory, localName);
            byte[]? packed = PackFile(source, metadata: false);
            if (packed == null && !File.Exists(source))
                throw new FileNotFoundException($"A texture required by {block.Name} is missing.", source);
            byte[] bytes = packed ?? File.ReadAllBytes(source);
            ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            File.WriteAllBytes(destination, bytes);
            byte[]? metadata = PackFile(source + ".mcmeta", metadata: true);
            if (metadata != null) File.WriteAllBytes(destination + ".mcmeta", metadata);
            else if (packed == null && File.Exists(source + ".mcmeta"))
                File.Copy(source + ".mcmeta", destination + ".mcmeta");
            textures.Add(source, localName);
            return localName;
        }

        if (block.Model == "mob_head" && !block.Name.StartsWith("player", StringComparison.Ordinal))
        {
            document["player_skin"] = false;
            if (document["parts"]?[0]?["shapes"] is JsonArray shapes && shapes.Count > 1)
                shapes.RemoveAt(1);
        }
        Snapshot(document);
        string snapshot = Path.Combine(snapshotDirectory, block.Model + ".mimodel");
        File.WriteAllText(snapshot, document.ToJsonString());
        if (!addToProject)
        {
            _models.Add(key, snapshot);
            return snapshot;
        }
        var manager = ProjectManager.Instance;
        var asset = manager.AddAsset(snapshot, ProjectAssetType.Model);
        addedAssets.Add(asset);
        string result = manager.GetAssetFullPath(asset);
        _models.Add(key, result);
        return result;
    }

    private static string ResolveVariantTexture(CharacterEntry entry, string relative)
    {
        string direct = Path.Combine(MinecraftDataLoader.GetVersionRoot(), relative);
        if (File.Exists(direct)) return direct;
        string fileName = Path.GetFileName(relative);
        var variant = entry.TextureVariants.FirstOrDefault(v =>
            !v.IsCustom && string.Equals(Path.GetFileName(v.FilePath), fileName, StringComparison.OrdinalIgnoreCase));
        if (variant != null && File.Exists(variant.FilePath)) return variant.FilePath;
        throw new FileNotFoundException($"The texture variant {fileName} for {entry.Name} is not installed.", direct);
    }

    private byte[]? PackFile(string source, bool metadata)
    {
        if (string.IsNullOrEmpty(resourcePack)) return null;
        Dictionary<string, byte[]> ReadFiles(string suffix) =>
            MinecraftDataLoader.EnumerateResourcePackFiles("assets/minecraft/textures/", suffix)
                .Where(f => MinecraftDataLoader.GetResourcePackId(f.PackName) == resourcePack)
                .GroupBy(f => f.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Data, StringComparer.OrdinalIgnoreCase);
        var files = metadata ? _packMetadata ??= ReadFiles(".png.mcmeta") : _packTextures ??= ReadFiles(".png");
        string relative = Path.GetRelativePath(MinecraftDataLoader.GetVersionRoot(), source).Replace('\\', '/');
        return files.GetValueOrDefault("assets/minecraft/" + relative);
    }

    private static void ApplyPose(SceneObject root, WorldSpecialBlock block)
    {
        static IEnumerable<SceneObject> Descendants(SceneObject obj)
        {
            foreach (var child in obj.Children)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
        var parts = Descendants(root).ToArray();
        void Rotate(string name, vec3 degrees)
        {
            var part = parts.FirstOrDefault(p => p.Name == name) ??
                throw new InvalidDataException($"The {block.Model} model is missing its {name} part.");
            part.SetLocalRotation(degrees * (MathF.PI / 180));
        }
        if (block.Name is "piston" or "sticky_piston" && block.Property("extended") == "true")
        {
            var extension = parts.FirstOrDefault(p => p.Name == "extension") ??
                throw new InvalidDataException("The piston model is missing its extension.");
            extension.SetLocalPosition(extension.LocalPosition + vec3.UnitZ);
        }
        if (block.Model == "lever")
            Rotate("handle", new vec3(block.Property("powered") == "true" ? 45 : -45, 0, 0));
        if (block.Model == "fence_gate" && block.Property("open") == "true")
        {
            Rotate("right_door", new vec3(0, -90, 0));
            Rotate("left_door", new vec3(0, 90, 0));
        }
        if (block.Model == "tripwire_hook" && block.Property("powered") == "true")
            Rotate("handle", new vec3(0, 0, 0));
        if (block.Model is "standing_banner" or "wall_banner")
        {
            string color = block.Name.Replace("_wall_banner", "").Replace("_banner", "");
            var cloth = parts.FirstOrDefault(p => p.Name == "banner") ??
                throw new InvalidDataException("The banner model is missing its cloth.");
            cloth.SetExplicitMaterialSettings();
            cloth.MaterialSettings.AlbedoColor = BannerColor(color);
            cloth.ApplyMaterialSettingsToMeshes();
            if (block.Model == "wall_banner")
            {
                float top = parts.SelectMany(part => part.Visuals.SelectMany(mesh =>
                    mesh.Vertices.Select(vertex => (part.GetWorldMatrix() * new vec4(vertex, 1)).y))).Max();
                root.SetLocalPosition(root.LocalPosition + vec3.UnitY * (block.Position.y + 15f / 16 - top));
            }
        }
    }

    private static vec4 BannerColor(string color) => color switch
    {
        "orange" => new(0.85f, 0.5f, 0.2f, 1), "magenta" => new(0.7f, 0.3f, 0.85f, 1),
        "light_blue" => new(0.4f, 0.6f, 0.85f, 1), "yellow" => new(0.9f, 0.9f, 0.2f, 1),
        "lime" => new(0.5f, 0.8f, 0.1f, 1), "pink" => new(0.95f, 0.5f, 0.65f, 1),
        "gray" => new(0.3f, 0.3f, 0.3f, 1), "light_gray" => new(0.6f, 0.6f, 0.6f, 1),
        "cyan" => new(0.3f, 0.5f, 0.6f, 1), "purple" => new(0.5f, 0.25f, 0.7f, 1),
        "blue" => new(0.2f, 0.3f, 0.7f, 1), "brown" => new(0.4f, 0.3f, 0.2f, 1),
        "green" => new(0.4f, 0.5f, 0.2f, 1), "red" => new(0.6f, 0.2f, 0.2f, 1),
        "black" => new(0.1f, 0.1f, 0.1f, 1), _ => vec4.Ones
    };

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
        if (_embeddedDirectory != null && Directory.Exists(_embeddedDirectory))
            Directory.Delete(_embeddedDirectory, recursive: true);
    }

    private static void DisposeScene(SceneObject scene)
    {
        foreach (var child in scene.Children) DisposeScene(child);
        foreach (var mesh in scene.Visuals) mesh.Dispose();
    }
}
