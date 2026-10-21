using GlmSharp;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;
using Shader = MineImatorSimplyRemade.core.mdl.Shader;

namespace MineImatorSimplyRemade.core.ui;

/// <summary>
/// Draws Mine-imator–style wireframe gizmos for light objects each frame using
/// dynamic GL_LINES geometry — no pre-baked meshes required.
///
/// Point lights get:
///   • A small camera-facing circle at their origin (the "bulb" shape icon)
///   • A larger camera-facing range circle coloured by the light's own colour
///
/// Spot lights get:
///   • A small camera-facing circle at their origin
///   • An 8-point truncated-cone wireframe box (the shape icon)
///   • A central axis line from origin to cone tip
///   • Four edge lines from origin to the cone base rim
///   • A world-space circle at the cone base (range indicator)
///
/// All geometry is drawn with depth-test ALWAYS so gizmos stay visible through
/// scene geometry, and with no depth writes so they don't occlude each other.
/// </summary>
public sealed class LightGizmoRenderer : IDisposable
{
    // ── GL ─────────────────────────────────────────────────────────────────────
    private readonly GL _gl;

    // ── Shader (gizmo.vert / gizmo.frag — flat colour MVP) ─────────────────────
    private readonly Shader _shader;

    // ── Dynamic line buffer ─────────────────────────────────────────────────────
    // We accumulate all line segments for a frame into one CPU list, then upload
    // once and draw with a single DrawArrays call per colour group.
    private uint _vao;
    private uint _vbo;
    private const int InitialCapacityFloats = 4096; // pre-alloc; grown as needed
    private float[] _buf = new float[InitialCapacityFloats];
    private int _bufCount; // number of floats currently in _buf

    // ── Camera state (set per Draw call) ────────────────────────────────────────
    private mat4 _view;
    private mat4 _proj;
    private int  _viewportW;
    private int  _viewportH;
    private float _fovDeg;

    // Camera direction / position extracted from view matrix inverse each frame
    private vec3 _camPos;
    private vec3 _camForward;

    // ── Segment counts ──────────────────────────────────────────────────────────
    /// <summary>Segments for the camera-facing icon circle (bulb).</summary>
    private const int BulbSegments  = 16;

    /// <summary>Segments for range / cone-base circles.</summary>
    private const int RingSegments  = 32;

    // ── Constructor ─────────────────────────────────────────────────────────────

    public LightGizmoRenderer(GL gl)
    {
        _gl = gl;

        _shader = new Shader(gl);
        _shader.CompileShader("gizmo.vert", "gizmo.frag");

        // VAO / VBO for streaming line vertices (positions only; gizmo.vert
        // reads location 0 as vec3 aPos).
        gl.GenVertexArrays(1, out _vao);
        gl.GenBuffers(1, out _vbo);

        gl.BindVertexArray(_vao);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        // Allocate with STREAM_DRAW since we rewrite every frame.
        gl.BufferData(GLEnum.ArrayBuffer,
                      (uint)(InitialCapacityFloats * sizeof(float)),
                      (ReadOnlySpan<float>)ReadOnlySpan<float>.Empty,
                      GLEnum.StreamDraw);
        // location 0: position (xyz) — stride 12 bytes
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, 12, 0);
        gl.EnableVertexAttribArray(0);
        gl.BindVertexArray(0);
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
    }

    // ── Public entry point ──────────────────────────────────────────────────────

    /// <summary>
    /// Renders all light gizmos for every visible <see cref="LightSceneObject"/>
    /// in <paramref name="roots"/>.  Call once per frame after the main scene
    /// pass, while the main FBO is still bound.
    /// </summary>
    public void Draw(
        IEnumerable<SceneObject> roots,
        mat4 view, mat4 proj,
        int viewportW, int viewportH,
        float fovDeg)
    {
        _view      = view;
        _proj      = proj;
        _viewportW = viewportW;
        _viewportH = viewportH;
        _fovDeg    = fovDeg;

        // Extract camera position and forward from the view matrix inverse.
        // The view matrix is V = R * T, so V^-1 = T^-1 * R^-1.
        // The camera world position is the last column of V^-1, which equals
        // the negated translation rotated back: -R^T * t.
        mat4 viewInv = view.Inverse;
        _camPos     = new vec3(viewInv.m30, viewInv.m31, viewInv.m32);
        _camForward = new vec3(-view.m02, -view.m12, -view.m22).Normalized;

        // ── GL state ────────────────────────────────────────────────────────────
        uint prog = _shader.ShaderProgram;
        _gl.UseProgram(prog);

        _gl.Enable(GLEnum.DepthTest);
        _gl.DepthFunc(GLEnum.Always);   // always visible through walls
        _gl.DepthMask(false);
        _gl.Disable(GLEnum.CullFace);
        _gl.Enable(GLEnum.Blend);
        _gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        _gl.LineWidth(2f);

        // Upload shared MVP = proj * view (model = identity for screen-space
        // circles; world-space lines carry position in the vertex data itself).
        int mvpLoc   = _gl.GetUniformLocation(prog, "uMVP");
        int colorLoc = _gl.GetUniformLocation(prog, "uColor");

        // We use the same VAO/VBO for every batch; flush colour by colour.
        _gl.BindVertexArray(_vao);

        // Collect all lights then draw in batches grouped by colour so we
        // minimise shader uniform updates.
        var lights = new List<LightSceneObject>();
        CollectLights(roots, lights);

        foreach (var light in lights)
            DrawLight(light, mvpLoc, colorLoc);

        // ── Restore GL state ────────────────────────────────────────────────────
        _gl.BindVertexArray(0);
        _gl.LineWidth(1f);
        _gl.DepthMask(true);
        _gl.DepthFunc(GLEnum.Less);
        _gl.Enable(GLEnum.CullFace);
        _gl.CullFace(GLEnum.Back);
        _gl.Disable(GLEnum.Blend);
    }

    // ── Per-light dispatch ──────────────────────────────────────────────────────

    private void DrawLight(LightSceneObject light, int mvpLoc, int colorLoc)
    {
        mat4 world   = light.GetWorldMatrix();
        vec3 origin  = new vec3(world.m30, world.m31, world.m32);
        vec4 lc      = light.LightColor;
        vec3 col     = new vec3(lc.x, lc.y, lc.z);

        // Clamp colour so it is always visible even for very dark lights.
        col = vec3.Max(col, new vec3(0.35f));

        // ── Shape icon (always shown, white) ────────────────────────────────────
        SetColor(colorLoc, 1f, 1f, 1f, 0.9f);
        // Small camera-facing bulb circle (radius ≈ Mine-imator's "4" units,
        // but in metres; use 0.3 m for a reasonable viewport size).
        DrawCameraFacingCircle(origin, 0.3f, mvpLoc, BulbSegments);

        if (light.Type == LightType.Spot)
        {
            // 8-point truncated cone wireframe (same shape as Mine-imator).
            // Local cone opens along +Z to match the world matrix columns below.
            // Small end near origin, large end further in +Z.
            float s = 0.06f, l = 0.12f, d1 = -0.09f, d2 = 0.18f;
            DrawBox8(world,
                new vec3(-s, -s, d1), new vec3(-s,  s, d1),
                new vec3( s, -s, d1), new vec3( s,  s, d1),
                new vec3(-l, -l, d2), new vec3(-l,  l, d2),
                new vec3( l, -l, d2), new vec3( l,  l, d2),
                mvpLoc);

            // ── Spot bounds / guide ────────────────────────────────────────────
            float range   = MathF.Max(0.01f, light.LightRange);
            float halfAng = MathF.Max(0.1f,  light.LightSpotAngle * 0.5f);
            float sinH    = MathF.Sin(glm.Radians(halfAng));
            float cosH    = MathF.Cos(glm.Radians(halfAng));

            // Use the same matrix multiplication as the lighting system to get
            // the exact world-space aim direction (world * local +Z direction).
            vec3 aimAxis = new vec3((world * new vec4(0f, 0f, 1f, 0f)).xyz).Normalized;
            vec3 axisX   = new vec3((world * new vec4(1f, 0f, 0f, 0f)).xyz).Normalized;
            vec3 axisY   = new vec3((world * new vec4(0f, 1f, 0f, 0f)).xyz).Normalized;

            vec3 tipPos  = origin + aimAxis * range;          // cone tip in world
            vec3 basePos = origin + aimAxis * range * cosH;   // base centre

            // Axis line (origin → tip), coloured with light colour
            SetColor(colorLoc, col.x, col.y, col.z, 0.8f);
            DrawWorldLine(origin, tipPos, mvpLoc);

            // Four edge lines (origin → rim of cone base)
            float rimR = range * sinH;
            DrawWorldLine(origin, basePos + axisX *  rimR, mvpLoc);
            DrawWorldLine(origin, basePos + axisX * -rimR, mvpLoc);
            DrawWorldLine(origin, basePos + axisY *  rimR, mvpLoc);
            DrawWorldLine(origin, basePos + axisY * -rimR, mvpLoc);

            // World-space range circle at the cone base
            DrawWorldCircle(basePos, aimAxis, axisX, rimR, mvpLoc, RingSegments);
        }
        else // Point light
        {
            // ── Point light range circle ─────────────────────────────────────────
            float range = MathF.Max(0.01f, light.LightRange);
            SetColor(colorLoc, col.x, col.y, col.z, 0.7f);
            DrawCameraFacingCircle(origin, range, mvpLoc, RingSegments);
        }
    }

    // ── Primitive builders ──────────────────────────────────────────────────────

    /// <summary>
    /// Draws a camera-facing (billboard) circle centred at <paramref name="worldPos"/>
    /// with the given world-space radius.  The circle is expanded in screen space
    /// using the camera's right/up vectors — identical to Mine-imator's
    /// <c>view_shape_circle</c>.
    /// </summary>
    private void DrawCameraFacingCircle(vec3 worldPos, float radius, int mvpLoc, int segments)
    {
        // Project centre to clip space, then to NDC to get screen position.
        vec4 clip = _proj * _view * new vec4(worldPos, 1f);
        if (clip.w <= 0f) return; // behind camera

        // Perpendicular distance from camera to the point (along camera forward).
        float perpDist = vec3.Dot(worldPos - _camPos, _camForward);
        if (perpDist <= 0f) return;

        // Convert world-space radius to screen pixels using the same formula
        // Mine-imator uses: rad2D = (rad / cdist * render_height) / (fov / 58.72)
        float rad2D = (radius / perpDist * _viewportH) / (_fovDeg / 58.72f);

        // NDC of the centre
        float ndcX = clip.x / clip.w;
        float ndcY = clip.y / clip.w;

        // Screen-space centre (pixels, y-flipped from NDC)
        float cx = ( ndcX * 0.5f + 0.5f) * _viewportW;
        float cy = (-ndcY * 0.5f + 0.5f) * _viewportH;

        // Build a screen-space ortho MVP so we can emit pixel-space vertices
        // directly.  ortho(0, W, H, 0, -1, 1) maps pixel coords → NDC.
        // GlmSharp Ortho: left, right, bottom, top, near, far
        // Screen pixels: x=0 left, x=W right, y=0 top, y=H bottom.
        mat4 orthoMvp = mat4.Ortho(0f, _viewportW, _viewportH, 0f, -1f, 1f);
        UploadMVP(mvpLoc, orthoMvp);

        // Emit line-strip (GL_LINES: pair of verts per segment)
        _bufCount = 0;
        for (int i = 0; i < segments; i++)
        {
            float a0 = MathF.PI * 2f * i       / segments;
            float a1 = MathF.PI * 2f * (i + 1) / segments;
            EmitVertex(cx + MathF.Cos(a0) * rad2D, cy + MathF.Sin(a0) * rad2D, 0f);
            EmitVertex(cx + MathF.Cos(a1) * rad2D, cy + MathF.Sin(a1) * rad2D, 0f);
        }
        FlushLines();

        // Restore the world-space MVP for subsequent draws.
        UploadMVP(mvpLoc, _proj * _view);
    }

    /// <summary>
    /// Draws a world-space circle in a plane perpendicular to <paramref name="normal"/>.
    /// Used for the spotlight cone-base ring — same as Mine-imator's
    /// <c>view_shape_spotlight_guidecircle</c>.
    /// </summary>
    private void DrawWorldCircle(vec3 centre, vec3 normal, vec3 tangent, float radius, int mvpLoc, int segments)
    {
        UploadMVP(mvpLoc, _proj * _view);

        vec3 bitangent = vec3.Cross(normal, tangent).Normalized;

        _bufCount = 0;
        for (int i = 0; i < segments; i++)
        {
            float a0 = MathF.PI * 2f * i       / segments;
            float a1 = MathF.PI * 2f * (i + 1) / segments;
            vec3 p0  = centre + (tangent * MathF.Cos(a0) + bitangent * MathF.Sin(a0)) * radius;
            vec3 p1  = centre + (tangent * MathF.Cos(a1) + bitangent * MathF.Sin(a1)) * radius;
            EmitVertex(p0.x, p0.y, p0.z);
            EmitVertex(p1.x, p1.y, p1.z);
        }
        FlushLines();
    }

    /// <summary>Draws a single world-space line segment.</summary>
    private void DrawWorldLine(vec3 a, vec3 b, int mvpLoc)
    {
        UploadMVP(mvpLoc, _proj * _view);
        _bufCount = 0;
        EmitVertex(a.x, a.y, a.z);
        EmitVertex(b.x, b.y, b.z);
        FlushLines();
    }

    /// <summary>
    /// Draws the 12 edges of an 8-corner box in world space.
    /// The 8 corners are supplied in local space; they are transformed to world
    /// space via <paramref name="world"/> before drawing.
    /// Corner layout matches Mine-imator's <c>view_shape_draw</c> convention.
    /// </summary>
    private void DrawBox8(mat4 world,
        vec3 p0, vec3 p1, vec3 p2, vec3 p3,
        vec3 p4, vec3 p5, vec3 p6, vec3 p7,
        int mvpLoc)
    {
        vec3 W(vec3 v) => new vec3((world * new vec4(v, 1f)).xyz);

        vec3 w0 = W(p0), w1 = W(p1), w2 = W(p2), w3 = W(p3);
        vec3 w4 = W(p4), w5 = W(p5), w6 = W(p6), w7 = W(p7);

        UploadMVP(mvpLoc, _proj * _view);
        _bufCount = 0;

        // 12 edges of a box (same wiring as view_shape_draw)
        AddWorldSeg(w0, w1); AddWorldSeg(w0, w2); AddWorldSeg(w0, w4);
        AddWorldSeg(w1, w3); AddWorldSeg(w1, w5);
        AddWorldSeg(w2, w3); AddWorldSeg(w2, w6);
        AddWorldSeg(w3, w7);
        AddWorldSeg(w4, w5); AddWorldSeg(w4, w6);
        AddWorldSeg(w5, w7);
        AddWorldSeg(w6, w7);

        FlushLines();
    }

    private void AddWorldSeg(vec3 a, vec3 b)
    {
        EmitVertex(a.x, a.y, a.z);
        EmitVertex(b.x, b.y, b.z);
    }

    // ── Buffer helpers ──────────────────────────────────────────────────────────

    private void EmitVertex(float x, float y, float z)
    {
        int need = _bufCount + 3;
        if (need > _buf.Length)
            Array.Resize(ref _buf, need * 2);
        _buf[_bufCount++] = x;
        _buf[_bufCount++] = y;
        _buf[_bufCount++] = z;
    }

    private unsafe void FlushLines()
    {
        if (_bufCount == 0) return;

        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);

        uint byteCount = (uint)(_bufCount * sizeof(float));
        // Orphan + reupload (streaming pattern to avoid sync stalls).
        _gl.BufferData(GLEnum.ArrayBuffer, byteCount, (ReadOnlySpan<float>)ReadOnlySpan<float>.Empty, GLEnum.StreamDraw);
        fixed (float* p = _buf)
            _gl.BufferSubData(GLEnum.ArrayBuffer, 0, byteCount, p);

        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);

        int vertexCount = _bufCount / 3;
        _gl.DrawArrays(GLEnum.Lines, 0, (uint)vertexCount);

        _bufCount = 0;
    }

    // ── Shader helpers ──────────────────────────────────────────────────────────

    private unsafe void UploadMVP(int mvpLoc, mat4 mvp)
    {
        if (mvpLoc < 0) return;
        float[] f =
        {
            mvp.m00, mvp.m01, mvp.m02, mvp.m03,
            mvp.m10, mvp.m11, mvp.m12, mvp.m13,
            mvp.m20, mvp.m21, mvp.m22, mvp.m23,
            mvp.m30, mvp.m31, mvp.m32, mvp.m33,
        };
        fixed (float* p = f) _gl.UniformMatrix4(mvpLoc, 1, false, p);
    }

    private void SetColor(int colorLoc, float r, float g, float b, float a)
    {
        if (colorLoc >= 0) _gl.Uniform4(colorLoc, r, g, b, a);
    }

    // ── Scene traversal ─────────────────────────────────────────────────────────

    private static void CollectLights(IEnumerable<SceneObject> roots, List<LightSceneObject> result)
    {
        foreach (var obj in roots)
            CollectLight(obj, result);
    }

    private static void CollectLight(SceneObject obj, List<LightSceneObject> result)
    {
        if (!obj.GetEffectiveVisibility()) return;
        if (obj is LightSceneObject light && !obj.IsHiddenForRendering()) result.Add(light);
        var children = obj.Children;
        for (int i = 0; i < children.Count; i++)
            CollectLight(children[i], result);
    }

    // ── IDisposable ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_vao != 0) { _gl.DeleteVertexArrays(1, _vao); _vao = 0; }
        if (_vbo != 0) { _gl.DeleteBuffers(1, _vbo);      _vbo = 0; }
        _shader.Dispose();
    }
}
