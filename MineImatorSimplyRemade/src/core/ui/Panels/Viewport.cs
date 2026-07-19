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
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.window.windows;
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
    private enum SceneRenderMode
    {
        Unrendered,
        Rendered
    }

    // ── Scene ──────────────────────────────────────────────────────────────────

    public List<SceneObject> SceneObjects { get; } = new();

    // ── Panel references ───────────────────────────────────────────────────────

    /// <summary>
    /// Reference to the properties panel, used to read the background color
    /// for the viewport clear color.
    /// </summary>
    public PropertiesPanel? PropertiesPanel { get; set; }

    /// <summary>
    /// Reference to the preferences panel, used to apply theme colors to buttons.
    /// </summary>
    public PreferencesPanel? PreferencesPanel { get; set; }

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
    public bool GroundPlaneVisible { get; private set; } = true;
    public string GroundTileAtlas { get; private set; } = "block";
    public string GroundTileKey { get; private set; } = "grass_block_top";

    // ── Background image plane ───────────────────────────────────────────────

    private MineImatorSimplyRemade.core.mdl.Shader? _backgroundShader;
    private uint _backgroundVao;
    private uint _backgroundVbo;
    private uint _backgroundTexture;
    private int _backgroundImageWidth;
    private int _backgroundImageHeight;
    private int _backgroundRenderMode;
    private float _backgroundUserScale = 1f;
    private float _backgroundRotationRadians;
    private Vector2 _backgroundUserOffset = Vector2.Zero;
    private string _backgroundImagePath = "No image selected";

    // ── Camera ─────────────────────────────────────────────────────────────────

    public Camera Camera { get; } = new Camera();

    // ── Gizmo ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The 3D transform gizmo for moving/rotating/scaling selected objects.
    /// Created in <see cref="InitFramebuffer"/> once the GL context is ready.
    /// </summary>
    public Gizmo3D? Gizmo { get; private set; }

    // ── Input handling ─────────────────────────────────────────────────────────

    /// <summary>
    /// Centralized input handler for camera controls.
    /// Abstracts away ImGui dependencies to work with both main and undocked windows.
    /// </summary>
    private Input _input = new Input();
    private Input _previewInput = new Input();

    // ── Orbit drag state ───────────────────────────────────────────────────────

    private bool  _dragging;
    private bool  _panning;
    private bool  _gizmoDragging;
    /// <summary>
    /// Screen position where the left mouse button was pressed this drag sequence.
    /// Used to distinguish a click (no/minimal movement) from an orbit drag.
    /// </summary>

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
    public bool HighQualityPreviewEnabled { get; private set; } = false;
    public bool ShadowDebugEnabled { get; private set; } = false;

    // ── Secondary camera viewport ──────────────────────────────────────────────

    /// <summary>
    /// The secondary camera viewport panel (inline overlay + optional undocked window).
    /// Set by <see cref="MainWindow"/> after construction.
    /// </summary>
    public Viewport? PreviewViewport { get; set; }

    /// <summary>
    /// When true, the inline camera preview overlay is skipped.
    /// Useful when a fullscreen launcher/home screen is shown above the editor.
    /// </summary>
    public bool SuppressInlinePreviewViewport { get; set; } = false;

    // ── Preview viewport state ────────────────────────────────────────────────

    /// <summary>True if this viewport is a preview (secondary) viewport rather than the main editor viewport.</summary>
    public bool IsPreviewViewport { get; set; } = false;

    /// <summary>Reference to the main viewport for preview instances to access scene objects.</summary>
    public Viewport? MainViewport { get; set; }

    private int _selectedCameraIndex = 0;
    /// <summary>Public accessor for the selected camera index (0 = work camera, 1+ = spawned cameras).</summary>
    public int SelectedCameraIndex
    {
        get => _selectedCameraIndex;
        set => _selectedCameraIndex = value;
    }

    public Glfw? GlfwApiPreview { get; set; }
    public unsafe WindowHandle* GlfwWindowPreview { get; set; }

    private uint _previewFbo;
    private uint _previewColorTex;
    private uint _previewRbo;
    private uint _previewWidth = 1;
    private uint _previewHeight = 1;
    private uint _previewShadowFbo;
    private uint _previewShadowTex;
    private MineImatorSimplyRemade.core.mdl.Shader? _previewShadowShader;
    private uint _previewPointShadowFbo;
    private MineImatorSimplyRemade.core.mdl.Shader? _previewPointShadowShader;
    private readonly uint[] _previewPointShadowCubeTextures = new uint[MaxPointShadowLights];

    // Preview pick / silhouette FBOs. Mirrors the main viewport's resources so
    // the camera preview can run its own GPU colour-ID pick and Sobel outline
    // pass without disturbing the main viewport's framebuffers.
    private uint _previewPickFbo;
    private uint _previewPickColorTex;
    private uint _previewPickRbo;
    private uint _previewSilhouetteFbo;
    private uint _previewSilhouetteTex;

    // Screen-space mouse position captured on left-button release inside the
    // preview viewport so the colour-pick read-back can happen inside the
    // preview render (GL context active). NaN means no pick is pending.
    private float _pendingPreviewPickX = float.NaN;
    private float _pendingPreviewPickY = float.NaN;
    private bool  _pendingPreviewPickCtrl;

    public uint ColorTexture => _previewColorTex;

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

    private enum Corner { BottomRight, BottomLeft, TopRight, TopLeft }
    private Corner _corner = Corner.BottomRight;

    private Vector2 _inlineSize = new Vector2(340f, 220f);
    private const float InlineMinW = 160f;
    private const float InlineMinH = 120f;
    private const float InlinePad = 8f;
    private bool _inlineResizeDragActive;
    private bool _inlineResizeDragActiveLastFrame;
    private Vector2 _prevInlineWindowSize;
    private bool _hasPrevInlineWindowSize;

    /// <summary>
    /// Tracks whether the inline preview window is being manually dragged by the user.
    /// Used to suppress position forcing and camera input while dragging.
    /// </summary>
    private bool _inlineDragActive = false;
    private Vector2 _inlineWindowLastPos = Vector2.Zero;
    private bool _inlineMouseWasDownLastFrame = false;
    private Vector2 _inlineMouseDownPos = Vector2.Zero;
    private int _inlineFramesSinceMouseUp = 0;
    private Vector2 _inlineWindowPosBeforeSnap = Vector2.Zero;
    private bool _inlineSnappedThisInteraction = false;
    private Vector2 _prevImageMin = Vector2.Zero;
    private Vector2 _prevImageSize = Vector2.Zero;

    public bool InlineVisible { get; private set; } = true;

    public bool IsInlineVisible =>
        !Undocked &&
        InlineVisible &&
        !(MainViewport?.SuppressInlinePreviewViewport ?? false);

    public bool IsVisible => Undocked || IsInlineVisible;



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

    /// <summary>Camera-facing ring shader used for selected-light range indicators.</summary>
    private MineImatorSimplyRemade.core.mdl.Shader? _lightRingShader;

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

        SetGroundPlaneTexture("block", "grass_block_top");
    }

    public void SetGroundPlaneVisible(bool visible)
    {
        GroundPlaneVisible = visible;
    }

    public bool SetGroundPlaneTexture(string atlasKind, string tileKey)
    {
        if (_groundPlane == null)
            return false;

        string normalizedAtlas = string.Equals(atlasKind, "item", StringComparison.OrdinalIgnoreCase)
            ? "item"
            : "block";
        string normalizedKey = (tileKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedKey))
            return false;

        var atlas = normalizedAtlas == "item" ? ItemsAtlas.Textures : TerrainAtlas.Textures;
        if (!atlas.TryGetValue(normalizedKey, out uint tileId))
            return false;

        _groundPlane.TextureId = tileId;
        GroundTileAtlas = normalizedAtlas;
        GroundTileKey = normalizedKey;
        return true;
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

        // ── Background quad shader + geometry ───────────────────────────────
        InitBackgroundRenderer();

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

        // ── Export-quality shadow resources reused for viewport preview ───────
        EnsureShadowResources();
        EnsurePointShadowResources();

        // Apply persisted project background settings if available.
        if (PropertiesPanel != null)
            SetBackgroundImage(
                PropertiesPanel.BackgroundImagePath,
                PropertiesPanel.BackgroundRenderMode,
                PropertiesPanel.BackgroundScale,
                PropertiesPanel.BackgroundRotationDegrees,
                new Vector2(PropertiesPanel.BackgroundOffset[0], PropertiesPanel.BackgroundOffset[1]));
    }

    private unsafe void InitBackgroundRenderer()
    {
        _backgroundShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
        _backgroundShader.CompileShader("background.vert", "background.frag");

        float[] quadVertices =
        {
            // pos      // uv
            -1f, -1f,   0f, 0f,
             1f, -1f,   1f, 0f,
             1f,  1f,   1f, 1f,
            -1f, -1f,   0f, 0f,
             1f,  1f,   1f, 1f,
            -1f,  1f,   0f, 1f,
        };

        Gl.GenVertexArrays(1, out _backgroundVao);
        Gl.GenBuffers(1, out _backgroundVbo);

        Gl.BindVertexArray(_backgroundVao);
        Gl.BindBuffer(GLEnum.ArrayBuffer, _backgroundVbo);
        fixed (float* ptr = quadVertices)
            Gl.BufferData(GLEnum.ArrayBuffer, (uint)(quadVertices.Length * sizeof(float)), ptr, GLEnum.StaticDraw);

        const uint stride = 4 * sizeof(float);
        Gl.VertexAttribPointer(0, 2, GLEnum.Float, false, stride, 0);
        Gl.EnableVertexAttribArray(0);
        Gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, 2 * sizeof(float));
        Gl.EnableVertexAttribArray(1);

        Gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        Gl.BindVertexArray(0);
    }

    private static int ParseBackgroundRenderMode(string mode)
    {
        if (string.Equals(mode, "fit", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (string.Equals(mode, "original", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 0;
    }

    public void SetBackgroundImage(string imagePath, bool stretch)
    {
        SetBackgroundImage(imagePath, stretch ? "stretch" : "original", 1f, 0f, Vector2.Zero);
    }

    public void SetBackgroundImage(string imagePath, string renderMode, float userScale, float rotationDegrees, Vector2 userOffset)
    {
        _backgroundRenderMode = ParseBackgroundRenderMode(renderMode);
        _backgroundUserScale = Math.Clamp(userScale, 0.01f, 20f);
        _backgroundRotationRadians = rotationDegrees * (MathF.PI / 180f);
        _backgroundUserOffset = userOffset;

        string normalizedPath = string.IsNullOrWhiteSpace(imagePath) ? "No image selected" : imagePath.Trim();
        bool samePathLoaded = string.Equals(normalizedPath, _backgroundImagePath, StringComparison.OrdinalIgnoreCase) &&
                              _backgroundTexture != 0;
        if (samePathLoaded)
            return;

        _backgroundImagePath = normalizedPath;
        DisposeBackgroundTexture();

        if (string.Equals(_backgroundImagePath, "No image selected", StringComparison.OrdinalIgnoreCase))
            return;

        string resolvedPath = ResolveBackgroundImagePath(_backgroundImagePath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            return;

        TryLoadBackgroundTexture(resolvedPath);
    }

    private string ResolveBackgroundImagePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        if (ProjectManager.Instance.HasProject)
        {
            string insideProject = Path.Combine(ProjectManager.Instance.ProjectFolder, configuredPath);
            if (File.Exists(insideProject))
                return insideProject;

            string fileOnly = Path.GetFileName(configuredPath);
            if (!string.IsNullOrWhiteSpace(fileOnly))
            {
                string underImages = Path.Combine(ProjectManager.Instance.ImagesFolder, fileOnly);
                if (File.Exists(underImages))
                    return underImages;
            }
        }

        return configuredPath;
    }

    private unsafe void TryLoadBackgroundTexture(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            ImageResult img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            if (img.Data == null || img.Data.Length == 0)
                return;

            _backgroundImageWidth = img.Width;
            _backgroundImageHeight = img.Height;

            int rowBytes = img.Width * 4;
            byte[] flipped = new byte[img.Data.Length];
            for (int row = 0; row < img.Height; row++)
            {
                int srcRow = img.Height - 1 - row;
                System.Buffer.BlockCopy(img.Data, srcRow * rowBytes, flipped, row * rowBytes, rowBytes);
            }

            Gl.GenTextures(1, out _backgroundTexture);
            Gl.BindTexture(GLEnum.Texture2D, _backgroundTexture);
            fixed (byte* p = flipped)
            {
                Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8,
                    (uint)img.Width, (uint)img.Height, 0,
                    PixelFormat.Rgba, GLEnum.UnsignedByte, p);
            }

            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            Gl.BindTexture(GLEnum.Texture2D, 0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Viewport] Failed to load background image '{path}': {ex.Message}");
            DisposeBackgroundTexture();
        }
    }

    private void DisposeBackgroundTexture()
    {
        if (_backgroundTexture != 0)
        {
            Gl.DeleteTexture(_backgroundTexture);
            _backgroundTexture = 0;
        }

        _backgroundImageWidth = 0;
        _backgroundImageHeight = 0;
    }

    public void RenderBackgroundPlanePublic(uint viewportWidth, uint viewportHeight)
    {
        RenderBackgroundPlane(viewportWidth, viewportHeight);
    }

    private unsafe void RenderBackgroundPlane(uint viewportWidth, uint viewportHeight)
    {
        if (_backgroundShader == null || _backgroundTexture == 0 || _backgroundVao == 0)
            return;

        Gl.Disable(GLEnum.DepthTest);
        Gl.DepthMask(false);
        Gl.Disable(GLEnum.CullFace);
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);

        uint program = _backgroundShader.ShaderProgram;
        Gl.UseProgram(program);

        int texLoc = Gl.GetUniformLocation(program, "uBackgroundTex");
        if (texLoc >= 0) Gl.Uniform1(texLoc, 0);

        int modeLoc = Gl.GetUniformLocation(program, "uMode");
        if (modeLoc >= 0) Gl.Uniform1(modeLoc, _backgroundRenderMode);

        int userScaleLoc = Gl.GetUniformLocation(program, "uUserScale");
        if (userScaleLoc >= 0) Gl.Uniform1(userScaleLoc, _backgroundUserScale);

        int userRotationLoc = Gl.GetUniformLocation(program, "uUserRotationRadians");
        if (userRotationLoc >= 0) Gl.Uniform1(userRotationLoc, _backgroundRotationRadians);

        int userOffsetLoc = Gl.GetUniformLocation(program, "uUserOffset");
        if (userOffsetLoc >= 0) Gl.Uniform2(userOffsetLoc, _backgroundUserOffset.X, _backgroundUserOffset.Y);

        int viewportLoc = Gl.GetUniformLocation(program, "uViewportSize");
        if (viewportLoc >= 0) Gl.Uniform2(viewportLoc, (float)viewportWidth, (float)viewportHeight);

        int imageLoc = Gl.GetUniformLocation(program, "uImageSize");
        if (imageLoc >= 0) Gl.Uniform2(imageLoc, (float)_backgroundImageWidth, (float)_backgroundImageHeight);

        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, _backgroundTexture);
        Gl.BindVertexArray(_backgroundVao);
        Gl.DrawArrays(GLEnum.Triangles, 0, 6);

        Gl.BindVertexArray(0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.Disable(GLEnum.Blend);

        Gl.DepthMask(true);
        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.FrontFace(GLEnum.Ccw);
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

        // Camera-facing ring shader for selected-light range indicators.
        _lightRingShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
        _lightRingShader.CompileShader("lightring.vert", "lightring.frag");

        // Shared procedural ring mesh for selected-light range indicators.
        LightSceneObject.EnsureRangeRingMesh(Gl);
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

    // ── Light range indicator render ──────────────────────────────────────────

    /// <summary>
    /// Draws a thin ring on the XZ plane around every selected
    /// <see cref="LightSceneObject"/>, scaled by the light's
    /// <c>LightRange</c>.  Uses the gizmo flat-colour shader (same as bone
    /// indicators) tinted with the light's own colour so each indicator is
    /// visually tied to its source.  Rendered with depth-test always + no depth
    /// writes so the range is always visible, even through walls.
    /// </summary>
    private unsafe void RenderLightRangeIndicators(mat4 view, mat4 proj)
    {
        if (_lightRingShader == null) return;
        if (LightSceneObject.SharedRangeRingMesh == null) return;

        var sm = SelectionManager.Instance;

        uint prog = _lightRingShader.ShaderProgram;
        Gl.UseProgram(prog);

        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Always);
        Gl.DepthMask(false);
        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.Blend);

        int viewLoc   = Gl.GetUniformLocation(prog, "uView");
        int projLoc   = Gl.GetUniformLocation(prog, "uProj");
        int posLoc    = Gl.GetUniformLocation(prog, "uWorldPos");
        int rangeLoc  = Gl.GetUniformLocation(prog, "uRange");
        int colorLoc  = Gl.GetUniformLocation(prog, "uColor");

        if (viewLoc >= 0)
        {
            float[] f =
            {
                view.m00, view.m01, view.m02, view.m03,
                view.m10, view.m11, view.m12, view.m13,
                view.m20, view.m21, view.m22, view.m23,
                view.m30, view.m31, view.m32, view.m33,
            };
            fixed (float* p = f) Gl.UniformMatrix4(viewLoc, 1, false, p);
        }
        if (projLoc >= 0)
        {
            float[] f =
            {
                proj.m00, proj.m01, proj.m02, proj.m03,
                proj.m10, proj.m11, proj.m12, proj.m13,
                proj.m20, proj.m21, proj.m22, proj.m23,
                proj.m30, proj.m31, proj.m32, proj.m33,
            };
            fixed (float* p = f) Gl.UniformMatrix4(projLoc, 1, false, p);
        }

        foreach (var root in SceneObjects)
            RenderLightRangeIndicatorsRecursive(root, view, proj, posLoc, rangeLoc, colorLoc, sm);

        Gl.DepthMask(true);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
    }

    private unsafe void RenderLightRangeIndicatorsRecursive(
        SceneObject obj, mat4 view, mat4 proj,
        int posLoc, int rangeLoc, int colorLoc, SelectionManager? sm)
    {
        if (!obj.GetEffectiveVisibility()) return;

        if (obj is LightSceneObject light && sm != null && sm.SelectedObjects.Contains(obj))
        {
            mat4 world    = obj.GetWorldMatrix();
            vec3 lightPos = new vec3(world.m30, world.m31, world.m32);

            if (posLoc   >= 0) Gl.Uniform3(posLoc, lightPos.x, lightPos.y, lightPos.z);
            if (rangeLoc >= 0) Gl.Uniform1(rangeLoc, MathF.Max(0.01f, light.LightRange / 2f));
            if (colorLoc >= 0) Gl.Uniform4(colorLoc,
                light.LightColor.x, light.LightColor.y, light.LightColor.z, 1f);

            LightSceneObject.SharedRangeRingMesh!.RenderPickPass(Gl);
        }

        foreach (var child in obj.Children)
            RenderLightRangeIndicatorsRecursive(child, view, proj, posLoc, rangeLoc, colorLoc, sm);
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

        // Restore state that bone indicators may have altered.
        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);

        RenderLightRangeIndicators(view, proj);
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

                // Tint the button to indicate the active state using accent color when enabled.
                if (!OverlaysEnabled)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.20f, 0.20f, 0.20f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.30f, 0.30f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.40f, 0.40f, 0.40f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.Text,          new Vector4(0.50f, 0.50f, 0.50f, 1.0f));
                }
                else
                {
                    var accentColor = GetAccentColorFromPreferences();
                    ImGui.PushStyleColor(ImGuiCol.Button,        accentColor with { W = 0.6f });
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, accentColor with { W = 0.8f });
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive,  accentColor);
                    ImGui.PushStyleColor(ImGuiCol.Text,          new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
                }

                if (ImGui.Button(OverlaysEnabled ? "Overlays" : "Overlays",
                        new Vector2(0, iconSize)))
                    OverlaysEnabled = !OverlaysEnabled;

                ImGui.PopStyleColor(4);
            }

            if (PreviewViewport != null)
            {
                bool previewVisible = PreviewViewport.IsVisible;

                ImGui.SameLine();
                ImGui.SetCursorPosY(itemY);

                if (!previewVisible)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.20f, 0.20f, 0.20f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.30f, 0.30f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.40f, 0.40f, 0.40f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.Text,          new Vector4(0.60f, 0.60f, 0.60f, 1.0f));
                }
                else
                {
                    var accentColor = GetAccentColorFromPreferences();
                    ImGui.PushStyleColor(ImGuiCol.Button,        accentColor with { W = 0.6f });
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, accentColor with { W = 0.8f });
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive,  accentColor);
                    ImGui.PushStyleColor(ImGuiCol.Text,          new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
                }

                if (ImGui.Button(previewVisible ? "Hide Preview" : "Show Preview", new Vector2(0, iconSize)))
                    PreviewViewport.ToggleInlineVisibility();

                ImGui.PopStyleColor(4);
            }

            bool previewF5Target = PreviewViewport?.IsVisible ?? false;
            bool f5Rendered = previewF5Target
                ? PreviewViewport!.HighQualityPreviewEnabled
                : HighQualityPreviewEnabled;
            string f5TargetLabel = previewF5Target ? "Preview" : "Main";

            ImGui.SameLine();
            ImGui.SetCursorPosY(itemY + 4f);
            ImGui.TextDisabled(f5Rendered ? $"Rendered {f5TargetLabel} (F5)" : $"Unrendered {f5TargetLabel} (F5)");

            ImGui.SameLine();
            ImGui.SetCursorPosY(itemY + 4f);
            ImGui.TextDisabled(ShadowDebugEnabled ? "Shadow Debug (F6)" : "Shadow Debug Off (F6)");

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
        HandleCameraInput(imageMin, imageSize);

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

        RenderBackgroundPlane(w, h);

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

        // ── Per-frame mesh globals ─────────────────────────────────────────────
        Mesh.DeltaTime = ImGui.GetIO().DeltaTime;
        bool timelinePlaying = Timeline.Instance?.IsPlaying ?? false;
        Mesh.AdvanceAnimatedTextures = timelinePlaying || MainWindow.IsAnimationRenderExportActive;
        int textureAnimFps = Math.Clamp(PropertiesPanel?.TextureAnimationFps ?? 20, 1, 240);
        Mesh.AnimatedTextureSpeedScale = textureAnimFps / 20.0;
        Mesh.ShadowsEnabled = false;
        Mesh.ShadowMapTexture = 0;
        Mesh.ShadowLightSpaceMatrix = mat4.Identity;
        Mesh.ShadowDebugMode = 0;
        Mesh.DirectionalShadowEnabled = PropertiesPanel?.FillLightCastsShadows ?? true;
        Array.Clear(Mesh.PointShadowCubeTextures, 0, Mesh.PointShadowCubeTextures.Length);

        // Rebuild the static light list used by Mesh.Render() every frame so
        // that moved / deleted lights are always up-to-date.
        Mesh.PointLights.Clear();
        Dictionary<LightSceneObject, int> pointShadowIndices = new();

        SceneRenderMode renderMode = HighQualityPreviewEnabled ? SceneRenderMode.Rendered : SceneRenderMode.Unrendered;
        if (renderMode == SceneRenderMode.Rendered)
        {
            Mesh.ShadowDebugMode = ShadowDebugEnabled ? 1 : 0;
            if (Mesh.DirectionalShadowEnabled)
                RenderShadowMap();
            pointShadowIndices = RenderPointShadowMaps();
        }

        CollectPointLights(SceneObjects, pointShadowIndices);

        // ── Ground plane ──────────────────────────────────────────────────────
        if (_groundPlane != null && GroundPlaneVisible)
            _groundPlane.Render(mat4.Identity, view, proj);

        // ── Scene objects ─────────────────────────────────────────────────────
        // Split meshes into three buckets:
        //   opaque       – Alpha == 1, no texture  → normal depth read+write
        //   textured     – has a TextureId          → depth pre-pass + color pass (LEQUAL)
        //   alphaBlend   – Alpha < 1, no texture    → straight back-to-front blend, no pre-pass
        var opaquePairs    = new List<(mat4 model, Mesh mesh)>();
        var texturedPairs  = new List<(mat4 model, Mesh mesh, float dist, int sortDepth)>();
        var alphaBlendPairs = new List<(mat4 model, Mesh mesh, float dist, int sortDepth)>();
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
            texturedPairs.Sort((a, b) =>
            {
                int byDist = b.dist.CompareTo(a.dist);
                return byDist != 0 ? byDist : a.sortDepth.CompareTo(b.sortDepth);
            });

            Gl.ColorMask(false, false, false, false);
            foreach (var (model, mesh, _, _) in texturedPairs)
                mesh.Render(model, view, proj);
            Gl.ColorMask(true, true, true, true);

            Gl.DepthFunc(GLEnum.Lequal);
            Gl.Enable(GLEnum.Blend);
            Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
            Gl.DepthMask(false);

            foreach (var (model, mesh, _, _) in texturedPairs)
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
            alphaBlendPairs.Sort((a, b) =>
            {
                int byDist = b.dist.CompareTo(a.dist);
                return byDist != 0 ? byDist : a.sortDepth.CompareTo(b.sortDepth);
            });

            Gl.Enable(GLEnum.Blend);
            Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
            Gl.DepthMask(false);

            foreach (var (model, mesh, _, _) in alphaBlendPairs)
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

            // ── Selected-light range rings ───────────────────────────────────────
            // Drawn after bone indicators so they stack on top consistently.
            Gl.Enable(GLEnum.DepthTest);
            Gl.DepthFunc(GLEnum.Less);
            Gl.Enable(GLEnum.CullFace);
            Gl.CullFace(GLEnum.Back);
            RenderLightRangeIndicators(view, proj);

            // ── Selection highlight: Sobel edge detection ─────────────────────────
            // Pass 1: stamp flat-white silhouette mask into _silhouetteFbo.
            //         No depth test → X-ray: occluded parts still contribute.
            RenderSilhouettePass(view, proj);
            // Pass 2: Sobel filter over the mask; composite edges onto _fbo.
            //         Restores depth/cull/FBO state before returning.
            RenderEdgePass();

            // ── Gizmo 3D ──────────────────────────────────────────────────────────
            Gizmo?.Render(Camera, view, proj, imageMin, size);
        }

        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.DepthTest);
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Mesh.ShadowsEnabled = false;
        Mesh.ShadowMapTexture = 0;
        Mesh.ShadowLightSpaceMatrix = mat4.Identity;
        Mesh.ShadowDebugMode = 0;
        Array.Clear(Mesh.PointShadowCubeTextures, 0, Mesh.PointShadowCubeTextures.Length);

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

        // ── Inline preview viewport (bottom-right overlay) ──────────────────────
        if (PreviewViewport?.IsInlineVisible == true)
        {
            var spawnedCams = PreviewViewport.GetSpawnedCamerasPublic();
            PreviewViewport.RenderInline(imageMin, imageSize, spawnedCams);
        }

        // Draw after other viewport UI so the fly-speed badge stays readable.
        RenderFreeFlySpeedOverlay(imageMin, imageSize);

        ImGui.EndChild();
        ImGui.End();
    }

    private void RenderFreeFlySpeedOverlay(Vector2 imageMin, Vector2 imageSize)
    {
        if (!_freeFly) return;
        if (imageSize.X < 1f || imageSize.Y < 1f) return;

        string label = $"Fly speed x{_freeFlySpeed:0.##}";
        Vector2 textSize = ImGui.CalcTextSize(label);

        const float margin = 10f;
        const float padX = 10f;
        const float padY = 6f;

        Vector2 boxMin = new Vector2(
            imageMin.X + imageSize.X - textSize.X - (padX * 2f) - margin,
            imageMin.Y + margin);
        Vector2 boxMax = new Vector2(
            boxMin.X + textSize.X + (padX * 2f),
            boxMin.Y + textSize.Y + (padY * 2f));
        Vector2 textPos = new Vector2(boxMin.X + padX, boxMin.Y + padY);

        var fg = ImGui.GetForegroundDrawList();
        uint bgColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.05f, 0.05f, 0.86f));
        uint borderColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.78f, 0.22f, 0.95f));
        uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));

        fg.AddRectFilled(boxMin, boxMax, bgColor, 6f);
        fg.AddRect(boxMin, boxMax, borderColor, 6f);
        fg.AddText(textPos, textColor, label);
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

    private unsafe void EnsurePreviewShadowResources()
    {
        if (Gl == null || !IsPreviewViewport || _previewShadowShader != null)
            return;

        _previewShadowShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
        _previewShadowShader.CompileShader("shadow_depth.vert", "shadow_depth.frag");

        Gl.GenFramebuffers(1, out _previewShadowFbo);
        Gl.GenTextures(1, out _previewShadowTex);
        Gl.BindTexture(GLEnum.Texture2D, _previewShadowTex);
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

        Gl.BindFramebuffer(GLEnum.Framebuffer, _previewShadowFbo);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.DepthAttachment, GLEnum.Texture2D, _previewShadowTex, 0);
        Gl.DrawBuffer(GLEnum.None);
        Gl.ReadBuffer(GLEnum.None);
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
    }

    private unsafe void EnsurePreviewPointShadowResources()
    {
        if (Gl == null || !IsPreviewViewport || _previewPointShadowShader != null)
            return;

        _previewPointShadowShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
        _previewPointShadowShader.CompileShader("point_shadow_depth.vert", "point_shadow_depth.frag");

        Gl.GenFramebuffers(1, out _previewPointShadowFbo);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _previewPointShadowFbo);
        Gl.DrawBuffer(GLEnum.None);
        Gl.ReadBuffer(GLEnum.None);

        for (int i = 0; i < MaxPointShadowLights; i++)
        {
            Gl.GenTextures(1, out _previewPointShadowCubeTextures[i]);
            Gl.BindTexture(GLEnum.TextureCubeMap, _previewPointShadowCubeTextures[i]);
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
        if (Gl == null)
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

        if (_groundPlane != null && GroundPlaneVisible)
            _groundPlane.RenderShadow(_shadowShader, lightViewProj, mat4.Identity);

        RenderShadowCasters(SceneObjects, lightViewProj, _shadowShader!);

        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        Gl.Viewport(0, 0, _viewportWidth, _viewportHeight);
    }

    private unsafe Dictionary<LightSceneObject, int> RenderPointShadowMaps()
    {
        Dictionary<LightSceneObject, int> shadowIndices = new();
        if (Gl == null)
            return shadowIndices;

        EnsurePointShadowResources();
        if (_pointShadowShader == null || _pointShadowFbo == 0)
            return shadowIndices;

        List<(LightSceneObject Light, vec3 Position, float Range)> shadowLights = [];
        CollectPointShadowCasters(SceneObjects, shadowLights);
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

                if (_groundPlane != null && GroundPlaneVisible)
                    _groundPlane.RenderPointShadow(_pointShadowShader, lightViewProj, mat4.Identity, position, farPlane);

                RenderPointShadowCasters(SceneObjects, lightViewProj, position, farPlane, _pointShadowShader!);
            }
        }

        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        Gl.Viewport(0, 0, _viewportWidth, _viewportHeight);
        return shadowIndices;
    }

    private mat4 ComputeShadowLightSpaceMatrix()
    {
        var bounds = new SceneShadowBounds();
        CollectShadowBounds(SceneObjects, ref bounds);

        if (GroundPlaneVisible)
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

    private void RenderShadowCasters(IEnumerable<SceneObject> objects, mat4 lightViewProj, MineImatorSimplyRemade.core.mdl.Shader shadowShader)
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

                mesh.RenderShadow(shadowShader, lightViewProj, model);
            }

            RenderShadowCasters(obj.Children, lightViewProj, shadowShader);
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

    private void RenderPointShadowCasters(IEnumerable<SceneObject> objects, mat4 lightViewProj, vec3 lightPos, float farPlane, MineImatorSimplyRemade.core.mdl.Shader pointShadowShader)
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

                mesh.RenderPointShadow(pointShadowShader, lightViewProj, model, lightPos, farPlane);
            }

            RenderPointShadowCasters(obj.Children, lightViewProj, lightPos, farPlane, pointShadowShader);
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

    /// <summary>
    /// Public method for preview viewport to render directional shadow map.
    /// Renders shadows into the preview viewport's own shadow framebuffer.
    /// </summary>
    public unsafe void RenderShadowMapPublic()
    {
        if (Gl == null || !IsPreviewViewport)
            return;

        EnsurePreviewShadowResources();
        if (_previewShadowShader == null || _previewShadowFbo == 0 || _previewShadowTex == 0)
            return;

        mat4 lightViewProj = ComputeShadowLightSpaceMatrix();
        Mesh.ShadowsEnabled = true;
        Mesh.ShadowMapTexture = _previewShadowTex;
        Mesh.ShadowLightSpaceMatrix = lightViewProj;

        Gl.BindFramebuffer(GLEnum.Framebuffer, _previewShadowFbo);
        Gl.Viewport(0, 0, _shadowMapSize, _shadowMapSize);
        Gl.Clear(ClearBufferMask.DepthBufferBit);
        Gl.Enable(GLEnum.DepthTest);
        Gl.Disable(GLEnum.CullFace);

        if (MainViewport?.GroundPlane != null && MainViewport.GroundPlaneVisible)
            MainViewport.GroundPlane.RenderShadow(_previewShadowShader, lightViewProj, mat4.Identity);

        RenderShadowCasters(MainViewport?.SceneObjects ?? [], lightViewProj, _previewShadowShader!);


        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _previewFbo);
        Gl.Viewport(0, 0, _previewWidth, _previewHeight);
    }

    /// <summary>
    /// Public method for preview viewport to render point shadow maps.
    /// Renders shadows into the preview viewport's own point shadow framebuffer.
    /// </summary>
    public unsafe Dictionary<LightSceneObject, int> RenderPointShadowMapsPublic()
    {
        Dictionary<LightSceneObject, int> shadowIndices = new();
        if (Gl == null || !IsPreviewViewport)
            return shadowIndices;

        EnsurePreviewPointShadowResources();
        if (_previewPointShadowShader == null || _previewPointShadowFbo == 0)
            return shadowIndices;

        List<(LightSceneObject Light, vec3 Position, float Range)> shadowLights = [];
        CollectPointShadowCasters(MainViewport?.SceneObjects ?? [], shadowLights);
        int lightCount = Math.Min(shadowLights.Count, MaxPointShadowLights);
        Array.Clear(Mesh.PointShadowCubeTextures, 0, Mesh.PointShadowCubeTextures.Length);

        for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
        {
            var (light, position, range) = shadowLights[lightIndex];
            shadowIndices[light] = lightIndex;
            Mesh.PointShadowCubeTextures[lightIndex] = _previewPointShadowCubeTextures[lightIndex];

            float farPlane = Math.Max(range, 0.5f);
            mat4 projection = mat4.Perspective(GlmSharp.glm.Radians(90f), 1f, 0.05f, farPlane);

            for (int face = 0; face < 6; face++)
            {
                mat4 view = mat4.LookAt(position, position + PointShadowFaceDirections[face], PointShadowFaceUps[face]);
                mat4 lightViewProj = projection * view;

                Gl.BindFramebuffer(GLEnum.Framebuffer, _previewPointShadowFbo);
                Gl.FramebufferTexture2D(
                    GLEnum.Framebuffer,
                    GLEnum.DepthAttachment,
                    GLEnum.TextureCubeMapPositiveX + face,
                    _previewPointShadowCubeTextures[lightIndex],
                    0);
                Gl.Viewport(0, 0, PointShadowMapSize, PointShadowMapSize);
                Gl.Clear(ClearBufferMask.DepthBufferBit);
                Gl.Enable(GLEnum.DepthTest);
                Gl.Disable(GLEnum.CullFace);

                if (MainViewport?.GroundPlane != null && MainViewport.GroundPlaneVisible)
                    MainViewport.GroundPlane.RenderPointShadow(_previewPointShadowShader, lightViewProj, mat4.Identity, position, farPlane);

                RenderPointShadowCasters(MainViewport?.SceneObjects ?? [], lightViewProj, position, farPlane, _previewPointShadowShader!);
            }
        }

        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _previewFbo);
        Gl.Viewport(0, 0, _previewWidth, _previewHeight);
        return shadowIndices;
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
        var (pickCamera, pickCamObj) = GetActiveRenderCamera();
        float pickSavedFovY = pickCamera.FovY, pickSavedNear = pickCamera.Near, pickSavedFar = pickCamera.Far;
        if (pickCamObj != null)
        {
            pickCamera.FovY = GlmSharp.glm.Radians(pickCamObj.Fov);
            pickCamera.Near = pickCamObj.Near;
            pickCamera.Far  = pickCamObj.Far;
        }
        float aspect = (_viewportHeight > 0) ? (float)_viewportWidth / _viewportHeight : 1f;
        mat4 view = pickCamera.GetViewMatrix();
        mat4 proj = pickCamera.GetProjectionMatrix(aspect);
        pickCamera.FovY = pickSavedFovY; pickCamera.Near = pickSavedNear; pickCamera.Far = pickSavedFar;

        ExecutePendingPickGeneric(
            _pickFbo, _pickColorTex, _viewportWidth, _viewportHeight,
            view, proj, SceneObjects, imageMin, imageSize,
            _pendingPickX, _pendingPickY, _pendingPickCtrl);

        // Consume the main-viewport pending pick regardless of outcome.
        _pendingPickX = float.NaN;
        _pendingPickY = float.NaN;
    }

    /// <summary>
    /// Renders the pick pass into the supplied framebuffer, reads the pixel
    /// under the cursor, decodes the pick ID and updates the global
    /// <see cref="SelectionManager"/> accordingly.
    ///
    /// Shared by the main viewport and the preview viewport so both can select
    /// objects with a left-click.  The caller is responsible for supplying the
    /// correct view/projection matrices (matching whatever was used for the
    /// visible scene render) and the full <see cref="SceneObject"/> list.
    /// </summary>
    private unsafe void ExecutePendingPickGeneric(
        uint pickFbo,
        uint pickColorTex,
        uint w,
        uint h,
        mat4 view,
        mat4 proj,
        IEnumerable<SceneObject> sceneObjects,
        Vector2 imageMin,
        Vector2 imageSize,
        float screenX,
        float screenY,
        bool ctrlHeld)
    {
        if (_pickShader == null) return;
        if (pickFbo == 0 || pickColorTex == 0 || w == 0 || h == 0) return;

        // ── Render pick pass ─────────────────────────────────────────────────
        Gl.BindFramebuffer(GLEnum.Framebuffer, pickFbo);
        Gl.Viewport(0, 0, w, h);
        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.ClearColor(0f, 0f, 0f, 0f);
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        Gl.UseProgram(_pickShader.ShaderProgram);
        RenderPickObjects(sceneObjects, view, proj);

        // ── Read back the clicked pixel ───────────────────────────────────────
        // Map screen-space click coords into pick texture pixel coords.
        // imageMin / imageSize describe the sub-rect of the window where the
        // 3D image is displayed.  We clamp to valid texture bounds.
        float relX = (screenX - imageMin.X) / imageSize.X;
        float relY = (screenY - imageMin.Y) / imageSize.Y;

        // Guard: click outside the image rect → clear selection.
        if (relX < 0f || relX > 1f || relY < 0f || relY > 1f)
        {
            Gl.Disable(GLEnum.DepthTest);
            Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
            if (!ctrlHeld) SelectionManager.Instance?.ClearSelection();
            return;
        }

        int pixelX = (int)(relX * w);
        int pixelY = (int)((1f - relY) * h); // flip Y: OpenGL origin is bottom-left

        pixelX = Math.Clamp(pixelX, 0, (int)w - 1);
        pixelY = Math.Clamp(pixelY, 0, (int)h - 1);

        // Read a single pixel from the pick texture.
        byte[] pixel = new byte[4]; // RGBA
        fixed (byte* p = pixel)
        {
            Gl.ReadPixels(pixelX, pixelY, 1, 1, GLEnum.Rgba, GLEnum.UnsignedByte, p);
        }

        Gl.Disable(GLEnum.DepthTest);
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
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
        SceneObject? hit = FindObjectByPickId(sceneObjects, pickId);

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
        RenderSilhouettePassGeneric(_silhouetteFbo, _viewportWidth, _viewportHeight, view, proj);
    }

    /// <summary>
    /// Pass 1 of the Sobel outline effect.
    ///
    /// Renders every visible mesh belonging to a selected object into the
    /// supplied single-channel framebuffer as flat white (1.0).  The FBO has no
    /// depth attachment; <c>GL_ALWAYS</c> ensures occluded parts of the object
    /// still contribute to the mask, so the outline is drawn "through" other
    /// geometry.  Shared by the main and preview viewports.
    /// </summary>
    private unsafe void RenderSilhouettePassGeneric(uint silhouetteFbo, uint w, uint h, mat4 view, mat4 proj)
    {
        if (_silhouetteShader == null || silhouetteFbo == 0) return;

        var sm = SelectionManager.Instance;
        if (sm == null || sm.SelectedObjects.Count == 0) return;

        Gl.BindFramebuffer(GLEnum.Framebuffer, silhouetteFbo);
        Gl.Viewport(0, 0, w, h);

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
    /// Runs a full-screen Sobel gradient filter over the supplied silhouette
    /// mask and blends the resulting edge pixels on top of the supplied display
    /// framebuffer using <see cref="EdgeColor"/>.  Shared by the main and
    /// preview viewports.
    /// </summary>
    private unsafe void RenderEdgePass()
    {
        RenderEdgePassGeneric(_fbo, _silhouetteTex, _viewportWidth, _viewportHeight);
    }

    private unsafe void RenderEdgePassGeneric(uint displayFbo, uint silhouetteTex, uint w, uint h)
    {
        if (_edgeShader == null || displayFbo == 0 || silhouetteTex == 0) return;

        var sm = SelectionManager.Instance;
        if (sm == null || sm.SelectedObjects.Count == 0) return;

        // Draw into the supplied scene FBO.
        Gl.BindFramebuffer(GLEnum.Framebuffer, displayFbo);
        Gl.Viewport(0, 0, w, h);

        Gl.Disable(GLEnum.DepthTest);
        Gl.Disable(GLEnum.CullFace);

        // Additive-style alpha blend so the edge sits on top of the scene.
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);

        uint prog = _edgeShader.ShaderProgram;
        Gl.UseProgram(prog);

        // Bind the silhouette mask texture to unit 0.
        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, silhouetteTex);
        int maskLoc = Gl.GetUniformLocation(prog, "uMask");
        if (maskLoc >= 0) Gl.Uniform1(maskLoc, 0);

        // Texel size = 1 / viewport.  The shader uses this both for UV from
        // gl_FragCoord and for computing neighbour sample offsets.
        int texelLoc = Gl.GetUniformLocation(prog, "uTexelSize");
        if (texelLoc >= 0)
            Gl.Uniform2(texelLoc,
                        1f / w,
                        1f / h);

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

        // Restore depth/cull state so subsequent passes (e.g. the gizmo in the
        // main viewport) draw correctly.
        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
    }

    /// <summary>
    /// Recursively walks <paramref name="objects"/> and appends every visible
    /// <see cref="LightSceneObject"/> to <see cref="Mesh.PointLights"/>.
    /// </summary>
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

    /// <summary>
    /// Recursively collects (worldMatrix, mesh) pairs from every visible node in the
    /// scene hierarchy.  Each node uses its own <see cref="SceneObject.GetWorldMatrix"/>
    /// so that child nodes are correctly positioned relative to their parents.
    /// </summary>
    private static void CollectRenderPairs(
        IEnumerable<SceneObject> objects,
        vec3 camPos,
        List<(mat4 model, Mesh mesh)> opaque,
        List<(mat4 model, Mesh mesh, float dist, int sortDepth)> textured,
        List<(mat4 model, Mesh mesh, float dist, int sortDepth)> alphaBlend,
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
                if (mesh.PickOnly)
                    continue;

                // Overlay meshes (e.g. camera icon) are collected separately so
                // they can be shown/hidden with the Overlays toggle and are never
                // fed into the normal depth-sorted scene passes.
                if (mesh.DepthTestDisabled)
                {
                    overlays.Add((model, mesh));
                    continue;
                }

                if (mesh.TextureId != 0)
                    textured.Add((model, mesh, dist, mesh.SortDepth));
                else if (mesh.Alpha < 1.0f)
                    alphaBlend.Add((model, mesh, dist, mesh.SortDepth));
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
    /// Gets the accent color from the PreferencesPanel, or returns a default purple if not available.
    /// Used by buttons to match the active theme accent color.
    /// </summary>
    private Vector4 GetAccentColorFromPreferences()
    {
        if (PreferencesPanel == null)
            return new Vector4(0.8f, 0.3f, 1.0f, 1.0f); // default purple

        return PreferencesPanel.Accent switch
        {
            PreferencesPanel.AccentColor.Red => new Vector4(1.0f, 0.2f, 0.2f, 1.0f),
            PreferencesPanel.AccentColor.Orange => new Vector4(1.0f, 0.6f, 0.2f, 1.0f),
            PreferencesPanel.AccentColor.Yellow => new Vector4(1.0f, 1.0f, 0.2f, 1.0f),
            PreferencesPanel.AccentColor.Lime => new Vector4(0.7f, 1.0f, 0.2f, 1.0f),
            PreferencesPanel.AccentColor.Green => new Vector4(0.2f, 1.0f, 0.5f, 1.0f),
            PreferencesPanel.AccentColor.SkyBlue => new Vector4(0.4f, 0.8f, 1.0f, 1.0f),
            PreferencesPanel.AccentColor.Blue => new Vector4(0.3f, 0.5f, 1.0f, 1.0f),
            PreferencesPanel.AccentColor.Purple => new Vector4(0.8f, 0.3f, 1.0f, 1.0f),
            PreferencesPanel.AccentColor.Pink => new Vector4(1.0f, 0.4f, 0.7f, 1.0f),
            PreferencesPanel.AccentColor.Custom => new Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            _ => new Vector4(0.8f, 0.3f, 1.0f, 1.0f) // default purple
        };
    }

    // ── Preview viewport support ───────────────────────────────────────────────

    public void ToggleInlineVisibility()
    {
        if (IsPreviewViewport)
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
    }

    public unsafe void InitPreviewViewport(uint width, uint height)
    {
        if (!IsPreviewViewport || Gl == null) return;
        _previewWidth = width;
        _previewHeight = height;

        Gl.GenFramebuffers(1, out _previewFbo);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _previewFbo);

        Gl.GenTextures(1, out _previewColorTex);
        Gl.BindTexture(GLEnum.Texture2D, _previewColorTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, width, height, 0,
                      PixelFormat.Rgb, GLEnum.UnsignedByte, (void*)0);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0,
                                GLEnum.Texture2D, _previewColorTex, 0);

        Gl.GenRenderbuffers(1, out _previewRbo);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, _previewRbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, width, height);
        Gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthStencilAttachment,
                                   GLEnum.Renderbuffer, _previewRbo);

        var status = Gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.Error.WriteLine($"[Viewport] Preview framebuffer incomplete: {status}");

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);

        // ── Pick + silhouette FBOs ──────────────────────────────────────────
        // The preview viewport needs its own GPU picking resources so it can
        // resolve a left-click on the preview image to a scene object.
        EnsurePreviewPickFbo(width, height);
        EnsurePreviewSilhouetteFbo(width, height);

        // Shaders are shared conceptually with the main viewport; compile them
        // here too so the preview can run its own passes without depending on
        // the main viewport having initialised first.
        if (_pickShader == null)
        {
            _pickShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
            _pickShader.CompileShader("pick.vert", "pick.frag");
        }
        if (_silhouetteShader == null)
        {
            _silhouetteShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
            _silhouetteShader.CompileShader("pick.vert", "silhouette.frag");
        }
        if (_edgeShader == null)
        {
            _edgeShader = new MineImatorSimplyRemade.core.mdl.Shader(Gl);
            _edgeShader.CompileShader("edge.vert", "edge.frag");
            Gl.GenVertexArrays(1, out _edgeVao);
        }

        // Share the transform gizmo with the main viewport so it renders and
        // can be interacted with from the preview as well. Selection is
        // already global via SelectionManager, so both viewports drive the
        // same gizmo instance and share hover/drag state.
        if (MainViewport?.Gizmo != null)
            Gizmo = MainViewport.Gizmo;
    }

    private unsafe void EnsurePreviewPickFbo(uint width, uint height)
    {
        if (Gl == null || _previewPickFbo != 0) return;

        Gl.GenFramebuffers(1, out _previewPickFbo);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _previewPickFbo);

        Gl.GenTextures(1, out _previewPickColorTex);
        Gl.BindTexture(GLEnum.Texture2D, _previewPickColorTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, width, height, 0,
                      PixelFormat.Rgba, GLEnum.UnsignedByte, (void*)0);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0,
                                GLEnum.Texture2D, _previewPickColorTex, 0);

        Gl.GenRenderbuffers(1, out _previewPickRbo);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, _previewPickRbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.DepthComponent24, width, height);
        Gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthAttachment,
                                   GLEnum.Renderbuffer, _previewPickRbo);

        var status = Gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.Error.WriteLine($"[Viewport] Preview pick framebuffer incomplete: {status}");

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
    }

    private unsafe void EnsurePreviewSilhouetteFbo(uint width, uint height)
    {
        if (Gl == null || _previewSilhouetteFbo != 0) return;

        Gl.GenFramebuffers(1, out _previewSilhouetteFbo);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _previewSilhouetteFbo);

        Gl.GenTextures(1, out _previewSilhouetteTex);
        Gl.BindTexture(GLEnum.Texture2D, _previewSilhouetteTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.R8, width, height, 0,
                      PixelFormat.Red, GLEnum.UnsignedByte, (void*)0);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0,
                                GLEnum.Texture2D, _previewSilhouetteTex, 0);

        var status = Gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.Error.WriteLine($"[Viewport] Preview silhouette framebuffer incomplete: {status}");

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
    }

    public unsafe void ResizeFboPublic(uint w, uint h)
    {
        if (!IsPreviewViewport || Gl == null || (w == _previewWidth && h == _previewHeight)) return;
        _previewWidth = w;
        _previewHeight = h;

        Gl.BindTexture(GLEnum.Texture2D, _previewColorTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, w, h, 0,
                      PixelFormat.Rgb, GLEnum.UnsignedByte, (void*)0);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, _previewRbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, w, h);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);

        // Lazily create the pick / silhouette FBOs the first time we have a
        // valid size, then resize the attachments.
        EnsurePreviewPickFbo(w, h);
        EnsurePreviewSilhouetteFbo(w, h);

        if (_previewPickColorTex != 0)
        {
            Gl.BindTexture(GLEnum.Texture2D, _previewPickColorTex);
            Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, w, h, 0,
                          PixelFormat.Rgba, GLEnum.UnsignedByte, (void*)0);
            Gl.BindRenderbuffer(GLEnum.Renderbuffer, _previewPickRbo);
            Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.DepthComponent24, w, h);
            Gl.BindTexture(GLEnum.Texture2D, 0);
            Gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);
        }

        if (_previewSilhouetteTex != 0)
        {
            Gl.BindTexture(GLEnum.Texture2D, _previewSilhouetteTex);
            Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.R8, w, h, 0,
                          PixelFormat.Red, GLEnum.UnsignedByte, (void*)0);
            Gl.BindTexture(GLEnum.Texture2D, 0);
        }
    }

    public List<CameraSceneObject> GetSpawnedCamerasPublic()
    {
        var result = new List<CameraSceneObject>();
        var sourceSceneObjects = IsPreviewViewport ? (MainViewport?.SceneObjects ?? SceneObjects) : SceneObjects;
        CollectSpawnedCameras(sourceSceneObjects, result);
        return result;
    }

    public (Camera, CameraSceneObject?) DrawCameraDropdownInternal(List<CameraSceneObject> spawned)
    {
        if (_selectedCameraIndex > spawned.Count) _selectedCameraIndex = 0;
        return GetActiveCameraPreview(spawned);
    }

    public (Camera, CameraSceneObject?) DrawCameraDropdownPublic(List<CameraSceneObject> spawned)
        => DrawCameraDropdownInternal(spawned);

    private (Camera cam, CameraSceneObject? sceneObj) GetActiveCameraPreview(List<CameraSceneObject> spawned)
    {
        if (_selectedCameraIndex == 0)
            return (MainViewport?.Camera ?? Camera, null);

        int idx = _selectedCameraIndex - 1;
        if (idx >= 0 && idx < spawned.Count)
        {
            var camObj = spawned[idx];
            camObj.SyncCameraToTransform();
            return (camObj.ViewCamera, camObj);
        }
        _selectedCameraIndex = 0;
        return (MainViewport?.Camera ?? Camera, null);
    }

    public unsafe void RenderInline(Vector2 imageMin, Vector2 imageSize, List<CameraSceneObject> spawned)
    {
        if (!IsInlineVisible) return;

        _inlineSize.X = Math.Clamp(_inlineSize.X, InlineMinW, imageSize.X - InlinePad * 2);
        _inlineSize.Y = Math.Clamp(_inlineSize.Y, InlineMinH, imageSize.Y - InlinePad * 2);

        // ── Get current mouse and window state for position control ─────────────────
        var io = ImGui.GetIO();
        Vector2 mouse = io.MousePos;

        // Detect if bounds have changed (viewport was resized)
        bool boundsChanged = !_hasPrevInlineWindowSize || 
                             _prevImageMin != imageMin || 
                             _prevImageSize != imageSize;

        float posX = _corner switch
        {
            Corner.BottomLeft or Corner.TopLeft => imageMin.X + InlinePad,
            _ => imageMin.X + imageSize.X - _inlineSize.X - InlinePad,
        };
        float posY = _corner switch
        {
            Corner.TopLeft or Corner.TopRight => imageMin.Y + InlinePad,
            _ => imageMin.Y + imageSize.Y - _inlineSize.Y - InlinePad,
        };

        // Allow free dragging while mouse is down. After release, snap to the nearest corner once.
        bool shouldSnapToCorner = _inlineFramesSinceMouseUp > 2 && _inlineFramesSinceMouseUp < 60 && !_inlineSnappedThisInteraction;
        if (shouldSnapToCorner)
        {
            // Use the stored position from before snapping to calculate nearest corner
            float midX = imageMin.X + imageSize.X * 0.5f;
            float midY = imageMin.Y + imageSize.Y * 0.5f;
                
            bool isLeft = _inlineWindowPosBeforeSnap.X < midX;
            bool isTop = _inlineWindowPosBeforeSnap.Y < midY;
                
            Corner nearestCorner = (isTop, isLeft) switch
            {
                (true, true) => Corner.TopLeft,
                (true, false) => Corner.TopRight,
                (false, true) => Corner.BottomLeft,
                (false, false) => Corner.BottomRight,
            };
                
            posX = nearestCorner switch
            {
                Corner.BottomLeft or Corner.TopLeft => imageMin.X + InlinePad,
                _ => imageMin.X + imageSize.X - _inlineSize.X - InlinePad,
            };
            posY = nearestCorner switch
            {
                Corner.TopLeft or Corner.TopRight => imageMin.Y + InlinePad,
                _ => imageMin.Y + imageSize.Y - _inlineSize.Y - InlinePad,
            };
                
        ImGui.SetNextWindowPos(new Vector2(posX, posY), ImGuiCond.Always);
            _corner = nearestCorner;  // Update the corner tracking
            _inlineSnappedThisInteraction = true;
        }
        else if (boundsChanged && !_inlineDragActive && _inlineFramesSinceMouseUp > 0)
        {
            // Bounds changed (viewport resized) - reposition the window to stay in its current corner
            ImGui.SetNextWindowPos(new Vector2(posX, posY), ImGuiCond.Always);
        }
        
        ImGui.SetNextWindowSize(_inlineSize, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(InlineMinW, InlineMinH),
            new Vector2(imageSize.X - InlinePad * 2, imageSize.Y - InlinePad * 2));

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoDocking;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.08f, 0.90f));
        bool beginOk = ImGui.Begin("Camera View##CamViewInline", flags);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);

        if (!beginOk) { ImGui.End(); return; }

        _inlineSize = ImGui.GetWindowSize();
        
        // Store the window's actual position BEFORE we snap it, so next frame we can calculate
        // the nearest corner based on where the user actually dragged it to
        if (_inlineFramesSinceMouseUp == 0)
        {
            // Just released the mouse - store the position for nearest-corner calculation
            _inlineWindowPosBeforeSnap = ImGui.GetWindowPos();
        }

        Vector2 winPos = ImGui.GetWindowPos();
        Vector2 winSz = _inlineSize;
        bool leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
        
        // ── Track mouse interaction state for camera input suppression ─────────────
        bool mouseJustPressed = leftDown && !_inlineMouseWasDownLastFrame;
        if (leftDown && hovered)
        {
            if (mouseJustPressed)
            {
                _inlineMouseDownPos = mouse;
                _inlineSnappedThisInteraction = false;  // Reset snap flag for new drag
            }
            
            _inlineFramesSinceMouseUp = 0;
        }
        else if (!leftDown)
        {
            // Mouse was just released - end drag mode
            if (_inlineMouseWasDownLastFrame)
            {
                _inlineDragActive = false;
                _inlineFramesSinceMouseUp = 0;
            }
            else
            {
                _inlineFramesSinceMouseUp++;
            }
        }
        
        _inlineMouseWasDownLastFrame = leftDown && hovered;
        _inlineWindowLastPos = winPos;

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

        // When resize ends, reset snap flags to allow snapping to nearest corner
        if (_inlineResizeDragActiveLastFrame && !_inlineResizeDragActive)
        {
            _inlineSnappedThisInteraction = false;
            _inlineFramesSinceMouseUp = 0;
        }

        _inlineResizeDragActiveLastFrame = _inlineResizeDragActive;
        _prevInlineWindowSize = winSz;
        _hasPrevInlineWindowSize = true;

        // Store current bounds for next frame to detect viewport resizing
        _prevImageMin = imageMin;
        _prevImageSize = imageSize;

        var (activeCam, sceneObj) = DrawCameraDropdown(spawned);

        ImGui.SameLine();
        if (ImGui.Button("Pop"))
        {
            Undocked = true;
            PopRequested?.Invoke();
            ImGui.End();
            return;
        }

        ImGui.SameLine();
        {
            if (!OverlaysEnabled)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.20f, 0.20f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.30f, 0.30f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.40f, 0.40f, 0.40f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.50f, 0.50f, 0.50f, 1.0f));
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.45f, 0.25f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.55f, 0.30f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.35f, 0.60f, 0.35f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.90f, 0.95f, 0.90f, 1.0f));
            }

            if (ImGui.Button("Overlays"))
                OverlaysEnabled = !OverlaysEnabled;

            ImGui.PopStyleColor(4);
        }

        ImGui.SameLine();
        DrawCornerPicker();

        var avail = ImGui.GetContentRegionAvail();
        if (avail.X >= 4 && avail.Y >= 4)
        {
            Vector2 drawSize = GetPreviewDrawSize(avail);
            uint w = (uint)Math.Max(1, (int)MathF.Round(drawSize.X));
            uint h = (uint)Math.Max(1, (int)MathF.Round(drawSize.Y));
            ResizeFboPublic(w, h);
            RenderScenePublic(activeCam, sceneObj, w, h);

            Vector2 startPos = ImGui.GetCursorPos();
            if (avail.X > drawSize.X)
                ImGui.SetCursorPosX(startPos.X + (avail.X - drawSize.X) * 0.5f);
            if (avail.Y > drawSize.Y)
                ImGui.SetCursorPosY(startPos.Y + (avail.Y - drawSize.Y) * 0.5f);

            // Handle input AFTER cursor is positioned (so bounds are correct for centered image)
            bool allowCameraInput = ImGui.IsWindowHovered();
            var previewImageMin = ImGui.GetCursorScreenPos();
            var previewImageSize = drawSize;
            HandleFreeFlyPublic(activeCam, sceneObj, allowCameraInput, previewImageMin, previewImageSize);

            ImGui.Image(
                new ImTextureRef(texId: (ulong)_previewColorTex),
                drawSize,
                new Vector2(0, 1),
                new Vector2(1, 0));

            // Gizmo rotation-arc overlay (drawn on the ImGui draw list so it
            // sits on top of the rendered preview image, using the screen
            // rect of that image for correct screen-space projection).
            if (OverlaysEnabled)
                Gizmo?.RenderOverlay(activeCam, previewImageMin, previewImageSize);
        }

        ImGui.End();
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
        return GetActiveCameraPreview(spawned);
    }

    private void DrawCornerPicker()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));

        if (ImGui.Button(_corner == Corner.TopLeft ? "[TL]" : " TL ", new Vector2(28, 16)))
            _corner = Corner.TopLeft;
        ImGui.SameLine(0, 2);
        if (ImGui.Button(_corner == Corner.TopRight ? "[TR]" : " TR ", new Vector2(28, 16)))
            _corner = Corner.TopRight;
        ImGui.SameLine(0, 2);
        if (ImGui.Button(_corner == Corner.BottomLeft ? "[BL]" : " BL ", new Vector2(28, 16)))
            _corner = Corner.BottomLeft;
        ImGui.SameLine(0, 2);
        if (ImGui.Button(_corner == Corner.BottomRight ? "[BR]" : " BR ", new Vector2(28, 16)))
            _corner = Corner.BottomRight;

        ImGui.PopStyleVar();
    }

    public Vector2 GetPreviewDrawSize(Vector2 available)
    {
        float fallbackAspect = available.Y > 0f ? available.X / available.Y : (16f / 9f);
        float targetAspect = GetProjectPreviewAspect(fallbackAspect);
        return FitSizeToAspect(available, targetAspect);
    }

    private float GetProjectPreviewAspect(float fallbackAspect)
    {
        var mainVp = MainViewport;
        int width = mainVp?.PropertiesPanel?.GetResolutionWidth() ?? 0;
        int height = mainVp?.PropertiesPanel?.GetResolutionHeight() ?? 0;
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

    public unsafe void RenderScenePublic(Camera cam, CameraSceneObject? sceneObj, uint w, uint h, bool highQuality = false)
    {
        if (Gl == null || MainViewport == null) return;

        SceneRenderMode renderMode = (highQuality || HighQualityPreviewEnabled)
            ? SceneRenderMode.Rendered
            : SceneRenderMode.Unrendered;

        Gl.BindFramebuffer(GLEnum.Framebuffer, _previewFbo);
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
            cam.Far = sceneObj.Far;
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

        Mesh.PointLights.Clear();
        Dictionary<LightSceneObject, int> pointShadowIndices = new();

        if (renderMode == SceneRenderMode.Rendered)
        {
            Mesh.ShadowDebugMode = MainViewport.ShadowDebugEnabled ? 1 : 0;
            if (Mesh.DirectionalShadowEnabled)
                RenderShadowMapPublic();
            pointShadowIndices = RenderPointShadowMapsPublic();
        }

        CollectPointLights(MainViewport.SceneObjects, pointShadowIndices);

        if (MainViewport.GroundPlane != null && MainViewport.GroundPlaneVisible)
            MainViewport.GroundPlane.Render(mat4.Identity, view, proj);

        vec3 camPos = cam.Position;
        var opaque = new List<(mat4, Mesh)>();
        var textured = new List<(mat4, Mesh, float, int)>();
        var alphaBlend = new List<(mat4, Mesh, float, int)>();
        var overlays = new List<(mat4, Mesh)>();
        CollectRenderPairs(MainViewport.SceneObjects, camPos, opaque, textured, alphaBlend, overlays);

        foreach (var (model, mesh) in opaque)
            mesh.Render(model, view, proj);

        if (textured.Count > 0)
        {
            textured.Sort((a, b) =>
            {
                int byDist = b.Item3.CompareTo(a.Item3);
                return byDist != 0 ? byDist : a.Item4.CompareTo(b.Item4);
            });
            Gl.ColorMask(false, false, false, false);
            foreach (var (model, mesh, _, _) in textured) mesh.Render(model, view, proj);
            Gl.ColorMask(true, true, true, true);
            Gl.DepthFunc(GLEnum.Lequal);
            Gl.Enable(GLEnum.Blend);
            Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
            Gl.DepthMask(false);
            foreach (var (model, mesh, _, _) in textured) mesh.Render(model, view, proj);
            Gl.DepthMask(true);
            Gl.DepthFunc(GLEnum.Less);
            Gl.Disable(GLEnum.Blend);
        }

        if (alphaBlend.Count > 0)
        {
            alphaBlend.Sort((a, b) =>
            {
                int byDist = b.Item3.CompareTo(a.Item3);
                return byDist != 0 ? byDist : a.Item4.CompareTo(b.Item4);
            });
            Gl.Enable(GLEnum.Blend);
            Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
            Gl.DepthMask(false);
            foreach (var (model, mesh, _, _) in alphaBlend) mesh.Render(model, view, proj);
            Gl.DepthMask(true);
            Gl.Disable(GLEnum.Blend);
        }

        if (OverlaysEnabled)
        {
            foreach (var (model, mesh) in overlays)
                mesh.Render(model, view, proj);

            MainViewport.RenderOverlaysPublic(view, proj);

            // Selection outline (Sobel edge over a silhouette mask) so the
            // user can see which object is currently selected inside the
            // preview viewport.
            RenderSilhouettePassGeneric(_previewSilhouetteFbo, w, h, view, proj);
            // Composite the outline onto the preview framebuffer.
            Gl.BindFramebuffer(GLEnum.Framebuffer, _previewFbo);
            Gl.Viewport(0, 0, w, h);
            RenderEdgePassGeneric(_previewFbo, _previewSilhouetteTex, w, h);

            // Transform gizmo — uses the preview camera so the handles are
            // sized/perspective-correct relative to what the preview shows.
            Gizmo?.Render(cam, view, proj, Vector2.Zero, new Vector2(w, h));
        }

        // ── Pending pick (left-click) → GPU colour-ID pass ───────────────────
        if (!float.IsNaN(_pendingPreviewPickX))
        {
            ExecutePendingPickGeneric(
                _previewPickFbo, _previewPickColorTex, w, h,
                view, proj, MainViewport.SceneObjects,
                Vector2.Zero, new Vector2(w, h), // pick FBO is unscaled; coords already in FBO space
                _pendingPreviewPickX, _pendingPreviewPickY, _pendingPreviewPickCtrl);

            _pendingPreviewPickX = float.NaN;
            _pendingPreviewPickY = float.NaN;
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

    /// <summary>
    /// Core free-fly control logic (unified source of truth for all viewports).
    /// Handles mouse look, keyboard movement, speed adjustments, and cursor management.
    /// </summary>
    private unsafe void DoFreeFlyMovement(
        ref bool freeFlyActive,
        ref float freeFlySpeed,
        ref double lastMouseX,
        ref double lastMouseY,
        Camera camera,
        Glfw? glfw,
        WindowHandle* window,
        Vector2 imageMin,
        Vector2 imageSize)
    {
        var io = ImGui.GetIO();

        double glfwCursorX = 0, glfwCursorY = 0;
        if (glfw != null)
        {
            glfw.GetCursorPos(window, out glfwCursorX, out glfwCursorY);
        }

        bool mouseInViewportBounds = glfwCursorX >= imageMin.X && glfwCursorX <= imageMin.X + imageSize.X &&
                                     glfwCursorY >= imageMin.Y && glfwCursorY <= imageMin.Y + imageSize.Y;

        // Detect initial right-click within viewport bounds to enter free-fly mode
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && mouseInViewportBounds)
        {
            freeFlyActive = true;
            if (glfw != null)
            {
                glfw.SetInputMode(window, CursorStateAttribute.Cursor, CursorModeValue.CursorDisabled);
                // Seed last position on entry so we don't get a spurious camera jump on first frame
                glfw.GetCursorPos(window, out lastMouseX, out lastMouseY);
            }
        }

        // Continue free-fly logic while mouse button is held and free-fly is active
        if (freeFlyActive && ImGui.IsMouseDown(ImGuiMouseButton.Right) && glfw != null)
        {
            ImGui.SetNextFrameWantCaptureMouse(true);

            glfw.GetCursorPos(window, out double cursorX, out double cursorY);

            // Apply look (mouse rotation)
            if (!double.IsNaN(cursorX) && !double.IsNaN(cursorY) &&
                !double.IsInfinity(cursorX) && !double.IsInfinity(cursorY) &&
                !double.IsNaN(lastMouseX) && !double.IsNaN(lastMouseY))
            {
                float lookDx = (float)(cursorX - lastMouseX) * FreeFlyLookSensitivity;
                float lookDy = -(float)(cursorY - lastMouseY) * FreeFlyLookSensitivity;
                camera.Look(lookDx, lookDy);
            }

            // Recenter mouse to viewport center after each frame to prevent hitting edges
            // and to ensure the mouse always has room to move in any direction
            double centerX = imageMin.X + imageSize.X * 0.5;
            double centerY = imageMin.Y + imageSize.Y * 0.5;
            glfw.SetCursorPos(window, centerX, centerY);
            lastMouseX = centerX;
            lastMouseY = centerY;

            // Calculate movement speed
            float dt = io.DeltaTime;
            float speed = freeFlySpeed * camera.Distance * 0.2f;
            if (ImGui.IsKeyDown(ImGuiKey.Space)) speed *= 2.5f;
            else if (ImGui.IsKeyDown(ImGuiKey.ModShift)) speed *= 0.4f;

            // Apply keyboard movement (WASD + QE)
            float fwd = 0f, rt = 0f, up = 0f;
            if (ImGui.IsKeyDown(ImGuiKey.W)) fwd += speed * dt;
            if (ImGui.IsKeyDown(ImGuiKey.S)) fwd -= speed * dt;
            if (ImGui.IsKeyDown(ImGuiKey.D)) rt += speed * dt;
            if (ImGui.IsKeyDown(ImGuiKey.A)) rt -= speed * dt;
            if (ImGui.IsKeyDown(ImGuiKey.E)) up += speed * dt;
            if (ImGui.IsKeyDown(ImGuiKey.Q)) up -= speed * dt;
            if (fwd != 0f || rt != 0f || up != 0f) camera.MoveFreeFly(fwd, rt, up);

            // Handle scroll wheel for speed adjustment
            if (io.MouseWheel != 0)
            {
                float factor = io.MouseWheel > 0 ? 1.3f : 1f / 1.3f;
                for (int i = 0; i < (int)MathF.Abs(io.MouseWheel); i++) freeFlySpeed *= factor;
                freeFlySpeed = Math.Clamp(freeFlySpeed, 0.1f, 500f);
            }
        }
        else if (!ImGui.IsMouseDown(ImGuiMouseButton.Right) && freeFlyActive)
        {
            // Exit free-fly when mouse button is released
            if (glfw != null)
                glfw.SetInputMode(window, CursorStateAttribute.Cursor, CursorModeValue.CursorNormal);
            freeFlyActive = false;
            // Reset mouse position tracking on free-fly exit
            lastMouseX = double.NaN;
            lastMouseY = double.NaN;
        }
    }

    public unsafe void HandleFreeFlyPublic(Camera cam, CameraSceneObject? sceneObj, bool hovered, Vector2 imageMin, Vector2 imageSize)
    {
        // Allow input to continue while free-fly is active even if the ImGui
        // window hover state changes (the OS cursor is locked by GLFW so the real
        // cursor never moved — only the internal ImGui position drifts).
        // Also continue while an orbit/pan/gizmo drag is active so the mouse-
        // wrap at the viewport edge can fire and the action keeps going even
        // when the cursor has been dragged past the ImGui window boundary.
        if (!_previewInput.IsFreeFlyActive && !hovered &&
            !_previewInput.IsDragging && !_previewInput.IsPanning && !_previewInput.IsGizmoDragging) return;

        var io = ImGui.GetIO();
        Vector2 mousePos = new Vector2(io.MousePos.X, io.MousePos.Y);

        // Gather full input state for undocked window (same as main viewport)
        bool mouseDown_Left = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        bool mouseDown_Right = ImGui.IsMouseDown(ImGuiMouseButton.Right);
        bool mouseDown_Middle = ImGui.IsMouseDown(ImGuiMouseButton.Middle);
        bool mouseClicked_Left = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool mouseClicked_Right = ImGui.IsMouseClicked(ImGuiMouseButton.Right);
        bool mouseReleased_Left = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        bool mouseReleased_Right = ImGui.IsMouseReleased(ImGuiMouseButton.Right);
        float mouseWheel = io.MouseWheel;

        bool keyDown_W = ImGui.IsKeyDown(ImGuiKey.W);
        bool keyDown_S = ImGui.IsKeyDown(ImGuiKey.S);
        bool keyDown_A = ImGui.IsKeyDown(ImGuiKey.A);
        bool keyDown_D = ImGui.IsKeyDown(ImGuiKey.D);
        bool keyDown_E = ImGui.IsKeyDown(ImGuiKey.E);
        bool keyDown_Q = ImGui.IsKeyDown(ImGuiKey.Q);
        bool keyDown_Space = ImGui.IsKeyDown(ImGuiKey.Space);
        bool keyDown_Shift = ImGui.IsKeyDown(ImGuiKey.ModShift);

        bool keyPressed_G = ImGui.IsKeyPressed(ImGuiKey.G);
        bool keyPressed_R = ImGui.IsKeyPressed(ImGuiKey.R, false);

        // Process full input (orbit, pan, zoom, free-fly, gizmo) through the
        // centralized input handler. The gizmo is shared with the main viewport
        // (see InitPreviewViewport) so hover/drag works here as well.
        _previewInput.ProcessInput(
            cam,
            MainViewport?.Gizmo,
            imageMin,
            imageSize,
            GlfwApiPreview,
            GlfwWindowPreview,
            mousePos,
            mouseDown_Left,
            mouseDown_Right,
            mouseDown_Middle,
            mouseClicked_Left,
            mouseClicked_Right,
            mouseReleased_Left,
            mouseReleased_Right,
            mouseWheel,
            keyDown_W, keyDown_S, keyDown_A, keyDown_D,
            keyDown_E, keyDown_Q, keyDown_Space, keyDown_Shift,
            keyPressed_G,
            keyPressed_R,
            io.DeltaTime,
            hovered,
            io.KeyCtrl,
            OverlaysEnabled
        );

        // Sync camera transform only when a scene camera is active
        sceneObj?.SyncTransformFromCamera();

        // If the input handler queued a left-click pick, convert it from
        // screen-space into the preview FBO's pixel space and store it so the
        // next RenderScenePublic call can run the GPU colour-ID pick pass.
        // Y is intentionally NOT flipped here — ExecutePendingPickGeneric
        // handles the OpenGL bottom-left → ImGui top-left conversion itself.
        if (_previewInput.HasPendingPick)
        {
            var (pickX, pickY) = _previewInput.PendingPickPosition;
            float relX = (pickX - imageMin.X) / imageSize.X;
            float relY = (pickY - imageMin.Y) / imageSize.Y;
            int pixelX = (int)(relX * _previewWidth);
            int pixelY = (int)(relY * _previewHeight);

            _pendingPreviewPickX     = pixelX;
            _pendingPreviewPickY     = pixelY;
            _pendingPreviewPickCtrl  = _previewInput.PendingPickCtrl;
            _previewInput.ClearPendingPick();
        }
    }

    public unsafe bool CaptureCurrentViewRgb(uint w, uint h, bool highQuality, out byte[] rgbPixels)
    {
        rgbPixels = Array.Empty<byte>();
        if (Gl == null || MainViewport == null || _previewFbo == 0 || w == 0 || h == 0)
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

            Gl.BindFramebuffer(GLEnum.Framebuffer, _previewFbo);
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

    private unsafe void HandleCameraInput(Vector2 imageMin, Vector2 imageSize)
    {
        // Allow input to continue while free-fly is active even if the ImGui
        // mouse position has drifted outside the panel (the OS cursor is locked
        // by GLFW so the real cursor never moved — only the internal position).
        // Also continue while an orbit/pan/gizmo drag is active so the mouse-
        // wrap at the viewport edge can fire and the action keeps going even
        // when the cursor has been dragged past the ImGui window boundary.
        bool windowHovered = ImGui.IsWindowHovered();
        if (!_input.IsFreeFlyActive && !windowHovered &&
            !_input.IsDragging && !_input.IsPanning && !_input.IsGizmoDragging) return;

        var io = ImGui.GetIO();

        // While free-fly is active, tell ImGui this window owns the mouse so
        // other panels and widgets do not receive hover/click events.
        if (_input.IsFreeFlyActive)
            ImGui.SetNextFrameWantCaptureMouse(true);

        Vector2 mousePos = new Vector2(io.MousePos.X, io.MousePos.Y);

        // Gather input state
        bool mouseDown_Left = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        bool mouseDown_Right = ImGui.IsMouseDown(ImGuiMouseButton.Right);
        bool mouseDown_Middle = ImGui.IsMouseDown(ImGuiMouseButton.Middle);
        bool mouseClicked_Left = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool mouseClicked_Right = ImGui.IsMouseClicked(ImGuiMouseButton.Right);
        bool mouseReleased_Left = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        bool mouseReleased_Right = ImGui.IsMouseReleased(ImGuiMouseButton.Right);
        float mouseWheel = io.MouseWheel;

        bool keyDown_W = ImGui.IsKeyDown(ImGuiKey.W);
        bool keyDown_S = ImGui.IsKeyDown(ImGuiKey.S);
        bool keyDown_A = ImGui.IsKeyDown(ImGuiKey.A);
        bool keyDown_D = ImGui.IsKeyDown(ImGuiKey.D);
        bool keyDown_E = ImGui.IsKeyDown(ImGuiKey.E);
        bool keyDown_Q = ImGui.IsKeyDown(ImGuiKey.Q);
        bool keyDown_Space = ImGui.IsKeyDown(ImGuiKey.Space);
        bool keyDown_Shift = ImGui.IsKeyDown(ImGuiKey.ModShift);

        bool keyPressed_G = ImGui.IsKeyPressed(ImGuiKey.G);
        bool keyPressed_R = ImGui.IsKeyPressed(ImGuiKey.R, false);

        // Process input through the centralized input handler
        _input.ProcessInput(
            Camera,
            Gizmo,
            imageMin,
            imageSize,
            GlfwApi,
            GlfwWindow,
            mousePos,
            mouseDown_Left,
            mouseDown_Right,
            mouseDown_Middle,
            mouseClicked_Left,
            mouseClicked_Right,
            mouseReleased_Left,
            mouseReleased_Right,
            mouseWheel,
            keyDown_W, keyDown_S, keyDown_A, keyDown_D,
            keyDown_E, keyDown_Q, keyDown_Space, keyDown_Shift,
            keyPressed_G, keyPressed_R,
            io.DeltaTime,
            windowHovered,
            io.KeyCtrl,
            OverlaysEnabled
        );

        // Sync old fields for backward compatibility
        _freeFly = _input.IsFreeFlyActive;
        _freeFlySpeed = _input.FreeFlySpeed;
        _dragging = _input.IsDragging;
        _panning = _input.IsPanning;
        _gizmoDragging = _input.IsGizmoDragging;

        // Handle pending pick
        if (_input.HasPendingPick)
        {
            var (pickX, pickY) = _input.PendingPickPosition;
            _pendingPickX = pickX;
            _pendingPickY = pickY;
            _pendingPickCtrl = _input.PendingPickCtrl;
            _input.ClearPendingPick();
        }
    }
}
