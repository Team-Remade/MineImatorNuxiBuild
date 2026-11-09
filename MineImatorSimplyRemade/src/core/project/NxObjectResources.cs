using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.mdl;

namespace MineImatorSimplyRemade.core.project;

internal sealed class NxObjectResources : IDisposable
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, string> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public NxObjectResources(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Mode == ZipArchiveMode.Read)
            throw new ArgumentException("The resource archive must be writable.", nameof(archive));
        _archive = archive;
        if (archive.Mode == ZipArchiveMode.Update)
            foreach (var entry in archive.Entries)
                _entries.Add(entry.FullName, "");
    }

    public string AddFile(string fullPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        if (!Path.IsPathFullyQualified(fullPath))
            throw new ArgumentException("Resources require a fully resolved file path.", nameof(fullPath));
        fullPath = Path.GetFullPath(fullPath);
        RequireSupportedModelFormat(fullPath);
        RequireFile(fullPath);
        if (_targets.TryGetValue(fullPath, out string? existing)) return existing;

        string directory = Path.GetDirectoryName(fullPath)!;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())));
        string target = "resources/" + hash + "/" + Path.GetFileName(fullPath);
        _targets.Add(fullPath, target); // Register before recursion so cyclic model references terminate.
        StoreFile(fullPath, target);
        return target;
    }

    private void StoreFile(string source, string target)
    {
        if (_entries.TryGetValue(target, out string? owner))
        {
            if (string.Equals(owner, source, StringComparison.OrdinalIgnoreCase)) return;
            throw new InvalidDataException($"Resource archive entry collision: {target}");
        }
        _entries.Add(target, source);
        string extension = Path.GetExtension(source).ToLowerInvariant();
        string? text = null;
        long glbTail = 0;
        if (extension == ".json")
        {
            JsonObject document = BlockModelLoader.ReadDocument(source);
            BlockModelLoader.RewriteTextures(document, reference =>
                RelativeTarget(target, AddFile(ResolveSource(source, reference, false))));
            text = document.ToJsonString(AppJsonContext.Default.Options);
        }
        else if (extension is ".mimodel" or ".miobject" or ".gltf")
        {
            JsonNode document = ReadModelJson(source, extension);
            RewriteJson(document, extension, reference =>
                RelativeTarget(target, AddFile(ResolveSource(source, reference, extension == ".gltf"))));
            text = document.ToJsonString(AppJsonContext.Default.Options);
        }
        else if (extension == ".glb")
        {
            (JsonNode document, glbTail) = ReadGlbJson(source);
            RewriteJson(document, ".gltf", reference =>
                RelativeTarget(target, AddFile(ResolveSource(source, reference, true))));
            text = document.ToJsonString(AppJsonContext.Default.Options);
        }
        else if (extension is ".obj" or ".mtl")
        {
            text = RewriteObj(File.ReadAllText(source), extension == ".obj", source,
                reference => RelativeTarget(target, AddFile(ResolveSource(source, reference, false))));
        }
        if (IsImage(source) && File.Exists(source + ".mcmeta"))
            StoreFile(source + ".mcmeta", target + ".mcmeta");

        // Dependencies must be written first: ZipArchiveMode.Create permits one open entry at a time.
        using Stream output = _archive.CreateEntry(target, CompressionLevel.Optimal).Open();
        if (extension == ".glb")
        {
            byte[] json = Encoding.UTF8.GetBytes(text!);
            int paddedLength = checked((json.Length + 3) & ~3);
            using Stream input = File.OpenRead(source);
            using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
            writer.Write(0x46546C67u);
            writer.Write(2u);
            writer.Write(checked((uint)(20L + paddedLength + input.Length - glbTail)));
            writer.Write((uint)paddedLength);
            writer.Write(0x4E4F534Au);
            writer.Write(json);
            for (int i = json.Length; i < paddedLength; i++) writer.Write((byte)' ');
            input.Position = glbTail;
            input.CopyTo(output);
        }
        else if (text != null)
        {
            using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
            writer.Write(text);
        }
        else
        {
            using Stream input = File.OpenRead(source);
            input.CopyTo(output);
        }
    }

    // Opaque model formats are rejected rather than passed to an unrestricted native importer.
    public static void ValidateModelReferences(string fullPath, string extractionRoot)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(extractionRoot));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(fullPath));
        while (pending.TryPop(out string? current))
        {
            RequireSupportedModelFormat(current);
            EnsureContainedFile(current, root);
            if (!visited.Add(current)) continue;
            string extension = Path.GetExtension(current).ToLowerInvariant();
            string Validate(string reference)
            {
                string normalized = extension is ".gltf" or ".glb" ? DecodeUri(reference) : reference.Replace('\\', '/');
                if (Path.IsPathRooted(normalized) || normalized.Contains(':') || normalized.Contains('\0'))
                    throw new InvalidDataException($"A model dependency is not archive-relative: {reference}");
                string dependency = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, normalized));
                EnsureContainedFile(dependency, root);
                pending.Push(dependency);
                return reference;
            }

            if (extension == ".json")
            {
                var document = (JsonObject)ReadModelJson(current, extension);
                if (document["parent"] != null)
                    throw new InvalidDataException("Archived block models must have flattened parents.");
                BlockModelLoader.RewriteTextures(document, Validate);
            }
            else if (extension is ".mimodel" or ".miobject" or ".gltf")
                RewriteJson(ReadModelJson(current, extension), extension, Validate);
            else if (extension == ".glb")
                RewriteJson(ReadGlbJson(current).Document, ".gltf", Validate);
            else if (extension is ".obj" or ".mtl")
                RewriteObj(File.ReadAllText(current), extension == ".obj", current, Validate);

            if (IsImage(current) && File.Exists(current + ".mcmeta"))
                pending.Push(current + ".mcmeta");
        }
    }

    private static void EnsureContainedFile(string file, string root)
    {
        string prefix = root + Path.DirectorySeparatorChar;
        if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"A model dependency escapes the extraction directory: {file}");
        RequireFile(file);
        // Lexical containment alone does not protect against junctions or symbolic links.
        for (string? path = file; path != null; path = Path.GetDirectoryName(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"A model dependency uses a reparse point: {path}");
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    private static JsonNode ReadModelJson(string path, string extension)
    {
        string text = File.ReadAllText(path);
        if (extension is ".mimodel" or ".miobject")
            text = MineImatorLoader.EscapeJsonControlCharacters(text);
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            MaxDepth = AppJsonContext.MaxDepth,
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        }) as JsonObject ?? throw new InvalidDataException($"Model JSON must contain an object: {path}");
    }

    private static (JsonNode Document, long Tail) ReadGlbJson(string path)
    {
        using Stream input = File.OpenRead(path);
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        if (input.Length < 20 || reader.ReadUInt32() != 0x46546C67u || reader.ReadUInt32() != 2 ||
            reader.ReadUInt32() != input.Length)
            throw new InvalidDataException($"Invalid GLB header: {path}");
        uint size = reader.ReadUInt32();
        if (size > 64 * 1024 * 1024 || size % 4 != 0 || size > input.Length - 20 || reader.ReadUInt32() != 0x4E4F534Au)
            throw new InvalidDataException($"Invalid GLB JSON chunk: {path}");
        JsonNode document = JsonNode.Parse(reader.ReadBytes((int)size), documentOptions: AppJsonContext.DocumentOptions) as JsonObject ??
            throw new InvalidDataException($"Invalid GLB model JSON: {path}");
        long tail = input.Position;
        while (input.Position < input.Length)
        {
            if (input.Length - input.Position < 8) throw new InvalidDataException($"Truncated GLB chunk: {path}");
            uint chunkSize = reader.ReadUInt32();
            reader.ReadUInt32();
            if (chunkSize % 4 != 0 || chunkSize > input.Length - input.Position)
                throw new InvalidDataException($"Invalid GLB chunk length: {path}");
            input.Position += chunkSize;
        }
        return (document, tail);
    }

    private static void RewriteJson(JsonNode document, string extension, Func<string, string> rewrite)
    {
        if (extension == ".gltf")
        {
            foreach (string collection in new[] { "buffers", "images" })
                if (document[collection] is JsonArray values)
                    foreach (JsonNode? value in values)
                        if (value is JsonObject obj && obj["uri"] is JsonValue uri && uri.TryGetValue<string>(out var reference))
                            if (!reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                                obj["uri"] = Uri.EscapeDataString(rewrite(reference)).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
            return;
        }

        var resourceIds = new HashSet<string>(StringComparer.Ordinal);
        if (document["resources"] is JsonArray resources)
            foreach (JsonNode? resource in resources)
                if (resource is JsonObject obj && obj["id"] is JsonValue id)
                    resourceIds.Add(id.ToString());

        void RewriteReference(JsonObject owner, string key, bool mandatory, bool allowResourceId = true)
        {
            if (owner[key] is not JsonValue scalar || !scalar.TryGetValue<string>(out string? reference) ||
                string.IsNullOrWhiteSpace(reference) || reference is "default" or "null" ||
                (allowResourceId && resourceIds.Contains(reference))) return;
            // Built-in model identifiers and texture slots are not filenames.
            if (mandatory || reference.Contains('/') || reference.Contains('\\') || reference.Contains(':') || Path.HasExtension(reference))
                owner[key] = rewrite(reference);
        }

        void Visit(JsonNode? node, bool textureMap = false)
        {
            if (node is JsonArray array)
            {
                foreach (JsonNode? child in array) Visit(child);
                return;
            }
            if (node is not JsonObject obj) return;
            foreach (string key in obj.Select(p => p.Key).ToArray())
            {
                JsonNode? value = obj[key];
                string name = key.ToLowerInvariant();
                bool objectReference = name is "model" or "model_tex" or "model_tex_material" or "model_tex_normal";
                if (objectReference && value is JsonObject referenceObject)
                {
                    // Match MiTemplate.Model and MiStringOrObjectRefConverter's first scalar key.
                    foreach (string referenceKey in new[] { "id", "resource", "filename", "file", "path", "model" })
                    {
                        if (referenceObject[referenceKey] is not JsonValue candidate) continue;
                        if (candidate.TryGetValue<string>(out _))
                        {
                            RewriteReference(referenceObject, referenceKey, referenceKey == "filename");
                            break;
                        }
                        if (name != "model" && candidate.GetValueKind() == JsonValueKind.Number) break;
                    }
                    continue;
                }
                bool path = textureMap || name is "texture" or "texture_material" or "texture_normal" or
                    "model" or "model_tex" or "model_tex_material" or "model_tex_normal" or "texture_obj" or
                    "filename" or "tex";
                if (path && value is JsonValue)
                    RewriteReference(obj, key, textureMap || name == "filename", name != "filename");
                else if (value is JsonObject or JsonArray)
                    Visit(value, name == "textures");
            }
        }
        Visit(document);
    }

    private static string ResolveSource(string owner, string reference, bool uri)
    {
        if (reference.StartsWith("data://", StringComparison.OrdinalIgnoreCase))
            return DataAssetRemapper.Current.Resolve(reference) ??
                throw new FileNotFoundException($"Missing data model dependency: {reference}", reference);
        string path = uri ? DecodeUri(reference) : reference.Replace('\\', '/');
        if (uri && Uri.TryCreate(path, UriKind.Absolute, out Uri? absolute))
        {
            if (!absolute.IsFile) throw new InvalidDataException($"Remote model dependencies are unsupported: {reference}");
            path = absolute.LocalPath;
        }
        string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(owner)!, path));
        RequireFile(resolved);
        return resolved;
    }

    private static string DecodeUri(string reference)
    {
        if (reference.IndexOfAny(['?', '#', '\0']) >= 0)
            throw new InvalidDataException($"Unsupported model dependency URI: {reference}");
        return Uri.UnescapeDataString(reference).Replace('\\', '/');
    }

    private static string RelativeTarget(string owner, string target) =>
        Path.GetRelativePath(ArchiveDirectory(owner), target).Replace('\\', '/');

    private static string ArchiveDirectory(string path) => path[..path.LastIndexOf('/')];

    private static void RequireFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Missing object resource: {path}", path);
    }

    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".gif" or ".webp" or ".tif" or ".tiff" or ".hdr" or ".exr" or ".dds";

    private static void RequireSupportedModelFormat(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".fbx" or ".dae" or ".3ds" or ".ply" or ".x3d")
            throw new NotSupportedException($"nxObj cannot safely package or validate {extension} models and their external dependencies. Convert the source to GLB, glTF, or OBJ first.");
    }

    private static string RewriteObj(string text, bool isObj, string source, Func<string, string> rewrite)
    {
        string[] lines = text.Split('\n');
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            var tokens = Tokenize(line);
            if (tokens.Count == 0) continue;
            string directive = tokens[0].Value.ToLowerInvariant();
            if (isObj ? directive != "mtllib" :
                !(directive.StartsWith("map_", StringComparison.Ordinal) || directive is "bump" or "disp" or "decal" or "refl" or "norm"))
                continue;
            int first = isObj ? 1 : TexturePathIndex(tokens);
            if (first < 1 || first >= tokens.Count)
                throw new InvalidDataException($"Malformed model dependency in {source}: {line}");
            var replacements = new List<(int Start, int Length, string Value)>();
            while (first < tokens.Count)
            {
                int last = tokens.Count - 1;
                string Reference() => string.Join(" ", tokens.Skip(first).Take(last - first + 1).Select(t => t.Value));
                // An existing unquoted filename containing spaces wins over separate mtllib arguments.
                if (isObj)
                    while (last > first && !DependencyExists(source, Reference())) last--;
                string rewritten = rewrite(Reference());
                if (rewritten.Any(char.IsWhiteSpace) || rewritten.Contains('#')) rewritten = "\"" + rewritten + "\"";
                replacements.Add((tokens[first].Start, tokens[last].End - tokens[first].Start, rewritten));
                first = last + 1;
            }
            foreach (var replacement in replacements.AsEnumerable().Reverse())
                line = line.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, replacement.Value);
            lines[lineIndex] = line;
        }
        return string.Join("\n", lines);
    }

    private static bool DependencyExists(string owner, string reference)
    {
        try { return File.Exists(Path.Combine(Path.GetDirectoryName(owner)!, reference.Replace('\\', '/'))); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static List<(string Value, int Start, int End)> Tokenize(string line)
    {
        var tokens = new List<(string Value, int Start, int End)>();
        for (int i = 0; i < line.Length;)
        {
            if (char.IsWhiteSpace(line[i])) { i++; continue; }
            if (line[i] == '#') break;
            int start = i;
            char quote = '\0';
            var value = new StringBuilder();
            while (i < line.Length)
            {
                char c = line[i];
                if (quote == '\0' && (char.IsWhiteSpace(c) || c == '#')) break;
                if (c == quote) quote = '\0';
                else if (quote == '\0' && i == start && c is '\"' or '\'') quote = c;
                else value.Append(c);
                i++;
            }
            if (quote != '\0') throw new InvalidDataException("Unterminated quote in OBJ/MTL dependency.");
            tokens.Add((value.ToString(), start, i));
        }
        return tokens;
    }

    private static int TexturePathIndex(List<(string Value, int Start, int End)> tokens)
    {
        int i = 1;
        while (i < tokens.Count && tokens[i].Value.StartsWith('-'))
        {
            string option = tokens[i++].Value.ToLowerInvariant();
            if (option is "-o" or "-s" or "-t")
            {
                int count = 0;
                while (i < tokens.Count && count < 3 && double.TryParse(tokens[i].Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out _)) { i++; count++; }
                if (count == 0) return -1;
            }
            else if (option == "-mm") i += 2;
            else if (option is "-blendu" or "-blendv" or "-boost" or "-bm" or "-cc" or "-clamp" or
                "-imfchan" or "-texres" or "-type" or "-colorspace") i++;
            else return -1;
        }
        return i;
    }

    // The caller owns the archive; no file or entry streams survive AddFile.
    public void Dispose() => _disposed = true;
}
