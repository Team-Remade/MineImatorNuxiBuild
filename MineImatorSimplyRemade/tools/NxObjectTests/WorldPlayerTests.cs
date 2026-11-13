using System.IO.Compression;
using Cyotek.Data.Nbt;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.world;

internal static partial class Program
{
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
