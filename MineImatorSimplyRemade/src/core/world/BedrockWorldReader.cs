using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MineImatorSimplyRemade.core.world;

/// <summary>
/// Read-only Bedrock disk importer for persistent paletted subchunks v8/v9.
/// Coordinates use signed subchunk Y and XZY-packed block indices. Runtime palettes
/// and legacy numeric terrain are deliberately rejected. Unknown names/properties
/// remain visible in output, never replaced with air.
/// See minecraft.wiki/w/Bedrock_Edition_level_format for the disk layout.
/// </summary>
internal sealed class BedrockWorldReader : IDisposable
{
    private readonly BedrockLevelDb database;
    private readonly CancellationToken cancellation;
    private bool disposed;

    public BedrockWorldReader(string worldPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        cancellation = cancellationToken;
        string root = Path.GetFullPath(worldPath);
        string db = Path.Combine(root, "db");
        if (!Directory.Exists(db)) throw new DirectoryNotFoundException($"Bedrock database directory not found: {db}");
        database = new BedrockLevelDb(db, cancellationToken);
    }

    /// <summary>
    /// Reads level.dat without opening LevelDB. Missing spawn fields, wrong NBT types,
    /// unsupported headers and truncated payloads are errors, not default spawn (0,0,0).
    /// </summary>
    public static (string Name, int SpawnX, int SpawnY, int SpawnZ) ReadLevelInfo(string worldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        string path = Path.Combine(worldPath, "level.dat");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header);
        int size = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (version is < 1 or > 10)
            throw new NotSupportedException($"Unsupported Bedrock level.dat header version {version}.");
        if (size < 1 || size > 64 * 1024 * 1024 || stream.Length != size + 8L)
            throw new InvalidDataException("Invalid Bedrock level.dat payload length.");
        byte[] payload = new byte[size];
        stream.ReadExactly(payload);
        var nbt = new LittleNbt(payload, CancellationToken.None);
        Dictionary<string, object> root = nbt.Root();
        if (nbt.Remaining != 0) throw new InvalidDataException("Trailing bytes after level.dat NBT.");
        string name;
        if (root.TryGetValue("LevelName", out object? value))
            name = value as string ?? throw new InvalidDataException("LevelName must be an NBT string.");
        else
        {
            string fallback = Path.Combine(worldPath, "levelname.txt");
            if (!File.Exists(fallback)) throw new InvalidDataException("Bedrock world has neither LevelName nor levelname.txt.");
            var info = new FileInfo(fallback);
            if (info.Length > 32768) throw new InvalidDataException("Bedrock levelname.txt exceeds name limit.");
            name = File.ReadAllText(fallback, StrictUtf8).TrimEnd('\r', '\n');
        }
        return (name, Integer("SpawnX"), Integer("SpawnY"), Integer("SpawnZ"));

        int Integer(string key) => root.TryGetValue(key, out object? field) && field is int number
            ? number : throw new InvalidDataException($"Bedrock level.dat is missing int {key}.");
    }

    public IEnumerable<(int X, int Y, int Z, string State)> ReadChunk(int chunkX, int chunkZ, int dimension)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (dimension is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(dimension));
        cancellation.ThrowIfCancellationRequested();
        int baseX = checked(chunkX * 16), baseZ = checked(chunkZ * 16);
        byte[] prefix = new byte[dimension == 0 ? 8 : 12];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, chunkX);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(4), chunkZ);
        if (dimension != 0) BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(8), dimension);

        // Materialize and validate each section before yielding any of its blocks.
        // The LevelDB merge includes tombstones, so old table versions cannot reappear.
        foreach ((byte[] key, byte[] value) in database.ReadPrefix(prefix))
        {
            cancellation.ThrowIfCancellationRequested();
            if (key.Length != prefix.Length + 1 && key.Length != prefix.Length + 2) continue;
            byte tag = key[prefix.Length];
            if (tag == 0x30 && key.Length == prefix.Length + 1)
                throw new NotSupportedException($"Legacy numeric Bedrock terrain at chunk {chunkX},{chunkZ} is unsupported.");
            if (tag != 0x2f) continue;
            if (key.Length != prefix.Length + 2) throw new InvalidDataException("Bedrock subchunk key has no Y index.");
            int sectionY = unchecked((sbyte)key[^1]);
            string?[] blocks;
            try { blocks = DecodeSection(value, sectionY); }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"Invalid Bedrock subchunk ({chunkX},{sectionY},{chunkZ}): {ex.Message}", ex);
            }
            for (int index = 0; index < 4096; index++)
            {
                if ((index & 255) == 0) cancellation.ThrowIfCancellationRequested();
                string? state = blocks[index];
                if (state != null)
                    yield return (checked(baseX + (index >> 8)), sectionY * 16 + (index & 15),
                        checked(baseZ + ((index >> 4) & 15)), state);
            }
        }
    }

    private string?[] DecodeSection(byte[] bytes, int keyY)
    {
        var reader = new LittleNbt(bytes, cancellation);
        int version = reader.Byte();
        if (version is not (8 or 9))
            throw new NotSupportedException($"Unsupported Bedrock subchunk version {version}; persistent v8/v9 are required.");
        int storages = reader.Byte();
        if (storages > 16) throw new InvalidDataException("Invalid Bedrock storage layer count.");
        if (version == 9 && unchecked((sbyte)reader.Byte()) != keyY)
            throw new InvalidDataException("Subchunk header Y does not match its LevelDB key.");
        var layers = new string?[storages][];
        for (int layer = 0; layer < storages; layer++)
        {
            cancellation.ThrowIfCancellationRequested();
            int header = reader.Byte(), bits = header >> 1;
            if ((header & 1) != 0)
                throw new NotSupportedException("Bedrock runtime/network palettes cannot be imported as persistent block states.");
            if (bits is not (0 or 1 or 2 or 3 or 4 or 5 or 6 or 8 or 16))
                throw new NotSupportedException($"Unsupported Bedrock palette index width {bits}.");
            int perWord = bits == 0 ? 0 : 32 / bits;
            uint[] words = new uint[bits == 0 ? 0 : (4096 + perWord - 1) / perWord];
            for (int i = 0; i < words.Length; i++) words[i] = reader.UInt32();
            int count = reader.Int32();
            if (count < 1 || count > 65536 || (bits == 0 && count != 1) || (bits > 0 && count > (1 << bits)))
                throw new InvalidDataException("Invalid Bedrock persistent palette size.");
            var palette = new string?[count];
            for (int i = 0; i < count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                Dictionary<string, object> entry = reader.Root();
                if (!entry.TryGetValue("name", out object? name) || name is not string blockName)
                    throw new InvalidDataException("Bedrock palette entry has no string name.");
                if (!entry.TryGetValue("states", out object? states) || states is not Dictionary<string, object> stateMap)
                    throw new NotSupportedException($"Bedrock block {blockName} has no persistent states compound (legacy val palettes unsupported).");
                string state = JavaState(blockName, stateMap);
                palette[i] = IsAir(state) ? null : state;
            }
            layers[layer] = new string?[4096];
            uint mask = bits == 0 ? 0 : (1U << bits) - 1;
            for (int i = 0; i < 4096; i++)
            {
                uint index = bits == 0 ? 0 : (words[i / perWord] >> ((i % perWord) * bits)) & mask;
                if (index >= count) throw new InvalidDataException("Bedrock packed index is outside its palette.");
                layers[layer][i] = palette[index];
            }
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Unexpected trailing Bedrock subchunk data.");
        var result = new string?[4096];
        for (int i = 0; i < 4096; i++)
        {
            if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
            for (int layer = 0; layer < storages; layer++)
            {
                string? next = layers[layer][i];
                if (next == null) continue;
                if (result[i] == null) { result[i] = next; continue; }
                if (IsWater(next))
                {
                    if (CanWaterlog(result[i]!))
                        result[i] = SetProperty(result[i]!, "waterlogged", "true");
                    // If result[i] is already bubble_column, seagrass, kelp, etc., or other solid block,
                    // keep result[i].
                }
                else if (IsWater(result[i]!))
                {
                    if (CanWaterlog(next))
                        result[i] = SetProperty(next, "waterlogged", "true");
                    else
                        result[i] = next;
                }
                else if (result[i] == next)
                {
                    // Identical block states across layers.
                }
                else
                {
                    // Fallback for overlapping non-water blocks: prefer primary (earlier) layer.
                }
            }
        }
        return result;
    }

    private static bool IsAir(string state) => BlockName(state) is "minecraft:air" or "minecraft:cave_air" or "minecraft:void_air";
    private static bool IsWater(string state) => BlockName(state) == "minecraft:water";
    private static string BlockName(string state) => state.Split('[', 2)[0];
    private static bool CanWaterlog(string state)
    {
        string name = BlockName(state);
        return name.EndsWith("_slab", StringComparison.Ordinal) || name.EndsWith("_stairs", StringComparison.Ordinal) ||
            name.EndsWith("_fence", StringComparison.Ordinal) || name.EndsWith("_wall", StringComparison.Ordinal) ||
            name.EndsWith("_trapdoor", StringComparison.Ordinal) || name.EndsWith("_leaves", StringComparison.Ordinal) ||
            name.EndsWith("_sign", StringComparison.Ordinal) || name.EndsWith("_hanging_sign", StringComparison.Ordinal) ||
            name.EndsWith("_coral", StringComparison.Ordinal) || name.EndsWith("_coral_fan", StringComparison.Ordinal) ||
            name.EndsWith("_coral_wall_fan", StringComparison.Ordinal) ||
            name.EndsWith("_candle", StringComparison.Ordinal) || name.EndsWith("_candle_cake", StringComparison.Ordinal) ||
            name.EndsWith("_cluster", StringComparison.Ordinal) || name.EndsWith("_bud", StringComparison.Ordinal) ||
            name is "minecraft:iron_bars" or "minecraft:glass_pane" or "minecraft:ladder" or "minecraft:chest" or
            "minecraft:trapped_chest" or "minecraft:ender_chest" or "minecraft:chain" or "minecraft:lantern" or
            "minecraft:soul_lantern" or "minecraft:scaffolding" or "minecraft:sea_pickle" or "minecraft:campfire" or
            "minecraft:soul_campfire" or "minecraft:lightning_rod" or "minecraft:pointed_dripstone" or
            "minecraft:small_dripleaf" or "minecraft:big_dripleaf" or "minecraft:big_dripleaf_stem" or
            "minecraft:conduit" or "minecraft:end_rod" or "minecraft:glow_lichen" or "minecraft:sculk_sensor" or
            "minecraft:sculk_shrieker" or "minecraft:sculk_vein" or "minecraft:mangrove_roots" ||
            name.EndsWith("_stained_glass_pane", StringComparison.Ordinal);
    }

    private static string SetProperty(string state, string property, string value)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        int bracket = state.IndexOf('[');
        string name = bracket < 0 ? state : state[..bracket];
        if (bracket >= 0)
            foreach (string part in state[(bracket + 1)..^1].Split(','))
            {
                string[] pair = part.Split('=', 2);
                properties.Add(pair[0], pair[1]);
            }
        properties[property] = value;
        return FormatState(name, properties);
    }

    private static readonly Dictionary<string, string> Renamed = new(StringComparer.Ordinal)
    {
        ["grass"] = "grass_block", ["grass_path"] = "dirt_path", ["web"] = "cobweb",
        ["reeds"] = "sugar_cane", ["waterlily"] = "lily_pad", ["deadbush"] = "dead_bush",
        ["melon_block"] = "melon", ["lit_pumpkin"] = "jack_o_lantern", ["end_bricks"] = "end_stone_bricks",
        ["nether_brick"] = "nether_bricks", ["red_nether_brick"] = "red_nether_bricks",
        ["magma"] = "magma_block", ["quartz_ore"] = "nether_quartz_ore", ["slime"] = "slime_block",
        ["hardened_clay"] = "terracotta", ["stained_hardened_clay"] = "terracotta",
        ["stonebrick"] = "stone_bricks", ["brick_block"] = "bricks",
        ["golden_rail"] = "powered_rail", ["noteblock"] = "note_block",
        ["yellow_flower"] = "dandelion", ["lit_furnace"] = "furnace", ["lit_redstone_ore"] = "redstone_ore",
        ["unlit_redstone_torch"] = "redstone_torch", ["lit_redstone_lamp"] = "redstone_lamp",
        ["unpowered_repeater"] = "repeater", ["powered_repeater"] = "repeater",
        ["unpowered_comparator"] = "comparator", ["powered_comparator"] = "comparator",
        ["wooden_door"] = "oak_door", ["trapdoor"] = "oak_trapdoor", ["fence_gate"] = "oak_fence_gate",
        ["wooden_button"] = "oak_button", ["wooden_pressure_plate"] = "oak_pressure_plate",
        ["standing_sign"] = "oak_sign", ["wall_sign"] = "oak_wall_sign",
        ["standing_banner"] = "white_banner", ["wall_banner"] = "white_wall_banner",
        ["skull"] = "skeleton_skull", ["piston_arm_collision"] = "piston_head",
        ["sticky_piston_arm_collision"] = "piston_head",
        ["pistonArmCollision"] = "piston_head", ["stickyPistonArmCollision"] = "piston_head",
        ["stone_stairs"] = "cobblestone_stairs", ["normal_stone_stairs"] = "stone_stairs",
        ["flowing_water"] = "water", ["flowing_lava"] = "lava", ["snow_layer"] = "snow",
        ["snow"] = "snow_block", ["portal"] = "nether_portal", ["invisible_bedrock"] = "barrier",
        ["dirt_with_roots"] = "rooted_dirt", ["azalea_leaves_flowered"] = "flowering_azalea_leaves",
        ["lit_deepslate_redstone_ore"] = "deepslate_redstone_ore",
        ["undyed_shulker_box"] = "shulker_box", ["concretepowder"] = "concrete_powder",
        ["darkoak_standing_sign"] = "dark_oak_sign", ["darkoak_wall_sign"] = "dark_oak_wall_sign",
        ["dark_oak_standing_sign"] = "dark_oak_sign", ["dark_oak_wall_sign"] = "dark_oak_wall_sign"
    };
    private static readonly string[] Wood = ["oak", "spruce", "birch", "jungle", "acacia", "dark_oak"];
    private static readonly string[] Colors =
        ["white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray", "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"];

    // Bedrock's block registry is not Java's registry. Translate known aliases and
    // properties, preserving unknown identifiers/properties instead of inventing air.
    // Neighbor-dependent Java states (e.g. stair shape) cannot be reconstructed here.
    private static string JavaState(string bedrockName, Dictionary<string, object> states)
    {
        string fullName = bedrockName.Contains(':') ? bedrockName : "minecraft:" + bedrockName;
        ValidateIdentifier(fullName, true);
        var source = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in states)
        {
            if (pair.Value is not (sbyte or short or int or long or string))
                throw new NotSupportedException($"Unsupported Bedrock block property type: {fullName}.{pair.Key}.");
            string value = Convert.ToString(pair.Value, CultureInfo.InvariantCulture)!;
            string key = pair.Key.StartsWith("minecraft:", StringComparison.Ordinal) ? pair.Key[10..] : pair.Key;
            ValidateIdentifier(key, false);
            ValidateIdentifier(value, false);
            if (!source.TryAdd(key, value)) throw new InvalidDataException("Duplicate normalized Bedrock state property.");
        }
        if (!fullName.StartsWith("minecraft:", StringComparison.Ordinal))
            return FormatState(fullName, new SortedDictionary<string, string>(source, StringComparer.Ordinal));

        string original = fullName[10..], name = Renamed.GetValueOrDefault(original, original);
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string? Take(string key) => source.Remove(key, out string? value) ? value : null;
        string Variant(string key, params string[] accepted)
        {
            string? value = Take(key);
            if (value == null) throw new NotSupportedException($"Bedrock {original} requires property {key}.");
            if (!accepted.Contains(value, StringComparer.Ordinal))
                throw new NotSupportedException($"Unsupported Bedrock {original} {key}={value}.");
            return value;
        }
        if (original is "log" or "log2" or "leaves" or "leaves2")
        {
            bool second = original.EndsWith('2');
            bool leaves = original.StartsWith("leaves", StringComparison.Ordinal);
            string wood = Variant((second ? "new_" : "old_") + (leaves ? "leaf_type" : "log_type"), second ? ["acacia", "dark_oak"] : ["oak", "spruce", "birch", "jungle"]);
            name = wood + (original.StartsWith("leaves", StringComparison.Ordinal) ? "_leaves" : "_log");
        }
        else if (original is "planks" or "wooden_slab" or "double_wooden_slab" or "fence")
        {
            string wood = Variant("wood_type", Wood);
            name = wood + (original == "fence" ? "_fence" : original == "planks" ? "_planks" : "_slab");
            if (original == "double_wooden_slab") properties["type"] = "double";
        }
        else if (original == "wood")
        {
            string wood = Variant("wood_type", Wood);
            string stripped = Take("stripped_bit") ?? "0";
            name = (Boolean(stripped) == "true" ? "stripped_" : "") + wood + "_wood";
        }
        else if (original == "stone" && source.ContainsKey("stone_type"))
            name = Variant("stone_type", "stone", "granite", "granite_smooth", "diorite", "diorite_smooth", "andesite", "andesite_smooth") switch
            {
                "granite_smooth" => "polished_granite", "diorite_smooth" => "polished_diorite",
                "andesite_smooth" => "polished_andesite", var type => type
            };
        else if (original == "dirt" && source.ContainsKey("dirt_type"))
            name = Variant("dirt_type", "normal", "coarse") == "coarse" ? "coarse_dirt" : "dirt";
        else if (original == "sand" && source.ContainsKey("sand_type"))
            name = Variant("sand_type", "normal", "red") == "red" ? "red_sand" : "sand";
        else if (original == "stonebrick" && source.ContainsKey("stone_brick_type"))
            name = Variant("stone_brick_type", "default", "mossy", "cracked", "chiseled", "smooth") switch
            {
                "mossy" => "mossy_stone_bricks", "cracked" => "cracked_stone_bricks",
                "chiseled" => "chiseled_stone_bricks", _ => "stone_bricks"
            };
        else if (original is "sandstone" or "red_sandstone" && source.ContainsKey("sand_stone_type"))
            name = Variant("sand_stone_type", "default", "heiroglyphs", "cut", "smooth") switch
            {
                "heiroglyphs" => "chiseled_" + original, "cut" => "cut_" + original,
                "smooth" => "smooth_" + original, _ => original
            };
        else if (original is "wool" or "carpet" or "concrete" or "concretepowder" or "stained_glass" or
            "stained_glass_pane" or "stained_hardened_clay" or "shulker_box")
        {
            string color = Variant("color", Colors.Concat(["silver"]).ToArray());
            if (color == "silver") color = "light_gray";
            name = color + "_" + (original == "stained_hardened_clay" ? "terracotta" : name);
        }
        else if (original == "tallgrass")
            name = Variant("tall_grass_type", "default", "tall", "fern", "snow") switch
            {
                "fern" => "fern", "snow" => throw new NotSupportedException("Unsupported Bedrock snow tallgrass variant."),
                _ => "short_grass"
            };
        else if (original == "red_flower")
            name = Variant("flower_type", "poppy", "orchid", "allium", "houstonia", "tulip_red", "tulip_orange", "tulip_white", "tulip_pink", "oxeye", "cornflower", "lily_of_the_valley") switch
            {
                "orchid" => "blue_orchid", "houstonia" => "azure_bluet", "oxeye" => "oxeye_daisy",
                "tulip_red" => "red_tulip", "tulip_orange" => "orange_tulip",
                "tulip_white" => "white_tulip", "tulip_pink" => "pink_tulip", var flower => flower
            };
        else if (original == "double_plant")
            name = Variant("double_plant_type", "sunflower", "syringa", "grass", "fern", "rose", "paeonia") switch
            {
                "syringa" => "lilac", "grass" => "tall_grass", "fern" => "large_fern",
                "rose" => "rose_bush", "paeonia" => "peony", var plant => plant
            };
        else if (original == "sapling")
            name = Variant("sapling_type", Wood) + "_sapling";
        else if (original is "stone_slab" or "double_stone_slab" or "stone_block_slab" or "double_stone_block_slab")
        {
            name = Variant("stone_slab_type", "smooth_stone", "sandstone", "wood", "cobblestone", "brick", "stone_brick", "quartz", "nether_brick") switch
            {
                "wood" => "petrified_oak_slab", "brick" => "brick_slab", "stone_brick" => "stone_brick_slab",
                var slab => slab + "_slab"
            };
        }
        else if (original is "stone_block_slab2" or "double_stone_block_slab2")
            name = Variant("stone_slab_type_2", "red_sandstone", "purpur", "prismarine_rough", "prismarine_dark", "prismarine_brick", "mossy_cobblestone", "smooth_sandstone", "red_nether_brick") switch
            {
                "prismarine_rough" => "prismarine_slab", "prismarine_dark" => "dark_prismarine_slab",
                "prismarine_brick" => "prismarine_brick_slab", var slab => slab + "_slab"
            };
        else if (original is "stone_block_slab3" or "double_stone_block_slab3")
            name = Variant("stone_slab_type_3", "end_stone_brick", "smooth_red_sandstone", "polished_andesite", "andesite", "diorite", "polished_diorite", "granite", "polished_granite") + "_slab";
        else if (original is "stone_block_slab4" or "double_stone_block_slab4")
            name = Variant("stone_slab_type_4", "mossy_stone_brick", "smooth_quartz", "stone", "cut_sandstone", "cut_red_sandstone") + "_slab";
        else if (original == "quartz_block" && source.ContainsKey("chisel_type"))
            name = Variant("chisel_type", "default", "chiseled", "lines", "smooth") switch
            {
                "chiseled" => "chiseled_quartz_block", "lines" => "quartz_pillar",
                "smooth" => "smooth_quartz", _ => "quartz_block"
            };
        else if (original == "prismarine" && source.ContainsKey("prismarine_block_type"))
            name = Variant("prismarine_block_type", "default", "dark", "bricks") switch
            {
                "dark" => "dark_prismarine", "bricks" => "prismarine_bricks", _ => "prismarine"
            };
        else if (original == "sponge" && source.ContainsKey("sponge_type"))
            name = Variant("sponge_type", "dry", "wet") == "wet" ? "wet_sponge" : "sponge";
        else if (original == "cobblestone_wall" && source.ContainsKey("wall_block_type"))
            name = Variant("wall_block_type", "cobblestone", "mossy_cobblestone", "granite", "diorite", "andesite", "sandstone",
                "brick", "stone_brick", "mossy_stone_brick", "nether_brick", "end_brick", "prismarine", "red_sandstone", "red_nether_brick") switch
            {
                "end_brick" => "end_stone_brick_wall", var wall => wall + "_wall"
            };

        if (original.EndsWith("_standing_sign", StringComparison.Ordinal)) name = original[..^14] + "_sign";
        if (name.EndsWith("_hanging_sign", StringComparison.Ordinal) &&
            source.TryGetValue("hanging", out string? hanging) && Boolean(hanging) == "false")
            name = name[..^13] + "_wall_hanging_sign";
        if (original.EndsWith("_double_slab", StringComparison.Ordinal))
        {
            name = original[..^12] + "_slab";
            properties["type"] = "double";
        }
        else if (original.StartsWith("double_", StringComparison.Ordinal) && original.EndsWith("_slab", StringComparison.Ordinal) &&
            !original.Contains("stone_block", StringComparison.Ordinal) && original != "double_stone_slab" && original != "double_wooden_slab")
        {
            name = original[7..];
            properties["type"] = "double";
        }
        if (original.StartsWith("double_", StringComparison.Ordinal) && name.EndsWith("_slab", StringComparison.Ordinal))
            properties["type"] = "double";
        if (original is "lit_furnace" or "lit_redstone_ore" or "lit_redstone_lamp" or "lit_deepslate_redstone_ore")
            properties["lit"] = "true";
        if (original == "unlit_redstone_torch") properties["lit"] = "false";
        if (original.StartsWith("powered_", StringComparison.Ordinal) && (name is "repeater" or "comparator"))
            properties["powered"] = "true";
        if (original.StartsWith("unpowered_", StringComparison.Ordinal) && (name is "repeater" or "comparator"))
            properties["powered"] = "false";

        bool stairs = name.EndsWith("_stairs", StringComparison.Ordinal);
        bool door = name.EndsWith("_door", StringComparison.Ordinal);
        bool trapdoor = name.EndsWith("_trapdoor", StringComparison.Ordinal);
        foreach (var pair in source)
        {
            string key = pair.Key, value = pair.Value;
            switch (key)
            {
                case "pillar_axis": properties["axis"] = value; break;
                case "cardinal_direction": case "block_face": properties["facing"] = value; break;
                case "facing_direction":
                    properties["facing"] = Direction(value, ["down", "up", "north", "south", "west", "east"]); break;
                case "direction":
                    properties["facing"] = Direction(value, door ? ["east", "south", "west", "north"] : trapdoor ? ["east", "west", "south", "north"] : ["south", "west", "north", "east"]); break;
                case "weirdo_direction":
                    properties["facing"] = Direction(value, ["east", "west", "south", "north"]); break;
                case "upside_down_bit": properties["half"] = Boolean(value) == "true" ? "top" : "bottom"; break;
                case "top_slot_bit":
                    if (!properties.ContainsKey("type")) properties["type"] = Boolean(value) == "true" ? "top" : "bottom";
                    break;
                case "vertical_half":
                    if (value is not ("top" or "bottom")) throw new NotSupportedException($"Unknown vertical_half={value}.");
                    if (properties.GetValueOrDefault("type") != "double")
                        properties[name.EndsWith("_slab", StringComparison.Ordinal) ? "type" : "half"] = value;
                    break;
                case "upper_block_bit": properties["half"] = Boolean(value) == "true" ? "upper" : "lower"; break;
                case "door_hinge_bit": properties["hinge"] = Boolean(value) == "true" ? "right" : "left"; break;
                case "open_bit": properties[name == "lever" ? "powered" : "open"] = Boolean(value); break;
                case "in_wall_bit": properties["in_wall"] = Boolean(value); break;
                case "powered_bit": properties["powered"] = Boolean(value); break;
                case "button_pressed_bit": properties["powered"] = Boolean(value); break;
                case "extended_bit": properties["extended"] = Boolean(value); break;
                case "attached_bit": properties["attached"] = Boolean(value); break;
                case "lever_direction":
                    properties["face"] = value.StartsWith("up_", StringComparison.Ordinal) ? "floor" :
                        value.StartsWith("down_", StringComparison.Ordinal) ? "ceiling" : "wall";
                    properties["facing"] = value switch
                    {
                        "up_east_west" or "down_east_west" => "east",
                        "up_north_south" or "down_north_south" => "north",
                        "north" or "south" or "east" or "west" => value,
                        _ => throw new NotSupportedException($"Unsupported lever_direction={value}.")
                    };
                    break;
                case "attachment" when name == "bell":
                    properties["attachment"] = value switch
                    {
                        "standing" => "floor", "hanging" => "ceiling", "side" => "single_wall",
                        "multiple" => "double_wall", _ => value
                    };
                    break;
                case "persistent_bit": properties["persistent"] = Boolean(value); break;
                case "update_bit":
                    if (!name.EndsWith("_leaves", StringComparison.Ordinal)) properties[key] = value;
                    break; // Java leaf distance is neighbor-derived, not Bedrock's decay-update flag.
                case "liquid_depth": properties["level"] = value; break;
                case "ground_sign_direction": properties["rotation"] = value; break;
                case "redstone_signal": properties["power"] = value; break;
                case "growth": properties["age"] = value; break;
                case "age_bit": properties["stage"] = Boolean(value) == "true" ? "1" : "0"; break;
                case "moisturized_amount": properties["moisture"] = value; break;
                case "repeater_delay": properties["delay"] = (ParseInt(value) + 1).ToString(CultureInfo.InvariantCulture); break;
                case "output_subtract_bit": properties["mode"] = Boolean(value) == "true" ? "subtract" : "compare"; break;
                case "rail_direction":
                    properties["shape"] = Direction(value, ["north_south", "east_west", "ascending_east", "ascending_west", "ascending_north", "ascending_south", "south_east", "south_west", "north_west", "north_east"]); break;
                case "rail_data_bit": properties["powered"] = Boolean(value); break;
                case "wall_post_bit": properties["up"] = Boolean(value); break;
                case "wall_connection_type_north": case "wall_connection_type_south":
                case "wall_connection_type_east": case "wall_connection_type_west":
                    properties[key[21..]] = value switch
                    {
                        "none" => "none", "short" => "low", "tall" => "tall",
                        _ => throw new NotSupportedException($"Unknown Bedrock wall connection {value}.")
                    };
                    break;
                case "snow_layer": case "height":
                    if (name == "snow") properties["layers"] = (ParseInt(value) + 1).ToString(CultureInfo.InvariantCulture);
                    else properties[key] = value;
                    break;
                case "covered_bit":
                    if (name != "snow") properties[key] = value;
                    break;
                case "torch_facing_direction":
                    if (value is "unknown" or "top") { }
                    else if (value is "north" or "south" or "west" or "east")
                    {
                        name = name == "redstone_torch" ? "redstone_wall_torch" : name == "soul_torch" ? "soul_wall_torch" : "wall_torch";
                        properties["facing"] = value switch { "north" => "south", "south" => "north", "west" => "east", _ => "west" };
                    }
                    else throw new NotSupportedException($"Unsupported torch_facing_direction={value}.");
                    break;
                case "head_piece_bit": properties["part"] = Boolean(value) == "true" ? "head" : "foot"; break;
                case "occupied_bit": properties["occupied"] = Boolean(value); break;
                case "stripped_bit":
                    if (Boolean(value) == "true" && !name.StartsWith("stripped_", StringComparison.Ordinal)) name = "stripped_" + name;
                    break;
                default:
                    // Unknown state names are intentionally retained. Consumers can show
                    // unresolved blocks and diagnostics instead of silently deleting them.
                    properties[key] = value;
                    break;
            }
        }
        if (stairs)
        {
            properties.TryAdd("shape", "straight");
            properties.TryAdd("half", "bottom");
        }
        if (door) properties.TryAdd("hinge", "left");
        if (trapdoor) properties.TryAdd("half", "bottom");
        string qualified = "minecraft:" + name;
        if (CanWaterlog(qualified)) properties.TryAdd("waterlogged", "false");
        return FormatState(qualified, properties);
    }

    private static int ParseInt(string text) => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
        ? value : throw new InvalidDataException($"Invalid numeric Bedrock block property '{text}'.");
    private static string Boolean(string value) => value switch
    {
        "0" or "false" => "false", "1" or "true" => "true",
        _ => throw new InvalidDataException($"Invalid boolean Bedrock block property '{value}'.")
    };
    private static string Direction(string value, string[] names)
    {
        int index = ParseInt(value);
        return index >= 0 && index < names.Length ? names[index] :
            throw new NotSupportedException($"Unsupported Bedrock direction value {value}.");
    }
    private static string FormatState(string name, SortedDictionary<string, string> properties) =>
        properties.Count == 0 ? name : name + "[" + string.Join(",", properties.Select(p => p.Key + "=" + p.Value)) + "]";
    private static void ValidateIdentifier(string text, bool qualified)
    {
        if (text.Length == 0 || text.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.' or '/' || (qualified && c == ':'))) ||
            (qualified && (text.Count(c => c == ':') != 1 || text.StartsWith(':') || text.EndsWith(':'))))
            throw new NotSupportedException($"Bedrock identifier cannot be represented as a Java state: '{text}'.");
    }

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Little-endian, fixed-width disk NBT (not network/varint NBT).</summary>
    private sealed class LittleNbt
    {
        private readonly BedrockLevelDb.Cursor reader;
        private readonly CancellationToken cancellation;
        private int nodes;
        public LittleNbt(byte[] bytes, CancellationToken cancellation)
        {
            reader = new BedrockLevelDb.Cursor(bytes);
            this.cancellation = cancellation;
        }
        public int Remaining => reader.Remaining;
        public byte Byte() => reader.Byte();
        public uint UInt32() => reader.UInt32();
        public int Int32() => unchecked((int)reader.UInt32());
        private string String() => StrictUtf8.GetString(reader.Bytes(reader.UInt16()));
        public Dictionary<string, object> Root()
        {
            if (reader.Byte() != 10) throw new InvalidDataException("Bedrock NBT root is not a compound.");
            String();
            return (Dictionary<string, object>)Payload(10, 0);
        }
        private int Count(int elementSize)
        {
            int count = Int32();
            if (count < 0 || count > 1_000_000 || (long)count * elementSize > reader.Remaining)
                throw new InvalidDataException("Invalid Bedrock NBT array/list length.");
            return count;
        }
        private object Payload(byte type, int depth)
        {
            cancellation.ThrowIfCancellationRequested();
            if (depth > 64 || ++nodes > 1_000_000) throw new InvalidDataException("Bedrock NBT exceeds nesting/node safety limit.");
            switch (type)
            {
                case 1: return unchecked((sbyte)reader.Byte());
                case 2: return unchecked((short)reader.UInt16());
                case 3: return Int32();
                case 4: return unchecked((long)reader.UInt64());
                case 5: return BitConverter.Int32BitsToSingle(Int32());
                case 6: return BitConverter.Int64BitsToDouble(unchecked((long)reader.UInt64()));
                case 7: return reader.Bytes(Count(1));
                case 8: return String();
                case 9:
                {
                    byte itemType = reader.Byte();
                    int count = Count(0);
                    if (itemType > 12 || (itemType == 0 && count != 0))
                        throw new InvalidDataException("Invalid Bedrock NBT list type.");
                    var list = new List<object>(count);
                    for (int i = 0; i < count; i++) list.Add(Payload(itemType, depth + 1));
                    return list;
                }
                case 10:
                {
                    var compound = new Dictionary<string, object>(StringComparer.Ordinal);
                    while (true)
                    {
                        byte childType = reader.Byte();
                        if (childType == 0) break;
                        string name = String();
                        if (!compound.TryAdd(name, Payload(childType, depth + 1)))
                            throw new InvalidDataException($"Duplicate Bedrock NBT tag '{name}'.");
                    }
                    return compound;
                }
                case 11:
                {
                    int count = Count(4);
                    var array = new int[count];
                    for (int i = 0; i < count; i++) { cancellation.ThrowIfCancellationRequested(); array[i] = Int32(); }
                    return array;
                }
                case 12:
                {
                    int count = Count(8);
                    var array = new long[count];
                    for (int i = 0; i < count; i++) { cancellation.ThrowIfCancellationRequested(); array[i] = unchecked((long)reader.UInt64()); }
                    return array;
                }
                default: throw new NotSupportedException($"Unsupported Bedrock NBT tag type {type}.");
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        database.Dispose();
    }
}
