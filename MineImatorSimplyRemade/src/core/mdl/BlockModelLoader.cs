using System.Text.Json;
using System.Text.Json.Nodes;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl;

public static class BlockModelLoader
{
    public static SceneObject Load(GL gl, string filePath, string? textureOverridePath = null,
        Action<string>? warning = null)
    {
        warning ??= Console.Error.WriteLine;
        JsonObject document = ReadDocument(filePath, warning);
        var model = BlockRegistry.ParseModelJson(document);
        var textures = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        uint LoadTexture(string reference)
        {
            string path = textureOverridePath ?? reference;
            if (textures.TryGetValue(path, out uint cached)) return cached;
            uint id = File.Exists(path) ? MineImatorLoader.Instance.LoadTextureFromFile(path) : 0;
            if (id == 0) warning($"Could not load block model texture: {path}");
            textures[path] = id;
            return id;
        }

        var root = new SceneObject { Name = Path.GetFileNameWithoutExtension(filePath) };
        foreach (var mesh in MinecraftModelMesh.Build(gl, model, textureLoader: LoadTexture))
            root.AddMesh(mesh);
        return root;
    }

    public static JsonObject ReadDocument(string filePath, Action<string>? warning = null)
    {
        warning ??= Console.Error.WriteLine;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonObject document = Read(Path.GetFullPath(filePath), 0);
        ValidateGeometry(document, warning);
        return document;

        JsonObject Read(string path, int depth)
        {
            if (depth >= 64 || !visited.Add(path))
                throw new InvalidDataException($"Circular or excessively deep block model parent chain: {path}");
            var raw = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = AppJsonContext.MaxDepth
            }) as JsonObject ?? throw new InvalidDataException($"Block model must be a JSON object: {path}");
            JsonObject merged = new();
            if (raw["parent"] is JsonNode parent)
            {
                string reference = parent.GetValue<string>();
                if (reference.Contains("builtin/", StringComparison.Ordinal))
                    throw new InvalidDataException($"Built-in generated/entity models are not cuboid block models: {reference}");
                string parentPath = FindResource(path, reference, "models", ".json") ??
                    throw new FileNotFoundException($"Could not resolve block model parent '{reference}' from '{path}'.");
                merged = Read(parentPath, depth + 1);
            }
            RewriteTextures(raw, reference =>
            {
                string? texturePath = FindResource(path, reference, "textures", ".png");
                if (texturePath != null) return texturePath;
                warning($"Could not resolve block model texture '{reference}' from '{path}'.");
                return reference;
            });
            foreach (var (key, value) in raw)
            {
                if (key == "parent") continue;
                if (key == "textures" && value is JsonObject textures)
                {
                    if (merged["textures"] is not JsonObject) merged["textures"] = new JsonObject();
                    foreach (var (slot, texture) in textures)
                    {
                        merged["textures"]![slot] = texture?.DeepClone();
                    }
                }
                else merged[key] = value?.DeepClone();
            }
            return merged;
        }
    }

    public static void RewriteTextures(JsonObject document, Func<string, string> rewrite)
    {
        void Rewrite(JsonObject owner, string key)
        {
            if (owner[key] is not JsonValue value || !value.TryGetValue<string>(out string? reference) ||
                string.IsNullOrWhiteSpace(reference))
                throw new InvalidDataException("Block model texture references must be nonempty strings.");
            if (!reference.StartsWith('#')) owner[key] = rewrite(reference);
        }
        if (document["textures"] is JsonObject textures)
            foreach (string key in textures.Select(p => p.Key).ToArray()) Rewrite(textures, key);
        if (document["elements"] is JsonArray elements)
            foreach (JsonNode? element in elements)
                if (element?["faces"] is JsonObject faces)
                    foreach (var (_, face) in faces)
                        if (face is JsonObject obj) Rewrite(obj, "texture");
    }

    public static void CopyIntoFolder(string sourcePath, string folder)
    {
        JsonObject document = ReadDocument(sourcePath);
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        RewriteTextures(document, reference =>
        {
            if (!File.Exists(reference)) return reference;
            if (targets.TryGetValue(reference, out string? existing)) return existing;
            string relative = Path.Combine("textures", $"{targets.Count}_{Path.GetFileName(reference)}");
            string target = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(reference, target, true);
            if (File.Exists(reference + ".mcmeta")) File.Copy(reference + ".mcmeta", target + ".mcmeta", true);
            targets.Add(reference, relative);
            return relative.Replace('\\', '/');
        });
        File.WriteAllText(Path.Combine(folder, Path.GetFileName(sourcePath)),
            document.ToJsonString(AppJsonContext.Default.Options));
    }

    private static string? FindResource(string owner, string reference, string kind, string extension)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(owner))!;
        string local = reference.Replace('/', '\\');
        if (Path.IsPathFullyQualified(local)) return File.Exists(local) ? local : null;
        int colon = reference.IndexOf(':');
        string ns = colon < 0 ? "minecraft" : reference[..colon];
        string resource = (colon < 0 ? reference : reference[(colon + 1)..]).Replace('/', '\\');
        bool hasExtension = kind == "models"
            ? resource.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            : Path.GetExtension(resource).ToLowerInvariant() is
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".gif" or ".webp" or ".tiff" or ".hdr" or ".exr";
        if (!hasExtension) resource += extension;
        DirectoryInfo? assetsRoot = new(directory);
        while (assetsRoot != null && !assetsRoot.Name.Equals("assets", StringComparison.OrdinalIgnoreCase))
            assetsRoot = assetsRoot.Parent;
        if (colon < 0)
        {
            string direct = Path.GetFullPath(Path.Combine(directory, resource));
            if (File.Exists(direct)) return direct;
        }
        for (DirectoryInfo? dir = new(directory); dir != null; dir = dir.Parent)
        {
            string[] candidates =
            [
                Path.Combine(dir.FullName, "assets", ns, kind, resource),
                Path.Combine(dir.FullName, ns, kind, resource)
            ];
            foreach (string candidate in candidates)
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            if (assetsRoot == null ||
                dir.FullName.Equals(Path.Combine(assetsRoot.FullName, ns), StringComparison.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(dir.FullName, kind, resource);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }
        if (ns == "minecraft")
        {
            string bundled = Path.Combine(MinecraftDataLoader.GetVersionRoot(), kind, resource);
            if (File.Exists(bundled)) return Path.GetFullPath(bundled);
        }
        // Loose exports often put namespace-style texture names next to the JSON.
        if (kind == "textures" && assetsRoot == null)
        {
            string sibling = Path.Combine(directory, Path.GetFileName(resource));
            if (File.Exists(sibling)) return sibling;
        }
        return null;
    }

    private static void ValidateGeometry(JsonObject document, Action<string> warning)
    {
        if (document["texture_size"] != null)
        {
            ValidateArray(document["texture_size"], 2);
            if (document["texture_size"]!.AsArray().Any(n => n!.GetValue<float>() <= 0))
                throw new InvalidDataException("Block model texture size must be positive.");
        }
        if (document["elements"] is not JsonArray elements || elements.Count == 0)
            throw new InvalidDataException("Block model has no cuboid elements.");
        foreach (JsonNode? token in elements)
        {
            if (token is not JsonObject element) throw new InvalidDataException("Invalid block model element.");
            ValidateArray(element["from"], 3);
            ValidateArray(element["to"], 3);
            if (element["rotation"] is JsonObject rotation)
            {
                if (rotation["origin"] != null) ValidateArray(rotation["origin"], 3);
                if (rotation["axis"]?.GetValue<string>() is not ("x" or "y" or "z"))
                    throw new InvalidDataException("Block model rotation axis must be x, y or z.");
                if (rotation["angle"] is not JsonNode angle || !float.IsFinite(angle.GetValue<float>()))
                    throw new InvalidDataException("Invalid block model rotation angle.");
            }
            if (element["faces"] is not JsonObject faces || faces.Count == 0)
                throw new InvalidDataException("Block model element has no faces.");
            foreach (var (name, face) in faces)
            {
                if (name is not ("up" or "down" or "north" or "south" or "east" or "west") || face is not JsonObject obj)
                    throw new InvalidDataException($"Invalid block model face: {name}");
                if (obj["uv"] != null) ValidateArray(obj["uv"], 4);
                int uvRotation = obj["rotation"]?.GetValue<int>() ?? 0;
                if (uvRotation is not (0 or 90 or 180 or 270))
                    throw new InvalidDataException("Block model UV rotation must be 0, 90, 180 or 270.");
            }
        }
        // Reuse the registry parser after shape validation rather than maintaining a second geometry parser.
        var model = BlockRegistry.ParseModelJson(document);
        foreach (var element in model.Elements)
            foreach (var face in element.Faces.Values)
                if (BlockRegistry.ResolveTextureReference(model, face.Texture) == null)
                    warning($"Unresolved block model texture slot: {face.Texture}");
    }

    private static void ValidateArray(JsonNode? value, int count)
    {
        if (value is not JsonArray array || array.Count != count ||
            array.Any(n => n == null || !float.IsFinite(n.GetValue<float>())))
            throw new InvalidDataException($"Block model coordinates must contain {count} finite numbers.");
    }
}
