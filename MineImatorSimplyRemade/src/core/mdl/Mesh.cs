using GlmSharp;
using MineImatorSimplyRemade.core.mdl.material;
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

    private uint _vbo, _ebo, _vao;
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

    /// <summary>
    /// Optional index buffer (uint32).  Leave null for plain <c>DrawArrays</c>.
    /// </summary>
    public uint[]? Indices;

    // ── Texture ───────────────────────────────────────────────────────────────

    /// <summary>
    /// OpenGL texture handle to bind when rendering this mesh.
    /// Set to 0 (default) to render with the flat <see cref="Albedo"/> colour.
    /// </summary>
    public uint TextureId = 0;

    // ── Culling ───────────────────────────────────────────────────────────────

    /// <summary>
    /// When true, back-face culling is disabled for this mesh so both sides are
    /// visible.  When false (default), only front faces (CCW winding) are drawn.
    /// </summary>
    public bool DoubleSided = false;

    // ── Material ──────────────────────────────────────────────────────────────

    private readonly List<Material> _surfaces = new() { new Material() };

    public int GetSurfaceCount() => _surfaces.Count;
    public Material SurfaceGetMaterial(int index) => _surfaces[index];
    public void SurfaceSetMaterial(int index, Material mat) => _surfaces[index] = mat;

    // ── Albedo colour (set by material layer) ─────────────────────────────────

    /// <summary>Base colour passed to the fragment shader as <c>uAlbedo</c>.</summary>
    public vec3 Albedo = new vec3(0.8f, 0.3f, 0.02f);

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
        }

        if (Vertices.Count == 0) return;

        // Auto-generate flat normals if none were supplied by the subclass.
        if (Normals.Count != Vertices.Count)
            GenerateNormals();

        // Pad TexCoords to match vertex count with (0,0) if not provided.
        bool hasUVs = TexCoords.Count == Vertices.Count;

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

        // Index buffer
        if (Indices != null && Indices.Length > 0)
        {
            _gl.GenBuffers(1, out _ebo);
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, _ebo);
            _gl.BufferData(GLEnum.ElementArrayBuffer, (uint)(Indices.Length * sizeof(uint)), Indices, GLEnum.StaticDraw);
        }

        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

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
            Console.Error.WriteLine($"[Mesh] Render skipped: vao={_vao} shader null={_shader == null}");
            return;
        }

        _gl.UseProgram(_shader.ShaderProgram);

        // Combine into single MVP matrix exactly as the opengl-tutorial.org tutorial does:
        //   glm::mat4 mvp = Projection * View * Model;
        //   glUniformMatrix4fv(id, 1, GL_FALSE, &mvp[0][0]);
        // GlmSharp mat4*mat4 and mat4*vec4 use column-vector convention (same as GLM).
        mat4 mvp = proj * view * model;
        SetUniformMat4("uMVP",   mvp);
        SetUniformMat4("uModel", model);

        SetUniformVec3("uAlbedo",     Albedo);
        // Light travels from upper-right-front toward origin (world space)
        SetUniformVec3("uLightDir",   new vec3(1f, 1f, 1f).Normalized);
        SetUniformVec3("uLightColor", new vec3(0.85f, 0.85f, 0.85f));
        SetUniformVec3("uAmbient",    new vec3(0.35f, 0.35f, 0.35f));

        // Texture binding
        bool useTexture = TextureId != 0 && TexCoords.Count == Vertices.Count;
        SetUniformBool("uUseTexture", useTexture);
        if (useTexture)
        {
            _gl.ActiveTexture(GLEnum.Texture0);
            _gl.BindTexture(GLEnum.Texture2D, TextureId);
            SetUniformInt("uTexture", 0);
        }

        if (DoubleSided) _gl.Disable(GLEnum.CullFace);

        _gl.BindVertexArray(_vao);

        if (Indices != null && _ebo != 0)
            _gl.DrawElements(GLEnum.Triangles, (uint)Indices.Length, GLEnum.UnsignedInt, (void*)0);
        else
            _gl.DrawArrays(GLEnum.Triangles, 0, (uint)Vertices.Count);

        _gl.BindVertexArray(0);

        if (DoubleSided) _gl.Enable(GLEnum.CullFace);

        if (useTexture)
        {
            _gl.ActiveTexture(GLEnum.Texture0);
            _gl.BindTexture(GLEnum.Texture2D, 0);
        }
    }

    // ── Uniform helpers ───────────────────────────────────────────────────────

    private unsafe void SetUniformMat4(string name, mat4 m)
    {
        int loc = _gl.GetUniformLocation(_shader.ShaderProgram, name);
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

    private void SetUniformVec3(string name, vec3 v)
    {
        int loc = _gl.GetUniformLocation(_shader.ShaderProgram, name);
        if (loc >= 0) _gl.Uniform3(loc, v.x, v.y, v.z);
    }

    private void SetUniformBool(string name, bool value)
    {
        int loc = _gl.GetUniformLocation(_shader.ShaderProgram, name);
        if (loc >= 0) _gl.Uniform1(loc, value ? 1 : 0);
    }

    private void SetUniformInt(string name, int value)
    {
        int loc = _gl.GetUniformLocation(_shader.ShaderProgram, name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_vao != 0) _gl.DeleteVertexArrays(1, _vao);
        if (_vbo != 0) _gl.DeleteBuffers(1, _vbo);
        if (_ebo != 0) _gl.DeleteBuffers(1, _ebo);
        _shader?.Dispose();
    }
}
