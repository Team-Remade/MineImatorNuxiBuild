using System.IO.Compression;
using Cyotek.Data.Nbt;

namespace MineImatorSimplyRemade.core.world;

internal sealed record WorldDimension(
    string Name, string RegionPath, int BedrockId, int MinY, int MaxY, string Id = "minecraft:overworld");

internal sealed record MinecraftWorld(
    string Folder, string Name, bool IsBedrock, int SpawnX, int SpawnY, int SpawnZ,
    IReadOnlyList<WorldDimension> Dimensions, string SpawnDimension = "minecraft:overworld")
{
    public static MinecraftWorld Open(string path)
    {
        path = Path.GetFullPath(path);
        string folder;
        if (File.Exists(path))
        {
            if (path.EndsWith(".mcworld", StringComparison.OrdinalIgnoreCase))
            {
                string baseExtract = Path.Combine(Path.GetTempPath(), "MineImatorSimplyRemade", "mcworld");
                string extractDir = Path.Combine(baseExtract, Path.GetFileNameWithoutExtension(path));
                try
                {
                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, true);
                    Directory.CreateDirectory(extractDir);
                }
                catch
                {
                    extractDir = Path.Combine(baseExtract, $"{Path.GetFileNameWithoutExtension(path)}_{Guid.NewGuid():N}");
                    Directory.CreateDirectory(extractDir);
                }
                ZipFile.ExtractToDirectory(path, extractDir, overwriteFiles: true);

                string targetFolder = extractDir;
                if (!Directory.Exists(Path.Combine(targetFolder, "db")))
                {
                    string? found = Directory.EnumerateDirectories(extractDir, "*", SearchOption.AllDirectories)
                        .FirstOrDefault(d => Directory.Exists(Path.Combine(d, "db")) || File.Exists(Path.Combine(d, "level.dat")));
                    if (found != null) targetFolder = found;
                }

                folder = targetFolder;
            }
            else
            {
                folder = Path.GetDirectoryName(path)!;
            }
        }
        else
        {
            folder = path;
        }
        if (Directory.Exists(Path.Combine(folder, "db")))
        {
            var info = BedrockWorldReader.ReadLevelInfo(folder);
            return new MinecraftWorld(folder, info.Name, true, info.SpawnX, info.SpawnY, info.SpawnZ,
            [
                new("Overworld", "", 0, -64, 319),
                new("Nether", "", 1, 0, 127, "minecraft:the_nether"),
                new("The End", "", 2, 0, 255, "minecraft:the_end")
            ]);
        }

        using var source = File.OpenRead(Path.Combine(folder, "level.dat"));
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var buffer = JavaWorldReader.ReadBounded(gzip, 32 * 1024 * 1024);
        var root = NbtDocument.LoadDocument(buffer).DocumentRoot?.GetCompound("Data") ??
                   throw new InvalidDataException("The world has no level Data compound.");
        string name = root.GetTag("LevelName") is TagString tag ? tag.Value : Path.GetFileName(folder);
        int version = root.GetTag("DataVersion") is TagInt dataVersion ? dataVersion.Value : 0;
        var dimensions = new List<WorldDimension>();
        AddDimension("Overworld", "minecraft:overworld", folder, version >= 2825 ? -64 : 0, version >= 2825 ? 319 : 255);
        AddDimension("Nether", "minecraft:the_nether", Path.Combine(folder, "DIM-1"), 0, 255);
        AddDimension("The End", "minecraft:the_end", Path.Combine(folder, "DIM1"), 0, 255);
        string customRoot = Path.Combine(folder, "dimensions");
        if (Directory.Exists(customRoot))
        {
            foreach (string region in Directory.EnumerateDirectories(customRoot, "region", SearchOption.AllDirectories))
            {
                string dimensionFolder = Path.GetDirectoryName(region)!;
                string relative = Path.GetRelativePath(customRoot, dimensionFolder);
                string[] parts = relative.Split(Path.DirectorySeparatorChar);
                string id = parts.Length >= 2 ? $"{parts[0]}:{string.Join('/', parts.Skip(1))}" :
                    throw new InvalidDataException("A custom dimension is missing its namespace or resource path.");
                string displayName = id switch
                {
                    "minecraft:overworld" => "Overworld",
                    "minecraft:the_nether" => "Nether",
                    "minecraft:the_end" => "The End",
                    _ => relative
                };
                bool oldHeight = id is "minecraft:the_nether" or "minecraft:the_end";
                AddDimension(displayName, id, dimensionFolder, oldHeight ? 0 : -64, oldHeight ? 255 : 319);
            }
        }
        if (dimensions.Count == 0)
            throw new InvalidDataException("This world has no generated Anvil region folders.");
        var spawn = ReadJavaSpawn(root);
        return new MinecraftWorld(folder, name, false, spawn.X, spawn.Y, spawn.Z, dimensions, spawn.Dimension);

        void AddDimension(string displayName, string id, string dimensionFolder, int minY, int maxY)
        {
            string region = Path.Combine(dimensionFolder, "region");
            if (Directory.Exists(region))
            {
                var dimension = new WorldDimension(displayName, region, 0, minY, maxY, id);
                int existing = dimensions.FindIndex(d => d.Id == id);
                if (existing >= 0) dimensions[existing] = dimension;
                else dimensions.Add(dimension);
            }
        }
    }

    internal static (int X, int Y, int Z, string Dimension) ReadJavaSpawn(TagCompound data)
    {
        if (data.Contains("spawn"))
        {
            if (data.GetTag("spawn") is not TagCompound spawn ||
                spawn.GetTag("pos") is not TagIntArray position || position.Value.Length != 3 ||
                spawn.GetTag("dimension") is not TagString dimension || string.IsNullOrWhiteSpace(dimension.Value))
                throw new InvalidDataException("The world spawn must contain a three-integer pos array and a dimension string.");
            return (position.Value[0], position.Value[1], position.Value[2], dimension.Value);
        }
        if (data.GetTag("SpawnX") is TagInt x && data.GetTag("SpawnY") is TagInt y &&
            data.GetTag("SpawnZ") is TagInt z)
            return (x.Value, y.Value, z.Value, "minecraft:overworld");
        throw new InvalidDataException("The world metadata has no valid spawn position (spawn.pos or SpawnX/SpawnY/SpawnZ).");
    }

    public static (List<MinecraftWorld> Worlds, List<string> Warnings) Discover()
    {
        var worlds = new List<MinecraftWorld>();
        var warnings = new List<string>();
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new List<string> { Path.Combine(roaming, ".minecraft", "saves") };
        foreach (string package in new[] { "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe" })
            roots.Add(Path.Combine(local, "Packages", package, "LocalState", "games", "com.mojang", "minecraftWorlds"));
        string users = Path.Combine(roaming, "Minecraft Bedrock", "Users");
        if (Directory.Exists(users))
        {
            try
            {
                roots.AddRange(Directory.EnumerateDirectories(users).Select(user =>
                    Path.Combine(user, "games", "com.mojang", "minecraftWorlds")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Cannot browse Bedrock saves: {ex.Message}");
            }
        }
        foreach (string saveRoot in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(saveRoot)) continue;
            try
            {
                foreach (string folder in Directory.EnumerateDirectories(saveRoot))
                {
                    if (!File.Exists(Path.Combine(folder, "level.dat"))) continue;
                    try { worlds.Add(Open(folder)); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                    {
                        warnings.Add($"{Path.GetFileName(folder)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{saveRoot}: {ex.Message}");
            }
        }
        return (worlds.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList(), warnings);
    }
}

internal static class WorldImportService
{
    public static WorldVolume Read(MinecraftWorld world, WorldDimension dimension, WorldBounds bounds,
        CancellationToken cancellationToken, IProgress<float>? progress = null, bool allowEmpty = false)
    {
        bounds.Validate();
        var volume = new WorldVolume(bounds);
        ReadVolumes(world, dimension, [volume], cancellationToken, progress);
        if (volume.NonAirCount == 0 && !allowEmpty)
            throw new InvalidDataException("No terrain was found here. Choose generated chunks or another dimension.");
        return volume;
    }

    public static List<WorldVolume> ReadTiles(MinecraftWorld world, WorldDimension dimension,
        IReadOnlyList<WorldBounds> bounds, CancellationToken cancellationToken, IProgress<float>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var volumes = bounds.Select(b => new WorldVolume(b)).ToList();
        if (volumes.Count > 0)
            ReadVolumes(world, dimension, volumes, cancellationToken, progress);
        return volumes;
    }

    private static void ReadVolumes(MinecraftWorld world, WorldDimension dimension,
        IReadOnlyList<WorldVolume> volumes, CancellationToken cancellationToken, IProgress<float>? progress)
    {
        int total = volumes.Sum(v => ((v.Bounds.MaxX >> 4) - (v.Bounds.MinX >> 4) + 1) *
                                    ((v.Bounds.MaxZ >> 4) - (v.Bounds.MinZ >> 4) + 1));
        int completed = 0;
        using var java = world.IsBedrock ? null : new JavaWorldReader(dimension.RegionPath, cancellationToken);
        using var bedrock = world.IsBedrock ? new BedrockWorldReader(world.Folder, cancellationToken) : null;
        foreach (var volume in volumes)
        {
            var bounds = volume.Bounds;
            for (int z = bounds.MinZ >> 4; z <= bounds.MaxZ >> 4; z++)
            for (int x = bounds.MinX >> 4; x <= bounds.MaxX >> 4; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var blocks = bedrock != null
                    ? bedrock.ReadChunk(x, z, dimension.BedrockId)
                    : java!.ReadChunk(x, z);
                foreach (var block in blocks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    volume.Set(block.X, block.Y, block.Z, block.State);
                }
                progress?.Report((float)++completed / total);
            }
        }
    }
}
