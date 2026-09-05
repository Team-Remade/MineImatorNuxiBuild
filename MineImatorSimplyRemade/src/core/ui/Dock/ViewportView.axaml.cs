using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MineImatorSimplyRemade.core.render;
using MineImatorSimplyRemade.core.window;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Veldrid;

namespace MineImatorSimplyRemade.core.ui.Dock;

/// <summary>
/// The main 3D editor viewport. Ported from <c>core.ui.Panels.Viewport</c>'s UI
/// chrome and camera input (orbit/pan/zoom/free-fly - see <c>core.Input</c>'s
/// <c>ProcessInput</c>, which this reimplements directly against Avalonia
/// pointer/keyboard events instead of ImGui IO polling + GLFW cursor warping).
///
/// MIGRATION STATUS: this ports the viewport shell - camera navigation, the
/// full Veldrid render pipeline (directional shadow map, ambient occlusion,
/// indirect lighting), and a placeholder ground plane + demo cube standing in
/// for real scene content. Explicitly NOT ported yet (each is its own
/// follow-up item):
///   - real <c>SceneObject</c> rendering (every scene object type still owns a
///     GL-based <c>core.mdl.Mesh</c>, not a <see cref="VeldridMesh"/> - once
///     those are migrated, replace BuildPlaceholderScene's demo cube with a
///     loop over the actual scene graph)
///   - the 3D manipulation gizmo (<c>Gizmo3D</c> - not ported), click-to-select
///     picking (VeldridMesh.RenderPick/RenderSilhouette are ready and wired
///     into the pipeline below, just not driven by real scene objects yet)
///   - sky/background-image-plane rendering, camera-feed textures, particle
///     preview, point-light shadow cubemaps for real scene lights
///   - glow/film-grain post effects (built in VeldridGlowPass/VeldridFilmGrainPass,
///     not yet wired into this control's per-frame render call)
/// </summary>
public partial class ViewportView : UserControl
{
    /// <summary>The UI-agnostic viewport model (scene objects, work camera,
    /// ground plane, render-mode state). Owned by <c>MainWindow</c>.</summary>
    public Panels.Viewport Model { get; }

    public core.Camera Camera => Model.Camera;

    private VeldridBitmapRenderSurface? _surface;
    private VeldridShadowMap? _shadowMap;
    private VeldridAmbientOcclusionPass? _aoPass;
    private VeldridIndirectLightingPass? _indirectPass;
    private VeldridGlowPass? _glowPass;
    private VeldridFilmGrainPass? _filmGrainPass;
    private float _filmGrainFrame;
    private readonly List<(Matrix4x4 World, VeldridMesh Mesh)> _renderItems = new();
    private readonly DispatcherTimer _renderTimer;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private int _framesThisSecond;
    private double _lastFpsSampleSeconds;

    // ── Orbit/pan/free-fly input state (ported from core.Input) ─────────────
    private bool _dragging;
    private bool _panning;
    private bool _freeFlyActive;
    private float _freeFlySpeed = 5f;
    private Point _pressPos;
    private Point _lastOrbitPos;
    private Point _lastPanPos;
    private const float OrbitDragThreshold = 4f;
    private const float FreeFlyLookSensitivity = 0.003f;
    private readonly HashSet<Key> _heldKeys = new();
    private double _lastFrameSeconds;

    public ViewportView(Panels.Viewport model)
    {
        Model = model;
        InitializeComponent();

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0) };
        _renderTimer.Tick += (_, _) => RenderFrame();

        AttachedToVisualTree += (_, _) => _renderTimer.Start();
        DetachedFromVisualTree += (_, _) => _renderTimer.Stop();

        SceneImage.SizeChanged += (_, e) => EnsureSurface((uint)Math.Max(1, e.NewSize.Width), (uint)Math.Max(1, e.NewSize.Height));

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += (_, e) => _heldKeys.Add(e.Key);
        KeyUp += (_, e) => _heldKeys.Remove(e.Key);
    }

    private void EnsureSurface(uint width, uint height)
    {
        if (_surface == null)
        {
            _surface = new VeldridBitmapRenderSurface(width, height);
            _shadowMap = new VeldridShadowMap(_surface.GraphicsDevice, 2048);
            _aoPass = new VeldridAmbientOcclusionPass(_surface.GraphicsDevice) { Radius = 5f, Strength = 0.7f };
            _indirectPass = new VeldridIndirectLightingPass(_surface.GraphicsDevice);
            _glowPass = new VeldridGlowPass(_surface.GraphicsDevice) { Strength = 0.6f, BlurSize = 2f };
            _filmGrainPass = new VeldridFilmGrainPass(_surface.GraphicsDevice) { Strength = 0.03f };
        }
        else
        {
            _surface.Resize(width, height);
        }

        _indirectPass?.Resize(width, height);
        _glowPass?.Resize(width, height);
        _filmGrainPass?.Resize(width, height);
    }

    private void RenderFrame()
    {
        if (_surface == null || _shadowMap == null)
            return;

        double now = _clock.Elapsed.TotalSeconds;
        float deltaTime = _lastFrameSeconds > 0 ? (float)(now - _lastFrameSeconds) : 1f / 60f;
        _lastFrameSeconds = now;
        Model.FrameDeltaTime = deltaTime;
        ProcessFreeFlyMovement(deltaTime);
        UpdateFps(now);

        // Lazy ground-plane init: atlases may not be loaded when the surface is
        // first created, so retry the texture until the tile resolves.
        if (Model.GroundPlane == null)
            Model.InitGroundPlane();
        else if (Model.GroundPlane.AlbedoTexture == null)
            Model.SetGroundPlaneTexture(Model.GroundTileAtlas, Model.GroundTileKey);

        Model.UpdateParticleSpawners(Model.SceneObjects, deltaTime, Panels.Timeline.Instance?.IsPlaying ?? false);

        var (renderCam, _) = Model.GetActiveRenderCamera();
        float aspect = _surface.Width / (float)_surface.Height;
        Matrix4x4 view = ToNumerics(renderCam.GetViewMatrix());
        Matrix4x4 proj = ToNumerics(renderCam.GetProjectionMatrix(aspect));

        Model.UpdateGroundPlaneFollow(renderCam.Target);
        Matrix4x4 groundModel = ToNumerics(Model.GroundPlaneModel);
        VeldridMesh? ground = Model.GroundPlaneVisible ? Model.GroundPlane : null;

        // Flatten the scene graph into (world, mesh) draw pairs for this frame.
        _renderItems.Clear();
        foreach (var root in Model.SceneObjects)
        {
            Dictionary<string, BoneSceneObject>? bones = null;
            CollectRenderables(root, renderCam.Position, ref bones);
        }

        // TODO(migration): derive the sun direction/shadows from PropertiesPanel
        // sky settings (old RenderSky/ComputeShadowLightSpaceMatrix logic).
        Vector3 lightDir = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.35f));
        Matrix4x4 lightSpace = VeldridShadowMap.ComputeLightSpaceMatrix(lightDir, ToNumerics(renderCam.Target), extent: 60f, near: 0.1f, far: 200f);

        var sceneData = SceneDataUniforms.Default;
        sceneData.LightSpaceMatrix = lightSpace;
        sceneData.LightDir = -lightDir;
        sceneData.MainLightCastsShadows = 1;
        sceneData.UseShadowMap = 1;
        _surface.UpdateSceneData(sceneData);
        _surface.UpdatePointLights(PointLightUniforms.Empty);

        var environment = SceneEnvironmentUniforms.Default;
        environment.CameraPosition = ToNumerics(renderCam.Position);
        _surface.UpdateEnvironment(environment);

        _shadowMap.RenderShadowPass(cl =>
        {
            ground?.RenderDepthOnly(cl, groundModel * lightSpace, _shadowMap.Framebuffer.OutputDescription);
            foreach (var (world, mesh) in _renderItems)
                mesh.RenderDepthOnly(cl, world * lightSpace, _shadowMap.Framebuffer.OutputDescription);
        });

        float[] bg = Model.PropertiesPanel?.BackgroundColor ?? [0.08f, 0.09f, 0.11f, 1f];
        var bitmap = _surface.RenderFrame(new RgbaFloat(bg[0], bg[1], bg[2], 1f), cl =>
        {
            ground?.Render(cl, groundModel, view, proj, _surface.SceneDataBuffer, _surface.PointLightBuffer, _surface.EnvironmentBuffer, _shadowMap);

            foreach (var (world, mesh) in _renderItems)
                mesh.Render(cl, world, view, proj, _surface.SceneDataBuffer, _surface.PointLightBuffer, _surface.EnvironmentBuffer, _shadowMap);

            _aoPass?.Render(cl, _surface.DepthTargetView, _surface.Width, _surface.Height, renderCam.Near, renderCam.Far, _surface.OutputDescription);

            if (_indirectPass != null)
            {
                _indirectPass.RenderRaw(cl, _surface.ColorTargetView, _surface.DepthTargetView, renderCam.Near, renderCam.Far);
                cl.SetFramebuffer(_surface.Framebuffer);
                _indirectPass.CompositeDenoised(cl, _surface.DepthTargetView, renderCam.Near, renderCam.Far, _surface.OutputDescription);
            }

            // Bloom/glow: extract+blur the bright scene pixels and composite them
            // additively back onto the main framebuffer (post-lighting).
            _glowPass?.Render(cl, _surface.ColorTargetView, _surface.Framebuffer);

            // Film grain applied last, in place on the color target.
            if (_filmGrainPass != null)
            {
                _filmGrainPass.Frame = _filmGrainFrame;
                _filmGrainFrame += 1f;
                _filmGrainPass.Render(cl, _surface.ColorTarget, _surface.ColorTargetView);
            }
        });

        SceneImage.Source = bitmap;
    }

    /// <summary>
    /// Recursively flattens visible scene objects into <see cref="_renderItems"/>,
    /// resolving each object's render world matrix (camera-facing planes etc.)
    /// and refreshing bone matrices for skinned meshes. The bone dictionary is
    /// built lazily once per scene root and shared down its subtree.
    /// </summary>
    private void CollectRenderables(SceneObject obj, GlmSharp.vec3 cameraPos, ref Dictionary<string, BoneSceneObject>? bones)
    {
        if (!obj.GetEffectiveVisibility()) return;

        if (obj.Visuals.Count > 0)
        {
            GlmSharp.mat4 world = Panels.Viewport.GetRenderableWorldMatrix(obj, cameraPos);
            Matrix4x4 worldN = ToNumerics(world);

            foreach (var mesh in obj.Visuals)
            {
                if (mesh.PickOnly || mesh.Vertices.Count == 0) continue;

                if (mesh.IsSkinned)
                {
                    bones ??= Panels.Viewport.BuildBoneDictionary(FindRoot(obj));
                    UpdateBoneMatrices(mesh, world, bones);
                }

                mesh.CullFrontFaces = obj.GetEffectiveInvertFaces();
                _renderItems.Add((worldN, mesh));
            }
        }

        foreach (var child in obj.Children)
            CollectRenderables(child, cameraPos, ref bones);
    }

    private static SceneObject FindRoot(SceneObject obj)
    {
        var root = obj;
        while (root.Parent != null) root = root.Parent;
        return root;
    }

    /// <summary>
    /// Ported from the old GL <c>Viewport.UpdateBoneMatrices</c>: writes each
    /// bone's mesh-local skinning matrix (inverse bind × current pose) into
    /// <see cref="VeldridMesh.BoneMatrices"/> for the vertex shader.
    /// </summary>
    private static void UpdateBoneMatrices(VeldridMesh mesh, GlmSharp.mat4 meshWorld, Dictionary<string, BoneSceneObject> bones)
    {
        if (mesh.BoneNames.Count == 0) return;

        mesh.BoneMatrices ??= new List<Matrix4x4>(mesh.BoneNames.Count);
        while (mesh.BoneMatrices.Count < mesh.BoneNames.Count)
            mesh.BoneMatrices.Add(Matrix4x4.Identity);

        GlmSharp.mat4 invMeshWorld = meshWorld.Inverse;
        for (int i = 0; i < mesh.BoneNames.Count; i++)
        {
            if (!bones.TryGetValue(mesh.BoneNames[i], out var bone))
            {
                mesh.BoneMatrices[i] = Matrix4x4.Identity;
                continue;
            }

            Matrix4x4 poseInMeshSpace = ToNumerics(invMeshWorld * bone.GetWorldMatrix());
            Matrix4x4 invBind = i < mesh.BoneInverseBindMatrices.Count ? mesh.BoneInverseBindMatrices[i] : Matrix4x4.Identity;
            mesh.BoneMatrices[i] = invBind * poseInMeshSpace;
        }
    }

    private static Matrix4x4 ToNumerics(GlmSharp.mat4 m) => new(
        m.m00, m.m01, m.m02, m.m03,
        m.m10, m.m11, m.m12, m.m13,
        m.m20, m.m21, m.m22, m.m23,
        m.m30, m.m31, m.m32, m.m33);

    private static Vector3 ToNumerics(GlmSharp.vec3 v) => new(v.x, v.y, v.z);

    private void UpdateFps(double nowSeconds)
    {
        _framesThisSecond++;
        if (nowSeconds - _lastFpsSampleSeconds >= 1.0)
        {
            FpsText.Text = $"{_framesThisSecond} fps";
            _framesThisSecond = 0;
            _lastFpsSampleSeconds = nowSeconds;
        }
    }

    // ── Camera input: orbit (left-drag), pan (middle-drag), zoom (scroll),
    //    free-fly (right-hold + WASD/QE) - ported from core.Input.ProcessInput.
    //    Avalonia pointer capture replaces GLFW's cursor-disable + recenter-each-
    //    frame trick: while captured, PointerMoved keeps delivering deltas even
    //    past the control's bounds, so no manual mouse-wrap/warp is needed. ──

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        Point pos = e.GetPosition(this);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressPos = pos;
            _lastOrbitPos = pos;
        }
        else if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            _panning = true;
            _lastPanPos = pos;
            e.Pointer.Capture(this);
        }
        else if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            _freeFlyActive = true;
            Cursor = new Cursor(StandardCursorType.None);
            e.Pointer.Capture(this);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        Point pos = e.GetPosition(this);
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;

        if (props.IsLeftButtonPressed)
        {
            if (!_dragging)
            {
                double moveDist = Math.Sqrt(Math.Pow(pos.X - _pressPos.X, 2) + Math.Pow(pos.Y - _pressPos.Y, 2));
                if (moveDist >= OrbitDragThreshold)
                {
                    _dragging = true;
                    e.Pointer.Capture(this);
                }
            }

            if (_dragging)
            {
                float dx = (float)(pos.X - _lastOrbitPos.X);
                float dy = (float)(pos.Y - _lastOrbitPos.Y);
                Camera.Orbit(dx * 0.005f, dy * 0.005f);
            }
        }

        if (_panning && props.IsMiddleButtonPressed)
        {
            float dx = (float)(pos.X - _lastPanPos.X);
            float dy = (float)(pos.Y - _lastPanPos.Y);
            if (Camera.Distance < 0.01f)
            {
                var view = Camera.GetViewMatrix();
                var right = new GlmSharp.vec3(view.m00, view.m10, view.m20);
                var up = new GlmSharp.vec3(view.m01, view.m11, view.m21);
                Camera.Target += right * (-dx * 0.05f) + up * (dy * 0.05f);
            }
            else
            {
                Camera.Pan(-dx * 0.01f * (Camera.Distance / 5f), dy * 0.01f * (Camera.Distance / 5f));
            }
            _lastPanPos = pos;
        }

        if (_freeFlyActive)
        {
            // Pointer capture keeps this control receiving moves even while the
            // (hidden) cursor visually stays near its capture point on most
            // platforms - unlike GLFW's raw-delta mode, Avalonia gives us
            // absolute positions, so just diff against the previous move.
            float lookDx = (float)(pos.X - _lastOrbitPos.X) * FreeFlyLookSensitivity;
            float lookDy = -(float)(pos.Y - _lastOrbitPos.Y) * FreeFlyLookSensitivity;
            Camera.Look(lookDx, lookDy);
        }

        _lastOrbitPos = pos;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            // TODO(migration): queue a color-ID pick here once real SceneObjects
            // and VeldridMesh.RenderPick are wired together (see class doc).
            _dragging = false;
        }
        else if (e.InitialPressMouseButton == MouseButton.Middle)
        {
            _panning = false;
        }
        else if (e.InitialPressMouseButton == MouseButton.Right)
        {
            _freeFlyActive = false;
            Cursor = Cursor.Default;
        }

        if (!_dragging && !_panning && !_freeFlyActive)
            e.Pointer.Capture(null);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_freeFlyActive)
        {
            float factor = e.Delta.Y > 0 ? 1.3f : 1f / 1.3f;
            for (int i = 0; i < Math.Max(1, (int)Math.Abs(e.Delta.Y)); i++)
                _freeFlySpeed *= factor;
            _freeFlySpeed = Math.Clamp(_freeFlySpeed, 0.1f, 500f);
            return;
        }

        if (Camera.Distance < 0.01f) return;
        Camera.Zoom((float)e.Delta.Y * Camera.Distance * 0.1f);
    }

    private void ProcessFreeFlyMovement(float deltaTime)
    {
        if (!_freeFlyActive) return;

        float distanceForSpeed = Camera.Distance < 0.01f ? core.Camera.DefaultDistance : Camera.Distance;
        float speed = _freeFlySpeed * distanceForSpeed * 0.2f;
        if (_heldKeys.Contains(Key.Space)) speed *= 2.5f;
        else if (_heldKeys.Contains(Key.LeftShift) || _heldKeys.Contains(Key.RightShift)) speed *= 0.4f;

        float fwd = 0f, rt = 0f, up = 0f;
        if (_heldKeys.Contains(Key.W)) fwd += speed * deltaTime;
        if (_heldKeys.Contains(Key.S)) fwd -= speed * deltaTime;
        if (_heldKeys.Contains(Key.D)) rt += speed * deltaTime;
        if (_heldKeys.Contains(Key.A)) rt -= speed * deltaTime;
        if (_heldKeys.Contains(Key.E)) up += speed * deltaTime;
        if (_heldKeys.Contains(Key.Q)) up -= speed * deltaTime;
        if (fwd != 0f || rt != 0f || up != 0f)
            Camera.MoveFreeFly(fwd, rt, up);

        if (_heldKeys.Contains(Key.R))
            Camera.ResetToDefaultPose();
    }
}
