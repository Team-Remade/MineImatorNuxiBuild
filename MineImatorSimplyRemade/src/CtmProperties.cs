namespace MineImatorSimplyRemade;

public class CtmProperties
{
    public string PropertiesPath { get; set; } = "";
    public string PackId { get; set; } = "";
    public string Directory { get; set; } = "";
    
    public List<string> MatchTiles { get; set; } = new();
    public List<string> MatchBlocks { get; set; } = new();
    public string Method { get; set; } = "ctm_compact";
    public List<int> Tiles { get; set; } = new();
    public string Connect { get; set; } = "block";
    public List<string> Faces { get; set; } = new();
    public List<uint> TileTextureIds { get; set; } = new();
    
    public static CtmProperties Parse(string text, string propertiesPath, string packId)
    {
        var props = new CtmProperties
        {
            PropertiesPath = propertiesPath,
            PackId = MinecraftDataLoader.GetResourcePackId(packId),
            Directory = Path.GetDirectoryName(propertiesPath) ?? ""
        };
        
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;
            
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx < 0) continue;
            
            var key = trimmed[..eqIdx].Trim().ToLowerInvariant();
            var value = trimmed[(eqIdx + 1)..].Trim();
            
            switch (key)
            {
                case "matchtiles":
                    props.MatchTiles = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                    break;
                case "matchblocks":
                case "matchblock":
                    props.MatchBlocks = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                    break;
                case "method":
                    props.Method = value.ToLowerInvariant();
                    break;
                case "tiles":
                    props.Tiles = ParseTileRange(value);
                    break;
                case "connect":
                    props.Connect = value.ToLowerInvariant();
                    break;
                case "faces":
                    props.Faces = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                    break;
            }
        }
        
        return props;
    }
    
    private static List<int> ParseTileRange(string value)
    {
        var tiles = new List<int>();
        value = value.Trim();

        if (string.IsNullOrEmpty(value))
            return tiles;

        // Range like "0-46"
        if (value.Contains('-'))
        {
            var parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out int start) && int.TryParse(parts[1], out int end))
            {
                for (int i = start; i <= end; i++)
                    tiles.Add(i);
                return tiles;
            }
        }

        // Comma-separated list or space-separated list like "0 1 2 3"
        var separators = new[] { ' ', ',' };
        var tokens = value.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (int.TryParse(token, out int num))
                tiles.Add(num);
        }

        return tiles;
    }
}
