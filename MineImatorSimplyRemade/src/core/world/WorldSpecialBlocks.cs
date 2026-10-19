using GlmSharp;
using MineImatorSimplyRemade.core.ui.Panels;

namespace MineImatorSimplyRemade.core.world;

internal sealed record WorldSpecialBlock(string Name, Dictionary<string, string> Properties,
    string? Model, string? Texture, int[] Indices, vec3 Position, vec3 Rotation, vec3 Pivot,
    bool KeepScenery = false)
{
    public string Property(string name, string fallback = "") => Properties.GetValueOrDefault(name, fallback);
}

internal sealed record WorldSpecialBlockPlan(List<WorldSpecialBlock> Blocks, int IncompleteBlocks)
{
    public WorldVolume Terrain(WorldVolume source) =>
        source.WithoutBlocks(Blocks.Where(b => !b.KeepScenery).SelectMany(b => b.Indices));
}

internal static class WorldSpecialBlocks
{
    private sealed record State(string Name, Dictionary<string, string> Properties, bool Legacy)
    {
        public string Get(string key, string fallback = "") => Properties.GetValueOrDefault(key, fallback);
    }

    public static bool IsSpecial(string name) =>
        name is "piston" or "sticky_piston" or "chest" or "trapped_chest" or "ender_chest" or
            "lever" or "enchanting_table" or "tripwire_hook" or "conduit" or "bell" or "decorated_pot" ||
        name.EndsWith("_chest", StringComparison.Ordinal) && name.Contains("copper", StringComparison.Ordinal) ||
        name.EndsWith("_door", StringComparison.Ordinal) || name.EndsWith("_trapdoor", StringComparison.Ordinal) ||
        name.EndsWith("_pressure_plate", StringComparison.Ordinal) || name.EndsWith("_button", StringComparison.Ordinal) ||
        name.EndsWith("_fence_gate", StringComparison.Ordinal) || name.EndsWith("_sign", StringComparison.Ordinal) ||
        name.EndsWith("_banner", StringComparison.Ordinal) || name is "shulker_box" ||
        name.EndsWith("_shulker_box", StringComparison.Ordinal) ||
        name != "piston_head" && (name.EndsWith("_head", StringComparison.Ordinal) || name.EndsWith("_skull", StringComparison.Ordinal)) ||
        name.EndsWith("copper_golem_statue", StringComparison.Ordinal);

    public static WorldSpecialBlockPlan Detect(WorldVolume volume)
    {
        var palette = volume.Palette.Select(Parse).ToArray();
        var result = new List<WorldSpecialBlock>();
        var used = new HashSet<int>();
        int incomplete = 0;
        int width = volume.Bounds.Width, length = volume.Bounds.Length, height = volume.Bounds.Height;
        State? At(int index) => index >= 0 && index < volume.Blocks.Length ? palette[volume.Blocks[index]] : null;
        int Index(int x, int y, int z) =>
            x >= 0 && x < width && y >= 0 && y < height && z >= 0 && z < length
                ? (y * length + z) * width + x : -1;

        for (int index = 0; index < volume.Blocks.Length; index++)
        {
            var state = At(index);
            if (used.Contains(index) || state == null || !IsSpecial(state.Name)) continue;
            int x = index % width, z = index / width % length, y = index / (width * length);
            string name = state.Name;
            var properties = new Dictionary<string, string>(state.Properties, StringComparer.OrdinalIgnoreCase);
            var indices = new List<int> { index };
            vec3 position = new(x - (width - 1) * 0.5f, y, z - (length - 1) * 0.5f);
            vec3 rotation = new(0, FacingAngle(state.Get("facing", "south")), 0);
            vec3 pivot = vec3.Zero;
            string? model = null, texture = null;
            bool keepScenery = false;

            if (name.EndsWith("_door", StringComparison.Ordinal))
            {
                bool upper = state.Get("half") == "upper";
                int partner = Index(x, y + (upper ? -1 : 1), z);
                var other = At(partner);
                if (other?.Name != name || other.Get("half") != (upper ? "lower" : "upper"))
                {
                    incomplete++;
                    continue;
                }
                if (upper) continue;
                if (state.Legacy)
                    properties["hinge"] = other.Get("hinge", "left");
                indices.Add(partner);
                pivot = DoorPivot(properties);
                rotation = vec3.Zero;
            }
            else if (IsChest(name))
            {
                bool doubleChest = state.Get("type", "single") != "single";
                bool inferPair = state.Legacy || !state.Properties.ContainsKey("type");
                var candidates = new List<int>();
                var direction = Direction(state.Get("facing", "south"));
                if (name != "ender_chest" && (doubleChest || inferPair))
                {
                    foreach (int sign in new[] { -1, 1 })
                    {
                        int neighbor = Index(x + sign * direction.Z, y, z - sign * direction.X);
                        var other = At(neighbor);
                        if (other?.Name == name && other.Get("facing") == state.Get("facing") &&
                            (inferPair && (other.Legacy || !other.Properties.ContainsKey("type")) ||
                             other.Get("type", "single") != "single" && other.Get("type") != state.Get("type")))
                            candidates.Add(neighbor);
                    }
                    if (doubleChest && candidates.Count != 1 || candidates.Count > 1)
                    {
                        incomplete++;
                        continue;
                    }
                }
                if (candidates.Count == 1)
                {
                    int partner = candidates[0];
                    if (used.Contains(partner)) continue;
                    indices.Add(partner);
                    position.x += (partner % width - x) * 0.5f;
                    position.z += (partner / width % length - z) * 0.5f;
                }
                model = indices.Count == 2 ? "chest_double" : "chest";
                string variant = name switch
                {
                    "trapped_chest" => "trapped",
                    "ender_chest" => "ender",
                    _ when name.Contains("copper", StringComparison.Ordinal) =>
                        "copper" + OxidationSuffix(name),
                    _ => "normal"
                };
                texture = $"textures\\entity\\chest\\{variant}.png";
            }
            else if (name is "piston" or "sticky_piston")
            {
                var direction = Direction(state.Get("facing", "up"));
                int head = Index(x + direction.X, y + direction.Y, z + direction.Z);
                bool extended = state.Get("extended") == "true" ||
                    !state.Properties.ContainsKey("extended") && At(head)?.Name == "piston_head";
                properties["extended"] = extended ? "true" : "false";
                if (extended)
                {
                    int partner = head;
                    var other = At(partner);
                    if (other?.Name != "piston_head" || other.Get("facing") != state.Get("facing"))
                    {
                        incomplete++;
                        continue;
                    }
                    indices.Add(partner);
                }
                model = "piston";
                position.y += 0.5f;
                rotation = DirectionRotation(state.Get("facing", "up"));
            }
            else if (name.EndsWith("_fence_gate", StringComparison.Ordinal))
            {
                model = "fence_gate";
                texture = $"textures\\block\\{name[..^11]}_planks.png";
                if (state.Get("in_wall") == "true") position.y -= 3f / 16;
            }
            else if (name.EndsWith("_sign", StringComparison.Ordinal))
            {
                bool wall = name.Contains("_wall_", StringComparison.Ordinal);
                bool hanging = name.EndsWith("_hanging_sign", StringComparison.Ordinal);
                model = hanging ? wall ? "wall_hanging_sign" : "hanging_sign" : wall ? "wall_sign" : "standing_sign";
                string wood = name[..name.IndexOf(wall ? "_wall_" : hanging ? "_hanging_sign" : "_sign", StringComparison.Ordinal)];
                texture = $"textures\\block\\{wood}_{(hanging ? "hanging_sign" : "sign")}.png";
                if (hanging) position.y += 1;
                else if (wall) position += WallOffset(state.Get("facing"));
                if (!wall) rotation.y = -Number(state.Get("rotation")) * 22.5f;
            }
            else if (name.EndsWith("_banner", StringComparison.Ordinal))
            {
                bool wall = name.EndsWith("_wall_banner", StringComparison.Ordinal);
                model = wall ? "wall_banner" : "standing_banner";
                if (!wall) rotation.y = -Number(state.Get("rotation")) * 22.5f;
            }
            else if (name == "shulker_box" || name.EndsWith("_shulker_box", StringComparison.Ordinal))
            {
                model = "shulker_box";
                string color = name == "shulker_box" ? "" : "_" + name[..^12];
                texture = $"textures\\entity\\shulker\\shulker{color}.png";
                string facing = state.Get("facing", "up");
                rotation = UprightRotation(facing);
                position += facing switch
                {
                    "down" => vec3.UnitY,
                    "north" or "south" or "east" or "west" => WallOffset(facing) + vec3.UnitY * 0.5f,
                    _ => vec3.Zero
                };
            }
            else if (name.EndsWith("_head", StringComparison.Ordinal) || name.EndsWith("_skull", StringComparison.Ordinal))
            {
                model = name.StartsWith("dragon", StringComparison.Ordinal) ? "dragon_head" :
                    name.StartsWith("piglin", StringComparison.Ordinal) ? "piglin_head" : "mob_head";
                if (model == "mob_head")
                    texture = name.StartsWith("player", StringComparison.Ordinal) ? "textures\\entity\\steve.png" :
                        name.StartsWith("wither_skeleton", StringComparison.Ordinal) ? "textures\\entity\\wither_skeleton.png" :
                        $"textures\\entity\\{name.Split('_')[0]}.png";
                if (name.Contains("_wall_", StringComparison.Ordinal))
                {
                    var direction = Direction(state.Get("facing"));
                    position += new vec3(-direction.X * 0.25f, 0.25f, -direction.Z * 0.25f);
                }
                else rotation.y = 180 - Number(state.Get("rotation")) * 22.5f;
            }
            else if (name == "lever")
            {
                model = "lever";
                string face = state.Get("face", "floor"), facing = state.Get("facing", "north");
                if (face == "wall") position += WallOffset(facing) + vec3.UnitY * 0.5f;
                else if (face == "ceiling") position.y += 1;
                rotation = face == "wall" ? UprightRotation(facing) :
                    new vec3(face == "ceiling" ? 180 : 0, FacingAngle(facing), 0);
            }
            else if (name == "tripwire_hook")
            {
                model = "tripwire_hook";
                position += WallOffset(state.Get("facing")) * 0.875f;
            }
            else if (name == "enchanting_table")
            {
                model = "book";
                keepScenery = true;
                position.y += 1.25f;
                rotation = new vec3(15, 0, 0);
            }
            else if (name == "bell")
            {
                model = "bell";
                keepScenery = true;
                position.y += 13f / 16;
                rotation.y = -FacingAngle(state.Get("facing", "north")) + 180;
            }
            else if (name is "conduit" or "decorated_pot") model = name;
            else if (name.EndsWith("copper_golem_statue", StringComparison.Ordinal))
            {
                model = "copper_golem";
                texture = $"textures\\entity\\copper_golem\\copper_golem{OxidationSuffix(name)}.png";
            }
            else
            {
                rotation = vec3.Zero;
                pivot = name.EndsWith("_trapdoor", StringComparison.Ordinal)
                    ? TrapdoorPivot(properties) : new vec3(0, 0.0625f, 0);
            }
            used.UnionWith(indices);
            result.Add(new WorldSpecialBlock(name, properties, model, texture, indices.ToArray(),
                position, rotation, pivot, keepScenery));
        }
        return new WorldSpecialBlockPlan(result, incomplete);
    }

    private static bool IsChest(string name) =>
        name is "chest" or "trapped_chest" or "ender_chest" || name.EndsWith("_chest", StringComparison.Ordinal) && name.Contains("copper", StringComparison.Ordinal);

    private static string OxidationSuffix(string name) =>
        name.Contains("oxidized", StringComparison.Ordinal) ? "_oxidized" :
        name.Contains("weathered", StringComparison.Ordinal) ? "_weathered" :
        name.Contains("exposed", StringComparison.Ordinal) ? "_exposed" : "";

    private static float Number(string value) => int.TryParse(value, out int result) ? result : 0;
    private static float FacingAngle(string facing) => facing switch { "north" => 180, "east" => 90, "west" => -90, _ => 0 };
    private static vec3 WallOffset(string facing) => facing switch
    {
        "north" => new vec3(0, 0, 0.5f), "south" => new vec3(0, 0, -0.5f),
        "east" => new vec3(-0.5f, 0, 0), "west" => new vec3(0.5f, 0, 0), _ => vec3.Zero
    };
    private static (int X, int Y, int Z) Direction(string facing) => facing switch
    {
        "north" => (0, 0, -1), "south" => (0, 0, 1), "east" => (1, 0, 0),
        "west" => (-1, 0, 0), "down" => (0, -1, 0), "up" => (0, 1, 0), _ => (0, 0, 0)
    };
    private static vec3 DirectionRotation(string facing) =>
        new(facing == "up" ? -90 : facing == "down" ? 90 : 0, FacingAngle(facing), 0);
    private static vec3 UprightRotation(string facing) => facing switch
    {
        "north" => new(-90, 0, 0), "south" => new(90, 0, 0),
        "east" => new(0, 0, -90), "west" => new(0, 0, 90),
        "down" => new(180, 0, 0), _ => vec3.Zero
    };
    private static vec3 DoorPivot(Dictionary<string, string> properties)
    {
        bool right = properties.GetValueOrDefault("hinge") == "right";
        return properties.GetValueOrDefault("facing") switch
        {
            "north" => new(right ? 0.5f : -0.5f, 0, 0.40625f),
            "south" => new(right ? -0.5f : 0.5f, 0, -0.40625f),
            "east" => new(-0.40625f, 0, right ? 0.5f : -0.5f),
            _ => new(0.40625f, 0, right ? -0.5f : 0.5f)
        };
    }
    private static vec3 TrapdoorPivot(Dictionary<string, string> properties) =>
        WallOffset(properties.GetValueOrDefault("facing", "north")) +
        new vec3(0, properties.GetValueOrDefault("half") == "top" ? 0.90625f : 0.09375f, 0);

    private static State? Parse(string text)
    {
        bool legacy = text.StartsWith("legacy:", StringComparison.Ordinal);
        int legacyId = -1, legacyData = 0;
        if (legacy)
        {
            string[] parts = text.Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[1], out int id) || !int.TryParse(parts[2], out int data))
                throw new InvalidDataException($"Invalid legacy block state: {text}");
            legacyId = id;
            legacyData = data;
            if (!SpawnMenu.TryResolveLegacyBlock(id, data, out string name, out string? hint))
            {
                name = id switch
                {
                    34 => "piston_head", 116 => "enchanting_table", 130 => "ender_chest",
                    131 => "tripwire_hook", 143 => "oak_button", 144 => "skeleton_skull",
                    147 => "light_weighted_pressure_plate", 148 => "heavy_weighted_pressure_plate",
                    167 => "iron_trapdoor", 176 => "white_banner", 177 => "white_wall_banner",
                    >= 183 and <= 187 => new[] { "spruce", "birch", "jungle", "dark_oak", "acacia" }[id - 183] + "_fence_gate",
                    >= 193 and <= 197 => new[] { "spruce", "birch", "jungle", "acacia", "dark_oak" }[id - 193] + "_door",
                    >= 219 and <= 234 => new[] { "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
                        "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black" }[id - 219] + "_shulker_box",
                    _ => ""
                };
                if (name.Length == 0) return null;
            }
            text = name + (string.IsNullOrEmpty(hint) ? "" : $"[{hint}]");
            if (id is 64 or 71 or >= 193 and <= 197)
            {
                bool upper = (data & 8) != 0;
                text = $"{name}[half={(upper ? "upper" : "lower")},hinge={((data & 1) != 0 ? "right" : "left")}," +
                    $"facing={new[] { "west", "north", "east", "south" }[data & 3]},open={((data & 4) != 0 ? "true" : "false")}]";
            }
        }
        else if (text.Contains(':') && !text.StartsWith("minecraft:", StringComparison.Ordinal)) return null;
        if (!SpawnMenu.TryParsePaletteState(text, out string block, out var properties)) return null;
        if (legacy)
        {
            string horizontal = new[] { "south", "west", "north", "east" }[legacyData & 3];
            string direction = new[] { "down", "up", "north", "south", "west", "east", "north", "north" }[legacyData & 7];
            if (legacyId is 29 or 33 or 34 or 130 or >= 219 and <= 234)
                properties["facing"] = direction;
            if (legacyId is 29 or 33)
                properties["extended"] = (legacyData & 8) != 0 ? "true" : "false";
            if (legacyId is 63 or 176)
                properties["rotation"] = legacyData.ToString();
            if (legacyId is 177 or 68)
                properties["facing"] = direction;
            if (legacyId is 107 or >= 183 and <= 187 or 131)
            {
                properties["facing"] = horizontal;
                properties[legacyId == 131 ? "powered" : "open"] = (legacyData & (legacyId == 131 ? 8 : 4)) != 0 ? "true" : "false";
            }
            if (legacyId == 69)
            {
                properties["face"] = (legacyData & 7) is 0 or 7 ? "ceiling" : (legacyData & 7) is 5 or 6 ? "floor" : "wall";
                properties["facing"] = new[] { "east", "east", "west", "south", "north", "north", "east", "north" }[legacyData & 7];
                properties["powered"] = (legacyData & 8) != 0 ? "true" : "false";
            }
            if (legacyId is 77 or 143)
            {
                properties["face"] = (legacyData & 7) == 0 ? "ceiling" : (legacyData & 7) == 5 ? "floor" : "wall";
                properties["facing"] = new[] { "north", "east", "west", "south", "north", "north", "north", "north" }[legacyData & 7];
                properties["powered"] = (legacyData & 8) != 0 ? "true" : "false";
            }
            if (legacyId is 70 or 72)
                properties["powered"] = legacyData != 0 ? "true" : "false";
            if (legacyId is 147 or 148) properties["power"] = legacyData.ToString();
            if (legacyId == 167)
            {
                properties["facing"] = new[] { "north", "south", "east", "west" }[legacyData & 3];
                properties["half"] = (legacyData & 8) != 0 ? "top" : "bottom";
                properties["open"] = (legacyData & 4) != 0 ? "true" : "false";
            }
        }
        return new State(block, properties, legacy);
    }
}
