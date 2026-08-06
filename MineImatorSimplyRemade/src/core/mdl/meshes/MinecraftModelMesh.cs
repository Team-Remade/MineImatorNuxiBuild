using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>
/// A <see cref="Mesh"/> that renders a fully-resolved Minecraft block model
/// (<see cref="ResolvedBlockModel"/>) as a set of textured cuboid elements.
///
/// Because Minecraft block models can reference different textures per face,
/// this mesh is split into one sub-mesh per unique texture used.  Each sub-mesh
/// is stored as an independent <see cref="Mesh"/> instance in <see cref="SubMeshes"/>;
/// the owner <see cref="SceneObject"/> should add each one to its Visuals list.
///
/// Usage:
/// <code>
/// var meshes = MinecraftModelMesh.Build(gl, resolvedModel);
/// foreach (var m in meshes) sceneObject.AddMesh(m);
/// </code>
/// </summary>
public static class MinecraftModelMesh
{
    // Minecraft model coordinates are 0–16; we normalise to 0–1 (one block unit)
    private const float Scale = 1f / 16f;

    // ── Default block tints ───────────────────────────────────────────────────
    //
    // Vanilla applies a per-biome colour multiply to certain block faces
    // (tracked via the model's "tintindex" — see BlockModelFace.TintIndex).
    // This tool has no biome/world data to sample, so instead of wiring up
    // full per-face tint support we apply one fixed colour per block, keyed
    // by block name, to every submesh generated for that block.
    //
    // Only blocks whose *currently bundled* textures actually need a tint to
    // look correct are listed here. Several vanilla-era textures in this
    // asset set (e.g. grass top, leaves) already bake their tint directly
    // into the PNG, so adding entries for those would double-tint them.
    // Water's real texture (water_still.png) is a light desaturated grey
    // that vanilla tints blue at render time — 0x3F76E4 is Minecraft's
    // biome-independent default water colour, used here since there's no
    // biome to sample. Lava needs no entry: lava_still.png is already fully
    // opaque and coloured.
    private static readonly Dictionary<string, vec3> DefaultBlockTints =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "water", new vec3(63f / 255f, 118f / 255f, 228f / 255f) },
        };

    private static readonly HashSet<string> NoBiomeTintBlocks =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "cherry_leaves",
            "pale_oak_leaves",
        };

    private static readonly Lazy<vec3> DefaultGrassTint = new(() =>
        LoadDefaultColormapTint("grass.png", new vec3(145f / 255f, 189f / 255f, 89f / 255f)));

    private static readonly Lazy<vec3> DefaultFoliageTint = new(() =>
        LoadDefaultColormapTint("foliage.png", new vec3(72f / 255f, 181f / 255f, 24f / 255f)));

    private static readonly Dictionary<string, bool> GrayscaleTextureCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Applies <see cref="DefaultBlockTints"/> (if any) for <paramref name="blockName"/>
    /// to every mesh in <paramref name="meshes"/>. No-op for blocks without a
    /// registered tint or when <paramref name="blockName"/> is empty (e.g. spawn-menu
    /// preview thumbnails built without a block-name hint).
    /// </summary>
    private static void ApplyDefaultBlockTint(List<Mesh> meshes, string blockName)
    {
        if (string.IsNullOrEmpty(blockName)) return;
        if (!DefaultBlockTints.TryGetValue(blockName, out vec3 tint)) return;

        foreach (var mesh in meshes)
            mesh.Albedo = tint;
    }

    /// <summary>
    /// Public lookup into <see cref="DefaultBlockTints"/> for callers that build
    /// their own meshes for a block outside <see cref="Build"/> (e.g. the schematic
    /// importer's per-voxel liquid mesher in <c>SpawnMenu</c>, which merges geometry
    /// directly into shared accumulators instead of going through this class).
    /// </summary>
    public static bool TryGetDefaultBlockTint(string blockName, out vec3 tint) =>
        DefaultBlockTints.TryGetValue(blockName, out tint);

    private enum BiomeTintKind
    {
        None,
        Grass,
        Foliage,
    }

    private static vec3 LoadDefaultColormapTint(string colormapFileName, vec3 fallback)
    {
        // Vanilla-style fallback uses the default climate sample (temp=0.8, downfall=0.4)
        // when colormap data is unavailable.
        try
        {
            string versionRoot = MinecraftDataLoader.GetVersionRoot();
            string colormapPath = Path.Combine(versionRoot, "textures", "colormap", colormapFileName);
            if (!File.Exists(colormapPath))
                return fallback;

            using var stream = File.OpenRead(colormapPath);
            ImageResult img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            if (img.Width <= 0 || img.Height <= 0 || img.Data.Length < 4)
                return fallback;

            const float temperature = 0.8f;
            const float downfall = 0.4f;

            int x = (int)MathF.Round((1f - Math.Clamp(temperature, 0f, 1f)) * (img.Width - 1));
            int y = (int)MathF.Round((1f - Math.Clamp(downfall * temperature, 0f, 1f)) * (img.Height - 1));

            x = Math.Clamp(x, 0, img.Width - 1);
            y = Math.Clamp(y, 0, img.Height - 1);

            int i = (y * img.Width + x) * 4;
            byte[] p = img.Data;
            return new vec3(p[i] / 255f, p[i + 1] / 255f, p[i + 2] / 255f);
        }
        catch
        {
            return fallback;
        }
    }

    private static vec3 GetBiomeTint(BiomeTintKind kind)
    {
        return kind switch
        {
            BiomeTintKind.Grass => DefaultGrassTint.Value,
            BiomeTintKind.Foliage => DefaultFoliageTint.Value,
            _ => new vec3(1f, 1f, 1f),
        };
    }

    private static bool TryGetFaceBiomeTint(string blockName, BlockModelFace face, string baseTextureKey, string resolvedTextureKey,
                                            out BiomeTintKind tintKind, out vec3 tint)
    {
        tintKind = BiomeTintKind.None;
        tint = new vec3(1f, 1f, 1f);

        if (face.TintIndex < 0)
            return false;

        if (!string.IsNullOrWhiteSpace(blockName) && NoBiomeTintBlocks.Contains(blockName))
            return false;

        // If a face declares tintindex, trust the model metadata and tint it.
        // Name inference decides grass vs foliage when possible; tintindex then
        // provides a deterministic fallback for unknown naming patterns.
        tintKind = InferBiomeTintKind(blockName, baseTextureKey, resolvedTextureKey, face.TintIndex);
        if (tintKind == BiomeTintKind.None)
            return false;

        tint = GetBiomeTint(tintKind);
        return true;
    }

    private static bool IsTextureGrayscale(string primaryKey, string secondaryKey)
    {
        if (!string.IsNullOrWhiteSpace(primaryKey) && GrayscaleTextureCache.TryGetValue(primaryKey, out bool cached))
            return cached;
        if (!string.IsNullOrWhiteSpace(secondaryKey) && GrayscaleTextureCache.TryGetValue(secondaryKey, out cached))
            return cached;

        byte[]? pixels = null;
        string cacheKey = !string.IsNullOrWhiteSpace(primaryKey) ? primaryKey : secondaryKey;

        if (!string.IsNullOrWhiteSpace(primaryKey) && TerrainAtlas.TilePixels.TryGetValue(primaryKey, out byte[]? primaryPixels))
            pixels = primaryPixels;
        else if (!string.IsNullOrWhiteSpace(secondaryKey) && TerrainAtlas.TilePixels.TryGetValue(secondaryKey, out byte[]? secondaryPixels))
            pixels = secondaryPixels;

        if (pixels == null)
        {
            if (!string.IsNullOrWhiteSpace(cacheKey))
                GrayscaleTextureCache[cacheKey] = false;
            return false;
        }

        bool sawVisible = false;
        bool grayscale = true;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a <= 8) continue;

            sawVisible = true;
            int r = pixels[i];
            int g = pixels[i + 1];
            int b = pixels[i + 2];
            if (Math.Abs(r - g) > 6 || Math.Abs(g - b) > 6 || Math.Abs(r - b) > 6)
            {
                grayscale = false;
                break;
            }
        }

        bool result = sawVisible && grayscale;
        if (!string.IsNullOrWhiteSpace(cacheKey))
            GrayscaleTextureCache[cacheKey] = result;

        return result;
    }

    private static BiomeTintKind InferBiomeTintKind(string blockName, string baseTextureKey, string resolvedTextureKey, int tintIndex = -1)
    {
        string[] parts =
        {
            blockName ?? string.Empty,
            baseTextureKey ?? string.Empty,
            resolvedTextureKey ?? string.Empty
        };

        foreach (string part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;

            if (part.Contains("pale_oak_leaves", StringComparison.OrdinalIgnoreCase))
                return BiomeTintKind.None;

            if (part.Contains("grass", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("fern", StringComparison.OrdinalIgnoreCase))
                return BiomeTintKind.Grass;

            if (part.Contains("leaves", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("leaf", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("vine", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("foliage", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("ivy", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("hedge", StringComparison.OrdinalIgnoreCase) ||
                part.Contains("canopy", StringComparison.OrdinalIgnoreCase))
                return BiomeTintKind.Foliage;
        }

        return InferBiomeTintKindFromTintIndex(tintIndex);
    }

    private static BiomeTintKind InferBiomeTintKindFromTintIndex(int tintIndex)
    {
        return tintIndex switch
        {
            1 => BiomeTintKind.Grass,
            2 => BiomeTintKind.Foliage,
            < 0 => BiomeTintKind.None,
            _ => BiomeTintKind.Foliage,
        };
    }

    /// <summary>
    /// Infers and returns a biome tint for a standalone texture key (for example
    /// floor/ground plane tiles) when that texture is grayscale and its name
    /// matches known grass/foliage patterns.
    /// </summary>
    public static bool TryGetBiomeTintForTextureKey(string textureKey, out vec3 tint)
    {
        tint = new vec3(1f, 1f, 1f);
        if (string.IsNullOrWhiteSpace(textureKey))
            return false;

        if (textureKey.Contains("pale_oak_leaves", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!IsTextureGrayscale(textureKey, textureKey))
            return false;

        BiomeTintKind kind = InferBiomeTintKind(textureKey, textureKey, textureKey);
        if (kind == BiomeTintKind.None)
            return false;

        tint = GetBiomeTint(kind);
        return true;
    }

    /// <summary>
    /// Builds one or more <see cref="Mesh"/> objects from a fully-resolved
    /// Minecraft block model. Returns an empty list when the model has no
    /// renderable geometry.
    /// </summary>
    /// <param name="gl">Active OpenGL context.</param>
    /// <param name="model">The resolved model (from <see cref="BlockRegistry.ResolveModel"/>).</param>
    /// <param name="variantRotX">Blockstate-level X rotation (degrees, 0/90/180/270).</param>
    /// <param name="variantRotY">Blockstate-level Y rotation (degrees, 0/90/180/270).</param>
    /// <param name="tileX">Number of times to repeat the block along +X (≥1).</param>
    /// <param name="tileY">Number of times to repeat the block along +Y (≥1).</param>
    /// <param name="tileZ">Number of times to repeat the block along +Z (≥1).</param>
    public static List<Mesh> Build(GL gl, ResolvedBlockModel model,
                                   int variantRotX = 0, int variantRotY = 0,
                                   string resourcePackId = "",
                                   string blockName = "",
                                   int tileX = 1, int tileY = 1, int tileZ = 1)
    {
        tileX = ClampTileCount(tileX);
        tileY = ClampTileCount(tileY);
        tileZ = ClampTileCount(tileZ);

        if (model.Elements.Count == 0)
        {
            // Model has no geometry elements (e.g. only references a builtin parent).
            // Try to produce a textured cube from whatever textures the model exposes.
            var fallbackMeshes = new List<Mesh>
            {
                BuildTiledFallbackCube(gl, model, blockNameHint: blockName,
                    resourcePackId: resourcePackId,
                    tileX: tileX, tileY: tileY, tileZ: tileZ)
            };
            ApplyDefaultBlockTint(fallbackMeshes, blockName);
            return fallbackMeshes;
        }

        // Group faces by texture key so each texture gets one draw call
        // key → (vertices, normals, texCoords)
        var groups = new Dictionary<string, (List<vec3> verts, List<vec3> norms, List<vec2> uvs, uint texId, BiomeTintKind tintKind, vec3 tint)>();

        // Blockstate variant rotation matrix (applied on top of element geometry)
        mat4 variantTransform = BuildVariantTransform(variantRotX, variantRotY);

        // Centered offsets so the tile group is anchored on the mesh origin.
        float centerX = (tileX - 1) * 0.5f;
        float centerY = (tileY - 1) * 0.5f;
        float centerZ = (tileZ - 1) * 0.5f;

        // Generate geometry per tile, culling internal faces between adjacent tiles.
        for (int tz = 0; tz < tileZ; tz++)
        for (int ty = 0; ty < tileY; ty++)
        for (int tx = 0; tx < tileX; tx++)
        {
            vec3 tileOffset = new vec3(tx - centerX, ty - centerY, tz - centerZ);
            foreach (var element in model.Elements)
            {
                AppendElement(element, model, groups, variantTransform, resourcePackId,
                              blockName, tileOffset, tx, ty, tz, tileX, tileY, tileZ);
            }
        }

        var result = new List<Mesh>();
        foreach (var kvp in groups)
        {
            var (verts, norms, uvs, texId, tintKind, tint) = kvp.Value;
            if (verts.Count == 0) continue;

            var mesh = new Mesh(gl);
            mesh.Vertices.AddRange(verts);
            mesh.Normals.AddRange(norms);
            mesh.TexCoords.AddRange(uvs);
            mesh.TextureId   = texId;
            mesh.DoubleSided = false;
            if (tintKind != BiomeTintKind.None)
                mesh.Albedo = tint;

            // If this texture has animation data, wire up the animation key
            string texKey = kvp.Key;
            if (TerrainAtlas.AnimatedTextures.ContainsKey(texKey))
                mesh.AnimationKey = texKey;

            // Route true alpha-blend textures (water) through the depth-tested,
            // depth-write-off blend pass instead of the cutout/opaque path — see
            // Mesh.IsTranslucent.
            mesh.IsTranslucent = TerrainAtlas.IsTextureTranslucent(texKey);

            mesh.Upload();
            result.Add(mesh);
        }

        if (result.Count == 0)
        {
            var fallbackMeshes = new List<Mesh>
            {
                BuildTiledFallbackCube(gl, model, blockNameHint: blockName,
                    resourcePackId: resourcePackId,
                    tileX: tileX, tileY: tileY, tileZ: tileZ)
            };
            ApplyDefaultBlockTint(fallbackMeshes, blockName);
            return fallbackMeshes;
        }

        ApplyDefaultBlockTint(result, blockName);
        return result;
    }

    private static int ClampTileCount(int value)
    {
        return value switch
        {
            < 1 => 1,
            > SceneObject.MaxTilesPerAxis => SceneObject.MaxTilesPerAxis,
            _ => value
        };
    }

    /// <summary>
    /// Returns true when a face is on the boundary between this tile and an
    /// adjacent tile (i.e. it's an internal face and should be culled).
    /// Only non-rotated elements that span the full block extent are culled,
    /// since rotated or partial elements aren't guaranteed to sit on a tile
    /// boundary.
    /// </summary>
    private static bool IsInternalFace(string faceName, BlockModelElement element,
                                       int tx, int ty, int tz,
                                       int tileX, int tileY, int tileZ)
    {
        if (element.Rotation != null) return false;

        return faceName switch
        {
            "down"  => element.From[1] == 0f  && ty > 0,
            "up"    => element.To[1]   == 16f && ty + 1 < tileY,
            "north" => element.From[2] == 0f  && tz > 0,
            "south" => element.To[2]   == 16f && tz + 1 < tileZ,
            "west"  => element.From[0] == 0f  && tx > 0,
            "east"  => element.To[0]   == 16f && tx + 1 < tileX,
            _       => false
        };
    }

    /// <summary>
    /// Builds a textured fallback unit cube when a model has no elements or is null.
    /// Picks the first resolvable texture from the model's texture map, trying
    /// common slot names in order.  If <paramref name="blockNameHint"/> is provided,
    /// also tries direct TerrainAtlas key lookups by the block name.
    /// Falls back to an untextured white cube if nothing is found.
    /// </summary>
    public static Mesh BuildTexturedFallbackCube(GL gl, ResolvedBlockModel? model,
                                                 string? blockNameHint = null,
                                                 string resourcePackId = "",
                                                 int tileX = 1, int tileY = 1, int tileZ = 1)
    {
        return BuildTiledFallbackCube(gl, model, blockNameHint, resourcePackId, tileX, tileY, tileZ);
    }

    /// <summary>
    /// Tiled fallback cube generator. Emits only the externally-visible faces
    /// of the tile group, so a 100×100×100 fallback only contains the ~6N²
    /// shell faces instead of 6N³ interior faces.
    /// </summary>
    private static Mesh BuildTiledFallbackCube(GL gl, ResolvedBlockModel? model,
                                               string? blockNameHint,
                                               string resourcePackId,
                                               int tileX, int tileY, int tileZ)
    {
        tileX = ClampTileCount(tileX);
        tileY = ClampTileCount(tileY);
        tileZ = ClampTileCount(tileZ);

        uint texId = PickFallbackTexture(model, blockNameHint, resourcePackId);
        if (texId == 0)
            return BuildTiledUntexturedCube(gl, tileX, tileY, tileZ);

        var mesh = new Mesh(gl);
        mesh.TextureId = texId;
        mesh.DoubleSided = false;

        // Rare fallback path — a linear reverse lookup is fine here (see Mesh.IsTranslucent).
        string? texKeyForTranslucency = TerrainAtlas.Textures.FirstOrDefault(kv => kv.Value == texId).Key;
        if (texKeyForTranslucency != null)
            mesh.IsTranslucent = TerrainAtlas.IsTextureTranslucent(texKeyForTranslucency);

        float centerX = (tileX - 1) * 0.5f;
        float centerY = (tileY - 1) * 0.5f;
        float centerZ = (tileZ - 1) * 0.5f;

        var uvCorners = new vec2[]
        {
            new vec2(0, 0), new vec2(1, 0), new vec2(1, 1), new vec2(0, 1)
        };

        for (int tz = 0; tz < tileZ; tz++)
        for (int ty = 0; ty < tileY; ty++)
        for (int tx = 0; tx < tileX; tx++)
        {
            vec3 offset = new vec3(tx - centerX, ty - centerY, tz - centerZ);

            AppendCubeFaceIfExternal(mesh, "down",  offset, ty > 0,         uvCorners);
            AppendCubeFaceIfExternal(mesh, "up",    offset, ty + 1 < tileY, uvCorners);
            AppendCubeFaceIfExternal(mesh, "north", offset, tz > 0,         uvCorners);
            AppendCubeFaceIfExternal(mesh, "south", offset, tz + 1 < tileZ, uvCorners);
            AppendCubeFaceIfExternal(mesh, "west",  offset, tx > 0,         uvCorners);
            AppendCubeFaceIfExternal(mesh, "east",  offset, tx + 1 < tileX, uvCorners);
        }

        mesh.Upload();
        return mesh;
    }

    private static void AppendCubeFaceIfExternal(Mesh mesh, string faceName, vec3 offset,
                                                 bool hasNeighbor, vec2[] uvCorners)
    {
        if (hasNeighbor) return;

        var (x0, y0, z0, x1, y1, z1) = faceName switch
        {
            "down"  => (-0.5f, -0.5f, -0.5f,  0.5f, -0.5f,  0.5f),
            "up"    => (-0.5f,  0.5f, -0.5f,  0.5f,  0.5f,  0.5f),
            "north" => (-0.5f, -0.5f, -0.5f,  0.5f,  0.5f, -0.5f),
            "south" => (-0.5f, -0.5f,  0.5f,  0.5f,  0.5f,  0.5f),
            "west"  => (-0.5f, -0.5f, -0.5f, -0.5f,  0.5f,  0.5f),
            "east"  => ( 0.5f, -0.5f, -0.5f,  0.5f,  0.5f,  0.5f),
            _       => (0f, 0f, 0f, 0f, 0f, 0f)
        };

        (vec3 v0, vec3 v1, vec3 v2, vec3 v3) = FaceQuad(faceName, x0, y0, z0, x1, y1, z1);
        vec3 n = FaceNormal(faceName);
        v0 += offset; v1 += offset; v2 += offset; v3 += offset;

            if (faceName is "east" or "up" or "south" or "west" or "down")
            {
                // Positive-direction faces: (V0,V3,V2) and (V0,V2,V1) — CCW
                mesh.Vertices.Add(v0); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[0]);
                mesh.Vertices.Add(v3); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[3]);
                mesh.Vertices.Add(v2); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[2]);
                mesh.Vertices.Add(v0); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[0]);
                mesh.Vertices.Add(v2); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[2]);
                mesh.Vertices.Add(v1); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[1]);
            }
            else
            {
                // Negative-direction faces: (V0,V1,V2) and (V0,V2,V3) — CCW
                mesh.Vertices.Add(v0); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[0]);
                mesh.Vertices.Add(v1); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[1]);
                mesh.Vertices.Add(v2); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[2]);
                mesh.Vertices.Add(v0); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[0]);
                mesh.Vertices.Add(v2); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[2]);
                mesh.Vertices.Add(v3); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[3]);
            }
    }

    private static uint PickFallbackTexture(ResolvedBlockModel? model, string? blockNameHint, string resourcePackId)
    {
        uint texId = 0;

        if (model != null)
        {
            // Priority order for common all-sides texture slots
            string[] preferredSlots = { "all", "side", "texture", "north", "top", "bottom", "east", "west", "south", "down", "up" };
            foreach (string slot in preferredSlots)
            {
                if (model.Textures.ContainsKey(slot))
                {
                    string? key = BlockRegistry.ResolveTextureKey(model, "#" + slot);
                    if (key != null)
                    {
                        string resolvedKey = ResolveTextureKeyForPack(key, resourcePackId);
                        if (TerrainAtlas.Textures.TryGetValue(resolvedKey, out uint t))
                        {
                            texId = t;
                            break;
                        }
                    }
                }
            }

            // Last resort: use whatever the first resolvable texture is
            if (texId == 0)
            {
                foreach (var resolvedKey in model.Textures.Select(kvp => BlockRegistry.ResolveTextureKey(model, "#" + kvp.Key)).OfType<string>().Select(key => ResolveTextureKeyForPack(key, resourcePackId)))
                {
                    if (TerrainAtlas.Textures.TryGetValue(resolvedKey, out uint t))
                    {
                        texId = t;
                        break;
                    }
                }
            }
        }

        // If still no texture and we have a block name hint, try direct atlas lookups
        if (texId == 0 && !string.IsNullOrEmpty(blockNameHint))
        {
            // Try exact match, then common suffixes
            string[] candidates = {
                blockNameHint,
                blockNameHint + "_side",
                blockNameHint + "_top",
                blockNameHint + "_front"
            };
            foreach (string candidate in candidates)
            {
                string resolvedCandidate = ResolveTextureKeyForPack(candidate, resourcePackId);
                if (TerrainAtlas.Textures.TryGetValue(resolvedCandidate, out uint t) ||
                    TerrainAtlas.Textures.TryGetValue(candidate, out t))
                {
                    texId = t;
                    break;
                }
            }
        }

        return texId;
    }

    /// <summary>
    /// Builds a tiled untextured cube (white material) with internal face culling.
    /// Used when no texture is available for a fallback.
    /// </summary>
    private static Mesh BuildTiledUntexturedCube(GL gl, int tileX, int tileY, int tileZ)
    {
        var cube = new CubeMesh(gl);
        // CubeMesh already generates a single unit cube; we need a tiled version.
        // For simplicity and correctness, discard the single cube and build per-tile.
        cube.Dispose();

        var mesh = new Mesh(gl);
        mesh.DoubleSided = false;

        float centerX = (tileX - 1) * 0.5f;
        float centerY = (tileY - 1) * 0.5f;
        float centerZ = (tileZ - 1) * 0.5f;

        for (int tz = 0; tz < tileZ; tz++)
        for (int ty = 0; ty < tileY; ty++)
        for (int tx = 0; tx < tileX; tx++)
        {
            vec3 offset = new vec3(tx - centerX, ty - centerY, tz - centerZ);
            // CubeMesh face vertex layout (matches FaceQuad winding for the same faces)
            var faces = new (string name, bool skip, vec3 v0, vec3 v1, vec3 v2, vec3 v3, vec3 n)[]
            {
                ("down",  ty > 0,         new vec3(-0.5f, -0.5f, -0.5f), new vec3( 0.5f, -0.5f, -0.5f), new vec3( 0.5f, -0.5f,  0.5f), new vec3(-0.5f, -0.5f,  0.5f), new vec3( 0, -1,  0)),
                ("up",    ty + 1 < tileY, new vec3(-0.5f,  0.5f, -0.5f), new vec3( 0.5f,  0.5f, -0.5f), new vec3( 0.5f,  0.5f,  0.5f), new vec3(-0.5f,  0.5f,  0.5f), new vec3( 0,  1,  0)),
                ("north", tz > 0,         new vec3(-0.5f,  0.5f, -0.5f), new vec3( 0.5f,  0.5f, -0.5f), new vec3( 0.5f, -0.5f, -0.5f), new vec3(-0.5f, -0.5f, -0.5f), new vec3( 0,  0, -1)),
                ("south", tz + 1 < tileZ, new vec3(-0.5f,  0.5f,  0.5f), new vec3( 0.5f,  0.5f,  0.5f), new vec3( 0.5f, -0.5f,  0.5f), new vec3(-0.5f, -0.5f,  0.5f), new vec3( 0,  0,  1)),
                ("west",  tx > 0,         new vec3(-0.5f,  0.5f, -0.5f), new vec3(-0.5f,  0.5f,  0.5f), new vec3(-0.5f, -0.5f,  0.5f), new vec3(-0.5f, -0.5f, -0.5f), new vec3(-1,  0,  0)),
                ("east",  tx + 1 < tileX, new vec3( 0.5f,  0.5f,  0.5f), new vec3( 0.5f,  0.5f, -0.5f), new vec3( 0.5f, -0.5f, -0.5f), new vec3( 0.5f, -0.5f,  0.5f), new vec3( 1,  0,  0)),
            };

            foreach (var (name, skip, v0, v1, v2, v3, n) in faces)
            {
                if (skip) continue;
                // Tri 0: v0(TL), v3(BL), v2(BR) — CCW
                mesh.Vertices.Add(v0 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
                mesh.Vertices.Add(v3 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
                mesh.Vertices.Add(v2 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
                // Tri 1: v0(TL), v2(BR), v1(TR) — CCW
                mesh.Vertices.Add(v0 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
                mesh.Vertices.Add(v2 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
                mesh.Vertices.Add(v1 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
            }
        }

        mesh.Upload();
        return mesh;
    }

    // Same vertex order as QuadForFace but using direct world-space coords.
    private static (vec3, vec3, vec3, vec3) FaceQuad(string face,
        float x0, float y0, float z0, float x1, float y1, float z1)
    {
        return face switch
        {
            "down"  => (new vec3(x0, y0, z0), new vec3(x1, y0, z0), new vec3(x1, y0, z1), new vec3(x0, y0, z1)),
            "up"    => (new vec3(x0, y1, z0), new vec3(x1, y1, z0), new vec3(x1, y1, z1), new vec3(x0, y1, z1)),
            "north" => (new vec3(x0, y1, z0), new vec3(x1, y1, z0), new vec3(x1, y0, z0), new vec3(x0, y0, z0)),
            "south" => (new vec3(x0, y1, z1), new vec3(x1, y1, z1), new vec3(x1, y0, z1), new vec3(x0, y0, z1)),
            "west"  => (new vec3(x0, y1, z0), new vec3(x0, y1, z1), new vec3(x0, y0, z1), new vec3(x0, y0, z0)),
            "east"  => (new vec3(x1, y1, z1), new vec3(x1, y1, z0), new vec3(x1, y0, z0), new vec3(x1, y0, z1)),
            _       => (vec3.Zero, vec3.Zero, vec3.Zero, vec3.Zero)
        };
    }

    private static vec3 FaceNormal(string face) => face switch
    {
        "down"  => new vec3( 0, -1,  0),
        "up"    => new vec3( 0,  1,  0),
        "north" => new vec3( 0,  0, -1),
        "south" => new vec3( 0,  0,  1),
        "west"  => new vec3(-1,  0,  0),
        "east"  => new vec3( 1,  0,  0),
        _       => vec3.UnitY
    };

    private static string ResolveTextureKeyForPack(string baseTextureKey, string resourcePackId)
    {
        string normalizedPackId = MinecraftDataLoader.NormalizeResourcePackId(resourcePackId);
        if (string.IsNullOrWhiteSpace(normalizedPackId))
            return baseTextureKey;

        string namespaced = MinecraftDataLoader.BuildResourcePackTextureKeyFromId(normalizedPackId, baseTextureKey);
        return TerrainAtlas.Textures.ContainsKey(namespaced) ? namespaced : baseTextureKey;
    }

    // ── Element geometry builder ──────────────────────────────────────────────

    private static void AppendElement(
        BlockModelElement element,
        ResolvedBlockModel model,
        Dictionary<string, (List<vec3>, List<vec3>, List<vec2>, uint, BiomeTintKind, vec3)> groups,
        mat4 variantTransform,
        string resourcePackId,
        string blockName,
        vec3 tileOffset,
        int tx, int ty, int tz,
        int tileX, int tileY, int tileZ)
    {
        float x0 = element.From[0] * Scale - 0.5f;
        float y0 = element.From[1] * Scale - 0.5f;
        float z0 = element.From[2] * Scale - 0.5f;
        float x1 = element.To[0]   * Scale - 0.5f;
        float y1 = element.To[1]   * Scale - 0.5f;
        float z1 = element.To[2]   * Scale - 0.5f;

        // Optional element-level rotation
        mat4 elementTransform = mat4.Identity;
        if (element.Rotation != null)
            elementTransform = BuildElementRotation(element.Rotation);

        // Combined transform: element rotation first, then variant rotation
        mat4 transform = variantTransform * elementTransform;

        foreach (var (faceName, face) in element.Faces)
        {
            // Skip internal faces between adjacent tiles
            if (IsInternalFace(faceName, element, tx, ty, tz, tileX, tileY, tileZ))
                continue;

            // Resolve texture
            string? texKey = BlockRegistry.ResolveTextureKey(model, face.Texture);
            if (texKey == null)
            {
            // texture ref missing — skip this face
                continue;
            }

            string resolvedTexKey = ResolveTextureKeyForPack(texKey, resourcePackId);
            uint texId;
            CtmResolvedTile? ctmTile = CtmResolver.Resolve(blockName, texKey, faceName,
                                                         tx, ty, tz, tileX, tileY, tileZ,
                                                         resourcePackId);

            if (ctmTile != null)
            {
                texId = ctmTile.TextureId;
            }
            else
            {
                texId = TerrainAtlas.Textures.TryGetValue(resolvedTexKey, out uint t)
                    ? t
                    : TerrainAtlas.Textures.TryGetValue(texKey, out t) ? t : 0;
            }

            bool hasFaceTint = TryGetFaceBiomeTint(blockName, face, texKey, resolvedTexKey, out BiomeTintKind tintKind, out vec3 faceTint);

            string groupKey = ctmTile != null
                ? $"ctm:{ctmTile.TextureId}:{ctmTile.TileIndex}:{(hasFaceTint ? tintKind.ToString() : "none")}"
                : $"{resolvedTexKey}:{(hasFaceTint ? tintKind.ToString() : "none")}";
            if (!groups.TryGetValue(groupKey, out var group))
            {
                group = ([], [], [], texId, hasFaceTint ? tintKind : BiomeTintKind.None, hasFaceTint ? faceTint : new vec3(1f, 1f, 1f));
                groups[groupKey] = group;
            }

            var (verts, norms, uvs, _, _, _) = group;

            // Determine UV from face data or derive from face extents
            float uMin, vMin, uMax, vMax;
            if (face.Uv != null)
            {
                uMin = face.Uv[0] / 16f;
                vMin = face.Uv[1] / 16f;
                uMax = face.Uv[2] / 16f;
                vMax = face.Uv[3] / 16f;
            }
            else
            {
                (uMin, vMin, uMax, vMax) = DefaultUvForFace(faceName, element);
            }

            // Remap UV into CTM tile sub-region if a CTM tile is being used
            if (ctmTile != null)
            {
                uMin = ctmTile.UMin + uMin * (ctmTile.UMax - ctmTile.UMin);
                uMax = ctmTile.UMin + uMax * (ctmTile.UMax - ctmTile.UMin);
                vMin = ctmTile.VMin + vMin * (ctmTile.VMax - ctmTile.VMin);
                vMax = ctmTile.VMin + vMax * (ctmTile.VMax - ctmTile.VMin);
            }

            // Apply face UV rotation
            var (ru0, rv0, ru1, rv1, ru2, rv2, ru3, rv3) =
                RotateUv(face.Rotation, uMin, vMin, uMax, vMax);

            // Build quad vertices + normals + UVs for this face
            (vec3 v0, vec3 v1, vec3 v2, vec3 v3) = QuadForFace(faceName, x0, y0, z0, x1, y1, z1);
            vec3 normal = NormalForFace(faceName);

            // Apply transform to vertices and normals, then add tile offset
            v0 = TransformPoint(transform, v0) + tileOffset;
            v1 = TransformPoint(transform, v1) + tileOffset;
            v2 = TransformPoint(transform, v2) + tileOffset;
            v3 = TransformPoint(transform, v3) + tileOffset;
            normal = TransformNormal(transform, normal);

            if (faceName is "east" or "up" or "south" or "west" or "down")
            {
                // Positive-direction faces: (V0,V3,V2) and (V0,V2,V1) — CCW
                verts.Add(v0); norms.Add(normal); uvs.Add(new vec2(ru0, rv0));
                verts.Add(v3); norms.Add(normal); uvs.Add(new vec2(ru3, rv3));
                verts.Add(v2); norms.Add(normal); uvs.Add(new vec2(ru2, rv2));
                verts.Add(v0); norms.Add(normal); uvs.Add(new vec2(ru0, rv0));
                verts.Add(v2); norms.Add(normal); uvs.Add(new vec2(ru2, rv2));
                verts.Add(v1); norms.Add(normal); uvs.Add(new vec2(ru1, rv1));
            }
            else
            {
                // Negative-direction faces: (V0,V1,V2) and (V0,V2,V3) — CCW
                verts.Add(v0); norms.Add(normal); uvs.Add(new vec2(ru0, rv0));
                verts.Add(v1); norms.Add(normal); uvs.Add(new vec2(ru1, rv1));
                verts.Add(v2); norms.Add(normal); uvs.Add(new vec2(ru2, rv2));
                verts.Add(v0); norms.Add(normal); uvs.Add(new vec2(ru0, rv0));
                verts.Add(v2); norms.Add(normal); uvs.Add(new vec2(ru2, rv2));
                verts.Add(v3); norms.Add(normal); uvs.Add(new vec2(ru3, rv3));
            }
        }
    }

    // ── Face geometry helpers ─────────────────────────────────────────────────

    private static (vec3, vec3, vec3, vec3) QuadForFace(
        string face, float x0, float y0, float z0, float x1, float y1, float z1)
    {
        // Vertex order: V0=TL, V1=TR, V2=BR, V3=BL when viewed from outside.
        // West and down use Mojang's original vertex order (V0=BL for up/down,
        // V0↔V1, V2↔V3 for west); up uses the standard V0=TL convention.
        // Down/north use negative-face winding; west uses positive-face winding.
        return face switch
        {
            "down"  => (new vec3(x0, y0, z1), new vec3(x1, y0, z1), new vec3(x1, y0, z0), new vec3(x0, y0, z0)),
            "up"    => (new vec3(x0, y1, z0), new vec3(x1, y1, z0), new vec3(x1, y1, z1), new vec3(x0, y1, z1)),
            "north" => (new vec3(x0, y1, z0), new vec3(x1, y1, z0), new vec3(x1, y0, z0), new vec3(x0, y0, z0)),
            "south" => (new vec3(x0, y1, z1), new vec3(x1, y1, z1), new vec3(x1, y0, z1), new vec3(x0, y0, z1)),
            "west"  => (new vec3(x0, y1, z0), new vec3(x0, y1, z1), new vec3(x0, y0, z1), new vec3(x0, y0, z0)),
            "east"  => (new vec3(x1, y1, z1), new vec3(x1, y1, z0), new vec3(x1, y0, z0), new vec3(x1, y0, z1)),
            _       => (vec3.Zero, vec3.Zero, vec3.Zero, vec3.Zero)
        };
    }

    private static vec3 NormalForFace(string face)
    {
        return face switch
        {
            "down"  => new vec3( 0, -1,  0),
            "up"    => new vec3( 0,  1,  0),
            "north" => new vec3( 0,  0, -1),
            "south" => new vec3( 0,  0,  1),
            "west"  => new vec3(-1,  0,  0),
            "east"  => new vec3( 1,  0,  0),
            _       => vec3.UnitY
        };
    }

    /// <summary>
    /// Derives default UV coordinates from the element extents when the face
    /// JSON does not supply explicit UV values.
    /// </summary>
    private static (float uMin, float vMin, float uMax, float vMax) DefaultUvForFace(
        string face, BlockModelElement el)
    {
        float x0 = el.From[0]; float x1 = el.To[0];
        float y0 = el.From[1]; float y1 = el.To[1];
        float z0 = el.From[2]; float z1 = el.To[2];

        return face switch
        {
            "down"  => (x0 / 16f, z0 / 16f, x1 / 16f, z1 / 16f),
            "up"    => (x0 / 16f, z1 / 16f, x1 / 16f, z0 / 16f),
            "north" => (x1 / 16f, (16f - y1) / 16f, x0 / 16f, (16f - y0) / 16f),
            "south" => (x0 / 16f, (16f - y1) / 16f, x1 / 16f, (16f - y0) / 16f),
            "west"  => (z0 / 16f, (16f - y1) / 16f, z1 / 16f, (16f - y0) / 16f),
            "east"  => (z1 / 16f, (16f - y1) / 16f, z0 / 16f, (16f - y0) / 16f),
            _       => (0f, 0f, 1f, 1f)
        };
    }

    // ── UV rotation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a UV rotation (0/90/180/270 degrees) to the four corner UVs of a
    /// quad by remapping which corner of the original UV rectangle each vertex
    /// receives.
    ///
    /// All quad faces use V0=TL, V1=TR, V2=BR, V3=BL (CCW from outside).
    /// A clockwise rotation of N degrees maps to a backward shift of N/90 steps
    /// through the corner array: step = (4 - rotation/90) % 4.
    /// </summary>
    private static (float, float, float, float, float, float, float, float)
        RotateUv(int rotation, float uMin, float vMin, float uMax, float vMax)
    {
        var corners = new[]
        {
            (uMin, vMin), // 0 = TL
            (uMax, vMin), // 1 = TR
            (uMax, vMax), // 2 = BR
            (uMin, vMax)  // 3 = BL
        };

        int step = (4 - (rotation / 90) % 4) % 4;

        var (ru0, rv0) = corners[(step + 0) % 4];
        var (ru1, rv1) = corners[(step + 1) % 4];
        var (ru2, rv2) = corners[(step + 2) % 4];
        var (ru3, rv3) = corners[(step + 3) % 4];

        return (ru0, rv0, ru1, rv1, ru2, rv2, ru3, rv3);
    }

    // ── Element rotation ──────────────────────────────────────────────────────

    private static mat4 BuildElementRotation(ElementRotation rot)
    {
        float ox = rot.Origin[0] * Scale - 0.5f;
        float oy = rot.Origin[1] * Scale - 0.5f;
        float oz = rot.Origin[2] * Scale - 0.5f;

        float rad = rot.Angle * MathF.PI / 180f;

        mat4 toOrigin   = mat4.Translate(new vec3(-ox, -oy, -oz));
        mat4 fromOrigin = mat4.Translate(new vec3( ox,  oy,  oz));

        mat4 rotation = rot.Axis.ToLower() switch
        {
            "x" => mat4.RotateX(rad),
            "y" => mat4.RotateY(rad),
            "z" => mat4.RotateZ(rad),
            _   => mat4.Identity
        };

        // If rescale, we'd normally scale the non-rotating axes to compensate for the
        // diagonal distortion, but this is a minor visual detail we skip for simplicity.
        return fromOrigin * rotation * toOrigin;
    }

    // ── Variant transform ─────────────────────────────────────────────────────

    private static mat4 BuildVariantTransform(int rotX, int rotY)
    {
        if (rotX == 0 && rotY == 0) return mat4.Identity;

        float radX = rotX * MathF.PI / 180f;
        float radY = rotY * MathF.PI / 180f;

        // Block model convention: X rotation = clockwise when looking from +X,
        // which is the opposite of standard math (right-hand rule). Y rotation
        // uses the same direction as standard math.
        mat4 rx = mat4.RotateX(-radX);
        mat4 ry = mat4.RotateY(radY);
        return ry * rx;
    }

    // ── Transform helpers ─────────────────────────────────────────────────────

    private static vec3 TransformPoint(mat4 m, vec3 p)
    {
        vec4 t = m * new vec4(p, 1f);
        return new vec3(t.x, t.y, t.z);
    }

    private static vec3 TransformNormal(mat4 m, vec3 n)
    {
        // Use the upper-left 3×3 (no translation) for normals
        vec4 t = m * new vec4(n, 0f);
        var  r = new vec3(t.x, t.y, t.z);
        return r.LengthSqr > 0 ? r.Normalized : vec3.UnitY;
    }
}
