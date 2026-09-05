using System.Numerics;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.render;
using MineImatorSimplyRemade.core.window.windows;
using MineImatorSimplyRemade.gizmo;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class Viewport
{
    private enum SceneRenderMode
    {
        Unrendered,
        Rendered
    }

    public enum ViewportRenderMode
    {
        Wireframe,
        FlatUnshaded,
        Shaded,
        Rendered
    }

    public enum RenderedPassMode
    {
        Combined,
        AmbientOcclusion,
        Shadow
    }

    // ── Scene ──────────────────────────────────────────────────────────────────

    public List<SceneObject> SceneObjects { get; } = new();

    /// <summary>Seconds since the previous frame, set by the hosting view each tick
    /// (replaces the old ImGui.GetIO().DeltaTime read).</summary>
    public float FrameDeltaTime { get; set; } = 1f / 60f;

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

    // ── Ground plane ───────────────────────────────────────────────────────────

    /// <summary>
    /// The XZ-plane ground mesh that displays the tiled terrain texture.
    /// Initialised in <see cref="InitGroundPlane"/> after atlases are loaded.
    /// Exposed so the <see cref="CameraViewport"/> can render the same ground plane.
    /// </summary>
    public PlaneMesh? GroundPlane => _groundPlane;
    private PlaneMesh? _groundPlane;
    private mat4 _groundPlaneModel = mat4.Identity;
    public mat4 GroundPlaneModel => _groundPlaneModel;
    public bool GroundPlaneVisible { get; private set; } = true;
    public string GroundTileAtlas { get; private set; } = "block";
    public string GroundTileKey { get; private set; } = "grass_block_top";

    // ── Background image plane ───────────────────────────────────────────────

    /// <summary>Background image texture (null when no image is loaded). The
    /// Avalonia/Veldrid viewport reads this to draw the background plane.</summary>
    public Veldrid.Texture? BackgroundTexture { get; private set; }
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

    // Screen-space mouse position captured on left-button release inside the
    // preview viewport so the colour-pick read-back can happen inside the
    // preview render (GL context active). NaN means no pick is pending.
    private float _pendingPreviewPickX = float.NaN;
    private float _pendingPreviewPickY = float.NaN;
    private bool  _pendingPreviewPickCtrl;

    /// <summary>True while an undocked CameraWindow owns the rendering.</summary>
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

    // ── Ground plane setup ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a camera-following tiled XZ ground plane and assigns the <c>grass_block_top</c>
    /// texture as its surface.  Must be called after both the graphics device and
    /// <see cref="TerrainAtlas"/> are initialised.
    /// </summary>
    public void InitGroundPlane()
    {
        // Keep the quad reasonably sized to avoid visible interpolation artifacts
        // on very large two-triangle planes; recentering still makes it feel infinite.
        _groundPlane = new PlaneMesh(512f, 512f, PlaneOrientation.XZ);

        // One texture tile per world unit (block); the albedo sampler wraps.
        for (int i = 0; i < _groundPlane.TexCoords.Count; i++)
            _groundPlane.TexCoords[i] *= 512f;
        _groundPlane.Upload(VeldridContext.StandardOutputDescription);

        SetGroundPlaneTexture("block", "grass_block_top");
    }

    /// <summary>
    /// Recenters the ground plane under the camera target, snapped to whole
    /// blocks so the world-anchored UV tiling doesn't visibly swim.
    /// </summary>
    public void UpdateGroundPlaneFollow(vec3 cameraTarget)
    {
        _groundPlaneModel = mat4.Translate(MathF.Floor(cameraTarget.x), 0f, MathF.Floor(cameraTarget.z));
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
        if (!atlas.TryGetValue(normalizedKey, out var tileTexture))
            return false;

        _groundPlane.AlbedoTexture = tileTexture;
        _groundPlane.Albedo = Vector3.One;
        if (normalizedAtlas == "block" && MinecraftModelMesh.TryGetBiomeTintForTextureKey(normalizedKey, out Vector3 tint))
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

    public void SetRenderMode(ViewportRenderMode mode)
    {
        _viewportRenderMode = mode;
        if (_viewportRenderMode != ViewportRenderMode.Rendered)
            _renderedPassMode = RenderedPassMode.Combined;
        SyncLegacyRenderFlags();
    }

    public void SetRenderedPass(RenderedPassMode pass)
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

    public void ReloadSkyTextures()
    {
        // TODO(migration): sun/moon/cloud sky textures load here once the sky
        // renderer is ported to Veldrid in ViewportView.
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
                              BackgroundTexture != null;
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

    private void TryLoadBackgroundTexture(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            ImageResult img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            if (img.Data == null || img.Data.Length == 0)
                return;

            _backgroundImageWidth = img.Width;
            _backgroundImageHeight = img.Height;

            BackgroundTexture = VeldridTextureLoader.UploadRgba(
                img.Data, (uint)img.Width, (uint)img.Height, nearest: false, repeat: false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load background image '{path}': {ex.Message}");
            DisposeBackgroundTexture();
        }
    }

    private void DisposeBackgroundTexture()
    {
        BackgroundTexture?.Dispose();
        BackgroundTexture = null;
        _backgroundImageWidth = 0;
        _backgroundImageHeight = 0;
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
   
    public static mat4 GetRenderableWorldMatrix(SceneObject obj, vec3 cameraPos)
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

    public void UpdateParticleSpawners(IEnumerable<SceneObject> objects, float deltaTime, bool timelinePlaying)
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
    public static Dictionary<string, BoneSceneObject> BuildBoneDictionary(SceneObject root)
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

    public bool CaptureCurrentViewRgb(uint w, uint h, bool highQuality, out byte[] rgbPixels)
    {
        rgbPixels = Array.Empty<byte>();
        // TODO(migration): re-implement against the Veldrid render surface
        // (offscreen VeldridBitmapRenderSurface render + staging-texture readback)
        // once the ViewportView pipeline renders real scene content.
        return false;
    }

    /// <summary>
    /// Returns the active render <see cref="Camera"/> based on <see cref="_activeCameraIndex"/>.
    /// Index 0 = work camera; 1+ = spawned cameras.
    /// Also returns the associated <see cref="CameraSceneObject"/> if applicable.
    /// </summary>
    public (Camera cam, CameraSceneObject? sceneObj) GetActiveRenderCamera()
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
}
