using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade;

public static class CtmAtlas
{
    private static GL? _gl;
    private static readonly Dictionary<string, CtmProperties> _rulesByBlockName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CtmProperties> _rulesByTextureKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<CtmProperties>> _rulesByPack = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(GL gl)
    {
        _gl = gl;
        _rulesByBlockName.Clear();
        _rulesByTextureKey.Clear();
        _rulesByPack.Clear();
        LoadCtmFromResourcePacks();
    }

    private static void LoadCtmFromResourcePacks()
    {
        var allRules = new List<CtmProperties>();

        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets", ".properties"))
        {
            if (!file.RelativePath.Contains("/optifine/ctm/", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var text = MinecraftDataLoader.DecodeUtf8(file.Data);
                var props = CtmProperties.Parse(text, file.RelativePath, file.PackName);

                if (props.MatchTiles.Count == 0 && props.MatchBlocks.Count == 0)
                    continue;

                allRules.Add(props);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse CTM properties '{file.RelativePath}': {ex.Message}");
            }
        }

        foreach (var props in allRules)
        {
            LoadTileTextures(props);

            string key = $"{props.PackId}:{props.PropertiesPath}";
            if (!_rulesByPack.ContainsKey(key))
                _rulesByPack[key] = new List<CtmProperties>();
            _rulesByPack[key].Add(props);

            foreach (var blockName in props.MatchBlocks)
            {
                string normalized = NormalizeBlockName(blockName);
                if (!string.IsNullOrEmpty(normalized))
                    _rulesByBlockName[normalized] = props;
            }

            foreach (var tileName in props.MatchTiles)
            {
                string normalized = NormalizeTextureKey(tileName);
                if (!string.IsNullOrEmpty(normalized))
                    _rulesByTextureKey[normalized] = props;
            }
        }
    }

    private static void LoadTileTextures(CtmProperties props)
    {
        if (_gl == null || props.Tiles.Count == 0) return;

        foreach (int tileIndex in props.Tiles)
        {
            string tileFileName = $"{tileIndex}.png";
            string dir = props.Directory.Replace('\\', '/');
            string expectedPath = $"{dir}/{tileFileName}";

            uint texId = 0;
            foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets", ".png"))
            {
                if (!file.RelativePath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                string filePackId = MinecraftDataLoader.GetResourcePackId(file.PackName);
            if (!filePackId.Equals(props.PackId, StringComparison.OrdinalIgnoreCase))
                    continue;

                texId = LoadTextureFromBytes(file.Data);
                break;
            }

            props.TileTextureIds.Add(texId);
        }
    }

    private static uint LoadTextureFromBytes(byte[] data)
    {
        if (_gl == null) return 0;

        try
        {
            var img = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha);

            uint texId = _gl.GenTexture();
            _gl.BindTexture(GLEnum.Texture2D, texId);

            unsafe
            {
                fixed (byte* ptr = img.Data)
                {
                    _gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba,
                                   (uint)img.Width, (uint)img.Height, 0,
                                   PixelFormat.Rgba, GLEnum.UnsignedByte, ptr);
                }
            }

            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _gl.BindTexture(GLEnum.Texture2D, 0);

            return texId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load CTM tile texture: {ex.Message}");
            return 0;
        }
    }

    public static CtmProperties? FindRule(string blockName, string textureKey)
    {
        if (!string.IsNullOrEmpty(blockName))
        {
            string normalizedBlock = NormalizeBlockName(blockName);
            if (_rulesByBlockName.TryGetValue(normalizedBlock, out var rule))
                return rule;
        }

        if (!string.IsNullOrEmpty(textureKey))
        {
            string normalizedTex = NormalizeTextureKey(textureKey);
            if (_rulesByTextureKey.TryGetValue(normalizedTex, out var rule))
                return rule;
        }

        return null;
    }

    private static string NormalizeBlockName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        name = name.Trim().ToLowerInvariant();
        if (name.Contains(':'))
            name = name[(name.IndexOf(':') + 1)..];
        return name;
    }

    private static string NormalizeTextureKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        key = key.Trim().ToLowerInvariant();
        if (key.StartsWith("minecraft:"))
            key = key["minecraft:".Length..];
        if (key.StartsWith("block/"))
            key = key["block/".Length..];
        return key;
    }
}
