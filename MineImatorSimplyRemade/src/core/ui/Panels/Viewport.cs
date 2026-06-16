using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class Viewport : UiPanel
{
    // ── Scene ──────────────────────────────────────────────────────────────────

    public List<SceneObject> SceneObjects { get; } = new();

    // ── Ground plane ───────────────────────────────────────────────────────────

    /// <summary>
    /// The XZ-plane ground mesh that displays the tiled terrain texture.
    /// Initialised in <see cref="InitGroundPlane"/> after atlases are loaded.
    /// </summary>
    private PlaneMesh? _groundPlane;

    // ── Camera ─────────────────────────────────────────────────────────────────

    public Camera Camera { get; } = new Camera();

    // ── Orbit drag state ───────────────────────────────────────────────────────

    private bool  _dragging;
    private bool  _panning;
    private float _lastMouseX = float.NaN;
    private float _lastMouseY = float.NaN;

    // ── Framebuffer ────────────────────────────────────────────────────────────

    private uint _fbo;
    private uint _colorTex;
    private uint _rbo;

    private uint _viewportWidth, _viewportHeight;

    // ── Ground plane setup ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates the 64×64 XZ ground plane and assigns the terrain atlas tile (8,2)
    /// as its texture.  Must be called after both the GL context and
    /// <see cref="TerrainAtlas"/> are initialised.
    /// </summary>
    public void InitGroundPlane()
    {
        if (Gl == null) return;

        _groundPlane = new PlaneMesh(Gl, 64f, 64f, PlaneOrientation.XZ);

        // Terrain tile (8,2): column 8, row 2 of the Minecraft 1.3.2 terrain sheet
        // (grass-top / dirt depending on the atlas version).
        if (TerrainAtlas.Textures.TryGetValue("8,2", out uint tileId))
            _groundPlane.TextureId = tileId;
    }

    // ── FBO setup ──────────────────────────────────────────────────────────────

    public unsafe void InitFramebuffer(uint width, uint height)
    {
        _viewportWidth  = width;
        _viewportHeight = height;

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
    }

    private unsafe void ResizeFramebuffer(uint width, uint height)
    {
        _viewportWidth  = width;
        _viewportHeight = height;

        Gl.BindTexture(GLEnum.Texture2D, _colorTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, width, height, 0,
                      PixelFormat.Rgb, GLEnum.UnsignedByte, (void*)0);

        Gl.BindRenderbuffer(GLEnum.Renderbuffer, _rbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, width, height);

        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);
    }

    // ── Render ─────────────────────────────────────────────────────────────────

    public override unsafe void Render()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);

        ImGui.Begin("Viewport");

        var size = ImGui.GetContentRegionAvail();

        // Skip rendering until ImGui has finished its first layout pass and the
        // panel has a real size. Avoids drawing into a 1×1 or 4×1 stub texture.
        if (size.X < 16 || size.Y < 16)
        {
            ImGui.End();
            ImGui.PopStyleVar(2);
            return;
        }

        uint w = (uint)size.X;
        uint h = (uint)size.Y;

        // ── Resize FBO when panel changes size ────────────────────────────────
        if (w != _viewportWidth || h != _viewportHeight)
            ResizeFramebuffer(w, h);

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

        Gl.ClearColor(0.18f, 0.18f, 0.18f, 1.0f);
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float aspect = (h > 0) ? (float)w / h : 1f;
        mat4 view    = Camera.GetViewMatrix();
        mat4 proj    = Camera.GetProjectionMatrix(aspect);

        // ── Ground plane ──────────────────────────────────────────────────────
        if (_groundPlane != null)
            _groundPlane.Render(mat4.Identity, view, proj);

        // ── Scene objects ─────────────────────────────────────────────────────
        foreach (var sceneObject in SceneObjects)
        {
            if (!sceneObject.GetEffectiveVisibility()) continue;

            mat4 model = sceneObject.GetWorldMatrix();

            foreach (Mesh mesh in sceneObject.GetMeshInstancesRecursively())
                mesh.Render(model, view, proj);
        }

        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.DepthTest);
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);

        // ── Display FBO as ImGui image (flip V for OpenGL→ImGui convention) ────
        ImGui.Image(
            new ImTextureRef(texId: (ulong)_colorTex),
            size,
            new Vector2(0, 1),
            new Vector2(1, 0));

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    // ── Camera input ───────────────────────────────────────────────────────────

    private void HandleCameraInput()
    {
        // Only process input when the viewport panel is hovered.
        if (!ImGui.IsWindowHovered()) return;

        var io = ImGui.GetIO();

        float mouseX = io.MousePos.X;
        float mouseY = io.MousePos.Y;

        // Seed last position on first call so we never get a giant spurious delta.
        if (float.IsNaN(_lastMouseX)) { _lastMouseX = mouseX; _lastMouseY = mouseY; }

        float dx = mouseX - _lastMouseX;
        float dy = mouseY - _lastMouseY;

        // Left-button drag → orbit
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (_dragging)
                Camera.Orbit(dx * 0.005f, dy * 0.005f);
            _dragging = true;
        }
        else
        {
            _dragging = false;
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

        // Scroll wheel → zoom
        if (io.MouseWheel != 0)
            Camera.Zoom(io.MouseWheel * Camera.Distance * 0.1f);

        _lastMouseX = mouseX;
        _lastMouseY = mouseY;
    }
}
