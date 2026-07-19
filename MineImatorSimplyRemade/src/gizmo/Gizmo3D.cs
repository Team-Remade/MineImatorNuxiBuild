using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.gizmo;

// ── Minimal Transform3D stand-in ─────────────────────────────────────────────
/// <summary>
/// Lightweight analogue of Godot's Transform3D.
/// Basis is stored as a mat4 (only the upper-left 3×3 is used for rotation/scale).
/// Origin is the translation.
/// </summary>
public struct Transform3D
{
    public mat4 Basis;   // upper-left 3×3 used; column convention matches GlmSharp
    public vec3 Origin;

    public static readonly Transform3D Identity = new(mat4.Identity, vec3.Zero);

    public Transform3D(mat4 basis, vec3 origin)
    {
        Basis  = basis;
        Origin = origin;
    }

    // GlmSharp field naming is mCR (column C, row R).
    // Column i = (m{i}0, m{i}1, m{i}2).
    public vec3 BasisX => new(Basis.m00, Basis.m01, Basis.m02);  // col 0
    public vec3 BasisY => new(Basis.m10, Basis.m11, Basis.m12);  // col 1
    public vec3 BasisZ => new(Basis.m20, Basis.m21, Basis.m22);  // col 2

    public vec3 BasisColumn(int i) => i switch
    {
        0 => BasisX,
        1 => BasisY,
        2 => BasisZ,
        _ => throw new ArgumentOutOfRangeException()
    };

    public Transform3D AffineInverse()
    {
        mat4 inv = Basis.Inverse;
        vec4 invOriginH = inv * new vec4(-Origin, 1f);
        vec3 invOrigin = new(invOriginH.x, invOriginH.y, invOriginH.z);
        return new Transform3D(inv, invOrigin);
    }

    public Transform3D Orthonormalized()
    {
        vec3 x = BasisX.Normalized;
        vec3 y = BasisY;
        y = (y - x * vec3.Dot(x, y)).Normalized;
        vec3 z = vec3.Cross(x, y);
        mat4 m = mat4.Identity;
        // mCR: col0=(m00,m01,m02), col1=(m10,m11,m12), col2=(m20,m21,m22)
        m.m00 = x.x; m.m01 = x.y; m.m02 = x.z;
        m.m10 = y.x; m.m11 = y.y; m.m12 = y.z;
        m.m20 = z.x; m.m21 = z.y; m.m22 = z.z;
        return new Transform3D(m, Origin);
    }

    public Transform3D ScaledBasis(vec3 scale)
    {
        mat4 m = Basis;
        // Scale each column: col0 *= scale.x, col1 *= scale.y, col2 *= scale.z
        m.m00 *= scale.x; m.m01 *= scale.x; m.m02 *= scale.x;
        m.m10 *= scale.y; m.m11 *= scale.y; m.m12 *= scale.y;
        m.m20 *= scale.z; m.m21 *= scale.z; m.m22 *= scale.z;
        return new Transform3D(m, Origin);
    }

    public Transform3D TranslatedLocal(vec3 localOffset)
    {
        return new Transform3D(Basis, Origin + GizmoMath.BasisTransform(Basis, localOffset));
    }

    public Transform3D Translated(vec3 worldOffset)
    {
        return new Transform3D(Basis, Origin + worldOffset);
    }

    /// <summary>Builds a full 4×4 world matrix from this transform (GlmSharp column-major).</summary>
    public mat4 ToMat4()
    {
        mat4 m = Basis;
        m.m30 = Origin.x;
        m.m31 = Origin.y;
        m.m32 = Origin.z;
        m.m33 = 1f;
        return m;
    }
}

// ── Math helpers ──────────────────────────────────────────────────────────────

internal static class GizmoMath
{
    /// <summary>Ray-plane intersection.  Returns null when ray is parallel to the plane.</summary>
    public static vec3? PlaneIntersectsRay(vec3 planeNormal, vec3 planePoint,
                                            vec3 rayOrigin, vec3 rayDir)
    {
        float denom = vec3.Dot(planeNormal, rayDir);
        if (MathF.Abs(denom) < 1e-6f) return null;
        float t = vec3.Dot(planeNormal, planePoint - rayOrigin) / denom;
        if (t < 0) return null;
        return rayOrigin + rayDir * t;
    }

    /// <summary>
    /// Returns the first intersection point (and normal) of a segment against a sphere, or
    /// an empty array if there is no intersection.  Mirrors Geometry3D.SegmentIntersectsSphere.
    /// </summary>
    public static vec3[] SegmentIntersectsSphere(vec3 from, vec3 to,
                                                  vec3 center, float radius)
    {
        vec3  dir = to - from;
        float len = dir.Length;
        if (len < 1e-7f) return Array.Empty<vec3>();
        vec3 d = dir / len;

        vec3  oc    = from - center;
        float b     = vec3.Dot(oc, d);
        float c     = oc.LengthSqr - radius * radius;
        float discr = b * b - c;

        if (discr < 0) return Array.Empty<vec3>();

        float t = -b - MathF.Sqrt(discr);
        if (t < 0 || t > len) return Array.Empty<vec3>();

        vec3 hit    = from + d * t;
        vec3 normal = (hit - center).Normalized;
        return [hit, normal];
    }

    /// <summary>Index of the smallest absolute component (0=X, 1=Y, 2=Z).</summary>
    public static int MinAxisIndex(vec3 v)
    {
        float ax = MathF.Abs(v.x), ay = MathF.Abs(v.y), az = MathF.Abs(v.z);
        if (ax <= ay && ax <= az) return 0;
        if (ay <= az)             return 1;
        return 2;
    }

    /// <summary>Signed angle from <paramref name="from"/> to <paramref name="to"/> around <paramref name="axis"/>.</summary>
    public static float SignedAngleTo(vec3 from, vec3 to, vec3 axis)
    {
        vec3  cross = vec3.Cross(from, to);
        float dot   = vec3.Dot(from, to);
        float angle = MathF.Atan2(cross.Length, dot);
        if (vec3.Dot(axis, cross) < 0) angle = -angle;
        return angle;
    }

    /// <summary>Snap a value to the nearest multiple of <paramref name="step"/>.</summary>
    public static float Snapped(float value, float step)
    {
        if (step <= 0) return value;
        return MathF.Floor(value / step + 0.5f) * step;
    }

    /// <summary>Component-wise snap.</summary>
    public static vec3 Snapped(vec3 value, float step)
        => new(Snapped(value.x, step), Snapped(value.y, step), Snapped(value.z, step));

    /// <summary>
    /// Builds a rotation matrix from an axis-angle (Rodrigues formula).
    /// Equivalent to <c>new Basis(axis, angle)</c> in Godot.
    /// </summary>
    public static mat4 AxisAngle(vec3 axis, float angle)
    {
        return mat4.Rotate(angle, axis);
    }

    /// <summary>Rotate vector <paramref name="v"/> by the 3×3 part of matrix <paramref name="m"/>.</summary>
    public static vec3 BasisTransform(mat4 m, vec3 v)
    {
        // GlmSharp mCR (col C, row R): mat4 * vec4 is the standard column-vector multiply.
        vec4 r = m * new vec4(v, 0f);
        return new vec3(r.x, r.y, r.z);
    }

    /// <summary>
    /// ScaledOrthogonal – port of basis.cpp#L262.
    /// Rescales the basis while preserving orientation.
    /// </summary>
    public static mat4 ScaledOrthogonal(mat4 basis, vec3 scale)
    {
        // mCR: col0=(m00,m01,m02), col1=(m10,m11,m12), col2=(m20,m21,m22)
        vec3 col0 = new(basis.m00, basis.m01, basis.m02);
        vec3 col1 = new(basis.m10, basis.m11, basis.m12);
        vec3 col2 = new(basis.m20, basis.m21, basis.m22);

        vec3 s = new(-1, -1, -1);
        s.x += scale.x; s.y += scale.y; s.z += scale.z;
        bool sign = (s.x + s.y + s.z) < 0;

        vec3 bx = col0.Normalized;
        vec3 by = col1;
        by = (by - bx * vec3.Dot(bx, by)).Normalized;
        vec3 bz = vec3.Cross(bx, by).Normalized;

        float sx = s.x * bx.x + s.y * by.x + s.z * bz.x;
        float sy = s.x * bx.y + s.y * by.y + s.z * bz.y;
        float sz = s.x * bx.z + s.y * by.z + s.z * bz.z;
        s = new(sx, sy, sz);

        vec3   dots = vec3.Zero;
        vec3[] bCols = [bx, by, bz];
        vec3[] sCols = [col0, col1, col2];
        for (int i = 0; i < 3; i++)
        {
            float sv = i == 0 ? s.x : i == 1 ? s.y : s.z;
            for (int j = 0; j < 3; j++)
            {
                float dot = vec3.Dot(sCols[i].Normalized, bCols[j]);
                if (j == 0) dots.x += sv * MathF.Abs(dot);
                else if (j == 1) dots.y += sv * MathF.Abs(dot);
                else dots.z += sv * MathF.Abs(dot);
            }
        }
        if (sign != ((dots.x + dots.y + dots.z) < 0))
            dots = -dots;

        vec3 newScale = vec3.Ones + dots;
        mat4 result = basis;
        // Scale each column: col0*=newScale.x, col1*=newScale.y, col2*=newScale.z
        result.m00 *= newScale.x; result.m01 *= newScale.x; result.m02 *= newScale.x;
        result.m10 *= newScale.y; result.m11 *= newScale.y; result.m12 *= newScale.y;
        result.m20 *= newScale.z; result.m21 *= newScale.z; result.m22 *= newScale.z;
        return result;
    }

    /// <summary>
    /// Decompose Euler YXZ angles from a pure-rotation mat4 (column-major GlmSharp).
    /// The convention matches SceneObject: Rotation = (pitch/X, yaw/Y, roll/Z) in radians,
    /// applied as  Rz * Ry * Rx.
    /// </summary>
    public static vec3 MatrixToEulerYXZ(mat4 m)
    {
        // Extract Euler (x, y, z) for R = Rz*Ry*Rx (GlmSharp column-major, mCR where C=col, R=row).
        // GetLocalMatrix builds rotation as rz * ry * rx  →  R = Rz*Ry*Rx.
        //
        // Expanding R = Rz*Ry*Rx gives the following key elements:
        //   m.m02 (col 0, row 2) = -sin(y)
        //   m.m12 (col 1, row 2) =  cos(y)*sin(x)
        //   m.m22 (col 2, row 2) =  cos(y)*cos(x)
        //   m.m01 (col 0, row 1) =  cos(y)*sin(z)
        //   m.m00 (col 0, row 0) =  cos(y)*cos(z)
        float yaw   = MathF.Asin(-Math.Clamp(m.m02, -1f, 1f));   // Y = asin(-m02)
        float pitch, roll;
        if (MathF.Abs(m.m02) < 0.9999f)
        {
            pitch = MathF.Atan2(m.m12, m.m22);   // X = atan2(cos(y)*sin(x), cos(y)*cos(x))
            roll  = MathF.Atan2(m.m01, m.m00);   // Z = atan2(cos(y)*sin(z), cos(y)*cos(z))
        }
        else
        {
            // Gimbal lock (y ≈ ±90°): x and z are coupled; convention: z = 0.
            // At y=±90°: m.m11 = cos(z-x), m.m21 = sin(z-x)  → x = atan2(-m21, m11) with z=0.
            pitch = MathF.Atan2(-m.m21, m.m11);
            roll  = 0;
        }
        return new vec3(pitch, yaw, roll);
    }

    /// <summary>Extract scale from the upper-left 3×3 (column lengths). mCR convention.</summary>
    public static vec3 ExtractScale(mat4 m)
    {
        return new vec3(
            new vec3(m.m00, m.m01, m.m02).Length,   // col 0
            new vec3(m.m10, m.m11, m.m12).Length,   // col 1
            new vec3(m.m20, m.m21, m.m22).Length);  // col 2
    }
}

// ─────────────────────────────────────────────────────────────────────────────
/// <summary>
/// OpenGL port of GizmoPlugin/Gizmo3D.  All MonoGame/XNA rendering APIs are
/// replaced with raw Silk.NET.OpenGL VAO/VBO primitives and a dedicated
/// gizmo.vert / gizmo.frag shader.
///
/// Mouse input is driven externally from the Viewport via UpdateHover /
/// TryBeginEdit / ContinueEdit / EndEdit.
/// </summary>
public class Gizmo3D : IDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    const float DEFAULT_FLOAT_STEP    = 0.001f;
    const float MAX_Z                 = 1000000.0f;

    const float GIZMO_ARROW_WIDTH     = 0.12f;
    const float GIZMO_ARROW_SIZE      = 0.35f;
    const float GIZMO_RING_HALF_WIDTH = 0.1f;
    const float GIZMO_PLANE_SIZE      = 0.2f;
    const float GIZMO_PLANE_DST       = 0.3f;
    const float GIZMO_CIRCLE_SIZE     = 1.1f;
    const float GIZMO_SCALE_OFFSET    = GIZMO_CIRCLE_SIZE - 0.3f;
    const float GIZMO_ARROW_OFFSET    = GIZMO_CIRCLE_SIZE + 0.15f;
    const int   CIRCLE_SEGMENTS       = 128;
    const int   ARC_SEGMENTS          = 64;

    // ── Enums ─────────────────────────────────────────────────────────────────

    [Flags]
    public enum ToolMode { Move = 1, Rotate = 2, Scale = 4, All = 7 }
    public enum TransformMode  { None, Rotate, Translate, Scale }
    public enum TransformPlane { View, X, Y, Z, YZ, XZ, XY }

    // ── Public properties ─────────────────────────────────────────────────────

    public ToolMode Mode             { get; set; } = ToolMode.Move | ToolMode.Scale | ToolMode.Rotate;
    public bool     Snapping         { get; set; }
    public bool     ShiftSnap        { get; set; }
    public string   Message          { get; private set; } = "";
    public bool     Editing          { get; private set; }
    public bool     Hovering         { get; private set; }
    public bool     Visible          { get; private set; }
    public bool     UseLocalSpace    { get; set; } = false;
    public float    Size             { get; set; } = 80.0f;
    public bool     ShowAxes         { get; set; } = true;
    public bool     ShowSelectionBox { get; set; } = false;
    public bool     ShowRotationLine { get; set; } = true;
    public bool     ShowRotationArc  { get; set; } = true;
    public float    Opacity          { get; set; } = 0.9f;
    public float    RotateSnap       { get; set; } = 15.0f;
    public float    TranslateSnap    { get; set; } = 1.0f;
    public float    ScaleSnap        { get; set; } = 0.25f;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action<TransformMode>?                  TransformBegin;
    public event Action<TransformMode, vec3>?            TransformChanged;
    public event Action<TransformMode, TransformPlane>?  TransformEnd;

    // ── Axis colors (RGBA 0-1) ────────────────────────────────────────────────

    private readonly vec4[] _colors =
    [
        new(0.96f, 0.20f, 0.32f, 1f),   // X – red
        new(0.53f, 0.84f, 0.01f, 1f),   // Y – green
        new(0.16f, 0.55f, 0.96f, 1f),   // Z – blue
    ];
    private readonly vec4 _selBoxColor = new(1.0f, 0.5f, 0f, 1f);

    // ── Internal geometry buffers ─────────────────────────────────────────────

    private struct GizmoPart
    {
        public uint Vao;
        public uint Vbo;
        public int  VertexCount;
        public bool IsLines;       // true = GL_LINES, false = GL_TRIANGLES
        public vec4 NormalColor;
        public vec4 HighlightColor;
    }

    private GizmoPart[] _moveGizmo       = new GizmoPart[3];
    private GizmoPart[] _movePlaneGizmo  = new GizmoPart[3];
    private GizmoPart[] _rotateGizmo     = new GizmoPart[3];
    private GizmoPart[] _scaleGizmo      = new GizmoPart[3];
    private GizmoPart[] _scalePlaneGizmo = new GizmoPart[3];
    private GizmoPart[] _axisGizmo       = new GizmoPart[3];

    private int _highlightAxis = -1;

    // ── Transform state ───────────────────────────────────────────────────────

    private Transform3D _gizmoTransform = Transform3D.Identity;
    private float       _gizmoScale     = 1.0f;

    private struct SelectedItem
    {
        public SceneObject Object;
        public Transform3D TargetOriginal;
        public Transform3D TargetGlobal;
    }

    private readonly List<SelectedItem> _selections = new();
    private vec3 _visualCenter;

    private struct EditData
    {
        public bool          ShowRotationLine;
        public Transform3D   Original;
        public TransformMode Mode;
        public TransformPlane Plane;
        public vec3          ClickRay, ClickRayPos;
        public vec3          Center;
        public Vector2       MousePos;

        // Rotation arc
        public vec3   RotationAxis;
        public float  AccumulatedRotationAngle;
        public float  DisplayRotationAngle;
        public vec3?  InitialClickVector;
        public vec3?  PreviousRotationVector;
        public bool   GizmoInitiated;
    }

    private EditData _edit;

    // ── Camera / viewport ─────────────────────────────────────────────────────

    private Camera?  _camera;
    private Vector2  _imageMin;
    private Vector2  _imageSize;

    // ── OpenGL ────────────────────────────────────────────────────────────────

    private readonly GL _gl;
    private MineImatorSimplyRemade.core.mdl.Shader? _shader;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public Gizmo3D(GL gl)
    {
        _gl = gl;
    }

    /// <summary>
    /// Compile shaders and build all GPU geometry buffers.
    /// Must be called once after the OpenGL context is ready (e.g. from Viewport.InitFramebuffer).
    /// </summary>
    public void Init()
    {
        _shader = new MineImatorSimplyRemade.core.mdl.Shader(_gl);
        _shader.CompileShader("gizmo.vert", "gizmo.frag");
        InitIndicators();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Selection API
    // ─────────────────────────────────────────────────────────────────────────

    public void Select(SceneObject obj)
    {
        if (obj == null) return;
        foreach (var s in _selections)
            if (s.Object == obj) return;

        _selections.Add(new SelectedItem
        {
            Object         = obj,
            TargetOriginal = GetLocalTransform(obj),
            TargetGlobal   = GetWorldTransform(obj),
        });
        UpdateTransformGizmo();
    }

    public void Deselect(SceneObject obj)
    {
        for (int i = _selections.Count - 1; i >= 0; i--)
            if (_selections[i].Object == obj) { _selections.RemoveAt(i); break; }
        UpdateTransformGizmo();
    }

    public void ClearSelection()
    {
        _selections.Clear();
        UpdateTransformGizmo();
    }

    public bool IsSelected(SceneObject obj)
    {
        foreach (var s in _selections)
            if (s.Object == obj) return true;
        return false;
    }

    public int GetSelectedCount() => _selections.Count;

    // ─────────────────────────────────────────────────────────────────────────
    // Object capability checks
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if all selected objects can be rotated.
    /// Rotation is disabled for point lights (omni-directional, no orientation)
    /// but enabled for spot lights, which use rotation to aim the cone.
    /// </summary>
    private bool CanRotateSelection()
    {
        if (_selections.Count == 0) return false;
        foreach (var selection in _selections)
        {
            if (selection.Object is MineImatorSimplyRemadeNuxi.core.objs.sceneObjects.LightSceneObject light &&
                light.Type != MineImatorSimplyRemadeNuxi.core.objs.sceneObjects.LightType.Spot)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true if all selected objects can be scaled.
    /// Scaling is disabled for CameraSceneObject and LightSceneObject instances.
    /// </summary>
    private bool CanScaleSelection()
    {
        if (_selections.Count == 0) return false;
        foreach (var selection in _selections)
        {
            var obj = selection.Object;
            if (obj is MineImatorSimplyRemadeNuxi.core.objs.sceneObjects.CameraSceneObject ||
                obj is MineImatorSimplyRemadeNuxi.core.objs.sceneObjects.LightSceneObject)
                return false;
        }
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Input API
    // ─────────────────────────────────────────────────────────────────────────

    public void UpdateHover(Vector2 screenPos, Camera camera, Vector2 imageMin, Vector2 imageSize)
    {
        _camera    = camera;
        _imageMin  = imageMin;
        _imageSize = imageSize;
        Hovering   = TransformGizmoSelect(screenPos, highlightOnly: true);
    }

    public bool TryBeginEdit(Vector2 screenPos, Camera camera, Vector2 imageMin, Vector2 imageSize)
    {
        _camera    = camera;
        _imageMin  = imageMin;
        _imageSize = imageSize;

        _edit.InitialClickVector       = null;
        _edit.PreviousRotationVector   = null;
        _edit.AccumulatedRotationAngle = 0;
        _edit.DisplayRotationAngle     = 0;
        _edit.GizmoInitiated           = false;
        _edit.MousePos                 = screenPos;

        bool hit = TransformGizmoSelect(screenPos, highlightOnly: false);
        if (hit)
        {
            Editing = true;
            TransformBegin?.Invoke(_edit.Mode);
        }
        return hit;
    }

    public void ContinueEdit(Vector2 screenPos, Camera camera, Vector2 imageMin, Vector2 imageSize)
    {
        if (!Editing) return;
        // Refresh the viewport parameters because Render() calls from other
        // viewports that share this gizmo may have overwritten them between
        // frames. Without this, an in-progress drag would briefly use the
        // wrong camera for ray/scale calculations.
        _camera    = camera;
        _imageMin  = imageMin;
        _imageSize = imageSize;
        UpdateTransformGizmoView();
        _edit.MousePos = screenPos;
        vec3 value = UpdateTransform(false);
        TransformChanged?.Invoke(_edit.Mode, value);
    }

    public void EndEdit()
    {
        if (!Editing) return;
        TransformEnd?.Invoke(_edit.Mode, _edit.Plane);
        Editing    = false;
        Message    = "";
        _edit.Mode = TransformMode.None;
        UpdateTransformGizmo();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Snap helpers
    // ─────────────────────────────────────────────────────────────────────────

    public float GetTranslateSnap() => ShiftSnap ? TranslateSnap / 10f : TranslateSnap;
    public float GetRotationSnap()  => ShiftSnap ? RotateSnap    / 3f  : RotateSnap;
    public float GetScaleSnap()     => ShiftSnap ? ScaleSnap     / 2f  : ScaleSnap;

    // ─────────────────────────────────────────────────────────────────────────
    // Render – 3D pass
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draw all gizmo 3D geometry.  Call after all scene objects are rendered.
    /// <paramref name="view"/> and <paramref name="proj"/> come from the viewport camera.
    /// </summary>
    public void Render(Camera camera, mat4 view, mat4 proj, Vector2 imageMin, Vector2 imageSize)
    {
        _camera    = camera;
        _imageMin  = imageMin;
        _imageSize = imageSize;
        UpdateTransformGizmo();

        if (!Visible || _selections.Count == 0 || _shader == null) return;

        UpdateTransformGizmoView();

        // Disable depth test so gizmo always renders on top
        _gl.Disable(GLEnum.DepthTest);
        _gl.Disable(GLEnum.CullFace);

        _gl.UseProgram(_shader.ShaderProgram);

        bool showGizmo    = !IsRotationArcVisible();
        bool hasMove      = (Mode & ToolMode.Move)   != 0;
        bool hasRot       = (Mode & ToolMode.Rotate) != 0 && CanRotateSelection();
        bool hasScale     = (Mode & ToolMode.Scale)  != 0 && CanScaleSelection();

        if (showGizmo)
        {
            for (int i = 0; i < 3; i++)
            {
                mat4 axisWorld = BuildAxisTransform(i);
                mat4 mvp       = proj * view * axisWorld;

                if (hasMove)
                {
                    DrawPart(ref _moveGizmo[i],      mvp, i);
                    DrawPart(ref _movePlaneGizmo[i], mvp, i + 6);
                }
                if (hasScale)
                {
                    DrawPart(ref _scaleGizmo[i], mvp, i + 9);
                    if (!hasMove) DrawPart(ref _scalePlaneGizmo[i], mvp, i + 12);
                }
            }

            if (hasRot)
            {
                for (int i = 0; i < 3; i++)
                {
                    mat4 axisWorld = BuildAxisTransform(i);
                    mat4 mvp       = proj * view * axisWorld;
                    DrawPart(ref _rotateGizmo[i], mvp, i + 3);
                }
            }
        }

        // Axis constraint lines (shown while dragging)
        mat4 gizmoMvp = proj * view * _gizmoTransform.ToMat4();
        for (int i = 0; i < 3; i++)
        {
            bool showAxis = ShowAxes && Editing &&
                (_edit.Plane == (TransformPlane)(i + 1) ||
                 (_edit.Plane == TransformPlane.XY && i < 2) ||
                 (_edit.Plane == TransformPlane.XZ && i != 1) ||
                 (_edit.Plane == TransformPlane.YZ && i > 0));
            if (showAxis)
                DrawPart(ref _axisGizmo[i], gizmoMvp, i);
        }

        // Selection boxes
        if (ShowSelectionBox)
            DrawSelectionBoxes(view, proj);

        _gl.Enable(GLEnum.DepthTest);
        _gl.Enable(GLEnum.CullFace);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Render – 2D overlay (ImGui draw list)
    // ─────────────────────────────────────────────────────────────────────────

    public void RenderOverlay(Camera camera, Vector2 imageMin, Vector2 imageSize)
    {
        _camera    = camera;
        _imageMin  = imageMin;
        _imageSize = imageSize;

        if (_edit.Mode != TransformMode.Rotate) return;

        var dl = ImGui.GetWindowDrawList();

        Vector2 center2d = PointToScreen(_edit.Center);

        vec4 handleColor = _edit.Plane switch
        {
            TransformPlane.X => _colors[0],
            TransformPlane.Y => _colors[1],
            TransformPlane.Z => _colors[2],
            _                => new vec4(1f, 1f, 1f, 1f),
        };

        if (IsRotationArcVisible() && _edit.InitialClickVector.HasValue)
        {
            vec3 up    = _edit.RotationAxis;
            vec3 right = _edit.InitialClickVector.Value;
            right -= up * vec3.Dot(up, right);
            if (right.LengthSqr > 1e-8f)
                right = right.Normalized;
            vec3 forward = vec3.Cross(up, right);

            // Draw full circle
            var circlePts = new List<Vector2>(ARC_SEGMENTS + 1);
            for (int i = 0; i <= ARC_SEGMENTS; i++)
            {
                float angle = (float)i / ARC_SEGMENTS * MathF.PI * 2f;
                vec3 pt3 = _edit.Center + _gizmoScale * GIZMO_CIRCLE_SIZE *
                           (right * MathF.Cos(angle) + forward * MathF.Sin(angle));
                circlePts.Add(PointToScreen(pt3));
            }
            uint circleCol = ToImGuiColor(HsvAdjust(handleColor, 0.6f, 1f, 0.8f));
            for (int i = 0; i < circlePts.Count - 1; i++)
                dl.AddLine(circlePts[i], circlePts[i + 1], circleCol, 2f);

            // Draw filled arc
            float dispAngle = _edit.DisplayRotationAngle;
            float absAngle  = MathF.Abs(dispAngle);
            if (absAngle > MathF.PI * 2f)
            {
                float rem = absAngle % (MathF.PI * 2f);
                if (rem < 0.01f) rem = MathF.PI * 2f;
                dispAngle = MathF.Sign(dispAngle) * rem;
                absAngle  = MathF.Abs(dispAngle);
            }

            int numSeg = Math.Max(8, (int)(absAngle / (MathF.PI * 2f / ARC_SEGMENTS) * ARC_SEGMENTS));
            numSeg = Math.Min(numSeg, ARC_SEGMENTS);

            uint fillCol = ToImGuiColor(new vec4(1f, 1f, 1f, 0.2f));
            float startA = dispAngle > 0 ? 0f : dispAngle;
            float endA   = dispAngle > 0 ? dispAngle : 0f;

            for (int i = 0; i < numSeg; i++)
            {
                float t1 = (float)i / numSeg;
                float t2 = (float)(i + 1) / numSeg;
                float a1 = glm.Lerp(startA, endA, t1);
                float a2 = glm.Lerp(startA, endA, t2);

                vec3 p1_3d = _edit.Center + _gizmoScale * GIZMO_CIRCLE_SIZE *
                             (right * MathF.Cos(a1) + forward * MathF.Sin(a1));
                vec3 p2_3d = _edit.Center + _gizmoScale * GIZMO_CIRCLE_SIZE *
                             (right * MathF.Cos(a2) + forward * MathF.Sin(a2));

                dl.AddTriangleFilled(
                    center2d,
                    PointToScreen(p1_3d),
                    PointToScreen(p2_3d),
                    fillCol);
            }

            // Edge lines from center
            uint edgeCol = ToImGuiColor(HsvAdjust(handleColor, 0.8f, 1f, 0.7f));
            vec3 startPt = _edit.Center + _gizmoScale * GIZMO_CIRCLE_SIZE * right;
            vec3 endPt   = _edit.Center + _gizmoScale * GIZMO_CIRCLE_SIZE *
                           (right * MathF.Cos(dispAngle) + forward * MathF.Sin(dispAngle));
            dl.AddLine(center2d, PointToScreen(startPt), edgeCol, 2f);
            dl.AddLine(center2d, PointToScreen(endPt),   edgeCol, 2f);
        }

        // Rotation line to cursor
        if (_edit.ShowRotationLine && ShowRotationLine)
        {
            vec4  lineColor = HsvAdjust(handleColor, 0.25f, 1f, 1f);
            dl.AddLine(_edit.MousePos, center2d, ToImGuiColor(lineColor), 2f);
        }
    }

    public bool IsRotationArcVisible()
        => _edit.Mode == TransformMode.Rotate && ShowRotationArc
           && _edit.AccumulatedRotationAngle != 0f && _edit.GizmoInitiated;

    // ─────────────────────────────────────────────────────────────────────────
    // Internal: geometry init
    // ─────────────────────────────────────────────────────────────────────────

    private void InitIndicators()
    {
        vec3 ivec  = new(0,  0, -1);
        vec3 nivec = new(-1, -1,  0);
        vec3 ivec2 = new(-1,  0,  0);
        vec3 ivec3 = new(0,  -1,  0);

        var canonMoveVerts = BuildArrow(
        [
            nivec * 0.0f  + ivec * GIZMO_ARROW_OFFSET,
            nivec * 0.01f + ivec * GIZMO_ARROW_OFFSET,
            nivec * 0.01f + ivec * GIZMO_ARROW_OFFSET,
            nivec * GIZMO_ARROW_WIDTH + ivec * GIZMO_ARROW_OFFSET,
            nivec * 0.0f  + ivec * (GIZMO_ARROW_OFFSET + GIZMO_ARROW_SIZE)
        ], ivec, 5, 16);

        var canonScaleVerts = BuildArrow(
        [
            nivec * 0.0f  + ivec * 0.0f,
            nivec * 0.01f + ivec * 0.0f,
            nivec * 0.01f + ivec * 1.0f * GIZMO_SCALE_OFFSET,
            nivec * 0.07f + ivec * 1.0f * GIZMO_SCALE_OFFSET,
            nivec * 0.07f + ivec * 1.2f * GIZMO_SCALE_OFFSET,
            nivec * 0.0f  + ivec * 1.2f * GIZMO_SCALE_OFFSET
        ], ivec, 6, 4);

        var canonPlaneVerts = BuildPlaneQuad(ivec, ivec2, ivec3);
        var canonRingVerts  = BuildRotationRing(ivec, ivec2);

        for (int i = 0; i < 3; i++)
        {
            vec4 col   = WithAlpha(_colors[i], Opacity);
            vec4 colHl = HsvAdjust(_colors[i], 0.25f, 1f, 1f);

            _moveGizmo[i]       = MakePart(canonMoveVerts,  col, colHl, isLines: false);
            _movePlaneGizmo[i]  = MakePart(canonPlaneVerts, col, colHl, isLines: false);
            _rotateGizmo[i]     = MakePart(canonRingVerts,  col, colHl, isLines: false);
            _scaleGizmo[i]      = MakePart(canonScaleVerts, col, colHl, isLines: false);
            _scalePlaneGizmo[i] = MakePart(canonPlaneVerts, col, colHl, isLines: false);

            vec3 axisDir = i == 0 ? vec3.UnitX : i == 1 ? vec3.UnitY : vec3.UnitZ;
            _axisGizmo[i] = MakePart(BuildAxisLine(axisDir), colHl, colHl, isLines: true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Geometry builders  (return vec3 position lists)
    // ─────────────────────────────────────────────────────────────────────────

    private static List<vec3> BuildArrow(vec3[] arrowProfile, vec3 ivec,
                                          int arrowPoints, int arrowSides)
    {
        var verts = new List<vec3>();
        float step = MathF.PI * 2f / arrowSides;

        for (int k = 0; k < arrowSides; k++)
        {
            mat4 maa = GizmoMath.AxisAngle(ivec, k * step);
            mat4 mbb = GizmoMath.AxisAngle(ivec, (k + 1) * step);

            for (int j = 0; j < arrowPoints - 1; j++)
            {
                vec3 a0 = GizmoMath.BasisTransform(maa, arrowProfile[j]);
                vec3 a1 = GizmoMath.BasisTransform(mbb, arrowProfile[j]);
                vec3 a2 = GizmoMath.BasisTransform(mbb, arrowProfile[j + 1]);
                vec3 a3 = GizmoMath.BasisTransform(maa, arrowProfile[j + 1]);

                verts.Add(a0); verts.Add(a1); verts.Add(a2);
                verts.Add(a0); verts.Add(a2); verts.Add(a3);
            }
        }
        return verts;
    }

    private static List<vec3> BuildPlaneQuad(vec3 ivec, vec3 ivec2, vec3 ivec3)
    {
        vec3 vec = ivec2 - ivec3;
        vec3[] plane =
        [
            vec * GIZMO_PLANE_DST,
            vec * GIZMO_PLANE_DST + ivec2 * GIZMO_PLANE_SIZE,
            vec * (GIZMO_PLANE_DST + GIZMO_PLANE_SIZE),
            vec * GIZMO_PLANE_DST - ivec3 * GIZMO_PLANE_SIZE
        ];

        mat4 ma = GizmoMath.AxisAngle(ivec, MathF.PI / 2f);
        vec3[] pts =
        [
            GizmoMath.BasisTransform(ma, plane[0]),
            GizmoMath.BasisTransform(ma, plane[1]),
            GizmoMath.BasisTransform(ma, plane[2]),
            GizmoMath.BasisTransform(ma, plane[3])
        ];

        return [pts[0], pts[1], pts[2], pts[0], pts[2], pts[3]];
    }

    private static List<vec3> BuildRotationRing(vec3 ivec, vec3 ivec2)
    {
        const int THICKNESS  = 3;
        const float TUBE_RADIUS = 0.025f;

        var positions = new vec3[CIRCLE_SEGMENTS * THICKNESS];
        float step = MathF.PI * 2f / CIRCLE_SEGMENTS;

        for (int j = 0; j < CIRCLE_SEGMENTS; j++)
        {
            mat4 basis  = GizmoMath.AxisAngle(ivec, j * step);
            vec3 centre = GizmoMath.BasisTransform(basis, ivec2 * GIZMO_CIRCLE_SIZE);

            vec3 radial = centre.Normalized;
            vec3 axial  = ivec;

            for (int k = 0; k < THICKNESS; k++)
            {
                float a = MathF.PI * 2f * k / THICKNESS;
                vec3 offset = radial * MathF.Cos(a) * TUBE_RADIUS
                            + axial  * MathF.Sin(a) * TUBE_RADIUS;
                positions[j * THICKNESS + k] = centre + offset;
            }
        }

        var verts = new List<vec3>(CIRCLE_SEGMENTS * THICKNESS * 6);
        for (int j = 0; j < CIRCLE_SEGMENTS; j++)
        {
            int cur  = j * THICKNESS;
            int next = ((j + 1) % CIRCLE_SEGMENTS) * THICKNESS;

            for (int k = 0; k < THICKNESS; k++)
            {
                int ks = k;
                int kn = (k + 1) % THICKNESS;

                verts.Add(positions[cur  + kn]);
                verts.Add(positions[cur  + ks]);
                verts.Add(positions[next + ks]);

                verts.Add(positions[next + ks]);
                verts.Add(positions[next + kn]);
                verts.Add(positions[cur  + kn]);
            }
        }
        return verts;
    }

    private static List<vec3> BuildAxisLine(vec3 dir)
    {
        vec3 d = dir.LengthSqr > 1e-8f ? dir.Normalized : vec3.UnitX;
        return [d * -1048576f, d * 1048576f];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GPU buffer helpers
    // ─────────────────────────────────────────────────────────────────────────

    private unsafe GizmoPart MakePart(List<vec3> verts, vec4 normalColor, vec4 highlightColor, bool isLines)
    {
        if (verts.Count == 0) return new GizmoPart();

        _gl.GenVertexArrays(1, out uint vao);
        _gl.GenBuffers(1, out uint vbo);

        _gl.BindVertexArray(vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, vbo);

        float[] data = new float[verts.Count * 3];
        for (int i = 0; i < verts.Count; i++)
        {
            data[i * 3 + 0] = verts[i].x;
            data[i * 3 + 1] = verts[i].y;
            data[i * 3 + 2] = verts[i].z;
        }

        fixed (float* p = data)
            _gl.BufferData(GLEnum.ArrayBuffer, (uint)(data.Length * sizeof(float)), p, GLEnum.StaticDraw);

        _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, 3 * (uint)sizeof(float), 0);
        _gl.EnableVertexAttribArray(0);

        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindVertexArray(0);

        return new GizmoPart
        {
            Vao            = vao,
            Vbo            = vbo,
            VertexCount    = verts.Count,
            IsLines        = isLines,
            NormalColor    = normalColor,
            HighlightColor = highlightColor,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Draw helpers
    // ─────────────────────────────────────────────────────────────────────────

    private unsafe void DrawPart(ref GizmoPart part, mat4 mvp, int highlightAxisCode)
    {
        if (part.Vao == 0 || part.VertexCount == 0 || _shader == null) return;
        bool hl = _highlightAxis == highlightAxisCode;
        DrawPartColor(ref part, mvp, hl ? part.HighlightColor : part.NormalColor);
    }

    private unsafe void DrawPartColor(ref GizmoPart part, mat4 mvp, vec4 color)
    {
        if (part.Vao == 0 || part.VertexCount == 0 || _shader == null) return;

        int mvpLoc   = _gl.GetUniformLocation(_shader.ShaderProgram, "uMVP");
        int colorLoc = _gl.GetUniformLocation(_shader.ShaderProgram, "uColor");

        float[] m =
        [
            mvp.m00, mvp.m01, mvp.m02, mvp.m03,
            mvp.m10, mvp.m11, mvp.m12, mvp.m13,
            mvp.m20, mvp.m21, mvp.m22, mvp.m23,
            mvp.m30, mvp.m31, mvp.m32, mvp.m33,
        ];
        fixed (float* p = m)
            _gl.UniformMatrix4(mvpLoc, 1, false, p);

        _gl.Uniform4(colorLoc, color.x, color.y, color.z, color.w);

        _gl.BindVertexArray(part.Vao);
        if (part.IsLines)
            _gl.DrawArrays(GLEnum.Lines, 0, (uint)part.VertexCount);
        else
            _gl.DrawArrays(GLEnum.Triangles, 0, (uint)part.VertexCount);
        _gl.BindVertexArray(0);
    }

    private void DrawSelectionBoxes(mat4 view, mat4 proj)
    {
        foreach (var item in _selections)
        {
            var (pos, size) = GetObjectAabb(item.Object);
            var lineVerts = new List<vec3>();

            for (int e = 0; e < 12; e++)
            {
                GizmoHelper.GetEdge(pos, size, e, out var a, out var b);
                lineVerts.Add(a);
                lineVerts.Add(b);
            }

            if (lineVerts.Count == 0) continue;

            mat4 worldMat = item.TargetGlobal.ToMat4();
            mat4 mvp      = proj * view * worldMat;

            vec4 col     = _selBoxColor;
            vec4 xrayCol = WithAlpha(col, 0.15f * Opacity);

            // X-ray pass
            var xrayPart = MakePart(lineVerts, xrayCol, xrayCol, isLines: true);
            DrawPartColor(ref xrayPart, mvp, xrayCol);
            FreePart(ref xrayPart);

            // Solid pass (re-enable depth briefly)
            _gl.Enable(GLEnum.DepthTest);
            var solidPart = MakePart(lineVerts, col, col, isLines: true);
            DrawPartColor(ref solidPart, mvp, col);
            FreePart(ref solidPart);
            _gl.Disable(GLEnum.DepthTest);
        }
    }

    private void FreePart(ref GizmoPart part)
    {
        if (part.Vao != 0) { _gl.DeleteVertexArrays(1, part.Vao); part.Vao = 0; }
        if (part.Vbo != 0) { _gl.DeleteBuffers(1, part.Vbo); part.Vbo = 0; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Transform update
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateTransformGizmo()
    {
        int  count  = _selections.Count;
        vec3 center = vec3.Zero;
        mat4 basis  = mat4.Identity;

        if (count == 0) { Visible = false; return; }

        if (Editing)
        {
            if (UseLocalSpace && count == 1)
                basis = _selections[0].TargetGlobal.Basis;
            vec3 origin = _edit.Mode == TransformMode.Translate ? _visualCenter : _edit.Center;
            Visible         = true;
            _gizmoTransform = new Transform3D(basis, origin);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            Transform3D xf = GetWorldTransform(_selections[i].Object);
            center += xf.Origin;
            if (i == 0 && UseLocalSpace) basis = xf.Basis;
        }
        center /= count;

        Visible         = true;
        _gizmoTransform = new Transform3D(count == 1 ? basis : mat4.Identity, center);
    }

    private void UpdateTransformGizmoView()
    {
        if (!Visible || _camera == null) return;

        vec3 gizmoOrigin  = _gizmoTransform.Origin;
        vec3 cameraOrigin = _camera.Position;

        if ((gizmoOrigin - cameraOrigin).LengthSqr < 1e-8f) { Visible = false; return; }

        mat4 viewMat = _camera.GetViewMatrix();

        // Camera forward in world space (–Z column of view inverse = third column of view transposed)
        // view = lookAt; its column 2 (m02,m12,m22) is the Z axis in world (camera forward in right-hand = -camZ)
        vec3 camZ = -new vec3(viewMat.m02, viewMat.m12, viewMat.m22).Normalized;
        vec3 camY = -new vec3(viewMat.m01, viewMat.m11, viewMat.m21).Normalized;

        float gizmoD = MathF.Max(MathF.Abs(vec3.Dot(camZ, gizmoOrigin - cameraOrigin)), float.Epsilon);

        Vector2 p0 = UnprojectPos(cameraOrigin + camZ * gizmoD);
        Vector2 p1 = UnprojectPos(cameraOrigin + camZ * gizmoD + camY);
        float   dd = MathF.Max(MathF.Abs(p0.Y - p1.Y), float.Epsilon);

        _gizmoScale = Size / MathF.Abs(dd);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Hit testing
    // ─────────────────────────────────────────────────────────────────────────

    private bool TransformGizmoSelect(Vector2 screenpos, bool highlightOnly)
    {
        if (!Visible || _selections.Count == 0)
        {
            if (highlightOnly) _highlightAxis = -1;
            return false;
        }

        UpdateTransformGizmo();
        UpdateTransformGizmoView();

        vec3        rayPos = GetRayPos(screenpos);
        vec3        ray    = GetRay(screenpos);
        Transform3D gt     = _gizmoTransform;

        // ── Move ────────────────────────────────────────────────────────────
        if ((Mode & ToolMode.Move) != 0)
        {
            int   colAxis = -1;
            float colD    = 1e20f;

            for (int i = 0; i < 3; i++)
            {
                vec3  grabberPos    = gt.Origin + gt.BasisColumn(i).Normalized * _gizmoScale * (GIZMO_ARROW_OFFSET + GIZMO_ARROW_SIZE * 0.5f);
                float grabberRadius = _gizmoScale * GIZMO_ARROW_SIZE;

                var r = GizmoMath.SegmentIntersectsSphere(rayPos, rayPos + ray * MAX_Z, grabberPos, grabberRadius);
                if (r.Length != 0)
                {
                    float d = (r[0] - rayPos).Length;
                    if (d < colD) { colD = d; colAxis = i; }
                }
            }

            bool isPlane = false;
            if (colAxis == -1)
            {
                colD = 1e20f;
                for (int i = 0; i < 3; i++)
                {
                    vec3  iv2        = gt.BasisColumn((i + 1) % 3).Normalized;
                    vec3  iv3        = gt.BasisColumn((i + 2) % 3).Normalized;
                    vec3  grabberPos = gt.Origin + (iv2 + iv3) * _gizmoScale * (GIZMO_PLANE_SIZE + GIZMO_PLANE_DST * 0.6667f);
                    vec3  planeNorm  = gt.BasisColumn(i).Normalized;
                    vec3? r          = GizmoMath.PlaneIntersectsRay(planeNorm, gt.Origin, rayPos, ray);
                    if (r != null)
                    {
                        float dist = (r.Value - grabberPos).Length;
                        if (dist < _gizmoScale * GIZMO_PLANE_SIZE * 1.5f)
                        {
                            float d = (rayPos - r.Value).Length;
                            if (d < colD) { colD = d; colAxis = i; isPlane = true; }
                        }
                    }
                }
            }

            if (colAxis != -1)
            {
                if (highlightOnly)
                    _highlightAxis = colAxis + (isPlane ? 6 : 0);
                else
                {
                    _edit.Mode  = TransformMode.Translate;
                    ComputeEdit(screenpos);
                    _edit.Plane = TransformPlane.X + colAxis + (isPlane ? 3 : 0);
                }
                return true;
            }
        }

        // ── Rotate ──────────────────────────────────────────────────────────
        if ((Mode & ToolMode.Rotate) != 0 && CanRotateSelection())
        {
            int colAxis = -1;

            float rayLen = (gt.Origin - rayPos).Length + GIZMO_CIRCLE_SIZE * _gizmoScale * 4f;
            var result   = GizmoMath.SegmentIntersectsSphere(rayPos, rayPos + ray * rayLen, gt.Origin, _gizmoScale * GIZMO_CIRCLE_SIZE);
            if (result.Length != 0)
            {
                vec3 hitPos    = result[0];
                vec3 hitNormal = result[1];
                if (vec3.Dot(hitNormal, GetCameraNormal()) < 0.05f)
                {
                    mat4 invBasis = gt.Basis.Inverse;
                    vec3 local    = GizmoMath.BasisTransform(invBasis, hitPos);
                    vec3 abs      = new(MathF.Abs(local.x), MathF.Abs(local.y), MathF.Abs(local.z));
                    int  minIdx   = GizmoMath.MinAxisIndex(abs);
                    float absAtMin = minIdx == 0 ? abs.x : minIdx == 1 ? abs.y : abs.z;
                    if (absAtMin < _gizmoScale * GIZMO_RING_HALF_WIDTH)
                        colAxis = minIdx;
                }
            }

            if (colAxis == -1)
            {
                float colD = 1e20f;
                for (int i = 0; i < 3; i++)
                {
                    vec3  planeNorm = gt.BasisColumn(i).Normalized;
                    vec3? r         = GizmoMath.PlaneIntersectsRay(planeNorm, gt.Origin, rayPos, ray);
                    if (r == null) continue;

                    float dist = (r.Value - gt.Origin).Length;
                    // Allow picking the rotation ring from any angle within the ring bounds
                    if (dist > _gizmoScale * (GIZMO_CIRCLE_SIZE - GIZMO_RING_HALF_WIDTH) &&
                        dist < _gizmoScale * (GIZMO_CIRCLE_SIZE + GIZMO_RING_HALF_WIDTH))
                    {
                        float d = (rayPos - r.Value).Length;
                        if (d < colD) { colD = d; colAxis = i; }
                    }
                }
            }

            if (colAxis != -1)
            {
                if (highlightOnly)
                    _highlightAxis = colAxis + 3;
                else
                {
                    _edit.Mode                    = TransformMode.Rotate;
                    ComputeEdit(screenpos);
                    _edit.Plane                   = TransformPlane.X + colAxis;
                    _edit.AccumulatedRotationAngle = 0f;
                    _edit.RotationAxis            = gt.BasisColumn(colAxis).Normalized;
                    _edit.GizmoInitiated          = true;
                }
                return true;
            }
        }

        // ── Scale ────────────────────────────────────────────────────────────
        if ((Mode & ToolMode.Scale) != 0 && CanScaleSelection())
        {
            int   colAxis = -1;
            float colD    = 1e20f;

            for (int i = 0; i < 3; i++)
            {
                vec3  grabberPos    = gt.Origin + gt.BasisColumn(i).Normalized * _gizmoScale * GIZMO_SCALE_OFFSET;
                float grabberRadius = _gizmoScale * GIZMO_ARROW_SIZE;

                var r = GizmoMath.SegmentIntersectsSphere(rayPos, rayPos + ray * MAX_Z, grabberPos, grabberRadius);
                if (r.Length != 0)
                {
                    float d = (r[0] - rayPos).Length;
                    if (d < colD) { colD = d; colAxis = i; }
                }
            }

            bool isPlane = false;
            if (colAxis == -1)
            {
                colD = 1e20f;
                for (int i = 0; i < 3; i++)
                {
                    vec3  iv2        = gt.BasisColumn((i + 1) % 3).Normalized;
                    vec3  iv3        = gt.BasisColumn((i + 2) % 3).Normalized;
                    vec3  grabberPos = gt.Origin + (iv2 + iv3) * _gizmoScale * (GIZMO_PLANE_SIZE + GIZMO_PLANE_DST * 0.6667f);
                    vec3  planeNorm  = gt.BasisColumn(i).Normalized;
                    vec3? r          = GizmoMath.PlaneIntersectsRay(planeNorm, gt.Origin, rayPos, ray);
                    if (r != null)
                    {
                        float dist = (r.Value - grabberPos).Length;
                        if (dist < _gizmoScale * GIZMO_PLANE_SIZE * 1.5f)
                        {
                            float d = (rayPos - r.Value).Length;
                            if (d < colD) { colD = d; colAxis = i; isPlane = true; }
                        }
                    }
                }
            }

            if (colAxis != -1)
            {
                if (highlightOnly)
                    _highlightAxis = colAxis + (isPlane ? 12 : 9);
                else
                {
                    _edit.Mode  = TransformMode.Scale;
                    ComputeEdit(screenpos);
                    _edit.Plane = TransformPlane.X + colAxis + (isPlane ? 3 : 0);
                }
                return true;
            }
        }

        if (highlightOnly) _highlightAxis = -1;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Transform computation
    // ─────────────────────────────────────────────────────────────────────────

    private void ComputeEdit(Vector2 point)
    {
        _edit.ClickRay    = GetRay(point);
        _edit.ClickRayPos = GetRayPos(point);
        _edit.Plane       = TransformPlane.View;
        UpdateTransformGizmo();
        _edit.Center   = _gizmoTransform.Origin;
        _edit.Original = _gizmoTransform;
        _visualCenter  = _edit.Center;

        for (int i = 0; i < _selections.Count; i++)
        {
            var item = _selections[i];
            item.TargetGlobal   = GetWorldTransform(item.Object);
            item.TargetOriginal = GetLocalTransform(item.Object);
            _selections[i] = item;
        }
    }

    private vec3 UpdateTransform(bool shift)
    {
        vec3  rayPos = GetRayPos(_edit.MousePos);
        vec3  ray    = GetRay(_edit.MousePos);
        float snap   = DEFAULT_FLOAT_STEP;

        Transform3D gt = _gizmoTransform;

        switch (_edit.Mode)
        {
            case TransformMode.Scale:
            {
                vec3 smotionMask = vec3.Zero;
                vec3 planeNorm   = vec3.Zero;
                vec3 planePt     = _edit.Center;
                bool splaneMv    = false;

                switch (_edit.Plane)
                {
                    case TransformPlane.View:
                        smotionMask = vec3.Zero;
                        planeNorm   = GetCameraNormal();
                        break;
                    case TransformPlane.X:
                        smotionMask = gt.BasisColumn(0).Normalized;
                        planeNorm   = vec3.Cross(smotionMask, vec3.Cross(smotionMask, GetCameraNormal())).Normalized;
                        break;
                    case TransformPlane.Y:
                        smotionMask = gt.BasisColumn(1).Normalized;
                        planeNorm   = vec3.Cross(smotionMask, vec3.Cross(smotionMask, GetCameraNormal())).Normalized;
                        break;
                    case TransformPlane.Z:
                        smotionMask = gt.BasisColumn(2).Normalized;
                        planeNorm   = vec3.Cross(smotionMask, vec3.Cross(smotionMask, GetCameraNormal())).Normalized;
                        break;
                    case TransformPlane.YZ:
                        smotionMask = gt.BasisColumn(2).Normalized + gt.BasisColumn(1).Normalized;
                        planeNorm   = gt.BasisColumn(0).Normalized;
                        splaneMv    = true;
                        break;
                    case TransformPlane.XZ:
                        smotionMask = gt.BasisColumn(2).Normalized + gt.BasisColumn(0).Normalized;
                        planeNorm   = gt.BasisColumn(1).Normalized;
                        splaneMv    = true;
                        break;
                    case TransformPlane.XY:
                        smotionMask = gt.BasisColumn(0).Normalized + gt.BasisColumn(1).Normalized;
                        planeNorm   = gt.BasisColumn(2).Normalized;
                        splaneMv    = true;
                        break;
                }

                vec3? si = GizmoMath.PlaneIntersectsRay(planeNorm, planePt, rayPos, ray);
                if (si == null) break;
                vec3? sc = GizmoMath.PlaneIntersectsRay(planeNorm, planePt, _edit.ClickRayPos, _edit.ClickRay);
                if (sc == null) break;

                vec3 smotion = si.Value - sc.Value;
                if (_edit.Plane != TransformPlane.View)
                {
                    if (!splaneMv)
                        smotion = smotionMask * vec3.Dot(smotionMask, smotion);
                    else if (shift)
                        smotion = smotionMask * vec3.Dot(smotionMask, smotion);
                }
                else
                {
                    float clickDist  = (sc.Value - _edit.Center).Length;
                    float intersDist = (si.Value - _edit.Center).Length;
                    if (clickDist == 0) break;
                    float scale = intersDist - clickDist;
                    smotion = new(scale, scale, scale);
                }

                smotion /= (sc.Value - _edit.Center).Length;

                bool slocalCoords = UseLocalSpace && _edit.Plane != TransformPlane.View;
                if (Snapping) snap = GetScaleSnap();
                if (slocalCoords)
                    smotion = GizmoMath.BasisTransform(_edit.Original.Basis.Inverse, smotion);

                smotion = EditScale(smotion);
                vec3 smSnapped = GizmoMath.Snapped(smotion, snap);
                Message = $"Scaling: ({smSnapped.x:0.###}, {smSnapped.y:0.###}, {smSnapped.z:0.###})";
                ApplyTransform(smotion, snap);
                return smotion;
            }

            case TransformMode.Translate:
            {
                vec3 tmotionMask = vec3.Zero;
                vec3 planeNorm   = vec3.Zero;
                vec3 planePt     = _edit.Center;
                bool tplaneMv    = false;

                switch (_edit.Plane)
                {
                    case TransformPlane.View:
                        planeNorm = GetCameraNormal();
                        break;
                    case TransformPlane.X:
                        tmotionMask = gt.BasisColumn(0).Normalized;
                        planeNorm   = vec3.Cross(tmotionMask, vec3.Cross(tmotionMask, GetCameraNormal())).Normalized;
                        break;
                    case TransformPlane.Y:
                        tmotionMask = gt.BasisColumn(1).Normalized;
                        planeNorm   = vec3.Cross(tmotionMask, vec3.Cross(tmotionMask, GetCameraNormal())).Normalized;
                        break;
                    case TransformPlane.Z:
                        tmotionMask = gt.BasisColumn(2).Normalized;
                        planeNorm   = vec3.Cross(tmotionMask, vec3.Cross(tmotionMask, GetCameraNormal())).Normalized;
                        break;
                    case TransformPlane.YZ:
                        planeNorm = gt.BasisColumn(0).Normalized;
                        tplaneMv  = true;
                        break;
                    case TransformPlane.XZ:
                        planeNorm = gt.BasisColumn(1).Normalized;
                        tplaneMv  = true;
                        break;
                    case TransformPlane.XY:
                        planeNorm = gt.BasisColumn(2).Normalized;
                        tplaneMv  = true;
                        break;
                }

                vec3? ti = GizmoMath.PlaneIntersectsRay(planeNorm, planePt, rayPos, ray);
                if (ti == null) break;
                vec3? tc = GizmoMath.PlaneIntersectsRay(planeNorm, planePt, _edit.ClickRayPos, _edit.ClickRay);
                if (tc == null) break;

                vec3 tmotion = ti.Value - tc.Value;
                if (_edit.Plane != TransformPlane.View && !tplaneMv)
                    tmotion = tmotionMask * vec3.Dot(tmotionMask, tmotion);

                bool tlocalCoords = UseLocalSpace && _edit.Plane != TransformPlane.View;
                if (Snapping) snap = GetTranslateSnap();
                if (tlocalCoords)
                    tmotion = GizmoMath.BasisTransform(_gizmoTransform.Basis.Inverse, tmotion);

                tmotion = EditTranslate(tmotion);
                vec3 tmSnapped = GizmoMath.Snapped(tmotion, snap);
                Message = $"Translating: ({tmSnapped.x:0.###}, {tmSnapped.y:0.###}, {tmSnapped.z:0.###})";
                ApplyTransform(tmotion, snap);
                return tmotion;
            }

            case TransformMode.Rotate:
            {
                vec3 camToObj = _edit.Center - (_camera?.Position ?? vec3.Zero);
                vec3 planeNorm;
                if (camToObj != vec3.Zero)
                    planeNorm = camToObj.Normalized;
                else
                    planeNorm = GetCameraNormal();

                vec3 planePt    = _edit.Center;
                vec3 localAxis  = vec3.Zero;
                vec3 globalAxis = vec3.Zero;

                switch (_edit.Plane)
                {
                    case TransformPlane.View:
                        globalAxis = planeNorm;
                        break;
                    case TransformPlane.X:
                        localAxis = vec3.UnitX;
                        globalAxis = vec3.UnitX;
                        break;
                    case TransformPlane.Y:
                        localAxis = vec3.UnitY;
                        globalAxis = vec3.UnitY;
                        break;
                    case TransformPlane.Z:
                        localAxis = vec3.UnitZ;
                        globalAxis = vec3.UnitZ;
                        break;
                }

                if (UseLocalSpace && _edit.Plane != TransformPlane.View)
                    globalAxis = GizmoMath.BasisTransform(_gizmoTransform.Basis, localAxis).Normalized;

                vec3? ri = GizmoMath.PlaneIntersectsRay(planeNorm, planePt, rayPos, ray);
                if (ri == null) break;
                vec3? rc = GizmoMath.PlaneIntersectsRay(planeNorm, planePt, _edit.ClickRayPos, _edit.ClickRay);
                if (rc == null) break;

                vec3 curVec = (ri.Value - _edit.Center).Normalized;
                if (!_edit.InitialClickVector.HasValue)
                {
                    _edit.InitialClickVector       = (rc.Value - _edit.Center).Normalized;
                    _edit.PreviousRotationVector   = curVec;
                    _edit.AccumulatedRotationAngle = 0f;
                    _edit.DisplayRotationAngle     = 0f;
                }

                float orthThreshold  = MathF.Cos(glm.Radians(85f));
                bool  axisOrthogonal = MathF.Abs(vec3.Dot(planeNorm, globalAxis)) < orthThreshold;

                float angle;
                if (axisOrthogonal)
                {
                    _edit.ShowRotationLine = false;
                    vec3  projAxis = vec3.Cross(planeNorm, globalAxis);
                    vec3  delta    = ri.Value - rc.Value;
                    float proj     = vec3.Dot(delta, projAxis);
                    angle = (proj * (MathF.PI / 2f)) / (_gizmoScale * GIZMO_CIRCLE_SIZE);
                }
                else
                {
                    _edit.ShowRotationLine = true;
                    vec3 clickAxis = (rc.Value - _edit.Center).Normalized;
                    angle = GizmoMath.SignedAngleTo(clickAxis, curVec, globalAxis);
                }

                if (_edit.PreviousRotationVector.HasValue)
                {
                    float da = GizmoMath.SignedAngleTo(_edit.PreviousRotationVector.Value, curVec, globalAxis);
                    _edit.AccumulatedRotationAngle += da;
                }
                _edit.PreviousRotationVector = curVec;

                if (Snapping)
                {
                    snap = GetRotationSnap();
                    _edit.DisplayRotationAngle = glm.Radians(
                        GizmoMath.Snapped(glm.Degrees(_edit.AccumulatedRotationAngle), snap));
                }
                else
                {
                    _edit.DisplayRotationAngle = _edit.AccumulatedRotationAngle;
                }

                bool    rlocalCoords = UseLocalSpace && _edit.Plane != TransformPlane.View;
                vec3    computeAxis  = rlocalCoords ? localAxis : globalAxis;
                vec3    rotResult    = EditRotate(computeAxis * angle);
                if (rotResult != computeAxis * angle)
                {
                    computeAxis = rotResult.Normalized;
                    angle       = rotResult.Length;
                }

                float angleDeg = glm.Degrees(angle);
                if (Snapping) angleDeg = GizmoMath.Snapped(angleDeg, snap);
                Message = $"Rotating: {angleDeg:0.###} degrees";
                angle   = glm.Radians(angleDeg);

                ApplyTransform(computeAxis, angle);
                return computeAxis * angle;
            }
        }

        return vec3.Zero;
    }

    private void ApplyTransform(vec3 motion, float snap)
    {
        bool localCoords = UseLocalSpace && _edit.Plane != TransformPlane.View;

        for (int i = 0; i < _selections.Count; i++)
        {
            var item = _selections[i];
            Transform3D newTransform = ComputeTransform(
                _edit.Mode,
                item.TargetGlobal,
                item.TargetOriginal,
                motion, snap, localCoords,
                _edit.Plane != TransformPlane.View);

            SceneObject obj = item.Object;

            if (_edit.Mode == TransformMode.Translate)
            {
                vec3 newWorldPos = newTransform.Origin;

                if (obj.Parent != null)
                {
                    mat4 parentTransform = obj.GetParentWorldTransform();
                    mat4 parentInverse   = parentTransform.Inverse;
                    vec4 localH          = parentInverse * new vec4(newWorldPos, 1f);
                    obj.SetLocalPosition(new vec3(localH.x, localH.y, localH.z));
                }
                else
                {
                    obj.SetLocalPosition(newWorldPos);
                }
            }
            else if (_edit.Mode == TransformMode.Rotate)
            {
                if (obj is CameraSceneObject cameraObject)
                {
                    vec3 forward = GizmoMath.BasisTransform(newTransform.Basis, vec3.UnitZ);
                    cameraObject.ApplyLookDirection(forward);
                }
                else
                {
                    // Strip scale from the basis before extracting Euler angles.
                    // In local-rotation mode, ComputeTransform returns purRot * rotMat * scaleMat,
                    // so the basis includes scale. MatrixToEulerYXZ assumes a pure-rotation matrix
                    // (m.m21 == -sin(x) only holds without scale), so we must normalise first.
                    obj.SetLocalRotation(GizmoMath.MatrixToEulerYXZ(NormalizeRotation(newTransform.Basis)));
                }
            }
            else if (_edit.Mode == TransformMode.Scale)
            {
                obj.SetLocalScale(GizmoMath.ExtractScale(newTransform.Basis));
            }
        }

        if (_edit.Mode == TransformMode.Translate && _selections.Count > 0)
        {
            vec3 liveCenter = vec3.Zero;
            foreach (var s in _selections)
                liveCenter += s.Object.GetWorldPosition();
            _visualCenter = liveCenter / _selections.Count;
        }

        UpdateTransformGizmo();
    }

    private Transform3D ComputeTransform(TransformMode mode, Transform3D original,
                                          Transform3D originalLocal, vec3 motion,
                                          float extra, bool local, bool orthogonal)
    {
        switch (mode)
        {
            case TransformMode.Scale:
            {
                if (Snapping) motion = GizmoMath.Snapped(motion, extra);
                Transform3D s;
                if (local)
                {
                    mat4 newBasis  = originalLocal.Basis;
                    vec3 scaleVec  = motion + vec3.Ones;
                    // mCR: scale col0 by scaleVec.x, col1 by .y, col2 by .z
                    newBasis.m00 *= scaleVec.x; newBasis.m01 *= scaleVec.x; newBasis.m02 *= scaleVec.x;
                    newBasis.m10 *= scaleVec.y; newBasis.m11 *= scaleVec.y; newBasis.m12 *= scaleVec.y;
                    newBasis.m20 *= scaleVec.z; newBasis.m21 *= scaleVec.z; newBasis.m22 *= scaleVec.z;
                    s = new Transform3D(newBasis, originalLocal.Origin);
                }
                else
                {
                    vec3 sv = motion + vec3.Ones;
                    vec3 newOrigin = new(
                        sv.x * (original.Origin.x - _edit.Center.x) + _edit.Center.x,
                        sv.y * (original.Origin.y - _edit.Center.y) + _edit.Center.y,
                        sv.z * (original.Origin.z - _edit.Center.z) + _edit.Center.z);

                    // Use originalLocal.Basis which contains the actual scale, not original.Basis 
                    // which has scale stripped for gizmo display consistency.
                    mat4 newBasis = originalLocal.Basis;
                    if (orthogonal)
                        newBasis = GizmoMath.ScaledOrthogonal(newBasis, sv);
                    else
                    {
                        newBasis.m00 *= sv.x; newBasis.m01 *= sv.x; newBasis.m02 *= sv.x;
                        newBasis.m10 *= sv.y; newBasis.m11 *= sv.y; newBasis.m12 *= sv.y;
                        newBasis.m20 *= sv.z; newBasis.m21 *= sv.z; newBasis.m22 *= sv.z;
                    }
                    s = new Transform3D(newBasis, newOrigin);
                }
                return s;
            }

            case TransformMode.Translate:
            {
                if (Snapping) motion = GizmoMath.Snapped(motion, extra);
                if (local)
                    return original.TranslatedLocal(motion);
                return original.Translated(motion);
            }

            case TransformMode.Rotate:
            {
                vec3 axis   = motion.LengthSqr > 1e-8f ? motion.Normalized : vec3.UnitY;
                mat4 rotMat = GizmoMath.AxisAngle(axis, extra);
                if (local)
                {
                    // Post-multiply so the rotation is applied in the object's own local
                    // coordinate frame, not world space.  Strip scale from the local basis
                    // first (originalLocal.Basis = rot * scl) so that the pure-rotation
                    // matrix fed to MatrixToEulerYXZ / SetLocalRotation is correct; then
                    // reattach scale so ExtractScale still produces the right value.
                    vec3 scaleVec = GizmoMath.ExtractScale(originalLocal.Basis);
                    mat4 scaleMat = mat4.Scale(scaleVec);
                    mat4 purRot   = originalLocal.Basis * scaleMat.Inverse;
                    return new Transform3D(purRot * rotMat * scaleMat, originalLocal.Origin);
                }
                else
                {
                    vec3 newOrigin = GizmoMath.BasisTransform(rotMat, original.Origin - _edit.Center) + _edit.Center;
                    mat4 newBasis  = rotMat * original.Basis;

                    if (original.Basis != originalLocal.Basis)
                    {
                        mat4 localToWorld = original.Basis * originalLocal.Basis.Inverse;
                        mat4 worldToLocal = localToWorld.Inverse;
                        newBasis = rotMat * localToWorld * originalLocal.Basis;
                        // The local basis is what gets written back; world-relative rotation is baked in.
                        _ = worldToLocal; // used implicitly in the expression above
                    }
                    return new Transform3D(newBasis, newOrigin);
                }
            }

            default:
                Console.Error.WriteLine("Gizmo3D#ComputeTransform: Invalid mode");
                return Transform3D.Identity;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Camera helpers
    // ─────────────────────────────────────────────────────────────────────────

    private vec3 GetRayPos(Vector2 screenPos)
    {
        if (_camera == null) return vec3.Zero;
        return _camera.ProjectRayOrigin(screenPos, _imageMin, _imageSize);
    }

    private vec3 GetRay(Vector2 screenPos)
    {
        if (_camera == null) return new vec3(0, 0, -1);
        return _camera.ProjectRayNormal(screenPos, _imageMin, _imageSize);
    }

    private vec3 GetCameraNormal()
    {
        if (_camera == null) return new vec3(0, 0, -1);
        mat4 view = _camera.GetViewMatrix();
        // Camera forward = -view column 2 in world space
        return -new vec3(view.m02, view.m12, view.m22).Normalized;
    }

    private Vector2 PointToScreen(vec3 worldPos)
    {
        if (_camera == null) return default;
        return _camera.UnprojectPosition(worldPos, _imageMin, _imageSize);
    }

    private Vector2 UnprojectPos(vec3 worldPos)
    {
        if (_camera == null) return Vector2.Zero;
        return _camera.UnprojectPosition(worldPos, _imageMin, _imageSize);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SceneObject transform helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Transform3D GetWorldTransform(SceneObject obj)
    {
        vec3 worldOrigin = obj.GetWorldPosition();

        // Build a pure-rotation basis (no scale) so gizmo axes are always unit-length
        // and correctly oriented regardless of the object's scale.
        mat4 rx  = mat4.RotateX(obj.Rotation.x);
        mat4 ry  = mat4.RotateY(obj.Rotation.y);
        mat4 rz  = mat4.RotateZ(obj.Rotation.z);
        mat4 rot = rz * ry * rx;   // local rotation only

        // Accumulate parent rotations (strip scale from each parent's contribution
        // by normalising each column before combining).
        if (obj.Parent != null)
        {
            mat4 parentWorld = obj.GetParentWorldTransform();
            mat4 parentRot   = NormalizeRotation(parentWorld);
            rot = parentRot * rot;
        }

        return new Transform3D(rot, worldOrigin);
    }

    /// <summary>
    /// Returns a copy of <paramref name="m"/> with the scale stripped from the
    /// upper-left 3×3 by normalising each column vector.
    /// </summary>
    private static mat4 NormalizeRotation(mat4 m)
    {
        // mCR: col0=(m00,m01,m02), col1=(m10,m11,m12), col2=(m20,m21,m22)
        vec3 col0 = new vec3(m.m00, m.m01, m.m02).Normalized;
        vec3 col1 = new vec3(m.m10, m.m11, m.m12).Normalized;
        vec3 col2 = new vec3(m.m20, m.m21, m.m22).Normalized;
        mat4 r = mat4.Identity;
        r.m00 = col0.x; r.m01 = col0.y; r.m02 = col0.z;
        r.m10 = col1.x; r.m11 = col1.y; r.m12 = col1.z;
        r.m20 = col2.x; r.m21 = col2.y; r.m22 = col2.z;
        return r;
    }

    private static Transform3D GetLocalTransform(SceneObject obj)
    {
        mat4 rx   = mat4.RotateX(obj.Rotation.x);
        mat4 ry   = mat4.RotateY(obj.Rotation.y);
        mat4 rz   = mat4.RotateZ(obj.Rotation.z);
        mat4 rot  = rz * ry * rx;
        mat4 scl  = mat4.Scale(obj.Scale);
        mat4 basis = rot * scl;
        return new Transform3D(basis, obj.Position);
    }

    private static (vec3 pos, vec3 size) GetObjectAabb(SceneObject obj)
    {
        vec3 scale = obj.Scale == vec3.Zero ? vec3.Ones : obj.Scale;
        vec3 size  = scale;
        vec3 pos   = -size * 0.5f - obj.PivotOffset;
        return (pos, size);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Axis transform for gizmo handle rendering
    // ─────────────────────────────────────────────────────────────────────────

    private mat4 BuildAxisTransform(int axis)
    {
        vec3 basisAxis = _gizmoTransform.BasisColumn(axis).Normalized;
        vec3 basisUp   = _gizmoTransform.BasisColumn((axis + 1) % 3).Normalized;

        mat4 orient;
        if (basisAxis.LengthSqr < 1e-8f || MathF.Abs(vec3.Dot(basisAxis, basisUp)) > 0.9999f)
        {
            orient = mat4.Identity;
        }
        else
        {
            // Local –Z → basisAxis; i.e. local +Z → –basisAxis.
            vec3 zRow  = -basisAxis;
            vec3 right = vec3.Cross(basisAxis, basisUp).Normalized;
            vec3 yRow  = vec3.Cross(zRow, right);

            // mCR: col0=(m00,m01,m02)=right, col1=(m10,m11,m12)=yRow, col2=(m20,m21,m22)=zRow
            orient = mat4.Identity;
            orient.m00 = right.x; orient.m01 = right.y; orient.m02 = right.z;
            orient.m10 = yRow.x;  orient.m11 = yRow.y;  orient.m12 = yRow.z;
            orient.m20 = zRow.x;  orient.m21 = zRow.y;  orient.m22 = zRow.z;
        }

        // Scale then translate
        mat4 scaleMat = mat4.Scale(new vec3(_gizmoScale));
        mat4 transMat = mat4.Translate(_gizmoTransform.Origin);
        return transMat * scaleMat * orient;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Virtual override points
    // ─────────────────────────────────────────────────────────────────────────

    protected virtual vec3 EditTranslate(vec3 translation) => translation;
    protected virtual vec3 EditScale(vec3 scale)           => scale;
    protected virtual vec3 EditRotate(vec3 rotation)       => rotation;

    // ─────────────────────────────────────────────────────────────────────────
    // Color utilities
    // ─────────────────────────────────────────────────────────────────────────

    private static vec4 WithAlpha(vec4 c, float alpha)
        => new(c.x, c.y, c.z, Math.Clamp(alpha, 0f, 1f));

    /// <summary>
    /// Return a copy of <paramref name="c"/> with HSV saturation/value/alpha overrides,
    /// preserving the hue.  Used for highlight and arc colors.
    /// </summary>
    private static vec4 HsvAdjust(vec4 c, float s, float v, float a)
    {
        float h = GetHue(c);
        return HsvToRgba(h, s, v, a);
    }

    private static vec4 HsvToRgba(float h, float s, float v, float a)
    {
        h = h % 1f;
        if (h < 0) h += 1f;
        float hh = h * 6f;
        int   i  = (int)hh;
        float ff = hh - i;
        float p  = v * (1f - s);
        float q  = v * (1f - s * ff);
        float t  = v * (1f - s * (1f - ff));
        float r, g, b;
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return new vec4(r, g, b, a);
    }

    private static float GetHue(vec4 c)
    {
        float max   = MathF.Max(c.x, MathF.Max(c.y, c.z));
        float min   = MathF.Min(c.x, MathF.Min(c.y, c.z));
        float delta = max - min;
        if (delta < 1e-6f) return 0;
        float h;
        if (max == c.x)      h = ((c.y - c.z) / delta) % 6f;
        else if (max == c.y) h = (c.z - c.x) / delta + 2f;
        else                 h = (c.x - c.y) / delta + 4f;
        h /= 6f;
        if (h < 0) h += 1f;
        return h;
    }

    private static uint ToImGuiColor(vec4 c)
    {
        byte r = (byte)(Math.Clamp(c.x, 0f, 1f) * 255f);
        byte g = (byte)(Math.Clamp(c.y, 0f, 1f) * 255f);
        byte b = (byte)(Math.Clamp(c.z, 0f, 1f) * 255f);
        byte a = (byte)(Math.Clamp(c.w, 0f, 1f) * 255f);
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        for (int i = 0; i < 3; i++)
        {
            FreePart(ref _moveGizmo[i]);
            FreePart(ref _movePlaneGizmo[i]);
            FreePart(ref _rotateGizmo[i]);
            FreePart(ref _scaleGizmo[i]);
            FreePart(ref _scalePlaneGizmo[i]);
            FreePart(ref _axisGizmo[i]);
        }
        (_shader as IDisposable)?.Dispose();
    }
}
