using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.gizmo;
using MineImatorSimplyRemadeNuxi.core;
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

        // ── Gizmo ─────────────────────────────────────────────────────────────
        // Initialise the gizmo now that the GL context is fully ready.
        Gizmo = new Gizmo3D(Gl);
        Gizmo.Init();

        // Register the gizmo with the SelectionManager so selection changes
        // automatically sync to the gizmo handle set.
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.Gizmo = Gizmo;
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

        // ── Gizmo 3D ──────────────────────────────────────────────────────────
        Gizmo?.Render(Camera, view, proj);

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
        Gizmo?.RenderOverlay(Camera, imageMin, size);

        ImGui.End();
        ImGui.PopStyleVar(2);
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
        mat4 view = Camera.GetViewMatrix();
        mat4 proj = Camera.GetProjectionMatrix(aspect);

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

            if (obj.IsSelectable && obj.Visuals.Count > 0)
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

                // Draw each mesh's VAO directly via the pick shader (position attrib 0).
                foreach (var mesh in obj.Visuals)
                    mesh.RenderPickPass(Gl);
            }

            // Recurse into children.
            RenderPickObjects(obj.Children, view, proj);
        }
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
        // Only process input when the viewport panel is hovered.
        if (!ImGui.IsWindowHovered()) return;

        var io = ImGui.GetIO();

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

        // Scroll wheel → zoom
        if (io.MouseWheel != 0)
            Camera.Zoom(io.MouseWheel * Camera.Distance * 0.1f);

        _lastMouseX = mouseX;
        _lastMouseY = mouseY;
    }
}
