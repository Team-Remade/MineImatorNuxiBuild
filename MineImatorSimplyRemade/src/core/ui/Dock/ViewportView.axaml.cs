using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MineImatorSimplyRemade.core.render;
using MineImatorSimplyRemade.core.window;
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
    public core.Camera Camera { get; } = new();

    private VeldridBitmapRenderSurface? _surface;
    private VeldridShadowMap? _shadowMap;
    private VeldridAmbientOcclusionPass? _aoPass;
    private VeldridIndirectLightingPass? _indirectPass;
    private VeldridMesh? _groundMesh;
    private VeldridMesh? _demoCube;
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

    public ViewportView()
    {
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
            BuildPlaceholderScene();
        }
        else
        {
            _surface.Resize(width, height);
        }

        _indirectPass?.Resize(width, height);
    }

    private void BuildPlaceholderScene()
    {
        if (_surface == null) return;

        _groundMesh = new VeldridMesh(_surface.GraphicsDevice) { Albedo = new Vector3(0.35f, 0.45f, 0.3f), Unlit = false };
        AddQuad(_groundMesh, new Vector3(-25, 0, -25), new Vector3(25, 0, -25), new Vector3(25, 0, 25), new Vector3(-25, 0, 25), Vector3.UnitY);
        _groundMesh.Upload(_surface.OutputDescription);

        _demoCube = new VeldridMesh(_surface.GraphicsDevice) { Albedo = new Vector3(0.8f, 0.35f, 0.25f), Unlit = false };
        BuildUnitCube(_demoCube, new Vector3(1, 1, 1));
        _demoCube.Upload(_surface.OutputDescription);
    }

    private static void AddQuad(VeldridMesh mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        foreach (Vector3 p in new[] { a, b, c, a, c, d })
        {
            mesh.Vertices.Add(p);
            mesh.Normals.Add(normal);
            mesh.TexCoords.Add(Vector2.Zero);
        }
    }

    private static void BuildUnitCube(VeldridMesh mesh, Vector3 size)
    {
        Vector3 h = size * 0.5f;
        var faces = new (Vector3 n, Vector3[] q)[]
        {
            (new Vector3(0, 0, 1), new[] { new Vector3(-h.X, -h.Y, h.Z), new Vector3(h.X, -h.Y, h.Z), new Vector3(h.X, h.Y, h.Z), new Vector3(-h.X, h.Y, h.Z) }),
            (new Vector3(0, 0, -1), new[] { new Vector3(h.X, -h.Y, -h.Z), new Vector3(-h.X, -h.Y, -h.Z), new Vector3(-h.X, h.Y, -h.Z), new Vector3(h.X, h.Y, -h.Z) }),
            (new Vector3(0, 1, 0), new[] { new Vector3(-h.X, h.Y, h.Z), new Vector3(h.X, h.Y, h.Z), new Vector3(h.X, h.Y, -h.Z), new Vector3(-h.X, h.Y, -h.Z) }),
            (new Vector3(0, -1, 0), new[] { new Vector3(-h.X, -h.Y, -h.Z), new Vector3(h.X, -h.Y, -h.Z), new Vector3(h.X, -h.Y, h.Z), new Vector3(-h.X, -h.Y, h.Z) }),
            (new Vector3(1, 0, 0), new[] { new Vector3(h.X, -h.Y, h.Z), new Vector3(h.X, -h.Y, -h.Z), new Vector3(h.X, h.Y, -h.Z), new Vector3(h.X, h.Y, h.Z) }),
            (new Vector3(-1, 0, 0), new[] { new Vector3(-h.X, -h.Y, -h.Z), new Vector3(-h.X, -h.Y, h.Z), new Vector3(-h.X, h.Y, h.Z), new Vector3(-h.X, h.Y, -h.Z) }),
        };
        foreach (var (n, q) in faces)
        {
            foreach (int i in new[] { 0, 1, 2, 0, 2, 3 })
            {
                mesh.Vertices.Add(q[i]);
                mesh.Normals.Add(n);
                mesh.TexCoords.Add(Vector2.Zero);
            }
        }
    }

    private void RenderFrame()
    {
        if (_surface == null || _shadowMap == null || _groundMesh == null || _demoCube == null)
            return;

        double now = _clock.Elapsed.TotalSeconds;
        float deltaTime = _lastFrameSeconds > 0 ? (float)(now - _lastFrameSeconds) : 1f / 60f;
        _lastFrameSeconds = now;
        ProcessFreeFlyMovement(deltaTime);
        UpdateFps(now);

        float aspect = _surface.Width / (float)_surface.Height;
        Matrix4x4 view = ToNumerics(Camera.GetViewMatrix());
        Matrix4x4 proj = ToNumerics(Camera.GetProjectionMatrix(aspect));

        Vector3 lightDir = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.35f));
        Matrix4x4 lightSpace = VeldridShadowMap.ComputeLightSpaceMatrix(lightDir, Vector3.Zero, extent: 40f, near: 0.1f, far: 120f);

        var sceneData = SceneDataUniforms.Default;
        sceneData.LightSpaceMatrix = lightSpace;
        sceneData.LightDir = -lightDir;
        sceneData.MainLightCastsShadows = 1;
        sceneData.UseShadowMap = 1;
        _surface.UpdateSceneData(sceneData);
        _surface.UpdatePointLights(PointLightUniforms.Empty);

        var environment = SceneEnvironmentUniforms.Default;
        environment.CameraPosition = ToNumerics(Camera.Position);
        _surface.UpdateEnvironment(environment);

        _shadowMap.RenderShadowPass(cl =>
        {
            _groundMesh.RenderDepthOnly(cl, Matrix4x4.Identity * lightSpace, _shadowMap.Framebuffer.OutputDescription);
            _demoCube.RenderDepthOnly(cl, Matrix4x4.CreateTranslation(0, 0.5f, 0) * lightSpace, _shadowMap.Framebuffer.OutputDescription);
        });

        var bitmap = _surface.RenderFrame(new RgbaFloat(0.08f, 0.09f, 0.11f, 1f), cl =>
        {
            _groundMesh.Render(cl, Matrix4x4.Identity, view, proj, _surface.SceneDataBuffer, _surface.PointLightBuffer, _surface.EnvironmentBuffer, _shadowMap);
            _demoCube.Render(cl, Matrix4x4.CreateTranslation(0, 0.5f, 0), view, proj, _surface.SceneDataBuffer, _surface.PointLightBuffer, _surface.EnvironmentBuffer, _shadowMap);

            _aoPass?.Render(cl, _surface.DepthTargetView, _surface.Width, _surface.Height, Camera.Near, Camera.Far, _surface.OutputDescription);

            if (_indirectPass != null)
            {
                _indirectPass.RenderRaw(cl, _surface.ColorTargetView, _surface.DepthTargetView, Camera.Near, Camera.Far);
                cl.SetFramebuffer(_surface.Framebuffer);
                _indirectPass.CompositeDenoised(cl, _surface.DepthTargetView, Camera.Near, Camera.Far, _surface.OutputDescription);
            }
        });

        SceneImage.Source = bitmap;
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
