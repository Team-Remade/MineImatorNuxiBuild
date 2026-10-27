using GlmSharp;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui;

/// <summary>
/// Off-screen renderer used by spawn previews and the world browser.
///
/// Defaults to a square FBO; <see cref="Resize"/> supports rectangular viewports.
/// Call <see cref="Render"/> with a list of meshes and a selection key each frame;
/// the result is written to <see cref="ColorTexture"/> which can be displayed
/// directly in ImGui via <c>ImGui.Image(new ImTextureRef(texId: (ulong)ColorTexture), …)</c>.
///
/// Uses an orbit camera unless an explicit camera is supplied to <see cref="Render"/>.
/// </summary>
public class PreviewRenderer : IDisposable
{
    // ── FBO resources ─────────────────────────────────────────────────────────
    public const int TextureSize = 256;

    /// <summary>
    /// OpenGL color-texture attached to the preview FBO.
    /// Valid after <see cref="Initialize"/> is called.
    /// </summary>
    public uint ColorTexture { get; private set; }

    private uint _fbo;
    private uint _rbo; // depth+stencil renderbuffer

    // ── Auto-orbit state ──────────────────────────────────────────────────────
    /// <summary>Horizontal orbit angle in radians. Advances each frame.</summary>
    public float Yaw   = 0.75f;

    /// <summary>Vertical tilt in radians (positive = looking slightly down).</summary>
    public float Pitch = 0.4f;

    /// <summary>Distance from target to camera eye (world units).</summary>
    public float Distance = 2.2f;

    /// <summary>Auto-rotation speed in radians per second.</summary>
    public float AutoRotateSpeed = 0.6f;

    // ── GL context ────────────────────────────────────────────────────────────
    private readonly GL _gl;
    public int Width { get; private set; }
    public int Height { get; private set; }
    internal mat4 ViewProjection { get; private set; } = mat4.Identity;

    // ── Dirty tracking ────────────────────────────────────────────────────────
    /// <summary>
    /// Opaque token for the last selection rendered.  Set by the caller; when it
    /// changes the preview is re-rendered immediately (dirty = true) on the next frame.
    /// </summary>
    public string LastSelectionKey { get; private set; } = "";

    private bool _initialized;

    // ── Constructor ───────────────────────────────────────────────────────────

    public PreviewRenderer(GL gl, int textureSize = TextureSize)
    {
        _gl = gl;
        if (textureSize <= 0) throw new ArgumentOutOfRangeException(nameof(textureSize));
        Width = Height = textureSize;
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        float scale = Math.Min(1, 4096f / Math.Max(width, height));
        width = Math.Max(1, (int)(width * scale));
        height = Math.Max(1, (int)(height * scale));
        if (Width == width && Height == height) return;
        Width = width;
        Height = height;
        Initialize();
    }

    // ── Initialization ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the FBO and its color+depth attachments.
    /// Safe to call more than once; previous resources are released first.
    /// </summary>
    public unsafe void Initialize()
    {
        Dispose(); // release previous resources if any

        uint width = (uint)Width, height = (uint)Height;

        // Color texture (RGBA so ImGui can sample it)
        _gl.GenTextures(1, out uint colorTex);
        _gl.BindTexture(GLEnum.Texture2D, colorTex);
        _gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba,
                       width, height, 0,
                       PixelFormat.Rgba, GLEnum.UnsignedByte, (void*)0);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.BindTexture(GLEnum.Texture2D, 0);
        ColorTexture = colorTex;

        // Depth+stencil renderbuffer
        _gl.GenRenderbuffers(1, out _rbo);
        _gl.BindRenderbuffer(GLEnum.Renderbuffer, _rbo);
        _gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, width, height);
        _gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);

        // FBO
        _gl.GenFramebuffers(1, out _fbo);
        _gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        _gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0,
                                 GLEnum.Texture2D, ColorTexture, 0);
        _gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthStencilAttachment,
                                    GLEnum.Renderbuffer, _rbo);

        var status = _gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        _gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        if (status != GLEnum.FramebufferComplete)
        {
            Dispose();
            throw new InvalidOperationException($"Preview framebuffer is incomplete: {status}");
        }

        _initialized = true;
    }

    // ── Render ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders <paramref name="meshes"/> into the preview FBO.
    ///
    /// <paramref name="selectionKey"/> is compared to <see cref="LastSelectionKey"/>;
    /// when they differ the preview is re-rendered and the orbit is reset.  When they
    /// are the same the orbit continues advancing and the FBO is re-rendered each frame
    /// so the auto-rotation is visible.
    ///
    /// <paramref name="deltaTime"/> drives the auto-orbit animation.
    ///
    /// When <paramref name="sceneRoot"/> is non-null the scene-object hierarchy is
    /// rendered in addition to (or instead of) the flat <paramref name="meshes"/> list.
    /// Each node in the hierarchy is rendered with its own world matrix so that a
    /// character's bones appear in their correct poses.
    /// </summary>
    public void Render(
        IReadOnlyList<Mesh> meshes,
        string              selectionKey,
        double              deltaTime,
        float               boundsRadius = 0.75f,
        SceneObject?        sceneRoot    = null,
        Camera?             camera       = null,
        bool                centerMeshes = false)
    {
        if (!_initialized) Initialize();
        if (_fbo == 0 || ColorTexture == 0) return;

        bool selectionChanged = selectionKey != LastSelectionKey;
        if (selectionChanged && camera == null)
        {
            LastSelectionKey = selectionKey;
            Yaw   = 0.75f;
            Pitch = 0.4f;
        }

        // Advance auto-orbit
        LastSelectionKey = selectionKey;
        if (camera == null)
            Yaw += AutoRotateSpeed * (float)deltaTime;

        // ── Bind preview FBO ─────────────────────────────────────────────────
        _gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        _gl.Enable(GLEnum.DepthTest);
        _gl.DepthFunc(GLEnum.Less);
        _gl.Enable(GLEnum.CullFace);
        _gl.CullFace(GLEnum.Back);
        _gl.Enable(GLEnum.Blend);
        _gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);

        // Dark charcoal background  (0.12, 0.12, 0.14, 1)
        _gl.ClearColor(0.12f, 0.12f, 0.14f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        bool hasSomethingToRender = meshes.Count > 0 || sceneRoot != null || camera != null;
        if (hasSomethingToRender)
        {
            // ── Camera matrices ───────────────────────────────────────────────
            float cosP = MathF.Cos(Pitch);
            var eye = new vec3(
                cosP * MathF.Sin(Yaw),
                MathF.Sin(Pitch),
                cosP * MathF.Cos(Yaw)) * Distance;

            mat4 view = camera?.GetViewMatrix() ?? mat4.LookAt(eye, vec3.Zero, vec3.UnitY);
            mat4 proj = camera?.GetProjectionMatrix((float)Width / Height) ?? mat4.Perspective(
                glm.Radians(50f),   // 50° FOV
                (float)Width / Height,
                0.05f,
                100f);
            ViewProjection = proj * view;

            // ── Flat mesh list (Items, Blocks, Primitives) ────────────────────
            var previousCameraPosition = Mesh.GlobalCameraPosition;
            if (camera != null) Mesh.GlobalCameraPosition = camera.Position;
            try
            {
                mat4 model = mat4.Identity;
                if (centerMeshes)
                {
                    vec3 min = new vec3(float.PositiveInfinity);
                    vec3 max = new vec3(float.NegativeInfinity);
                    foreach (var mesh in meshes)
                    foreach (var vertex in mesh.Vertices)
                    {
                        min = vec3.Min(min, vertex);
                        max = vec3.Max(max, vertex);
                    }
                    if (float.IsFinite(min.x))
                        model = mat4.Translate(-(min + max) * 0.5f);
                }
                foreach (var mesh in meshes)
                {
                    Mesh.ApplyBlendMode(_gl, mesh.BlendMode);
                    mesh.Render(model, view, proj);
                }

                if (sceneRoot != null)
                    RenderSceneObjectRecursive(sceneRoot, view, proj);
            }
            finally
            {
                Mesh.GlobalCameraPosition = previousCameraPosition;
            }
        }

        // ── Restore GL state ─────────────────────────────────────────────────
        _gl.BlendEquation(GLEnum.FuncAdd);
        _gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        _gl.BindFramebuffer(GLEnum.Framebuffer, 0);
    }

    /// <summary>
    /// Recursively renders all visible nodes in a <see cref="SceneObject"/> hierarchy,
    /// applying each node's own world matrix so that bones are placed correctly.
    /// </summary>
    private void RenderSceneObjectRecursive(SceneObject obj, mat4 view, mat4 proj)
    {
        if (!obj.GetEffectiveVisibility()) return;

        mat4 worldMatrix = obj.GetWorldMatrix();

        foreach (var mesh in obj.Visuals)
        {
            Mesh.ApplyBlendMode(_gl, mesh.BlendMode);
            mesh.Render(worldMatrix, view, proj);
        }

        foreach (var child in obj.Children)
            RenderSceneObjectRecursive(child, view, proj);
    }

    // ── Manual orbit (drag) ───────────────────────────────────────────────────

    /// <summary>Applies a mouse-drag delta to the orbit angles.</summary>
    public void Orbit(float deltaYaw, float deltaPitch)
    {
        Yaw   += deltaYaw;
        Pitch  = Math.Clamp(Pitch + deltaPitch, -MathF.PI / 2f + 0.01f, MathF.PI / 2f - 0.01f);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_fbo != 0)
        {
            _gl.DeleteFramebuffers(1, _fbo);
            _fbo = 0;
        }
        if (ColorTexture != 0)
        {
            _gl.DeleteTextures(1, ColorTexture);
            ColorTexture = 0;
        }
        if (_rbo != 0)
        {
            _gl.DeleteRenderbuffers(1, _rbo);
            _rbo = 0;
        }
        _initialized = false;
    }
}
