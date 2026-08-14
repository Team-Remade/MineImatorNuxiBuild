using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using GlmSharp;
using Hexa.NET.ImGui;
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
using Buffer = System.Buffer;
using Shader = MineImatorSimplyRemade.core.mdl.Shader;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class Viewport : UiPanel
{
    private enum SceneRenderMode
    {
        Unrendered,
        Rendered
    }

    private enum ViewportRenderMode
    {
        Wireframe,
        FlatUnshaded,
        Shaded,
        Rendered
    }

    private enum RenderedPassMode
    {
        Combined,
        AmbientOcclusion,
        Shadow
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
    private CubeMesh? _particleEmissionWireCube;
    private LightRangeRingMesh? _particleEmissionWireRing;
    private mat4 _groundPlaneModel = mat4.Identity;
    public mat4 GroundPlaneModel => _groundPlaneModel;
    public bool GroundPlaneVisible { get; private set; } = true;
    public string GroundTileAtlas { get; private set; } = "block";
    public string GroundTileKey { get; private set; } = "grass_block_top";

    // ── Background image plane ───────────────────────────────────────────────

    private Shader? _backgroundShader;
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

    // ── Minecraft-style procedural sky ─────────────────────────────────────
    private Shader? _skyShader;
    private uint _sunTexture, _moonTexture, _cloudTexture;
    private readonly uint[] _moonPhaseTextures = new uint[8];
    private readonly string[] _loadedMoonPhasePaths = new string[8];
    private bool _moonTextureIsAtlas = true;
    private string _loadedSunPath = "", _loadedMoonPath = "", _loadedCloudPath = "";

    private const string LegacySunTexturePath = "minecraft:environment/sun.png";
    private const string ModernSunTexturePath = "minecraft:environment/celestial/sun.png";
    private const string LegacyMoonTexturePath = "minecraft:environment/moon_phases.png";
    private static readonly string[] ModernMoonPhaseFileNames =
    [
        // Preserve vanilla legacy moon phase order (indices 0..7).
        "full_moon.png",
        "waning_gibbous.png",
        "third_quarter.png",
        "waning_crescent.png",
        "new_moon.png",
        "waxing_crescent.png",
        "first_quarter.png",
        "waxing_gibbous.png"
    ];

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

    // ── Shared scene UBO ──────────────────────────────────────────────────────

    private SceneUniformBuffer? _sceneUBO;

    // ── Framebuffer ────────────────────────────────────────────────────────────

    private uint _fbo;
    private uint _colorTex;
    private uint _depthTex;
    private Shader? _ambientOcclusionShader;
    private Shader? _indirectLightingShader;
    private Shader? _indirectDenoiseShader;
    private Shader? _glowShader;
    private Shader? _filmGrainShader;
    private uint _indirectFbo;
    private uint _indirectTex;
    private uint _indirectPingFbo;
    private uint _indirectPingTex;
    private uint _glowFbo;
    private uint _glowTex;
    private uint _glowPingFbo;
    private uint _glowPingTex;
    private uint _shadowFbo;
    private uint _shadowTex;
    private uint _shadowMapSize = 2048;
    private uint _allocatedShadowMapSize;
    private Shader? _shadowShader;
    private uint _pointShadowFbo;
    private Shader? _pointShadowShader;
    // Must match MAX_POINT_SHADOWS in simple.frag.
    private const int MaxPointShadowLights = 8;
    private readonly uint[] _pointShadowCubeTextures = new uint[MaxPointShadowLights];
    private readonly int[] _pointShadowTextureSizes = new int[MaxPointShadowLights];

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
    private Shader? _pickShader;

    // ── Sobel selection highlight ──────────────────────────────────────────────

    /// <summary>
    /// Off-screen R8 framebuffer used to stamp a flat white mask of the selected
    /// objects.  The Sobel edge shader then reads this to find silhouette edges.
    /// </summary>
    private uint _silhouetteFbo;
    private uint _silhouetteTex;

    /// <summary>Shader that writes flat white into the silhouette mask FBO.</summary>
    private Shader? _silhouetteShader;

    /// <summary>
    /// Full-screen Sobel edge detection shader.  Reads <see cref="_silhouetteTex"/>
    /// and paints the detected edges into the main display FBO.
    /// </summary>
    private Shader? _edgeShader;

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
    /// Sentinel value for the camera-selection indices meaning "use the
    /// render-output camera" — i.e. the first visible active camera, or the
    /// first visible camera if none is active, or the work camera otherwise.
    /// </summary>
    public const int RenderOutputIndex = -1;

    /// <summary>
    /// Index of the active camera for the main viewport.
    /// <see cref="RenderOutputIndex"/> (-1) = use the render-output camera.
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

    /// <summary>
    /// Enables manual particle preview while paused. When enabled, only
    /// user-selected particle spawners are simulated.
    /// </summary>
    public bool ParticlePreviewEnabled { get; set; } = false;

    private ViewportRenderMode _viewportRenderMode = ViewportRenderMode.Shaded;
    private RenderedPassMode _renderedPassMode = RenderedPassMode.Combined;
    public bool HighQualityPreviewEnabled { get; private set; }
    public bool ShadowDebugEnabled { get; private set; }

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
    private uint _previewDepthTex;
    private uint _previewGlowFbo;
    private uint _previewGlowTex;
    private uint _previewGlowPingFbo;
    private uint _previewGlowPingTex;
    private uint _previewIndirectFbo;
    private uint _previewIndirectTex;
    private uint _previewIndirectPingFbo;
    private uint _previewIndirectPingTex;
    private uint _previewWidth = 1;
    private uint _previewHeight = 1;
    private uint _previewShadowFbo;
    private uint _previewShadowTex;
    private Shader? _previewShadowShader;
    private uint _previewPointShadowFbo;
    private Shader? _previewPointShadowShader;
    private readonly uint[] _previewPointShadowCubeTextures = new uint[MaxPointShadowLights];

    // Preview pick / silhouette FBOs. Mirrors the main viewport's resources so
    // the camera preview can run its own GPU colour-ID pick and Sobel outline
    // pass without disturbing the main viewport's framebuffers.
    private uint _previewPickFbo;
    private uint _previewPickColorTex;
    private uint _previewPickRbo;
    private uint _previewSilhouetteFbo;
    private uint _previewSilhouetteTex;

    // Stable targets for scene-camera albedo textures. The normal preview
    // texture is shared/resized by the UI and therefore cannot be bound
    // directly to scene objects.
    private readonly Dictionary<string, uint> _cameraFeedTextures = new(StringComparer.Ordinal);
    private uint _cameraFeedCopyFbo;
    private const uint CameraFeedSize = 512;

    // Screen-space mouse position captured on left-button release inside the
    // preview viewport so the colour-pick read-back can happen inside the
    // preview render (GL context active). NaN means no pick is pending.
    private float _pendingPreviewPickX = float.NaN;
    private float _pendingPreviewPickY = float.NaN;
    private bool  _pendingPreviewPickCtrl;

    public uint ColorTexture => _previewColorTex;

    /// <summary>True while a GLFW CameraWindow owns the rendering.</summary>
    public bool Undocked { get; set; }

    /// <summary>
    /// Raised when the user clicks "Pop".  The subscriber (MainWindow / main.cs)
    /// should create a <see cref="CameraWindow"/> and add it to the window list.
    /// </summary>
    public event Action? PopRequested;

    /// <summary>
    /// Raised when the preview requests that an undocked camera window be hidden.
    /// </summary>
    public event Action? HideRequested;

    public enum Corner { BottomRight, BottomLeft, TopRight, TopLeft }
    private Corner _corner = Corner.BottomRight;

    /// <summary>Current corner the inline preview is anchored to.</summary>
    public Corner InlineCorner => _corner;

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
    private bool _inlineDragActive;
    private Vector2 _inlineWindowLastPos = Vector2.Zero;
    private bool _inlineMouseWasDownLastFrame;
    private Vector2 _inlineMouseDownPos = Vector2.Zero;
    private int _inlineFramesSinceMouseUp;
    private Vector2 _inlineWindowPosBeforeSnap = Vector2.Zero;
    private bool _inlineSnappedThisInteraction;
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
    private Shader? _boneShader;

    /// <summary>RGBA colour for unselected bone indicators.</summary>
    private static readonly vec4 BoneColor         = new(0.80f, 0.65f, 0.10f, 1f); // amber
    /// <summary>RGBA colour for selected bone indicators.</summary>
    private static readonly vec4 BoneColorSelected = new(1.00f, 0.90f, 0.20f, 1f); // bright yellow

    // ── Light billboard ────────────────────────────────────────────────────────

    /// <summary>Billboard shader used to draw camera-facing light icons.</summary>
    private Shader? _billboardShader;

    /// <summary>Camera-facing ring shader used for selected-light range indicators.</summary>
    private Shader? _lightRingShader;

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
    /// Creates a camera-following tiled XZ ground plane and assigns the <c>grass_block_top</c>
    /// texture as its surface.  Must be called after both the GL context and
    /// <see cref="TerrainAtlas"/> are initialised.
    /// </summary>
    public void InitGroundPlane()
    {
        if (Gl == null) return;

        // Keep the quad reasonably sized to avoid visible interpolation artifacts
        // on very large two-triangle planes; recentering still makes it feel infinite.
        _groundPlane = new PlaneMesh(Gl, 512f, 512f, PlaneOrientation.XZ);

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
        string normalizedKey = tileKey.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKey))
            return false;

        var atlas = normalizedAtlas == "item" ? ItemsAtlas.Textures : TerrainAtlas.Textures;
        if (!atlas.TryGetValue(normalizedKey, out uint tileId))
            return false;

        _groundPlane.TextureId = tileId;
        _groundPlane.Albedo = new vec3(1f, 1f, 1f);
        if (normalizedAtlas == "block" && MinecraftModelMesh.TryGetBiomeTintForTextureKey(normalizedKey, out vec3 tint))
            _groundPlane.Albedo = tint;

        GroundTileAtlas = normalizedAtlas;
        GroundTileKey = normalizedKey;
        return true;
    }

    private void SyncLegacyRenderFlags()
    {
        HighQualityPreviewEnabled = _viewportRenderMode == ViewportRenderMode.Rendered;
        ShadowDebugEnabled = HighQualityPreviewEnabled && _renderedPassMode == RenderedPassMode.Shadow;
    }

    private void SetRenderMode(ViewportRenderMode mode)
    {
        _viewportRenderMode = mode;
        if (_viewportRenderMode != ViewportRenderMode.Rendered)
            _renderedPassMode = RenderedPassMode.Combined;
        SyncLegacyRenderFlags();
    }

    private void SetRenderedPass(RenderedPassMode pass)
    {
        _renderedPassMode = pass;
        if (_viewportRenderMode != ViewportRenderMode.Rendered)
            _viewportRenderMode = ViewportRenderMode.Rendered;
        SyncLegacyRenderFlags();
    }

    private string GetRenderModeLabel()
    {
        return _viewportRenderMode switch
        {
            ViewportRenderMode.Wireframe => "Wireframe",
            ViewportRenderMode.FlatUnshaded => "Flat",
            ViewportRenderMode.Shaded => "Shaded",
            ViewportRenderMode.Rendered => "Rendered",
            _ => "Shaded"
        };
    }

    private string GetRenderedPassLabel()
    {
        return _renderedPassMode switch
        {
            RenderedPassMode.Combined => "Combined",
            RenderedPassMode.AmbientOcclusion => "AO",
            RenderedPassMode.Shadow => "Shadow",
            _ => "Combined"
        };
    }

    private void DrawRenderModeSelector(string idSuffix, float width = 170f)
    {
        string previewLabel = _viewportRenderMode == ViewportRenderMode.Rendered
            ? $"{GetRenderModeLabel()} | {GetRenderedPassLabel()}"
            : GetRenderModeLabel();

        ImGui.SetNextItemWidth(width);

        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.05f, 0.05f, 0.05f, 0.88f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.12f, 0.12f, 0.12f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.16f, 0.16f, 0.16f, 0.98f));

        if (ImGui.BeginCombo($"##renderModeCombo{idSuffix}", previewLabel))
        {
            bool wire = _viewportRenderMode == ViewportRenderMode.Wireframe;
            if (ImGui.Selectable("Wireframe", wire)) SetRenderMode(ViewportRenderMode.Wireframe);
            if (wire) ImGui.SetItemDefaultFocus();

            bool flat = _viewportRenderMode == ViewportRenderMode.FlatUnshaded;
            if (ImGui.Selectable("Flat (Unshaded)", flat)) SetRenderMode(ViewportRenderMode.FlatUnshaded);

            bool shaded = _viewportRenderMode == ViewportRenderMode.Shaded;
            if (ImGui.Selectable("Shaded", shaded)) SetRenderMode(ViewportRenderMode.Shaded);

            bool rendered = _viewportRenderMode == ViewportRenderMode.Rendered;
            if (ImGui.Selectable("Rendered", rendered)) SetRenderMode(ViewportRenderMode.Rendered);

            if (_viewportRenderMode == ViewportRenderMode.Rendered)
            {
                ImGui.Separator();
                ImGui.TextDisabled("Render Pass");

                bool combined = _renderedPassMode == RenderedPassMode.Combined;
                if (ImGui.Selectable("Combined", combined)) SetRenderedPass(RenderedPassMode.Combined);

                bool ao = _renderedPassMode == RenderedPassMode.AmbientOcclusion;
                if (ImGui.Selectable("AO", ao)) SetRenderedPass(RenderedPassMode.AmbientOcclusion);

                bool shadow = _renderedPassMode == RenderedPassMode.Shadow;
                if (ImGui.Selectable("Shadow", shadow)) SetRenderedPass(RenderedPassMode.Shadow);
            }

            ImGui.EndCombo();
        }

        ImGui.PopStyleColor(3);
    }

    public void DrawRenderModeSelectorPublic(string idSuffix, float width = 170f)
    {
        DrawRenderModeSelector(idSuffix, width);
    }

    public void ToggleHighQualityPreview()
    {
        if (_viewportRenderMode == ViewportRenderMode.Rendered)
            SetRenderMode(ViewportRenderMode.Shaded);
        else
            SetRenderMode(ViewportRenderMode.Rendered);
    }

    public void ToggleShadowDebugMode()
    {
        if (_viewportRenderMode != ViewportRenderMode.Rendered)
        {
            SetRenderMode(ViewportRenderMode.Rendered);
            SetRenderedPass(RenderedPassMode.Shadow);
            return;
        }

        if (_renderedPassMode == RenderedPassMode.Shadow)
            SetRenderedPass(RenderedPassMode.Combined);
        else
            SetRenderedPass(RenderedPassMode.Shadow);
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

        // Sampleable depth texture used by the ambient-occlusion post pass.
        Gl.GenTextures(1, out _depthTex);
        Gl.BindTexture(GLEnum.Texture2D, _depthTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.DepthComponent24, width, height, 0,
            PixelFormat.DepthComponent, GLEnum.UnsignedInt, null);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.DepthAttachment, GLEnum.Texture2D, _depthTex, 0);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);

        var status = Gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.Error.WriteLine($"Framebuffer incomplete: {status}");

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);

        // ── Pick FBO ──────────────────────────────────────────────────────────
        InitPickFramebuffer(width, height);

        // ── Pick shader ───────────────────────────────────────────────────────
        _pickShader = new Shader(Gl);
        _pickShader.CompileShader("pick.vert", "pick.frag");

        // ── Silhouette mask FBO ───────────────────────────────────────────────
        InitSilhouetteFbo(width, height);

        // ── Silhouette shader (flat white into the mask FBO) ──────────────────
        _silhouetteShader = new Shader(Gl);
        _silhouetteShader.CompileShader("pick.vert", "silhouette.frag");

        // ── Sobel edge shader (full-screen quad, reads mask, writes edges) ────
        _edgeShader = new Shader(Gl);
        _edgeShader.CompileShader("edge.vert", "edge.frag");
        _ambientOcclusionShader = new Shader(Gl);
        _ambientOcclusionShader.CompileShader("edge.vert", "ambient_occlusion.frag");
        _indirectLightingShader = new Shader(Gl);
        _indirectLightingShader.CompileShader("edge.vert", "indirect_lighting.frag");
        _indirectDenoiseShader = new Shader(Gl);
        _indirectDenoiseShader.CompileShader("edge.vert", "indirect_denoise.frag");
        _glowShader = new Shader(Gl);
        _glowShader.CompileShader("edge.vert", "glow.frag");
        _filmGrainShader = new Shader(Gl);
        _filmGrainShader.CompileShader("edge.vert", "film_grain.frag");

        // ── Background quad shader + geometry ───────────────────────────────
        InitBackgroundRenderer();
        InitSkyRenderer();

        // ── Glow ping texture/FBO ───────────────────────────────────────────
        InitGlowResources(ref _indirectFbo, ref _indirectTex, width, height, "Main indirect");
        InitGlowResources(ref _indirectPingFbo, ref _indirectPingTex, width, height, "Main indirect ping");
        InitGlowResources(ref _glowFbo, ref _glowTex, width, height, "Main glow");
        InitGlowResources(ref _glowPingFbo, ref _glowPingTex, width, height, "Main glow ping");

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

        // ── Scene UBO (shared by all meshes) ──────────────────────────────────
        _sceneUBO = new SceneUniformBuffer(Gl);
        Mesh.SceneUBO = _sceneUBO;

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
        _backgroundShader = new Shader(Gl);
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

    private void InitSkyRenderer()
    {
        _skyShader = new Shader(Gl);
        _skyShader.CompileShader("sky.vert", "sky.frag");
        ReloadSkyTextures();
    }

    public void ReloadSkyTextures()
    {
        if (Gl == null || PropertiesPanel == null) return;
        LoadSunSkyTexture(PropertiesPanel.SunTexture);
        LoadMoonSkyTextures(PropertiesPanel.MoonTexture);
        LoadSkyTexture(PropertiesPanel.CloudTexture, ref _cloudTexture, ref _loadedCloudPath, true);
    }

    private void LoadSunSkyTexture(string configuredPath)
    {
        if (TryLoadSkyTexture(configuredPath, ref _sunTexture, ref _loadedSunPath, out _, out _))
            return;

        if (string.Equals(configuredPath, LegacySunTexturePath, StringComparison.OrdinalIgnoreCase))
            TryLoadSkyTexture(ModernSunTexturePath, ref _sunTexture, ref _loadedSunPath, out _, out _);
        else if (string.Equals(configuredPath, ModernSunTexturePath, StringComparison.OrdinalIgnoreCase))
            TryLoadSkyTexture(LegacySunTexturePath, ref _sunTexture, ref _loadedSunPath, out _, out _);
    }

    private void LoadMoonSkyTextures(string configuredPath)
    {
        // Prefer the configured moon texture when available.
        if (TryLoadSkyTexture(configuredPath, ref _moonTexture, ref _loadedMoonPath, out int width, out int height))
        {
            _moonTextureIsAtlas = IsMoonAtlasSize(width, height);
            if (_moonTextureIsAtlas)
                DeleteMoonPhaseTextures();
            return;
        }

        // Legacy moon atlas path moved to split phase files in modern versions.
        if (string.Equals(configuredPath, LegacyMoonTexturePath, StringComparison.OrdinalIgnoreCase) &&
            TryLoadModernMoonPhaseTextures())
        {
            _moonTextureIsAtlas = false;
            return;
        }

        _moonTextureIsAtlas = true;
    }

    private bool TryLoadModernMoonPhaseTextures()
    {
        bool loadedAll = true;
        for (int i = 0; i < ModernMoonPhaseFileNames.Length; i++)
        {
            string phasePath = $"minecraft:environment/celestial/moon/{ModernMoonPhaseFileNames[i]}";
            if (!TryLoadSkyTexture(phasePath, ref _moonPhaseTextures[i], ref _loadedMoonPhasePaths[i], out _, out _))
                loadedAll = false;
        }

        if (!loadedAll)
        {
            DeleteMoonPhaseTextures();
            return false;
        }

        if (_moonTexture != 0)
        {
            Gl.DeleteTexture(_moonTexture);
            _moonTexture = 0;
        }
        _loadedMoonPath = LegacyMoonTexturePath;
        return true;
    }

    private void DeleteMoonPhaseTextures()
    {
        if (Gl == null) return;
        for (int i = 0; i < _moonPhaseTextures.Length; i++)
        {
            if (_moonPhaseTextures[i] != 0)
                Gl.DeleteTexture(_moonPhaseTextures[i]);
            _moonPhaseTextures[i] = 0;
            _loadedMoonPhasePaths[i] = "";
        }
    }

    private uint GetMoonTextureForPhase(int phase)
    {
        if (_moonTextureIsAtlas)
            return _moonTexture;

        int index = Math.Clamp(phase, 0, _moonPhaseTextures.Length - 1);
        uint phaseTexture = _moonPhaseTextures[index];
        return phaseTexture != 0 ? phaseTexture : _moonTexture;
    }

    private static bool IsMoonAtlasSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return false;
        if (width % 4 != 0 || height % 2 != 0)
            return false;
        return (width / 4) == (height / 2);
    }

    private unsafe void LoadSkyTexture(string configuredPath, ref uint texture, ref string loadedPath, bool repeat = false)
    {
        TryLoadSkyTexture(configuredPath, ref texture, ref loadedPath, out _, out _, repeat);
    }

    private unsafe bool TryLoadSkyTexture(
        string configuredPath,
        ref uint texture,
        ref string loadedPath,
        out int width,
        out int height,
        bool repeat = false)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(configuredPath)) return false;
        if (string.Equals(configuredPath, loadedPath, StringComparison.OrdinalIgnoreCase) && texture != 0)
            return true;
        byte[]? data = null;
        if (configuredPath.StartsWith("resourcepack:", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = configuredPath.Split(':', 3);
            if (parts.Length == 3)
                data = MinecraftDataLoader.EnumerateResourcePackFiles("assets", ".png")
                    .FirstOrDefault(f => MinecraftDataLoader.GetResourcePackId(f.PackName) == parts[1] &&
                                         f.RelativePath.Equals(parts[2], StringComparison.OrdinalIgnoreCase))?.Data;
        }
        else
        {
            string path = configuredPath.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(MinecraftDataLoader.GetVersionRoot(), "textures", configuredPath[10..].Replace('/', Path.DirectorySeparatorChar))
                : Path.GetFullPath(configuredPath);
            if (File.Exists(path)) data = File.ReadAllBytes(path);
        }
        if (data == null || data.Length == 0) return false;
        try
        {
            using MemoryStream stream = new(data);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            width = image.Width;
            height = image.Height;
            // Legacy sun/moon PNGs are RGB and encode transparency as black.
            bool pngHasAlpha = data.Length > 25 && (data[25] == 4 || data[25] == 6);
            if (!pngHasAlpha)
            {
                for (int i = 0; i < image.Data.Length; i += 4)
                {
                    byte alpha = Math.Max(image.Data[i], Math.Max(image.Data[i + 1], image.Data[i + 2]));
                    image.Data[i + 3] = alpha;
                    if (alpha > 0)
                    {
                        image.Data[i] = (byte)Math.Min(255, image.Data[i] * 255 / alpha);
                        image.Data[i + 1] = (byte)Math.Min(255, image.Data[i + 1] * 255 / alpha);
                        image.Data[i + 2] = (byte)Math.Min(255, image.Data[i + 2] * 255 / alpha);
                    }
                }
            }
            if (texture != 0) Gl.DeleteTexture(texture);
            Gl.GenTextures(1, out texture);
            Gl.BindTexture(GLEnum.Texture2D, texture);
            fixed (byte* pixels = image.Data)
                Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, (uint)image.Width, (uint)image.Height, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pixels);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)(repeat ? GLEnum.Repeat : GLEnum.ClampToEdge));
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)(repeat ? GLEnum.Repeat : GLEnum.ClampToEdge));
            Gl.BindTexture(GLEnum.Texture2D, 0);
            loadedPath = configuredPath;
            return true;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Failed to load sky texture '{configuredPath}': {ex.Message}"); }
        return false;
    }

    private static vec3 DirectionFromEuler(float[] degrees)
    {
        float x = glm.Radians(degrees[0]), y = glm.Radians(degrees[1]), z = glm.Radians(degrees[2]);
        vec3 v = new(0f, 0f, -1f);
        v = new vec3(v.x, v.y * MathF.Cos(x) - v.z * MathF.Sin(x), v.y * MathF.Sin(x) + v.z * MathF.Cos(x));
        v = new vec3(v.x * MathF.Cos(y) + v.z * MathF.Sin(y), v.y, -v.x * MathF.Sin(y) + v.z * MathF.Cos(y));
        return new vec3(v.x * MathF.Cos(z) - v.y * MathF.Sin(z), v.x * MathF.Sin(z) + v.y * MathF.Cos(z), v.z).Normalized;
    }

    // Keep celestial fill lights from affecting the world when they are too
    // close to/below the horizon by X-rotation.
    private const float CelestialFillLightMinXAngle = 5f;
    private const float CelestialFillLightMaxXAngle = 175f;

    private static float NormalizeAngle360(float angleDegrees)
    {
        float wrapped = angleDegrees % 360f;
        if (wrapped < 0f)
            wrapped += 360f;
        return wrapped;
    }

    private static bool IsCelestialFillLightActive(float xAngleDegrees)
    {
        float normalizedX = NormalizeAngle360(xAngleDegrees);
        return normalizedX >= CelestialFillLightMinXAngle && normalizedX <= CelestialFillLightMaxXAngle;
    }

    private void RenderSky(Camera camera, uint width, uint height)
    {
        var p = PropertiesPanel;
        if (p == null) return;
        float orbit = p.SkyTime * 15f;
        float[] sunAngles = [p.SunAngle[0] + orbit, p.SunAngle[1], p.SunAngle[2]];
        bool sunFillLightActive = IsCelestialFillLightActive(sunAngles[0]);
        vec3 sunDirection = DirectionFromEuler(sunAngles);
        vec3 groundCenter = new vec3(
            MathF.Floor(camera.Position.x),
            0f,
            MathF.Floor(camera.Position.z));
        _groundPlaneModel = mat4.Translate(groundCenter);
        if (_groundPlane != null)
        {
            // Keep UV sampling anchored in world space while the mesh recenters.
            _groundPlane.TextureOffset = new vec2(groundCenter.x, groundCenter.z);
        }
        Mesh.GlobalSunFillLightDirection = sunDirection;
        Mesh.GlobalSunFillLightColor = new vec3(p.SunFillLightColor[0], p.SunFillLightColor[1], p.SunFillLightColor[2]);
        Mesh.GlobalSunFillLightStrength = (p.UseSky && sunFillLightActive) ? p.SunFillLightStrength : 0f;
        Mesh.SunFillLightCastsShadows = p.UseSky && sunFillLightActive && p.SunFillLightCastsShadows;
        float[] moonAngles = [p.MoonAngle[0] + orbit, p.MoonAngle[1], p.MoonAngle[2]];
        bool moonFillLightActive = IsCelestialFillLightActive(moonAngles[0]);
        vec3 moonDirection = DirectionFromEuler(moonAngles);
        Mesh.GlobalMoonFillLightDirection = moonDirection;
        Mesh.GlobalMoonFillLightColor = new vec3(p.MoonFillLightColor[0], p.MoonFillLightColor[1], p.MoonFillLightColor[2]);
        Mesh.GlobalMoonFillLightStrength = (p.UseSky && moonFillLightActive) ? p.MoonFillLightStrength : 0f;
        Mesh.MoonFillLightCastsShadows = p.UseSky && moonFillLightActive && p.MoonFillLightCastsShadows;
        Mesh.MainFillLightCastsShadows = p.FillLightCastsShadows;
        float nightFactor = ComputeNightBlend(sunDirection.y);
        float ambR = p.AmbientLightColor[0] * (1f - nightFactor) + p.NightAmbientLightColor[0] * nightFactor;
        float ambG = p.AmbientLightColor[1] * (1f - nightFactor) + p.NightAmbientLightColor[1] * nightFactor;
        float ambB = p.AmbientLightColor[2] * (1f - nightFactor) + p.NightAmbientLightColor[2] * nightFactor;
        float ambStr = p.AmbientLightStrength * (1f - nightFactor) + p.NightAmbientLightStrength * nightFactor;
        Mesh.GlobalAmbientColor = new vec3(Math.Clamp(ambR, 0f, 1f), Math.Clamp(ambG, 0f, 1f), Math.Clamp(ambB, 0f, 1f));
        Mesh.GlobalAmbientStrength = Math.Clamp(ambStr, 0f, 5f);
        vec3 fogColor = p.CustomFogColor
            ? new vec3(p.FogColor[0], p.FogColor[1], p.FogColor[2])
            : new vec3(p.BackgroundColor[0], p.BackgroundColor[1], p.BackgroundColor[2]);
        Mesh.GlobalCameraPosition = camera.Position;
        Mesh.FogEnabled = p.FogEnabled;
        Mesh.FogColor = p.CustomObjectFogColor
            ? new vec3(p.ObjectFogColor[0], p.ObjectFogColor[1], p.ObjectFogColor[2])
            : fogColor;
        Mesh.FogDistance = Math.Max(0f, p.FogDistance);
        Mesh.FogFadeSize = Math.Max(1f, p.FogFadeSize);
        Mesh.FogHeight = p.FogHeight;
        Mesh.HeightFogEnabled = p.HeightFog;
        Mesh.HeightFogColor = p.CustomHeightFogColor
            ? new vec3(p.HeightFogColor[0], p.HeightFogColor[1], p.HeightFogColor[2])
            : Mesh.FogColor;
        Mesh.HeightFogSize = Math.Max(1f, p.HeightFogSize);
        Mesh.HeightFogOffset = p.HeightFogOffset;
        if (!p.UseSky || _skyShader == null || _backgroundVao == 0) return;
        ReloadSkyTextures();
        vec3 forward = (camera.Target - camera.Position).Normalized;
        vec3 right = vec3.Cross(forward, vec3.UnitY).Normalized;
        vec3 up = vec3.Cross(right, forward).Normalized;
        uint program = _skyShader.ShaderProgram;
        Gl.Enable(GLEnum.DepthTest); Gl.DepthFunc(GLEnum.Always); Gl.DepthMask(true); Gl.Disable(GLEnum.CullFace); Gl.UseProgram(program);
        void V3(string n, vec3 v) { int l = Gl.GetUniformLocation(program, n); if (l >= 0) Gl.Uniform3(l, v.x, v.y, v.z); }
        void C3(string n, float[] v) { int l = Gl.GetUniformLocation(program, n); if (l >= 0) Gl.Uniform3(l, v[0], v[1], v[2]); }
        void F(string n, float v) { int l = Gl.GetUniformLocation(program, n); if (l >= 0) Gl.Uniform1(l, v); }
        V3("uCameraForward", forward); V3("uCameraRight", right); V3("uCameraUp", up);
        V3("uCameraPosition", camera.Position);
        F("uTanHalfFov", MathF.Tan(camera.FovY * 0.5f)); F("uAspect", height > 0 ? (float)width / height : 1f);
        F("uCameraNear", camera.Near); F("uCameraFar", camera.Far);
        C3("uHorizonDay", p.SkyHorizonDay); C3("uZenithDay", p.SkyZenithDay);
        C3("uHorizonSunset", p.SkyHorizonSunset); C3("uZenithSunset", p.SkyZenithSunset);
        C3("uHorizonNight", p.SkyHorizonNight); C3("uZenithNight", p.SkyZenithNight);
        C3("uCloudColor", p.CloudColor);
        C3("uNightCloudColor", p.NightCloudColor);
        int cloudMode = Gl.GetUniformLocation(program, "uCloudMode");
        if (cloudMode >= 0) Gl.Uniform1(cloudMode, p.CloudRenderMode == "story" ? 1 : p.CloudRenderMode == "flat" ? 2 : 0);
        float animationSeconds = (Timeline.Instance?.CurrentFrame ?? 0) / (float)Math.Max(1, p.GetFramerate());
        int cloudOffset = Gl.GetUniformLocation(program, "uCloudOffset");
        if (cloudOffset >= 0) Gl.Uniform2(cloudOffset, p.CloudOffset[0] + p.CloudSpeed * animationSeconds, p.CloudOffset[1]);
        F("uCloudHeight", p.CloudHeight); F("uCloudBlockSize", p.CloudBlockSize); F("uCloudThickness", p.CloudThickness);
        V3("uSunDirection", sunDirection); V3("uMoonDirection", moonDirection);
        F("uSunSize", p.SunSize); F("uMoonSize", p.MoonSize);
        int phase = Gl.GetUniformLocation(program, "uMoonPhase"); if (phase >= 0) Gl.Uniform1(phase, p.MoonPhase);
        int moonAtlas = Gl.GetUniformLocation(program, "uMoonAtlasEnabled"); if (moonAtlas >= 0) Gl.Uniform1(moonAtlas, _moonTextureIsAtlas ? 1 : 0);
        int twilight = Gl.GetUniformLocation(program, "uTwilight"); if (twilight >= 0) Gl.Uniform1(twilight, p.Twilight ? 1 : 0);
        int showStars = Gl.GetUniformLocation(program, "uShowStars"); if (showStars >= 0) Gl.Uniform1(showStars, p.ShowStars ? 1 : 0);
        F("uStarDensity", p.StarDensity);
        F("uStarBrightness", p.StarBrightness);
        F("uStarTwinkleSpeed", p.StarTwinkleSpeed);
        C3("uStarColor", p.StarColor);
        F("uTime", animationSeconds);
        int fogEnabled = Gl.GetUniformLocation(program, "uFogEnabled"); if (fogEnabled >= 0) Gl.Uniform1(fogEnabled, p.FogEnabled && p.SkyFog ? 1 : 0);
        V3("uFogColor", fogColor);
        int cloudFogEnabled = Gl.GetUniformLocation(program, "uCloudFogEnabled"); if (cloudFogEnabled >= 0) Gl.Uniform1(cloudFogEnabled, p.FogEnabled ? 1 : 0);
        V3("uObjectFogColor", Mesh.FogColor);
        F("uFogDistance", Mesh.FogDistance);
        F("uFogFadeSize", Mesh.FogFadeSize);
        Gl.ActiveTexture(GLEnum.Texture0); Gl.BindTexture(GLEnum.Texture2D, _sunTexture);
        int sun = Gl.GetUniformLocation(program, "uSunTex"); if (sun >= 0) Gl.Uniform1(sun, 0);
        Gl.ActiveTexture(GLEnum.Texture1); Gl.BindTexture(GLEnum.Texture2D, GetMoonTextureForPhase(p.MoonPhase));
        int moon = Gl.GetUniformLocation(program, "uMoonTex"); if (moon >= 0) Gl.Uniform1(moon, 1);
        Gl.ActiveTexture(GLEnum.Texture2); Gl.BindTexture(GLEnum.Texture2D, _cloudTexture);
        int clouds = Gl.GetUniformLocation(program, "uCloudTex"); if (clouds >= 0) Gl.Uniform1(clouds, 2);
        Gl.BindVertexArray(_backgroundVao); Gl.DrawArrays(GLEnum.Triangles, 0, 6); Gl.BindVertexArray(0);
        Gl.ActiveTexture(GLEnum.Texture0); Gl.DepthMask(true); Gl.Enable(GLEnum.DepthTest); Gl.DepthFunc(GLEnum.Less); Gl.Enable(GLEnum.CullFace);
    }

    private static float ComputeNightBlend(float sunY)
    {
        float t = Math.Clamp((sunY + 0.18f) / 0.2f, 0f, 1f);
        return 1f - t * t * (3f - 2f * t);
    }

    public void RenderSkyPublic(Camera camera, uint width, uint height) => RenderSky(camera, width, height);

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
                Buffer.BlockCopy(img.Data, srcRow * rowBytes, flipped, row * rowBytes, rowBytes);
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
            Console.Error.WriteLine($"Failed to load background image '{path}': {ex.Message}");
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

    private void RenderBackgroundPlane(uint viewportWidth, uint viewportHeight)
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
        if (viewportLoc >= 0) Gl.Uniform2(viewportLoc, (float)viewportWidth, viewportHeight);

        int imageLoc = Gl.GetUniformLocation(program, "uImageSize");
        if (imageLoc >= 0) Gl.Uniform2(imageLoc, _backgroundImageWidth, (float)_backgroundImageHeight);

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
            Console.Error.WriteLine($"Pick framebuffer incomplete: {status}");

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
            Console.Error.WriteLine($"Silhouette framebuffer incomplete: {status}");

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
    }

    // ── Light billboard init ───────────────────────────────────────────────────

    /// <summary>
    /// Loads light billboard textures and creates the shared screen-aligned quad
    /// VAO used by <see cref="RenderLightBillboards"/>.
    /// </summary>
    private void InitLightBillboards()
    {
        if (Gl == null) return;

        // Load textures.
        _lightIconTexture = LoadEmbeddedTexture("light",    nearest: true);
        _lightRayTexture  = LoadEmbeddedTexture("lightRay", nearest: true);

        // Assign to shared static handles on LightSceneObject.
        LightSceneObject.BillboardVao = CreateBillboardVao();

        // Compile billboard shader.
        _billboardShader = new Shader(Gl);
        _billboardShader.CompileShader("billboard.vert", "billboard.frag");

        // Bone indicator shader (flat MVP + colour, same as gizmo).
        _boneShader = new Shader(Gl);
        _boneShader.CompileShader("gizmo.vert", "gizmo.frag");

        // Camera-facing ring shader for selected-light range indicators.
        _lightRingShader = new Shader(Gl);
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
    private void RenderBoneIndicators(mat4 view, mat4 proj)
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

    private bool ShouldShowBoneIndicator(BoneSceneObject bone) => bone is MiBoneSceneObject
        ? PreferencesPanel?.HideMineImatorBoneDisplayShapes != true
        : PreferencesPanel?.HideRegularAssetBoneDisplayShapes != true;

    private unsafe void RenderBoneIndicatorsRecursive(
        SceneObject obj, mat4 view, mat4 proj,
        int mvpLoc, int colorLoc, SelectionManager? sm)
    {
        if (!obj.GetEffectiveVisibility()) return;

        if (obj is BoneSceneObject bone && bone.IndicatorMesh != null && ShouldShowBoneIndicator(bone))
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

    private void RenderLightRangeIndicatorsRecursive(
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

    // ── Spot-light coverage indicators ────────────────────────────────────────

    /// <summary>
    /// Draws the spot-light editor representation.  Unselected spot lights
    /// receive a thin "stick" along their forward axis so the user can see
    /// the aim direction; selected spot lights receive a procedurally generated
    /// cone showing the full coverage area.  Both are drawn with depth-test
    /// always + no depth writes so they remain visible through geometry.
    /// Uses the same flat-colour gizmo shader as bone indicators / range rings.
    /// </summary>
    private void RenderSpotLightIndicators(mat4 view, mat4 proj)
    {
        if (_boneShader == null) return;
        if (LightSceneObject.SharedSpotConeMesh == null ||
            LightSceneObject.SharedSpotStickMesh == null) return;

        var sm = SelectionManager.Instance;

        uint prog = _boneShader.ShaderProgram;
        Gl.UseProgram(prog);

        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Always);
        Gl.DepthMask(false);
        Gl.Disable(GLEnum.CullFace);
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);

        int mvpLoc   = Gl.GetUniformLocation(prog, "uMVP");
        int colorLoc = Gl.GetUniformLocation(prog, "uColor");

        foreach (var root in SceneObjects)
            RenderSpotLightIndicatorsRecursive(root, view, proj, mvpLoc, colorLoc, sm);

        Gl.DepthMask(true);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.Disable(GLEnum.Blend);
    }

    private void RenderSpotLightIndicatorsRecursive(
        SceneObject obj, mat4 view, mat4 proj,
        int mvpLoc, int colorLoc, SelectionManager? sm)
    {
        if (!obj.GetEffectiveVisibility()) return;

        if (obj is LightSceneObject light && light.Type == LightType.Spot)
        {
            mat4 world    = obj.GetWorldMatrix();
            float range   = MathF.Max(0.01f, light.LightRange);
            float halfAng = MathF.Max(0.1f, light.LightSpotAngle * 0.5f);
            float tanHalf = MathF.Tan(glm.Radians(halfAng));
            float baseR   = range * tanHalf;

            // The light's local +Z is the cone / stick aim direction; the world
            // matrix already encodes the rotation, so scaling the model matrix
            // by (baseR, baseR, range) and then applying view+proj gives a
            // correctly oriented cone in world space.
            mat4 scale = mat4.Scale(new vec3(baseR, baseR, range));

            if (sm != null && sm.SelectedObjects.Contains(obj))
            {
                // Selected: draw the full cone (alpha 0.25 so interior is see-through).
                mat4 mvp = proj * view * world * scale;
                UploadFlatMVP(mvpLoc, mvp);
                if (colorLoc >= 0)
                    Gl.Uniform4(colorLoc,
                        light.LightColor.x,
                        light.LightColor.y,
                        light.LightColor.z,
                        0.25f);
                LightSceneObject.SharedSpotConeMesh!.RenderPickPass(Gl);
            }
            else
            {
                // Unselected: draw a thin stick along the aim direction.
                mat4 mvp = proj * view * world * scale;
                UploadFlatMVP(mvpLoc, mvp);
                if (colorLoc >= 0)
                    Gl.Uniform4(colorLoc,
                        light.LightColor.x,
                        light.LightColor.y,
                        light.LightColor.z,
                        1f);
                LightSceneObject.SharedSpotStickMesh!.RenderPickPass(Gl);
            }
        }

        foreach (var child in obj.Children)
            RenderSpotLightIndicatorsRecursive(child, view, proj, mvpLoc, colorLoc, sm);
    }

    private unsafe void UploadFlatMVP(int mvpLoc, mat4 mvp)
    {
        if (mvpLoc < 0) return;
        float[] f =
        {
            mvp.m00, mvp.m01, mvp.m02, mvp.m03,
            mvp.m10, mvp.m11, mvp.m12, mvp.m13,
            mvp.m20, mvp.m21, mvp.m22, mvp.m23,
            mvp.m30, mvp.m31, mvp.m32, mvp.m33,
        };
        fixed (float* p = f) Gl.UniformMatrix4(mvpLoc, 1, false, p);
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

        // Restore state that range indicators may have altered.
        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);

        // Spot-light coverage indicators (cone when selected, stick when unselected).
        RenderSpotLightIndicators(view, proj);

        // Particle spawner emission bounds (wireframe box / ellipsoid).
        RenderParticleSpawnerEmissionShapes(view, proj);
    }

    private void RenderParticleSpawnerEmissionShapes(mat4 view, mat4 proj)
    {
        if (Gl == null)
            return;

        _particleEmissionWireCube ??= new CubeMesh(Gl)
        {
            Unlit = true,
            DoubleSided = true,
            IncludeInFog = false,
            Albedo = new vec3(0.25f, 0.85f, 1f),
            Alpha = 1f
        };

        _particleEmissionWireRing ??= new LightRangeRingMesh(Gl)
        {
            Unlit = true,
            DoubleSided = true,
            IncludeInFog = false,
            Albedo = new vec3(0.25f, 0.85f, 1f),
            Alpha = 1f
        };

        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Always);
        Gl.DepthMask(false);
        Gl.Disable(GLEnum.CullFace);
        Gl.PolygonMode(GLEnum.FrontAndBack, PolygonMode.Line);

        foreach (var root in SceneObjects)
            RenderParticleSpawnerEmissionShapesRecursive(root, view, proj);

        Gl.PolygonMode(GLEnum.FrontAndBack, PolygonMode.Fill);
        Gl.DepthMask(true);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
    }

    private void RenderParticleSpawnerEmissionShapesRecursive(SceneObject obj, mat4 view, mat4 proj)
    {
        if (!obj.GetEffectiveVisibility())
            return;

        if (obj is ParticleSpawnerSceneObject spawner && !spawner.IsRuntimeTransient)
        {
            vec3 extents = new vec3(
                MathF.Max(0f, spawner.SpawnBoxExtents.x),
                MathF.Max(0f, spawner.SpawnBoxExtents.y),
                MathF.Max(0f, spawner.SpawnBoxExtents.z));

            vec3 emissionScale = new vec3(
                MathF.Max(0.0001f, extents.x * 2f),
                MathF.Max(0.0001f, extents.y * 2f),
                MathF.Max(0.0001f, extents.z * 2f));

            mat4 baseTransform = spawner.TopLevelParticles
                ? mat4.Translate(spawner.GetWorldPosition())
                : spawner.GetWorldMatrix();

            if (spawner.EmissionShape == ParticleEmissionShape.Box)
            {
                mat4 boxModel = baseTransform * mat4.Scale(emissionScale);
                RenderMeshWithModeOverride(_particleEmissionWireCube!, boxModel, view, proj, true, includeFog: false);
            }
            else
            {
                mat4 scaled = baseTransform * mat4.Scale(emissionScale);

                // Draw 3 orthogonal rings to represent a wireframe ellipsoid.
                RenderMeshWithModeOverride(_particleEmissionWireRing!, scaled, view, proj, true, includeFog: false);

                mat4 xyRing = scaled * mat4.Rotate(MathF.PI * 0.5f, vec3.UnitX);
                RenderMeshWithModeOverride(_particleEmissionWireRing!, xyRing, view, proj, true, includeFog: false);

                mat4 yzRing = scaled * mat4.Rotate(MathF.PI * 0.5f, vec3.UnitZ);
                RenderMeshWithModeOverride(_particleEmissionWireRing!, yzRing, view, proj, true, includeFog: false);
            }
        }

        foreach (var child in obj.Children)
            RenderParticleSpawnerEmissionShapesRecursive(child, view, proj);
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
            Console.Error.WriteLine($"Embedded texture not found: {resourceName}");
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
            Buffer.BlockCopy(img.Data, srcRow * rowBytes, flipped, row * rowBytes, rowBytes);
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

        Gl.BindTexture(GLEnum.Texture2D, _depthTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.DepthComponent24, width, height, 0,
            PixelFormat.DepthComponent, GLEnum.UnsignedInt, null);

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

        ResizeGlowResources(_indirectTex, width, height);
        ResizeGlowResources(_indirectPingTex, width, height);
        ResizeGlowResources(_glowTex, width, height);
        ResizeGlowResources(_glowPingTex, width, height);
    }

    private unsafe void InitGlowResources(ref uint fbo, ref uint texture, uint width, uint height, string label)
    {
        if (Gl == null || width == 0 || height == 0)
            return;

        if (fbo == 0)
            Gl.GenFramebuffers(1, out fbo);
        if (texture == 0)
            Gl.GenTextures(1, out texture);

        Gl.BindTexture(GLEnum.Texture2D, texture);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, width, height, 0,
            PixelFormat.Rgb, GLEnum.UnsignedByte, (void*)0);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        Gl.BindFramebuffer(GLEnum.Framebuffer, fbo);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, texture, 0);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);

        var status = Gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.Error.WriteLine($"{label} framebuffer incomplete: {status}");

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
    }

    private unsafe void ResizeGlowResources(uint texture, uint width, uint height)
    {
        if (Gl == null || texture == 0 || width == 0 || height == 0)
            return;

        Gl.BindTexture(GLEnum.Texture2D, texture);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, width, height, 0,
            PixelFormat.Rgb, GLEnum.UnsignedByte, (void*)0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
    }

    private void ApplyGlowPassGeneric(
        uint sceneFbo,
        uint sourceColorTex,
        uint glowFbo,
        uint glowTex,
        uint glowPingFbo,
        uint glowPingTex,
        uint width,
        uint height,
        PropertiesPanel? settings,
        RenderedPassMode passMode)
    {
        if (Gl == null || _glowShader == null || sceneFbo == 0 || sourceColorTex == 0 || glowFbo == 0 || glowTex == 0 || glowPingFbo == 0 || glowPingTex == 0)
            return;
        if (width == 0 || height == 0 || settings == null || passMode != RenderedPassMode.Combined || !settings.GlowEnabled)
            return;

        float glowStrength = Math.Clamp(settings.GlowStrength, 0f, 2f);
        float glowSize = Math.Clamp(settings.GlowSize, 0f, 20f);
        if (glowStrength <= 0f || glowSize <= 0f)
            return;

        uint program = _glowShader.ShaderProgram;
        int sceneLoc = Gl.GetUniformLocation(program, "uScene");
        int texelLoc = Gl.GetUniformLocation(program, "uTexelSize");
        int strengthLoc = Gl.GetUniformLocation(program, "uStrength");
        int sizeLoc = Gl.GetUniformLocation(program, "uSize");
        int directionLoc = Gl.GetUniformLocation(program, "uDirection");
        int modeLoc = Gl.GetUniformLocation(program, "uMode");

        Gl.Disable(GLEnum.DepthTest);
        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.Blend);

        // Pass 1: threshold extraction from the scene.
        Gl.BindFramebuffer(GLEnum.Framebuffer, glowFbo);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);
        Gl.Viewport(0, 0, width, height);
        Gl.ClearColor(0f, 0f, 0f, 1f);
        Gl.Clear(ClearBufferMask.ColorBufferBit);
        Gl.UseProgram(program);
        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, sourceColorTex);
        if (sceneLoc >= 0) Gl.Uniform1(sceneLoc, 0);
        if (texelLoc >= 0) Gl.Uniform2(texelLoc, 1f / width, 1f / height);
        if (strengthLoc >= 0) Gl.Uniform1(strengthLoc, 1f);
        if (sizeLoc >= 0) Gl.Uniform1(sizeLoc, glowSize);
        if (directionLoc >= 0) Gl.Uniform2(directionLoc, 1f, 0f);
        if (modeLoc >= 0) Gl.Uniform1(modeLoc, 0);
        Gl.BindVertexArray(_edgeVao);
        Gl.DrawArrays(GLEnum.Triangles, 0, 3);

        // Pass 2: separable Gaussian blur, 3 stages, mirrored from the upscaled build.
        const int blurPassCount = 3;
        for (int i = 0; i < blurPassCount; i++)
        {
            float radius = glowSize / (1f + (1.333f * i));

            // Horizontal
            Gl.BindFramebuffer(GLEnum.Framebuffer, glowPingFbo);
            Gl.DrawBuffer(GLEnum.ColorAttachment0);
            Gl.ReadBuffer(GLEnum.ColorAttachment0);
            Gl.Viewport(0, 0, width, height);
            Gl.BindTexture(GLEnum.Texture2D, glowTex);
            if (sceneLoc >= 0) Gl.Uniform1(sceneLoc, 0);
            if (texelLoc >= 0) Gl.Uniform2(texelLoc, 1f / width, 1f / height);
            if (strengthLoc >= 0) Gl.Uniform1(strengthLoc, 1f);
            if (sizeLoc >= 0) Gl.Uniform1(sizeLoc, radius);
            if (directionLoc >= 0) Gl.Uniform2(directionLoc, 1f, 0f);
            if (modeLoc >= 0) Gl.Uniform1(modeLoc, 1);
            Gl.DrawArrays(GLEnum.Triangles, 0, 3);

            // Vertical
            Gl.BindFramebuffer(GLEnum.Framebuffer, glowFbo);
            Gl.DrawBuffer(GLEnum.ColorAttachment0);
            Gl.ReadBuffer(GLEnum.ColorAttachment0);
            Gl.Viewport(0, 0, width, height);
            Gl.BindTexture(GLEnum.Texture2D, glowPingTex);
            if (sizeLoc >= 0) Gl.Uniform1(sizeLoc, radius);
            if (directionLoc >= 0) Gl.Uniform2(directionLoc, 0f, 1f);
            if (modeLoc >= 0) Gl.Uniform1(modeLoc, 1);
            Gl.DrawArrays(GLEnum.Triangles, 0, 3);
        }

        // Pass 3: additive composite into the scene color texture.
        Gl.BindFramebuffer(GLEnum.Framebuffer, sceneFbo);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, sourceColorTex, 0);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);
        Gl.Viewport(0, 0, width, height);
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.One, GLEnum.One);
        Gl.BindTexture(GLEnum.Texture2D, glowTex);
        if (strengthLoc >= 0) Gl.Uniform1(strengthLoc, glowStrength);
        if (sizeLoc >= 0) Gl.Uniform1(sizeLoc, glowSize);
        if (directionLoc >= 0) Gl.Uniform2(directionLoc, 1f, 0f);
        if (modeLoc >= 0) Gl.Uniform1(modeLoc, 2);
        Gl.DrawArrays(GLEnum.Triangles, 0, 3);
        Gl.BindVertexArray(0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.Disable(GLEnum.Blend);

        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
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
            Buffer.BlockCopy(rgbPixels, srcRow, scanlines, destRow + 1, stride);
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
                string currentCamLabel;
                if (_activeCameraIndex == RenderOutputIndex)
                {
                    currentCamLabel = "Render Output";
                }
                else if (_activeCameraIndex == 0)
                {
                    currentCamLabel = "Work Camera";
                }
                else
                {
                    currentCamLabel = _activeCameraIndex - 1 < spawnedCams.Count
                        ? spawnedCams[_activeCameraIndex - 1].Name
                        : "Render Output";
                }

                ImGui.SetCursorPosY(itemY);
                ImGui.SetNextItemWidth(160f);
                if (ImGui.BeginCombo("##vpCamSelect", currentCamLabel))
                {
                    bool roSel = _activeCameraIndex == RenderOutputIndex;
                    if (ImGui.Selectable("Render Output##vpcamRO", roSel)) _activeCameraIndex = RenderOutputIndex;
                    if (roSel) ImGui.SetItemDefaultFocus();

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

            // Particle preview toggle — only affects paused manual simulation.
            {
                ImGui.SameLine();
                ImGui.SetCursorPosY(itemY);

                if (!ParticlePreviewEnabled)
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

                if (ImGui.Button("Particle Preview", new Vector2(0, iconSize)))
                    ParticlePreviewEnabled = !ParticlePreviewEnabled;

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
            ImGui.TextDisabled(ShadowDebugEnabled ? "Shadow Debug (Ctrl+F6)" : "Shadow Debug Off (Ctrl+F6)");

            // Render mode selector (header, top-right).
            const float renderModeWidth = 176f;
            float rightAlignedX = MathF.Max(4f, totalSize.X - renderModeWidth - 8f);
            ImGui.SetCursorPos(new Vector2(rightAlignedX, itemY));
            DrawRenderModeSelector("MainHeader", renderModeWidth);

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

        // Camera-backed materials must be refreshed before the main scene is
        // drawn. Each camera owns a stable texture, so several feeds can be
        // visible at the same time.
        PreviewViewport?.UpdateCameraTextureFeeds();

        // Use the active camera (work camera or a spawned camera) for both
        // input and rendering so controls always affect what is being viewed.
        var (activeCamera, activeCamObj) = GetActiveRenderCamera();

        // ── Camera orbit/pan input ─────────────────────────────────────────────
        HandleCameraInput(imageMin, imageSize, activeCamera, activeCamObj);

        // ── 3D render into FBO ────────────────────────────────────────────────
        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);
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

        var renderCamera = BuildPreparedCamera(activeCamera, activeCamObj);

        mat4 view = renderCamera.GetViewMatrix();
        mat4 proj = renderCamera.GetProjectionMatrix(aspect);
        float renderNear = renderCamera.Near;
        float renderFar = renderCamera.Far;

        RenderSky(renderCamera, w, h);
        RenderBackgroundPlane(w, h);
        // Sky and procedural clouds are background elements, not AO geometry.
        // Preserve their colour while resetting depth before scene objects draw.
        Gl.Clear(ClearBufferMask.DepthBufferBit);


        // Build frustum for per-frame culling.
        Frustum frustum = Frustum.FromViewProj(proj * view);

        // ── Per-frame mesh globals ─────────────────────────────────────────────
        Mesh.DeltaTime = ImGui.GetIO().DeltaTime;
        bool timelinePlaying = Timeline.Instance?.IsPlaying ?? false;
        if (!IsPreviewViewport)
            UpdateParticleSpawners(SceneObjects, (float)Mesh.DeltaTime, timelinePlaying);

        Mesh.AdvanceAnimatedTextures = timelinePlaying || MainWindow.IsAnimationRenderExportActive;
        int textureAnimFps = Math.Clamp(PropertiesPanel?.TextureAnimationFps ?? 20, 1, 240);
        Mesh.AnimatedTextureSpeedScale = textureAnimFps / 20.0;
        Mesh.ShadowsEnabled = false;
        Mesh.ShadowMapTexture = 0;
        Mesh.ShadowLightSpaceMatrix = mat4.Identity;
        Mesh.ShadowDebugMode = 0;
        bool globalShadows = PropertiesPanel?.ShadowsEnabled ?? true;
        Mesh.DirectionalShadowEnabled = globalShadows && (Mesh.MainFillLightCastsShadows ||
                        Mesh.SunFillLightCastsShadows ||
                        Mesh.MoonFillLightCastsShadows);
        Array.Clear(Mesh.PointShadowCubeTextures, 0, Mesh.PointShadowCubeTextures.Length);

        // Rebuild the full scene light list every frame so that moved / deleted
        // lights are always up-to-date. This is the unfiltered candidate list;
        // Mesh.PointLights (what the shader actually reads) is repopulated per
        // mesh by SelectPointLightsForMesh just before each draw call.
        Dictionary<LightSceneObject, int> pointShadowIndices = new();
        _cachedAllPointLights.Clear();
        vec3 camPos = renderCamera.Position;

        SceneRenderMode renderMode = _viewportRenderMode == ViewportRenderMode.Rendered
            ? SceneRenderMode.Rendered
            : SceneRenderMode.Unrendered;
        bool forceUnshaded = _viewportRenderMode == ViewportRenderMode.FlatUnshaded ||
                             _viewportRenderMode == ViewportRenderMode.Wireframe;
        bool wireframeMode = _viewportRenderMode == ViewportRenderMode.Wireframe;
        bool useWorldGi = renderMode == SceneRenderMode.Rendered &&
                  PropertiesPanel?.IndirectLightingEnabled == true &&
                  string.Equals(PropertiesPanel.GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase);
        bool useScreenSpaceGi = renderMode == SceneRenderMode.Rendered &&
                    PropertiesPanel?.IndirectLightingEnabled == true &&
                    !string.Equals(PropertiesPanel.GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase);
        Mesh.IndirectWorldEnabled = useWorldGi || useScreenSpaceGi;
        Mesh.IndirectWorldStrength = Math.Clamp(PropertiesPanel?.IndirectLightingStrength ?? 1f, 0f, 4f);
        if (renderMode == SceneRenderMode.Rendered)
        {
            Mesh.ShadowBlurStrength = Math.Clamp(PropertiesPanel?.ShadowBlurStrength ?? 1f, 0f, 4f);
            Mesh.SssEnabled = PropertiesPanel?.SubsurfaceEnabled == true;
            Mesh.SssBlurSamples = Math.Clamp(PropertiesPanel?.SubsurfaceBlurSamples ?? 8, 0, 32);
            Mesh.SssStrength = Math.Clamp(PropertiesPanel?.SubsurfaceStrength ?? 1f, 0f, 4f);
            Mesh.SssDesaturation = Math.Clamp(PropertiesPanel?.SubsurfaceDesaturation ?? 0f, 0f, 1f);
            Mesh.SssColorThreshold = Math.Clamp(PropertiesPanel?.SubsurfaceColorThreshold ?? 0f, 0f, 1f);
            float[] sssRadius = PropertiesPanel?.SubsurfaceRadiusRgb ?? [0.42f, 0.24f, 0.14f];
            Mesh.SssRadius = new vec3(
                Math.Clamp(sssRadius[0], 0.0001f, 8f),
                Math.Clamp(sssRadius[1], 0.0001f, 8f),
                Math.Clamp(sssRadius[2], 0.0001f, 8f));
            Mesh.SssHighlightSize = Math.Clamp(PropertiesPanel?.SubsurfaceHighlightSize ?? 1f, 0f, 8f);
            Mesh.SssHighlightStrength = Math.Clamp(PropertiesPanel?.SubsurfaceHighlightStrength ?? 1f, 0f, 8f);
            Mesh.SssHighlightSharpness = Math.Clamp(PropertiesPanel?.SubsurfaceHighlightSharpness ?? 2f, 0.01f, 16f);
            Mesh.SssHighlightDesaturation = Math.Clamp(PropertiesPanel?.SubsurfaceHighlightDesaturation ?? 0f, 0f, 1f);
            Mesh.SssHighlightColorThreshold = Math.Clamp(PropertiesPanel?.SubsurfaceHighlightColorThreshold ?? 0f, 0f, 1f);
            Mesh.SssAbsorption = Math.Clamp(PropertiesPanel?.SubsurfaceAbsorption ?? 0.35f, -0.95f, 0.95f);
            Mesh.ShadowDebugMode = _renderedPassMode == RenderedPassMode.Shadow ? 1 : 0;
            if (Mesh.DirectionalShadowEnabled)
                RenderShadowMap();
            if (globalShadows)
                pointShadowIndices = RenderPointShadowMaps(camPos);
        }

        CollectPointLights(SceneObjects, renderMode, pointShadowIndices, useScreenSpaceGi, _cachedAllPointLights);

        // ── Upload scene UBO ──────────────────────────────────────────────────
        if (_sceneUBO != null)
        {
            float fillStrength   = Math.Clamp(Mesh.GlobalFillLightStrength, 0f, 5f);
            var   fillColor      = new vec3(
                Math.Clamp(Mesh.GlobalFillLightColor.x, 0f, 1f),
                Math.Clamp(Mesh.GlobalFillLightColor.y, 0f, 1f),
                Math.Clamp(Mesh.GlobalFillLightColor.z, 0f, 1f));
            float sunStrength    = Math.Clamp(Mesh.GlobalSunFillLightStrength, 0f, 5f);
            float moonStrength   = Math.Clamp(Mesh.GlobalMoonFillLightStrength, 0f, 5f);
            float ambientStrength= Math.Clamp(Mesh.GlobalAmbientStrength, 0f, 5f);
            var   ambientColor   = new vec3(
                Math.Clamp(Mesh.GlobalAmbientColor.x, 0f, 1f),
                Math.Clamp(Mesh.GlobalAmbientColor.y, 0f, 1f),
                Math.Clamp(Mesh.GlobalAmbientColor.z, 0f, 1f));

            bool mainShadows  = Mesh.MainFillLightCastsShadows && !Mesh.SunFillLightCastsShadows && !Mesh.MoonFillLightCastsShadows;
            bool sunShadows   = Mesh.SunFillLightCastsShadows && !Mesh.MoonFillLightCastsShadows;
            bool moonShadows  = Mesh.MoonFillLightCastsShadows;

            _sceneUBO.Upload(
                Mesh.ShadowLightSpaceMatrix,
                ambientColor * ambientStrength,
                new vec3(1f, 1f, 1f).Normalized,
                fillColor * fillStrength,
                Mesh.GlobalSunFillLightDirection,
                Mesh.GlobalSunFillLightColor * sunStrength,
                Mesh.GlobalMoonFillLightDirection,
                Mesh.GlobalMoonFillLightColor * moonStrength,
                Mesh.ShadowDebugMode,
                mainShadows,
                sunShadows,
                moonShadows);
        }

        // ── Ground plane ──────────────────────────────────────────────────────
        if (wireframeMode)
            Gl.PolygonMode(GLEnum.FrontAndBack, PolygonMode.Line);

        if (_groundPlane != null && GroundPlaneVisible)
        {
            SelectPointLightsForMesh(vec3.Zero, _cachedAllPointLights);
            RenderMeshWithModeOverride(_groundPlane, _groundPlaneModel, view, proj, forceUnshaded);
        }

        // ── Scene objects ─────────────────────────────────────────────────────
        // Split meshes into three buckets:
        //   opaque       – Alpha == 1, no texture  → normal depth read+write
        //   textured     – has a TextureId          → depth pre-pass + color pass (LEQUAL)
        //   alphaBlend   – Alpha < 1, no texture    → straight back-to-front blend, no pre-pass
        _cachedRenderItems.Clear();
        _cachedRenderItemsNoAo.Clear();
        _cachedOverlays.Clear();

        CollectRenderPairs(SceneObjects, camPos, renderMode, frustum, _cachedRenderItems, _cachedRenderItemsNoAo, _cachedOverlays);

        bool splitAoObjects = renderMode == SceneRenderMode.Rendered &&
                              _ambientOcclusionShader != null &&
                              PropertiesPanel?.AmbientOcclusionEnabled == true;

        if (splitAoObjects)
        {
            RenderDepthOrdered(view, proj, _cachedRenderItems, _cachedAllPointLights, forceUnshaded);
        }
        else
        {
            _cachedRenderItems.AddRange(_cachedRenderItemsNoAo);
            RenderDepthOrdered(view, proj, _cachedRenderItems, _cachedAllPointLights, forceUnshaded);
        }
        if (wireframeMode)
            Gl.PolygonMode(GLEnum.FrontAndBack, PolygonMode.Fill);

        if (renderMode == SceneRenderMode.Rendered)
            RenderAmbientOcclusion(_fbo, _depthTex, w, h, renderNear, renderFar, PropertiesPanel, _renderedPassMode);

        if (splitAoObjects && _renderedPassMode != RenderedPassMode.AmbientOcclusion)
            RenderDepthOrdered(view, proj, _cachedRenderItemsNoAo, _cachedAllPointLights, forceUnshaded);

        if (renderMode == SceneRenderMode.Rendered)
            ApplyIndirectLightingPassGeneric(_fbo, _colorTex, _depthTex, _indirectFbo, _indirectTex, _indirectPingFbo, _indirectPingTex,
                w, h, renderNear, renderFar, PropertiesPanel, _renderedPassMode);

        if (renderMode == SceneRenderMode.Rendered)
            ApplyGlowPassGeneric(_fbo, _colorTex, _glowFbo, _glowTex, _glowPingFbo, _glowPingTex, w, h, PropertiesPanel, _renderedPassMode);

        if (renderMode == SceneRenderMode.Rendered && _renderedPassMode == RenderedPassMode.Combined)
            ApplyFilmGrainPass(_fbo, _colorTex, _glowFbo, _glowTex, w, h, activeCamObj);

        if (OverlaysEnabled)
        {
            // ── Object-mesh overlays (e.g. camera icon) ───────────────────────────
            // Rendered with depth-test off so they always appear on top.
            // Mesh.Render() handles the DepthTestDisabled / Unlit flags internally.
            foreach (var (model, mesh) in _cachedOverlays)
            {
                SelectPointLightsForMesh(new vec3(model.m30, model.m31, model.m32), _cachedAllPointLights);
                mesh.Render(model, view, proj);
            }

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

            // ── Spot-light coverage indicators ─────────────────────────────────────
            // Drawn after the range ring so the cone / stick stack on top.  Shown
            // in both rendered and unrendered modes so the user can always see
            // the spot light's aim / coverage; the billboard for spot lights is
            // skipped below to avoid visual clutter.
            Gl.Enable(GLEnum.DepthTest);
            Gl.DepthFunc(GLEnum.Less);
            Gl.Enable(GLEnum.CullFace);
            Gl.CullFace(GLEnum.Back);
            RenderSpotLightIndicators(view, proj);

            // Selected particle spawner emission bounds (wireframe box / ellipsoid).
            RenderParticleSpawnerEmissionShapes(view, proj);

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
        Mesh.IndirectWorldEnabled = false;
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

        // FPS badge, only in rendered (high-quality) mode, in a corner the
        // preview viewport isn't already occupying.
        if (HighQualityPreviewEnabled)
            RenderFpsOverlay(imageMin, imageSize, PickFpsCornerAvoidingPreview());

        ImGui.EndChild();
        ImGui.End();
    }

    /// <summary>
    /// Draws a small framed "FPS: nn" badge in the requested corner of the
    /// given image area. Used to indicate performance in whichever viewport
    /// is currently in rendered (high-quality) mode.
    /// </summary>
    public void RenderFpsOverlay(Vector2 imageMin, Vector2 imageSize, Corner corner)
    {
        if (imageSize.X < 1f || imageSize.Y < 1f) return;

        float fps = ImGui.GetIO().Framerate;
        if (fps <= 0f) return;

        string label = $"FPS: {fps:0}";
        Vector2 textSize = ImGui.CalcTextSize(label);

        const float margin = 10f;
        const float padX = 10f;
        const float padY = 6f;

        Vector2 boxSize = new(textSize.X + padX * 2f, textSize.Y + padY * 2f);

        float boxX = corner switch
        {
            Corner.BottomLeft or Corner.TopLeft => imageMin.X + margin,
            _ => imageMin.X + imageSize.X - boxSize.X - margin,
        };
        float boxY = corner switch
        {
            Corner.TopLeft or Corner.TopRight => imageMin.Y + margin,
            _ => imageMin.Y + imageSize.Y - boxSize.Y - margin,
        };

        Vector2 boxMin = new(boxX, boxY);
        Vector2 boxMax = new(boxMin.X + boxSize.X, boxMin.Y + boxSize.Y);
        Vector2 textPos = new(boxMin.X + padX, boxMin.Y + padY);

        var fg = ImGui.GetForegroundDrawList();
        uint bgColor     = ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.05f, 0.05f, 0.86f));
        uint borderColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.45f, 0.75f, 0.95f, 0.95f));
        uint textColor   = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));

        fg.AddRectFilled(boxMin, boxMax, bgColor, 6f);
        fg.AddRect(boxMin, boxMax, borderColor, 6f);
        fg.AddText(textPos, textColor, label);
    }

    /// <summary>
    /// Picks a bottom corner for the main viewport's FPS overlay, avoiding
    /// the corner currently occupied by the inline preview viewport.
    /// </summary>
    private Corner PickFpsCornerAvoidingPreview()
    {
        if (PreviewViewport?.IsInlineVisible == true)
        {
            return PreviewViewport.InlineCorner switch
            {
                Corner.BottomRight => Corner.BottomLeft,
                Corner.BottomLeft  => Corner.BottomRight,
                _ => Corner.BottomRight,
            };
        }

        return Corner.BottomRight;
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
        _shadowMapSize = (uint)Math.Clamp(PropertiesPanel?.SunShadowBufferSize ?? 2048, 256, 8192);
        if (Gl == null)
            return;
        if (_shadowShader != null)
        {
            if (_allocatedShadowMapSize != _shadowMapSize)
            {
                Gl.BindTexture(GLEnum.Texture2D, _shadowTex);
                Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.DepthComponent32f, _shadowMapSize,
                    _shadowMapSize, 0, PixelFormat.DepthComponent, GLEnum.Float, null);
                Gl.BindTexture(GLEnum.Texture2D, 0);
                _allocatedShadowMapSize = _shadowMapSize;
            }
            return;
        }

        _shadowShader = new Shader(Gl);
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
        _allocatedShadowMapSize = _shadowMapSize;
    }

    private unsafe void EnsurePointShadowResources()
    {
        if (Gl == null || _pointShadowShader != null)
            return;

        _pointShadowShader = new Shader(Gl);
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
                    1024,
                    1024,
                    0,
                    PixelFormat.DepthComponent,
                    GLEnum.Float,
                    null);
            }

            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
            _pointShadowTextureSizes[i] = 1024;
        }

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.TextureCubeMap, 0);
    }

    private unsafe void EnsurePreviewShadowResources()
    {
        if (Gl == null || !IsPreviewViewport || _previewShadowShader != null)
            return;

        _previewShadowShader = new Shader(Gl);
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
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
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

        _previewPointShadowShader = new Shader(Gl);
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
                    1024,
                    1024,
                    0,
                    PixelFormat.DepthComponent,
                    GLEnum.Float,
                    null);
            }

            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            Gl.TexParameter(GLEnum.TextureCubeMap, GLEnum.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        }

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        Gl.BindTexture(GLEnum.TextureCubeMap, 0);
    }

    private void RenderShadowMap()
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
            _groundPlane.RenderShadow(_shadowShader, lightViewProj, _groundPlaneModel);

        RenderShadowCasters(SceneObjects, lightViewProj, _shadowShader!, SceneRenderMode.Rendered);

        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        Gl.Viewport(0, 0, _viewportWidth, _viewportHeight);
    }

    private Dictionary<LightSceneObject, int> RenderPointShadowMaps(vec3 cameraPos)
    {
        Dictionary<LightSceneObject, int> shadowIndices = new();
        if (Gl == null)
            return shadowIndices;

        EnsurePointShadowResources();
        if (_pointShadowShader == null || _pointShadowFbo == 0)
            return shadowIndices;

        List<(LightSceneObject Light, vec3 Position, float Range)> shadowLights = [];
        CollectPointShadowCasters(SceneObjects, SceneRenderMode.Rendered, shadowLights);

        // When there are more shadow-enabled lights than cubemap slots, give
        // the slots to the lights closest to the camera instead of whichever
        // came first in scene-tree order, so nearby shadows never "randomly"
        // disappear just because a duplicate happened to land earlier in the
        // hierarchy (same closest-wins principle as SelectPointLightsForMesh).
        if (shadowLights.Count > MaxPointShadowLights)
            shadowLights.Sort((a, b) => (a.Position - cameraPos).LengthSqr.CompareTo((b.Position - cameraPos).LengthSqr));

        int lightCount = Math.Min(shadowLights.Count, MaxPointShadowLights);
        Array.Clear(Mesh.PointShadowCubeTextures, 0, Mesh.PointShadowCubeTextures.Length);

        for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
        {
            var (light, position, range) = shadowLights[lightIndex];
            int shadowSize = light.Type == LightType.Spot
                ? PropertiesPanel?.SpotShadowBufferSize ?? 1024
                : PropertiesPanel?.PointShadowBufferSize ?? 1024;
            ResizePointShadowTexture(lightIndex, shadowSize);
            shadowIndices[light] = lightIndex;
            Mesh.PointShadowCubeTextures[lightIndex] = _pointShadowCubeTextures[lightIndex];

            float farPlane = Math.Max(range, 0.5f);
            mat4 projection = mat4.Perspective(glm.Radians(90f), 1f, 0.05f, farPlane);

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
                Gl.Viewport(0, 0, (uint)shadowSize, (uint)shadowSize);
                Gl.Clear(ClearBufferMask.DepthBufferBit);
                Gl.Enable(GLEnum.DepthTest);
                Gl.Disable(GLEnum.CullFace);

                if (_groundPlane != null && GroundPlaneVisible)
                    _groundPlane.RenderPointShadow(_pointShadowShader, lightViewProj, _groundPlaneModel, position, farPlane);

                RenderPointShadowCasters(SceneObjects, lightViewProj, position, farPlane, _pointShadowShader!, SceneRenderMode.Rendered);
            }
        }

        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _fbo);
        Gl.Viewport(0, 0, _viewportWidth, _viewportHeight);
        return shadowIndices;
    }

    private unsafe void ResizePointShadowTexture(int index, int requestedSize)
    {
        int size = Math.Clamp(requestedSize, 256, 8192);
        if (Gl == null || _pointShadowTextureSizes[index] == size)
            return;
        Gl.BindTexture(GLEnum.TextureCubeMap, _pointShadowCubeTextures[index]);
        for (int face = 0; face < 6; face++)
            Gl.TexImage2D(GLEnum.TextureCubeMapPositiveX + face, 0, InternalFormat.DepthComponent32f,
                (uint)size, (uint)size, 0, PixelFormat.DepthComponent, GLEnum.Float, null);
        Gl.BindTexture(GLEnum.TextureCubeMap, 0);
        _pointShadowTextureSizes[index] = size;
    }

    private mat4 ComputeShadowLightSpaceMatrix()
    {
        var bounds = new SceneShadowBounds();
        CollectShadowBounds(SceneObjects, ref bounds, SceneRenderMode.Rendered);

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
        vec3 lightDir = Mesh.MoonFillLightCastsShadows
            ? Mesh.GlobalMoonFillLightDirection
            : Mesh.SunFillLightCastsShadows
                ? Mesh.GlobalSunFillLightDirection
                : new vec3(1f, 1f, 1f).Normalized;
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

    private void RenderShadowCasters(IEnumerable<SceneObject> objects, mat4 lightViewProj, Shader shadowShader, SceneRenderMode renderMode, Dictionary<string, BoneSceneObject>? boneDict = null)
    {
        Frustum shadowFrustum = Frustum.FromViewProj(lightViewProj);

        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility())
                continue;
            if (!ShouldRenderInQuality(obj, renderMode))
                continue;

            mat4 model = obj.GetWorldMatrix();
            var localBoneDict = boneDict ?? BuildBoneDictionary(obj);

            // Respect the per-object "Cast Shadow" toggle: objects with it
            // disabled still render normally but are skipped from the
            // directional (sun) shadow-map pass.
            if (obj.CastShadow)
            {
                foreach (var mesh in obj.Visuals.Where(mesh => !mesh.PickOnly && !mesh.DepthTestDisabled))
                {
                    mesh.CullFrontFaces = obj.GetEffectiveInvertFaces();

                    // Frustum cull against the shadow light frustum.
                    vec3 worldCenter = new vec3((model * new vec4(mesh.BoundingSphereCenter, 1f)).xyz);
                    float scale = MathF.Max(
                        new vec3(model.m00, model.m10, model.m20).Length,
                        MathF.Max(
                            new vec3(model.m01, model.m11, model.m21).Length,
                            new vec3(model.m02, model.m12, model.m22).Length));
                    float worldRadius = mesh.BoundingSphereRadius * scale;
                    if (!shadowFrustum.TestSphere(worldCenter, worldRadius))
                        continue;

                    if (mesh.IsSkinned)
                        UpdateBoneMatrices(mesh, model, localBoneDict);

                    mesh.RenderShadow(shadowShader, lightViewProj, model);
                }
            }

            RenderShadowCasters(obj.Children, lightViewProj, shadowShader, renderMode, localBoneDict);
        }
    }

    private static void CollectShadowBounds(IEnumerable<SceneObject> objects, ref SceneShadowBounds bounds, SceneRenderMode renderMode)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility())
                continue;
            if (!ShouldRenderInQuality(obj, renderMode))
                continue;

            mat4 world = obj.GetWorldMatrix();
            bool includedMeshVertex = false;
            foreach (var mesh in obj.Visuals.Where(m => !m.PickOnly && !m.DepthTestDisabled && m.Vertices.Count != 0))
            {
                includedMeshVertex = true;
                // Use the local bounding sphere to compute world-space extents,
                // avoiding per-vertex iteration over every mesh.
                vec3 worldCenter = new vec3((world * new vec4(mesh.BoundingSphereCenter, 1f)).xyz);
                float scale = MathF.Max(
                    new vec3(world.m00, world.m10, world.m20).Length,
                    MathF.Max(
                        new vec3(world.m01, world.m11, world.m21).Length,
                        new vec3(world.m02, world.m12, world.m22).Length));
                float worldRadius = mesh.BoundingSphereRadius * scale;
                bounds.Include(worldCenter - new vec3(worldRadius));
                bounds.Include(worldCenter + new vec3(worldRadius));
            }

            if (!includedMeshVertex)
                bounds.Include(new vec3(world.m30, world.m31, world.m32));

            CollectShadowBounds(obj.Children, ref bounds, renderMode);
        }
    }

    private void RenderPointShadowCasters(IEnumerable<SceneObject> objects, mat4 lightViewProj, vec3 lightPos, float farPlane, Shader pointShadowShader, SceneRenderMode renderMode, Dictionary<string, BoneSceneObject>? boneDict = null)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility())
                continue;
            if (!ShouldRenderInQuality(obj, renderMode))
                continue;

            mat4 model = obj.GetWorldMatrix();
            var localBoneDict = boneDict;
            if (localBoneDict == null)
                localBoneDict = BuildBoneDictionary(obj);

            // Respect the per-object "Cast Shadow" toggle for point/spot
            // light shadow cubemaps too — previously this flag was only
            // ever read by the (now identical) directional-shadow pass'
            // stub, so it was silently ignored everywhere and every visible
            // mesh always cast point/spot shadows regardless of the setting.
            if (obj.CastShadow)
            {
                foreach (var mesh in obj.Visuals.Where(mesh => mesh is { PickOnly: false, DepthTestDisabled: false }))
                {
                    mesh.CullFrontFaces = obj.GetEffectiveInvertFaces();

                    // Distance cull: skip meshes outside the light's range.
                    vec3 worldCenter = new vec3((model * new vec4(mesh.BoundingSphereCenter, 1f)).xyz);
                    float distToLight = (worldCenter - lightPos).Length;
                    float scale = MathF.Max(
                        new vec3(model.m00, model.m10, model.m20).Length,
                        MathF.Max(
                            new vec3(model.m01, model.m11, model.m21).Length,
                            new vec3(model.m02, model.m12, model.m22).Length));
                    float worldRadius = mesh.BoundingSphereRadius * scale;
                    if (distToLight - worldRadius > farPlane)
                        continue;

                    if (mesh.IsSkinned)
                        UpdateBoneMatrices(mesh, model, localBoneDict);

                    mesh.RenderPointShadow(pointShadowShader, lightViewProj, model, lightPos, farPlane);
                }
            }

            RenderPointShadowCasters(obj.Children, lightViewProj, lightPos, farPlane, pointShadowShader, renderMode, localBoneDict);
        }
    }

    private static void CollectPointShadowCasters(IEnumerable<SceneObject> objects, SceneRenderMode renderMode, List<(LightSceneObject Light, vec3 Position, float Range)> result)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility())
                continue;
            if (!ShouldRenderInQuality(obj, renderMode))
                continue;

            if (obj is LightSceneObject light && light.LightShadowEnabled)
            {
                mat4 world = obj.GetWorldMatrix();
                result.Add((light, new vec3(world.m30, world.m31, world.m32), Math.Max(light.LightRange, 0.5f)));
            }

            CollectPointShadowCasters(obj.Children, renderMode, result);
        }
    }

    /// <summary>
    /// Public method for preview viewport to render directional shadow map.
    /// Renders shadows into the preview viewport's own shadow framebuffer.
    /// </summary>
    public void RenderShadowMapPublic()
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
            MainViewport.GroundPlane.RenderShadow(_previewShadowShader, lightViewProj, MainViewport.GroundPlaneModel);

        RenderShadowCasters(MainViewport?.SceneObjects ?? [], lightViewProj, _previewShadowShader!, SceneRenderMode.Rendered);


        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.BindFramebuffer(GLEnum.Framebuffer, _previewFbo);
        Gl.Viewport(0, 0, _previewWidth, _previewHeight);
    }

    /// <summary>
    /// Public method for preview viewport to render point shadow maps.
    /// Renders shadows into the preview viewport's own point shadow framebuffer.
    /// </summary>
    public Dictionary<LightSceneObject, int> RenderPointShadowMapsPublic(vec3 cameraPos)
    {
        Dictionary<LightSceneObject, int> shadowIndices = new();
        if (Gl == null || !IsPreviewViewport)
            return shadowIndices;

        EnsurePreviewPointShadowResources();
        if (_previewPointShadowShader == null || _previewPointShadowFbo == 0)
            return shadowIndices;

        List<(LightSceneObject Light, vec3 Position, float Range)> shadowLights = [];
        CollectPointShadowCasters(MainViewport?.SceneObjects ?? [], SceneRenderMode.Rendered, shadowLights);

        // Same closest-wins prioritization as the main viewport's shadow pass.
        if (shadowLights.Count > MaxPointShadowLights)
            shadowLights.Sort((a, b) => (a.Position - cameraPos).LengthSqr.CompareTo((b.Position - cameraPos).LengthSqr));

        int lightCount = Math.Min(shadowLights.Count, MaxPointShadowLights);
        Array.Clear(Mesh.PointShadowCubeTextures, 0, Mesh.PointShadowCubeTextures.Length);

        for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
        {
            var (light, position, range) = shadowLights[lightIndex];
            shadowIndices[light] = lightIndex;
            Mesh.PointShadowCubeTextures[lightIndex] = _previewPointShadowCubeTextures[lightIndex];

            float farPlane = Math.Max(range, 0.5f);
            mat4 projection = mat4.Perspective(glm.Radians(90f), 1f, 0.05f, farPlane);

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
                Gl.Viewport(0, 0, 1024, 1024);
                Gl.Clear(ClearBufferMask.DepthBufferBit);
                Gl.Enable(GLEnum.DepthTest);
                Gl.Disable(GLEnum.CullFace);

                if (MainViewport?.GroundPlane != null && MainViewport.GroundPlaneVisible)
                    MainViewport.GroundPlane.RenderPointShadow(_previewPointShadowShader, lightViewProj, MainViewport.GroundPlaneModel, position, farPlane);

                RenderPointShadowCasters(MainViewport?.SceneObjects ?? [], lightViewProj, position, farPlane, _previewPointShadowShader!, SceneRenderMode.Rendered);
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
    private void ExecutePendingPick(Vector2 imageMin, Vector2 imageSize)
    {
        var (pickCamera, pickCamObj) = GetActiveRenderCamera();
        var preparedPickCamera = BuildPreparedCamera(pickCamera, pickCamObj);
        float aspect = (_viewportHeight > 0) ? (float)_viewportWidth / _viewportHeight : 1f;
        mat4 view = preparedPickCamera.GetViewMatrix();
        mat4 proj = preparedPickCamera.GetProjectionMatrix(aspect);

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
    /// Skinned meshes are deformed using the current bone palette so picking
    /// matches the rendered pose.
    /// </summary>
    private unsafe void RenderPickObjects(
        IEnumerable<SceneObject> objects,
        mat4 view,
        mat4 proj,
        Dictionary<string, BoneSceneObject>? boneDict = null)
    {
        if (_pickShader == null) return;

        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility()) continue;

            var localBoneDict = boneDict;
            if (localBoneDict == null)
                localBoneDict = BuildBoneDictionary(obj);

            bool hasBoneIndicator = obj is BoneSceneObject b && b.IndicatorMesh != null && ShouldShowBoneIndicator(b);
            if (obj.IsSelectable && (obj.Visuals.Count > 0 || hasBoneIndicator))
            {
                mat4 model = GetRenderableWorldMatrix(obj, Mesh.GlobalCameraPosition);
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
                {
                    if (mesh.IsSkinned && localBoneDict != null)
                        UpdateBoneMatrices(mesh, model, localBoneDict);
                    mesh.ApplySkinningUniforms(_pickShader);
                    mesh.RenderPickPass(_pickShader);
                }

                // Draw the bone indicator octahedron as an additional pick target.
                if (obj is BoneSceneObject bone && bone.IndicatorMesh != null && ShouldShowBoneIndicator(bone))
                {
                    bone.IndicatorMesh.ApplySkinningUniforms(_pickShader);
                    bone.IndicatorMesh.RenderPickPass(_pickShader);
                }
            }

            // Recurse into children.
            RenderPickObjects(obj.Children, view, proj, localBoneDict);
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
    private void RenderSilhouettePass(mat4 view, mat4 proj)
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

        // Outline the full selected hierarchy: each selected object plus all descendants.
        var outlineObjects = new List<SceneObject>();
        var outlineVisited = new HashSet<SceneObject>();
        foreach (var selected in sm.SelectedObjects)
            CollectObjectAndDescendants(selected, outlineObjects, outlineVisited);

        var boneDictCache = new Dictionary<SceneObject, Dictionary<string, BoneSceneObject>>();

        foreach (var obj in outlineObjects)
        {
            if (!obj.GetEffectiveVisibility()) continue;
            if (obj.Visuals.Count == 0) continue;

            mat4 model = GetRenderableWorldMatrix(obj, Mesh.GlobalCameraPosition);
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

            // Build a bone dictionary for the top-level hierarchy that contains
            // the selected object, caching by root to avoid recomputation.
            var root = obj;
            while (root.Parent != null)
                root = root.Parent;

            if (!boneDictCache.TryGetValue(root, out var boneDict))
            {
                boneDict = BuildBoneDictionary(root);
                boneDictCache[root] = boneDict;
            }

            foreach (var mesh in obj.Visuals)
            {
                if (mesh.IsSkinned && boneDict != null)
                    UpdateBoneMatrices(mesh, model, boneDict);
                mesh.ApplySkinningUniforms(_silhouetteShader);
                mesh.RenderPickPass(_silhouetteShader);
            }
        }

        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
    }

    private static void CollectObjectAndDescendants(SceneObject root, List<SceneObject> output, HashSet<SceneObject> visited)
    {
        if (!visited.Add(root))
            return;

        output.Add(root);

        foreach (var child in root.Children)
            CollectObjectAndDescendants(child, output, visited);
    }

    /// <summary>
    /// Pass 2 of the Sobel outline effect.
    ///
    /// Runs a full-screen Sobel gradient filter over the supplied silhouette
    /// mask and blends the resulting edge pixels on top of the supplied display
    /// framebuffer using <see cref="EdgeColor"/>.  Shared by the main and
    /// preview viewports.
    /// </summary>
    private void RenderEdgePass()
    {
        RenderEdgePassGeneric(_fbo, _silhouetteTex, _viewportWidth, _viewportHeight);
    }

    private void RenderEdgePassGeneric(uint displayFbo, uint silhouetteTex, uint w, uint h)
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
    /// <see cref="LightSceneObject"/> to <paramref name="result"/> — the full,
    /// unfiltered list of every light in the scene this frame. This is no
    /// longer written directly into <see cref="Mesh.PointLights"/>: that field
    /// is now a small per-draw-call scratch buffer that
    /// <see cref="SelectPointLightsForMesh"/> repopulates before every
    /// individual mesh render, picking only the lights most relevant to that
    /// mesh (see remarks there for why).
    /// For spot lights the light's world-space forward direction is computed
    /// by rotating the local -Z axis by the world rotation; for point lights
    /// the direction is (0,0,0) and the spot-cosines are 0, which the shader
    /// interprets as "no cone test".
    /// </summary>
    private static void CollectPointLights(
        IEnumerable<SceneObject> objects,
        SceneRenderMode renderMode,
        Dictionary<LightSceneObject, int> shadowIndices,
        bool screenSpaceIndirectFallback,
        List<(vec3 pos, vec3 color, float range, float energy, int shadowIndex, vec3 direction, float indirectEnergy, float spotCosOuterAngle, float spotCosInnerAngle)> result)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility()) continue;
            if (!ShouldRenderInQuality(obj, renderMode)) continue;

            if (obj is LightSceneObject light)
            {
                mat4 world = obj.GetWorldMatrix();
                var pos    = new vec3(world.m30, world.m31, world.m32);
                var col    = new vec3(light.LightColor.x, light.LightColor.y, light.LightColor.z);
                int shadowIndex = shadowIndices.TryGetValue(light, out int index) ? index : -1;

                vec3  dir          = vec3.Zero;
                float spotOuterCos = 0f;
                float spotInnerCos = 0f;

                if (light.Type == LightType.Spot)
                {
                    // Use the same world matrix multiplication that the cone/stick
                    // use so the lighting direction exactly matches the visual.
                    // The mesh is built along local +Z, so the world-space aim is
                    // world * (0,0,1,0) (ignoring the translation column).
                    vec4 worldForwardH = world * new vec4(0f, 0f, 1f, 0f);
                    vec3 worldDir = new vec3(worldForwardH.x, worldForwardH.y, worldForwardH.z);
                    float len = worldDir.Length;
                    dir = len > 0.0001f ? worldDir / len : new vec3(0f, 0f, 1f);

                    // LightSpotAngle is the FULL cone angle in degrees; the
                    // shader uses cosine of the half-angle.  Blend is the
                    // soft-edge band in degrees, also relative to the half-angle.
                    float halfAngleDeg = Math.Clamp(light.LightSpotAngle * 0.5f, 0f, 89.9f);
                    float blendDeg     = Math.Clamp(light.LightSpotBlend, 0f, halfAngleDeg);
                    float innerDeg     = halfAngleDeg + blendDeg;   // fully off outside this
                    spotOuterCos = MathF.Cos(glm.Radians(halfAngleDeg));
                    spotInnerCos = MathF.Cos(glm.Radians(Math.Min(innerDeg, 89.9f)));
                }

                result.Add((
                    pos, col, light.LightRange, light.LightEnergy, shadowIndex,
                    dir, screenSpaceIndirectFallback ? 0f : light.LightIndirectEnergy,
                    spotOuterCos, spotInnerCos));
            }

            AddEmissivePointLightsForObject(obj, screenSpaceIndirectFallback, result);

            CollectPointLights(obj.Children, renderMode, shadowIndices, screenSpaceIndirectFallback, result);
        }
    }

    /// <summary>
    /// Converts emissive mesh materials into lightweight local point lights so
    /// emissive blocks (for example glowstone) can illuminate nearby surfaces.
    /// These pseudo-lights are unshadowed and use a bounded range derived from
    /// mesh size and emissive energy.
    /// </summary>
    private static void AddEmissivePointLightsForObject(
        SceneObject obj,
        bool screenSpaceIndirectFallback,
        List<(vec3 pos, vec3 color, float range, float energy, int shadowIndex, vec3 direction, float indirectEnergy, float spotCosOuterAngle, float spotCosInnerAngle)> result)
    {
        if (obj.Visuals.Count == 0)
            return;

        mat4 world = obj.GetWorldMatrix();
        float worldScale = MathF.Max(
            new vec3(world.m00, world.m10, world.m20).Length,
            MathF.Max(
                new vec3(world.m01, world.m11, world.m21).Length,
                new vec3(world.m02, world.m12, world.m22).Length));

        foreach (var mesh in obj.Visuals)
        {
            if (mesh.PickOnly || mesh.Vertices.Count == 0)
                continue;
            if (!mesh.EmissionEnabled)
                continue;

            float emissionEnergy = MathF.Max(mesh.EmissionEnergy, 0f);
            vec3 emissionColor = new vec3(
                MathF.Max(mesh.EmissionColor.x, 0f),
                MathF.Max(mesh.EmissionColor.y, 0f),
                MathF.Max(mesh.EmissionColor.z, 0f));

            float colorLuma = MathF.Max(emissionColor.x, MathF.Max(emissionColor.y, emissionColor.z));
            if (emissionEnergy <= 0.0001f || colorLuma <= 0.0001f)
                continue;

            vec3 localCenter = mesh.BoundingSphereCenter;
            vec3 worldCenter = new vec3((world * new vec4(localCenter, 1f)).xyz);
            float worldRadius = MathF.Max(mesh.BoundingSphereRadius * worldScale, 0.15f);

            // Keep emissive influence local and stable: scale mostly by object
            // size and emission amount, with a hard upper clamp.
            float lightRange = Math.Clamp((worldRadius * 3.5f) + (emissionEnergy * 2.0f), 0.5f, 40f);
            float lightEnergy = mesh.EmissionIndirectOnly
                ? 0f
                : Math.Clamp(emissionEnergy * 0.6f, 0f, 10f);
            float indirectEnergy = screenSpaceIndirectFallback
                ? (mesh.EmissionIndirectOnly ? Math.Clamp(emissionEnergy, 0f, 16f) : 0f)
                : Math.Clamp(emissionEnergy, 0f, 16f);

            result.Add((
                worldCenter,
                emissionColor,
                lightRange,
                lightEnergy,
                -1,
                vec3.Zero,
                indirectEnergy,
                0f,
                0f));
        }
    }

    private static bool ShouldRenderInQuality(SceneObject obj, SceneRenderMode renderMode)
    {
        return renderMode == SceneRenderMode.Rendered
            ? obj.RenderInHighQuality
            : obj.RenderInLowQuality;
    }

    // ── Reusable per-frame lists (reduces allocations) ────────────────────────

    private readonly List<(vec3 pos, vec3 color, float range, float energy, int shadowIndex, vec3 direction, float indirectEnergy, float spotCosOuterAngle, float spotCosInnerAngle)> _cachedAllPointLights = new();
    private readonly List<(mat4 model, Mesh mesh, float sortDepth, bool includeFog, bool blurTexture, bool textureMipmaps)> _cachedRenderItems = new();
    private readonly List<(mat4 model, Mesh mesh, float sortDepth, bool includeFog, bool blurTexture, bool textureMipmaps)> _cachedRenderItemsNoAo = new();
    private readonly List<(mat4 model, Mesh mesh)> _cachedOverlays = new();

    /// <summary>
    /// Maximum number of point/spot lights selected per individual mesh draw
    /// call. Kept comfortably below the shader's absolute <c>MAX_POINT_LIGHTS</c>
    /// (32, see simple.frag) so there is headroom.
    /// </summary>
    private const int MaxLightsPerMesh = 16;

    /// <summary>
    /// Scratch buffer reused every call to avoid a per-mesh, per-frame
    /// allocation while scoring candidate lights.
    /// </summary>
    private static readonly List<(float score, int index)> _lightScoreScratch = new();

    /// <summary>
    /// Picks the <see cref="MaxLightsPerMesh"/> lights most relevant to a mesh
    /// centred at <paramref name="meshWorldPos"/> out of every light in the
    /// scene (<paramref name="allLights"/>) and writes them into
    /// <see cref="Mesh.PointLights"/> — the small per-draw-call buffer that
    /// <see cref="Mesh.Render"/> reads uniforms from — immediately before that
    /// mesh's <c>Render</c> call.
    ///
    /// Before this existed, <see cref="Mesh.PointLights"/> held one shared,
    /// scene-tree-order list used identically by every mesh, truncated at a
    /// flat cap; duplicating enough lights meant some lights fell off the end
    /// of that single list and stopped affecting *every* mesh in the scene,
    /// regardless of whether they were actually near enough to matter. This
    /// mirrors how Godot's Forward Mobile / Compatibility renderers select
    /// lights per mesh instance once a scene exceeds their per-object light
    /// cap (see https://github.com/godotengine/godot/pull/107234): score
    /// every candidate light by distance to the mesh divided by its
    /// range*energy (closer / brighter / longer-range light wins), and keep
    /// only the best-scoring <see cref="MaxLightsPerMesh"/> — so a mesh always
    /// lights from its own nearest/brightest lights no matter how many other
    /// lights exist elsewhere in the scene.
    /// </summary>
    private static void SelectPointLightsForMesh(
        vec3 meshWorldPos,
        List<(vec3 pos, vec3 color, float range, float energy, int shadowIndex, vec3 direction, float indirectEnergy, float spotCosOuterAngle, float spotCosInnerAngle)> allLights)
    {
        Mesh.PointLights.Clear();

        if (allLights.Count <= MaxLightsPerMesh)
        {
            Mesh.PointLights.AddRange(allLights);
            return;
        }

        _lightScoreScratch.Clear();
        for (int i = 0; i < allLights.Count; i++)
        {
            var l = allLights[i];
            float rangeEnergy = MathF.Max(0.01f, l.range * MathF.Max(l.energy, 0.01f));
            float score = (l.pos - meshWorldPos).Length / rangeEnergy;
            _lightScoreScratch.Add((score, i));
        }

        _lightScoreScratch.Sort((a, b) => a.score.CompareTo(b.score));

        for (int i = 0; i < MaxLightsPerMesh; i++)
            Mesh.PointLights.Add(allLights[_lightScoreScratch[i].index]);
    }

    private void RenderAmbientOcclusion(uint displayFbo, uint depthTexture, uint width, uint height,
        float nearPlane, float farPlane, PropertiesPanel? settings, RenderedPassMode passMode)
    {
        if (displayFbo == 0 || depthTexture == 0 || width == 0 || height == 0)
            return;

        bool aoOnlyPass = passMode == RenderedPassMode.AmbientOcclusion;
        bool aoEnabled = _ambientOcclusionShader != null && settings?.AmbientOcclusionEnabled == true;

        if (!aoEnabled)
        {
            if (aoOnlyPass)
            {
                Gl.BindFramebuffer(GLEnum.Framebuffer, displayFbo);
                Gl.DrawBuffer(GLEnum.ColorAttachment0);
                Gl.ReadBuffer(GLEnum.ColorAttachment0);
                Gl.Viewport(0, 0, width, height);
                Gl.ClearColor(0f, 0f, 0f, 1f);
                Gl.Clear(ClearBufferMask.ColorBufferBit);
            }
            return;
        }

        Gl.BindFramebuffer(GLEnum.Framebuffer, displayFbo);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);
        Gl.Viewport(0, 0, width, height);
        // Avoid a framebuffer feedback loop while the attached depth texture is sampled.
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.DepthAttachment, GLEnum.Texture2D, 0, 0);
        Gl.Disable(GLEnum.DepthTest);
        Gl.Disable(GLEnum.CullFace);
        if (aoOnlyPass)
        {
            Gl.Disable(GLEnum.Blend);
            Gl.ClearColor(0f, 0f, 0f, 1f);
            Gl.Clear(ClearBufferMask.ColorBufferBit);
        }
        else
        {
            Gl.Enable(GLEnum.Blend);
            Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        }

        Shader aoShader = _ambientOcclusionShader!;
        PropertiesPanel aoSettings = settings!;
        uint program = aoShader.ShaderProgram;
        Gl.UseProgram(program);
        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, depthTexture);

        void Float(string name, float value)
        {
            int location = Gl.GetUniformLocation(program, name);
            if (location >= 0) Gl.Uniform1(location, value);
        }

        int depthLocation = Gl.GetUniformLocation(program, "uDepth");
        if (depthLocation >= 0) Gl.Uniform1(depthLocation, 0);
        int texelLocation = Gl.GetUniformLocation(program, "uTexelSize");
        if (texelLocation >= 0) Gl.Uniform2(texelLocation, 1f / width, 1f / height);
        Float("uNear", Math.Max(0.001f, nearPlane));
        Float("uFar", Math.Max(nearPlane + 0.001f, farPlane));
        Float("uRadius", Math.Clamp(aoSettings.AmbientOcclusionRadius, 0f, 128f));
        Float("uStrength", Math.Clamp(aoSettings.AmbientOcclusionStrength, 0f, 2f));
        Float("uRatio", Math.Clamp(aoSettings.AmbientOcclusionRatio, 0f, 1f));
        Float("uRatioBalance", Math.Clamp(aoSettings.AmbientOcclusionRatioBalance, 0f, 1f));
        int sampleCountLocation = Gl.GetUniformLocation(program, "uSampleCount");
        if (sampleCountLocation >= 0)
            Gl.Uniform1(sampleCountLocation, Math.Clamp(aoSettings.AmbientOcclusionSampleCount, 1, 128));
        int outputModeLocation = Gl.GetUniformLocation(program, "uOutputMode");
        if (outputModeLocation >= 0)
            Gl.Uniform1(outputModeLocation, aoOnlyPass ? 1 : 0);
        int colorLocation = Gl.GetUniformLocation(program, "uColor");
        if (colorLocation >= 0)
            Gl.Uniform3(colorLocation, aoSettings.AmbientOcclusionColor[0], aoSettings.AmbientOcclusionColor[1],
                aoSettings.AmbientOcclusionColor[2]);

        Gl.BindVertexArray(_edgeVao);
        Gl.DrawArrays(GLEnum.Triangles, 0, 3);
        Gl.BindVertexArray(0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.DepthAttachment, GLEnum.Texture2D, depthTexture, 0);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);
        Gl.Disable(GLEnum.Blend);
        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
    }

    private void ApplyIndirectLightingPassGeneric(
        uint sceneFbo,
        uint sourceColorTex,
        uint depthTexture,
        uint indirectFbo,
        uint indirectTex,
        uint indirectPingFbo,
        uint indirectPingTex,
        uint width,
        uint height,
        float nearPlane,
        float farPlane,
        PropertiesPanel? settings,
        RenderedPassMode passMode)
    {
        if (Gl == null || settings == null || passMode != RenderedPassMode.Combined)
            return;
        if (_indirectLightingShader == null || _glowShader == null || sourceColorTex == 0 || depthTexture == 0 ||
            indirectFbo == 0 || indirectTex == 0 || indirectPingFbo == 0 || indirectPingTex == 0 ||
            width == 0 || height == 0)
            return;

        if (!settings.IndirectLightingEnabled)
            return;
        if (string.Equals(settings.GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase))
            return;

        float precision = Math.Clamp(settings.IndirectLightingPrecision, 0f, 1f);
        float strength = Math.Clamp(settings.IndirectLightingStrength, 0f, 4f);
        float rayStep = Math.Clamp(settings.IndirectLightingRayStep, 1f, 64f);
        float blurRadius = Math.Clamp(settings.IndirectLightingBlurRadius, 0f, 8f);
        float denoiserStrength = Math.Clamp(settings.IndirectLightingDenoiserStrength, 0f, 200f);
        int sampleCount = Math.Clamp((int)MathF.Round(6f + (precision * 26f)), 4, 32);

        if (strength <= 0f)
            return;

        Gl.Disable(GLEnum.DepthTest);
        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.Blend);

        // Pass 1: generate raw screen-space indirect lighting from scene color/depth.
        Gl.BindFramebuffer(GLEnum.Framebuffer, indirectFbo);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);
        Gl.Viewport(0, 0, width, height);
        Gl.ClearColor(0f, 0f, 0f, 1f);
        Gl.Clear(ClearBufferMask.ColorBufferBit);

        uint indirectProgram = _indirectLightingShader.ShaderProgram;
        Gl.UseProgram(indirectProgram);
        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, sourceColorTex);
        Gl.ActiveTexture(GLEnum.Texture1);
        Gl.BindTexture(GLEnum.Texture2D, depthTexture);

        int sceneLoc = Gl.GetUniformLocation(indirectProgram, "uScene");
        int depthLoc = Gl.GetUniformLocation(indirectProgram, "uDepth");
        int texelLoc = Gl.GetUniformLocation(indirectProgram, "uTexelSize");
        int nearLoc = Gl.GetUniformLocation(indirectProgram, "uNear");
        int farLoc = Gl.GetUniformLocation(indirectProgram, "uFar");
        int precisionLoc = Gl.GetUniformLocation(indirectProgram, "uPrecision");
        int rayStepLoc = Gl.GetUniformLocation(indirectProgram, "uRayStep");
        int sampleLoc = Gl.GetUniformLocation(indirectProgram, "uSampleCount");

        if (sceneLoc >= 0) Gl.Uniform1(sceneLoc, 0);
        if (depthLoc >= 0) Gl.Uniform1(depthLoc, 1);
        if (texelLoc >= 0) Gl.Uniform2(texelLoc, 1f / width, 1f / height);
        if (nearLoc >= 0) Gl.Uniform1(nearLoc, Math.Max(0.001f, nearPlane));
        if (farLoc >= 0) Gl.Uniform1(farLoc, Math.Max(nearPlane + 0.001f, farPlane));
        if (precisionLoc >= 0) Gl.Uniform1(precisionLoc, precision);
        if (rayStepLoc >= 0) Gl.Uniform1(rayStepLoc, rayStep);
        if (sampleLoc >= 0) Gl.Uniform1(sampleLoc, sampleCount);

        Gl.BindVertexArray(_edgeVao);
        Gl.DrawArrays(GLEnum.Triangles, 0, 3);

        // Pass 2: optional blur, matching the reference pipeline controls.
        if (blurRadius > 0.001f)
        {
            uint blurProgram = _glowShader.ShaderProgram;
            int blurSceneLoc = Gl.GetUniformLocation(blurProgram, "uScene");
            int blurTexelLoc = Gl.GetUniformLocation(blurProgram, "uTexelSize");
            int blurStrengthLoc = Gl.GetUniformLocation(blurProgram, "uStrength");
            int blurSizeLoc = Gl.GetUniformLocation(blurProgram, "uSize");
            int blurDirectionLoc = Gl.GetUniformLocation(blurProgram, "uDirection");
            int blurModeLoc = Gl.GetUniformLocation(blurProgram, "uMode");

            Gl.UseProgram(blurProgram);
            if (blurSceneLoc >= 0) Gl.Uniform1(blurSceneLoc, 0);
            if (blurTexelLoc >= 0) Gl.Uniform2(blurTexelLoc, 1f / width, 1f / height);
            if (blurStrengthLoc >= 0) Gl.Uniform1(blurStrengthLoc, 1f);
            if (blurSizeLoc >= 0) Gl.Uniform1(blurSizeLoc, blurRadius);
            if (blurModeLoc >= 0) Gl.Uniform1(blurModeLoc, 1);

            Gl.BindFramebuffer(GLEnum.Framebuffer, indirectPingFbo);
            Gl.Viewport(0, 0, width, height);
            Gl.ActiveTexture(GLEnum.Texture0);
            Gl.BindTexture(GLEnum.Texture2D, indirectTex);
            if (blurDirectionLoc >= 0) Gl.Uniform2(blurDirectionLoc, 1f, 0f);
            Gl.DrawArrays(GLEnum.Triangles, 0, 3);

            Gl.BindFramebuffer(GLEnum.Framebuffer, indirectFbo);
            Gl.Viewport(0, 0, width, height);
            Gl.ActiveTexture(GLEnum.Texture0);
            Gl.BindTexture(GLEnum.Texture2D, indirectPingTex);
            if (blurDirectionLoc >= 0) Gl.Uniform2(blurDirectionLoc, 0f, 1f);
            Gl.DrawArrays(GLEnum.Triangles, 0, 3);
        }

        uint compositeTexture = indirectTex;

        // Pass 3: optional bilateral denoise using depth-aware weighting.
        if (settings.IndirectLightingDenoiser && denoiserStrength > 0.001f && _indirectDenoiseShader != null)
        {
            uint denoiseProgram = _indirectDenoiseShader.ShaderProgram;
            Gl.UseProgram(denoiseProgram);
            Gl.BindFramebuffer(GLEnum.Framebuffer, indirectPingFbo);
            Gl.DrawBuffer(GLEnum.ColorAttachment0);
            Gl.ReadBuffer(GLEnum.ColorAttachment0);
            Gl.Viewport(0, 0, width, height);

            Gl.ActiveTexture(GLEnum.Texture0);
            Gl.BindTexture(GLEnum.Texture2D, indirectTex);
            Gl.ActiveTexture(GLEnum.Texture1);
            Gl.BindTexture(GLEnum.Texture2D, depthTexture);

            int indirectLoc = Gl.GetUniformLocation(denoiseProgram, "uIndirectTex");
            int denoiseDepthLoc = Gl.GetUniformLocation(denoiseProgram, "uDepth");
            int denoiseTexelLoc = Gl.GetUniformLocation(denoiseProgram, "uTexelSize");
            int denoiseStrengthLoc = Gl.GetUniformLocation(denoiseProgram, "uDenoiseStrength");
            int denoiseNearLoc = Gl.GetUniformLocation(denoiseProgram, "uNear");
            int denoiseFarLoc = Gl.GetUniformLocation(denoiseProgram, "uFar");

            if (indirectLoc >= 0) Gl.Uniform1(indirectLoc, 0);
            if (denoiseDepthLoc >= 0) Gl.Uniform1(denoiseDepthLoc, 1);
            if (denoiseTexelLoc >= 0) Gl.Uniform2(denoiseTexelLoc, 1f / width, 1f / height);
            if (denoiseStrengthLoc >= 0) Gl.Uniform1(denoiseStrengthLoc, denoiserStrength);
            if (denoiseNearLoc >= 0) Gl.Uniform1(denoiseNearLoc, Math.Max(0.001f, nearPlane));
            if (denoiseFarLoc >= 0) Gl.Uniform1(denoiseFarLoc, Math.Max(nearPlane + 0.001f, farPlane));

            Gl.DrawArrays(GLEnum.Triangles, 0, 3);
            compositeTexture = indirectPingTex;
        }

        // Pass 4: additive composite of indirect contribution into the scene.
        uint compositeProgram = _glowShader.ShaderProgram;
        int compSceneLoc = Gl.GetUniformLocation(compositeProgram, "uScene");
        int compTexelLoc = Gl.GetUniformLocation(compositeProgram, "uTexelSize");
        int compStrengthLoc = Gl.GetUniformLocation(compositeProgram, "uStrength");
        int compSizeLoc = Gl.GetUniformLocation(compositeProgram, "uSize");
        int compDirectionLoc = Gl.GetUniformLocation(compositeProgram, "uDirection");
        int compModeLoc = Gl.GetUniformLocation(compositeProgram, "uMode");

        Gl.UseProgram(compositeProgram);
        if (compSceneLoc >= 0) Gl.Uniform1(compSceneLoc, 0);
        if (compTexelLoc >= 0) Gl.Uniform2(compTexelLoc, 1f / width, 1f / height);
        if (compStrengthLoc >= 0) Gl.Uniform1(compStrengthLoc, strength);
        if (compSizeLoc >= 0) Gl.Uniform1(compSizeLoc, 1f);
        if (compDirectionLoc >= 0) Gl.Uniform2(compDirectionLoc, 1f, 0f);
        if (compModeLoc >= 0) Gl.Uniform1(compModeLoc, 2);

        Gl.BindFramebuffer(GLEnum.Framebuffer, sceneFbo);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, sourceColorTex, 0);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);
        Gl.Viewport(0, 0, width, height);
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.One, GLEnum.One);
        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, compositeTexture);
        Gl.DrawArrays(GLEnum.Triangles, 0, 3);

        Gl.BindVertexArray(0);
        Gl.ActiveTexture(GLEnum.Texture1);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.Disable(GLEnum.Blend);
        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
    }

    /// <summary>
    /// Matches Modelbench's render_world pipeline: parts are drawn once in
    /// ascending render-depth order with both blending and depth writes enabled.
    /// Thus a partially transparent earlier part leaves its colour on screen and
    /// its depth prevents later parts behind it from drawing. Fully transparent
    /// fragments are discarded by the material shader and write no depth.
    /// </summary>
    private void RenderDepthOrdered(
        mat4 view, mat4 proj,
        List<(mat4 model, Mesh mesh, float sortDepth, bool includeFog, bool blurTexture, bool textureMipmaps)> renderItems,
        List<(vec3 pos, vec3 color, float range, float energy, int shadowIndex, vec3 direction, float indirectEnergy,
            float spotCosOuterAngle, float spotCosInnerAngle)> allLights,
        bool forceUnshaded)
    {
        // LINQ OrderBy is stable, matching Modelbench's insertion behavior for
        // parts with equal depth. Camera distance intentionally has no role.
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        Gl.DepthMask(true);
        foreach (var (model, mesh, _, includeFog, blurTexture, textureMipmaps) in renderItems.OrderBy(x => x.sortDepth))
        {
            SelectPointLightsForMesh(new vec3(model.m30, model.m31, model.m32), allLights);
            RenderMeshWithModeOverride(mesh, model, view, proj, forceUnshaded, includeFog, blurTexture, textureMipmaps);
        }
        Gl.Disable(GLEnum.Blend);
    }

    private static void RenderMeshWithModeOverride(
        Mesh mesh,
        mat4 model,
        mat4 view,
        mat4 proj,
        bool forceUnshaded,
        bool includeFog = true,
        bool blurTexture = false,
        bool textureMipmaps = false)
    {
        bool previousUnlit = mesh.Unlit;
        bool previousIncludeFog = mesh.IncludeInFog;
        bool previousBlurTexture = mesh.BlurTexture;
        bool previousTextureMipmaps = mesh.TextureMipmaps;

        mesh.IncludeInFog = includeFog;
        mesh.BlurTexture = blurTexture;
        mesh.TextureMipmaps = textureMipmaps;

        if (forceUnshaded)
            mesh.Unlit = true;

        mesh.Render(model, view, proj);

        if (forceUnshaded)
            mesh.Unlit = previousUnlit;

        mesh.IncludeInFog = previousIncludeFog;
        mesh.BlurTexture = previousBlurTexture;
        mesh.TextureMipmaps = previousTextureMipmaps;
    }

    /// <summary>
    /// Recursively collects (worldMatrix, mesh) pairs from every visible node in the
    /// scene hierarchy.  Each node uses its own <see cref="SceneObject.GetWorldMatrix"/>
    /// so that child nodes are correctly positioned relative to their parents.
    /// </summary>
    private void CollectRenderPairs(
        IEnumerable<SceneObject> objects,
        vec3 camPos,
        SceneRenderMode renderMode,
        Frustum frustum,
        List<(mat4 model, Mesh mesh, float sortDepth, bool includeFog, bool blurTexture, bool textureMipmaps)> renderItems,
        List<(mat4 model, Mesh mesh, float sortDepth, bool includeFog, bool blurTexture, bool textureMipmaps)> renderItemsNoAo,
        List<(mat4 model, Mesh mesh)> overlays,
        Dictionary<string, BoneSceneObject>? boneDict = null)
    {
        foreach (var obj in objects)
        {
            if (!obj.GetEffectiveVisibility()) continue;
            if (!ShouldRenderInQuality(obj, renderMode)) continue;

            mat4  model    = GetRenderableWorldMatrix(obj, camPos);
            vec3  worldPos = new vec3(model.m30, model.m31, model.m32);
            float dist     = (worldPos - camPos).LengthSqr;

            // Build a bone dictionary once for each top-level model hierarchy.
            var localBoneDict = boneDict ?? BuildBoneDictionary(obj);

            // Only this node's own Visuals — not its descendants.
            foreach (var mesh in obj.Visuals.Where(mesh => !mesh.PickOnly))
            {
                mesh.CullFrontFaces = obj.GetEffectiveInvertFaces();

                // Frustum culling: test mesh bounding sphere against the view frustum.
                // Overlay / always-visible meshes skip the test.
                if (!mesh.DepthTestDisabled)
                {
                    vec3 worldCenter = new vec3((model * new vec4(mesh.BoundingSphereCenter, 1f)).xyz);
                    float scale = MathF.Max(
                        new vec3(model.m00, model.m10, model.m20).Length,
                        MathF.Max(
                            new vec3(model.m01, model.m11, model.m21).Length,
                            new vec3(model.m02, model.m12, model.m22).Length));
                    float worldRadius = mesh.BoundingSphereRadius * scale;
                    if (!frustum.TestSphere(worldCenter, worldRadius))
                        continue;
                }

                if (mesh.IsSkinned && localBoneDict != null)
                    UpdateBoneMatrices(mesh, model, localBoneDict);

                // Overlay meshes (e.g. camera icon) are collected separately so
                // they can be shown/hidden with the Overlays toggle and are never
                // fed into the normal depth-sorted scene passes.
                if (mesh.DepthTestDisabled)
                {
                    overlays.Add((model, mesh));
                    continue;
                }

                var targetList = obj.IncludeInAmbientOcclusion ? renderItems : renderItemsNoAo;
                targetList.Add((
                    model,
                    mesh,
                    mesh.SortDepth + obj.RenderDepthOffset,
                    obj.IncludeInFog,
                    obj.BlurTexture,
                    obj.TextureMipmaps));
            }

            // Recurse into children so each child uses its own world matrix.
            CollectRenderPairs(obj.Children, camPos, renderMode, frustum, renderItems, renderItemsNoAo, overlays, localBoneDict);
        }
    }

    private static mat4 GetRenderableWorldMatrix(SceneObject obj, vec3 cameraPos)
    {
        mat4 world = obj.GetWorldMatrix();

        bool faceCamera = obj.PrimitivePlaneFaceCamera && string.Equals(obj.ObjectType, "Plane", StringComparison.OrdinalIgnoreCase)
                          || obj.TextMeshFaceCamera && string.Equals(obj.ObjectType, "Text Mesh", StringComparison.OrdinalIgnoreCase);
        if (!faceCamera ||
            !string.Equals(obj.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase) ||
            obj.Visuals.Count == 0)
            return world;

        vec3 worldPos = new vec3(world.m30, world.m31, world.m32);
        vec3 toCamera = cameraPos - worldPos;
        if (toCamera.LengthSqr <= 1e-10f)
            toCamera = new vec3(0f, 0f, -1f);

        vec3 forward = toCamera.Normalized;
        vec3 upRef = vec3.UnitY;
        if (MathF.Abs(vec3.Dot(forward, upRef)) > 0.999f)
            upRef = new vec3(0f, 0f, 1f);

        vec3 right = vec3.Cross(upRef, forward).Normalized;
        vec3 up = vec3.Cross(forward, right).Normalized;
        vec3 back = -forward;

        vec3 row0 = new vec3(world.m00, world.m01, world.m02);
        vec3 row1 = new vec3(world.m10, world.m11, world.m12);
        vec3 row2 = new vec3(world.m20, world.m21, world.m22);
        vec3 scale = new vec3(
            MathF.Max(row0.Length, 1e-6f),
            MathF.Max(row1.Length, 1e-6f),
            MathF.Max(row2.Length, 1e-6f));

        mat4 rotation = mat4.Identity;
        rotation.m00 = right.x; rotation.m01 = right.y; rotation.m02 = right.z;
        rotation.m10 = up.x;    rotation.m11 = up.y;    rotation.m12 = up.z;
        rotation.m20 = back.x;  rotation.m21 = back.y;  rotation.m22 = back.z;

        mat4 translation = mat4.Translate(worldPos);
        mat4 scaling = mat4.Scale(scale);
        mat4 pivot = mat4.Translate(obj.GetAccumulatedPivotOffset());
        return translation * rotation * scaling * pivot;
    }

    private void UpdateParticleSpawners(IEnumerable<SceneObject> objects, float deltaTime, bool timelinePlaying)
    {
        if (SpawnMenu == null)
            return;

        float timelineSeconds = timelinePlaying ? GetTimelineEffectTimeSeconds() : 0f;
        SelectionManager? selection = SelectionManager.Instance;

        // Particle simulation may add/remove child nodes while traversing.
        // Iterate over a snapshot to avoid invalidating the collection enumerator.
        var snapshot = new List<SceneObject>(objects);

        foreach (var obj in snapshot)
        {
            if (!obj.GetEffectiveVisibility())
                continue;

            if (obj is ParticleSpawnerSceneObject spawner && !spawner.IsRuntimeTransient)
            {
                bool previewSelected = ParticlePreviewEnabled &&
                                       selection != null &&
                                       selection.IsSelected(spawner);

                if (timelinePlaying)
                    spawner.SimulateToTime(timelineSeconds, this, SpawnMenu);
                else if (previewSelected)
                    spawner.Step(deltaTime, this, SpawnMenu);
            }

            UpdateParticleSpawners(obj.Children, deltaTime, timelinePlaying);
        }
    }

    /// <summary>
    /// Builds a name → BoneSceneObject lookup for the entire hierarchy rooted at
    /// <paramref name="root"/>.
    /// </summary>
    private static Dictionary<string, BoneSceneObject> BuildBoneDictionary(SceneObject root)
    {
        var dict = new Dictionary<string, BoneSceneObject>(StringComparer.OrdinalIgnoreCase);
        CollectBones(root, dict);
        return dict;
    }

    private static void CollectBones(SceneObject obj, Dictionary<string, BoneSceneObject> dict)
    {
        if (obj is BoneSceneObject bone && !string.IsNullOrEmpty(bone.BoneName))
        {
            dict.TryAdd(bone.BoneName, bone);
        }

        foreach (var child in obj.Children)
            CollectBones(child, dict);
    }

    /// <summary>
    /// Computes the current bone-matrix palette for a skinned mesh.
    /// Each matrix is <c>meshWorldInverse * boneWorld * inverseBindMatrix</c>,
    /// which deforms vertices in the mesh's local space so the regular model matrix
    /// still applies the mesh node's own transform.
    /// </summary>
    private static void UpdateBoneMatrices(Mesh mesh, mat4 meshWorld, Dictionary<string, BoneSceneObject> boneDict)
    {
        if (mesh.BoneNames.Count == 0 || mesh.BoneInverseBindMatrices.Count == 0)
            return;

        mat4 meshWorldInverse = meshWorld.Inverse;
        var matrices = new List<mat4>(mesh.BoneNames.Count);

        for (int i = 0; i < mesh.BoneNames.Count; i++)
        {
            string name = mesh.BoneNames[i];
            if (boneDict.TryGetValue(name, out var bone))
            {
                mat4 boneWorld = bone.GetWorldMatrix();
                mat4 ibm = mesh.BoneInverseBindMatrices[i];
                matrices.Add(meshWorldInverse * boneWorld * ibm);
            }
            else
            {
                matrices.Add(mat4.Identity);
            }
        }

        mesh.BoneMatrices = matrices;
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

    /// <summary>
    /// Applies a persisted preview visibility state.
    /// Valid states are:
    /// - docked + visible
    /// - docked + hidden
    /// - undocked + visible
    /// </summary>
    public void ApplySavedVisibilityState(bool visible, bool undocked)
    {
        if (!IsPreviewViewport)
            return;

        if (undocked)
        {
            Undocked = true;
            InlineVisible = false;
            return;
        }

        Undocked = false;
        InlineVisible = visible;
    }

    /// <summary>
    /// Transitions an undocked preview back to inline mode and shows it.
    /// </summary>
    public void DockToInlineVisible()
    {
        if (!IsPreviewViewport)
            return;

        Undocked = false;
        InlineVisible = true;
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

        Gl.GenTextures(1, out _previewDepthTex);
        Gl.BindTexture(GLEnum.Texture2D, _previewDepthTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.DepthComponent24, width, height, 0,
            PixelFormat.DepthComponent, GLEnum.UnsignedInt, null);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.DepthAttachment, GLEnum.Texture2D, _previewDepthTex, 0);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);

        var status = Gl.CheckFramebufferStatus(GLEnum.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.Error.WriteLine($"Preview framebuffer incomplete: {status}");

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
            _pickShader = new Shader(Gl);
            _pickShader.CompileShader("pick.vert", "pick.frag");
        }
        if (_silhouetteShader == null)
        {
            _silhouetteShader = new Shader(Gl);
            _silhouetteShader.CompileShader("pick.vert", "silhouette.frag");
        }
        if (_edgeShader == null)
        {
            _edgeShader = new Shader(Gl);
            _edgeShader.CompileShader("edge.vert", "edge.frag");
            Gl.GenVertexArrays(1, out _edgeVao);
        }
        if (_ambientOcclusionShader == null)
        {
            _ambientOcclusionShader = new Shader(Gl);
            _ambientOcclusionShader.CompileShader("edge.vert", "ambient_occlusion.frag");
        }
        if (_indirectLightingShader == null)
        {
            _indirectLightingShader = new Shader(Gl);
            _indirectLightingShader.CompileShader("edge.vert", "indirect_lighting.frag");
        }
        if (_indirectDenoiseShader == null)
        {
            _indirectDenoiseShader = new Shader(Gl);
            _indirectDenoiseShader.CompileShader("edge.vert", "indirect_denoise.frag");
        }
        if (_glowShader == null)
        {
            _glowShader = new Shader(Gl);
            _glowShader.CompileShader("edge.vert", "glow.frag");
        }
        if (_filmGrainShader == null)
        {
            _filmGrainShader = new Shader(Gl);
            _filmGrainShader.CompileShader("edge.vert", "film_grain.frag");
        }

        InitGlowResources(ref _previewIndirectFbo, ref _previewIndirectTex, width, height, "Preview indirect");
        InitGlowResources(ref _previewIndirectPingFbo, ref _previewIndirectPingTex, width, height, "Preview indirect ping");
        InitGlowResources(ref _previewGlowFbo, ref _previewGlowTex, width, height, "Preview glow");
        InitGlowResources(ref _previewGlowPingFbo, ref _previewGlowPingTex, width, height, "Preview glow ping");

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
            Console.Error.WriteLine($"Preview pick framebuffer incomplete: {status}");

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
            Console.Error.WriteLine($"Preview silhouette framebuffer incomplete: {status}");

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
        Gl.BindTexture(GLEnum.Texture2D, _previewDepthTex);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.DepthComponent24, w, h, 0,
            PixelFormat.DepthComponent, GLEnum.UnsignedInt, null);
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

        ResizeGlowResources(_previewIndirectTex, w, h);
        ResizeGlowResources(_previewIndirectPingTex, w, h);
        ResizeGlowResources(_previewGlowTex, w, h);
        ResizeGlowResources(_previewGlowPingTex, w, h);
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
        => DrawCameraDropdown(spawned);

    private (Camera cam, CameraSceneObject? sceneObj) GetActiveCameraPreview(List<CameraSceneObject> spawned)
    {
        switch (_selectedCameraIndex)
        {
            case RenderOutputIndex:
                return GetRenderOutputCamera(spawned);
            case 0:
                return (MainViewport?.Camera ?? Camera, null);
        }

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

    /// <summary>
    /// Resolves the camera used for render output (image / video export).
    /// Priority:
    ///   1. The first visible camera with <see cref="CameraSceneObject.Active"/>
    ///      set to <c>true</c>.
    ///   2. Otherwise, the first visible camera in the scene tree.
    ///   3. Otherwise, the work camera (index 0).
    /// </summary>
    public (Camera cam, CameraSceneObject? sceneObj) GetRenderOutputCamera(List<CameraSceneObject> spawned)
    {
        CameraSceneObject? firstVisible = null;
        foreach (var cam in spawned.Where(cam => cam.GetEffectiveVisibility()))
        {
            if (cam.Active) return ResolveCamera(cam);
            firstVisible ??= cam;
        }

        return firstVisible != null ? ResolveCamera(firstVisible) : (MainViewport?.Camera ?? Camera, null);
    }

    private (Camera cam, CameraSceneObject? sceneObj) ResolveCamera(CameraSceneObject cam)
    {
        cam.SyncCameraToTransform();
        return (cam.ViewCamera, cam);
    }

    private Camera BuildPreparedCamera(Camera source, CameraSceneObject? sceneObj)
    {
        var prepared = CloneCamera(source);
        float effectTimeSeconds = GetTimelineEffectTimeSeconds();

        if (sceneObj != null)
        {
            prepared.FovY = glm.Radians(sceneObj.Fov);
            prepared.Near = sceneObj.Near;
            prepared.Far = sceneObj.Far;

            if (sceneObj.Effects.Count > 0)
                ApplyCameraEffects(prepared, sceneObj.Effects, effectTimeSeconds);
        }

        return prepared;
    }

    private static float GetTimelineEffectTimeSeconds()
    {
        var timeline = Timeline.Instance;
        if (timeline == null)
            return 0f;

        float fps = MathF.Max(1f, timeline.Framerate);
        return timeline.CurrentFrame / fps;
    }

    private static Camera CloneCamera(Camera source)
    {
        return new Camera
        {
            Target = source.Target,
            Yaw = source.Yaw,
            Pitch = source.Pitch,
            Roll = source.Roll,
            Distance = source.Distance,
            FovY = source.FovY,
            Near = source.Near,
            Far = source.Far
        };
    }

    private static void ApplyCameraEffects(Camera camera, IEnumerable<CameraEffect> effects, float timeSeconds)
    {
        foreach (var effect in effects)
        {
            if (effect.Type != CameraEffectType.CameraShake)
                continue;

            var shake = effect.Shake ?? new CameraShakeSettings();
            float trauma = Math.Clamp(shake.Trauma, 0f, 100f);
            vec3 noise = new vec3(
                SampleShakeNoise(timeSeconds, shake.Speed.x, shake.Offset.x, 0.73f),
                SampleShakeNoise(timeSeconds, shake.Speed.y, shake.Offset.y, 1.61f),
                SampleShakeNoise(timeSeconds, shake.Speed.z, shake.Offset.z, 2.29f));

            if (shake.Mode is CameraShakeMode.Positional or CameraShakeMode.Both)
            {
                vec3 forward = (camera.Target - camera.Position).Normalized;
                vec3 right = vec3.Cross(forward, vec3.UnitY);
                if (right.LengthSqr < 1e-8f)
                    right = vec3.UnitX;
                else
                    right = right.Normalized;

                vec3 up = vec3.Cross(right, forward).Normalized;
                vec3 positionalOffset =
                    right * (noise.x * shake.Strength.x * trauma) +
                    up * (noise.y * shake.Strength.y * trauma) +
                    forward * (noise.z * shake.Strength.z * trauma);

                camera.Target += positionalOffset;
            }

            if (shake.Mode is CameraShakeMode.Rotational or CameraShakeMode.Both)
            {
                camera.Yaw += noise.x * shake.Strength.x * trauma;
                camera.Pitch = Math.Clamp(
                    camera.Pitch + noise.y * shake.Strength.y * trauma,
                    -MathF.PI / 2f + 0.01f,
                    MathF.PI / 2f - 0.01f);
                camera.Yaw += noise.z * shake.Strength.z * trauma * 0.25f;
            }
        }
    }

    private static float SampleShakeNoise(float timeSeconds, float speed, float offset, float seed)
    {
        float phase = timeSeconds * speed + offset + seed;
        float harmonic1 = MathF.Sin(phase);
        float harmonic2 = 0.5f * MathF.Sin(phase * 2.13f + 1.37f + seed * 0.7f);
        float harmonic3 = 0.25f * MathF.Sin(phase * 4.37f + 2.51f + seed * 1.3f);
        return (harmonic1 + harmonic2 + harmonic3) / 1.75f;
    }

    private void ApplyFilmGrainPass(uint sceneFbo, uint sceneTexture, uint scratchFbo, uint scratchTexture,
        uint width, uint height, CameraSceneObject? cameraObject)
    {
        if (Gl == null || _filmGrainShader == null || cameraObject == null || width == 0 || height == 0)
            return;

        var grains = cameraObject.Effects.Where(effect => effect.Type == CameraEffectType.FilmGrain).ToList();
        if (grains.Count == 0) return;

        Gl.BindFramebuffer(GLEnum.ReadFramebuffer, sceneFbo);
        Gl.BindFramebuffer(GLEnum.DrawFramebuffer, scratchFbo);
        Gl.BlitFramebuffer(0, 0, (int)width, (int)height, 0, 0, (int)width, (int)height,
            ClearBufferMask.ColorBufferBit, GLEnum.Nearest);

        Gl.Disable(GLEnum.DepthTest);
        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.Blend);
        Gl.BindFramebuffer(GLEnum.Framebuffer, sceneFbo);
        Gl.Viewport(0, 0, width, height);

        uint program = _filmGrainShader.ShaderProgram;
        Gl.UseProgram(program);
        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, scratchTexture);
        int sceneLoc = Gl.GetUniformLocation(program, "uScene");
        if (sceneLoc >= 0) Gl.Uniform1(sceneLoc, 0);

        float strength = Math.Clamp(grains.Sum(effect => effect.FilmGrain.Strength), 0f, 1f);
        float weight = MathF.Max(0.0001f, grains.Sum(effect => MathF.Max(0f, effect.FilmGrain.Strength)));
        float saturation = grains.Sum(effect => effect.FilmGrain.Saturation * MathF.Max(0f, effect.FilmGrain.Strength)) / weight;
        float size = grains.Sum(effect => effect.FilmGrain.Size * MathF.Max(0f, effect.FilmGrain.Strength)) / weight;
        int frame = Timeline.Instance?.CurrentFrame ?? 0;

        int strengthLoc = Gl.GetUniformLocation(program, "uStrength");
        int saturationLoc = Gl.GetUniformLocation(program, "uSaturation");
        int sizeLoc = Gl.GetUniformLocation(program, "uSize");
        int frameLoc = Gl.GetUniformLocation(program, "uFrame");
        int resolutionLoc = Gl.GetUniformLocation(program, "uResolution");
        if (strengthLoc >= 0) Gl.Uniform1(strengthLoc, strength);
        if (saturationLoc >= 0) Gl.Uniform1(saturationLoc, Math.Clamp(saturation, 0f, 1f));
        if (sizeLoc >= 0) Gl.Uniform1(sizeLoc, Math.Clamp(size, 0.25f, 8f));
        if (frameLoc >= 0) Gl.Uniform1(frameLoc, (float)frame);
        if (resolutionLoc >= 0) Gl.Uniform2(resolutionLoc, (float)width, (float)height);

        Gl.BindVertexArray(_edgeVao);
        Gl.DrawArrays(GLEnum.Triangles, 0, 3);
        Gl.BindVertexArray(0);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.Enable(GLEnum.DepthTest);
        Gl.Enable(GLEnum.CullFace);
    }

    public unsafe void RenderInline(Vector2 imageMin, Vector2 imageSize, List<CameraSceneObject> spawned)
    {
        if (!IsInlineVisible) return;

        _inlineSize.X = Math.Clamp(_inlineSize.X, InlineMinW, Math.Max(InlineMinW, imageSize.X - InlinePad * 2));
        _inlineSize.Y = Math.Clamp(_inlineSize.Y, InlineMinH, Math.Max(InlineMinH, imageSize.Y - InlinePad * 2));

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
            new Vector2(Math.Max(InlineMinW, imageSize.X - InlinePad * 2), Math.Max(InlineMinH, imageSize.Y - InlinePad * 2)));

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
        switch (leftDown)
        {
            case true when hovered:
            {
                if (mouseJustPressed)
                {
                    _inlineMouseDownPos = mouse;
                    _inlineSnappedThisInteraction = false;  // Reset snap flag for new drag
                }
            
                _inlineFramesSinceMouseUp = 0;
                break;
            }
            // Mouse was just released - end drag mode
            case false when _inlineMouseWasDownLastFrame:
                _inlineDragActive = false;
                _inlineFramesSinceMouseUp = 0;
                break;
            case false:
                _inlineFramesSinceMouseUp++;
                break;
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

        ImGui.SameLine();
        DrawRenderModeSelector("PreviewInlineHeader", 176f);

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

            if (HighQualityPreviewEnabled)
                RenderFpsOverlay(previewImageMin, previewImageSize, Corner.BottomRight);
        }

        ImGui.End();
    }

    public (Camera, CameraSceneObject?) DrawCameraDropdown(List<CameraSceneObject> spawned)
    {
        string currentLabel = _selectedCameraIndex switch
        {
            RenderOutputIndex => "Render Output",
            0 => "Work Camera",
            _ => _selectedCameraIndex - 1 < spawned.Count ? spawned[_selectedCameraIndex - 1].Name : "Render Output"
        };

        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo("##camSelect", currentLabel))
        {
            bool roSel = _selectedCameraIndex == RenderOutputIndex;
            if (ImGui.Selectable("Render Output##camRO", roSel)) _selectedCameraIndex = RenderOutputIndex;
            if (roSel) ImGui.SetItemDefaultFocus();

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

    public void DrawCornerPicker()
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

    /// <summary>
    /// Updates every camera view currently referenced by a scene object's
    /// <see cref="SceneObject.CameraTextureObjectId"/> and assigns its stable GL
    /// texture to that object's meshes.
    /// </summary>
    public unsafe void UpdateCameraTextureFeeds()
    {
        if (!IsPreviewViewport || Gl == null || MainViewport == null || _previewFbo == 0)
            return;

        var objects = new List<SceneObject>();
        foreach (var root in MainViewport.SceneObjects)
            CollectCameraTextureObjects(root, objects);
        if (objects.Count == 0) return;

        var cameras = new Dictionary<string, CameraSceneObject>(StringComparer.Ordinal);
        foreach (var camera in GetSpawnedCamerasPublic())
        {
            if (!string.IsNullOrEmpty(camera.ObjectId))
                cameras[camera.ObjectId] = camera;
        }

        // Allocate and assign all destinations first. This means feeds sample
        // the previous completed frame rather than the shared preview target,
        // avoiding recursive read/write feedback when cameras see screens.
        foreach (var obj in objects)
        {
            if (!cameras.ContainsKey(obj.CameraTextureObjectId)) continue;
            uint texture = GetOrCreateCameraFeedTexture(obj.CameraTextureObjectId);
            foreach (var mesh in obj.Visuals)
                mesh.TextureId = texture;
        }

        bool overlays = OverlaysEnabled;
        bool renderFeedsHighQuality =
            _viewportRenderMode == ViewportRenderMode.Rendered ||
            MainViewport._viewportRenderMode == ViewportRenderMode.Rendered;
        OverlaysEnabled = false;
        try
        {
            // RenderScenePublic sets the GL viewport, but it does not allocate
            // its attachments. Ensure the shared preview FBO really is the
            // feed resolution before rendering/copying; otherwise a small UI
            // preview leaves the remainder of the 512x512 copy stale.
            ResizeFboPublic(CameraFeedSize, CameraFeedSize);

            foreach (var cameraId in objects.Select(o => o.CameraTextureObjectId).Distinct(StringComparer.Ordinal))
            {
                if (!cameras.TryGetValue(cameraId, out var camera)) continue;
                uint destination = _cameraFeedTextures[cameraId];

                camera.SyncCameraToTransform();
                RenderScenePublic(camera.ViewCamera, camera, CameraFeedSize, CameraFeedSize,
                    highQuality: renderFeedsHighQuality, applyCameraEffects: true);

                if (_cameraFeedCopyFbo == 0)
                    Gl.GenFramebuffers(1, out _cameraFeedCopyFbo);
                Gl.BindFramebuffer(GLEnum.ReadFramebuffer, _previewFbo);
                Gl.ReadBuffer(GLEnum.ColorAttachment0);
                Gl.BindFramebuffer(GLEnum.DrawFramebuffer, _cameraFeedCopyFbo);
                Gl.FramebufferTexture2D(GLEnum.DrawFramebuffer, GLEnum.ColorAttachment0,
                    GLEnum.Texture2D, destination, 0);
                Gl.DrawBuffer(GLEnum.ColorAttachment0);
                Gl.BlitFramebuffer(0, 0, (int)CameraFeedSize, (int)CameraFeedSize,
                    0, 0, (int)CameraFeedSize, (int)CameraFeedSize,
                    ClearBufferMask.ColorBufferBit, GLEnum.Linear);
            }
        }
        finally
        {
            OverlaysEnabled = overlays;
            Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
        }
    }

    private static void CollectCameraTextureObjects(SceneObject obj, List<SceneObject> result)
    {
        if (!string.IsNullOrEmpty(obj.CameraTextureObjectId)) result.Add(obj);
        foreach (var child in obj.Children)
            CollectCameraTextureObjects(child, result);
    }

    private unsafe uint GetOrCreateCameraFeedTexture(string cameraId)
    {
        if (_cameraFeedTextures.TryGetValue(cameraId, out uint existing)) return existing;

        Gl!.GenTextures(1, out uint texture);
        Gl.BindTexture(GLEnum.Texture2D, texture);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, CameraFeedSize, CameraFeedSize,
            0, GLEnum.Rgb, GLEnum.UnsignedByte, null);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        _cameraFeedTextures[cameraId] = texture;
        return texture;
    }

    public void RenderScenePublic(Camera cam, CameraSceneObject? sceneObj, uint w, uint h,
        bool highQuality = false, bool applyCameraEffects = false)
    {
        if (Gl == null || MainViewport == null) return;

        ViewportRenderMode effectiveMode = highQuality ? ViewportRenderMode.Rendered : _viewportRenderMode;
        SceneRenderMode renderMode = effectiveMode == ViewportRenderMode.Rendered
            ? SceneRenderMode.Rendered
            : SceneRenderMode.Unrendered;
        bool forceUnshaded = effectiveMode == ViewportRenderMode.FlatUnshaded ||
                             effectiveMode == ViewportRenderMode.Wireframe;
        bool wireframeMode = effectiveMode == ViewportRenderMode.Wireframe;
        RenderedPassMode effectivePassMode = highQuality ? RenderedPassMode.Combined : _renderedPassMode;
        bool previewWorldGi = renderMode == SceneRenderMode.Rendered &&
                      MainViewport.PropertiesPanel?.IndirectLightingEnabled == true &&
                      string.Equals(MainViewport.PropertiesPanel.GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase);
        bool previewScreenSpaceGi = renderMode == SceneRenderMode.Rendered &&
                        MainViewport.PropertiesPanel?.IndirectLightingEnabled == true &&
                        !string.Equals(MainViewport.PropertiesPanel.GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase);
        Mesh.IndirectWorldEnabled = previewWorldGi || previewScreenSpaceGi;
        Mesh.IndirectWorldStrength = Math.Clamp(MainViewport.PropertiesPanel?.IndirectLightingStrength ?? 1f, 0f, 4f);

        Gl.BindFramebuffer(GLEnum.Framebuffer, _previewFbo);
        Gl.DrawBuffer(GLEnum.ColorAttachment0);
        Gl.ReadBuffer(GLEnum.ColorAttachment0);
        Gl.Viewport(0, 0, w, h);

        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthFunc(GLEnum.Less);
        Gl.Enable(GLEnum.CullFace);
        Gl.CullFace(GLEnum.Back);
        Gl.FrontFace(GLEnum.Ccw);

        float[] bg = MainViewport.PropertiesPanel?.BackgroundColor ?? [0.18f, 0.18f, 0.18f, 1.0f];
        Gl.ClearColor(bg[0], bg[1], bg[2], bg[3]);
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        float aspect = h > 0 ? (float)w / h : 1f;

        var renderCamera = BuildPreparedCamera(cam, sceneObj);

        mat4 view = renderCamera.GetViewMatrix();
        mat4 proj = renderCamera.GetProjectionMatrix(aspect);

        MainViewport.RenderSkyPublic(renderCamera, w, h);
        MainViewport.RenderBackgroundPlanePublic(w, h);
        Gl.Clear(ClearBufferMask.DepthBufferBit);


        Mesh.DeltaTime = ImGui.GetIO().DeltaTime;
        bool timelinePlaying = Timeline.Instance?.IsPlaying ?? false;
        if (!IsPreviewViewport)
            UpdateParticleSpawners(SceneObjects, (float)Mesh.DeltaTime, timelinePlaying);

        Mesh.AdvanceAnimatedTextures = timelinePlaying || MainWindow.IsAnimationRenderExportActive;
        int textureAnimFps = Math.Clamp(MainViewport.PropertiesPanel?.TextureAnimationFps ?? 20, 1, 240);
        Mesh.AnimatedTextureSpeedScale = textureAnimFps / 20.0;
        Mesh.ShadowsEnabled = false;
        Mesh.ShadowMapTexture = 0;
        Mesh.ShadowLightSpaceMatrix = mat4.Identity;
        Mesh.ShadowDebugMode = 0;
        bool globalShadows = MainViewport.PropertiesPanel?.ShadowsEnabled ?? true;
        Mesh.DirectionalShadowEnabled = globalShadows && (Mesh.MainFillLightCastsShadows ||
                        Mesh.SunFillLightCastsShadows ||
                        Mesh.MoonFillLightCastsShadows);
        Array.Clear(Mesh.PointShadowCubeTextures, 0, Mesh.PointShadowCubeTextures.Length);

        Dictionary<LightSceneObject, int> pointShadowIndices = new();
        var allPointLights = new List<(vec3 pos, vec3 color, float range, float energy, int shadowIndex, vec3 direction, float indirectEnergy, float spotCosOuterAngle, float spotCosInnerAngle)>();
        vec3 camPos = renderCamera.Position;

        if (renderMode == SceneRenderMode.Rendered)
        {
            Mesh.ShadowBlurStrength = Math.Clamp(MainViewport.PropertiesPanel?.ShadowBlurStrength ?? 1f, 0f, 4f);
            Mesh.SssEnabled = MainViewport.PropertiesPanel?.SubsurfaceEnabled == true;
            Mesh.SssBlurSamples = Math.Clamp(MainViewport.PropertiesPanel?.SubsurfaceBlurSamples ?? 8, 0, 32);
            Mesh.SssStrength = Math.Clamp(MainViewport.PropertiesPanel?.SubsurfaceStrength ?? 1f, 0f, 4f);
            Mesh.SssDesaturation = Math.Clamp(MainViewport.PropertiesPanel?.SubsurfaceDesaturation ?? 0f, 0f, 1f);
            Mesh.SssColorThreshold = Math.Clamp(MainViewport.PropertiesPanel?.SubsurfaceColorThreshold ?? 0f, 0f, 1f);
            float[] sssRadius = MainViewport.PropertiesPanel?.SubsurfaceRadiusRgb ?? [0.42f, 0.24f, 0.14f];
            Mesh.SssRadius = new vec3(
                Math.Clamp(sssRadius[0], 0.0001f, 8f),
                Math.Clamp(sssRadius[1], 0.0001f, 8f),
                Math.Clamp(sssRadius[2], 0.0001f, 8f));
            Mesh.SssHighlightSize = Math.Clamp(MainViewport.PropertiesPanel?.SubsurfaceHighlightSize ?? 1f, 0f, 8f);
            Mesh.SssHighlightStrength = Math.Clamp(MainViewport.PropertiesPanel?.SubsurfaceHighlightStrength ?? 1f, 0f, 8f);
            Mesh.SssHighlightSharpness = Math.Clamp(MainViewport.PropertiesPanel?.SubsurfaceHighlightSharpness ?? 2f, 0.01f, 16f);
            Mesh.SssHighlightDesaturation = Math.Clamp(MainViewport.PropertiesPanel?.SubsurfaceHighlightDesaturation ?? 0f, 0f, 1f);
            Mesh.SssHighlightColorThreshold = Math.Clamp(MainViewport.PropertiesPanel?.SubsurfaceHighlightColorThreshold ?? 0f, 0f, 1f);
            Mesh.SssAbsorption = Math.Clamp(MainViewport.PropertiesPanel?.SubsurfaceAbsorption ?? 0.35f, -0.95f, 0.95f);
            Mesh.ShadowDebugMode = effectivePassMode == RenderedPassMode.Shadow ? 1 : 0;
            if (Mesh.DirectionalShadowEnabled)
                RenderShadowMapPublic();
            if (globalShadows)
                pointShadowIndices = RenderPointShadowMapsPublic(camPos);
        }

        // ── Upload scene UBO ─────────────────────────────────────────────────
        if (_sceneUBO != null)
        {
            float fillStrength    = Math.Clamp(Mesh.GlobalFillLightStrength, 0f, 5f);
            var   fillColor       = new vec3(
                Math.Clamp(Mesh.GlobalFillLightColor.x, 0f, 1f),
                Math.Clamp(Mesh.GlobalFillLightColor.y, 0f, 1f),
                Math.Clamp(Mesh.GlobalFillLightColor.z, 0f, 1f));
            float sunStrength     = Math.Clamp(Mesh.GlobalSunFillLightStrength, 0f, 5f);
            float moonStrength    = Math.Clamp(Mesh.GlobalMoonFillLightStrength, 0f, 5f);
            float ambientStrength = Math.Clamp(Mesh.GlobalAmbientStrength, 0f, 5f);
            var   ambientColor    = new vec3(
                Math.Clamp(Mesh.GlobalAmbientColor.x, 0f, 1f),
                Math.Clamp(Mesh.GlobalAmbientColor.y, 0f, 1f),
                Math.Clamp(Mesh.GlobalAmbientColor.z, 0f, 1f));

            bool mainShadows = Mesh.MainFillLightCastsShadows && !Mesh.SunFillLightCastsShadows && !Mesh.MoonFillLightCastsShadows;
            bool sunShadows  = Mesh.SunFillLightCastsShadows && !Mesh.MoonFillLightCastsShadows;
            bool moonShadows = Mesh.MoonFillLightCastsShadows;

            _sceneUBO.Upload(
                Mesh.ShadowLightSpaceMatrix,
                ambientColor * ambientStrength,
                new vec3(1f, 1f, 1f).Normalized,
                fillColor * fillStrength,
                Mesh.GlobalSunFillLightDirection,
                Mesh.GlobalSunFillLightColor * sunStrength,
                Mesh.GlobalMoonFillLightDirection,
                Mesh.GlobalMoonFillLightColor * moonStrength,
                Mesh.ShadowDebugMode,
                mainShadows,
                sunShadows,
                moonShadows);
        }

        CollectPointLights(MainViewport.SceneObjects, renderMode, pointShadowIndices, previewScreenSpaceGi, allPointLights);

        if (wireframeMode)
            Gl.PolygonMode(GLEnum.FrontAndBack, PolygonMode.Line);

        if (MainViewport.GroundPlane != null && MainViewport.GroundPlaneVisible)
        {
            SelectPointLightsForMesh(vec3.Zero, allPointLights);
            RenderMeshWithModeOverride(MainViewport.GroundPlane, MainViewport.GroundPlaneModel, view, proj, forceUnshaded);
        }

        var renderItems = new List<(mat4, Mesh, float, bool, bool, bool)>();
        var renderItemsNoAo = new List<(mat4, Mesh, float, bool, bool, bool)>();
        var overlays = new List<(mat4, Mesh)>();
        Frustum previewFrustum = Frustum.FromViewProj(proj * view);
        CollectRenderPairs(MainViewport.SceneObjects, camPos, renderMode, previewFrustum, renderItems, renderItemsNoAo, overlays);

        bool splitAoObjects = renderMode == SceneRenderMode.Rendered &&
                              _ambientOcclusionShader != null &&
                              MainViewport.PropertiesPanel?.AmbientOcclusionEnabled == true;

        if (splitAoObjects)
        {
            RenderDepthOrdered(view, proj, renderItems, allPointLights, forceUnshaded);
        }
        else
        {
            renderItems.AddRange(renderItemsNoAo);
            RenderDepthOrdered(view, proj, renderItems, allPointLights, forceUnshaded);
        }
        if (wireframeMode)
            Gl.PolygonMode(GLEnum.FrontAndBack, PolygonMode.Fill);

        if (renderMode == SceneRenderMode.Rendered)
            RenderAmbientOcclusion(_previewFbo, _previewDepthTex, w, h, cam.Near, cam.Far, MainViewport.PropertiesPanel, effectivePassMode);

        if (splitAoObjects && effectivePassMode != RenderedPassMode.AmbientOcclusion)
            RenderDepthOrdered(view, proj, renderItemsNoAo, allPointLights, forceUnshaded);

        if (renderMode == SceneRenderMode.Rendered)
            ApplyIndirectLightingPassGeneric(_previewFbo, _previewColorTex, _previewDepthTex,
                _previewIndirectFbo, _previewIndirectTex, _previewIndirectPingFbo, _previewIndirectPingTex,
                w, h, cam.Near, cam.Far, MainViewport.PropertiesPanel, effectivePassMode);

        if (renderMode == SceneRenderMode.Rendered)
            ApplyGlowPassGeneric(_previewFbo, _previewColorTex, _previewGlowFbo, _previewGlowTex, _previewGlowPingFbo, _previewGlowPingTex, w, h, MainViewport.PropertiesPanel, effectivePassMode);

        // Live camera textures must include their camera's post effects even
        // when the preview viewport itself is in a shaded/unrendered mode.
        if ((renderMode == SceneRenderMode.Rendered || applyCameraEffects) &&
            effectivePassMode == RenderedPassMode.Combined)
            ApplyFilmGrainPass(_previewFbo, _previewColorTex, _previewGlowFbo, _previewGlowTex, w, h, sceneObj);

        if (OverlaysEnabled)
        {
            foreach (var (model, mesh) in overlays)
            {
                SelectPointLightsForMesh(new vec3(model.m30, model.m31, model.m32), allPointLights);
                mesh.Render(model, view, proj);
            }

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
        Mesh.IndirectWorldEnabled = false;
        Mesh.ShadowDebugMode = 0;
        Array.Clear(Mesh.PointShadowCubeTextures, 0, Mesh.PointShadowCubeTextures.Length);
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

    /// <summary>Processes retained-mode viewport input using the same camera, gizmo and pick path as ImGui.</summary>
    public unsafe void ProcessExternalPreviewInput(
        Camera camera, CameraSceneObject? sceneObject, Vector2 imageMin, Vector2 imageSize,
        Vector2 mousePosition, bool leftDown, bool rightDown, bool middleDown,
        bool leftPressed, bool rightPressed, bool leftReleased, bool rightReleased,
        float wheel, bool control, bool shift, float deltaTime, bool hovered,
        bool keyW = false, bool keyS = false, bool keyA = false, bool keyD = false,
        bool keyE = false, bool keyQ = false, bool keySpace = false,
        bool keyGPressed = false, bool keyRPressed = false)
    {
        if (!IsPreviewViewport) return;
        _previewInput.ProcessInput(camera, MainViewport?.Gizmo, imageMin, imageSize,
            GlfwApiPreview, GlfwWindowPreview, mousePosition,
            leftDown, rightDown, middleDown, leftPressed, rightPressed, leftReleased, rightReleased, wheel,
            keyW, keyS, keyA, keyD, keyE, keyQ, keySpace, shift, keyGPressed, keyRPressed,
            Math.Max(0.0001f, deltaTime), hovered, control, OverlaysEnabled);
        sceneObject?.SyncTransformFromCamera();

        if (!_previewInput.HasPendingPick) return;
        var (pickX, pickY) = _previewInput.PendingPickPosition;
        float relX = Math.Clamp((pickX - imageMin.X) / Math.Max(1f, imageSize.X), 0f, 1f);
        float relY = Math.Clamp((pickY - imageMin.Y) / Math.Max(1f, imageSize.Y), 0f, 1f);
        _pendingPreviewPickX = relX * _previewWidth;
        _pendingPreviewPickY = relY * _previewHeight;
        _pendingPreviewPickCtrl = _previewInput.PendingPickCtrl;
        _previewInput.ClearPendingPick();
    }

    public unsafe bool CaptureCurrentViewRgb(uint w, uint h, bool highQuality, out byte[] rgbPixels)
    {
        rgbPixels = Array.Empty<byte>();
        if (Gl == null || MainViewport == null || _previewFbo == 0 || w == 0 || h == 0)
            return false;

        var spawned = GetSpawnedCamerasPublic();
        var (activeCam, sceneObj) = GetRenderOutputCamera(spawned);

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

            Buffer.BlockCopy(rgbPixels, top, row, 0, stride);
            Buffer.BlockCopy(rgbPixels, bottom, rgbPixels, top, stride);
            Buffer.BlockCopy(row, 0, rgbPixels, bottom, stride);
        }
    }

    /// <summary>
    /// Returns the active render <see cref="Camera"/> based on <see cref="_activeCameraIndex"/>.
    /// Index 0 = work camera; 1+ = spawned cameras.
    /// Also returns the associated <see cref="CameraSceneObject"/> if applicable.
    /// </summary>
    private (Camera cam, CameraSceneObject? sceneObj) GetActiveRenderCamera()
    {
        if (_activeCameraIndex == RenderOutputIndex)
        {
            var spawnedRO = GetSpawnedCameras();
            return GetRenderOutputCamera(spawnedRO);
        }

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

    private unsafe void HandleCameraInput(Vector2 imageMin, Vector2 imageSize, Camera camera, CameraSceneObject? sceneObj)
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
            camera,
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

        // Keep spawned camera scene-object transform in sync when the active
        // viewport camera is a scene camera (distance ~= first-person mode).
        sceneObj?.SyncTransformFromCamera();

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
