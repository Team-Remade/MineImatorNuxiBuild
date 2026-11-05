using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageMagick;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.project;

public sealed class NxObjectManifest
{
    [JsonRequired] public string Format { get; set; } = "nxObj";
    [JsonRequired] public int Version { get; set; } = 1;
    [JsonRequired] public ProjectSceneObjectEntry Root { get; set; } = new();
    public List<ProjectSceneObjectEntry> ObjectLibrary { get; set; } = new();
    public Dictionary<string, string> ResourcePacks { get; set; } = new();
}

/// <summary>Portable editable object hierarchies, with their source assets and library dependencies.</summary>
public static class NxObjectArchive
{
    private const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxEntries = 100_000;
    private const long MaxManifestBytes = 32L * 1024 * 1024;

    public static void Export(string filePath, SceneObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var manager = ProjectManager.Instance;
        var manifest = new NxObjectManifest { Root = ProjectSceneSerializer.SerializeObjectForLibrary(root) };

        // Internal model parts need their original construction hierarchy, not an empty fallback bone.
        SceneObject source = root;
        for (var ancestor = root.Parent; root.SourceNodePath is not { Count: > 0 } && ancestor != null &&
             (root is BoneSceneObject || root.SpawnCategory == "Custom Models"); ancestor = ancestor.Parent)
        {
            if (!string.IsNullOrEmpty(ancestor.SourceAssetPath) &&
                (string.IsNullOrEmpty(source.SourceAssetPath) ||
                 string.Equals(source.SourceAssetPath, ancestor.SourceAssetPath, StringComparison.OrdinalIgnoreCase)))
                source = ancestor;
        }
        if (source != root)
        {
            var indices = new List<int>();
            for (var node = root; node != source; node = node.Parent!)
                indices.Insert(0, node.Parent!.Children.ToList().IndexOf(node));
            manifest.Root.SourceAssetPath = manager.ToAssetReference(source.SourceAssetPath);
            manifest.Root.SourceNodePath = (source.SourceNodePath ?? new List<int>()).Concat(indices).ToList();
            if (string.IsNullOrEmpty(manifest.Root.TextureOverridePath))
                manifest.Root.TextureOverridePath = manager.ToAssetReference(source.TextureOverridePath);
        }

        var pending = new Queue<ProjectSceneObjectEntry>(Walk(manifest.Root));
        var libraryIds = new HashSet<string>(StringComparer.Ordinal);
        var selectedTemplates = new List<ProjectSceneObjectEntry>();
        while (pending.TryDequeue(out var node))
        {
            string id = node.ParticleLibraryEntryId;
            if (string.IsNullOrEmpty(id) || !libraryIds.Add(id)) continue;
            var template = manager.Manifest.ObjectLibrary.SelectMany(Walk)
                .FirstOrDefault(entry => entry.LibraryEntryId == id)
                ?? throw new InvalidDataException($"The particle model '{node.ParticleLibraryDisplayName}' is missing from the object library.");
            // A selected ancestor already contains descendant templates, regardless of discovery order.
            if (selectedTemplates.Any(selected => Walk(selected).Contains(template))) continue;
            selectedTemplates.RemoveAll(selected => Walk(template).Contains(selected));
            selectedTemplates.Add(template);
            foreach (var child in Walk(template))
            {
                libraryIds.Add(child.LibraryEntryId);
                pending.Enqueue(child);
            }
        }
        foreach (var template in selectedTemplates)
            manifest.ObjectLibrary.Add(JsonSerializer.Deserialize(JsonSerializer.Serialize(template,
                AppJsonContext.Default.ProjectSceneObjectEntry), AppJsonContext.Default.ProjectSceneObjectEntry)!);

        string destination = Path.GetFullPath(filePath);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                using var resources = new NxObjectResources(archive);
                int itemSnapshot = 0;
                foreach (var entry in manifest.ObjectLibrary.Prepend(manifest.Root))
                {
                    ClearExternalReferences(entry);
                    foreach (var node in Walk(entry))
                    {
                        if (node.SpawnCategory == "Blocks")
                        {
                            string sourcePack = BlockRegistry.GetBlockSourceId(node.ObjectType);
                            if (!string.IsNullOrEmpty(sourcePack))
                                node.ResourcePackId = MinecraftDataLoader.NormalizeResourcePackStack(node.ResourcePackId + ";" + sourcePack);
                        }
                        else if (node.SpawnCategory == "Scenery" && !string.IsNullOrEmpty(node.SourceAssetPath))
                        {
                            var schematic = SchematicData.Load(manager.ResolveAssetPath(node.SourceAssetPath));
                            if (schematic.Palette != null)
                                node.ResourcePackId = MinecraftDataLoader.NormalizeResourcePackStack(node.ResourcePackId + ";" +
                                    string.Join(';', schematic.Palette.Values.Select(block => block.Split('[')[0])
                                        .Select(BlockRegistry.GetBlockSourceId).Where(id => !string.IsNullOrEmpty(id))));
                        }
                        RewriteFiles(node, path => resources.AddFile(manager.ResolveAssetPath(path)));
                        if (node.SpawnCategory == "Items" && node.TextureType != "block" &&
                            string.IsNullOrEmpty(node.TemporaryItemSheetPath) &&
                            ItemsAtlas.TilePixels.TryGetValue(node.ItemTileKey, out var pixels))
                        {
                            ItemsAtlas.TryGetTileDimensions(node.ItemTileKey, out int width, out int height);
                            string path = $"resources/items/{itemSnapshot++}.png";
                            using (var image = new MagickImage(pixels, new MagickReadSettings
                                   { Width = (uint)width, Height = (uint)height, Depth = 8, Format = MagickFormat.Rgba }))
                            using (var itemOutput = archive.CreateEntry(path, CompressionLevel.Optimal).Open())
                                image.Write(itemOutput, MagickFormat.Png);
                            node.TemporaryItemSheetPath = path;
                            node.TemporaryItemSheetColumns = node.TemporaryItemSheetRows = 1;
                            node.TemporaryItemSheetColumnIndex = node.TemporaryItemSheetRowIndex = 0;
                        }
                        node.LibrarySourceId = "";
                        node.RestoreExactChildren = true;
                    }
                }
                ExportResourcePacks(manifest, archive, resources);
                using var output = archive.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
                JsonSerializer.Serialize(output, manifest, AppJsonContext.Default.NxObjectManifest);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static SceneObject Import(string filePath, Viewport viewport, SpawnMenu spawnMenu)
    {
        var manager = ProjectManager.Instance;
        string storage = manager.HasProject ? Path.Combine(manager.ModelsFolder, "nxobjects") :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MineImatorSimplyRemade", "nxobjects");
        string directory = Path.Combine(storage, Guid.NewGuid().ToString("N"));
        var originalRoots = viewport.SceneObjects.ToHashSet();
        var addedLibrary = new List<ProjectSceneObjectEntry>();
        var installedPacks = new List<string>();
        try
        {
            var manifest = Extract(filePath, directory);
            var libraryIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var node in manifest.ObjectLibrary.SelectMany(Walk))
                if (!string.IsNullOrEmpty(node.LibraryEntryId))
                    libraryIds.TryAdd(node.LibraryEntryId, Guid.NewGuid().ToString("N"));

            var packIds = InstallResourcePacks(manifest, directory, installedPacks);
            foreach (var entry in manifest.ObjectLibrary.Prepend(manifest.Root))
            {
                ClearExternalReferences(entry);
                foreach (var node in Walk(entry))
                {
                    RewriteFiles(node, path => manager.ToAssetReference(ContainedFile(directory, path)));
                    node.LibraryEntryId = libraryIds.GetValueOrDefault(node.LibraryEntryId, "");
                    node.LibrarySourceId = "";
                    node.ParticleLibraryEntryId = libraryIds.GetValueOrDefault(node.ParticleLibraryEntryId, "");
                    node.CameraActive = false;
                    node.ResourcePackId = RemapPacks(node.ResourcePackId, packIds);
                    node.NormalResourcePackId = RemapPacks(node.NormalResourcePackId, packIds);
                    node.EmissionResourcePackId = RemapPacks(node.EmissionResourcePackId, packIds);
                    foreach (var (oldId, newId) in packIds)
                        node.ItemTileKey = node.ItemTileKey.Replace("resourcepack:" + oldId + ":", "resourcepack:" + newId + ":", StringComparison.OrdinalIgnoreCase);
                    if (!string.IsNullOrEmpty(node.TemporaryItemSheetPath))
                    {
                        // Atlas keys are process-global; never reuse another import's custom tile.
                        string key = "nxobj:" + Guid.NewGuid().ToString("N");
                        node.ItemTileKey = key;
                        node.TemporaryItemSheetCacheKey = key;
                    }
                }
            }
            if (installedPacks.Count > 0)
            {
                BlockRegistry.Initialize();
                var newPackIds = packIds.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
                TerrainAtlas.LoadAdditionalResourcePacks(newPackIds);
                ItemsAtlas.LoadAdditionalResourcePacks(newPackIds);
                CtmAtlas.Initialize(spawnMenu.Gl);
                spawnMenu.RefreshExternalAssetOptions();
            }
            foreach (var entry in manifest.ObjectLibrary)
            {
                manager.Manifest.ObjectLibrary.Add(entry);
                addedLibrary.Add(entry);
            }
            var root = ProjectSceneSerializer.SpawnObjectFromEntry(manifest.Root, viewport, spawnMenu)
                ?? throw new InvalidDataException("The object could not be loaded.");
            LoadAlbedoTextures(root);
            Timeline.Instance?.LoadKeyframesForAllObjects(new[] { root }.Concat(root.GetAllDescendants()));
            manager.SetDirty(true);
            return root;
        }
        catch
        {
            viewport.SceneObjects.RemoveAll(node => !originalRoots.Contains(node));
            foreach (var entry in addedLibrary) manager.Manifest.ObjectLibrary.Remove(entry);
            foreach (string path in installedPacks) if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    // Kept independent of GPU loading so malformed archives are rejected before scene mutation.
    internal static NxObjectManifest Extract(string filePath, string directory)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("The object archive contains too many files.");
        foreach (var entry in archive.Entries)
        {
            ValidateRelativePath(entry.FullName);
            if (!paths.Add(entry.FullName) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("The object archive contains duplicate paths or symbolic links.");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaxExpandedBytes) throw new InvalidDataException("The object archive exceeds the 2 GiB expanded size limit.");
        }
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("The object archive has no manifest.");
        if (manifestEntry.Length > MaxManifestBytes) throw new InvalidDataException("The object manifest is too large.");
        NxObjectManifest manifest;
        using (var stream = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize(stream, AppJsonContext.Default.NxObjectManifest)
                ?? throw new InvalidDataException("The object manifest is empty.");
        if (manifest.Format != "nxObj" || manifest.Version != 1 || manifest.Root == null ||
            manifest.ObjectLibrary == null || manifest.ResourcePacks == null)
            throw new InvalidDataException("Unsupported or invalid nxObj format version.");

        var libraryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in manifest.ObjectLibrary.SelectMany(Walk))
            if (!string.IsNullOrEmpty(node.LibraryEntryId) && !libraryIds.Add(node.LibraryEntryId))
                throw new InvalidDataException("Duplicate object library IDs in the object archive.");
        foreach (var root in manifest.ObjectLibrary.Prepend(manifest.Root))
        {
            var objectIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in Walk(root))
            {
                if (node.Children == null || node.Keyframes == null ||
                    (!string.IsNullOrEmpty(node.ObjectId) && !objectIds.Add(node.ObjectId)) ||
                    node.SourceNodePath?.Any(index => index < 0) == true ||
                    (!string.IsNullOrEmpty(node.ParticleLibraryEntryId) && !libraryIds.Contains(node.ParticleLibraryEntryId)))
                    throw new InvalidDataException("Invalid object hierarchy or duplicate object IDs.");
                RewriteFiles(node, path =>
                {
                    ValidateResource(path, paths);
                    if (Path.GetExtension(path).Equals(".nxobj", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Nested nxObj source objects are not supported.");
                    return path;
                });
            }
        }
        foreach (var path in manifest.ResourcePacks.Values) ValidateResource(path, paths);

        if (Directory.Exists(directory)) throw new IOException("The object extraction directory already exists.");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var entry in archive.Entries)
            {
                string destination = Path.Combine(directory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination);
            }
            foreach (string resource in paths.Where(path => !path.EndsWith('/')))
                NxObjectResources.ValidateModelReferences(Path.Combine(directory, resource), directory);
            return manifest;
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') ||
            Path.IsPathRooted(path) || path.TrimEnd('/').Split('/').Any(part => part is "" or "." or ".." ||
                part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("The object archive contains an unsafe path.");
    }

    private static void ValidateResource(string path, HashSet<string> paths)
    {
        ValidateRelativePath(path);
        if (!path.StartsWith("resources/", StringComparison.Ordinal) || !paths.Contains(path) || path.EndsWith('/'))
            throw new InvalidDataException($"The object resource '{path}' is missing or invalid.");
    }

    private static string ContainedFile(string directory, string path)
    {
        ValidateRelativePath(path);
        string fullPath = Path.GetFullPath(Path.Combine(directory, path));
        if (!fullPath.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            throw new InvalidDataException($"The object resource '{path}' is missing or invalid.");
        return fullPath;
    }

    private static IEnumerable<ProjectSceneObjectEntry> Walk(ProjectSceneObjectEntry entry)
    {
        yield return entry;
        foreach (var child in entry.Children ?? throw new InvalidDataException("Invalid object children."))
            foreach (var node in Walk(child ?? throw new InvalidDataException("Invalid object child."))) yield return node;
    }

    private static void RewriteFiles(ProjectSceneObjectEntry node, Func<string, string> rewrite)
    {
        string File(string? path) => string.IsNullOrWhiteSpace(path) ? "" : rewrite(path);
        if (!string.IsNullOrWhiteSpace(node.SourceAssetPath) &&
            Path.GetExtension(node.SourceAssetPath).ToLowerInvariant() is not
                (".glb" or ".gltf" or ".obj" or ".mimodel" or ".miobject" or ".schematic" or ".schem" or ".stl"))
            throw new NotSupportedException("This source model format cannot yet be packaged safely in nxObj. Convert the model to GLB, glTF, or OBJ first.");
        node.SourceAssetPath = File(node.SourceAssetPath);
        node.TextureOverridePath = File(node.TextureOverridePath);
        node.AlbedoTexturePath = File(node.AlbedoTexturePath);
        node.NormalTexturePath = File(node.NormalTexturePath);
        node.EmissionTexturePath = File(node.EmissionTexturePath);
        node.SurfaceTexturePath = File(node.SurfaceTexturePath);
        if (node.OcclusionTexturePath != null) node.OcclusionTexturePath = File(node.OcclusionTexturePath);
        if (node.RoughnessTexturePath != null) node.RoughnessTexturePath = File(node.RoughnessTexturePath);
        if (node.MetallicTexturePath != null) node.MetallicTexturePath = File(node.MetallicTexturePath);
        if (node.HeightTexturePath != null) node.HeightTexturePath = File(node.HeightTexturePath);
        node.TemporaryItemSheetPath = File(node.TemporaryItemSheetPath);
        if (!string.IsNullOrEmpty(node.TextMeshFontPath) && !node.TextMeshFontPath.Equals("minecraftia", StringComparison.OrdinalIgnoreCase))
            node.TextMeshFontPath = File(node.TextMeshFontPath);
        foreach (var (key, track) in node.Keyframes)
        {
            if (key is not ("material.texture" or "text.font")) continue;
            foreach (var frame in track)
            {
                string? path = frame.Value is JsonElement { ValueKind: JsonValueKind.String } value ? value.GetString() : frame.Value as string;
                if (!string.IsNullOrEmpty(path) && !(key == "text.font" && path.Equals("minecraftia", StringComparison.OrdinalIgnoreCase)))
                    frame.Value = File(path);
            }
        }
    }

    private static void ClearExternalReferences(ProjectSceneObjectEntry root)
    {
        var ids = Walk(root).Select(node => node.ObjectId).ToHashSet(StringComparer.Ordinal);
        string Reference(string id) => ids.Contains(id) ? id : "";
        foreach (var node in Walk(root))
        {
            node.CameraTextureObjectId = Reference(node.CameraTextureObjectId);
            node.ParticleAttractorObjectId = Reference(node.ParticleAttractorObjectId);
            foreach (var constraint in node.CopyConstraints ?? []) constraint.TargetObjectId = Reference(constraint.TargetObjectId);
            foreach (var constraint in node.BoneConstraints ?? [])
            {
                var ik = constraint.InverseKinematics;
                ik.TargetObjectId = Reference(ik.TargetObjectId);
                ik.PoleObjectId = Reference(ik.PoleObjectId);
                foreach (var bone in ik.Bones) bone.BoneObjectId = Reference(bone.BoneObjectId);
            }
            foreach (var (key, track) in node.Keyframes)
            {
                if (!(key.StartsWith("constraint.", StringComparison.Ordinal) && (key.EndsWith(".ik.target", StringComparison.Ordinal) || key.EndsWith(".ik.pole", StringComparison.Ordinal))) &&
                    !(key.StartsWith("copy_constraint.", StringComparison.Ordinal) && key.EndsWith(".target", StringComparison.Ordinal))) continue;
                foreach (var frame in track)
                {
                    string? id = frame.Value is JsonElement { ValueKind: JsonValueKind.String } value ? value.GetString() : frame.Value as string;
                    if (id != null) frame.Value = Reference(id);
                }
            }
        }
    }

    private static void ExportResourcePacks(NxObjectManifest manifest, ZipArchive archive, NxObjectResources resources)
    {
        var manager = ProjectManager.Instance;
        var ids = manifest.ObjectLibrary.Prepend(manifest.Root).SelectMany(Walk)
            .SelectMany(node => new[] { node.ResourcePackId, node.NormalResourcePackId, node.EmissionResourcePackId })
            .SelectMany(MinecraftDataLoader.GetResourcePackStack).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string id in ids)
        {
            string? container = null;
            foreach (string root in new[] { MinecraftDataLoader.GetBasePath(), manager.ProjectFolder }.Where(Directory.Exists))
                foreach (string kind in new[] { "resourcepacks", "javamods" })
                {
                    string folder = Path.Combine(root, "mods", kind);
                    if (!Directory.Exists(folder)) continue;
                    foreach (string path in Directory.EnumerateFileSystemEntries(folder).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                        if (MinecraftDataLoader.GetResourcePackId(Path.GetFileName(path)) == id) container = path;
                }
            if (container == null) throw new FileNotFoundException($"The resource pack '{id}' is not installed.");
            if (File.Exists(container)) manifest.ResourcePacks[id] = resources.AddFile(container);
            else
            {
                string entryPath = $"resources/packs/{manifest.ResourcePacks.Count}.zip";
                using var output = archive.CreateEntry(entryPath, CompressionLevel.NoCompression).Open();
                using var pack = new ZipArchive(output, ZipArchiveMode.Create);
                foreach (string file in Directory.EnumerateFiles(container, "*", SearchOption.AllDirectories))
                    pack.CreateEntryFromFile(file, Path.GetRelativePath(container, file).Replace('\\', '/'), CompressionLevel.Optimal);
                manifest.ResourcePacks[id] = entryPath;
            }
        }
    }

    private static Dictionary<string, string> InstallResourcePacks(NxObjectManifest manifest, string directory, List<string> installed)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string folder = ProjectManager.Instance.HasProject ? ProjectManager.Instance.ResourcePacksFolder :
            Path.Combine(MinecraftDataLoader.GetBasePath(), "mods", "resourcepacks");
        foreach (var (id, relative) in manifest.ResourcePacks)
        {
            string source = ContainedFile(directory, relative);
            using (var pack = ZipFile.OpenRead(source))
            {
                long expanded = 0;
                if (pack.Entries.Count > MaxEntries) throw new InvalidDataException("The resource pack contains too many files.");
                foreach (var entry in pack.Entries)
                {
                    ValidateRelativePath(entry.FullName);
                    expanded = checked(expanded + entry.Length);
                    if (expanded > MaxExpandedBytes) throw new InvalidDataException("The resource pack is too large.");
                }
            }
            string name = "nxobj_" + Guid.NewGuid().ToString("N") + ".zip";
            Directory.CreateDirectory(folder);
            string destination = Path.Combine(folder, name);
            File.Copy(source, destination);
            installed.Add(destination);
            ids.Add(id, MinecraftDataLoader.GetResourcePackId(name));
        }
        return ids;
    }

    private static string RemapPacks(string selection, Dictionary<string, string> ids) =>
        string.Join(';', MinecraftDataLoader.GetResourcePackStack(selection).Select(id => ids.TryGetValue(id, out var replacement)
            ? replacement : throw new InvalidDataException($"The resource pack '{id}' is missing from the object archive.")));

    private static void LoadAlbedoTextures(SceneObject root)
    {
        foreach (var node in new[] { root }.Concat(root.GetAllDescendants()))
        {
            if (string.IsNullOrEmpty(node.AlbedoTexturePath)) continue;
            uint texture = node.LoadTexture(node.AlbedoTexturePath);
            if (texture == 0) throw new InvalidDataException($"Could not load texture '{node.AlbedoTexturePath}'.");
            if (node is MiBoneSceneObject bone) bone.OverrideTexture(texture);
            else foreach (var mesh in node.Visuals) if (!mesh.PickOnly) mesh.TextureId = texture;
        }
        root.RefreshInheritedMaterials();
    }
}
