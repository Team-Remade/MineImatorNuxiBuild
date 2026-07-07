using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.window.windows;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// Secondary viewport that renders the scene from the work camera or any
/// user-spawned <see cref="CameraSceneObject"/>.
///
/// Inline (docked) mode: a resizable/draggable overlay anchored to one of the
/// four corners of the main viewport image.  A "Pop" button opens a dedicated
/// GLFW window via <see cref="PopRequested"/>.
///
/// Windowed mode: rendered by <see cref="CameraWindow"/> which calls the public
/// render helpers directly.
/// </summary>
public class CameraViewport : UiPanel
{
    private enum SceneRenderMode
    {
        Unrendered,
        Rendered
    }

    // ── References ────────────────────────────────────────────────────────────

    public Viewport? MainViewport { get; set; }

    // ── GLFW (for cursor lock during free-fly) ────────────────────────────────

    public Glfw? GlfwApi { get; set; }
    public unsafe WindowHandle* GlfwWindow { get; set; }

    // ── Active camera selection ───────────────────────────────────────────────

    private int _selectedCameraIndex = 0;

    // ── Framebuffer ───────────────────────────────────────────────────────────

    private uint _fbo;
    private uint _colorTex;
    private uint _rbo;
    private uint _width  = 1;
    private uint _height = 1;
    private uint _shadowFbo;
    private uint _shadowTex;
    private uint _shadowMapSize = 2048;
    private MineImatorSimplyRemade.core.mdl.Shader? _shadowShader;
    private uint _pointShadowFbo;
    private MineImatorSimplyRemade.core.mdl.Shader? _pointShadowShader;
    private const int MaxPointShadowLights = 4;
    private const uint PointShadowMapSize = 1024;
    private readonly uint[] _pointShadowCubeTextures = new uint[MaxPointShadowLights];

    private static readonly vec3[] PointShadowFaceDirections =
    [
        new vec3(1f, 0f, 0f),
        new vec3(-1f, 0f, 0f),
        new vec3(0f, 1f, 0f),
        new vec3(0f, -1f, 0f),
        new vec3(0f, 0f, 1f),
        new vec3(0f, 0f, -1f)
    ];

    private static readonly vec3[] PointShadowFaceUps =
    [
        new vec3(0f, -1f, 0f),
        new vec3(0f, -1f, 0f),
        new vec3(0f, 0f, 1f),
        new vec3(0f, 0f, -1f),
        new vec3(0f, -1f, 0f),
        new vec3(0f, -1f, 0f)
    ];

    /// <summary>The GL colour texture for the current frame (used by CameraWindow).</summary>
    public uint ColorTexture => _colorTex;

    // ── Free-fly state ────────────────────────────────────────────────────────

    private bool  _freeFly;
    private float _freeFlySpeed = 5f;
    private const float FreeFlyLookSensitivity = 0.003f;
    private float _lastMouseX = float.NaN;
    private float _lastMouseY = float.NaN;

    // ── Overlay visibility ─────────────────────────────────────────────────────

    /// <summary>
    /// When true the preview viewport renders editor overlays (gizmo, selection
    /// outline, light billboards, bone indicators).  Defaults to <c>false</c> so
    /// the preview is a clean render-output view out of the box.
    /// </summary>
    public bool OverlaysEnabled { get; set; } = false;
    public bool InlineVisible { get; private set; } = true;
    public bool HighQualityPreviewEnabled { get; private set; } = false;
    public bool ShadowDebugEnabled { get; private set; } = false;

    public bool IsInlineVisible =>
        !Undocked &&
        InlineVisible &&
        !(MainViewport?.SuppressInlineCameraViewport ?? false);

    public bool IsVisible => Undocked || IsInlineVisible;

    public Vector2 GetPreviewDrawSize(Vector2 available)
    {
        float fallbackAspect = available.Y > 0f ? available.X / available.Y : (16f / 9f);
        float targetAspect = GetProjectPreviewAspect(fallbackAspect);
        return FitSizeToAspect(available, targetAspect);
    }

    // ── Docking / pop state ───────────────────────────────────────────────────

    /// <summary>True while a GLFW CameraWindow owns the rendering.</summary>
    public bool Undocked { get; set; } = false;

    /// <summary>
    /// Raised when the user clicks "Pop".  The subscriber (MainWindow / main.cs)
    /// should create a <see cref="CameraWindow"/> and add it to the window list.
    /// </summary>
    public event Action? PopRequested;

    /// <summary>
    /// Raised when the preview requests that an undocked camera window be hidden.
    /// </summary>
    public event Action? HideRequested;

    // ── Inline panel geometry ─────────────────────────────────────────────────

    /// <summary>Which corner the inline panel is anchored to.</summary>
    private enum Corner { BottomRight, BottomLeft, TopRight, TopLeft }
    private Corner _corner = Corner.BottomRight;

    /// <summary>Current inline panel size (user-resizable).</summary>
    private Vector2 _inlineSize = new Vector2(340f, 220f);

    private const float InlineMinW = 160f;
    private const float InlineMinH = 120f;
    private const float InlinePad  = 8f;
    private bool _inlineResizeDragActive;
    private Vector2 _prevInlineWindowSize;
    private bool _hasPrevInlineWindowSize;

    // ── Constructor ───────────────────────────────────────────────────────────

    public CameraViewport() { }

    public void ToggleInlineVisibility()
    {
        if (Undocked)
        {
            Undocked = false;
            InlineVisible = false;
            HideRequested?.Invoke();
            return;
        }

        InlineVisible = !InlineVisible;
    }

    public void ToggleHighQualityPreview()
    {
        HighQualityPreviewEnabled = !HighQualityPreviewEnabled;
        if (!HighQualityPreviewEnabled)
            ShadowDebugEnabled = false;
    }

    public void ToggleShadowDebugMode()
    {
        ShadowDebugEnabled = !ShadowDebugEnabled;
        if (ShadowDebugEnabled)
            HighQualityPreviewEnabled = true;
    }

    private float GetProjectPreviewAspect(float fallbackAspect)
    {
        int width = MainViewport?.PropertiesPanel?.GetResolutionWidth() ?? 0;
        int height = MainViewport?.PropertiesPanel?.GetResolutionHeight() ?? 0;
        if (width <= 0 || height <= 0)
            return fallbackAspect > 0f ? fallbackAspect : (16f / 9f);

        return width / (float)height;
    }

    private static Vector2 FitSizeToAspect(Vector2 available, float aspect)
    {
        float availW = MathF.Max(1f, available.X);
        float availH = MathF.Max(1f, available.Y);
        float safeAspect = aspect > 0f ? aspect : (availW / availH);

        float drawW = availW;
        float drawH = drawW / safeAspect;
        if (drawH > availH)
        {
            drawH = availH;
            drawW = drawH * safeAspect;
        }

        return new Vector2(drawW, drawH);
    }

    // ── Init / resize FBO ────────────────────────────────────────────────────

    public unsafe void Init(uint width, uint height)
    {
        if (Gl == null) return;
        _width  = width;
        _height = height;

        Gl.GenFramebuffers(1, out _fbo);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);

        Gl.GenTextures(1, out _colorTex);
        Gl.BindTexture(GLEnum.Texture2D, _colorTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, width, height, 0,
                      PixelFormat.Rgb, GLEnum.UnsignedByte, (void*)0);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0,
                                GLEnum.Texture2D, _colorTex, 0);

        Gl.GenRenderbuffers(1, out _rbo);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, _rbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, width, height);
        Gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthStencilAttachment,
                                   GLEnum.Renderbuffer, _rbo);

        var status = Gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.Error.WriteLine($"[CameraViewport] Framebuffer incomplete: {status}");

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        EnsureShadowResources();
        EnsurePointShadowResources();
    }

    public unsafe void ResizeFboPublic(uint w, uint h)
    {
        if (Gl == null || (w == _width && h == _height)) return;
        _width  = w;
        _height = h;

        Gl.BindTexture(GLEnum.Texture2D, _colorTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, w, h, 0,
                      PixelFormat.Rgb, GLEnum.UnsignedByte, (void*)0);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, _rbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, w, h);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);
    }

    // ── Camera helpers ────────────────────────────────────────────────────────

    public List<CameraSceneObject> GetSpawnedCamerasPublic()
    {
        var result = new List<CameraSceneObject>();
        if (MainViewport == null) return result;
        CollectCameras(MainViewport.SceneObjects, result);
        return result;
    }

    private List<CameraSceneObject> GetSpawnedCameras() => GetSpawnedCamerasPublic();

    private static void CollectCameras(IEnumerable<SceneObject> objs, List<CameraSceneObject> result)
    {
        foreach (var obj in objs)
        {
            if (obj is CameraSceneObject cam) result.Add(cam);
            CollectCameras(obj.Children, result);
        }
    }

    private (Camera cam, CameraSceneObject? sceneObj) GetActiveCamera()
    {
        if (_selectedCameraIndex == 0)
            return (MainViewport?.Camera ?? new Camera(), null);

        var spawned = GetSpawnedCameras();
        int idx     = _selectedCameraIndex - 1;
        if (idx >= 0 && idx < spawned.Count)
        {
            var camObj = spawned[idx];
            camObj.SyncCameraToTransform();
            return (camObj.ViewCamera, camObj);
        }
        _selectedCameraIndex = 0;
        return (MainViewport?.Camera ?? new Camera(), null);
    }

    // ── Camera dropdown ───────────────────────────────────────────────────────

    /// <summary>Public alias used by CameraWindow for the ImGui dropdown.</summary>
    public (Camera, CameraSceneObject?) DrawCameraDropdownPublic(List<CameraSceneObject> spawned)
        => DrawCameraDropdown(spawned);

    /// <summary>
    /// Returns the active camera without drawing any ImGui widgets.
    /// Used by CameraWindow to resolve the camera during the GL phase
    /// (main context) before the ImGui phase switches contexts.
    /// </summary>
    public (Camera, CameraSceneObject?) DrawCameraDropdownInternal(List<CameraSceneObject> spawned)
    {
        if (_selectedCameraIndex > spawned.Count) _selectedCameraIndex = 0;
        return GetActiveCamera();
    }

    private (Camera, CameraSceneObject?) DrawCameraDropdown(List<CameraSceneObject> spawned)
    {
        string currentLabel = _selectedCameraIndex == 0
            ? "Work Camera"
            : (_selectedCameraIndex - 1 < spawned.Count
                ? spawned[_selectedCameraIndex - 1].Name
                : "Work Camera");

        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo("##camSelect", currentLabel))
        {
            bool workSel = _selectedCameraIndex == 0;
            if (ImGui.Selectable("Work Camera", workSel)) _selectedCameraIndex = 0;
            if (workSel) ImGui.SetItemDefaultFocus();

            for (int i = 0; i < spawned.Count; i++)
            {
                bool sel = _selectedCameraIndex == i + 1;
                if (ImGui.Selectable(spawned[i].Name + "##cam" + i, sel))
                    _selectedCameraIndex = i + 1;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (_selectedCameraIndex > spawned.Count) _selectedCameraIndex = 0;
        return GetActiveCamera();
    }

    // ── Render override (no-op — rendering done by RenderInline / CameraWindow) ──

    public override void Render()
    {
        // Rendering is driven externally:
        //   • Inline: Viewport calls RenderInline each frame.
        //   • Windowed: CameraWindow calls the public helpers.
    }

    // ── Inline overlay ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="Viewport.Render"/> each frame (inside the ##vpImage child).
    /// Draws a resizable, draggable overlay panel anchored to one of four corners.
    /// </summary>
    public unsafe void RenderInline(
        Vector2 imageMin,
        Vector2 imageSize,
        List<CameraSceneObject> spawned)
    {
        if (!IsInlineVisible) return;

        // Clamp panel size to available image area.
        _inlineSize.X = Math.Clamp(_inlineSize.X, InlineMinW, imageSize.X - InlinePad * 2);
        _inlineSize.Y = Math.Clamp(_inlineSize.Y, InlineMinH, imageSize.Y - InlinePad * 2);

        // Compute anchored position.
        float posX = _corner switch
        {
            Corner.BottomLeft or Corner.TopLeft => imageMin.X + InlinePad,
            _                                   => imageMin.X + imageSize.X - _inlineSize.X - InlinePad,
        };
        float posY = _corner switch
        {
            Corner.TopLeft or Corner.TopRight => imageMin.Y + InlinePad,
            _                                 => imageMin.Y + imageSize.Y - _inlineSize.Y - InlinePad,
        };

        // While the resize grip is being dragged, avoid forcing anchored
        // position/size each frame; otherwise live resize can fight the lock.
        if (!_inlineResizeDragActive)
        {
            ImGui.SetNextWindowPos(new Vector2(posX, posY), ImGuiCond.Always);
            ImGui.SetNextWindowSize(_inlineSize, ImGuiCond.Always);
        }
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(InlineMinW, InlineMinH),
            new Vector2(imageSize.X - InlinePad * 2, imageSize.Y - InlinePad * 2));

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar        |
            ImGuiWindowFlags.NoScrollbar       |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBringToFrontOnFocus;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.08f, 0.90f));
        bool beginOk = ImGui.Begin("##CamViewInline", flags);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);

        if (!beginOk) { ImGui.End(); return; }

        // Track resize (user drags the resize handle).
        _inlineSize = ImGui.GetWindowSize();

        Vector2 winPos = ImGui.GetWindowPos();
        Vector2 winSz = _inlineSize;
        Vector2 mouse = ImGui.GetMousePos();
        bool leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
        bool overResizeGrip = mouse.X >= winPos.X + winSz.X - 28f &&
                              mouse.X <= winPos.X + winSz.X + 12f &&
                              mouse.Y >= winPos.Y + winSz.Y - 28f &&
                              mouse.Y <= winPos.Y + winSz.Y + 12f;

        bool sizeChangedThisFrame = _hasPrevInlineWindowSize &&
                                    (MathF.Abs(winSz.X - _prevInlineWindowSize.X) > 0.1f ||
                                     MathF.Abs(winSz.Y - _prevInlineWindowSize.Y) > 0.1f);

        if (!leftDown)
            _inlineResizeDragActive = false;
        else if (!_inlineResizeDragActive && ((hovered && overResizeGrip) || sizeChangedThisFrame))
            _inlineResizeDragActive = true;

        _prevInlineWindowSize = winSz;
        _hasPrevInlineWindowSize = true;

        // ── Header row ────────────────────────────────────────────────────────
        var (activeCam, sceneObj) = DrawCameraDropdown(spawned);

        ImGui.SameLine();
        if (ImGui.Button("Pop"))
        {
            Undocked = true;
            PopRequested?.Invoke();
            ImGui.End();
            return;
        }

        // Overlays toggle — same pattern as the main Viewport button.
        ImGui.SameLine();
        {
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

            if (ImGui.Button("Overlays"))
                OverlaysEnabled = !OverlaysEnabled;

            ImGui.PopStyleColor(4);
        }

        // Corner picker — small buttons after the overlays button.
        ImGui.SameLine();
        DrawCornerPicker();

        // ── 3-D render ────────────────────────────────────────────────────────
        var avail = ImGui.GetContentRegionAvail();
        if (avail.X >= 4 && avail.Y >= 4)
        {
            Vector2 drawSize = GetPreviewDrawSize(avail);
            uint w = (uint)Math.Max(1, (int)MathF.Round(drawSize.X));
            uint h = (uint)Math.Max(1, (int)MathF.Round(drawSize.Y));
            ResizeFboPublic(w, h);
            RenderScenePublic(activeCam, sceneObj, w, h);
            HandleFreeFlyPublic(activeCam, sceneObj, ImGui.IsWindowHovered());

            Vector2 startPos = ImGui.GetCursorPos();
            if (avail.X > drawSize.X)
                ImGui.SetCursorPosX(startPos.X + (avail.X - drawSize.X) * 0.5f);
            if (avail.Y > drawSize.Y)
                ImGui.SetCursorPosY(startPos.Y + (avail.Y - drawSize.Y) * 0.5f);

            ImGui.Image(
                new ImTextureRef(texId: (ulong)_colorTex),
                drawSize,
                new Vector2(0, 1),
                new Vector2(1, 0));
        }

        ImGui.End();
    }

    /// <summary>
    /// Draws four tiny arrow/corner buttons that re-anchor the inline panel.
    /// </summary>
    private void DrawCornerPicker()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));

        // Use small Unicode corner glyphs; fall back to ASCII if needed.
        // Layout: [TL][TR] / [BL][BR] — drawn inline as four buttons.
        if (ImGui.Button(_corner == Corner.TopLeft     ? "[TL]" : " TL ", new Vector2(28, 16)))
            _corner = Corner.TopLeft;
        ImGui.SameLine(0, 2);
        if (ImGui.Button(_corner == Corner.TopRight    ? "[TR]" : " TR ", new Vector2(28, 16)))
            _corner = Corner.TopRight;
        ImGui.SameLine(0, 2);
        if (ImGui.Button(_corner == Corner.BottomLeft  ? "[BL]" : " BL ", new Vector2(28, 16)))
            _corner = Corner.BottomLeft;
        ImGui.SameLine(0, 2);
        if (ImGui.Button(_corner == Corner.BottomRight ? "[BR]" : " BR ", new Vector2(28, 16)))
            _corner = Corner.BottomRight;

        ImGui.PopStyleVar();
    }

    // ── Scene render (public so CameraWindow can call it) ────────────────────

    public unsafe void RenderScenePublic(Camera cam, CameraSceneObject? sceneObj, uint w, uint h, bool highQuality = false)
    {
        if (Gl == null || MainViewport == null) return;

        SceneRenderMode renderMode = (highQuality || HighQualityPreviewEnabled)
            ? SceneRenderMode.Rendered
            : SceneRenderMode.Unrendered;

        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        Gl.Viewport(0, 0, w, h);

        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.FrontFace(GLEnum.Ccw);

        float[] bg = MainViewport.PropertiesPanel?.BackgroundColor ?? [0.18f, 0.18f, 0.18f, 1.0f];
        Gl.ClearColor(bg[0], bg[1], bg[2], bg[3]);
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        MainViewport.RenderBackgroundPlanePublic(w, h);

        float aspect = h > 0 ? (float)w / h : 1f;

        float savedFovY = cam.FovY, savedNear = cam.Near, savedFar = cam.Far;
        if (sceneObj != null)
        {
            cam.FovY = GlmSharp.glm.Radians(sceneObj.Fov);
            cam.Near = sceneObj.Near;
            cam.Far  = sceneObj.Far;
        }

        mat4 view = cam.GetViewMatrix();
        mat4 proj = cam.GetProjectionMatrix(aspect);

        cam.FovY = savedFovY; cam.Near = savedNear; cam.Far = savedFar;

        Mesh.DeltaTime = ImGui.GetIO().DeltaTime;
        bool timelinePlaying = Timeline.Instance?.IsPlaying ?? false;
        Mesh.AdvanceAnimatedTextures = timelinePlaying || MainWindow.IsAnimationRenderExportActive;
        int textureAnimFps = Math.Clamp(MainViewport.PropertiesPanel?.TextureAnimationFps ?? 20, 1, 240);
        Mesh.AnimatedTextureSpeedScale = textureAnimFps / 20.0;
        Mesh.ShadowsEnabled = false;
        Mesh.ShadowMapTexture = 0;
        Mesh.ShadowLightSpaceMatrix = mat4.Identity;
        Mesh.ShadowDebugMode = 0;
        Mesh.DirectionalShadowEnabled = MainViewport.PropertiesPanel?.FillLightCastsShadows ?? true;
        Array.Clear(Mesh.PointShadowCubeTextures, 0, Mesh.PointShadowCubeTextures.Length);

        // Scene objects.
        Mesh.PointLights.Clear();
        Dictionary<LightSceneObject, int> pointShadowIndices = new();

        if (renderMode == SceneRenderMode.Rendered)
        {
            Mesh.ShadowDebugMode = ShadowDebugEnabled ? 1 : 0;
            if (Mesh.DirectionalShadowEnabled)
                RenderShadowMap();
            pointShadowIndices = RenderPointShadowMaps();
        }

        CollectPointLights(MainViewport.SceneObjects, pointShadowIndices);

        // Ground plane.
        if (MainViewport.GroundPlane != null && MainViewport.GroundPlaneVisible)
            MainViewport.GroundPlane.Render(mat4.Identity, view, proj);

        vec3 camPos = cam.Position;
        var opaque     = new List<(mat4, Mesh)>();
        var textured   = new List<(mat4, Mesh, float)>();
        var alphaBlend = new List<(mat4, Mesh, float)>();
        var overlays   = new List<(mat4, Mesh)>();
        CollectRenderPairs(MainViewport.SceneObjects, camPos, opaque, textured, alphaBlend, overlays);

        foreach (var (model, mesh) in opaque)
            mesh.Render(model, view, proj);

        if (textured.Count > 0)
        {
            textured.Sort((a, b) => b.Item3.CompareTo(a.Item3));
            Gl.ColorMask(false, false, false, false);
            foreach (var (model, mesh, _) in textured) mesh.Render(model, view, proj);
            Gl.ColorMask(true, true, true, true);
            Gl.DepthFunc(GLEnum.Lequal);
            Gl.Enable(GLEnum.Blend);
            Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
            Gl.DepthMask(false);
            foreach (var (model, mesh, _) in textured) mesh.Render(model, view, proj);
            Gl.DepthMask(true);
            Gl.DepthFunc(GLEnum.Less);
            Gl.Disable(GLEnum.Blend);
        }

        if (alphaBlend.Count > 0)
        {
            alphaBlend.Sort((a, b) => b.Item3.CompareTo(a.Item3));
            Gl.Enable(GLEnum.Blend);
            Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
            Gl.DepthMask(false);
            foreach (var (model, mesh, _) in alphaBlend) mesh.Render(model, view, proj);
            Gl.DepthMask(true);
            Gl.Disable(GLEnum.Blend);
        }

        // ── Editor overlays (optional) ────────────────────────────────────────
        if (OverlaysEnabled)
        {
            // Object-mesh overlays (e.g. camera icon).
            foreach (var (model, mesh) in overlays)
                mesh.Render(model, view, proj);

            MainViewport.RenderOverlaysPublic(view, proj);
        }

        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.DepthTest);
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Mesh.ShadowsEnabled = false;
        Mesh.ShadowMapTexture = 0;
        Mesh.ShadowLightSpaceMatrix = mat4.Identity;
        Mesh.ShadowDebugMode = 0;
        Array.Clear(Mesh.PointShadowCubeTextures, 0, Mesh.PointShadowCubeTextures.Length);
    }

    public unsafe bool CaptureCurrentViewRgb(uint w, uint h, bool highQuality, out byte[] rgbPixels)
    {
        rgbPixels = Array.Empty<byte>();
        if (Gl == null || MainViewport == null || _fbo == 0 || w == 0 || h == 0)
            return false;

        var spawned = GetSpawnedCamerasPublic();
        var (activeCam, sceneObj) = DrawCameraDropdownInternal(spawned);

        bool previousOverlays = OverlaysEnabled;
        OverlaysEnabled = false;
        try
        {
            ResizeFboPublic(w, h);
            RenderScenePublic(activeCam, sceneObj, w, h, highQuality);

            rgbPixels = new byte[checked((int)(w * h * 3))];

            Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
            Gl.PixelStore(GLEnum.PackAlignment, 1);
            fixed (byte* p = rgbPixels)
                Gl.ReadPixels(0, 0, w, h, GLEnum.Rgb, GLEnum.UnsignedByte, p);
            Gl.PixelStore(GLEnum.PackAlignment, 4);
            Gl.BindFramebuffer(GLEnum.Framebuffer, 0);

            FlipRgbRows(rgbPixels, (int)w, (int)h);
            return true;
        }
        finally
        {
            OverlaysEnabled = previousOverlays;
        }
    }

    private unsafe void EnsureShadowResources()
    {
        if (Gl == null || _shadowShader != null)
            return;

        _shadowShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
        _shadowShader.CompileShader("shadow_depth.vert", "shadow_depth.frag");

        Gl.GenFramebuffers(1, out _shadowFbo);
        Gl.GenTextures(1, out _shadowTex);
        Gl.BindTexture(GLEnum.Texture2D, _shadowTex);
        Gl.TexImage2D(
            GLEnum.Texture2D,
            0,
            InternalFormat.DepthComponent32f,
            _shadowMapSize,
            _shadowMapSize,
            0,
            PixelFormat.DepthComponent,
            GLEnum.Float,
            null);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        float[] borderColor = [1f, 1f, 1f, 1f];
        fixed (float* borderPtr = borderColor)
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureBorderColor, borderPtr);

        Gl.BindFramebuffer(GLEnum.Framebuffer, _shadowFbo);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.DepthAttachment, GLEnum.Texture2D, _shadowTex, 0);
        Gl.DrawBuffer(GLEnum.None);
        Gl.ReadBuffer(GLEnum.None);
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
    }

    private unsafe void EnsurePointShadowResources()
    {
        if (Gl == null || _pointShadowShader != null)
            return;

        _pointShadowShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
        _pointShadowShader.CompileShader("point_shadow_depth.vert", "point_shadow_depth.frag");

        Gl.GenFramebuffers(1, out _pointShadowFbo);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _pointShadowFbo);
        Gl.DrawBuffer(GLEnum.None);
        Gl.ReadBuffer(GLEnum.None);

        for (int i = 0; i < MaxPointShadowLights; i++)
        {
            Gl.GenTextures(1, out _pointShadowCubeTextures[i]);
            Gl.BindTexture(GLEnum.TextureCubeMap, _pointShadowCubeTextures[i]);
            for (int face = 0; face < 6; face++)
            {
                Gl.TexImage2D(
                    GLEnum.TextureCubeMapPositiveX + face,
                    0,
                    InternalFormat.DepthComponent32f,
                    PointShadowMapSize,
                    PointShadowMapSize,
                    0,
                    PixelFormat.DepthComponent,
                    GLEnum.Float,
                    null);
            }

            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        }

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.TextureCubeMap, 0);
    }

    private unsafe void RenderShadowMap()
    {
        if (Gl == null || MainViewport == null)
            return;

        EnsureShadowResources();
        if (_shadowShader == null || _shadowFbo == 0 || _shadowTex == 0)
            return;

        mat4 lightViewProj = ComputeShadowLightSpaceMatrix();
        Mesh.ShadowsEnabled = true;
        Mesh.ShadowMapTexture = _shadowTex;
        Mesh.ShadowLightSpaceMatrix = lightViewProj;

        Gl.BindFramebuffer(GLEnum.Framebuffer, _shadowFbo);
        Gl.Viewport(0, 0, _shadowMapSize, _shadowMapSize);
        Gl.Clear(ClearBufferMask.DepthBufferBit);
        Gl.Enable(GLEnum.DepthTest);
        Gl.Disable(GLEnum.CullFace);

        if (MainViewport.GroundPlane != null && MainViewport.GroundPlaneVisible)
            MainViewport.GroundPlane.RenderShadow(_shadowShader, lightViewProj, mat4.Identity);

        RenderShadowCasters(MainViewport.SceneObjects, lightViewProj);

        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        Gl.Viewport(0, 0, _width, _height);
    }

    private unsafe Dictionary<LightSceneObject, int> RenderPointShadowMaps()
    {
        Dictionary<LightSceneObject, int> shadowIndices = new();
        if (Gl == null || MainViewport == null)
            return shadowIndices;

        EnsurePointShadowResources();
        if (_pointShadowShader == null || _pointShadowFbo == 0)
            return shadowIndices;

        List<(LightSceneObject Light, vec3 Position, float Range)> shadowLights = [];
        CollectPointShadowCasters(MainViewport.SceneObjects, shadowLights);
        int lightCount = Math.Min(shadowLights.Count, MaxPointShadowLights);
        Array.Clear(Mesh.PointShadowCubeTextures, 0, Mesh.PointShadowCubeTextures.Length);

        for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
        {
            var (light, position, range) = shadowLights[lightIndex];
            shadowIndices[light] = lightIndex;
            Mesh.PointShadowCubeTextures[lightIndex] = _pointShadowCubeTextures[lightIndex];

            float farPlane = Math.Max(range, 0.5f);
            mat4 projection = mat4.Perspective(GlmSharp.glm.Radians(90f), 1f, 0.05f, farPlane);

            for (int face = 0; face < 6; face++)
            {
                mat4 view = mat4.LookAt(position, position + PointShadowFaceDirections[face], PointShadowFaceUps[face]);
                mat4 lightViewProj = projection * view;

                Gl.BindFramebuffer(GLEnum.Framebuffer, _pointShadowFbo);
                Gl.FramebufferTexture2D(
                    GLEnum.Framebuffer,
                    GLEnum.DepthAttachment,
                    GLEnum.TextureCubeMapPositiveX + face,
                    _pointShadowCubeTextures[lightIndex],
                    0);
                Gl.Viewport(0, 0, PointShadowMapSize, PointShadowMapSize);
                Gl.Clear(ClearBufferMask.DepthBufferBit);
                Gl.Enable(GLEnum.DepthTest);
                Gl.Disable(GLEnum.CullFace);

                if (MainViewport.GroundPlane != null && MainViewport.GroundPlaneVisible)
                    MainViewport.GroundPlane.RenderPointShadow(_pointShadowShader, lightViewProj, mat4.Identity, position, farPlane);

                RenderPointShadowCasters(MainViewport.SceneObjects, lightViewProj, position, farPlane);
            }
        }

        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        Gl.Viewport(0, 0, _width, _height);
        return shadowIndices;
    }

    private mat4 ComputeShadowLightSpaceMatrix()
    {
        var bounds = new SceneShadowBounds();
        CollectShadowBounds(MainViewport?.SceneObjects ?? [], ref bounds);

        if (MainViewport?.GroundPlaneVisible == true)
        {
            bounds.Include(new vec3(-32f, 0f, -32f));
            bounds.Include(new vec3(32f, 0f, 32f));
        }

        if (!bounds.HasAny)
        {
            bounds.Include(vec3.Zero);
            bounds.Include(new vec3(8f, 8f, 8f));
        }

        vec3 center = (bounds.Min + bounds.Max) * 0.5f;
        vec3 extents = bounds.Max - bounds.Min;
        float radius = Math.Max(12f, Math.Max(extents.x, Math.Max(extents.y, extents.z)) * 0.8f + 8f);
        vec3 lightDir = new vec3(1f, 1f, 1f).Normalized;
        vec3 lightPos = center + lightDir * (radius * 1.8f);

        mat4 lightView = mat4.LookAt(lightPos, center, vec3.UnitY);
        vec3[] corners =
        [
            new(bounds.Min.x, bounds.Min.y, bounds.Min.z),
            new(bounds.Min.x, bounds.Min.y, bounds.Max.z),
            new(bounds.Min.x, bounds.Max.y, bounds.Min.z),
            new(bounds.Min.x, bounds.Max.y, bounds.Max.z),
            new(bounds.Max.x, bounds.Min.y, bounds.Min.z),
            new(bounds.Max.x, bounds.Min.y, bounds.Max.z),
            new(bounds.Max.x, bounds.Max.y, bounds.Min.z),
            new(bounds.Max.x, bounds.Max.y, bounds.Max.z)
        ];

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        float minDepth = float.PositiveInfinity;
        float maxDepth = float.NegativeInfinity;

        foreach (vec3 corner in corners)
        {
            vec4 lightSpace = lightView * new vec4(corner, 1f);
            minX = Math.Min(minX, lightSpace.x);
            maxX = Math.Max(maxX, lightSpace.x);
            minY = Math.Min(minY, lightSpace.y);
            maxY = Math.Max(maxY, lightSpace.y);

            float depth = -lightSpace.z;
            minDepth = Math.Min(minDepth, depth);
            maxDepth = Math.Max(maxDepth, depth);
        }

        const float xyPadding = 6f;
        const float zPadding = 12f;
        mat4 lightProj = mat4.Ortho(
            minX - xyPadding,
            maxX + xyPadding,
            minY - xyPadding,
            maxY + xyPadding,
            Math.Max(0.1f, minDepth - zPadding),
            Math.Max(minDepth - zPadding + 1f, maxDepth + zPadding));
        return lightProj * lightView;
    }

    private void RenderShadowCasters(IEnumerable<SceneObject> objects, mat4 lightViewProj)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility())
                continue;

            mat4 model = obj.GetWorldMatrix();
            foreach (var mesh in obj.Visuals)
            {
                if (mesh.PickOnly || mesh.DepthTestDisabled)
                    continue;

                mesh.RenderShadow(_shadowShader!, lightViewProj, model);
            }

            RenderShadowCasters(obj.Children, lightViewProj);
        }
    }

    private void RenderPointShadowCasters(IEnumerable<SceneObject> objects, mat4 lightViewProj, vec3 lightPos, float farPlane)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility())
                continue;

            mat4 model = obj.GetWorldMatrix();
            foreach (var mesh in obj.Visuals)
            {
                if (mesh.PickOnly || mesh.DepthTestDisabled)
                    continue;

                mesh.RenderPointShadow(_pointShadowShader!, lightViewProj, model, lightPos, farPlane);
            }

            RenderPointShadowCasters(obj.Children, lightViewProj, lightPos, farPlane);
        }
    }

    private static void CollectPointShadowCasters(IEnumerable<SceneObject> objects, List<(LightSceneObject Light, vec3 Position, float Range)> result)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility())
                continue;

            if (obj is LightSceneObject light && light.LightShadowEnabled)
            {
                mat4 world = obj.GetWorldMatrix();
                result.Add((light, new vec3(world.m30, world.m31, world.m32), Math.Max(light.LightRange, 0.5f)));
            }

            CollectPointShadowCasters(obj.Children, result);
        }
    }

    private static void CollectShadowBounds(IEnumerable<SceneObject> objects, ref SceneShadowBounds bounds)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility())
                continue;

            mat4 world = obj.GetWorldMatrix();
            bool includedMeshVertex = false;
            foreach (var mesh in obj.Visuals)
            {
                if (mesh.PickOnly || mesh.DepthTestDisabled || mesh.Vertices.Count == 0)
                    continue;

                includedMeshVertex = true;
                foreach (vec3 vertex in mesh.Vertices)
                {
                    vec4 worldVertex = world * new vec4(vertex, 1f);
                    bounds.Include(new vec3(worldVertex.x, worldVertex.y, worldVertex.z));
                }
            }

            if (!includedMeshVertex)
                bounds.Include(new vec3(world.m30, world.m31, world.m32));

            CollectShadowBounds(obj.Children, ref bounds);
        }
    }

    private struct SceneShadowBounds
    {
        public bool HasAny;
        public vec3 Min;
        public vec3 Max;

        public void Include(vec3 point)
        {
            if (!HasAny)
            {
                Min = point;
                Max = point;
                HasAny = true;
                return;
            }

            Min = new vec3(Math.Min(Min.x, point.x), Math.Min(Min.y, point.y), Math.Min(Min.z, point.z));
            Max = new vec3(Math.Max(Max.x, point.x), Math.Max(Max.y, point.y), Math.Max(Max.z, point.z));
        }
    }

    private static void FlipRgbRows(byte[] rgbPixels, int width, int height)
    {
        int stride = width * 3;
        byte[] row = new byte[stride];

        for (int y = 0; y < height / 2; y++)
        {
            int top = y * stride;
            int bottom = (height - 1 - y) * stride;

            System.Buffer.BlockCopy(rgbPixels, top, row, 0, stride);
            System.Buffer.BlockCopy(rgbPixels, bottom, rgbPixels, top, stride);
            System.Buffer.BlockCopy(row, 0, rgbPixels, bottom, stride);
        }
    }

    // ── Scene collection helpers ──────────────────────────────────────────────

    private static void CollectPointLights(IEnumerable<SceneObject> objects, Dictionary<LightSceneObject, int> shadowIndices)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility()) continue;
            if (obj is LightSceneObject light)
            {
                mat4 world = obj.GetWorldMatrix();
                var pos    = new vec3(world.m30, world.m31, world.m32);
                var col    = new vec3(light.LightColor.x, light.LightColor.y, light.LightColor.z);
                int shadowIndex = shadowIndices.TryGetValue(light, out int index) ? index : -1;
                Mesh.PointLights.Add((pos, col, light.LightRange, light.LightEnergy, shadowIndex));
            }
            CollectPointLights(obj.Children, shadowIndices);
        }
    }

    private static void CollectRenderPairs(
        IEnumerable<SceneObject> objects, vec3 camPos,
        List<(mat4, Mesh)> opaque,
        List<(mat4, Mesh, float)> textured,
        List<(mat4, Mesh, float)> alphaBlend,
        List<(mat4, Mesh)> overlays)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility()) continue;
            mat4  model = obj.GetWorldMatrix();
            vec3  wp    = new vec3(model.m30, model.m31, model.m32);
            float dist  = (wp - camPos).LengthSqr;
            foreach (var mesh in obj.Visuals)
            {
                if (mesh.PickOnly) { continue; }
                if (mesh.DepthTestDisabled)    { overlays.Add((model, mesh)); continue; }
                if (mesh.TextureId != 0)       textured.Add((model, mesh, dist));
                else if (mesh.Alpha < 1.0f)    alphaBlend.Add((model, mesh, dist));
                else                           opaque.Add((model, mesh));
            }
            CollectRenderPairs(obj.Children, camPos, opaque, textured, alphaBlend, overlays);
        }
    }

    // ── Free-fly (public so CameraWindow can call it) ─────────────────────────

    public unsafe void HandleFreeFlyPublic(Camera cam, CameraSceneObject? sceneObj, bool hovered)
    {
        if (sceneObj == null) return;

        var io = ImGui.GetIO();
        if (float.IsNaN(_lastMouseX)) { _lastMouseX = io.MousePos.X; _lastMouseY = io.MousePos.Y; }

        // Allow free-fly to continue even if the ImGui mouse position has drifted
        // outside the panel — the OS cursor is locked so it never actually moved.
        if (ImGui.IsMouseDown(ImGuiMouseButton.Right) && (hovered || _freeFly))
        {
            if (!_freeFly && GlfwApi != null)
                GlfwApi.SetInputMode(GlfwWindow, CursorStateAttribute.Cursor, CursorModeValue.CursorDisabled);

            // Tell ImGui this window owns the mouse while free-fly is active.
            if (_freeFly)
                ImGui.SetNextFrameWantCaptureMouse(true);

            if (_freeFly)
            {
                cam.Look(io.MouseDelta.X * FreeFlyLookSensitivity, -io.MouseDelta.Y * FreeFlyLookSensitivity);

                float dt    = io.DeltaTime;
                float speed = _freeFlySpeed * cam.Distance * 0.2f;
                if (ImGui.IsKeyDown(ImGuiKey.Space))         speed *= 2.5f;
                else if (ImGui.IsKeyDown(ImGuiKey.ModShift)) speed *= 0.4f;

                float fwd = 0f, rt = 0f, up = 0f;
                if (ImGui.IsKeyDown(ImGuiKey.W)) fwd += speed * dt;
                if (ImGui.IsKeyDown(ImGuiKey.S)) fwd -= speed * dt;
                if (ImGui.IsKeyDown(ImGuiKey.D)) rt  += speed * dt;
                if (ImGui.IsKeyDown(ImGuiKey.A)) rt  -= speed * dt;
                if (ImGui.IsKeyDown(ImGuiKey.E)) up  += speed * dt;
                if (ImGui.IsKeyDown(ImGuiKey.Q)) up  -= speed * dt;
                if (fwd != 0f || rt != 0f || up != 0f) cam.MoveFreeFly(fwd, rt, up);

                if (io.MouseWheel != 0)
                {
                    float factor = io.MouseWheel > 0 ? 1.3f : 1f / 1.3f;
                    for (int i = 0; i < (int)MathF.Abs(io.MouseWheel); i++) _freeFlySpeed *= factor;
                    _freeFlySpeed = Math.Clamp(_freeFlySpeed, 0.1f, 500f);
                }
            }
            _freeFly = true;
        }
        else
        {
            if (_freeFly && GlfwApi != null)
                GlfwApi.SetInputMode(GlfwWindow, CursorStateAttribute.Cursor, CursorModeValue.CursorNormal);
            _freeFly = false;
        }

        _lastMouseX = io.MousePos.X;
        _lastMouseY = io.MousePos.Y;
        sceneObj.SyncTransformFromCamera();
    }
}
