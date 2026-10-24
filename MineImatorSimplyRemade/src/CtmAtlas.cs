using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade;

public static class CtmAtlas
{
    private static GL? _gl;
    private static readonly Dictionary<uint, string> _textureKeys = new();
    private static readonly Dictionary<(string Pack, string Path), uint> _textureHandles = new();
    private static readonly Dictionary<(string Pack, string Path), uint> _tileTextures = new();

    public static string? GetTextureKey(uint texture) => _textureKeys.GetValueOrDefault(texture);
    private static readonly Dictionary<(string Pack, string Key), List<CtmProperties>> _rulesByBlockName = new();
    private static readonly Dictionary<(string Pack, string Key), List<CtmProperties>> _rulesByTextureKey = new();

    public static void Initialize(GL gl)
    {
        // Existing meshes retain these handles. Reuse them on reload until the GL context closes.
        if (!ReferenceEquals(_gl, gl))
        {
            _textureHandles.Clear();
            _textureKeys.Clear();
        }
        _gl = gl;
        _tileTextures.Clear();
        _rulesByBlockName.Clear();
        _rulesByTextureKey.Clear();
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

        LoadTileTextures(allRules);
        foreach (var props in allRules)
        {
            foreach (var normalized in props.MatchBlocks.Select(NormalizeBlockName).Where(normalized => !string.IsNullOrEmpty(normalized)))
            {
                var key = (props.PackId, normalized);
                if (!_rulesByBlockName.TryGetValue(key, out var rules))
                    _rulesByBlockName[key] = rules = new();
                rules.Add(props);
            }

            foreach (var normalized in props.MatchTiles.Select(NormalizeTextureKey).Where(normalized => !string.IsNullOrEmpty(normalized)))
            {
                var key = (props.PackId, normalized);
                if (!_rulesByTextureKey.TryGetValue(key, out var rules))
                    _rulesByTextureKey[key] = rules = new();
                rules.Add(props);
            }
        }
    }

    private static void LoadTileTextures(List<CtmProperties> rules)
    {
        if (_gl == null) return;

        var paths = rules.SelectMany(rule => rule.Tiles.Select(tile =>
            $"{rule.Directory.Replace('\\', '/')}/{tile}.png".ToLowerInvariant())).ToHashSet();
        var files = new Dictionary<(string Pack, string Path), byte[]>();
        foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets", ".png"))
        {
            string path = file.RelativePath.Replace('\\', '/').ToLowerInvariant();
            if (paths.Contains(path))
                files[(MinecraftDataLoader.GetResourcePackId(file.PackName), path)] = file.Data;
        }

        foreach (var (key, data) in files)
        {
            _textureHandles.TryGetValue(key, out uint existing);
            uint texture = LoadTextureFromBytes(data, existing);
            if (texture == 0) continue;
            _textureHandles[key] = texture;
            _tileTextures[key] = texture;
            _textureKeys[texture] = MinecraftDataLoader.BuildResourcePackTextureKeyFromId(key.Pack, key.Path[..^4]);
        }
    }

    public static uint ResolveTileTexture(CtmProperties rule, int tileIndex, string resourcePackId, string blockName = "")
    {
        if (tileIndex < 0 || tileIndex >= rule.Tiles.Count) return 0;
        string path = $"{rule.Directory.Replace('\\', '/')}/{rule.Tiles[tileIndex]}.png".ToLowerInvariant();
        foreach (string pack in MinecraftDataLoader.GetResourcePackStack(resourcePackId))
            if (_tileTextures.TryGetValue((pack, path), out uint selected)) return selected;
        string source = BlockRegistry.GetBlockSourceId(blockName);
        return _tileTextures.GetValueOrDefault((source, path));
    }

    private static uint LoadTextureFromBytes(byte[] data, uint existing = 0)
    {
        if (_gl == null) return 0;

        try
        {
            var img = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha);

            uint texId = existing == 0 ? _gl.GenTexture() : existing;
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

    public static CtmProperties? FindRule(string blockName, string textureKey, string resourcePackId = "",
        Func<CtmProperties, bool>? matches = null)
    {
        string normalizedBlock = NormalizeBlockName(blockName);
        string normalizedTex = NormalizeTextureKey(textureKey);
        string selectedTex = NormalizeTextureKey(TerrainAtlas.ResolveTextureKey(textureKey, resourcePackId, blockName));
        string sourceId = BlockRegistry.GetBlockSourceId(blockName);
        var packs = MinecraftDataLoader.GetResourcePackStack(resourcePackId);
        for (int i = 0; i <= packs.Count; i++)
        {
            string packId = i < packs.Count ? packs[i] : sourceId;
            if (packId.Length == 0) continue;
            if (_rulesByBlockName.TryGetValue((packId, normalizedBlock), out var blockRules))
                for (int j = blockRules.Count - 1; j >= 0; j--)
                    if (matches == null || matches(blockRules[j])) return blockRules[j];

            if (_rulesByTextureKey.TryGetValue((packId, selectedTex), out var textureRules))
                for (int j = textureRules.Count - 1; j >= 0; j--)
                    if (matches == null || matches(textureRules[j])) return textureRules[j];
            if (selectedTex != normalizedTex && _rulesByTextureKey.TryGetValue((packId, normalizedTex), out textureRules))
                for (int j = textureRules.Count - 1; j >= 0; j--)
                    if (matches == null || matches(textureRules[j])) return textureRules[j];
        }

        return null;
    }

    private static string NormalizeBlockName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        name = name.Trim().ToLowerInvariant();
        return BlockRegistry.NormalizeBlockName(name);
    }

    private static string NormalizeTextureKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        key = key.Trim().ToLowerInvariant();
        if (key.StartsWith("resourcepack:"))
        {
            int separator = key.IndexOf(':', "resourcepack:".Length);
            if (separator >= 0) key = key[(separator + 1)..];
        }
        if (key.StartsWith("minecraft:"))
            key = key["minecraft:".Length..];
        if (key.StartsWith("block/"))
            key = key["block/".Length..];
        return key;
    }
}
