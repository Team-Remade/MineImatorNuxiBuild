using System.Text.Json;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemadeNuxi.core.objs;
using ZstdSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class SpawnMenu
{
    private static bool IsMeshCacheLoadError(Exception error) => error is IOException or InvalidDataException or
        UnauthorizedAccessException or NotSupportedException or ArgumentException or OverflowException or
        JsonException or ZstdException;

    private SceneObject BuildMeshCache(string cachePath, string sourcePath, string resourcePackId)
    {
        var data = MeshCacheData.Load(cachePath);
        string normalizedPack = MinecraftDataLoader.NormalizeResourcePackStack(resourcePackId);
        string name = Path.GetFileNameWithoutExtension(sourcePath);
        if (MeshCacheData.IsCache(sourcePath) && MeshCacheData.IsSchematic(name))
            name = Path.GetFileNameWithoutExtension(name);
        int next = GetNextAvailableObjectNumber(name);
        var root = new SceneObject
        {
            Name = next > 1 ? $"{name}{next}" : name,
            ObjectType = "Schematic",
            SpawnCategory = "Scenery",
            SourceAssetPath = sourcePath,
            ResourcePackId = normalizedPack,
            MaterialSettings = new MaterialSettings(),
            Position = vec3.Zero,
            PivotOffset = vec3.Zero,
            InheritPivotOffset = false
        };
        root.AssignObjectId();
        try
        {
            foreach (var batch in data.Batches)
            {
                var material = batch.Material;
                string reference = material.Texture;
                int suffix = reference.IndexOf(' ');
                if (suffix >= 0) reference = reference[..suffix];
                string key = reference.StartsWith("block/", StringComparison.Ordinal) ? reference[6..] : reference;
                string resolved = key.Length == 0 ? "" : TerrainAtlas.ResolveTextureKey(key, normalizedPack);
                uint texture = 0;
                if (key.Length != 0 && !TerrainAtlas.Textures.TryGetValue(resolved, out texture))
                {
                    string path = Path.Combine(MinecraftDataLoader.GetVersionRoot(), "textures",
                        reference.Replace('/', Path.DirectorySeparatorChar) + ".png");
                    if (!File.Exists(path))
                        throw new FileNotFoundException($"The meshcache texture '{reference}' is not installed.", path);
                    texture = MineImatorLoader.Instance.LoadTextureFromFile(path);
                    if (texture == 0) throw new InvalidDataException($"Could not upload meshcache texture '{path}'.");
                }
                uint color = material.Color;
                var tint = new vec3(color & 255, (color >> 8) & 255, (color >> 16) & 255) / 255;
                var mesh = new Mesh(Gl!)
                {
                    TextureId = texture,
                    MinecraftTextureKey = resolved,
                    AnimationKey = TerrainAtlas.GetAnimationKey(texture),
                    IsTranslucent = material.Depth == 2 || (color >> 24) < 255 ||
                        TerrainAtlas.IsTextureTranslucent(resolved),
                    BaseDoubleSided = material.Texture.Contains(" nocull", StringComparison.Ordinal),
                    BaseTint = tint,
                    Albedo = tint,
                    BaseAlpha = (color >> 24) / 255f,
                    BiomeTint = material.Biome,
                    AutoEmissionLevel = (byte)Math.Clamp((material.Emission * 15 + 127) / 255, 0, 15),
                    TerrainWindKind = material.Wave == 0 ? 0 :
                        WindSettings.ClassifyTexture(key) is var kind && kind != 0 ? kind :
                        material.Wave == 2 ? 2 : 1
                };
                root.AddMesh(mesh);
                mesh.Vertices.AddRange(batch.Vertices);
                mesh.Normals.AddRange(batch.Normals);
                mesh.TexCoords.AddRange(batch.TexCoords);
                mesh.Upload();
            }
            root.ApplyMaterialSettingsToMeshes();
            return root;
        }
        catch
        {
            foreach (var mesh in root.Visuals) mesh.Dispose();
            throw;
        }
    }
}
