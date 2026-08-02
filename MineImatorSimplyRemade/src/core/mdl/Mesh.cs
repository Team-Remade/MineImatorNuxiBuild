using System;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.material;
using MineImatorSimplyRemade.core.mdl.material.materials;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl;

/// <summary>
/// A GPU-resident triangle mesh.  Each vertex stores a position (vec3), a
/// normal (vec3), and an optional UV coordinate (vec2), interleaved as
/// [ px, py, pz, nx, ny, nz, u, v … ].
///
/// Usage:
///   1. Populate <see cref="Vertices"/> (positions) and <see cref="Normals"/>
///      (one normal per vertex, or leave empty for auto-generation).
///   2. Optionally populate <see cref="TexCoords"/> (one UV per vertex).
///      Leave empty for untextured meshes (UV defaults to 0,0).
///   3. Optionally set <see cref="Indices"/> for indexed drawing; leave null
///      for non-indexed mode.
///   4. Call <see cref="Upload"/> to push data to the GPU.
///   5. Call <see cref="Render(mat4, mat4, mat4)"/> each frame.
///   6. Optionally set <see cref="TextureId"/> to a GL texture handle to
///      render with a texture instead of the flat <see cref="Albedo"/> colour.
/// </summary>
public class Mesh : IDisposable
{
    private readonly GL _gl;

    private uint _vbo, _ebo, _vao, _skinVbo;
    private Shader _shader;

    // ── CPU-side geometry ─────────────────────────────────────────────────────

    /// <summary>Vertex positions (XYZ). Must have Count % 3 == 0 for non-indexed meshes.</summary>
    public readonly List<vec3> Vertices = new();

    /// <summary>
    /// Per-vertex normals, parallel to <see cref="Vertices"/>.
    /// Leave empty to auto-generate flat normals from triangles during <see cref="Upload"/>.
    /// </summary>
    public readonly List<vec3> Normals = new();

    /// <summary>
    /// Per-vertex texture coordinates (UV), parallel to <see cref="Vertices"/>.
    /// Leave empty for untextured meshes — the shader will use <see cref="Albedo"/> instead.
    /// </summary>
    public readonly List<vec2> TexCoords = new();

    // ── Skinning ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-vertex bone indices (up to 4 per vertex), parallel to <see cref="Vertices"/>.
    /// Only used when <see cref="IsSkinned"/> is true.
    /// </summary>
    public readonly List<ivec4> BoneIndices = new();

    /// <summary>
    /// Per-vertex bone weights (up to 4 per vertex), parallel to <see cref="Vertices"/>.
    /// Only used when <see cref="IsSkinned"/> is true.
    /// </summary>
    public readonly List<vec4> BoneWeights = new();

    /// <summary>
    /// Bone names used by this mesh, indexed by the bone indices in <see cref="BoneIndices"/>.
    /// </summary>
    public readonly List<string> BoneNames = new();

    /// <summary>
    /// Inverse bind matrices for each bone in <see cref="BoneNames"/>.
    /// These transform from mesh space to bone space in the bind pose.
    /// </summary>
    public readonly List<mat4> BoneInverseBindMatrices = new();

    /// <summary>
    /// Current bone matrices, uploaded to the shader each frame.
    /// Computed as <c>meshWorldInverse * boneWorld * inverseBindMatrix * meshWorld</c>
    /// so the shader can deform vertices in the mesh's local space.
    /// </summary>
    public List<mat4>? BoneMatrices { get; set; }

    /// <summary>True when this mesh has skinning data and should be GPU-deformed.</summary>
    public bool IsSkinned => BoneIndices.Count > 0 && BoneIndices.Count == Vertices.Count;

    /// <summary>
    /// Local-space bounding sphere center (computed from vertices during Upload).
    /// </summary>
    public vec3 BoundingSphereCenter;

    /// <summary>
    /// Local-space bounding sphere radius (computed from vertices during Upload).
    /// </summary>
    public float BoundingSphereRadius;

    /// <summary>
    /// Optional index buffer (uint32).  Leave null for plain <c>DrawArrays</c>.
    /// </summary>
    public uint[]? Indices;

    // ── Animation ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Key into <see cref="TerrainAtlas.AnimatedTextures"/>.
    /// When non-empty the mesh samples a single frame of the spritesheet each
    /// render call and advances the animation over time.
    /// Leave empty (default) for static textures.
    /// </summary>
    public string AnimationKey = "";

    /// <summary>Accumulated real-time seconds since the animation started.</summary>
    private double _animTime = 0.0;

    /// <summary>Minecraft runs at 20 ticks/s; each frametime unit = 1 tick.</summary>
    private const double SecondsPerTick = 1.0 / 20.0;

    // ── Overlay / unlit flags ─────────────────────────────────────────────────

    /// <summary>
    /// When true, the mesh is rendered without lighting: ambient, diffuse, and
    /// point-light contributions are all bypassed and the raw albedo/texture colour
    /// is used directly.  Intended for editor overlays such as the camera icon.
    /// </summary>
    public bool Unlit = false;

    /// <summary>
    /// When true, depth testing is disabled while drawing this mesh so it always
    /// renders on top of all other geometry in the scene.  The depth buffer is
    /// also left unmodified (depth writes off) so the overlay does not occlude
    /// objects rendered after it.  Intended for editor overlays such as the camera icon.
    /// </summary>
    public bool DepthTestDisabled = false;

    /// <summary>
    /// When true, this mesh is genuinely alpha-*blended* (e.g. water, whose texture
    /// is a uniform ~70% opacity) rather than merely alpha-*cutout* (e.g. leaves,
    /// glass panes — pixels are either fully opaque or fully transparent). The
    /// viewport routes translucent meshes through the depth-tested,
    /// depth-write-off blend pass instead of the textured/cutout depth-pre-pass
    /// path, since that pre-pass writes full depth for any non-transparent pixel
    /// and would otherwise make a partially-see-through surface like water wrongly
    /// hide opaque geometry behind it. See <see cref="TerrainAtlas.IsTextureTranslucent"/>.
    /// </summary>
    public bool IsTranslucent = false;

    /// <summary>
    /// Optional render ordering hint used by the viewport for coplanar layered
    /// meshes (e.g. Mine-imator facial rigs). Lower values render first.
    /// </summary>
    public float SortDepth = 0f;

    /// <summary>
    /// When true, this mesh is excluded from normal scene rendering and is
    /// intended only for editor helper passes such as colour picking and
    /// silhouette masking.
    /// </summary>
    public bool PickOnly = false;

    // ── Shape keys (blend shapes / morph targets) ─────────────────────────────

    /// <summary>
    /// One shape key imported from a model file (e.g. a glTF morph target such
    /// as a facial expression).  <see cref="Deltas"/> stores the per-vertex
    /// offset from the base mesh — the final position is
    /// <c>basePosition + Weight * Deltas[v]</c>.  Normals are recomputed from
    /// the deformed positions (see <see cref="RecomputeShapeKeyNormals"/>) so
    /// lighting follows the morphed surface instead of the stale base pose.
    /// </summary>
    public class ShapeKey
    {
        public string Name = "";
        public float[] Deltas = Array.Empty<float>(); // length == Vertices.Count * 3 (x,y,z per vertex)
        public float Weight = 0f;                     // user-controlled, typically -1..1
    }

    /// <summary>
    /// All shape keys defined for this mesh.  Empty for meshes without morph
    /// data.  Order is the display / animation order.
    /// </summary>
    public readonly List<ShapeKey> ShapeKeys = new();

    /// <summary>True if this mesh has any morph-target data.</summary>
    public bool HasShapeKeys => ShapeKeys.Count > 0;

    /// <summary>True when at least one shape key weight is non-zero.</summary>
    public bool HasActiveShapeKey =>
        _shapeKeyDirty || ShapeKeys.Any(sk => sk.Weight != 0f);

    /// <summary>
    /// CPU-side copy of the interleaved base vertex data (positions, normals,
    /// UVs) created by <see cref="Upload"/>.  Used as the source for re-
    /// generating the VBO when a shape-key weight changes.
    /// </summary>
    private float[]? _baseVertexData;

    /// <summary>
    /// CPU-side copy of the deformed interleaved vertex data, rebuilt from
    /// <see cref="_baseVertexData"/> plus the current shape-key weights on
    /// demand.  Allocated lazily and reused between refreshes.
    /// </summary>
    private float[]? _deformedVertexData;

    /// <summary>Set whenever a shape-key weight changes so the VBO is refreshed.</summary>
    private bool _shapeKeyDirty = false;

    /// <summary>
    /// Sets a shape key's weight in the range [-1, 1] and marks the mesh for a
    /// VBO re-upload on the next render.  Out-of-range values are clamped.
    /// </summary>
    public void SetShapeKeyWeight(int index, float weight)
    {
        if (index < 0 || index >= ShapeKeys.Count) return;
        float clamped = Math.Clamp(weight, -1f, 1f);
        if (ShapeKeys[index].Weight == clamped) return;
        ShapeKeys[index].Weight = clamped;
        _shapeKeyDirty = true;
    }

    /// <summary>
    /// Sets every shape key weight to 0 (effectively reverts to the base mesh).
    /// </summary>
    public void ResetShapeKeys()
    {
        bool anyNonZero = false;
        foreach (var sk in ShapeKeys.Where(sk => sk.Weight != 0f))
        {
            sk.Weight = 0f; anyNonZero = true;
        }
        if (anyNonZero) _shapeKeyDirty = true;
    }

    /// <summary>
    /// Adds a shape key to the mesh.  The deltas array length must match
    /// <see cref="Vertices"/>.Count * 3.
    /// </summary>
    public void AddShapeKey(string name, float[] deltas)
    {
        if (deltas == null || deltas.Length != Vertices.Count * 3) return;
        ShapeKeys.Add(new ShapeKey { Name = name, Deltas = deltas });
        _shapeKeyDirty = true;
    }

    /// <summary>
    /// Recomputes the deformed vertex positions by applying every shape key's
    /// weight to its delta buffer, then re-uploads the full interleaved VBO
    /// (<c>[px py pz nx ny nz u v]</c> × vertexCount) via
    /// <c>glBufferSubData</c>.  The whole buffer is rewritten because the
    /// positions are interleaved with the normal and UV streams; uploading
    /// only the position bytes would leave the rest of each vertex slot
    /// pointing at stale memory and collapse the mesh into a triangle.
    /// This is a cheap, per-vertex CPU pass that runs only when a weight
    /// changes (typically once per user slider drag), not on every frame.
    /// </summary>
    public unsafe void RefreshShapeKeyGeometry()
    {
        if (_vao == 0 || _vbo == 0) return;
        if (_baseVertexData == null) return; // Upload() not yet called
        if (!_shapeKeyDirty) return;
        if (ShapeKeys.Count == 0) { _shapeKeyDirty = false; return; }

        int vertexCount = Vertices.Count;
        if (vertexCount == 0) { _shapeKeyDirty = false; return; }

        const int floatsPerVertex = 8;
        int totalFloats = vertexCount * floatsPerVertex;
        int totalBytes  = totalFloats * sizeof(float);

        bool anyActive = false;
        foreach (var sk in ShapeKeys)
        {
            if (sk.Weight != 0f && sk.Deltas != null && sk.Deltas.Length >= vertexCount * 3)
            {
                anyActive = true;
                break;
            }
        }

        if (!anyActive)
        {
            // All weights are 0 — restore the original interleaved data so
            // the VBO matches what Upload() produced.
            _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
            fixed (float* p = _baseVertexData)
                _gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)totalBytes, p);
            _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
            _shapeKeyDirty = false;
            return;
        }

        // Build the deformed interleaved data from the cached base data plus
        // the weighted shape-key deltas.  Allocating once and reusing on
        // subsequent refreshes keeps the GC happy during slider drags.
        if (_deformedVertexData == null || _deformedVertexData.Length != totalFloats)
            _deformedVertexData = new float[totalFloats];
        Array.Copy(_baseVertexData, 0, _deformedVertexData, 0, totalFloats);

        foreach (var sk in ShapeKeys)
        {
            if (sk.Weight == 0f || sk.Deltas == null || sk.Deltas.Length < vertexCount * 3) continue;
            float w = sk.Weight;
            var d  = sk.Deltas;
            var p  = _deformedVertexData;
            for (int v = 0; v < vertexCount; v++)
            {
                int di = v * 3;
                int vi = v * floatsPerVertex;
                p[vi + 0] += w * d[di + 0];
                p[vi + 1] += w * d[di + 1];
                p[vi + 2] += w * d[di + 2];
            }
        }

        // Positions moved but the normal slots above still hold the base
        // mesh's normals — left unmodified, shading on displaced regions
        // looks flat/inside-out and reinforces the "deforming in weird
        // ways" appearance, especially for shape keys with large deltas.
        // Recompute per-vertex smooth normals from the deformed positions
        // so lighting follows the morphed surface.
        RecomputeShapeKeyNormals(_deformedVertexData, vertexCount, floatsPerVertex);

        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        fixed (float* p = _deformedVertexData)
            _gl.BufferSubData(GLEnum.ArrayBuffer, 0, (uint)totalBytes, p);
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);

        _shapeKeyDirty = false;
    }

    /// <summary>
    /// Recomputes area-weighted smooth per-vertex normals from
    /// <paramref name="interleavedData"/>'s current (shape-key-deformed)
    /// positions and writes them back into the same buffer's normal slots.
    /// Vertices that aren't referenced by any triangle (degenerate/unused)
    /// keep whatever normal they already had. Uses <see cref="Indices"/>
    /// when present, otherwise treats every three consecutive vertices as
    /// a triangle, mirroring <see cref="GenerateNormals"/>'s topology
    /// handling but accumulating (rather than overwriting) per-vertex
    /// contributions for smooth shading.
    /// </summary>
    private void RecomputeShapeKeyNormals(float[] interleavedData, int vertexCount, int floatsPerVertex)
    {
        var accum = new vec3[vertexCount];

        void Accumulate(int i0, int i1, int i2)
        {
            int b0 = i0 * floatsPerVertex;
            int b1 = i1 * floatsPerVertex;
            int b2 = i2 * floatsPerVertex;

            var p0 = new vec3(interleavedData[b0],     interleavedData[b0 + 1], interleavedData[b0 + 2]);
            var p1 = new vec3(interleavedData[b1],     interleavedData[b1 + 1], interleavedData[b1 + 2]);
            var p2 = new vec3(interleavedData[b2],     interleavedData[b2 + 1], interleavedData[b2 + 2]);

            // Un-normalized face normal: magnitude is proportional to triangle
            // area, giving a standard area-weighted contribution to each of
            // its vertices' accumulated normal.
            vec3 faceNormal = vec3.Cross(p1 - p0, p2 - p0);

            accum[i0] += faceNormal;
            accum[i1] += faceNormal;
            accum[i2] += faceNormal;
        }

        if (Indices != null && Indices.Length >= 3)
        {
            for (int i = 0; i + 2 < Indices.Length; i += 3)
                Accumulate((int)Indices[i], (int)Indices[i + 1], (int)Indices[i + 2]);
        }
        else
        {
            for (int i = 0; i + 2 < vertexCount; i += 3)
                Accumulate(i, i + 1, i + 2);
        }

        for (int v = 0; v < vertexCount; v++)
        {
            vec3 n = accum[v];
            float lenSq = n.x * n.x + n.y * n.y + n.z * n.z;
            if (lenSq <= 1e-12f) continue; // unreferenced/degenerate — keep existing normal

            vec3 norm = n.Normalized;
            int vi = v * floatsPerVertex;
            interleavedData[vi + 3] = norm.x;
            interleavedData[vi + 4] = norm.y;
            interleavedData[vi + 5] = norm.z;
        }
    }

    // ── Material ──────────────────────────────────────────────────────────────
    //
    // A mesh owns one Material per surface (currently always exactly one — multi
    // -surface support is scaffolded via the surface accessors below but every
    // caller in this codebase renders single-material meshes). All shading
    // properties (base colour/alpha, texture, emission, double-sidedness, …)
    // live on that Material and are exposed below as simple properties so
    // existing call sites (loaders, UI panels, viewport) keep working exactly
    // as before while the actual storage/source-of-truth is the Material
    // object itself instead of duplicate loose fields on the mesh.

    private readonly List<Material> _surfaces = new() { new StandardMaterial() };

    public int GetSurfaceCount() => _surfaces.Count;
    public Material SurfaceGetMaterial(int index) => _surfaces[index];
    public void SurfaceSetMaterial(int index, Material mat) => _surfaces[index] = mat;

    /// <summary>
    /// The <see cref="StandardMaterial"/> driving this mesh's appearance
    /// (surface 0). If surface 0 has been replaced with a non-<see cref="StandardMaterial"/>
    /// via <see cref="SurfaceSetMaterial"/>, a fresh <see cref="StandardMaterial"/> is
    /// substituted so rendering always has valid shading data to read from.
    /// </summary>
    private StandardMaterial DefaultMaterial
    {
        get
        {
            if (_surfaces.Count == 0)
                _surfaces.Add(new StandardMaterial());

            if (_surfaces[0] is not StandardMaterial std)
            {
                std = new StandardMaterial();
                _surfaces[0] = std;
            }

            return std;
        }
    }

    /// <summary>
    /// OpenGL texture handle to bind when rendering this mesh.
    /// Set to 0 (default) to render with the flat <see cref="Albedo"/> colour.
    /// Backed by <see cref="StandardMaterial.AlbedoTexture"/>.
    /// </summary>
    public uint TextureId
    {
        get => DefaultMaterial.AlbedoTexture;
        set => DefaultMaterial.AlbedoTexture = value;
    }

    /// <summary>
    /// When true, back-face culling is disabled for this mesh so both sides are
    /// visible.  When false (default), only front faces (CCW winding) are drawn.
    /// Backed by <see cref="StandardMaterial.DoubleSided"/>.
    /// </summary>
    public bool DoubleSided
    {
        get => DefaultMaterial.DoubleSided;
        set => DefaultMaterial.DoubleSided = value;
    }

    /// <summary>
    /// Base colour passed to the fragment shader as <c>uAlbedo</c>.
    /// Backed by the RGB channels of <see cref="StandardMaterial.AlbedoColor"/>.
    /// </summary>
    public vec3 Albedo
    {
        get => DefaultMaterial.AlbedoColor.xyz;
        set
        {
            var mat = DefaultMaterial;
            mat.AlbedoColor = new vec4(value, mat.AlbedoColor.w);
        }
    }

    /// <summary>
    /// Overall opacity of this mesh [0 = fully transparent, 1 = fully opaque].
    /// Combined with the texture alpha (if any) in the fragment shader.
    /// Backed by the alpha channel of <see cref="StandardMaterial.AlbedoColor"/>.
    /// </summary>
    public float Alpha
    {
        get => DefaultMaterial.AlbedoColor.w;
        set
        {
            var mat = DefaultMaterial;
            mat.AlbedoColor = new vec4(mat.AlbedoColor.xyz, value);
        }
    }

    public vec4 BlendColor
    {
        get => DefaultMaterial.BlendColor;
        set => DefaultMaterial.BlendColor = value;
    }

    public vec4 MixColor
    {
        get => DefaultMaterial.MixColor;
        set => DefaultMaterial.MixColor = value;
    }

    /// <summary>
    /// When true, adds emissive lighting to the final shaded result.
    /// Backed by <see cref="StandardMaterial.EmissionEnabled"/>.
    /// </summary>
    public bool EmissionEnabled
    {
        get => DefaultMaterial.EmissionEnabled;
        set => DefaultMaterial.EmissionEnabled = value;
    }

    /// <summary>
    /// Emissive RGB colour added in the fragment shader when
    /// <see cref="EmissionEnabled"/> is true.
    /// Backed by the RGB channels of <see cref="StandardMaterial.Emission"/>.
    /// </summary>
    public vec3 EmissionColor
    {
        get => DefaultMaterial.Emission.xyz;
        set
        {
            var mat = DefaultMaterial;
            mat.Emission = new vec4(value, mat.Emission.w);
        }
    }

    /// <summary>
    /// Scalar multiplier for <see cref="EmissionColor"/>.
    /// Backed by <see cref="StandardMaterial.EmissionEnergyMultiplier"/>.
    /// </summary>
    public float EmissionEnergy
    {
        get => DefaultMaterial.EmissionEnergyMultiplier;
        set => DefaultMaterial.EmissionEnergyMultiplier = value;
    }

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="Mesh"/> and populates it with a default unit cube
    /// centred at the origin so the viewport always has something to show.
    /// Call <see cref="Upload"/> after construction (or after filling custom geometry).
    /// </summary>
    public Mesh(GL gl)
    {
        _gl = gl;
        //BuildDefaultCube();
        //Upload();
    }

    /// <summary>
    /// Creates a <see cref="Mesh"/> with caller-supplied geometry.
    /// <paramref name="vertices"/> and <paramref name="normals"/> must be the
    /// same length; pass an empty/null normals list to auto-generate flat normals.
    /// </summary>
    public Mesh(GL gl, IEnumerable<vec3> vertices, IEnumerable<vec3>? normals = null, uint[]? indices = null)
    {
        _gl = gl;
        Vertices.AddRange(vertices);
        if (normals != null) Normals.AddRange(normals);
        Indices = indices;
        Upload();
    }

    /// <summary>
    /// Creates an independent deep copy of this mesh: geometry, skinning data,
    /// shape keys, animation state, and material are all copied so edits made
    /// to the clone (material colour, shape-key weights, texture, …) never
    /// affect the original instance and vice versa. The clone gets its own
    /// GPU buffers via <see cref="Upload"/>.
    /// </summary>
    public Mesh Clone()
    {
        var clone = new Mesh(_gl);

        clone.Vertices.AddRange(Vertices);
        clone.Normals.AddRange(Normals);
        clone.TexCoords.AddRange(TexCoords);
        clone.BoneIndices.AddRange(BoneIndices);
        clone.BoneWeights.AddRange(BoneWeights);
        clone.BoneNames.AddRange(BoneNames);
        clone.BoneInverseBindMatrices.AddRange(BoneInverseBindMatrices);
        clone.Indices = Indices != null ? (uint[])Indices.Clone() : null;

        clone.AnimationKey        = AnimationKey;
        clone.Unlit                = Unlit;
        clone.DepthTestDisabled    = DepthTestDisabled;
        clone.IsTranslucent        = IsTranslucent;
        clone.SortDepth            = SortDepth;
        clone.PickOnly             = PickOnly;

        foreach (var sk in ShapeKeys)
        {
            clone.ShapeKeys.Add(new ShapeKey
            {
                Name   = sk.Name,
                Deltas = (float[])sk.Deltas.Clone(),
                Weight = sk.Weight
            });
        }

        // Deep-copy surface 0's material so palette/colour edits on the clone
        // never write through to the source mesh's shared StandardMaterial.
        var srcMat = DefaultMaterial;
        var dstMat = clone.DefaultMaterial;
        dstMat.AlbedoColor              = srcMat.AlbedoColor;
        dstMat.BlendColor               = srcMat.BlendColor;
        dstMat.MixColor                 = srcMat.MixColor;
        dstMat.AlbedoTexture            = srcMat.AlbedoTexture;
        dstMat.Metallic                 = srcMat.Metallic;
        dstMat.Roughness                = srcMat.Roughness;
        dstMat.NormalEnabled             = srcMat.NormalEnabled;
        dstMat.NormalTexture             = srcMat.NormalTexture;
        dstMat.Transparency              = srcMat.Transparency;
        dstMat.EmissionEnabled           = srcMat.EmissionEnabled;
        dstMat.Emission                  = srcMat.Emission;
        dstMat.EmissionEnergyMultiplier  = srcMat.EmissionEnergyMultiplier;
        dstMat.DoubleSided               = srcMat.DoubleSided;

        if (Vertices.Count > 0)
            clone.Upload();

        return clone;
    }

    protected virtual void GenerateVertices()
    {
        
    }

    /// <summary>
    /// Generates flat per-vertex normals from the current <see cref="Vertices"/> and
    /// <see cref="Indices"/> (or sequential triangles when no index buffer is set).
    /// Each triangle contributes the same face normal to all three of its vertices;
    /// shared vertices keep the normal of the last triangle that referenced them.
    /// Call this after populating <see cref="Vertices"/> (and optionally <see cref="Indices"/>)
    /// but before <see cref="Upload"/>.
    /// </summary>
    protected void GenerateNormals()
    {
        Normals.Clear();

        // Initialise one normal slot per vertex.
        for (int i = 0; i < Vertices.Count; i++)
            Normals.Add(vec3.Zero);

        if (Indices != null && Indices.Length >= 3)
        {
            // Index-aware: iterate over every triangle defined by the EBO.
            for (int i = 0; i + 2 < Indices.Length; i += 3)
            {
                uint i0 = Indices[i];
                uint i1 = Indices[i + 1];
                uint i2 = Indices[i + 2];

                vec3 edge1 = Vertices[(int)i1] - Vertices[(int)i0];
                vec3 edge2 = Vertices[(int)i2] - Vertices[(int)i0];
                vec3 n = vec3.Cross(edge1, edge2).Normalized;

                Normals[(int)i0] = n;
                Normals[(int)i1] = n;
                Normals[(int)i2] = n;
            }
        }
        else
        {
            // Non-indexed: every three consecutive vertices form a triangle.
            for (int i = 0; i + 2 < Vertices.Count; i += 3)
            {
                vec3 edge1 = Vertices[i + 1] - Vertices[i];
                vec3 edge2 = Vertices[i + 2] - Vertices[i];
                vec3 n = vec3.Cross(edge1, edge2).Normalized;

                Normals[i]     = n;
                Normals[i + 1] = n;
                Normals[i + 2] = n;
            }
        }

        // Ensure any leftover slots are not zero (shouldn't happen, but guard anyway).
        for (int i = 0; i < Normals.Count; i++)
            if (Normals[i] == vec3.Zero) Normals[i] = vec3.UnitY;
    }

    // ── Default geometry ──────────────────────────────────────────────────────

    protected void BuildDefaultCube()
    {
        // A unit cube (side 1) centred at origin, with per-face flat normals.
        // 6 faces × 2 triangles × 3 vertices = 36 vertices (no index buffer).
        var faces = new (vec3 normal, vec3[] quad)[]
        {
            // +Z front
            (new vec3(0,0,1),  new[]{ new vec3(-0.5f,-0.5f, 0.5f), new vec3( 0.5f,-0.5f, 0.5f), new vec3( 0.5f, 0.5f, 0.5f), new vec3(-0.5f, 0.5f, 0.5f) }),
            // -Z back
            (new vec3(0,0,-1), new[]{ new vec3( 0.5f,-0.5f,-0.5f), new vec3(-0.5f,-0.5f,-0.5f), new vec3(-0.5f, 0.5f,-0.5f), new vec3( 0.5f, 0.5f,-0.5f) }),
            // +Y top
            (new vec3(0,1,0),  new[]{ new vec3(-0.5f, 0.5f, 0.5f), new vec3( 0.5f, 0.5f, 0.5f), new vec3( 0.5f, 0.5f,-0.5f), new vec3(-0.5f, 0.5f,-0.5f) }),
            // -Y bottom
            (new vec3(0,-1,0), new[]{ new vec3(-0.5f,-0.5f,-0.5f), new vec3( 0.5f,-0.5f,-0.5f), new vec3( 0.5f,-0.5f, 0.5f), new vec3(-0.5f,-0.5f, 0.5f) }),
            // +X right
            (new vec3(1,0,0),  new[]{ new vec3( 0.5f,-0.5f, 0.5f), new vec3( 0.5f,-0.5f,-0.5f), new vec3( 0.5f, 0.5f,-0.5f), new vec3( 0.5f, 0.5f, 0.5f) }),
            // -X left
            (new vec3(-1,0,0), new[]{ new vec3(-0.5f,-0.5f,-0.5f), new vec3(-0.5f,-0.5f, 0.5f), new vec3(-0.5f, 0.5f, 0.5f), new vec3(-0.5f, 0.5f,-0.5f) }),
        };

        foreach (var (normal, quad) in faces)
        {
            // Two triangles per quad: (0,1,2) and (0,2,3)
            foreach (int i in new[] { 0, 1, 2, 0, 2, 3 })
            {
                Vertices.Add(quad[i]);
                Normals.Add(normal);
            }
        }
    }

    // ── GPU upload ────────────────────────────────────────────────────────────

    /// <summary>
    /// Uploads <see cref="Vertices"/> + <see cref="Normals"/> (or auto-generated
    /// flat normals) to a VAO/VBO.  Safe to call again after geometry changes —
    /// old GPU resources are deleted first.
    /// </summary>
    public unsafe void Upload()
    {
        // Compile shader on first upload.
        if (_shader == null)
        {
            _shader = new Shader(_gl);
            _shader.CompileShader("simple.vert", "simple.frag");
        }

        // Clean up previous GPU resources.
        if (_vao != 0)
        {
            _gl.DeleteVertexArrays(1, _vao);
            _gl.DeleteBuffers(1, _vbo);
            if (_ebo != 0) _gl.DeleteBuffers(1, _ebo);
            if (_skinVbo != 0) _gl.DeleteBuffers(1, _skinVbo);
            _vao = _vbo = _ebo = _skinVbo = 0;
        }

        if (Vertices.Count == 0) return;

        // Auto-generate flat normals if none were supplied by the subclass.
        if (Normals.Count != Vertices.Count)
            GenerateNormals();

        // Pad TexCoords to match vertex count with (0,0) if not provided.
        bool hasUVs = TexCoords.Count == Vertices.Count;

        bool isSkinned = IsSkinned;

        // Interleave: [ px py pz nx ny nz u v ] per vertex
        const int floatsPerVertex = 8;
        float[] data = new float[Vertices.Count * floatsPerVertex];
        for (int i = 0; i < Vertices.Count; i++)
        {
            data[i * floatsPerVertex + 0] = Vertices[i].x;
            data[i * floatsPerVertex + 1] = Vertices[i].y;
            data[i * floatsPerVertex + 2] = Vertices[i].z;
            data[i * floatsPerVertex + 3] = Normals[i].x;
            data[i * floatsPerVertex + 4] = Normals[i].y;
            data[i * floatsPerVertex + 5] = Normals[i].z;
            data[i * floatsPerVertex + 6] = hasUVs ? TexCoords[i].x : 0f;
            data[i * floatsPerVertex + 7] = hasUVs ? TexCoords[i].y : 0f;
        }

        _gl.GenVertexArrays(1, out _vao);
        _gl.GenBuffers(1, out _vbo);

        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        _gl.BufferData(GLEnum.ArrayBuffer, (uint)(data.Length * sizeof(float)), data, GLEnum.StaticDraw);

        uint stride = (uint)(floatsPerVertex * sizeof(float));
        // location 0: position
        _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, 0);
        _gl.EnableVertexAttribArray(0);
        // location 1: normal
        _gl.VertexAttribPointer(1, 3, GLEnum.Float, false, stride, 3 * sizeof(float));
        _gl.EnableVertexAttribArray(1);
        // location 2: texcoord
        _gl.VertexAttribPointer(2, 2, GLEnum.Float, false, stride, 6 * sizeof(float));
        _gl.EnableVertexAttribArray(2);

        // Skinning attributes
        if (isSkinned)
        {
            const int boneIndicesPerVertex = 4;
            const int boneWeightsPerVertex = 4;
            const int skinStride = boneIndicesPerVertex * sizeof(int) + boneWeightsPerVertex * sizeof(float);
            byte[] skinData = new byte[Vertices.Count * skinStride];

            for (int i = 0; i < Vertices.Count; i++)
            {
                int offset = i * skinStride;
                // Bone indices (int)
                BitConverter.GetBytes(BoneIndices[i].x).CopyTo(skinData, offset + 0);
                BitConverter.GetBytes(BoneIndices[i].y).CopyTo(skinData, offset + 4);
                BitConverter.GetBytes(BoneIndices[i].z).CopyTo(skinData, offset + 8);
                BitConverter.GetBytes(BoneIndices[i].w).CopyTo(skinData, offset + 12);
                // Weights
                BitConverter.GetBytes(BoneWeights[i].x).CopyTo(skinData, offset + 16);
                BitConverter.GetBytes(BoneWeights[i].y).CopyTo(skinData, offset + 20);
                BitConverter.GetBytes(BoneWeights[i].z).CopyTo(skinData, offset + 24);
                BitConverter.GetBytes(BoneWeights[i].w).CopyTo(skinData, offset + 28);
            }

            _gl.GenBuffers(1, out _skinVbo);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _skinVbo);
            fixed (byte* p = skinData)
                _gl.BufferData(GLEnum.ArrayBuffer, (uint)skinData.Length, p, GLEnum.StaticDraw);

            uint skinStrideU = (uint)skinStride;
            // location 3: bone indices
            _gl.VertexAttribIPointer(3, 4, GLEnum.Int, skinStrideU, 0);
            _gl.EnableVertexAttribArray(3);
            // location 4: bone weights
            _gl.VertexAttribPointer(4, 4, GLEnum.Float, false, skinStrideU, boneIndicesPerVertex * sizeof(int));
            _gl.EnableVertexAttribArray(4);
        }

        // Index buffer
        if (Indices is { Length: > 0 })
        {
            _gl.GenBuffers(1, out _ebo);
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, _ebo);
            _gl.BufferData(GLEnum.ElementArrayBuffer, (uint)(Indices.Length * sizeof(uint)), Indices, GLEnum.StaticDraw);
        }

        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindVertexArray(0);

        // Compute local-space bounding sphere from the vertex positions.
        ComputeBoundingSphere();

        // Cache the freshly-built interleaved base data so the shape-key
        // refresh path can rebuild the VBO from it without re-interleaving
        // every time.  Any previous deformed buffer is invalidated.
        _baseVertexData    = data;
        _deformedVertexData = null;
        // Only mark dirty if the model already declares shape keys with
        // non-zero weights (rare on initial load — most start at 0).  When
        // shape keys are added later via AddShapeKey the dirty flag is set
        // there directly.
        if (ShapeKeys.Count > 0)
        {
            foreach (var t in ShapeKeys)
            {
                if (t.Weight != 0f) { _shapeKeyDirty = true; break; }
            }
        }
    }

    private void ComputeBoundingSphere()
    {
        if (Vertices.Count == 0)
        {
            BoundingSphereCenter = vec3.Zero;
            BoundingSphereRadius = 0.5f;
            return;
        }

        vec3 min = new(float.MaxValue);
        vec3 max = new(float.MinValue);
        foreach (var v in Vertices)
        {
            min = vec3.Min(min, v);
            max = vec3.Max(max, v);
        }

        BoundingSphereCenter = (min + max) * 0.5f;
        BoundingSphereRadius = MathF.Sqrt(
            vec3.DistanceSqr(min, max)) * 0.5f;

        if (BoundingSphereRadius < 0.001f)
            BoundingSphereRadius = 0.5f;
    }

    // ── Point light data (set by Viewport before each draw call) ─────────────

    /// <summary>
    /// The lights this specific draw call should use. Repopulated by
    /// <c>Viewport.SelectPointLightsForMesh</c> immediately before every
    /// individual mesh's <see cref="Render"/> call — it is <b>not</b> the full
    /// list of every light in the scene. Each mesh gets only its own
    /// nearest/brightest/longest-range lights (mirroring how Godot's Forward
    /// Mobile / Compatibility renderers pick lights per mesh instance), so a
    /// mesh's lighting never depends on how many other lights exist elsewhere
    /// in the scene. Each entry:
    ///   pos               – world position of the light
    ///   color             – RGB colour
    ///   range             – maximum influence distance
    ///   energy            – overall brightness multiplier
    ///   shadowIndex       – which point-shadow cubemap to sample (-1 = none)
    ///   direction         – unit forward direction in world space (only used by spot lights;
    ///                       (0,0,0) for point lights)
    ///   spotCosOuterAngle – cosine of the outer (full-intensity) spot-cone half-angle;
    ///                       0 for point lights (disables the cone test in the shader)
    ///   spotCosInnerAngle – cosine of the inner (zero-intensity) spot-cone half-angle;
    ///                       0 for point lights
    /// </summary>
    public static readonly List<(
        vec3 pos,
        vec3 color,
        float range,
        float energy,
        int   shadowIndex,
        vec3  direction,
        float spotCosOuterAngle,
        float spotCosInnerAngle)> PointLights = new();

    /// <summary>
    /// Global ambient light colour for lit meshes.
    /// Set by project settings UI; defaults to neutral white.
    /// </summary>
    public static vec3 GlobalAmbientColor = vec3.Ones;

    /// <summary>
    /// Global ambient light strength multiplier for lit meshes.
    /// </summary>
    public static float GlobalAmbientStrength = 0.35f;

    /// <summary>
    /// Global fill light (directional) colour for lit meshes.
    /// Set by project settings UI; defaults to light gray (0.85, 0.85, 0.85).
    /// </summary>
    public static vec3 GlobalFillLightColor = new vec3(0.85f, 0.85f, 0.85f);

    /// <summary>
    /// Global fill light strength multiplier for lit meshes.
    /// </summary>
    public static float GlobalFillLightStrength = 1f;

    public static vec3 GlobalSunFillLightColor = new(1f, 0.96862745f, 0.89411765f);
    public static float GlobalSunFillLightStrength = 0.25f;
    public static vec3 GlobalSunFillLightDirection = vec3.UnitY;
    public static bool SunFillLightCastsShadows = true;
    public static vec3 GlobalMoonFillLightColor = new(0.6f, 0.65f, 1f);
    public static float GlobalMoonFillLightStrength = 0.1f;
    public static vec3 GlobalMoonFillLightDirection = vec3.UnitY;
    public static bool MoonFillLightCastsShadows = false;
    public static bool MainFillLightCastsShadows = true;
    public static vec3 GlobalCameraPosition;
    public static bool FogEnabled;
    public static vec3 FogColor = new(0.5764706f, 0.5764706f, 1f);
    public static float FogDistance = 10000f;
    public static float FogFadeSize = 2000f;
    public static float FogHeight = 1250f;
    public static bool HeightFogEnabled;
    public static vec3 HeightFogColor = new(0.5764706f, 0.5764706f, 1f);
    public static float HeightFogSize = 4000f;
    public static float HeightFogOffset = -3850f;

    /// <summary>
    /// Export-only shadow state configured by <see cref="CameraViewport"/> when
    /// high-quality rendered capture is enabled.
    /// </summary>
    public static bool ShadowsEnabled = false;
    public static uint ShadowMapTexture = 0;
    public static mat4 ShadowLightSpaceMatrix = mat4.Identity;
    public static int ShadowDebugMode = 0;
    public static bool DirectionalShadowEnabled = true;
    public static float ShadowBlurStrength = 1f;

    /// <summary>
    /// Must match <c>MAX_POINT_SHADOWS</c> in simple.frag.
    /// </summary>
    public static readonly uint[] PointShadowCubeTextures = new uint[8];

    /// <summary>
    /// Elapsed seconds since the last frame.  Set by the Viewport once per frame
    /// before any meshes are rendered so animated textures advance correctly.
    /// </summary>
    public static double DeltaTime = 0.0;

    /// <summary>
    /// Global gate for animated texture time progression.
    /// When false, animated textures hold their current frame.
    /// </summary>
    public static bool AdvanceAnimatedTextures = true;

    /// <summary>
    /// Shared scene-uniform buffer set by Viewport once per frame.
    /// When non-null, Mesh.Render binds it to the current shader program and
    /// skips uploading the scene-constant lighting uniforms that it contains.
    /// </summary>
    public static SceneUniformBuffer? SceneUBO;

    /// <summary>
    /// Global playback speed multiplier for animated textures.
    /// A value of 1.0 reproduces vanilla 20 FPS timing.
    /// </summary>
    public static double AnimatedTextureSpeedScale = 1.0;

    // ── Rendering ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws the mesh using the supplied MVP matrices.
    /// The shader must already be loaded; call <see cref="Upload"/> before the
    /// first frame.
    /// </summary>
    public unsafe void Render(mat4 model, mat4 view, mat4 proj)
    {
        if (_vao == 0 || _shader == null)
        {
            Console.Error.WriteLine($"Render skipped: vao={_vao} shader null={_shader == null}");
            return;
        }

        // Apply any pending shape-key weight changes to the GPU VBO before drawing.
        if (HasShapeKeys)
            RefreshShapeKeyGeometry();

        _gl.UseProgram(_shader.ShaderProgram);

        // Bind the shared scene UBO (initializes link on first use).
        SceneUBO?.EnsureInitialized(_shader.ShaderProgram);

        // Combine into single MVP matrix exactly as the opengl-tutorial.org tutorial does:
        //   glm::mat4 mvp = Projection * View * Model;
        //   glUniformMatrix4fv(id, 1, GL_FALSE, &mvp[0][0]);
        // GlmSharp mat4*mat4 and mat4*vec4 use column-vector convention (same as GLM).
        mat4 mvp = proj * view * model;
        SetUniformMat4("uMVP",   mvp);
        SetUniformMat4("uModel", model);

        SetUniformBool("uIsSkinned", IsSkinned);
        if (IsSkinned && BoneMatrices != null)
        {
            const int maxBones = 64;
            int count = Math.Min(BoneMatrices.Count, maxBones);
            for (int i = 0; i < count; i++)
                SetUniformMat4($"uBoneMatrices[{i}]", BoneMatrices[i]);
        }

        SetUniformVec3("uAlbedo", Albedo);
        SetUniformVec4("uBlendColor", BlendColor);
        SetUniformVec4("uMixColor", MixColor);
        SetUniformFloat("uAlpha", Alpha);
        SetUniformBool("uEmissionEnabled", EmissionEnabled);
        SetUniformVec3("uEmissionColor", EmissionColor);
        SetUniformFloat("uEmissionEnergy", EmissionEnergy);
        SetUniformBool("uUseShadowMap", DirectionalShadowEnabled && ShadowsEnabled && !Unlit && ShadowMapTexture != 0);
        SetUniformFloat("uShadowBlurStrength", ShadowBlurStrength);

        // The rest of the lighting data (uLightDir, uLightColor, uAmbient, fill
        // lights, shadow flags, uLightSpaceMatrix, uShadowDebugMode) comes from
        // the shared SceneData UBO uploaded once per frame by the Viewport.
        SetUniformBool("uIsUnlit", Unlit);
        SetUniformVec3("uCameraPosition", GlobalCameraPosition);
        SetUniformBool("uFogEnabled", FogEnabled);
        SetUniformVec3("uFogColor", FogColor);
        SetUniformFloat("uFogDistance", FogDistance);
        SetUniformFloat("uFogFadeSize", FogFadeSize);
        SetUniformFloat("uFogHeight", FogHeight);
        SetUniformBool("uHeightFogEnabled", HeightFogEnabled);
        SetUniformVec3("uHeightFogColor", HeightFogColor);
        SetUniformFloat("uHeightFogSize", HeightFogSize);
        SetUniformFloat("uHeightFogOffset", HeightFogOffset);

        // ── Point lights ──────────────────────────────────────────────────────
        // Must match MAX_POINT_LIGHTS in simple.frag.
        const int maxLights = 32;
        int lightCount = Math.Min(PointLights.Count, maxLights);
        SetUniformInt("uPointLightCount", lightCount);
        for (int i = 0; i < lightCount; i++)
        {
            var (lpos, lcol, lrange, lenergy, lshadowIndex, ldir, lspotOuter, lspotInner) = PointLights[i];
            SetUniformVec3($"uPointLightPos[{i}]",   lpos);
            SetUniformVec3($"uPointLightColor[{i}]", lcol);
            SetUniformFloat($"uPointLightRange[{i}]",  lrange);
            SetUniformFloat($"uPointLightEnergy[{i}]", lenergy);
            SetUniformInt($"uPointLightShadowIndex[{i}]", lshadowIndex);
            SetUniformVec3($"uPointLightDir[{i}]", ldir);
            SetUniformFloat($"uPointLightSpotCosOuter[{i}]", lspotOuter);
            SetUniformFloat($"uPointLightSpotCosInner[{i}]", lspotInner);
        }

        // ── Animation UV offset ───────────────────────────────────────────────
        float texOffsetV = 0f;
        float texScaleV  = 1f;

        if (!string.IsNullOrEmpty(AnimationKey) &&
            TerrainAtlas.AnimatedTextures.TryGetValue(AnimationKey, out var animInfo))
        {
            if (AdvanceAnimatedTextures)
                _animTime += DeltaTime * AnimatedTextureSpeedScale;

            double ticksPerFrame = animInfo.FrameTime * SecondsPerTick;
            double totalDuration = animInfo.Frames.Length * ticksPerFrame;
            double t = _animTime % totalDuration;
            int sequenceIndex = (int)(t / ticksPerFrame);
            sequenceIndex = Math.Clamp(sequenceIndex, 0, animInfo.Frames.Length - 1);
            int frameIndex = animInfo.Frames[sequenceIndex];

            // Each frame occupies (frameH / totalH) of the texture in V
            texScaleV  = (float)animInfo.FrameHeight / (animInfo.FrameHeight * animInfo.TotalFrames);
            texOffsetV = frameIndex * texScaleV;
        }

        SetUniformVec2("uTexOffset",  new vec2(0f, texOffsetV));
        SetUniformFloat("uTexScaleV", texScaleV);

        // Texture binding
        bool useTexture = TextureId != 0 && TexCoords.Count == Vertices.Count;
        SetUniformBool("uUseTexture", useTexture);
        if (useTexture)
        {
            _gl.ActiveTexture(GLEnum.Texture0);
            _gl.BindTexture(GLEnum.Texture2D, TextureId);
            SetUniformInt("uTexture", 0);
        }

        if (ShadowsEnabled && !Unlit && ShadowMapTexture != 0)
        {
            _gl.ActiveTexture(GLEnum.Texture1);
            _gl.BindTexture(GLEnum.Texture2D, ShadowMapTexture);
            SetUniformInt("uShadowMap", 1);
            _gl.ActiveTexture(GLEnum.Texture0);
        }

        if (!Unlit)
        {
            for (int i = 0; i < PointShadowCubeTextures.Length; i++)
            {
                uint cubeTex = PointShadowCubeTextures[i];
                if (cubeTex == 0)
                    continue;

                _gl.ActiveTexture((GLEnum)((int)GLEnum.Texture0 + 2 + i));
                _gl.BindTexture(GLEnum.TextureCubeMap, cubeTex);
                SetUniformInt($"uPointShadowMaps[{i}]", 2 + i);
            }
            _gl.ActiveTexture(GLEnum.Texture0);
        }

        if (DoubleSided) _gl.Disable(GLEnum.CullFace);

        // Overlay meshes render on top of all geometry: depth test off, depth writes off.
        if (DepthTestDisabled)
        {
            _gl.Disable(GLEnum.DepthTest);
            _gl.DepthMask(false);
        }

        _gl.BindVertexArray(_vao);

        if (Indices != null && _ebo != 0)
            _gl.DrawElements(GLEnum.Triangles, (uint)Indices.Length, GLEnum.UnsignedInt, (void*)0);
        else
            _gl.DrawArrays(GLEnum.Triangles, 0, (uint)Vertices.Count);

        _gl.BindVertexArray(0);

        // Restore depth state after overlay draw.
        if (DepthTestDisabled)
        {
            _gl.Enable(GLEnum.DepthTest);
            _gl.DepthFunc(GLEnum.Less);
            _gl.DepthMask(true);
        }

        if (DoubleSided) _gl.Enable(GLEnum.CullFace);

        if (useTexture)
        {
            _gl.ActiveTexture(GLEnum.Texture0);
            _gl.BindTexture(GLEnum.Texture2D, 0);
        }

        if (ShadowsEnabled && !Unlit && ShadowMapTexture != 0)
        {
            _gl.ActiveTexture(GLEnum.Texture1);
            _gl.BindTexture(GLEnum.Texture2D, 0);
            _gl.ActiveTexture(GLEnum.Texture0);
        }

        if (!Unlit)
        {
            for (int i = 0; i < PointShadowCubeTextures.Length; i++)
            {
                if (PointShadowCubeTextures[i] == 0)
                    continue;

                _gl.ActiveTexture((GLEnum)((int)GLEnum.Texture0 + 2 + i));
                _gl.BindTexture(GLEnum.TextureCubeMap, 0);
            }
            _gl.ActiveTexture(GLEnum.Texture0);
        }
    }

    public unsafe void RenderShadow(Shader shader, mat4 lightViewProj, mat4 model)
    {
        if (_vao == 0 || shader == null)
            return;

        _gl.UseProgram(shader.ShaderProgram);
        SetUniformMat4(shader, "uMVP", lightViewProj * model);

        SetUniformBool(shader, "uIsSkinned", IsSkinned);
        if (IsSkinned && BoneMatrices != null)
        {
            const int maxBones = 64;
            int count = Math.Min(BoneMatrices.Count, maxBones);
            for (int i = 0; i < count; i++)
                SetUniformMat4(shader, $"uBoneMatrices[{i}]", BoneMatrices[i]);
        }

        float texOffsetV = 0f;
        float texScaleV  = 1f;
        if (!string.IsNullOrEmpty(AnimationKey) &&
            TerrainAtlas.AnimatedTextures.TryGetValue(AnimationKey, out var animInfo))
        {
            double ticksPerFrame = animInfo.FrameTime * SecondsPerTick;
            double totalDuration = animInfo.Frames.Length * ticksPerFrame;
            double t = _animTime % totalDuration;
            int sequenceIndex = (int)(t / ticksPerFrame);
            sequenceIndex = Math.Clamp(sequenceIndex, 0, animInfo.Frames.Length - 1);
            int frameIndex = animInfo.Frames[sequenceIndex];
            texScaleV  = (float)animInfo.FrameHeight / (animInfo.FrameHeight * animInfo.TotalFrames);
            texOffsetV = frameIndex * texScaleV;
        }

        bool useTexture = TextureId != 0 && TexCoords.Count == Vertices.Count;
        SetUniformFloat(shader, "uAlpha", Alpha);
        SetUniformBool(shader, "uUseTexture", useTexture);
        SetUniformVec2(shader, "uTexOffset", new vec2(0f, texOffsetV));
        SetUniformFloat(shader, "uTexScaleV", texScaleV);

        if (useTexture)
        {
            _gl.ActiveTexture(GLEnum.Texture0);
            _gl.BindTexture(GLEnum.Texture2D, TextureId);
            SetUniformInt(shader, "uTexture", 0);
        }

        if (DoubleSided) _gl.Disable(GLEnum.CullFace);

        _gl.BindVertexArray(_vao);

        if (Indices != null && _ebo != 0)
            _gl.DrawElements(GLEnum.Triangles, (uint)Indices.Length, GLEnum.UnsignedInt, (void*)0);
        else
            _gl.DrawArrays(GLEnum.Triangles, 0, (uint)Vertices.Count);

        _gl.BindVertexArray(0);

        if (useTexture)
        {
            _gl.ActiveTexture(GLEnum.Texture0);
            _gl.BindTexture(GLEnum.Texture2D, 0);
        }

        if (DoubleSided) _gl.Enable(GLEnum.CullFace);
    }

    public unsafe void RenderPointShadow(Shader shader, mat4 lightViewProj, mat4 model, vec3 lightPos, float farPlane)
    {
        if (_vao == 0 || shader == null)
            return;

        _gl.UseProgram(shader.ShaderProgram);
        SetUniformMat4(shader, "uLightViewProj", lightViewProj);
        SetUniformMat4(shader, "uModel", model);
        SetUniformVec3(shader, "uLightPos", lightPos);
        SetUniformFloat(shader, "uFarPlane", farPlane);

        SetUniformBool(shader, "uIsSkinned", IsSkinned);
        if (IsSkinned && BoneMatrices != null)
        {
            const int maxBones = 64;
            int count = Math.Min(BoneMatrices.Count, maxBones);
            for (int i = 0; i < count; i++)
                SetUniformMat4(shader, $"uBoneMatrices[{i}]", BoneMatrices[i]);
        }

        float texOffsetV = 0f;
        float texScaleV = 1f;
        if (!string.IsNullOrEmpty(AnimationKey) &&
            TerrainAtlas.AnimatedTextures.TryGetValue(AnimationKey, out var animInfo))
        {
            double ticksPerFrame = animInfo.FrameTime * SecondsPerTick;
            double totalDuration = animInfo.Frames.Length * ticksPerFrame;
            double t = _animTime % totalDuration;
            int sequenceIndex = (int)(t / ticksPerFrame);
            sequenceIndex = Math.Clamp(sequenceIndex, 0, animInfo.Frames.Length - 1);
            int frameIndex = animInfo.Frames[sequenceIndex];
            texScaleV = (float)animInfo.FrameHeight / (animInfo.FrameHeight * animInfo.TotalFrames);
            texOffsetV = frameIndex * texScaleV;
        }

        bool useTexture = TextureId != 0 && TexCoords.Count == Vertices.Count;
        SetUniformFloat(shader, "uAlpha", Alpha);
        SetUniformBool(shader, "uUseTexture", useTexture);
        SetUniformVec2(shader, "uTexOffset", new vec2(0f, texOffsetV));
        SetUniformFloat(shader, "uTexScaleV", texScaleV);

        if (useTexture)
        {
            _gl.ActiveTexture(GLEnum.Texture0);
            _gl.BindTexture(GLEnum.Texture2D, TextureId);
            SetUniformInt(shader, "uTexture", 0);
        }

        if (DoubleSided) _gl.Disable(GLEnum.CullFace);

        _gl.BindVertexArray(_vao);

        if (Indices != null && _ebo != 0)
            _gl.DrawElements(GLEnum.Triangles, (uint)Indices.Length, GLEnum.UnsignedInt, (void*)0);
        else
            _gl.DrawArrays(GLEnum.Triangles, 0, (uint)Vertices.Count);

        _gl.BindVertexArray(0);

        if (useTexture)
        {
            _gl.ActiveTexture(GLEnum.Texture0);
            _gl.BindTexture(GLEnum.Texture2D, 0);
        }

        if (DoubleSided) _gl.Enable(GLEnum.CullFace);
    }

    // ── Uniform helpers (cached locations via Shader) ─────────────────────────

    private unsafe void SetUniformMat4(string name, mat4 m)
    {
        int loc = _shader.GetUniformLocation(name);
        if (loc < 0) return;
        
        float[] f =
        {
            m.m00, m.m01, m.m02, m.m03,   // column 0
            m.m10, m.m11, m.m12, m.m13,   // column 1
            m.m20, m.m21, m.m22, m.m23,   // column 2
            m.m30, m.m31, m.m32, m.m33,   // column 3
        };
        fixed (float* p = f) _gl.UniformMatrix4(loc, 1, false, p);
    }

    private unsafe void SetUniformMat4(Shader shader, string name, mat4 m)
    {
        int loc = shader.GetUniformLocation(name);
        if (loc < 0) return;

        float[] f =
        {
            m.m00, m.m01, m.m02, m.m03,
            m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23,
            m.m30, m.m31, m.m32, m.m33,
        };
        fixed (float* p = f) _gl.UniformMatrix4(loc, 1, false, p);
    }

    private void SetUniformVec2(string name, vec2 v)
    {
        int loc = _shader.GetUniformLocation(name);
        if (loc >= 0) _gl.Uniform2(loc, v.x, v.y);
    }

    private void SetUniformVec2(Shader shader, string name, vec2 v)
    {
        int loc = shader.GetUniformLocation(name);
        if (loc >= 0) _gl.Uniform2(loc, v.x, v.y);
    }

    private void SetUniformVec3(string name, vec3 v)
    {
        int loc = _shader.GetUniformLocation(name);
        if (loc >= 0) _gl.Uniform3(loc, v.x, v.y, v.z);
    }

    private void SetUniformVec4(string name, vec4 v)
    {
        int loc = _shader.GetUniformLocation(name);
        if (loc >= 0) _gl.Uniform4(loc, v.x, v.y, v.z, v.w);
    }

    private void SetUniformVec3(Shader shader, string name, vec3 v)
    {
        int loc = shader.GetUniformLocation(name);
        if (loc >= 0) _gl.Uniform3(loc, v.x, v.y, v.z);
    }

    private void SetUniformBool(string name, bool value)
    {
        int loc = _shader.GetUniformLocation(name);
        if (loc >= 0) _gl.Uniform1(loc, value ? 1 : 0);
    }

    private void SetUniformBool(Shader shader, string name, bool value)
    {
        int loc = shader.GetUniformLocation(name);
        if (loc >= 0) _gl.Uniform1(loc, value ? 1 : 0);
    }

    private void SetUniformInt(string name, int value)
    {
        int loc = _shader.GetUniformLocation(name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    private void SetUniformInt(Shader shader, string name, int value)
    {
        int loc = shader.GetUniformLocation(name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    private void SetUniformFloat(string name, float value)
    {
        int loc = _shader.GetUniformLocation(name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    private void SetUniformFloat(Shader shader, string name, float value)
    {
        int loc = shader.GetUniformLocation(name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    // ── Pick pass ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Uploads the skinning uniforms (<c>uIsSkinned</c> and <c>uBoneMatrices</c>)
    /// to the supplied shader.  Callers such as the pick and silhouette passes
    /// use this after computing the current bone palette so skinned meshes are
    /// drawn in their deformed pose.
    /// </summary>
    public void ApplySkinningUniforms(Shader shader)
    {
        if (shader == null) return;

        SetUniformBool(shader, "uIsSkinned", IsSkinned);
        if (!IsSkinned || BoneMatrices == null) return;

        const int maxBones = 64;
        int count = Math.Min(BoneMatrices.Count, maxBones);
        for (int i = 0; i < count; i++)
            SetUniformMat4(shader, $"uBoneMatrices[{i}]", BoneMatrices[i]);
    }

    /// <summary>
    /// Draws the mesh geometry using whatever shader program is currently bound.
    /// Used by the colour-pick pass in <c>Viewport</c>, which sets up the flat
    /// pick shader and per-object uniforms (MVP, pick colour) before calling this.
    /// Only the position attribute (location 0) is needed; the pick shader ignores
    /// normals and UVs.
    /// </summary>
    public unsafe void RenderPickPass(GL gl)
    {
        if (_vao == 0) return;

        gl.BindVertexArray(_vao);

        if (Indices != null && _ebo != 0)
            gl.DrawElements(GLEnum.Triangles, (uint)Indices.Length, GLEnum.UnsignedInt, (void*)0);
        else
            gl.DrawArrays(GLEnum.Triangles, 0, (uint)Vertices.Count);

        gl.BindVertexArray(0);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_vao != 0) _gl.DeleteVertexArrays(1, _vao);
        if (_vbo != 0) _gl.DeleteBuffers(1, _vbo);
        if (_ebo != 0) _gl.DeleteBuffers(1, _ebo);
        if (_skinVbo != 0) _gl.DeleteBuffers(1, _skinVbo);
        _shader?.Dispose();
    }
}
