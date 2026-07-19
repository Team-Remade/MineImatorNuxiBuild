namespace MineImatorSimplyRemade;

public class CtmResolvedTile
{
    public uint TextureId { get; init; }
    public float UMin { get; init; }
    public float VMin { get; init; }
    public float UMax { get; init; }
    public float VMax { get; init; }
    public int TileIndex { get; init; }
}

public static class CtmResolver
{
    public static CtmResolvedTile? Resolve(string blockName, string textureKey, string faceName,
                                           int tx, int ty, int tz,
                                           int tileX, int tileY, int tileZ,
                                           string resourcePackId)
    {
        var rule = CtmAtlas.FindRule(blockName, textureKey);
        if (rule == null)
        {
            if (blockName.Contains("bookshelf", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[CTM] No rule found for block '{blockName}', texture '{textureKey}'");
            return null;
        }

        Console.WriteLine($"[CTM] Found rule for block '{blockName}': method={rule.Method}, faces={string.Join(",", rule.Faces)}, tiles={rule.Tiles.Count}, tileTextures={rule.TileTextureIds.Count}");

        if (!string.IsNullOrEmpty(resourcePackId) &&
            !rule.PackId.Equals(MinecraftDataLoader.NormalizeResourcePackId(resourcePackId), StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[CTM] Pack mismatch: rule pack='{rule.PackId}', requested='{resourcePackId}'");
            return null;
        }

        if (!FaceMatchesRule(faceName, rule))
        {
            Console.WriteLine($"[CTM] Face '{faceName}' doesn't match rule faces: {string.Join(",", rule.Faces)}");
            return null;
        }

        int tileIndex = ComputeTileIndex(rule, faceName, tx, ty, tz, tileX, tileY, tileZ);
        Console.WriteLine($"[CTM] Face '{faceName}' at tile ({tx},{ty},{tz}) computed tileIndex={tileIndex}");

        if (tileIndex < 0 || tileIndex >= rule.TileTextureIds.Count)
        {
            Console.WriteLine($"[CTM] Tile index {tileIndex} out of range (0-{rule.TileTextureIds.Count - 1})");
            tileIndex = 0;
        }

        uint texId = tileIndex < rule.TileTextureIds.Count ? rule.TileTextureIds[tileIndex] : 0;
        if (texId == 0)
        {
            Console.WriteLine($"[CTM] Tile {tileIndex} has no texture (texId=0)");
            return null;
        }

        Console.WriteLine($"[CTM] Resolved tile {tileIndex} with texId={texId}");

        return new CtmResolvedTile
        {
            TextureId = texId,
            UMin = 0f,
            VMin = 0f,
            UMax = 1f,
            VMax = 1f,
            TileIndex = tileIndex
        };
    }

    private static bool FaceMatchesRule(string faceName, CtmProperties rule)
    {
        if (rule.Faces.Count == 0)
            return true;

        foreach (var f in rule.Faces)
        {
            string lower = f.ToLowerInvariant();
            if (lower == faceName)
                return true;
            if (lower == "sides" && IsSideFace(faceName))
                return true;
            if (lower == "all")
                return true;
            if (lower == "top" && faceName == "up")
                return true;
            if (lower == "bottom" && faceName == "down")
                return true;
        }

        return false;
    }

    private static bool IsSideFace(string faceName) => faceName is "north" or "south" or "east" or "west";

    private static int ComputeTileIndex(CtmProperties rule, string faceName,
                                        int tx, int ty, int tz,
                                        int tileX, int tileY, int tileZ)
    {
        string method = rule.Method;

        if (method == "horizontal")
            return ComputeHorizontalTileIndex(faceName, tx, ty, tz, tileX, tileY, tileZ);

        if (method == "vertical")
            return ComputeVerticalTileIndex(faceName, tx, ty, tz, tileX, tileY, tileZ);

        if (method == "top" && faceName == "up")
            return ComputeCompactTileIndex(faceName, tx, ty, tz, tileX, tileY, tileZ);

        return ComputeCompactTileIndex(faceName, tx, ty, tz, tileX, tileY, tileZ);
    }

    private static int ComputeHorizontalTileIndex(string faceName, int tx, int ty, int tz,
                                                  int tileX, int tileY, int tileZ)
    {
        var (upDir, rightDir) = GetFaceAxes(faceName);
        bool left  = IsConnected(tx, ty, tz, rightDir[0], rightDir[1], rightDir[2], tileX, tileY, tileZ);
        bool right = IsConnected(tx, ty, tz, -rightDir[0], -rightDir[1], -rightDir[2], tileX, tileY, tileZ);

        // Matches OptiFine/Continuity: { 3, 2, 0, 1 }
        // bit 0 = left, bit 1 = right
        return (left, right) switch
        {
            (false, false) => 3,
            (true, false)  => 2,
            (false, true)  => 0,
            (true, true)   => 1,
        };
    }

    private static int ComputeVerticalTileIndex(string faceName, int tx, int ty, int tz,
                                                int tileX, int tileY, int tileZ)
    {
        var (upDir, rightDir) = GetFaceAxes(faceName);
        bool down = IsConnected(tx, ty, tz, -upDir[0], -upDir[1], -upDir[2], tileX, tileY, tileZ);
        bool up   = IsConnected(tx, ty, tz,  upDir[0],  upDir[1],  upDir[2], tileX, tileY, tileZ);

        // Matches OptiFine/Continuity: { 3, 2, 0, 1 }
        // bit 0 = down, bit 1 = up
        return (down, up) switch
        {
            (false, false) => 3,
            (true, false)  => 2,
            (false, true)  => 0,
            (true, true)   => 1,
        };
    }

    private static int ComputeCompactTileIndex(string faceName, int tx, int ty, int tz,
                                               int tileX, int tileY, int tileZ)
    {
        var (upDir, rightDir) = GetFaceAxes(faceName);
        bool top = IsConnected(tx, ty, tz, -upDir[0], -upDir[1], -upDir[2], tileX, tileY, tileZ);
        bool bottom = IsConnected(tx, ty, tz, upDir[0], upDir[1], upDir[2], tileX, tileY, tileZ);
        bool left = IsConnected(tx, ty, tz, rightDir[0], rightDir[1], rightDir[2], tileX, tileY, tileZ);
        bool right = IsConnected(tx, ty, tz, -rightDir[0], -rightDir[1], -rightDir[2], tileX, tileY, tileZ);

        // Check corners (only relevant when both adjacent edges are connected)
        bool topLeft = top && left && IsConnected(tx, ty, tz, upDir[0] - rightDir[0], upDir[1] - rightDir[1], upDir[2] - rightDir[2], tileX, tileY, tileZ);
        bool topRight = top && right && IsConnected(tx, ty, tz, upDir[0] + rightDir[0], upDir[1] + rightDir[1], upDir[2] + rightDir[2], tileX, tileY, tileZ);
        bool bottomLeft = bottom && left && IsConnected(tx, ty, tz, -upDir[0] - rightDir[0], -upDir[1] - rightDir[1], -upDir[2] - rightDir[2], tileX, tileY, tileZ);
        bool bottomRight = bottom && right && IsConnected(tx, ty, tz, -upDir[0] + rightDir[0], -upDir[1] + rightDir[1], -upDir[2] + rightDir[2], tileX, tileY, tileZ);

        bool allEdges = top && bottom && left && right;
        bool allCorners = topLeft && topRight && bottomLeft && bottomRight;

        // OptiFine/Continuity compact CTM mapping (5 tiles):
        // 0: fully surrounded (all edges + all corners)
        // 1: vertical strip (top+bottom, no left/right)
        // 2: horizontal strip (left+right, no top/bottom)
        // 3: isolated
        // 4: outer corners (all edges, not all corners)
        if (allEdges && allCorners) return 0;
        if (allEdges && !allCorners) return 4;
        if (top && bottom && !left && !right) return 1;
        if (left && right && !top && !bottom) return 2;
        return 3;
    }

    private static bool IsConnected(int tx, int ty, int tz, int dx, int dy, int dz,
                                    int tileX, int tileY, int tileZ)
    {
        int nx = tx + dx;
        int ny = ty + dy;
        int nz = tz + dz;
        return nx >= 0 && nx < tileX && ny >= 0 && ny < tileY && nz >= 0 && nz < tileZ;
    }

    private static (int[] up, int[] right) GetFaceAxes(string face)
    {
        return face.ToLowerInvariant() switch
        {
            "up"    => (new[] { 0, 0, -1 }, new[] { 1, 0, 0 }),
            "down"  => (new[] { 0, 0, 1 }, new[] { 1, 0, 0 }),
            "north" => (new[] { 0, 1, 0 }, new[] { -1, 0, 0 }),
            "south" => (new[] { 0, 1, 0 }, new[] { 1, 0, 0 }),
            "east"  => (new[] { 0, 1, 0 }, new[] { 0, 0, -1 }),
            "west"  => (new[] { 0, 1, 0 }, new[] { 0, 0, 1 }),
            _       => (new[] { 0, 1, 0 }, new[] { 1, 0, 0 }),
        };
    }
}
