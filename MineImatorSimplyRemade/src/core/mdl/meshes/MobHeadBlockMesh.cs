using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public static class MobHeadBlockMesh
{
    private static readonly IReadOnlyList<BlockVariantEntry> FloorVariants = Enumerable.Range(0, 16).Select(rotation =>
        new BlockVariantEntry { VariantKey = $"rotation={rotation}" }).ToArray();
    private static readonly IReadOnlyList<BlockVariantEntry> WallVariants = new[] { "north", "east", "south", "west" }
        .Select(facing => new BlockVariantEntry { VariantKey = $"facing={facing}" }).ToArray();

    public static string? GetModelName(string blockName) => BlockRegistry.NormalizeBlockName(blockName) switch
    {
        "skeleton_skull" or "skeleton_wall_skull" => "skeleton",
        "wither_skeleton_skull" or "wither_skeleton_wall_skull" => "wither_skeleton",
        "zombie_head" or "zombie_wall_head" => "zombie",
        "creeper_head" or "creeper_wall_head" => "creeper",
        "piglin_head" or "piglin_wall_head" => "piglin",
        "dragon_head" or "dragon_wall_head" => "ender_dragon",
        "player_head" or "player_wall_head" => "steve",
        _ => null
    };

    internal static IReadOnlyList<BlockVariantEntry> GetVariants(string blockName)
        => blockName.Contains("_wall_", StringComparison.Ordinal) ? WallVariants : FloorVariants;

    internal static JsonObject ReadModelDocument(string blockName, out string modelPath)
    {
        string name = GetModelName(blockName) ??
            throw new ArgumentException($"'{blockName}' is not a mob head block.", nameof(blockName));
        modelPath = Path.Combine(MinecraftDataLoader.GetVersionRoot(), "characters", "vanilla", name, name + ".mimodel");
        var document = JsonNode.Parse(File.ReadAllText(modelPath), documentOptions: AppJsonContext.DocumentOptions)?.AsObject()
            ?? throw new InvalidDataException($"Invalid mob model '{modelPath}'.");
        var head = FindHead(document["parts"] as JsonArray)?.DeepClone().AsObject()
            ?? throw new InvalidDataException($"The '{name}' model has no head node.");
        // The dragon's authored pivot is at the back of its head, halfway up.
        head["position"] = name == "ender_dragon" ? new JsonArray(0, 8, -8) : new JsonArray(0, 0, 0);
        float scale = name == "ender_dragon" ? 0.75f : 1f;
        document["scale"] = new JsonArray(scale, scale, scale);
        if (name is "steve" or "zombie")
            document["texture"] = Path.GetRelativePath(Path.GetDirectoryName(modelPath)!,
                name == "steve"
                    ? Path.Combine(MinecraftDataLoader.GetVersionRoot(), "textures", "entity", "player", "wide", "steve.png")
                    : Path.Combine(MinecraftDataLoader.GetVersionRoot(), "textures", "entity", "zombie", "zombie.png"));
        document["parts"] = new JsonArray(head);
        return document;

        static JsonObject? FindHead(JsonArray? parts)
        {
            if (parts == null) return null;
            foreach (var part in parts.OfType<JsonObject>())
            {
                if (string.Equals(part["name"]?.GetValue<string>(), "head", StringComparison.OrdinalIgnoreCase))
                    return part;
                if (FindHead(part["parts"] as JsonArray) is { } found) return found;
            }
            return null;
        }
    }

    public static List<Mesh> Build(GL gl, string blockName, BlockVariantEntry variant, string resourcePackId = "",
        int tileX = 1, int tileY = 1, int tileZ = 1)
    {
        blockName = BlockRegistry.NormalizeBlockName(blockName);
        var document = ReadModelDocument(blockName, out string modelPath);
        var model = document.Deserialize(AppJsonContext.Default.MiModel)
            ?? throw new InvalidDataException($"Invalid mob model '{modelPath}'.");
        model.DirectoryPath = Path.GetDirectoryName(modelPath)!;
        model.FullPath = modelPath;
        MineImatorLoader.NormalizeModelTextureSizes(model);
        var loader = MineImatorLoader.Instance;
        loader.Initialize(gl);
        var root = loader.CreateCharacterFromModel(model)
            ?? throw new InvalidDataException($"The mob head '{blockName}' has no geometry.");
        var meshes = new List<Mesh>();
        try
        {
            string state = variant.StateKey ?? variant.VariantKey;
            var properties = state.Split(',').Select(property => property.Split('=', 2))
                .Where(property => property.Length == 2).ToDictionary(property => property[0], property => property[1]);
            float yaw = 0;
            vec3 placement = new(0.5f, 0, 0.5f);
            if (blockName.Contains("_wall_", StringComparison.Ordinal))
            {
                string facing = properties.GetValueOrDefault("facing", "north");
                (yaw, vec3 direction) = facing switch
                {
                    "north" => (180f, -vec3.UnitZ),
                    "east" => (90f, vec3.UnitX),
                    "south" => (0f, vec3.UnitZ),
                    "west" => (270f, -vec3.UnitX),
                    _ => throw new InvalidDataException($"Invalid mob head facing '{facing}'.")
                };
                placement += new vec3(0, 0.25f, 0) - direction * 0.25f;
            }
            else if (properties.TryGetValue("rotation", out string? rotation))
            {
                if (!int.TryParse(rotation, NumberStyles.None, CultureInfo.InvariantCulture, out int step) || step is < 0 or > 15)
                    throw new InvalidDataException($"Invalid mob head rotation '{rotation}'.");
                yaw = -step * 22.5f;
            }
            var placementTransform = mat4.Translate(placement) * mat4.RotateY(yaw * MathF.PI / 180);
            foreach (var node in new[] { root }.Concat(root.GetAllDescendants()))
            {
                var transform = placementTransform * node.GetWorldMatrix();
                var normalTransform = transform.Inverse.Transposed;
                foreach (var mesh in node.Visuals.ToArray())
                {
                    string? texturePath = loader.GetTexturePath(mesh.TextureId);
                    if (texturePath != null)
                    {
                        string relative = Path.GetRelativePath(Path.Combine(MinecraftDataLoader.GetVersionRoot(), "textures"),
                            texturePath).Replace('\\', '/');
                        string key = relative[..^4];
                        string selected = TerrainAtlas.ResolveTextureKey(key, resourcePackId, blockName);
                        if (TerrainAtlas.Textures.TryGetValue(selected, out uint texture) && texture != 0)
                        {
                            mesh.TextureId = texture;
                            // Mine-imator pads skins to square images; the terrain atlas keeps their native aspect.
                            gl.BindTexture(TextureTarget.Texture2D, texture);
                            gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureWidth, out int width);
                            gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureHeight, out int height);
                            if (width <= 0 || height <= 0)
                                throw new InvalidDataException($"Invalid mob head texture '{selected}'.");
                            float side = Math.Max(width, height);
                            var uvScale = new vec2(side / width, side / height);
                            for (int i = 0; i < mesh.TexCoords.Count; i++) mesh.TexCoords[i] *= uvScale;
                        }
                        mesh.MinecraftTextureKey = selected;
                    }
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                        mesh.Vertices[i] = (transform * new vec4(mesh.Vertices[i], 1)).xyz;
                    for (int i = 0; i < mesh.Normals.Count; i++)
                        mesh.Normals[i] = (normalTransform * new vec4(mesh.Normals[i], 0)).xyz.Normalized;
                    mesh.BaseTint = mesh.Albedo;
                    node.Visuals.Remove(mesh);
                    meshes.Add(mesh);
                }
            }
            foreach (var mesh in meshes)
            {
                var vertices = mesh.Vertices.ToArray();
                var normals = mesh.Normals.ToArray();
                var uvs = mesh.TexCoords.ToArray();
                var sourceIndices = mesh.Indices;
                var indices = sourceIndices?.ToList();
                for (int z = 0; z < Math.Clamp(tileZ, 1, SceneObject.MaxTilesPerAxis); z++)
                for (int y = 0; y < Math.Clamp(tileY, 1, SceneObject.MaxTilesPerAxis); y++)
                for (int x = 0; x < Math.Clamp(tileX, 1, SceneObject.MaxTilesPerAxis); x++)
                {
                    if (x == 0 && y == 0 && z == 0) continue;
                    uint firstVertex = (uint)mesh.Vertices.Count;
                    var offset = new vec3(x, y, z);
                    mesh.Vertices.AddRange(vertices.Select(vertex => vertex + offset));
                    mesh.Normals.AddRange(normals);
                    mesh.TexCoords.AddRange(uvs);
                    if (sourceIndices != null)
                        indices!.AddRange(sourceIndices.Select(index => index + firstVertex));
                }
                mesh.Indices = indices?.ToArray();
                mesh.Upload();
            }
            return meshes;
        }
        catch
        {
            foreach (var mesh in meshes) mesh.Dispose();
            throw;
        }
        finally
        {
            foreach (var node in new[] { root }.Concat(root.GetAllDescendants()))
            {
                foreach (var mesh in node.Visuals) mesh.Dispose();
                if (node is BoneSceneObject bone) bone.IndicatorMesh?.Dispose();
            }
        }
    }
}
