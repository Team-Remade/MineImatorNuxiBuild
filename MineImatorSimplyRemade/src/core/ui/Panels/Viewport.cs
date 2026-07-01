using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.gizmo;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class Viewport : UiPanel
{
    // ── Scene ──────────────────────────────────────────────────────────────────

    public List<SceneObject> SceneObjects { get; } = new();

    // ── Panel references ───────────────────────────────────────────────────────

    /// <summary>
    /// Reference to the properties panel, used to read the background color
    /// for the viewport clear color.
    /// </summary>
    public PropertiesPanel? PropertiesPanel { get; set; }

    // ── GLFW references (for cursor lock during free-fly) ──────────────────────

    /// <summary>
    /// The GLFW API instance.  Set by <see cref="MainWindow"/> after construction
    /// so the viewport can toggle <see cref="CursorModeValue.CursorDisabled"/>.
    /// </summary>
    public Glfw? GlfwApi { get; set; }

    /// <summary>
    /// The native GLFW window handle.  Set alongside <see cref="GlfwApi"/>.
    /// </summary>
    public unsafe WindowHandle* GlfwWindow { get; set; }

    // ── Ground plane ───────────────────────────────────────────────────────────

    /// <summary>
    /// The XZ-plane ground mesh that displays the tiled terrain texture.
    /// Initialised in <see cref="InitGroundPlane"/> after atlases are loaded.
    /// Exposed so the <see cref="CameraViewport"/> can render the same ground plane.
    /// </summary>
    public PlaneMesh? GroundPlane => _groundPlane;
    private PlaneMesh? _groundPlane;

    // ── Camera ─────────────────────────────────────────────────────────────────

    public Camera Camera { get; } = new Camera();

    // ── Gizmo ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The 3D transform gizmo for moving/rotating/scaling selected objects.
    /// Created in <see cref="InitFramebuffer"/> once the GL context is ready.
    /// </summary>
    public Gizmo3D? Gizmo { get; private set; }

    // ── Orbit drag state ───────────────────────────────────────────────────────

    private bool  _dragging;
    private bool  _panning;
    private bool  _gizmoDragging;
    private float _lastMouseX = float.NaN;
    private float _lastMouseY = float.NaN;

    /// <summary>
    /// Screen position where the left mouse button was pressed this drag sequence.
    /// Used to distinguish a click (no/minimal movement) from an orbit drag.
    /// </summary>
    private float _pressMouseX = float.NaN;
    private float _pressMouseY = float.NaN;

    /// <summary>
    /// Movement threshold in pixels beyond which a left-button hold is treated as
    /// an orbit drag rather than a click.
    /// </summary>
    private const float OrbitDragThreshold = 4f;

    // ── Free-fly state ─────────────────────────────────────────────────────────

    /// <summary>
    /// True while the right mouse button is held and free-fly mode is active.
    /// Mouse look and WASD/QE movement are processed when this is true.
    /// </summary>
    private bool _freeFly;

    /// <summary>Current movement speed multiplier for free-fly, adjusted by scroll wheel.</summary>
    private float _freeFlySpeed = 5f;

    /// <summary>Mouse sensitivity for free-fly look (radians per pixel).</summary>
    private const float FreeFlyLookSensitivity = 0.003f;

    // ── Click position for deferred pick ──────────────────────────────────────

    /// <summary>
    /// Screen-space mouse position captured on left-button release so that the
    /// colour-pick read-back can happen inside the render loop (GL context active).
    /// NaN means no pick is pending.
    /// </summary>
    private float _pendingPickX = float.NaN;
    private float _pendingPickY = float.NaN;

    /// <summary>Whether Ctrl was held at the time of the pending pick click.</summary>
    private bool _pendingPickCtrl;

    // ── Framebuffer ────────────────────────────────────────────────────────────

    private uint _fbo;
    private uint _colorTex;
    private uint _rbo;

    private uint _viewportWidth, _viewportHeight;

    // ── Pick framebuffer ───────────────────────────────────────────────────────

    /// <summary>Off-screen FBO used for the colour-ID pick pass.</summary>
    private uint _pickFbo;

    /// <summary>RGBA colour texture for the pick pass (same size as the main FBO).</summary>
    private uint _pickColorTex;

    /// <summary>Depth renderbuffer shared with the pick FBO.</summary>
    private uint _pickRbo;

    /// <summary>Flat-colour shader used in the pick pass (outputs uPickColor).</summary>
    private MineImatorSimplyRemade.core.mdl.Shader? _pickShader;

    // ── Sobel selection highlight ──────────────────────────────────────────────

    /// <summary>
    /// Off-screen R8 framebuffer used to stamp a flat white mask of the selected
    /// objects.  The Sobel edge shader then reads this to find silhouette edges.
    /// </summary>
    private uint _silhouetteFbo;
    private uint _silhouetteTex;

    /// <summary>Shader that writes flat white into the silhouette mask FBO.</summary>
    private MineImatorSimplyRemade.core.mdl.Shader? _silhouetteShader;

    /// <summary>
    /// Full-screen Sobel edge detection shader.  Reads <see cref="_silhouetteTex"/>
    /// and paints the detected edges into the main display FBO.
    /// </summary>
    private MineImatorSimplyRemade.core.mdl.Shader? _edgeShader;

    /// <summary>
    /// Empty VAO required by the full-screen triangle draw call used in the edge pass.
    /// OpenGL 3.3 Core Profile mandates a VAO even when no vertex data is sourced.
    /// </summary>
    private uint _edgeVao;

    /// <summary>Colour of the Sobel selection outline (RGBA).</summary>
    private static readonly vec4 EdgeColor = new vec4(1.0f, 0.65f, 0.0f, 1.0f);

    /// <summary>Minimum Sobel gradient magnitude treated as an edge (0–1).</summary>
    private const float EdgeThreshold = 0.4f;

    // ── Camera dropdown ────────────────────────────────────────────────────────

    /// <summary>
    /// Index of the active camera for the main viewport.
    /// 0 = work camera; 1+ = spawned cameras by index.
    /// </summary>
    private int _activeCameraIndex = 0;

    // ── Overlay visibility ─────────────────────────────────────────────────────

    /// <summary>
    /// When true (default) the viewport renders editor overlays: the 3-D gizmo,
    /// the selection outline, light billboards, and bone indicators.
    /// Set to false by the "Overlays" toggle button in the top bar.
    /// </summary>
    public bool OverlaysEnabled { get; set; } = true;

    // ── Secondary camera viewport ──────────────────────────────────────────────

    /// <summary>
    /// The secondary camera viewport panel (inline overlay + optional undocked window).
    /// Set by <see cref="MainWindow"/> after construction.
    /// </summary>
    public CameraViewport? CameraViewport { get; set; }

    /// <summary>
    /// When true, the inline camera preview overlay is skipped.
    /// Useful when a fullscreen launcher/home screen is shown above the editor.
    /// </summary>
    public bool SuppressInlineCameraViewport { get; set; } = false;

    // ── Spawn menu / bench button ──────────────────────────────────────────────

    /// <summary>
    /// The floating spawn-object menu.  Set by <see cref="MainWindow"/> after
    /// both objects are created so the bench button can trigger it.
    /// </summary>
    public SpawnMenu? SpawnMenu { get; set; }

    /// <summary>
    /// OpenGL texture handle for the bench icon displayed as a button in the
    /// top-left corner of the viewport.  Loaded in <see cref="InitFramebuffer"/>.
    /// </summary>
    private uint _benchTexture;

    // ── Bone indicator ─────────────────────────────────────────────────────────

    /// <summary>Flat-colour shader used to draw the bone octahedron indicators.</summary>
    private MineImatorSimplyRemade.core.mdl.Shader? _boneShader;

    /// <summary>RGBA colour for unselected bone indicators.</summary>
    private static readonly vec4 BoneColor         = new(0.80f, 0.65f, 0.10f, 1f); // amber
    /// <summary>RGBA colour for selected bone indicators.</summary>
    private static readonly vec4 BoneColorSelected = new(1.00f, 0.90f, 0.20f, 1f); // bright yellow

    // ── Light billboard ────────────────────────────────────────────────────────

    /// <summary>Billboard shader used to draw camera-facing light icons.</summary>
    private MineImatorSimplyRemade.core.mdl.Shader? _billboardShader;

    /// <summary>GL texture handle for <c>light.png</c>.</summary>
    private uint _lightIconTexture;

    /// <summary>GL texture handle for <c>lightRay.png</c>.</summary>
    private uint _lightRayTexture;

    /// <summary>World-space size of the light icon billboard (metres).</summary>
    private const float LightBillboardSize = 0.5f;

    /// <summary>World-space size of the light-ray billboard (slightly larger, drawn behind).</summary>
    private const float LightRayBillboardSize = 0.8f;

    // ── Ground plane setup ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates the 64×64 XZ ground plane and assigns the <c>grass_block_top</c>
    /// texture as its surface.  Must be called after both the GL context and
    /// <see cref="TerrainAtlas"/> are initialised.
    /// </summary>
    public void InitGroundPlane()
    {
        if (Gl == null) return;

        _groundPlane = new PlaneMesh(Gl, 64f, 64f, PlaneOrientation.XZ);

        // Use the named grass_block_top texture from textures/block/.
        if (TerrainAtlas.Textures.TryGetValue("grass_block_top", out uint tileId))
            _groundPlane.TextureId = tileId;
    }

    // ── FBO setup ──────────────────────────────────────────────────────────────

    public unsafe void InitFramebuffer(uint width, uint height)
    {
        _viewportWidth  = width;
        _viewportHeight = height;

        // ── Display FBO ───────────────────────────────────────────────────────
        Gl.GenFramebuffers(1, out _fbo);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);

        // Colour attachment
        Gl.GenTextures(1, out _colorTex);
        Gl.BindTexture(GLEnum.Texture2D, _colorTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, width, height, 0,
                      PixelFormat.Rgb, GLEnum.UnsignedByte, (void*)0);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0,
                                GLEnum.Texture2D, _colorTex, 0);

        // Depth+stencil renderbuffer
        Gl.GenRenderbuffers(1, out _rbo);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, _rbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, width, height);
        Gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthStencilAttachment,
                                   GLEnum.Renderbuffer, _rbo);

        var status = Gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.Error.WriteLine($"[Viewport] Framebuffer incomplete: {status}");

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);

        // ── Pick FBO ──────────────────────────────────────────────────────────
        InitPickFramebuffer(width, height);

        // ── Pick shader ───────────────────────────────────────────────────────
        _pickShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
        _pickShader.CompileShader("pick.vert", "pick.frag");

        // ── Silhouette mask FBO ───────────────────────────────────────────────
        InitSilhouetteFbo(width, height);

        // ── Silhouette shader (flat white into the mask FBO) ──────────────────
        _silhouetteShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
        _silhouetteShader.CompileShader("pick.vert", "silhouette.frag");

        // ── Sobel edge shader (full-screen quad, reads mask, writes edges) ────
        _edgeShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
        _edgeShader.CompileShader("edge.vert", "edge.frag");

        // ── Empty VAO for the full-screen triangle draw (Core Profile req.) ───
        Gl.GenVertexArrays(1, out _edgeVao);

        // ── Bench button texture ──────────────────────────────────────────────
        _benchTexture = LoadEmbeddedTexture("bench");

        // ── Gizmo ─────────────────────────────────────────────────────────────
        // Initialise the gizmo now that the GL context is fully ready.
        Gizmo = new Gizmo3D(Gl);
        Gizmo.Init();

        // Register the gizmo with the SelectionManager so selection changes
        // automatically sync to the gizmo handle set.
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.Gizmo = Gizmo;

        // ── Light billboard resources ──────────────────────────────────────────
        InitLightBillboards();
    }

    private unsafe void InitPickFramebuffer(uint width, uint height)
    {
        Gl.GenFramebuffers(1, out _pickFbo);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _pickFbo);

        // RGBA colour texture — we read back individual pixels via GetTexImage.
        Gl.GenTextures(1, out _pickColorTex);
        Gl.BindTexture(GLEnum.Texture2D, _pickColorTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, width, height, 0,
                      PixelFormat.Rgba, GLEnum.UnsignedByte, (void*)0);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0,
                                GLEnum.Texture2D, _pickColorTex, 0);

        // Depth renderbuffer (no stencil needed for the pick pass).
        Gl.GenRenderbuffers(1, out _pickRbo);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, _pickRbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.DepthComponent24, width, height);
        Gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthAttachment,
                                   GLEnum.Renderbuffer, _pickRbo);

        var status = Gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.Error.WriteLine($"[Viewport] Pick framebuffer incomplete: {status}");

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
    }

    private unsafe void InitSilhouetteFbo(uint width, uint height)
    {
        Gl.GenFramebuffers(1, out _silhouetteFbo);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _silhouetteFbo);

        // Single-channel R8 texture — stores the flat white silhouette mask.
        Gl.GenTextures(1, out _silhouetteTex);
        Gl.BindTexture(GLEnum.Texture2D, _silhouetteTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.R8, width, height, 0,
                      PixelFormat.Red, GLEnum.UnsignedByte, (void*)0);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
        // Clamp to border (0) so Sobel samples outside the texture edge return 0.
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0,
                                GLEnum.Texture2D, _silhouetteTex, 0);

        // No depth needed — we use GL_ALWAYS depth test in the silhouette pass.
        var status = Gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.Error.WriteLine($"[Viewport] Silhouette framebuffer incomplete: {status}");

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
    }

    // ── Light billboard init ───────────────────────────────────────────────────

    /// <summary>
    /// Loads light billboard textures and creates the shared screen-aligned quad
    /// VAO used by <see cref="RenderLightBillboards"/>.
    /// </summary>
    private unsafe void InitLightBillboards()
    {
        if (Gl == null) return;

        // Load textures.
        _lightIconTexture = LoadEmbeddedTexture("light",    nearest: true);
        _lightRayTexture  = LoadEmbeddedTexture("lightRay", nearest: true);

        // Assign to shared static handles on LightSceneObject.
        LightSceneObject.BillboardVao = CreateBillboardVao();

        // Compile billboard shader.
        _billboardShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
        _billboardShader.CompileShader("billboard.vert", "billboard.frag");

        // Bone indicator shader (flat MVP + colour, same as gizmo).
        _boneShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
        _boneShader.CompileShader("gizmo.vert", "gizmo.frag");
    }

    /// <summary>
    /// Builds and returns a VAO for a unit XY quad (positions at ±0.5, UVs 0-1).
    /// The billboard vertex shader uses <c>aPos</c> (loc 0) and <c>aTexCoord</c> (loc 2).
    /// </summary>
    private unsafe uint CreateBillboardVao()
    {
        if (Gl == null) return 0;

        // Interleaved [ px py pz nx ny nz u v ] — same layout as Mesh so the
        // same attribute pointers apply.  Normals are unused but keep the stride identical.
        float[] verts =
        {
            //  px     py     pz    nx    ny    nz    u     v
             0.5f,  0.5f,  0f,   0f,  0f, -1f,  1f,  1f,  // top-right
             0.5f, -0.5f,  0f,   0f,  0f, -1f,  1f,  0f,  // bottom-right
            -0.5f, -0.5f,  0f,   0f,  0f, -1f,  0f,  0f,  // bottom-left
            -0.5f,  0.5f,  0f,   0f,  0f, -1f,  0f,  1f,  // top-left
        };
        uint[] indices = { 0, 1, 2, 0, 2, 3 };

        uint vao, vbo, ebo;
        Gl.GenVertexArrays(1, out vao);
        Gl.GenBuffers(1, out vbo);
        Gl.GenBuffers(1, out ebo);

        Gl.BindVertexArray(vao);

        Gl.BindBuffer(GLEnum.ArrayBuffer, vbo);
        fixed (float* p = verts)
            Gl.BufferData(GLEnum.ArrayBuffer, (uint)(verts.Length * sizeof(float)), p, GLEnum.StaticDraw);

        Gl.BindBuffer(GLEnum.ElementArrayBuffer, ebo);
        fixed (uint* p = indices)
            Gl.BufferData(GLEnum.ElementArrayBuffer, (uint)(indices.Length * sizeof(uint)), p, GLEnum.StaticDraw);

        uint stride = 8 * sizeof(float);
        // loc 0: position (xyz)
        Gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, 0);
        Gl.EnableVertexAttribArray(0);
        // loc 1: normal (xyz) — not used by billboard shader but keeps layout consistent
        Gl.VertexAttribPointer(1, 3, GLEnum.Float, false, stride, 3 * sizeof(float));
        Gl.EnableVertexAttribArray(1);
        // loc 2: texcoord (uv)
        Gl.VertexAttribPointer(2, 2, GLEnum.Float, false, stride, 6 * sizeof(float));
        Gl.EnableVertexAttribArray(2);

        Gl.BindVertexArray(0);
        Gl.BindBuffer(GLEnum.ArrayBuffer, 0);

        LightSceneObject.BillboardVbo = vbo;
        LightSceneObject.BillboardEbo = ebo;

        return vao;
    }

    // ── Light billboard render ─────────────────────────────────────────────────

    /// <summary>
    /// Draws a camera-facing icon + ray billboard for every visible
    /// <see cref="LightSceneObject"/> in the scene.
    /// Must be called while the main FBO is bound and blending is disabled
    /// (it enables/disables blending internally).
    /// </summary>
    private unsafe void RenderLightBillboards(mat4 view, mat4 proj)
    {
        if (_billboardShader == null) return;
        if (LightSceneObject.BillboardVao == 0) return;

        uint prog = _billboardShader.ShaderProgram;
        Gl.UseProgram(prog);

        // Blend: normal alpha (premultiplied would require different blend, but PNG
        // icons are straight-alpha so use standard src-alpha / one-minus-src-alpha).
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        Gl.Disable(GLEnum.CullFace);
        Gl.DepthMask(false); // don't write depth — billboards sit on top

        // Upload view + proj (shared for all billboards this frame).
        int viewLoc = Gl.GetUniformLocation(prog, "uView");
        int projLoc = Gl.GetUniformLocation(prog, "uProj");
        int posLoc  = Gl.GetUniformLocation(prog, "uWorldPos");
        int sizeLoc = Gl.GetUniformLocation(prog, "uSize");
        int texLoc  = Gl.GetUniformLocation(prog, "uTexture");
        int tintLoc = Gl.GetUniformLocation(prog, "uTint");

        if (viewLoc >= 0)
        {
            float[] vf =
            {
                view.m00, view.m01, view.m02, view.m03,
                view.m10, view.m11, view.m12, view.m13,
                view.m20, view.m21, view.m22, view.m23,
                view.m30, view.m31, view.m32, view.m33,
            };
            fixed (float* p = vf) Gl.UniformMatrix4(viewLoc, 1, false, p);
        }
        if (projLoc >= 0)
        {
            float[] pf =
            {
                proj.m00, proj.m01, proj.m02, proj.m03,
                proj.m10, proj.m11, proj.m12, proj.m13,
                proj.m20, proj.m21, proj.m22, proj.m23,
                proj.m30, proj.m31, proj.m32, proj.m33,
            };
            fixed (float* p = pf) Gl.UniformMatrix4(projLoc, 1, false, p);
        }

        Gl.BindVertexArray(LightSceneObject.BillboardVao);
        Gl.ActiveTexture(GLEnum.Texture0);
        if (texLoc >= 0) Gl.Uniform1(texLoc, 0);

        foreach (var obj in SceneObjects)
            RenderLightBillboardsRecursive(obj, posLoc, sizeLoc, tintLoc);

        Gl.BindVertexArray(0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.DepthMask(true);
        Gl.Disable(GLEnum.Blend);
        Gl.Enable(GLEnum.CullFace);
    }

    private unsafe void RenderLightBillboardsRecursive(
        SceneObject obj, int posLoc, int sizeLoc, int tintLoc)
    {
        if (!obj.GetEffectiveVisibility()) return;

        if (obj is LightSceneObject light)
        {
            vec3 worldPos = new vec3(
                obj.GetWorldMatrix().m30,
                obj.GetWorldMatrix().m31,
                obj.GetWorldMatrix().m32);

            if (posLoc >= 0) Gl.Uniform3(posLoc, worldPos.x, worldPos.y, worldPos.z);

            // 1) Ray billboard (behind the icon, tinted to light colour)
            if (_lightRayTexture != 0)
            {
                if (sizeLoc >= 0) Gl.Uniform1(sizeLoc, LightRayBillboardSize);
                if (tintLoc >= 0)
                    Gl.Uniform4(tintLoc,
                        light.LightColor.x,
                        light.LightColor.y,
                        light.LightColor.z,
                        light.LightColor.w);

                Gl.BindTexture(GLEnum.Texture2D, _lightRayTexture);
                Gl.DrawElements(GLEnum.Triangles, 6, GLEnum.UnsignedInt, (void*)0);
            }

            // 2) Icon billboard (white tint — preserve the PNG's own colours)
            if (_lightIconTexture != 0)
            {
                if (sizeLoc >= 0) Gl.Uniform1(sizeLoc, LightBillboardSize);
                if (tintLoc >= 0) Gl.Uniform4(tintLoc, 1f, 1f, 1f, 1f);

                Gl.BindTexture(GLEnum.Texture2D, _lightIconTexture);
                Gl.DrawElements(GLEnum.Triangles, 6, GLEnum.UnsignedInt, (void*)0);
            }
        }

        foreach (var child in obj.Children)
            RenderLightBillboardsRecursive(child, posLoc, sizeLoc, tintLoc);
    }

    // ── Bone indicator render ─────────────────────────────────────────────────

    /// <summary>
    /// Draws a small flat-coloured octahedron at the origin of every visible
    /// <see cref="BoneSceneObject"/> in the scene, using the gizmo shader.
    /// Selected bones are drawn in a brighter colour.
    /// Rendered with depth-test on so occluded bones are hidden by geometry,
    /// but depth-write off so they never occlude other objects.
    /// </summary>
    private unsafe void RenderBoneIndicators(mat4 view, mat4 proj)
    {
        if (_boneShader == null) return;

        var sm = SelectionManager.Instance;

        uint prog = _boneShader.ShaderProgram;
        Gl.UseProgram(prog);

        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Always); // always draw on top of geometry
        Gl.DepthMask(false);         // don't pollute the depth buffer
        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.Blend);

        int mvpLoc   = Gl.GetUniformLocation(prog, "uMVP");
        int colorLoc = Gl.GetUniformLocation(prog, "uColor");

        foreach (var root in SceneObjects)
            RenderBoneIndicatorsRecursive(root, view, proj, mvpLoc, colorLoc, sm);

        Gl.DepthMask(true);
        Gl.DepthFunc(GLEnum.Less); // restore normal depth testing
        Gl.Enable(GLEnum.CullFace);
    }

    private unsafe void RenderBoneIndicatorsRecursive(
        SceneObject obj, mat4 view, mat4 proj,
        int mvpLoc, int colorLoc, SelectionManager? sm)
    {
        if (!obj.GetEffectiveVisibility()) return;

        if (obj is BoneSceneObject bone && bone.IndicatorMesh != null)
        {
            mat4 model = obj.GetWorldMatrix();
            mat4 mvp   = proj * view * model;

            if (mvpLoc >= 0)
            {
                float[] f =
                {
                    mvp.m00, mvp.m01, mvp.m02, mvp.m03,
                    mvp.m10, mvp.m11, mvp.m12, mvp.m13,
                    mvp.m20, mvp.m21, mvp.m22, mvp.m23,
                    mvp.m30, mvp.m31, mvp.m32, mvp.m33,
                };
                fixed (float* p = f) Gl.UniformMatrix4(mvpLoc, 1, false, p);
            }

            bool selected = sm != null && sm.SelectedObjects.Contains(obj);
            vec4 col = selected ? BoneColorSelected : BoneColor;
            if (colorLoc >= 0) Gl.Uniform4(colorLoc, col.x, col.y, col.z, col.w);

            bone.IndicatorMesh.RenderPickPass(Gl);
        }

        foreach (var child in obj.Children)
            RenderBoneIndicatorsRecursive(child, view, proj, mvpLoc, colorLoc, sm);
    }

    /// <summary>
    /// Renders light billboards and bone indicators into the currently bound FBO
    /// using the supplied camera matrices.  Called by <see cref="CameraViewport"/>
    /// when its <c>OverlaysEnabled</c> flag is set, so the two viewports share the
    /// same overlay shaders and geometry without duplicating code.
    ///
    /// The caller is responsible for binding the target FBO and setting the
    /// viewport dimensions before calling this method.  GL state is restored to
    /// the expected scene defaults (depth-test on/Less, cull-face on/Back) on return.
    /// </summary>
    public void RenderOverlaysPublic(mat4 view, mat4 proj)
    {
        RenderLightBillboards(view, proj);

        // Restore state that billboards may have altered.
        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);

        RenderBoneIndicators(view, proj);
    }

    /// <summary>
    /// Loads an image from the embedded assembly resources and uploads it as a
    /// 2-D OpenGL texture, returning the texture handle.
    /// The resource name is the base name without path or extension
    /// (e.g. <c>"bench"</c> maps to <c>MineImatorSimplyRemade.assets.img.bench.png</c>).
    /// Pass <paramref name="nearest"/>=true for pixel-art textures that should use
    /// nearest-neighbour filtering instead of linear.
    /// Returns 0 if the resource is not found or if <see cref="Gl"/> is null.
    /// </summary>
    private unsafe uint LoadEmbeddedTexture(string resourceName, bool nearest = false)
    {
        if (Gl == null) return 0;

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(
            $"MineImatorSimplyRemade.assets.img.{resourceName}.png");
        if (stream == null)
        {
            Console.Error.WriteLine($"[Viewport] Embedded texture not found: {resourceName}");
            return 0;
        }

        var img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        // OpenGL expects pixel row 0 at the bottom; StbImageSharp delivers row 0 at
        // the top.  Flip the rows in-place so the texture is the right way up.
        int rowBytes = img.Width * 4; // RGBA = 4 bytes per pixel
        byte[] flipped = new byte[img.Data.Length];
        for (int row = 0; row < img.Height; row++)
        {
            int srcRow = img.Height - 1 - row;
            System.Buffer.BlockCopy(img.Data, srcRow * rowBytes, flipped, row * rowBytes, rowBytes);
        }

        uint tex = Gl.GenTexture();
        Gl.BindTexture(GLEnum.Texture2D, tex);

        fixed (byte* p = flipped)
        {
            Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8,
                (uint)img.Width, (uint)img.Height, 0,
                PixelFormat.Rgba, GLEnum.UnsignedByte, p);
        }

        var filter = nearest ? TextureMinFilter.Nearest : TextureMinFilter.Linear;
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)filter);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, nearest ? (int)TextureMagFilter.Nearest : (int)TextureMagFilter.Linear);
        Gl.BindTexture(GLEnum.Texture2D, 0);

        return tex;
    }

    private unsafe void ResizeFramebuffer(uint width, uint height)
    {
        _viewportWidth  = width;
        _viewportHeight = height;

        // Resize display FBO attachments.
        Gl.BindTexture(GLEnum.Texture2D, _colorTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, width, height, 0,
                      PixelFormat.Rgb, GLEnum.UnsignedByte, (void*)0);

        Gl.BindRenderbuffer(GLEnum.Renderbuffer, _rbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, width, height);

        // Resize pick FBO attachments.
        Gl.BindTexture(GLEnum.Texture2D, _pickColorTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, width, height, 0,
                      PixelFormat.Rgba, GLEnum.UnsignedByte, (void*)0);

        Gl.BindRenderbuffer(GLEnum.Renderbuffer, _pickRbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.DepthComponent24, width, height);

        // Resize silhouette mask FBO attachment.
        Gl.BindTexture(GLEnum.Texture2D, _silhouetteTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.R8, width, height, 0,
                      PixelFormat.Red, GLEnum.UnsignedByte, (void*)0);

        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);
    }

    public unsafe bool SaveThumbnail(string filePath, int outputWidth = 320, int outputHeight = 180)
    {
        if (Gl == null || _fbo == 0 || _viewportWidth == 0 || _viewportHeight == 0)
            return false;

        int srcWidth = (int)_viewportWidth;
        int srcHeight = (int)_viewportHeight;
        int srcStride = srcWidth * 3;
        byte[] source = new byte[srcStride * srcHeight];

        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        Gl.PixelStore(GLEnum.PackAlignment, 1);
        fixed (byte* p = source)
            Gl.ReadPixels(0, 0, (uint)srcWidth, (uint)srcHeight, GLEnum.Rgb, GLEnum.UnsignedByte, p);
        Gl.PixelStore(GLEnum.PackAlignment, 4);
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);

        byte[] resized = ResizeRgbNearest(source, srcWidth, srcHeight, outputWidth, outputHeight);
        WritePng24(filePath, resized, outputWidth, outputHeight);
        return true;
    }

    private static byte[] ResizeRgbNearest(byte[] source, int srcWidth, int srcHeight, int dstWidth, int dstHeight)
    {
        byte[] dest = new byte[dstWidth * dstHeight * 3];

        for (int y = 0; y < dstHeight; y++)
        {
            int srcY = y * srcHeight / dstHeight;
            for (int x = 0; x < dstWidth; x++)
            {
                int srcX = x * srcWidth / dstWidth;
                int srcIndex = (srcY * srcWidth + srcX) * 3;
                int dstIndex = (y * dstWidth + x) * 3;

                dest[dstIndex + 0] = source[srcIndex + 0];
                dest[dstIndex + 1] = source[srcIndex + 1];
                dest[dstIndex + 2] = source[srcIndex + 2];
            }
        }

        return dest;
    }

    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    private static void WritePng24(string filePath, byte[] rgbPixels, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");

        int stride = width * 3;
        byte[] scanlines = new byte[height * (stride + 1)];
        for (int y = 0; y < height; y++)
        {
            int destRow = y * (stride + 1);
            int srcY = height - 1 - y;
            int srcRow = srcY * stride;
            System.Buffer.BlockCopy(rgbPixels, srcRow, scanlines, destRow + 1, stride);
        }

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 2;

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(scanlines, 0, scanlines.Length);

            compressed = compressedStream.ToArray();
        }

        using var stream = File.Create(filePath);
        stream.Write(PngSignature, 0, PngSignature.Length);
        WritePngChunk(stream, "IHDR", ihdr);
        WritePngChunk(stream, "IDAT", compressed);
        WritePngChunk(stream, "IEND", []);
    }

    private static void WritePngChunk(Stream stream, string chunkType, byte[] data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)data.Length);
        header[4] = (byte)chunkType[0];
        header[5] = (byte)chunkType[1];
        header[6] = (byte)chunkType[2];
        header[7] = (byte)chunkType[3];

        stream.Write(header);
        if (data.Length > 0)
            stream.Write(data, 0, data.Length);

        uint crc = ComputePngCrc32(chunkType, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint ComputePngCrc32(string chunkType, byte[] data)
    {
        uint crc = 0xFFFFFFFFu;

        for (int i = 0; i < chunkType.Length; i++)
            crc = UpdatePngCrc32(crc, (byte)chunkType[i]);

        for (int i = 0; i < data.Length; i++)
            crc = UpdatePngCrc32(crc, data[i]);

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint UpdatePngCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
        {
            if ((crc & 1u) != 0)
                crc = (crc >> 1) ^ 0xEDB88320u;
            else
                crc >>= 1;
        }

        return crc;
    }

    // ── Render ─────────────────────────────────────────────────────────────────

    // Height of the top bar (camera dropdown + bench button row) in pixels.
    private const float TopBarHeight = 28f;

    public override unsafe void Render()
    {
        // Outer window uses the default window padding so the tab bar, borders,
        // and title bar all behave normally when docked.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);

        ImGui.Begin("Viewport");
        ImGui.PopStyleVar(); // pop WindowBorderSize — done before widgets

        var totalSize = ImGui.GetContentRegionAvail();

        // Skip rendering until ImGui has finished its first layout pass and the
        // panel has a real size.
        if (totalSize.X < 16 || totalSize.Y < 16)
        {
            ImGui.End();
            return;
        }

        // ── Top bar ───────────────────────────────────────────────────────────
        // Left-aligned: bench button | camera dropdown.
        // Background drawn via the draw list; widgets laid out with SameLine.
        {
            const float barPadX = 4f;
            const float barPadY = 3f;
            float iconSize = TopBarHeight - barPadY * 2f;

            // Solid background strip.
            var barMin = ImGui.GetCursorScreenPos();
            var barMax = new Vector2(barMin.X + totalSize.X, barMin.Y + TopBarHeight);
            ImGui.GetWindowDrawList().AddRectFilled(barMin, barMax,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.13f, 0.13f, 0.13f, 1.0f)));

            // Vertically centre items within the bar height.
            float itemY = ImGui.GetCursorPosY() + barPadY;

            // Bench button.
            if (_benchTexture != 0)
            {
                ImGui.SetCursorPos(new Vector2(ImGui.GetCursorPosX() + barPadX, itemY));
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1, 1, 1, 0.10f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(1, 1, 1, 0.20f));
                bool benchClicked = ImGui.ImageButton(
                    "##benchBtn",
                    new ImTextureRef(texId: (ulong)_benchTexture),
                    new Vector2(iconSize, iconSize),
                    new Vector2(0, 1),
                    new Vector2(1, 0));
                ImGui.PopStyleColor(3);
                if (benchClicked && SpawnMenu != null)
                {
                    var btnMax = ImGui.GetItemRectMax();
                    var btnMin = ImGui.GetItemRectMin();
                    SpawnMenu.Toggle(new Vector2(btnMin.X, btnMax.Y + 4f));
                }
                ImGui.SameLine();
            }

            // Camera dropdown — immediately to the right of the bench button.
            {
                var spawnedCams = GetSpawnedCameras();
                string currentCamLabel = _activeCameraIndex == 0
                    ? "Work Camera"
                    : (_activeCameraIndex - 1 < spawnedCams.Count
                        ? spawnedCams[_activeCameraIndex - 1].Name
                        : "Work Camera");

                ImGui.SetCursorPosY(itemY);
                ImGui.SetNextItemWidth(160f);
                if (ImGui.BeginCombo("##vpCamSelect", currentCamLabel))
                {
                    bool workSel = _activeCameraIndex == 0;
                    if (ImGui.Selectable("Work Camera", workSel)) _activeCameraIndex = 0;
                    if (workSel) ImGui.SetItemDefaultFocus();
                    for (int i = 0; i < spawnedCams.Count; i++)
                    {
                        bool sel = _activeCameraIndex == i + 1;
                        if (ImGui.Selectable(spawnedCams[i].Name + "##vpcam" + i, sel))
                            _activeCameraIndex = i + 1;
                        if (sel) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                if (_activeCameraIndex > spawnedCams.Count)
                    _activeCameraIndex = 0;
            }

            // Overlays toggle button — right-aligned at the end of the bar.
            {
                ImGui.SameLine();
                ImGui.SetCursorPosY(itemY);

                // Tint the button to indicate the active state.
                if (!OverlaysEnabled)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.20f, 0.20f, 0.20f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.30f, 0.30f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.40f, 0.40f, 0.40f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.Text,          new Vector4(0.50f, 0.50f, 0.50f, 1.0f));
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.25f, 0.45f, 0.25f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.55f, 0.30f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.35f, 0.60f, 0.35f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.Text,          new Vector4(0.90f, 0.95f, 0.90f, 1.0f));
                }

                if (ImGui.Button(OverlaysEnabled ? "Overlays" : "Overlays",
                        new Vector2(0, iconSize)))
                    OverlaysEnabled = !OverlaysEnabled;

                ImGui.PopStyleColor(4);
            }

            // Advance the layout cursor to the bottom edge of the bar.
            var winPos  = ImGui.GetWindowPos();
            float barBottomLocal = (barMin.Y + TopBarHeight) - winPos.Y - ImGui.GetScrollY();
            ImGui.SetCursorPosY(barBottomLocal);
        }

        // ── 3D image child window ─────────────────────────────────────────────
        // A zero-padding child window fills the remaining area below the bar.
        // Using a child means the image's clip rect is independent of the outer
        // window's padding/tab-bar, so nothing clips the top of the 3D view.
        var imageAreaSize = ImGui.GetContentRegionAvail();
        if (imageAreaSize.X < 1) imageAreaSize.X = 1;
        if (imageAreaSize.Y < 1) imageAreaSize.Y = 1;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("##vpImage", imageAreaSize, ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();

        var size = ImGui.GetContentRegionAvail();
        if (size.X < 1) size.X = 1;
        if (size.Y < 1) size.Y = 1;

        uint w = (uint)size.X;
        uint h = (uint)size.Y;

        // ── Resize FBO when panel changes size ────────────────────────────────
        if (w != _viewportWidth || h != _viewportHeight)
            ResizeFramebuffer(w, h);

        // Capture image rect before the Image call so HandleCameraInput and the
        // pick pass both have the correct sub-window coordinates.
        var imageMin  = ImGui.GetCursorScreenPos();
        var imageSize = size;

        // ── Camera orbit/pan input ─────────────────────────────────────────────
        HandleCameraInput();

        // ── 3D render into FBO ────────────────────────────────────────────────
        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        Gl.Viewport(0, 0, w, h);

        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);

        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.FrontFace(GLEnum.Ccw);

        float[] bg = PropertiesPanel?.BackgroundColor ?? [0.18f, 0.18f, 0.18f, 1.0f];
        Gl.ClearColor(bg[0], bg[1], bg[2], bg[3]);
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float aspect = (h > 0) ? (float)w / h : 1f;

        // Use the active camera (work camera or a spawned camera).
        var (activeCamera, activeCamObj) = GetActiveRenderCamera();

        // Apply spawned-camera projection settings if a scene camera is active.
        float savedFovY = activeCamera.FovY;
        float savedNear = activeCamera.Near;
        float savedFar  = activeCamera.Far;
        if (activeCamObj != null)
        {
            activeCamera.FovY = GlmSharp.glm.Radians(activeCamObj.Fov);
            activeCamera.Near = activeCamObj.Near;
            activeCamera.Far  = activeCamObj.Far;
        }

        mat4 view = activeCamera.GetViewMatrix();
        mat4 proj = activeCamera.GetProjectionMatrix(aspect);

        // Restore camera settings after extracting matrices.
        activeCamera.FovY = savedFovY;
        activeCamera.Near = savedNear;
        activeCamera.Far  = savedFar;

        // ── Ground plane ──────────────────────────────────────────────────────
        if (_groundPlane != null)
            _groundPlane.Render(mat4.Identity, view, proj);

        // ── Per-frame mesh globals ─────────────────────────────────────────────
        Mesh.DeltaTime = ImGui.GetIO().DeltaTime;

        // Rebuild the static light list used by Mesh.Render() every frame so
        // that moved / deleted lights are always up-to-date.
        Mesh.PointLights.Clear();
        CollectPointLights(SceneObjects);

        // ── Scene objects ─────────────────────────────────────────────────────
        // Split meshes into three buckets:
        //   opaque       – Alpha == 1, no texture  → normal depth read+write
        //   textured     – has a TextureId          → depth pre-pass + color pass (LEQUAL)
        //   alphaBlend   – Alpha < 1, no texture    → straight back-to-front blend, no pre-pass
        var opaquePairs    = new List<(mat4 model, Mesh mesh)>();
        var texturedPairs  = new List<(mat4 model, Mesh mesh, float dist)>();
        var alphaBlendPairs = new List<(mat4 model, Mesh mesh, float dist)>();
        var overlayPairs   = new List<(mat4 model, Mesh mesh)>();

        vec3 camPos = activeCamera.Position;

        CollectRenderPairs(SceneObjects, camPos, opaquePairs, texturedPairs, alphaBlendPairs, overlayPairs);

        // Pass 1 – Opaque geometry (depth read + write, no blending).
        foreach (var (model, mesh) in opaquePairs)
            mesh.Render(model, view, proj);

        // Pass 2 – Textured meshes (may have per-pixel alpha from the texture).
        //   2a. Depth pre-pass: populate depth buffer with color writes masked off so
        //       each mesh self-occludes (front faces block back faces of the same object).
        //   2b. Color pass: LEQUAL depth so the pre-pass depth values pass, depth writes
        //       off for correct back-to-front blending between separate objects.
        if (texturedPairs.Count > 0)
        {
            texturedPairs.Sort((a, b) => b.dist.CompareTo(a.dist));

            Gl.ColorMask(false, false, false, false);
            foreach (var (model, mesh, _) in texturedPairs)
                mesh.Render(model, view, proj);
            Gl.ColorMask(true, true, true, true);

            Gl.DepthFunc(GLEnum.Lequal);
            Gl.Enable(GLEnum.Blend);
            Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
            Gl.DepthMask(false);

            foreach (var (model, mesh, _) in texturedPairs)
                mesh.Render(model, view, proj);

            Gl.DepthMask(true);
            Gl.DepthFunc(GLEnum.Less);
            Gl.Disable(GLEnum.Blend);
        }

        // Pass 3 – Plain alpha-blended meshes (no texture, no pre-pass).
        //   Sorted back-to-front; depth test reads but does not write so they
        //   composite correctly behind/in-front of each other and textured meshes.
        if (alphaBlendPairs.Count > 0)
        {
            alphaBlendPairs.Sort((a, b) => b.dist.CompareTo(a.dist));

            Gl.Enable(GLEnum.Blend);
            Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
            Gl.DepthMask(false);

            foreach (var (model, mesh, _) in alphaBlendPairs)
                mesh.Render(model, view, proj);

            Gl.DepthMask(true);
            Gl.Disable(GLEnum.Blend);
        }

        if (OverlaysEnabled)
        {
            // ── Object-mesh overlays (e.g. camera icon) ───────────────────────────
            // Rendered with depth-test off so they always appear on top.
            // Mesh.Render() handles the DepthTestDisabled / Unlit flags internally.
            foreach (var (model, mesh) in overlayPairs)
                mesh.Render(model, view, proj);

            // ── Light billboards ──────────────────────────────────────────────────
            // Drawn after transparent geometry, before the selection outline so
            // selected lights still receive the orange Sobel outline.
            RenderLightBillboards(view, proj);

            // Restore state that billboards may have altered.
            Gl.Enable(GLEnum.DepthTest);
            Gl.DepthFunc(GLEnum.Less);
            Gl.Enable(GLEnum.CullFace);
            Gl.CullFace(GLEnum.Back);

            // ── Bone indicators ───────────────────────────────────────────────────
            RenderBoneIndicators(view, proj);

            // ── Selection highlight: Sobel edge detection ─────────────────────────
            // Pass 1: stamp flat-white silhouette mask into _silhouetteFbo.
            //         No depth test → X-ray: occluded parts still contribute.
            RenderSilhouettePass(view, proj);
            // Pass 2: Sobel filter over the mask; composite edges onto _fbo.
            //         Restores depth/cull/FBO state before returning.
            RenderEdgePass();

            // ── Gizmo 3D ──────────────────────────────────────────────────────────
            Gizmo?.Render(Camera, view, proj);
        }

        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.DepthTest);
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);

        // ── Colour-pick pass (only when a click was queued this frame) ─────────
        if (!float.IsNaN(_pendingPickX))
            ExecutePendingPick(imageMin, imageSize);

        // ── Display FBO as ImGui image (flip V for OpenGL→ImGui convention) ────
        ImGui.Image(
            new ImTextureRef(texId: (ulong)_colorTex),
            size,
            new Vector2(0, 1),
            new Vector2(1, 0));

        // ── Gizmo overlay (rotation arc drawn on the ImGui draw list) ─────────
        if (OverlaysEnabled)
            Gizmo?.RenderOverlay(Camera, imageMin, size);

        // ── Inline camera viewport (bottom-right overlay) ──────────────────────
        if (!SuppressInlineCameraViewport && CameraViewport != null && !CameraViewport.Undocked)
        {
            var spawnedCams = GetSpawnedCameras();
            CameraViewport.RenderInline(imageMin, imageSize, spawnedCams);
        }

        ImGui.EndChild();
        ImGui.End();
    }

    // ── Colour-pick pass ──────────────────────────────────────────────────────

    /// <summary>
    /// Renders every selectable scene object as a flat solid colour (its
    /// <see cref="SceneObject.PickColor"/>) into the off-screen pick FBO,
    /// reads back the pixel under the cursor, and resolves the clicked object.
    ///
    /// Called from <see cref="Render"/> when a pending pick has been queued
    /// by <see cref="HandleCameraInput"/>.
    ///
    /// <paramref name="imageMin"/> and <paramref name="imageSize"/> are the
    /// screen-space rectangle of the rendered image so we can map screen
    /// coordinates to FBO pixel coordinates correctly.
    /// </summary>
    private unsafe void ExecutePendingPick(Vector2 imageMin, Vector2 imageSize)
    {
        float screenX = _pendingPickX;
        float screenY = _pendingPickY;
        bool  ctrlHeld = _pendingPickCtrl;

        // Clear the pending pick before doing any work (so errors don't loop).
        _pendingPickX = float.NaN;
        _pendingPickY = float.NaN;

        if (_pickShader == null) return;

        uint w = _viewportWidth;
        uint h = _viewportHeight;

        float aspect = (h > 0) ? (float)w / h : 1f;
        // Pick pass uses the same camera as the render pass for consistency.
        var (pickCamera, pickCamObj) = GetActiveRenderCamera();
        float pickSavedFovY = pickCamera.FovY, pickSavedNear = pickCamera.Near, pickSavedFar = pickCamera.Far;
        if (pickCamObj != null)
        {
            pickCamera.FovY = GlmSharp.glm.Radians(pickCamObj.Fov);
            pickCamera.Near = pickCamObj.Near;
            pickCamera.Far  = pickCamObj.Far;
        }
        mat4 view = pickCamera.GetViewMatrix();
        mat4 proj = pickCamera.GetProjectionMatrix(aspect);
        pickCamera.FovY = pickSavedFovY; pickCamera.Near = pickSavedNear; pickCamera.Far = pickSavedFar;

        // ── Render pick pass ─────────────────────────────────────────────────
        Gl.BindFramebuffer(GLEnum.Framebuffer, _pickFbo);
        Gl.Viewport(0, 0, w, h);
        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.ClearColor(0f, 0f, 0f, 0f);
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        Gl.UseProgram(_pickShader.ShaderProgram);
        RenderPickObjects(SceneObjects, view, proj);

        Gl.Disable(GLEnum.DepthTest);
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);

        // ── Read back the clicked pixel ───────────────────────────────────────
        // Map screen-space click coords into pick texture pixel coords.
        // imageMin / imageSize describe the sub-rect of the window where the
        // 3D image is displayed.  We clamp to valid texture bounds.
        float relX = (screenX - imageMin.X) / imageSize.X;
        float relY = (screenY - imageMin.Y) / imageSize.Y;

        // Guard: click outside the image rect → clear selection.
        if (relX < 0f || relX > 1f || relY < 0f || relY > 1f)
        {
            if (!ctrlHeld) SelectionManager.Instance?.ClearSelection();
            return;
        }

        int pixelX = (int)(relX * w);
        int pixelY = (int)((1f - relY) * h); // flip Y: OpenGL origin is bottom-left

        pixelX = Math.Clamp(pixelX, 0, (int)w - 1);
        pixelY = Math.Clamp(pixelY, 0, (int)h - 1);

        // Read a single pixel from the pick texture.
        byte[] pixel = new byte[4]; // RGBA
        Gl.BindTexture(GLEnum.Texture2D, _pickColorTex);
        fixed (byte* p = pixel)
        {
            // Use glGetTexImage to pull the full texture, then index into it.
            // For a single-pixel read glReadPixels against the bound FBO is cheaper,
            // but that requires re-binding the FBO.  We use glReadPixels here.
            Gl.BindFramebuffer(GLEnum.Framebuffer, _pickFbo);
            Gl.ReadPixels(pixelX, pixelY, 1, 1, GLEnum.Rgba, GLEnum.UnsignedByte, p);
            Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        }
        Gl.BindTexture(GLEnum.Texture2D, 0);

        byte r = pixel[0], g = pixel[1], b = pixel[2], a = pixel[3];

        // Alpha near zero → empty space was clicked.
        if (a < 5)
        {
            if (!ctrlHeld) SelectionManager.Instance?.ClearSelection();
            return;
        }

        // Decode the pick ID from the RGB channels.
        int pickId = r | (g << 8) | (b << 16);
        if (pickId == 0)
        {
            if (!ctrlHeld) SelectionManager.Instance?.ClearSelection();
            return;
        }

        // Resolve pick ID → SceneObject.
        SceneObject? hit = FindObjectByPickId(SceneObjects, pickId);

        var sm = SelectionManager.Instance;
        if (sm == null) return;

        if (ctrlHeld)
        {
            if (hit != null) sm.ToggleSelection(hit);
        }
        else
        {
            sm.ClearSelection();
            if (hit != null) sm.SelectObject(hit);
        }
    }

    /// <summary>
    /// Recursively renders every selectable scene object using the flat pick shader.
    /// Objects with no meshes (or <c>IsSelectable == false</c>) are skipped.
    /// </summary>
    private unsafe void RenderPickObjects(IEnumerable<SceneObject> objects, mat4 view, mat4 proj)
    {
        if (_pickShader == null) return;

        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility()) continue;

            bool hasBoneIndicator = obj is BoneSceneObject b && b.IndicatorMesh != null;
            if (obj.IsSelectable && (obj.Visuals.Count > 0 || hasBoneIndicator))
            {
                mat4 model = obj.GetWorldMatrix();
                mat4 mvp   = proj * view * model;

                // Upload MVP.
                int mvpLoc = Gl.GetUniformLocation(_pickShader.ShaderProgram, "uMVP");
                if (mvpLoc >= 0)
                {
                    float[] f =
                    {
                        mvp.m00, mvp.m01, mvp.m02, mvp.m03,
                        mvp.m10, mvp.m11, mvp.m12, mvp.m13,
                        mvp.m20, mvp.m21, mvp.m22, mvp.m23,
                        mvp.m30, mvp.m31, mvp.m32, mvp.m33,
                    };
                    fixed (float* p = f)
                        Gl.UniformMatrix4(mvpLoc, 1, false, p);
                }

                // Upload pick colour.
                int colorLoc = Gl.GetUniformLocation(_pickShader.ShaderProgram, "uPickColor");
                if (colorLoc >= 0)
                    Gl.Uniform3(colorLoc, obj.PickColor.x, obj.PickColor.y, obj.PickColor.z);

                // Draw geometry meshes.
                foreach (var mesh in obj.Visuals)
                    mesh.RenderPickPass(Gl);

                // Draw the bone indicator octahedron as an additional pick target.
                if (obj is BoneSceneObject bone && bone.IndicatorMesh != null)
                    bone.IndicatorMesh.RenderPickPass(Gl);
            }

            // Recurse into children.
            RenderPickObjects(obj.Children, view, proj);
        }
    }

    /// <summary>
    /// Pass 1 of the Sobel outline effect.
    ///
    /// Renders every visible mesh belonging to a selected object into the
    /// single-channel <see cref="_silhouetteFbo"/> as flat white (1.0).  The FBO
    /// has no depth attachment; <c>GL_ALWAYS</c> ensures occluded parts of the
    /// object still contribute to the mask, so the outline is drawn "through"
    /// other geometry.
    /// </summary>
    private unsafe void RenderSilhouettePass(mat4 view, mat4 proj)
    {
        if (_silhouetteShader == null) return;

        var sm = SelectionManager.Instance;
        if (sm == null || sm.SelectedObjects.Count == 0) return;

        Gl.BindFramebuffer(GLEnum.Framebuffer, _silhouetteFbo);
        Gl.Viewport(0, 0, _viewportWidth, _viewportHeight);

        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.DepthTest);   // always stamp the full shape, even if behind other objects

        Gl.ClearColor(0f, 0f, 0f, 0f);
        Gl.Clear(ClearBufferMask.ColorBufferBit);

        uint prog = _silhouetteShader.ShaderProgram;
        Gl.UseProgram(prog);

        int mvpLoc = Gl.GetUniformLocation(prog, "uMVP");

        foreach (var obj in sm.SelectedObjects)
        {
            if (!obj.GetEffectiveVisibility()) continue;
            if (obj.Visuals.Count == 0) continue;

            mat4 model = obj.GetWorldMatrix();
            mat4 mvp   = proj * view * model;

            if (mvpLoc >= 0)
            {
                float[] f =
                {
                    mvp.m00, mvp.m01, mvp.m02, mvp.m03,
                    mvp.m10, mvp.m11, mvp.m12, mvp.m13,
                    mvp.m20, mvp.m21, mvp.m22, mvp.m23,
                    mvp.m30, mvp.m31, mvp.m32, mvp.m33,
                };
                fixed (float* p = f)
                    Gl.UniformMatrix4(mvpLoc, 1, false, p);
            }

            foreach (var mesh in obj.Visuals)
                mesh.RenderPickPass(Gl);
        }

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
    }

    /// <summary>
    /// Pass 2 of the Sobel outline effect.
    ///
    /// Runs a full-screen Sobel gradient filter over the silhouette mask produced
    /// by <see cref="RenderSilhouettePass"/> and blends the resulting edge pixels
    /// on top of the main display FBO using <see cref="EdgeColor"/>.
    /// </summary>
    private unsafe void RenderEdgePass()
    {
        if (_edgeShader == null) return;

        var sm = SelectionManager.Instance;
        if (sm == null || sm.SelectedObjects.Count == 0) return;

        // Draw into the main scene FBO.
        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        Gl.Viewport(0, 0, _viewportWidth, _viewportHeight);

        Gl.Disable(GLEnum.DepthTest);
        Gl.Disable(GLEnum.CullFace);

        // Additive-style alpha blend so the edge sits on top of the scene.
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);

        uint prog = _edgeShader.ShaderProgram;
        Gl.UseProgram(prog);

        // Bind the silhouette mask texture to unit 0.
        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, _silhouetteTex);
        int maskLoc = Gl.GetUniformLocation(prog, "uMask");
        if (maskLoc >= 0) Gl.Uniform1(maskLoc, 0);

        // Texel size = 1 / viewport.  The shader uses this both for UV from
        // gl_FragCoord and for computing neighbour sample offsets.
        int texelLoc = Gl.GetUniformLocation(prog, "uTexelSize");
        if (texelLoc >= 0)
            Gl.Uniform2(texelLoc,
                        1f / _viewportWidth,
                        1f / _viewportHeight);

        int colorLoc = Gl.GetUniformLocation(prog, "uEdgeColor");
        if (colorLoc >= 0)
            Gl.Uniform4(colorLoc, EdgeColor.x, EdgeColor.y, EdgeColor.z, EdgeColor.w);

        int threshLoc = Gl.GetUniformLocation(prog, "uThreshold");
        if (threshLoc >= 0) Gl.Uniform1(threshLoc, EdgeThreshold);

        // Full-screen triangle: 3 vertices, no VBO.
        Gl.BindVertexArray(_edgeVao);
        Gl.DrawArrays(GLEnum.Triangles, 0, 3);
        Gl.BindVertexArray(0);

        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.Disable(GLEnum.Blend);
        // Leave _fbo bound and restore depth/cull state so the gizmo (rendered
        // immediately after this pass) draws into the correct framebuffer.
        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
    }

    /// <summary>
    /// Recursively walks <paramref name="objects"/> and appends every visible
    /// <see cref="LightSceneObject"/> to <see cref="Mesh.PointLights"/>.
    /// </summary>
    private static void CollectPointLights(IEnumerable<SceneObject> objects)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility()) continue;

            if (obj is LightSceneObject light)
            {
                mat4 world = obj.GetWorldMatrix();
                var pos    = new vec3(world.m30, world.m31, world.m32);
                var col    = new vec3(light.LightColor.x, light.LightColor.y, light.LightColor.z);
                Mesh.PointLights.Add((pos, col, light.LightRange, light.LightEnergy));
            }

            CollectPointLights(obj.Children);
        }
    }

    /// <summary>
    /// Recursively collects (worldMatrix, mesh) pairs from every visible node in the
    /// scene hierarchy.  Each node uses its own <see cref="SceneObject.GetWorldMatrix"/>
    /// so that child nodes are correctly positioned relative to their parents.
    /// </summary>
    private static void CollectRenderPairs(
        IEnumerable<SceneObject> objects,
        vec3 camPos,
        List<(mat4 model, Mesh mesh)> opaque,
        List<(mat4 model, Mesh mesh, float dist)> textured,
        List<(mat4 model, Mesh mesh, float dist)> alphaBlend,
        List<(mat4 model, Mesh mesh)> overlays)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility()) continue;

            mat4  model    = obj.GetWorldMatrix();
            vec3  worldPos = new vec3(model.m30, model.m31, model.m32);
            float dist     = (worldPos - camPos).LengthSqr;

            // Only this node's own Visuals — not its descendants.
            foreach (Mesh mesh in obj.Visuals)
            {
                // Overlay meshes (e.g. camera icon) are collected separately so
                // they can be shown/hidden with the Overlays toggle and are never
                // fed into the normal depth-sorted scene passes.
                if (mesh.DepthTestDisabled)
                {
                    overlays.Add((model, mesh));
                    continue;
                }

                if (mesh.TextureId != 0)
                    textured.Add((model, mesh, dist));
                else if (mesh.Alpha < 1.0f)
                    alphaBlend.Add((model, mesh, dist));
                else
                    opaque.Add((model, mesh));
            }

            // Recurse into children so each child uses its own world matrix.
            CollectRenderPairs(obj.Children, camPos, opaque, textured, alphaBlend, overlays);
        }
    }

    /// <summary>
    /// Collects all <see cref="CameraSceneObject"/> instances from the entire scene.
    /// </summary>
    private List<CameraSceneObject> GetSpawnedCameras()
    {
        var result = new List<CameraSceneObject>();
        CollectSpawnedCameras(SceneObjects, result);
        return result;
    }

    private static void CollectSpawnedCameras(
        IEnumerable<SceneObject> objects,
        List<CameraSceneObject> result)
    {
        foreach (var obj in objects)
        {
            if (obj is CameraSceneObject cam) result.Add(cam);
            CollectSpawnedCameras(obj.Children, result);
        }
    }

    /// <summary>
    /// Returns the active render <see cref="Camera"/> based on <see cref="_activeCameraIndex"/>.
    /// Index 0 = work camera; 1+ = spawned cameras.
    /// Also returns the associated <see cref="CameraSceneObject"/> if applicable.
    /// </summary>
    private (Camera cam, CameraSceneObject? sceneObj) GetActiveRenderCamera()
    {
        if (_activeCameraIndex == 0) return (Camera, null);

        var spawned = GetSpawnedCameras();
        int idx     = _activeCameraIndex - 1;

        if (idx >= 0 && idx < spawned.Count)
        {
            var camObj = spawned[idx];
            camObj.SyncCameraToTransform();
            return (camObj.ViewCamera, camObj);
        }

        _activeCameraIndex = 0;
        return (Camera, null);
    }

    /// <summary>
    /// Depth-first search for the <see cref="SceneObject"/> whose
    /// <see cref="SceneObject.PickColorId"/> matches <paramref name="pickId"/>.
    /// Returns null if no match is found.
    /// </summary>
    private static SceneObject? FindObjectByPickId(IEnumerable<SceneObject> objects, int pickId)
    {
        foreach (var obj in objects)
        {
            if (obj.PickColorId == pickId) return obj;
            var hit = FindObjectByPickId(obj.Children, pickId);
            if (hit != null) return hit;
        }
        return null;
    }

    // ── Camera / gizmo input ───────────────────────────────────────────────────

    private void HandleCameraInput()
    {
        // Allow input to continue while free-fly is active even if the ImGui
        // mouse position has drifted outside the panel (the OS cursor is locked
        // by GLFW so the real cursor never moved — only the internal position).
        if (!_freeFly && !ImGui.IsWindowHovered()) return;

        var io = ImGui.GetIO();

        // While free-fly is active, tell ImGui this window owns the mouse so
        // other panels and widgets do not receive hover/click events.
        if (_freeFly)
            ImGui.SetNextFrameWantCaptureMouse(true);

        float mouseX = io.MousePos.X;
        float mouseY = io.MousePos.Y;

        // Seed last position on first call so we never get a giant spurious delta.
        if (float.IsNaN(_lastMouseX)) { _lastMouseX = mouseX; _lastMouseY = mouseY; }

        float dx = mouseX - _lastMouseX;
        float dy = mouseY - _lastMouseY;

        // Reconstruct image rect from ImGui cursor (set just before ImGui.Image call).
        // We approximate it here from the window content region.
        var imageMin  = ImGui.GetWindowPos() + ImGui.GetCursorPos();
        var imageSize = ImGui.GetContentRegionAvail();
        var mousePos  = new Vector2(mouseX, mouseY);

        // ── G key: toggle global ↔ local transform space ─────────────────────
        if (Gizmo != null && Gizmo.Visible && !Gizmo.Editing &&
            ImGui.IsKeyPressed(ImGuiKey.G))
        {
            Gizmo.UseLocalSpace = !Gizmo.UseLocalSpace;
        }

        // ── Gizmo hover (no button held) ──────────────────────────────────────
        if (Gizmo != null && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            Gizmo.UpdateHover(mousePos, Camera, imageMin, imageSize);

        // Left-button pressed this frame: try gizmo first, then orbit
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (!_dragging && !_gizmoDragging)
            {
                // Record press position for drag vs. click discrimination.
                if (float.IsNaN(_pressMouseX))
                {
                    _pressMouseX = mouseX;
                    _pressMouseY = mouseY;
                }

                // New press: check if gizmo claims it
                if (Gizmo != null && Gizmo.TryBeginEdit(mousePos, Camera, imageMin, imageSize))
                {
                    _gizmoDragging = true;
                }
                else
                {
                    // Only begin orbit if the mouse has moved beyond the threshold.
                    float moveDist = MathF.Sqrt(
                        (mouseX - _pressMouseX) * (mouseX - _pressMouseX) +
                        (mouseY - _pressMouseY) * (mouseY - _pressMouseY));
                    if (moveDist >= OrbitDragThreshold)
                        _dragging = true;
                }
            }

            if (_gizmoDragging)
                Gizmo?.ContinueEdit(mousePos);
            else if (_dragging)
                Camera.Orbit(dx * 0.005f, dy * 0.005f);
        }
        else
        {
            // Left-button just released.
            bool wasGizmoDragging = _gizmoDragging;
            bool wasOrbitDragging = _dragging;

            if (_gizmoDragging) Gizmo?.EndEdit();
            _dragging      = false;
            _gizmoDragging = false;

            // Queue a colour-pick if:
            //   - We were not finishing a gizmo drag
            //   - We were not finishing an orbit drag (mouse moved beyond threshold)
            //   - The gizmo is not currently hovering a handle (would be a gizmo click)
            bool gizmoHovering = Gizmo?.Hovering ?? false;
            if (!wasGizmoDragging && !wasOrbitDragging && !gizmoHovering &&
                ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _pendingPickX    = mouseX;
                _pendingPickY    = mouseY;
                _pendingPickCtrl = io.KeyCtrl;
            }

            // Reset press-position tracker.
            _pressMouseX = float.NaN;
            _pressMouseY = float.NaN;
        }

        // Middle-button drag → pan
        if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            if (_panning)
                Camera.Pan(-dx * 0.01f * (Camera.Distance / 5f),
                            dy * 0.01f * (Camera.Distance / 5f));
            _panning = true;
        }
        else
        {
            _panning = false;
        }

        // Scroll wheel → zoom (normal mode) or speed change (free-fly mode)
        if (io.MouseWheel != 0)
        {
            if (_freeFly)
            {
                // Multiply/divide speed by a fixed factor per notch so the steps
                // feel consistent at any speed level.
                float factor = io.MouseWheel > 0 ? 1.3f : 1f / 1.3f;
                for (int i = 0; i < (int)MathF.Abs(io.MouseWheel); i++)
                    _freeFlySpeed *= factor;
                _freeFlySpeed = Math.Clamp(_freeFlySpeed, 0.1f, 500f);
            }
            else
            {
                Camera.Zoom(io.MouseWheel * Camera.Distance * 0.1f);
            }
        }

        // ── Right-button: free-fly mode ───────────────────────────────────────
        // While the right mouse button is held GLFW cursor mode is set to
        // CursorDisabled: the OS cursor is hidden and locked; GLFW delivers
        // raw unbounded deltas via the normal cursor-position callbacks, which
        // the ImGui GLFW backend exposes as io.MouseDelta each frame.
        // Controls:
        //   • Mouse delta  → look (yaw / pitch)
        //   • W / S        → move forward / backward
        //   • A / D        → strafe left / right
        //   • E / Q        → move up / down
        if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            // Lock cursor on the first frame of right-click.
            if (!_freeFly && GlfwApi != null)
            {
                unsafe
                {
                    GlfwApi.SetInputMode(GlfwWindow,
                        CursorStateAttribute.Cursor,
                        CursorModeValue.CursorDisabled);
                }
            }

            if (_freeFly)
            {
                // io.MouseDelta is populated by the GLFW backend from the raw
                // cursor position callback — correct even while cursor is locked.
                float lookDx =  io.MouseDelta.X * FreeFlyLookSensitivity;
                // Screen Y increases downward; negate so dragging down pitches down.
                float lookDy = -io.MouseDelta.Y * FreeFlyLookSensitivity;

                // Look rotates the camera in place: the eye stays fixed and
                // Target is repositioned ahead of it (FPS-style, not orbit).
                Camera.Look(lookDx, lookDy);

                // WASD / QE keyboard movement (frame-rate independent).
                float dt    = io.DeltaTime;
                float speed = _freeFlySpeed * Camera.Distance * 0.2f;  // scale with distance

                // Space = speed boost (×2.5), Shift = slow (×0.4).
                if (ImGui.IsKeyDown(ImGuiKey.Space))         speed *= 2.5f;
                else if (ImGui.IsKeyDown(ImGuiKey.ModShift)) speed *= 0.4f;

                float fwdDelta   = 0f;
                float rightDelta = 0f;
                float upDelta    = 0f;

                if (ImGui.IsKeyDown(ImGuiKey.W)) fwdDelta   += speed * dt;
                if (ImGui.IsKeyDown(ImGuiKey.S)) fwdDelta   -= speed * dt;
                if (ImGui.IsKeyDown(ImGuiKey.D)) rightDelta += speed * dt;
                if (ImGui.IsKeyDown(ImGuiKey.A)) rightDelta -= speed * dt;
                if (ImGui.IsKeyDown(ImGuiKey.E)) upDelta    += speed * dt;
                if (ImGui.IsKeyDown(ImGuiKey.Q)) upDelta    -= speed * dt;

                if (fwdDelta != 0f || rightDelta != 0f || upDelta != 0f)
                    Camera.MoveFreeFly(fwdDelta, rightDelta, upDelta);
            }

            _freeFly = true;
        }
        else
        {
            // Restore normal cursor when right mouse button is released.
            if (_freeFly && GlfwApi != null)
            {
                unsafe
                {
                    GlfwApi.SetInputMode(GlfwWindow,
                        CursorStateAttribute.Cursor,
                        CursorModeValue.CursorNormal);
                }
            }
            _freeFly = false;
        }

        _lastMouseX = mouseX;
        _lastMouseY = mouseY;
    }
}
