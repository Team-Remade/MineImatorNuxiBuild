using MineImatorSimplyRemade.core.ui.Panels;

namespace MineImatorSimplyRemade.core.world;

public enum BlockFilterMode
{
    Remove,
    Keep
}

internal sealed class WorldBlockFilter
{
    public bool Enabled { get; set; }
    public BlockFilterMode Mode { get; set; } = BlockFilterMode.Remove;
    public HashSet<string> FilteredBlocks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => FilteredBlocks.Count == 0;

    public bool IsActive => Enabled && FilteredBlocks.Count > 0;

    public bool IsWaterFilteredOut()
    {
        if (!Enabled) return false;
        bool hasWater = ContainsWater(FilteredBlocks);
        return Mode == BlockFilterMode.Remove ? hasWater : !hasWater;
    }

    public static bool ContainsWater(IEnumerable<string> blocks)
    {
        foreach (string b in blocks)
        {
            string norm = NormalizeBlockName(b);
            if (norm is "water" or "flowing_water")
                return true;
        }
        return false;
    }

    public static string NormalizeBlockName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string trimmed = name.Trim();
        int colon = trimmed.IndexOf(':');
        string unnamespaced = (colon >= 0 && trimmed.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase))
            ? trimmed[(colon + 1)..]
            : trimmed;
        int bracket = unnamespaced.IndexOf('[');
        if (bracket >= 0)
            unnamespaced = unnamespaced[..bracket];
        return unnamespaced.Trim();
    }

    public static string GetBlockNameFromState(string state)
    {
        if (string.IsNullOrWhiteSpace(state)) return "air";
        if (state.StartsWith("legacy:", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = state.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int id))
            {
                int data = parts.Length >= 3 && int.TryParse(parts[2], out int d) ? d : 0;
                if (SpawnMenu.TryResolveLegacyBlock(id, data, out string legacyName, out _))
                    return NormalizeBlockName(legacyName);
            }
            return state;
        }
        if (SpawnMenu.TryParsePaletteState(state, out string parsedName, out _))
            return parsedName;
        return NormalizeBlockName(state);
    }

    public bool ShouldRemoveBlock(string blockName)
    {
        if (!IsActive) return false;
        string norm = NormalizeBlockName(blockName);
        if (norm is "air" or "cave_air" or "void_air" or "") return false;

        bool matches = FilteredBlocks.Contains(norm) || FilteredBlocks.Contains("minecraft:" + norm);
        if (!matches && (norm is "flowing_water" || norm is "water"))
        {
            matches = FilteredBlocks.Contains("water") || FilteredBlocks.Contains("minecraft:water") ||
                      FilteredBlocks.Contains("flowing_water") || FilteredBlocks.Contains("minecraft:flowing_water");
        }
        if (!matches && (norm is "flowing_lava" || norm is "lava"))
        {
            matches = FilteredBlocks.Contains("lava") || FilteredBlocks.Contains("minecraft:lava") ||
                      FilteredBlocks.Contains("flowing_lava") || FilteredBlocks.Contains("minecraft:flowing_lava");
        }

        return Mode switch
        {
            BlockFilterMode.Remove => matches,
            BlockFilterMode.Keep => !matches,
            _ => false
        };
    }

    public static string RemoveWaterloggedProperty(string state)
    {
        if (string.IsNullOrWhiteSpace(state)) return state;
        if (state.StartsWith("legacy:", StringComparison.OrdinalIgnoreCase)) return state;
        int bracket = state.IndexOf('[');
        if (bracket < 0) return state;
        int endBracket = state.LastIndexOf(']');
        if (endBracket <= bracket) return state;

        string name = state[..bracket];
        string body = state[(bracket + 1)..endBracket];
        string[] tokens = body.Split(',', StringSplitOptions.RemoveEmptyEntries);
        bool changed = false;
        var updated = new List<string>(tokens.Length);

        foreach (string token in tokens)
        {
            int eq = token.IndexOf('=');
            if (eq > 0)
            {
                string key = token[..eq].Trim();
                string val = token[(eq + 1)..].Trim();
                if (string.Equals(key, "waterlogged", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(val, "true", StringComparison.OrdinalIgnoreCase))
                {
                    updated.Add("waterlogged=false");
                    changed = true;
                    continue;
                }
            }
            updated.Add(token);
        }

        if (!changed) return state;
        return $"{name}[{string.Join(',', updated)}]";
    }

    public WorldBlockFilter Clone()
    {
        var clone = new WorldBlockFilter
        {
            Enabled = Enabled,
            Mode = Mode
        };
        foreach (string b in FilteredBlocks) clone.FilteredBlocks.Add(b);
        return clone;
    }
}
