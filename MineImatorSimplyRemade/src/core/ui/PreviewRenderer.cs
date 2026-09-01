using System.Numerics;
using Avalonia.Media.Imaging;
using GlmSharp;
using MineImatorSimplyRemade.core.render;
using MineImatorSimplyRemade.core.window;
using MineImatorSimplyRemadeNuxi.core.objs;
using Veldrid;

namespace MineImatorSimplyRemade.core.ui;

/// <summary>
/// Off-screen renderer used by the spawn-menu preview column.
///
/// Ported from the original Silk.NET.OpenGL FBO implementation to Veldrid: it now
/// owns a fixed-size <see cref="VeldridBitmapRenderSurface"/>
/// (<see cref="TextureSize"/> × <see cref="TextureSize"/>) on the shared
/// <see cref="VeldridContext"/> device and renders <see cref="VeldridMesh"/>
/// instances into it. Call <see cref="Render"/> with a list of meshes and a
/// selection key each frame; the result is returned (and cached in
/// <see cref="CurrentBitmap"/>) as an Avalonia <see cref="WriteableBitmap"/> that
/// can be displayed directly in an <c>&lt;Image&gt;</c> control.
///
/// The preview orbits automatically so the object rotates slowly when nothing
/// is interacted with, and the user can drag to orbit manually.
/// </summary>
public class PreviewRenderer : IDisposable
{
    // ── Surface resources ─────────────────────────────────────────────────────
    public const int TextureSize = 256;

    /// <summary>
    /// The most recently rendered preview image, or <c>null</c> before the first
    /// <see cref="Render"/> call. The same instance is returned by
    /// <see cref="Render"/> and reused across frames (its pixels are overwritten
    /// in place by the underlying render surface).
    /// </summary>
    public WriteableBitmap? CurrentBitmap { get; private set; }

    // ── Auto-orbit state ──────────────────────────────────────────────────────
    /// <summary>Horizontal orbit angle in radians. Advances each frame.</summary>
    public float Yaw   = 0.75f;

    /// <summary>Vertical tilt in radians (positive = looking slightly down).</summary>
    public float Pitch = 0.4f;

    /// <summary>Distance from target to camera eye (world units).</summary>
    public float Distance = 2.2f;

    /// <summary>Auto-rotation speed in radians per second.</summary>
    public float AutoRotateSpeed = 0.6f;

    // ── Render surface ────────────────────────────────────────────────────────
    private VeldridBitmapRenderSurface? _surface;

    // ── Dirty tracking ────────────────────────────────────────────────────────
    /// <summary>
    /// Opaque token for the last selection rendered.  Set by the caller; when it
    /// changes the preview is re-rendered immediately (dirty = true) on the next frame.
    /// </summary>
    public string LastSelectionKey { get; private set; } = "";

    // ── Constructor ───────────────────────────────────────────────────────────

    public PreviewRenderer()
    {
    }

    // ── Initialization ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the offscreen render surface. Safe to call more than once;
    /// previous resources are released first.
    /// </summary>
    public void Initialize()
    {
        Dispose(); // release previous resources if any
        _surface = new VeldridBitmapRenderSurface(TextureSize, TextureSize);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders <paramref name="meshes"/> into the preview surface and returns the
    /// resulting Avalonia bitmap (also stored in <see cref="CurrentBitmap"/>).
    ///
    /// <paramref name="selectionKey"/> is compared to <see cref="LastSelectionKey"/>;
    /// when they differ the orbit is reset.  When they are the same the orbit
    /// continues advancing so the auto-rotation is visible.
    ///
    /// <paramref name="deltaTime"/> drives the auto-orbit animation.
    ///
    /// When <paramref name="sceneRoot"/> is non-null the scene-object hierarchy is
    /// rendered in addition to (or instead of) the flat <paramref name="meshes"/> list.
    /// Each node in the hierarchy is rendered with its own world matrix so that a
    /// character's bones appear in their correct poses.
    /// </summary>
    public WriteableBitmap? Render(
        IReadOnlyList<VeldridMesh> meshes,
        string                     selectionKey,
        double                     deltaTime,
        float                      boundsRadius = 0.75f,
        SceneObject?               sceneRoot    = null)
    {
        if (_surface == null) Initialize();
        if (_surface == null) return CurrentBitmap;

        bool selectionChanged = selectionKey != LastSelectionKey;
        if (selectionChanged)
        {
            LastSelectionKey = selectionKey;
            Yaw   = 0.75f;
            Pitch = 0.4f;
        }

        // Advance auto-orbit
        Yaw += AutoRotateSpeed * (float)deltaTime;

        // ── Camera matrices ───────────────────────────────────────────────────
        float cosP = MathF.Cos(Pitch);
        var eye = new vec3(
            cosP * MathF.Sin(Yaw),
            MathF.Sin(Pitch),
            cosP * MathF.Cos(Yaw)) * Distance;

        Matrix4x4 view = ToNumerics(mat4.LookAt(eye, vec3.Zero, vec3.UnitY));
        Matrix4x4 proj = ToNumerics(mat4.Perspective(
            glm.Radians(50f),   // 50° FOV
            1.0f,               // 1:1 square surface
            0.05f,
            100f));

        // No shadows in the preview; keep the scene data neutral.
        var sceneData = SceneDataUniforms.Default;
        sceneData.UseShadowMap = 0;
        sceneData.MainLightCastsShadows = 0;
        _surface.UpdateSceneData(sceneData);
        _surface.UpdatePointLights(PointLightUniforms.Empty);

        var environment = SceneEnvironmentUniforms.Default;
        environment.CameraPosition = ToNumerics(eye);
        _surface.UpdateEnvironment(environment);

        // Dark charcoal background (0.12, 0.12, 0.14, 1)
        CurrentBitmap = _surface.RenderFrame(new RgbaFloat(0.12f, 0.12f, 0.14f, 1.0f), cl =>
        {
            // ── Flat mesh list (Items, Blocks, Primitives) ────────────────────
            foreach (var mesh in meshes)
                mesh.Render(cl, Matrix4x4.Identity, view, proj,
                    _surface.SceneDataBuffer, _surface.PointLightBuffer, _surface.EnvironmentBuffer);

            // ── Scene-object hierarchy (Characters) ───────────────────────────
            if (sceneRoot != null)
                RenderSceneObjectRecursive(cl, sceneRoot, view, proj);
        });

        return CurrentBitmap;
    }

    /// <summary>
    /// Recursively renders all visible nodes in a <see cref="SceneObject"/> hierarchy,
    /// applying each node's own world matrix so that bones are placed correctly.
    /// </summary>
    private void RenderSceneObjectRecursive(CommandList cl, SceneObject obj, Matrix4x4 view, Matrix4x4 proj)
    {
        if (_surface == null) return;
        if (!obj.GetEffectiveVisibility()) return;

        Matrix4x4 worldMatrix = ToNumerics(obj.GetWorldMatrix());

        foreach (var mesh in obj.Visuals)
            mesh.Render(cl, worldMatrix, view, proj,
                _surface.SceneDataBuffer, _surface.PointLightBuffer, _surface.EnvironmentBuffer);

        foreach (var child in obj.Children)
            RenderSceneObjectRecursive(cl, child, view, proj);
    }

    // ── GlmSharp → System.Numerics conversion (matches ViewportView) ───────────

    private static Matrix4x4 ToNumerics(mat4 m) => new(
        m.m00, m.m01, m.m02, m.m03,
        m.m10, m.m11, m.m12, m.m13,
        m.m20, m.m21, m.m22, m.m23,
        m.m30, m.m31, m.m32, m.m33);

    private static Vector3 ToNumerics(vec3 v) => new(v.x, v.y, v.z);

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
        _surface?.Dispose();
        _surface = null;
    }
}
