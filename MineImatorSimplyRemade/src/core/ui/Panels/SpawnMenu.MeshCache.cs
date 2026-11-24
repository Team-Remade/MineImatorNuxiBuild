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
        int tileX = Math.Clamp(_sceneryTileX, 1, SceneObject.MaxTilesPerAxis);
        int tileY = Math.Clamp(_sceneryTileY, 1, SceneObject.MaxTilesPerAxis);
        int tileZ = Math.Clamp(_sceneryTileZ, 1, SceneObject.MaxTilesPerAxis);
        long copies = (long)tileX * tileY * tileZ;
        vec3 tileSize = new(data.Width, data.Height, data.Length);
        if (copies > 1)
        {
            long vertexCount = 0;
            foreach (var batch in data.Batches) vertexCount += batch.Vertices.Count;
            if (vertexCount * copies > 8_000_000L)
                throw new InvalidDataException("The repeated meshcache is too large.");
            if (data.Width <= 1 && data.Height <= 1 && data.Length <= 1)
            {
                // Custom single-buffer caches carry dummy 1x1x1 dimensions; tile by their real extent.
                vec3 min = new(float.MaxValue), max = new(float.MinValue);
                foreach (var batch in data.Batches)
                    foreach (var v in batch.Vertices)
                    {
                        min = new vec3(Math.Min(min.x, v.x), Math.Min(min.y, v.y), Math.Min(min.z, v.z));
                        max = new vec3(Math.Max(max.x, v.x), Math.Max(max.y, v.y), Math.Max(max.z, v.z));
                    }
                if (max.x >= min.x) tileSize = max - min;
            }
        }
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
                for (int ty = 0; ty < tileY; ty++)
                    for (int tz = 0; tz < tileZ; tz++)
                        for (int tx = 0; tx < tileX; tx++)
                        {
                            var offset = new vec3((tx - (tileX - 1) * 0.5f) * tileSize.x,
                                (ty - (tileY - 1) * 0.5f) * tileSize.y,
                                (tz - (tileZ - 1) * 0.5f) * tileSize.z);
                            if (copies == 1) mesh.Vertices.AddRange(batch.Vertices);
                            else foreach (var v in batch.Vertices) mesh.Vertices.Add(v + offset);
                            mesh.Normals.AddRange(batch.Normals);
                            mesh.TexCoords.AddRange(batch.TexCoords);
                        }
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
