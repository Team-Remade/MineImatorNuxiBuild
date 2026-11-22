using System.IO.Compression;
using Cyotek.Data.Nbt;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.world;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.ui.Panels;

internal static partial class Program
{
    private static void WorldLargeSelection(Fixture f)
    {
        var fullHeight = new WorldBounds(0, 0, 0, 511, 383, 511);
        fullHeight.Validate();
        Equal(512 * 384 * 512, fullHeight.Count, "Full-height terrain selection count");

        var wide = new WorldBounds(-1024, 0, 0, 1023, 0, 0);
        wide.Validate();
        Equal(2048, wide.Width, "Maximum horizontal selection width");

        var maximum = new WorldBounds(0, -2048, 0, 2047, 2047, 15);
        maximum.Validate();
        Equal(128 * 1024 * 1024, maximum.Count, "Maximum supported selection volume");
        Equal(128 * 1024 * 1024, WorldBounds.MaxBlocks, "Maximum supported block count");

        Reject(new WorldBounds(0, -2048, 0, 2047, 2047, 16), "Selections above the volume limit are rejected");
        Reject(new WorldBounds(0, 0, 0, 2048, 0, 0), "Selections wider than the horizontal limit are rejected");
        Reject(new WorldBounds(1, 0, 0, 0, 0, 0), "Reversed selection coordinates are rejected");
        Reject(new WorldBounds(int.MinValue, 0, 0, int.MinValue, 0, 0), "Extreme world coordinates are rejected");

        var bounds = new WorldBounds(-1024, -512, -3, 1023, 512, -3);
        var volume = new WorldVolume(bounds);
        volume.Set(-1024, -512, -3, "minecraft:stone");
        volume.Set(1023, 512, -3, "minecraft:water");
        volume.Set(0, 0, -3, "minecraft:dirt");
        int last = volume.Blocks.Length - 1;
        Equal(2_099_200, volume.Blocks.Length, "Allocation above the former two-million-block cap");
        Equal(1, volume.Blocks[0], "Negative-coordinate first block index");
        Equal(2, volume.Blocks[last], "Negative-coordinate last block index");
        Equal(3, volume.NonAirCount, "Large selection non-air count");

        var filter = new WorldBlockFilter { Enabled = true };
        filter.FilteredBlocks.Add("water");
        var filtered = volume.ApplyFilter(filter);
        Check(!ReferenceEquals(volume, filtered), "Active filter copies a large selection");
        Equal(0, filtered.Blocks[last], "Filtering updates the last large-selection index");
        Equal(2, filtered.NonAirCount, "Filtered large-selection non-air count");

        string path = f.NewPath(".schematic");
        volume.SaveSchematic(path);
        var schematic = SchematicData.Load(path);
        Equal(bounds.Width, schematic.Width, "Large schematic width roundtrip");
        Equal(bounds.Height, schematic.Height, "Large schematic height roundtrip");
        Equal(bounds.Length, schematic.Length, "Large schematic length roundtrip");
        Equal(volume.Blocks.Length, schematic.PaletteIndices.Length, "Large schematic block data roundtrip");
        Equal("minecraft:stone", schematic.Palette![schematic.PaletteIndices[0]], "Negative-coordinate first schematic block");
        Equal("minecraft:water", schematic.Palette[schematic.PaletteIndices[last]], "Negative-coordinate last schematic block");

        static void Reject(WorldBounds bounds, string message)
        {
            bool rejected = false;
            try { bounds.Validate(); }
            catch (Exception ex) when (ex is InvalidDataException or OverflowException) { rejected = true; }
            Check(rejected, message);
        }
    }

    private static void WorldPreviewScheduling(Fixture f)
    {
        Equal(3, WorldImporter.ActivePreviewTileRadius, "Active preview loads a seven-by-seven tile neighborhood");
        Equal(448, WorldImporter.PreviewWidthInBlocks, "Active preview coverage in blocks");
        Check(WorldImporter.IsTileInActiveNeighborhood(-3, 3, 0, 0), "Active preview includes its far corner");
        Check(!WorldImporter.IsTileInActiveNeighborhood(-4, 0, 0, 0), "Active preview excludes tiles beyond its radius");

        var movedCenter = (-12, 9);
        Check(WorldImporter.ShouldRetainTile(-16, 13, movedCenter.Item1, movedCenter.Item2),
            "Retained cache includes a negative-coordinate corner at the radius boundary");
        Check(WorldImporter.ShouldRetainTile(-8, 9, movedCenter.Item1, movedCenter.Item2), "Retained cache includes its east edge");
        Check(!WorldImporter.ShouldRetainTile(-8, 9, -3, 9), "Moving the center evicts tiles outside the new retention window");
        Check(WorldImporter.ShouldRetainTile(-12, 5, movedCenter.Item1, movedCenter.Item2), "Retained cache includes its south edge");
        Check(WorldImporter.ShouldRetainTile(-12, 13, movedCenter.Item1, movedCenter.Item2), "Retained cache includes its north edge");
        Check(WorldImporter.ShouldRetainTile(-16, 9, movedCenter.Item1, movedCenter.Item2), "Retained cache includes its west edge");
        Check(!WorldImporter.ShouldRetainTile(-17, 9, movedCenter.Item1, movedCenter.Item2), "Tiles beyond the west edge are evicted");
        Check(!WorldImporter.ShouldRetainTile(-7, 9, movedCenter.Item1, movedCenter.Item2), "Tiles beyond the east edge are evicted");
        Check(!WorldImporter.ShouldRetainTile(-12, 4, movedCenter.Item1, movedCenter.Item2), "Tiles beyond the south edge are evicted");
        Check(!WorldImporter.ShouldRetainTile(-12, 14, movedCenter.Item1, movedCenter.Item2), "Tiles beyond the north edge are evicted");
        Check(!WorldImporter.ShouldRetainTile(int.MinValue, 0, int.MaxValue, 0), "Retention distance cannot overflow at extreme coordinates");

        var source = new TaskCompletionSource<SchematicData?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var build = new SpawnMenu.SchematicPreviewBuild(new SpawnMenu(), "World preview", "",
            source.Task, cancellation.Token);
        Check(!build.Advance(), "Preview yields its pending terrain preparation");
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
            Check(!build.Advance(), "Preview polling never waits for the worker");
        Check(elapsed.ElapsedMilliseconds < 100, "Pending terrain leaves the UI thread responsive");
        cancellation.Cancel();
        source.SetResult(null);
        bool cancelled = false;
        try { build.Advance(); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Cancelled preview cannot install a late worker result");

        using var empty = new SpawnMenu().CreateWorldPreviewBuild(
            new WorldVolume(new WorldBounds(0, 0, 0, 0, 0, 0)), new WorldBlockFilter(), "", CancellationToken.None);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!empty.Advance())
        {
            Check(timeout.Elapsed < TimeSpan.FromSeconds(10), "Empty preview worker finishes");
            Thread.Sleep(1);
        }
        Check(empty.TakeResult() == null, "Empty terrain needs no graphics context or mesh");

        var failed = Task.FromException<SchematicData?>(new IOException("Preview read regression"));
        using var faulted = new SpawnMenu.SchematicPreviewBuild(new SpawnMenu(), "World preview", "",
            failed, CancellationToken.None);
        Check(!faulted.Advance(), "Failed worker is observed on the next UI poll");
        bool reported = false;
        try { faulted.Advance(); }
        catch (IOException ex) when (ex.Message == "Preview read regression") { reported = true; }
        Check(reported, "Worker failures reach the importer instead of a success-shaped empty preview");
    }

    private static void WorldPlayerPositionReading(Fixture f)
    {
        string legacy = Path.Combine(f.Root, "legacy");
        WriteWorldLevel(legacy, data => data.Value.Add(PlayerCompound("Player", 10.5, 70, -3.25, "minecraft:overworld")));
        var legacyPlayer = MinecraftWorld.Open(legacy).ReadPlayerPosition();
        Check(legacyPlayer is { X: 10.5, Y: 70, Z: -3.25, Dimension: "minecraft:overworld" }, "Legacy level.dat Player tag");

        const string uuid = "01234567-89ab-cdef-0123-456789abcdef";
        string modern = Path.Combine(f.Root, "modern");
        WriteWorldLevel(modern, data => data.Value.Add(new TagIntArray("singleplayer_uuid",
            [0x01234567, unchecked((int)0x89abcdef), 0x01234567, unchecked((int)0x89abcdef)])));
        WriteNbt(Path.Combine(modern, "players", "data", "ffffffff-0000-0000-0000-000000000000.dat"),
            PlayerCompound("", 1, 2, 3, "minecraft:overworld"));
        WriteNbt(Path.Combine(modern, "players", "data", uuid + ".dat"),
            PlayerCompound("", -120.5, 45, 900.75, "minecraft:the_nether"));
        File.SetLastWriteTimeUtc(Path.Combine(modern, "players", "data", uuid + ".dat"), DateTime.UtcNow.AddDays(-1));
        Directory.CreateDirectory(Path.Combine(modern, "dimensions", "minecraft", "the_nether", "region"));
        var modernWorld = MinecraftWorld.Open(modern);
        var modernPlayer = modernWorld.ReadPlayerPosition();
        Check(modernPlayer is { X: -120.5, Y: 45, Z: 900.75, Dimension: "minecraft:the_nether" },
            "26.1 singleplayer_uuid int array resolves players/data/<uuid>.dat");
        Check(modernWorld.Dimensions.Any(d => d.Id == "minecraft:the_nether"), "26.1 namespaced nether dimension");

        string textUuid = Path.Combine(f.Root, "text-uuid");
        WriteWorldLevel(textUuid, data => data.Value.Add(new TagString("singleplayer_uuid", uuid.ToUpperInvariant())));
        WriteNbt(Path.Combine(textUuid, "players", "data", uuid + ".dat"), PlayerCompound("", 4, 5, 6, "minecraft:overworld"));
        Check(MinecraftWorld.Open(textUuid).ReadPlayerPosition() is { X: 4, Y: 5, Z: 6 }, "String singleplayer_uuid");

        string ownerless = Path.Combine(f.Root, "ownerless");
        WriteWorldLevel(ownerless, _ => { });
        WriteNbt(Path.Combine(ownerless, "players", "data", "aaaaaaaa-0000-0000-0000-000000000000.dat"),
            PlayerCompound("", 7, 8, 9, "minecraft:overworld"));
        File.WriteAllText(Path.Combine(ownerless, "players", "data", "broken.dat"), "not nbt");
        Check(MinecraftWorld.Open(ownerless).ReadPlayerPosition() is { X: 7, Y: 8, Z: 9 },
            "Missing owner falls back to a readable player file and skips corrupt files");

        string none = Path.Combine(f.Root, "none");
        WriteWorldLevel(none, data => data.Value.Add(new TagIntArray("singleplayer_uuid", [1, 2, 3, 4])));
        Check(MinecraftWorld.Open(none).ReadPlayerPosition() == null, "Missing player data is optional");
    }

    private static void WorldDefaultBlockStates(Fixture f)
    {
        var versionRoot = typeof(BlockRegistry).GetProperty(nameof(BlockRegistry.VersionRoot))!;
        object? previousRoot = versionRoot.GetValue(null);
        versionRoot.SetValue(null, Path.Combine(ParticleDataRoot(), "minecraft", "versions", "26.3"));
        try
        {
            CheckWorldDefaultBlockStates();
        }
        finally
        {
            versionRoot.SetValue(null, previousRoot);
        }
    }

    private static void CheckWorldDefaultBlockStates()
    {
        Check(JavaWorldReader.ParseState(new TagString("", "minecraft:oak_log")) == "minecraft:oak_log[axis=y]",
            "26.1+ bare palette string expands to the default axis");
        var wrapped = new TagCompound("");
        wrapped.Value.Add(new TagString("", "minecraft:stone_brick_stairs"));
        Check(JavaWorldReader.ParseState(wrapped) ==
              "minecraft:stone_brick_stairs[facing=north,half=bottom,shape=straight,waterlogged=false]",
            "26.1+ wrapped palette entry expands to the default state");
        var legacy = new TagCompound("");
        legacy.Value.Add(new TagString("Name", "minecraft:nether_portal"));
        Check(JavaWorldReader.ParseState(legacy) == "minecraft:nether_portal[axis=x]",
            "Property-less legacy compound uses the vanilla default, not the first variant");

        var explicitState = new TagCompound("");
        explicitState.Value.Add(new TagString("id", "minecraft:oak_log"));
        var properties = new TagCompound("properties");
        properties.Value.Add(new TagString("axis", "z"));
        explicitState.Value.Add(properties);
        Check(JavaWorldReader.ParseState(explicitState) == "minecraft:oak_log[axis=z]", "Explicit properties are kept");
        Check(JavaWorldReader.ParseState(new TagString("", "minecraft:stone")) == "minecraft:stone",
            "Blocks without properties stay bare");
        Check(JavaWorldReader.ParseState(new TagString("", "examplemod:oak_log")) == "examplemod:oak_log",
            "Modded IDs are not given vanilla defaults");
    }

    private static void WriteWorldLevel(string folder, Action<TagCompound> configure)
    {
        Directory.CreateDirectory(Path.Combine(folder, "region"));
        var data = new TagCompound("Data");
        data.Value.Add(new TagString("LevelName", Path.GetFileName(folder)));
        data.Value.Add(new TagInt("DataVersion", 4700));
        var spawn = new TagCompound("spawn");
        spawn.Value.Add(new TagIntArray("pos", [0, 64, 0]));
        spawn.Value.Add(new TagString("dimension", "minecraft:overworld"));
        data.Value.Add(spawn);
        configure(data);
        var root = new TagCompound("");
        root.Value.Add(data);
        WriteNbt(Path.Combine(folder, "level.dat"), root);
    }

    private static TagCompound PlayerCompound(string name, double x, double y, double z, string dimension)
    {
        var player = new TagCompound(name);
        var position = new TagList("Pos", TagType.Double);
        position.Value.Add(new TagDouble(x));
        position.Value.Add(new TagDouble(y));
        position.Value.Add(new TagDouble(z));
        player.Value.Add(position);
        player.Value.Add(new TagString("Dimension", dimension));
        return player;
    }

    private static void WriteNbt(string path, TagCompound root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var output = File.Create(path);
        using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        new NbtDocument(root).Save(gzip);
    }
}
