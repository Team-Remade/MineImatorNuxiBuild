using System.Numerics;
using MineImatorSimplyRemade;
using System.Text.Json;
using System.Text.Json.Nodes;
using MineImatorSimplyRemade.core.render;
using Veldrid;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>
/// Loads OptiFine CEM (<c>.jem</c>) entity model files and converts each box
/// part into one or more <see cref="VeldridMesh"/> objects ready for rendering.
///
/// Coordinate system notes
/// ───────────────────────
/// OptiFine JEM uses Minecraft's entity model coordinate space:
///   • 1 unit  = 1/16 of a block (pixel unit)
///   • Origin  = top-left-front of the block entity bounding box
///   • Y axis  = down (positive Y goes downward in Java edition)
///   • <c>invertAxis:"xy"</c> flips X and Y before applying the part transform,
///     which converts from Java's "up=negative Y" to OpenGL's "up=positive Y".
///
/// Per-face UV arrays are <c>[x1, y1, x2, y2]</c> in texture-pixel coordinates
/// (origin = top-left, matching OpenGL texture upload with stbi_set_flip=0).
/// </summary>
public static class CemLoader
{
    private const float PixelScale = 1f / 16f; // 1 pixel → 1/16 block unit

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a <c>.jem</c> file and builds one <see cref="VeldridMesh"/> per box part
    /// that has geometry (boxes with coordinates).  Each mesh receives the
    /// Veldrid texture resolved for the JEM's declared texture path.
    /// </summary>
    /// <param name="cemPath">Absolute path to the <c>.jem</c> file.</param>
    /// <param name="versionRoot">Version root directory used to resolve the texture path.</param>
    /// <returns>
    /// List of meshes (one per box), or a single fallback CubeMesh on failure.
    /// </returns>
    public static List<VeldridMesh> Load(string cemPath, string versionRoot, string resourcePackId = "")
    {
        if (!File.Exists(cemPath))
        {
            Console.WriteLine($"File not found: {cemPath}");
            return [new CubeMesh()];
        }

        JsonObject root;
        try
        {
            var parsed = JsonNode.Parse(File.ReadAllText(cemPath))?.AsObject();
            root = parsed ?? throw new Exception("Root is not a JSON object");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Parse error in '{cemPath}': {ex.Message}");
            return [new CubeMesh()];
        }

        // ── Resolve texture ───────────────────────────────────────────────────
        string   texturePath = root["texture"]?.GetValue<string>() ?? "";
        Texture? texture     = ResolveTexture(texturePath, versionRoot, resourcePackId);

        int[]  texSize     = JsonNodeToIntArray(root["textureSize"]) ?? [64, 64];
        float  texW        = texSize.Length > 0 ? texSize[0] : 64f;
        float  texH        = texSize.Length > 1 ? texSize[1] : 64f;

        // ── Build meshes from parts ───────────────────────────────────────────
        var result = new List<VeldridMesh>();
        if (root["models"] is not JsonArray parts) return result;

        foreach (JsonNode? partToken in parts)
        {
            if (partToken is not JsonObject part) continue;
            if (part["boxes"] is not JsonArray boxes) continue;

            // Part-level transform (rotate is intentionally ignored — see BuildPartTransform)
            float[] translate  = JsonNodeToFloatArray(part["translate"]) ?? [0, 0, 0];
            string  invertAxis = part["invertAxis"]?.GetValue<string>()  ?? "";
            string  partId     = part["id"]?.GetValue<string>()          ?? "";

            Matrix4x4 partTransform = BuildPartTransform(translate, invertAxis);

            // Large chest: the left and right halves are both centred at the same
            // origin; offset them by ±0.5 in X so they sit side-by-side.
            // System.Numerics uses row-vector convention, so composition order is
            // reversed relative to the old GlmSharp column-vector matrices: to
            // apply partTransform first then the offset, multiply partTransform * offset.
            if (partId.EndsWith("_left",  StringComparison.OrdinalIgnoreCase))
                partTransform = partTransform * Matrix4x4.CreateTranslation(new Vector3( 0.5f, 0f, 0f));
            else if (partId.EndsWith("_right", StringComparison.OrdinalIgnoreCase))
                partTransform = partTransform * Matrix4x4.CreateTranslation(new Vector3(-0.5f, 0f, 0f));

            foreach (JsonNode? boxToken in boxes)
            {
                if (boxToken is not JsonObject box) continue;
                var mesh = BuildBoxMesh(box, partTransform, texture, texW, texH, invertAxis);
                if (mesh != null)
                    result.Add(mesh);
            }
        }

        return result.Count > 0 ? result : [new CubeMesh()];
    }

    // ── Texture resolution ────────────────────────────────────────────────────

    private static Texture? ResolveTexture(string texturePath, string versionRoot, string resourcePackId)
    {
        if (string.IsNullOrEmpty(texturePath)) return null;

        // JEM texture paths look like "textures/block/classic_chest.png"
        // Try the key in TerrainAtlas first (key = filename without extension)
        string key = Path.GetFileNameWithoutExtension(texturePath);
        string normalizedPackId = MinecraftDataLoader.NormalizeResourcePackId(resourcePackId);
        if (!string.IsNullOrWhiteSpace(normalizedPackId))
        {
            string namespacedKey = MinecraftDataLoader.BuildResourcePackTextureKeyFromId(normalizedPackId, key);
            if (TerrainAtlas.Textures.TryGetValue(namespacedKey, out Texture? packAtlasTex))
                return packAtlasTex;
        }

        if (TerrainAtlas.Textures.TryGetValue(key, out Texture? atlasTex))
            return atlasTex;

        // Fall back: load the file directly relative to versionRoot
        string fullPath = Path.Combine(versionRoot, texturePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            Console.WriteLine($"Texture '{key}' not in atlas, loading from disk: {fullPath}");
            // We can't easily load a texture here without the device-bound loader used by
            // TerrainAtlas, so just return null and let it render untextured.
        }

        return null;
    }

    // ── Part transform ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the part-level transform matrix from a JEM part's translate/invertAxis.
    ///
    /// The <c>rotate</c> field in JEM is the entity renderer's bone animation pivot —
    /// combined with <c>invertAxis:"xy"</c> and <c>rotate:[-180,0,0]</c> it is the
    /// standard Java entity model trick to flip from Y-down entity space to Y-up render
    /// space.  Because we already handle the Y-flip via <c>invertAxis</c> on the box
    /// coordinates and translate, we must NOT apply the rotate as a mesh-space matrix
    /// rotation (doing so would double-flip and mis-place the geometry).
    /// </summary>
    private static Matrix4x4 BuildPartTransform(float[] translate, string invertAxis)
    {
        // The translate is in the same raw JEM pixel space as the box coordinates,
        // so the same invertAxis negation must be applied before scaling.
        float tx = translate.Length > 0 ? translate[0] : 0f;
        float ty = translate.Length > 1 ? translate[1] : 0f;
        float tz = translate.Length > 2 ? translate[2] : 0f;

        if (invertAxis.Contains('x')) tx = -tx;
        if (invertAxis.Contains('y')) ty = -ty;
        if (invertAxis.Contains('z')) tz = -tz;

        // Scale pixel units → block units
        tx *= PixelScale;
        ty *= PixelScale;
        tz *= PixelScale;

        // Final centring: after invertAxis + translate the geometry spans 0..1 on each
        // axis, but the scene expects meshes centred at the origin (−0.5..+0.5).
        // Row-vector convention: apply t first then centre, so t * centre.
        Matrix4x4 centre = Matrix4x4.CreateTranslation(new Vector3(-0.5f, -0.5f, -0.5f));
        Matrix4x4 t      = Matrix4x4.CreateTranslation(new Vector3(tx, ty, tz));

        return t * centre;
    }

    // ── Box mesh builder ──────────────────────────────────────────────────────

    private static VeldridMesh? BuildBoxMesh(JsonObject box,
        Matrix4x4 partTransform, Texture? texture,
        float texW, float texH, string invertAxis)
    {
        float[]? coords = JsonNodeToFloatArray(box["coordinates"]);
        if (coords == null || coords.Length < 6) return null;

        // coordinates: [x, y, z, width, height, depth] in pixel units
        // The box is specified in "raw" JEM space; invertAxis is applied to convert
        // to a sensible right-hand Y-up space before scaling to block units.
        float bx = coords[0]; float by = coords[1]; float bz = coords[2];
        float bw = coords[3]; float bh = coords[4]; float bd = coords[5];

        // Apply invertAxis to the min corner (the dimensions stay positive)
        // invertAxis "xy" → negate X and Y of the origin
        if (invertAxis.Contains('x')) { bx = -bx - bw; }
        if (invertAxis.Contains('y')) { by = -by - bh; }
        if (invertAxis.Contains('z')) { bz = -bz - bd; }

        // Convert pixel units → block units (scale to 0..1 range, then centre)
        float x0 = bx * PixelScale;
        float y0 = by * PixelScale;
        float z0 = bz * PixelScale;
        float x1 = x0 + bw * PixelScale;
        float y1 = y0 + bh * PixelScale;
        float z1 = z0 + bd * PixelScale;

        // Build the mesh with per-face UVs
        var mesh = new VeldridMesh(VeldridContext.Device);

        // Face order and names for CEM UV keys
        var faceSpecs = new (string uvKey, string faceName)[]
        {
            ("uvNorth", "north"),
            ("uvSouth", "south"),
            ("uvEast",  "east"),
            ("uvWest",  "west"),
            ("uvUp",    "up"),
            ("uvDown",  "down"),
        };

        foreach (var (uvKey, faceName) in faceSpecs)
        {
            float[]? uvArr = JsonNodeToFloatArray(box[uvKey]);
            if (uvArr == null || uvArr.Length < 4) continue;

            // JEM UV arrays are [x2, y2, x1, y1] — the second corner is stored first.
            // So arr[0,1] = bottom-right of the face in image space,
            //    arr[2,3] = top-left of the face in image space.
            float uBR = uvArr[0] / texW;
            float vBR = uvArr[1] / texH;
            float uTL = uvArr[2] / texW;
            float vTL = uvArr[3] / texH;

            // Vertex positions for this face (CCW winding from outside)
            (Vector3 v0p, Vector3 v1p, Vector3 v2p, Vector3 v3p) =
                FaceQuad(faceName, x0, y0, z0, x1, y1, z1);
            Vector3 normal = FaceNormal(faceName);

            // Apply part transform to vertices and normal
            v0p = TransformPoint(partTransform, v0p);
            v1p = TransformPoint(partTransform, v1p);
            v2p = TransformPoint(partTransform, v2p);
            v3p = TransformPoint(partTransform, v3p);
            normal = TransformNormal(partTransform, normal);

            // Quad corners: TL=v0p, TR=v1p, BR=v2p, BL=v3p
            var uvTL = new Vector2(uTL, vTL);
            var uvTR = new Vector2(uBR, vTL);
            var uvBR = new Vector2(uBR, vBR);
            var uvBL = new Vector2(uTL, vBR);

            if (faceName is "east" or "up" or "south" or "west")
            {
                // Positive-direction faces: (V0,V3,V2) and (V0,V2,V1) — CCW
                mesh.Vertices.Add(v0p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvTL);
                mesh.Vertices.Add(v3p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvBL);
                mesh.Vertices.Add(v2p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvBR);
                mesh.Vertices.Add(v0p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvTL);
                mesh.Vertices.Add(v2p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvBR);
                mesh.Vertices.Add(v1p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvTR);
            }
            else
            {
                // Negative-direction faces: (V0,V1,V2) and (V0,V2,V3) — CCW
                mesh.Vertices.Add(v0p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvTL);
                mesh.Vertices.Add(v1p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvTR);
                mesh.Vertices.Add(v2p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvBR);
                mesh.Vertices.Add(v0p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvTL);
                mesh.Vertices.Add(v2p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvBR);
                mesh.Vertices.Add(v3p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvBL);
            }
        }

        if (mesh.Vertices.Count == 0) return null;

        mesh.AlbedoTexture = texture;
        mesh.DoubleSided   = false;
        mesh.Upload(VeldridContext.StandardOutputDescription);
        return mesh;
    }

    // ── Face geometry ─────────────────────────────────────────────────────────

    // Same winding convention as MinecraftModelMesh: CCW from outside.
    private static (Vector3, Vector3, Vector3, Vector3) FaceQuad(string face,
        float x0, float y0, float z0, float x1, float y1, float z1)
    {
        return face switch
        {
            "down"  => (new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1)),
            "up"    => (new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1)),
            "north" => (new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), new Vector3(x0, y0, z0)),
            "south" => (new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1)),
            "west"  => (new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), new Vector3(x0, y0, z0)),
            "east"  => (new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1)),
            _       => (Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero)
        };
    }

    private static Vector3 FaceNormal(string face) => face switch
    {
        "down"  => new Vector3( 0, -1,  0),
        "up"    => new Vector3( 0,  1,  0),
        "north" => new Vector3( 0,  0, -1),
        "south" => new Vector3( 0,  0,  1),
        "west"  => new Vector3(-1,  0,  0),
        "east"  => new Vector3( 1,  0,  0),
        _       => Vector3.UnitY
    };

    // ── Transform helpers ─────────────────────────────────────────────────────

    private static Vector3 TransformPoint(Matrix4x4 m, Vector3 p)
    {
        // Row-vector convention (System.Numerics): p * m, including translation.
        return Vector3.Transform(p, m);
    }

    private static Vector3 TransformNormal(Matrix4x4 m, Vector3 n)
    {
        Vector3 r = Vector3.TransformNormal(n, m);
        return r.LengthSquared() > 0f ? Vector3.Normalize(r) : Vector3.UnitY;
    }

    // ── JSON helpers ──────────────────────────────────────────────────────────

    private static float[]? JsonNodeToFloatArray(JsonNode? node)
    {
        if (node is not JsonArray arr) return null;
        var result = new float[arr.Count];
        for (int i = 0; i < arr.Count; i++)
            result[i] = arr[i]?.GetValue<float>() ?? 0f;
        return result;
    }

    private static int[]? JsonNodeToIntArray(JsonNode? node)
    {
        if (node is not JsonArray arr) return null;
        var result = new int[arr.Count];
        for (int i = 0; i < arr.Count; i++)
            result[i] = arr[i]?.GetValue<int>() ?? 0;
        return result;
    }
}
