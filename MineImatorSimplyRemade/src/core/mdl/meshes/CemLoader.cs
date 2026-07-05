using GlmSharp;
using MineImatorSimplyRemade;
using System.Text.Json;
using System.Text.Json.Nodes;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>
/// Loads OptiFine CEM (<c>.jem</c>) entity model files and converts each box
/// part into one or more <see cref="Mesh"/> objects ready for rendering.
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
    /// Loads a <c>.jem</c> file and builds one <see cref="Mesh"/> per box part
    /// that has geometry (boxes with coordinates).  Each mesh receives the GL
    /// texture loaded for the JEM's declared texture path.
    /// </summary>
    /// <param name="gl">Active OpenGL context.</param>
    /// <param name="cemPath">Absolute path to the <c>.jem</c> file.</param>
    /// <param name="versionRoot">Version root directory used to resolve the texture path.</param>
    /// <returns>
    /// List of meshes (one per box), or a single fallback CubeMesh on failure.
    /// </returns>
    public static List<Mesh> Load(GL gl, string cemPath, string versionRoot, string resourcePackId = "")
    {
        if (!File.Exists(cemPath))
        {
            Console.WriteLine($"[CemLoader] File not found: {cemPath}");
            return new List<Mesh> { new CubeMesh(gl) };
        }

        JsonObject root;
        try
        {
            var parsed = JsonNode.Parse(File.ReadAllText(cemPath))?.AsObject();
            if (parsed == null) throw new Exception("Root is not a JSON object");
            root = parsed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CemLoader] Parse error in '{cemPath}': {ex.Message}");
            return new List<Mesh> { new CubeMesh(gl) };
        }

        // ── Resolve texture ───────────────────────────────────────────────────
        string texturePath = root["texture"]?.GetValue<string>() ?? "";
        uint   texId       = ResolveTexture(texturePath, versionRoot, resourcePackId);

        int[]  texSize     = JsonNodeToIntArray(root["textureSize"]) ?? new[] { 64, 64 };
        float  texW        = texSize.Length > 0 ? texSize[0] : 64f;
        float  texH        = texSize.Length > 1 ? texSize[1] : 64f;

        // ── Build meshes from parts ───────────────────────────────────────────
        var result = new List<Mesh>();
        if (root["models"] is not JsonArray parts) return result;

        foreach (JsonNode? partToken in parts)
        {
            if (partToken is not JsonObject part) continue;
            if (part["boxes"] is not JsonArray boxes) continue;

            // Part-level transform (rotate is intentionally ignored — see BuildPartTransform)
            float[] translate  = JsonNodeToFloatArray(part["translate"]) ?? new float[] { 0, 0, 0 };
            string  invertAxis = part["invertAxis"]?.GetValue<string>()  ?? "";
            string  partId     = part["id"]?.GetValue<string>()          ?? "";

            mat4 partTransform = BuildPartTransform(translate, invertAxis);

            // Large chest: the left and right halves are both centred at the same
            // origin; offset them by ±0.5 in X so they sit side-by-side.
            if (partId.EndsWith("_left",  StringComparison.OrdinalIgnoreCase))
                partTransform = mat4.Translate(new vec3( 0.5f, 0f, 0f)) * partTransform;
            else if (partId.EndsWith("_right", StringComparison.OrdinalIgnoreCase))
                partTransform = mat4.Translate(new vec3(-0.5f, 0f, 0f)) * partTransform;

            foreach (JsonNode? boxToken in boxes)
            {
                if (boxToken is not JsonObject box) continue;
                var mesh = BuildBoxMesh(gl, box, partTransform, texId, texW, texH, invertAxis);
                if (mesh != null)
                    result.Add(mesh);
            }
        }

        return result.Count > 0 ? result : new List<Mesh> { new CubeMesh(gl) };
    }

    // ── Texture resolution ────────────────────────────────────────────────────

    private static uint ResolveTexture(string texturePath, string versionRoot, string resourcePackId)
    {
        if (string.IsNullOrEmpty(texturePath)) return 0;

        // JEM texture paths look like "textures/block/classic_chest.png"
        // Try the key in TerrainAtlas first (key = filename without extension)
        string key = Path.GetFileNameWithoutExtension(texturePath);
        string normalizedPackId = MinecraftDataLoader.NormalizeResourcePackId(resourcePackId);
        if (!string.IsNullOrWhiteSpace(normalizedPackId))
        {
            string namespacedKey = MinecraftDataLoader.BuildResourcePackTextureKeyFromId(normalizedPackId, key);
            if (TerrainAtlas.Textures.TryGetValue(namespacedKey, out uint packAtlasId))
                return packAtlasId;
        }

        if (TerrainAtlas.Textures.TryGetValue(key, out uint atlasId))
            return atlasId;

        // Fall back: load the file directly relative to versionRoot
        string fullPath = Path.Combine(versionRoot, texturePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            Console.WriteLine($"[CemLoader] Texture '{key}' not in atlas, loading from disk: {fullPath}");
            // We can't easily load a GL texture here without the GL context captured in
            // TerrainAtlas, so just return 0 and let it render untextured.
        }

        return 0;
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
    private static mat4 BuildPartTransform(float[] translate, string invertAxis)
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
        mat4 centre = mat4.Translate(new vec3(-0.5f, -0.5f, -0.5f));
        mat4 t      = mat4.Translate(new vec3(tx, ty, tz));

        return centre * t;
    }

    // ── Box mesh builder ──────────────────────────────────────────────────────

    private static Mesh? BuildBoxMesh(GL gl, JsonObject box,
        mat4 partTransform, uint texId,
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
        var mesh = new Mesh(gl);

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
            (vec3 v0p, vec3 v1p, vec3 v2p, vec3 v3p) =
                FaceQuad(faceName, x0, y0, z0, x1, y1, z1);
            vec3 normal = FaceNormal(faceName);

            // Apply part transform to vertices and normal
            v0p = TransformPoint(partTransform, v0p);
            v1p = TransformPoint(partTransform, v1p);
            v2p = TransformPoint(partTransform, v2p);
            v3p = TransformPoint(partTransform, v3p);
            normal = TransformNormal(partTransform, normal);

            // Quad corners: TL=v0p, TR=v1p, BR=v2p, BL=v3p
            var uvTL = new vec2(uTL, vTL);
            var uvTR = new vec2(uBR, vTL);
            var uvBR = new vec2(uBR, vBR);
            var uvBL = new vec2(uTL, vBR);

            // Tri 0: v0(TL), v1(TR), v2(BR)
            mesh.Vertices.Add(v0p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvTL);
            mesh.Vertices.Add(v1p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvTR);
            mesh.Vertices.Add(v2p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvBR);
            // Tri 1: v0(TL), v2(BR), v3(BL)
            mesh.Vertices.Add(v0p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvTL);
            mesh.Vertices.Add(v2p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvBR);
            mesh.Vertices.Add(v3p); mesh.Normals.Add(normal); mesh.TexCoords.Add(uvBL);
        }

        if (mesh.Vertices.Count == 0) return null;

        mesh.TextureId  = texId;
        mesh.DoubleSided = false;
        mesh.Upload();
        return mesh;
    }

    // ── Face geometry ─────────────────────────────────────────────────────────

    // Same winding convention as MinecraftModelMesh: CCW from outside.
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

    // ── Transform helpers ─────────────────────────────────────────────────────

    private static vec3 TransformPoint(mat4 m, vec3 p)
    {
        vec4 t = m * new vec4(p, 1f);
        return new vec3(t.x, t.y, t.z);
    }

    private static vec3 TransformNormal(mat4 m, vec3 n)
    {
        vec4 t = m * new vec4(n, 0f);
        var  r = new vec3(t.x, t.y, t.z);
        return r.LengthSqr > 0f ? r.Normalized : vec3.UnitY;
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
