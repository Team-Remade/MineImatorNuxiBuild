using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

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
            return new List<Mesh>
            {
                BuildTiledFallbackCube(gl, model, blockNameHint: blockName,
                                       resourcePackId: resourcePackId,
                                       tileX: tileX, tileY: tileY, tileZ: tileZ)
            };
        }

        // Group faces by texture key so each texture gets one draw call
        // key → (vertices, normals, texCoords)
        var groups = new Dictionary<string, (List<vec3> verts, List<vec3> norms, List<vec2> uvs, uint texId)>();

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
            var (verts, norms, uvs, texId) = kvp.Value;
            if (verts.Count == 0) continue;

            var mesh = new Mesh(gl);
            mesh.Vertices.AddRange(verts);
            mesh.Normals.AddRange(norms);
            mesh.TexCoords.AddRange(uvs);
            mesh.TextureId   = texId;
            mesh.DoubleSided = false;

            // If this texture has animation data, wire up the animation key
            string texKey = kvp.Key;
            if (TerrainAtlas.AnimatedTextures.ContainsKey(texKey))
                mesh.AnimationKey = texKey;

            mesh.Upload();
            result.Add(mesh);
        }

        if (result.Count == 0)
        {
            return new List<Mesh>
            {
                BuildTiledFallbackCube(gl, model, blockNameHint: blockName,
                                       resourcePackId: resourcePackId,
                                       tileX: tileX, tileY: tileY, tileZ: tileZ)
            };
        }

        return result;
    }

    private static int ClampTileCount(int value)
    {
        if (value < 1) return 1;
        if (value > SceneObject.MaxTilesPerAxis) return SceneObject.MaxTilesPerAxis;
        return value;
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

        // Tri 0
        mesh.Vertices.Add(v0); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[0]);
        mesh.Vertices.Add(v1); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[1]);
        mesh.Vertices.Add(v2); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[2]);
        // Tri 1
        mesh.Vertices.Add(v0); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[0]);
        mesh.Vertices.Add(v2); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[2]);
        mesh.Vertices.Add(v3); mesh.Normals.Add(n); mesh.TexCoords.Add(uvCorners[3]);
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
                foreach (var kvp in model.Textures)
                {
                    string? key = BlockRegistry.ResolveTextureKey(model, "#" + kvp.Key);
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
                ("up",    ty + 1 < tileY, new vec3(-0.5f,  0.5f,  0.5f), new vec3( 0.5f,  0.5f,  0.5f), new vec3( 0.5f,  0.5f, -0.5f), new vec3(-0.5f,  0.5f, -0.5f), new vec3( 0,  1,  0)),
                ("north", tz > 0,         new vec3(-0.5f,  0.5f, -0.5f), new vec3( 0.5f,  0.5f, -0.5f), new vec3( 0.5f, -0.5f, -0.5f), new vec3(-0.5f, -0.5f, -0.5f), new vec3( 0,  0, -1)),
                ("south", tz + 1 < tileZ, new vec3( 0.5f,  0.5f,  0.5f), new vec3(-0.5f,  0.5f,  0.5f), new vec3(-0.5f, -0.5f,  0.5f), new vec3( 0.5f, -0.5f,  0.5f), new vec3( 0,  0,  1)),
                ("west",  tx > 0,         new vec3(-0.5f,  0.5f,  0.5f), new vec3(-0.5f,  0.5f, -0.5f), new vec3(-0.5f, -0.5f, -0.5f), new vec3(-0.5f, -0.5f,  0.5f), new vec3(-1,  0,  0)),
                ("east",  tx + 1 < tileX, new vec3( 0.5f,  0.5f, -0.5f), new vec3( 0.5f,  0.5f,  0.5f), new vec3( 0.5f, -0.5f,  0.5f), new vec3( 0.5f, -0.5f, -0.5f), new vec3( 1,  0,  0)),
            };

            foreach (var (name, skip, v0, v1, v2, v3, n) in faces)
            {
                if (skip) continue;
                mesh.Vertices.Add(v0 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
                mesh.Vertices.Add(v1 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
                mesh.Vertices.Add(v2 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
                mesh.Vertices.Add(v0 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
                mesh.Vertices.Add(v2 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
                mesh.Vertices.Add(v3 + offset); mesh.Normals.Add(n); mesh.TexCoords.Add(vec2.Zero);
            }
        }

        mesh.Upload();
        return mesh;
    }

    // Same winding as QuadForFace but using direct world-space coords
    private static (vec3, vec3, vec3, vec3) FaceQuad(string face,
        float x0, float y0, float z0, float x1, float y1, float z1)
    {
        return face switch
        {
            "down"  => (new vec3(x0, y0, z0), new vec3(x1, y0, z0), new vec3(x1, y0, z1), new vec3(x0, y0, z1)),
            "up"    => (new vec3(x0, y1, z1), new vec3(x1, y1, z1), new vec3(x1, y1, z0), new vec3(x0, y1, z0)),
            "north" => (new vec3(x0, y1, z0), new vec3(x1, y1, z0), new vec3(x1, y0, z0), new vec3(x0, y0, z0)),
            "south" => (new vec3(x1, y1, z1), new vec3(x0, y1, z1), new vec3(x0, y0, z1), new vec3(x1, y0, z1)),
            "west"  => (new vec3(x0, y1, z1), new vec3(x0, y1, z0), new vec3(x0, y0, z0), new vec3(x0, y0, z1)),
            "east"  => (new vec3(x1, y1, z0), new vec3(x1, y1, z1), new vec3(x1, y0, z1), new vec3(x1, y0, z0)),
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
        Dictionary<string, (List<vec3>, List<vec3>, List<vec2>, uint)> groups,
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
            if (texKey == null) continue;

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

            string groupKey = ctmTile != null
                ? $"ctm:{ctmTile.TextureId}:{ctmTile.TileIndex}"
                : resolvedTexKey;
            if (!groups.TryGetValue(groupKey, out var group))
            {
                group = (new List<vec3>(), new List<vec3>(), new List<vec2>(), texId);
                groups[groupKey] = group;
            }

            var (verts, norms, uvs, _) = group;

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

            // Tri 0: v0, v1, v2
            verts.Add(v0); norms.Add(normal); uvs.Add(new vec2(ru0, rv0));
            verts.Add(v1); norms.Add(normal); uvs.Add(new vec2(ru1, rv1));
            verts.Add(v2); norms.Add(normal); uvs.Add(new vec2(ru2, rv2));
            // Tri 1: v0, v2, v3
            verts.Add(v0); norms.Add(normal); uvs.Add(new vec2(ru0, rv0));
            verts.Add(v2); norms.Add(normal); uvs.Add(new vec2(ru2, rv2));
            verts.Add(v3); norms.Add(normal); uvs.Add(new vec2(ru3, rv3));
        }
    }

    // ── Face geometry helpers ─────────────────────────────────────────────────

    private static (vec3, vec3, vec3, vec3) QuadForFace(
        string face, float x0, float y0, float z0, float x1, float y1, float z1)
    {
        // Vertex order is CCW when viewed from the outside (front face),
        // matching OpenGL's default front-face winding convention.
        return face switch
        {
            "down"  => (new vec3(x0, y0, z0), new vec3(x1, y0, z0), new vec3(x1, y0, z1), new vec3(x0, y0, z1)),
            "up"    => (new vec3(x0, y1, z1), new vec3(x1, y1, z1), new vec3(x1, y1, z0), new vec3(x0, y1, z0)),
            "north" => (new vec3(x0, y1, z0), new vec3(x1, y1, z0), new vec3(x1, y0, z0), new vec3(x0, y0, z0)),
            "south" => (new vec3(x1, y1, z1), new vec3(x0, y1, z1), new vec3(x0, y0, z1), new vec3(x1, y0, z1)),
            "west"  => (new vec3(x0, y1, z1), new vec3(x0, y1, z0), new vec3(x0, y0, z0), new vec3(x0, y0, z1)),
            "east"  => (new vec3(x1, y1, z0), new vec3(x1, y1, z1), new vec3(x1, y0, z1), new vec3(x1, y0, z0)),
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
    /// Applies a UV rotation (0/90/180/270 degrees clockwise) to the four
    /// corner UVs of a quad.
    ///
    /// Minecraft face UV rotation rotates the texture clockwise within the face.
    /// The quad corner order is: v0=TL, v1=TR, v2=BR, v3=BL.
    /// Each 90° CW rotation shifts which texture corner maps to TL:
    ///   0°  → TL=(uMin,vMin)  TR=(uMax,vMin)  BR=(uMax,vMax)  BL=(uMin,vMax)
    ///   90° → TL=(uMin,vMax)  TR=(uMin,vMin)  BR=(uMax,vMin)  BL=(uMax,vMax)
    ///  180° → TL=(uMax,vMax)  TR=(uMin,vMax)  BR=(uMin,vMin)  BL=(uMax,vMin)
    ///  270° → TL=(uMax,vMin)  TR=(uMax,vMax)  BR=(uMin,vMax)  BL=(uMin,vMin)
    /// </summary>
    private static (float, float, float, float, float, float, float, float)
        RotateUv(int rotation, float uMin, float vMin, float uMax, float vMax)
    {
        // Corner order: TL=v0, TR=v1, BR=v2, BL=v3
        // Matches Blockbench's UV rotation: each 90° step rotates the UV lookup CCW.
        // Derived from Blockbench js/outliner/types/cube.js updateUV rotation loop.
        (float u, float v)[] corners = rotation switch
        {
            90  => new[] { (uMin, vMax), (uMin, vMin), (uMax, vMin), (uMax, vMax) },
            180 => new[] { (uMax, vMax), (uMin, vMax), (uMin, vMin), (uMax, vMin) },
            270 => new[] { (uMax, vMin), (uMax, vMax), (uMin, vMax), (uMin, vMin) },
            _   => new[] { (uMin, vMin), (uMax, vMin), (uMax, vMax), (uMin, vMax) },
        };

        return (corners[0].u, corners[0].v,
                corners[1].u, corners[1].v,
                corners[2].u, corners[2].v,
                corners[3].u, corners[3].v);
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

        // Block model convention: X rotation tilts, Y rotation spins
        mat4 rx = mat4.RotateX(radX);
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
