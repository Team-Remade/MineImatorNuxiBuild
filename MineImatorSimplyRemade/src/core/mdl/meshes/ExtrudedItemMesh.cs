using System.Numerics;
using MineImatorSimplyRemade.core.render;
using Veldrid;

namespace MineImatorSimplyRemade.core.mdl.meshes;

/// <summary>
/// Generates either a flat textured plane or a per-pixel extruded hull mesh
/// from a 16×16 RGBA tile pulled from <c>ItemsAtlas</c> or <c>TerrainAtlas</c>.
///
/// In <b>flat mode</b> (<see cref="Is3D"/> = false) the result is a double-sided
/// 1×1 XY plane with the tile texture applied.
///
/// In <b>3D mode</b> (<see cref="Is3D"/> = true) every pixel whose alpha exceeds
/// <see cref="AlphaThreshold"/> is extruded by <see cref="ExtrudeDepth"/> along
/// the +Z axis, producing a front face, a back face, and side faces along every
/// opaque/transparent boundary edge.  Pixels below the threshold are skipped
/// entirely (transparent hull).
///
/// UV coordinates always map to the full [0,1]×[0,1] range of the supplied
/// <see cref="VeldridMesh.AlbedoTexture"/> (the pre-sliced tile texture from the atlas).
/// </summary>
public class ExtrudedItemMesh : VeldridMesh
{
    // ── Parameters ────────────────────────────────────────────────────────────

    /// <summary>When false a plain double-sided plane is produced instead.</summary>
    public bool Is3D { get; }

    /// <summary>Maximum tile dimension in pixels used to normalize the mesh to unit size.</summary>
    public int TileSize { get; }

    /// <summary>Tile width in pixels.</summary>
    public int TileWidth { get; }

    /// <summary>Tile height in pixels.</summary>
    public int TileHeight { get; }

    /// <summary>
    /// Total depth of the extrusion in world units.
    /// The mesh spans [-ExtrudeDepth/2, +ExtrudeDepth/2] along Z.
    /// Default: 1/16 — one pixel-width thick.
    /// </summary>
    public float ExtrudeDepth { get; }

    /// <summary>
    /// Pixels whose alpha (0–255) is at or above this value are included in the
    /// hull and used for side-face neighbour checks.  Default: 128 (A &gt; 0.5),
    /// matching the reference implementation.
    /// </summary>
    public byte AlphaThreshold { get; }

    // ── Raw tile data ─────────────────────────────────────────────────────────

    /// <summary>
    /// RGBA pixel data, row-major, top-to-bottom.
    /// Length must equal <c>TileWidth * TileHeight * 4</c>.
    /// </summary>
    private readonly byte[] _pixels;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="tileTexture">Veldrid texture of the pre-sliced tile, or null for an untextured hull.</param>
    /// <param name="tilePixels">
    ///   Raw RGBA bytes for the tile (top-to-bottom, 4 bytes per pixel).
    ///   Must be <c>tileWidth * tileHeight * 4</c> bytes long.
    /// </param>
    /// <param name="is3D">True = extruded hull; false = flat plane.</param>
    /// <param name="tileSize">Maximum tile dimension used to normalize the mesh to unit size.</param>
    /// <param name="tileWidth">Actual tile width in pixels. Defaults to <paramref name="tileSize"/>.</param>
    /// <param name="tileHeight">Actual tile height in pixels. Defaults to <paramref name="tileSize"/>.</param>
    /// <param name="extrudeDepth">Total Z depth of the extrusion in world units.</param>
    /// <param name="alphaThreshold">Minimum alpha (0–255) to treat a pixel as opaque.</param>
    public ExtrudedItemMesh(
        Texture? tileTexture,
        byte[] tilePixels,
        bool is3D = true,
        int tileSize = 16,
        int? tileWidth = null,
        int? tileHeight = null,
        float extrudeDepth = 1f / 16f,
        byte alphaThreshold = 128)
        : base(VeldridContext.Device)
    {
        Is3D           = is3D;
        TileWidth      = Math.Max(1, tileWidth ?? tileSize);
        TileHeight     = Math.Max(1, tileHeight ?? tileSize);
        TileSize       = Math.Max(TileWidth, TileHeight);
        ExtrudeDepth   = extrudeDepth;
        AlphaThreshold = alphaThreshold;
        AlbedoTexture  = tileTexture;
        _pixels        = tilePixels;

        GenerateVertices();
        Upload(VeldridContext.StandardOutputDescription);
    }

    // ── Geometry generation ───────────────────────────────────────────────────

    private void GenerateVertices()
    {
        if (!Is3D)
            BuildFlatPlane();
        else
            BuildExtrudedHull();
    }

    // ── Flat plane ────────────────────────────────────────────────────────────

    private void BuildFlatPlane()
    {
        // A 1×1 double-sided XY plane centred at origin.
        // Front face (+Z normal), back face (-Z normal).
        // Front face (+Z normal), back face (-Z normal).
        // UV: top-right=(1,1), bottom-right=(1,0), bottom-left=(0,0), top-left=(0,1)

        float halfWidth = TileWidth / (float)TileSize * 0.5f;
        float halfHeight = TileHeight / (float)TileSize * 0.5f;

        Vector3[] positions =
        [
            new Vector3( halfWidth,  halfHeight, 0),   // 0 top-right
            new Vector3( halfWidth, -halfHeight, 0),   // 1 bottom-right
            new Vector3(-halfWidth, -halfHeight, 0),   // 2 bottom-left
            new Vector3(-halfWidth,  halfHeight, 0),   // 3 top-left
            // back face duplicates
            new Vector3( halfWidth,  halfHeight, 0),   // 4
            new Vector3( halfWidth, -halfHeight, 0),   // 5
            new Vector3(-halfWidth, -halfHeight, 0),   // 6
            new Vector3(-halfWidth,  halfHeight, 0),   // 7
        ];

        Vertices.AddRange(positions);

        // UVs: atlas tiles are uploaded top-to-bottom with no V-flip,
        // so V=0 is image top, V=1 is image bottom.
        // World top (y=+0.5) → UV V=0; world bottom (y=-0.5) → UV V=1.
        Vector2[] uvs =
        [
            new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0),
            new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0),
        ];
        TexCoords.AddRange(uvs);

        // Front face CCW from +Z, back face CCW from -Z
        Indices = [0, 1, 2, 0, 2, 3,
                   4, 6, 5, 4, 7, 6];

        // Match PlaneMesh convention: indices [0,1,2, 0,2,3] with TR/BR/BL/TL vertex order
        // is CCW when viewed from -Z, so the front face normal is -Z, back is +Z.
        Normals.Clear();
        var frontN = new Vector3(0, 0, -1);
        var backN  = new Vector3(0, 0,  1);
        for (int i = 0; i < 4; i++) Normals.Add(frontN);
        for (int i = 0; i < 4; i++) Normals.Add(backN);
    }

    // ── Extruded hull ─────────────────────────────────────────────────────────

    private void BuildExtrudedHull()
    {
        int width = TileWidth;
        int height = TileHeight;
        float s = 1f / TileSize;       // world size of one pixel on the dominant axis
        float zF = +ExtrudeDepth / 2f; // front face Z
        float zB = -ExtrudeDepth / 2f; // back face Z

        // ── Helper: add a quad as two triangles ──────────────────────────────

        var verts   = Vertices;
        var norms   = Normals;
        var uvs     = TexCoords;
        var indices = new List<uint>();

        void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                     Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3,
                     Vector3 normal)
        {
            uint baseIdx = (uint)verts.Count;
            verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
            norms.Add(normal); norms.Add(normal); norms.Add(normal); norms.Add(normal);
            uvs.Add(uv0); uvs.Add(uv1); uvs.Add(uv2); uvs.Add(uv3);
            // CW winding order in index list = CCW viewed from the normal direction
            indices.Add(baseIdx + 0); indices.Add(baseIdx + 2); indices.Add(baseIdx + 1);
            indices.Add(baseIdx + 0); indices.Add(baseIdx + 3); indices.Add(baseIdx + 2);
        }

        // ── Helper: is pixel (px, py) opaque? ────────────────────────────────
        // Samples the alpha of pixel (px, py) at its exact center in the byte array.
        // Out-of-bounds coordinates are treated as transparent (returns false).

        bool IsOpaque(int px, int py)
        {
            if (px < 0 || py < 0 || px >= width || py >= height) return false;
            int idx = (py * width + px) * 4 + 3; // alpha channel
            return _pixels[idx] >= AlphaThreshold;
        }

        // ── Helper: UV for pixel (px, py) ────────────────────────────────────
        // All four vertices of every face on a given pixel share the same UV —
        // the exact center of that pixel in the tile texture.
        // Tiles are uploaded top-to-bottom with no V-flip, so V=0 is the top.
        // Center of pixel (px, py): U = (px + 0.5) / n,  V = (py + 0.5) / n.

        Vector2 PixelCenterUV(int px, int py)
        {
            float u = (px + 0.5f) / width;
            float v = (py + 0.5f) / height;
            return new Vector2(u, v);
        }

        // World-space X,Y corners of pixel (px, py).
        // X: pixel column 0 → left edge at -0.5, column n-1 → right edge at +0.5.
        // Y: image row 0 is the top; world Y increases upward, so row 0 → y near +0.5.
        float totalWidth = width * s;
        float totalHeight = height * s;
        float WorldX(int px) => px * s - totalWidth * 0.5f;
        float WorldY(int py) => totalHeight * 0.5f - (py + 1) * s; // bottom edge of row py in world

        // ── Build geometry ────────────────────────────────────────────────────

        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                if (!IsOpaque(px, py)) continue;

                float x0 = WorldX(px);
                float x1 = x0 + s;
                float y0 = WorldY(py);        // bottom edge of this pixel in world
                float y1 = y0 + s;            // top edge

                // All faces for this pixel sample the same point: the pixel center.
                Vector2 uv = PixelCenterUV(px, py);

                // ── Front face (+Z normal) ─────────────────────────────────
                AddQuad(
                    new Vector3(x0, y1, zF), new Vector3(x1, y1, zF),
                    new Vector3(x1, y0, zF), new Vector3(x0, y0, zF),
                    uv, uv, uv, uv,
                    new Vector3(0, 0, 1));

                // ── Back face (-Z normal) ──────────────────────────────────
                AddQuad(
                    new Vector3(x1, y1, zB), new Vector3(x0, y1, zB),
                    new Vector3(x0, y0, zB), new Vector3(x1, y0, zB),
                    uv, uv, uv, uv,
                    new Vector3(0, 0, -1));

                // ── Side faces (only at silhouette edges) ─────────────────
                // Emit a side face only when the neighbour pixel is transparent,
                // using A > 0.5 as the cutoff (matching the reference implementation).

                // Right (+X)
                if (!IsOpaque(px + 1, py))
                    AddQuad(
                        new Vector3(x1, y1, zF), new Vector3(x1, y1, zB),
                        new Vector3(x1, y0, zB), new Vector3(x1, y0, zF),
                        uv, uv, uv, uv,
                        new Vector3(1, 0, 0));

                // Left (-X)
                if (!IsOpaque(px - 1, py))
                    AddQuad(
                        new Vector3(x0, y1, zB), new Vector3(x0, y1, zF),
                        new Vector3(x0, y0, zF), new Vector3(x0, y0, zB),
                        uv, uv, uv, uv,
                        new Vector3(-1, 0, 0));

                // Top (+Y world / row py-1 in image)
                if (!IsOpaque(px, py - 1))
                    AddQuad(
                        new Vector3(x0, y1, zB), new Vector3(x1, y1, zB),
                        new Vector3(x1, y1, zF), new Vector3(x0, y1, zF),
                        uv, uv, uv, uv,
                        new Vector3(0, 1, 0));

                // Bottom (-Y world / row py+1 in image)
                if (!IsOpaque(px, py + 1))
                    AddQuad(
                        new Vector3(x1, y0, zB), new Vector3(x0, y0, zB),
                        new Vector3(x0, y0, zF), new Vector3(x1, y0, zF),
                        uv, uv, uv, uv,
                        new Vector3(0, -1, 0));
            }
        }

        Indices = indices.ToArray();
    }
}
