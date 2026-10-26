using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>
/// A scene object that represents a skeletal bone imported from a 3-D model file.
///
/// Bones participate in the normal <see cref="SceneObject"/> hierarchy, support
/// keyframe animation, and are selectable via the colour-pick pass.
///
/// A small octahedron <see cref="IndicatorMesh"/> is generated at construction
/// and rendered by the Viewport as a flat-coloured 3-D indicator so the user
/// can always see and click the bone even when no mesh geometry covers its origin.
/// </summary>
public class BoneSceneObject : SceneObject
{
    /// <summary>Optional unbent endpoint in bone-local coordinates. Null targets the bone origin.</summary>
    public vec3? EndOffset { get; set; }

    public List<BoneConstraint> Constraints { get; set; } = new();

    internal mat4 EvaluatedIkRotation { get; private set; } = mat4.Identity;
    private mat4? _evaluatedIkWorldRotation;
    private mat4 _ikRotationEditAuthored = mat4.Identity;
    private mat4 _ikRotationEditPrefix = mat4.Identity;
    private mat4 _ikRotationEditSuffix = mat4.Identity;

    internal void SetEvaluatedIkRotation(mat4 rotation)
    {
        EvaluatedIkRotation = rotation;
        InvalidateRenderEvaluation();
    }

    internal virtual void ResetInverseKinematics()
    {
        _evaluatedIkWorldRotation = null;
        SetEvaluatedIkRotation(mat4.Identity);
    }

    internal void PreserveInverseKinematicsWorldRotation(mat4 reference)
    {
        _evaluatedIkWorldRotation = reference;
        InvalidateRenderEvaluation();
    }

    internal mat4 CaptureInverseKinematicsWorldRotation() =>
        GetWorldRotationEditFrame(out _ikRotationEditPrefix, out _ikRotationEditAuthored, out _ikRotationEditSuffix);

    internal bool TryGetInverseKinematicsRotationEditFrame(out mat4 referenceWorld, out mat4 authoredRotation,
        out mat4 rotationPrefix, out mat4 rotationSuffix)
    {
        referenceWorld = _evaluatedIkWorldRotation ?? mat4.Identity;
        authoredRotation = _ikRotationEditAuthored;
        rotationPrefix = _ikRotationEditPrefix;
        rotationSuffix = _ikRotationEditSuffix;
        return _evaluatedIkWorldRotation.HasValue;
    }

    internal mat4 ApplyInverseKinematicsWorldRotation(mat4 world)
    {
        if (_evaluatedIkWorldRotation is not { } reference) return world;
        static vec3 BasisScale(mat4 matrix) => new(
            new vec3(matrix.m00, matrix.m01, matrix.m02).Length,
            new vec3(matrix.m10, matrix.m11, matrix.m12).Length,
            new vec3(matrix.m20, matrix.m21, matrix.m22).Length);
        vec3 referenceScale = BasisScale(reference), scale = BasisScale(world);
        if (!(referenceScale.x > 1e-8f && referenceScale.y > 1e-8f && referenceScale.z > 1e-8f) ||
            !float.IsFinite(referenceScale.x + referenceScale.y + referenceScale.z) ||
            !float.IsFinite(scale.x + scale.y + scale.z)) return world;

        // Hold the pre-IK basis directions, but keep the solved position and live scale.
        mat4 result = reference * mat4.Scale(scale / referenceScale);
        result.m30 = world.m30; result.m31 = world.m31; result.m32 = world.m32;
        return result;
    }

    protected override void GetTimelineTransform(out mat4 modelTransform, out vec3 position, out mat4 rotation)
    {
        base.GetTimelineTransform(out modelTransform, out position, out rotation);
        rotation *= EvaluatedIkRotation;
    }

    /// <summary>Evaluated endpoint, including a Mine-imator bone's internal bend joint.</summary>
    public vec3 GetEndWorldPosition()
    {
        vec3 endpoint = EndOffset ?? vec3.Zero;
        if (EndOffset.HasValue && this is MiBoneSceneObject mi && mi.GetEffectiveBendParameters() is { } bend)
            endpoint = TransformPoint(BendHelper.GetPartBendMatrix(bend, BendHelper.GetBendVector(bend.Angle, 1f)),
                endpoint - BendHelper.GetBendPivot(bend, vec3.Zero));
        return TransformPoint(GetWorldMatrixNoPivot(), endpoint);
    }

    internal static vec3 TransformPoint(mat4 matrix, vec3 point)
    {
        vec4 result = matrix * new vec4(point, 1f);
        return new vec3(result.x, result.y, result.z);
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The original bone name as stored in the source asset.
    /// Set by <see cref="MineImatorSimplyRemade.core.mdl.AssimpModelLoader"/>.
    /// </summary>
    public string BoneName = "";

    // ── Visual indicator ──────────────────────────────────────────────────────

    /// <summary>
    /// A small procedural octahedron rendered by the Viewport as a flat-coloured
    /// 3-D indicator.  Built once via <see cref="CreateIndicator"/>.
    /// Null until GL is available.
    /// </summary>
    public Mesh? IndicatorMesh { get; private set; }

    /// <summary>
    /// Builds the octahedron indicator mesh.  Must be called once on the GL
    /// thread (typically from <see cref="AssimpModelLoader"/> after import).
    /// </summary>
    public void CreateIndicator(GL gl)
    {
        // Small octahedron: 6 vertices, 8 triangular faces.
        // Sized to be clearly visible but not distracting (0.08 world units radius).
        const float r  = 0.08f; // equatorial radius
        const float h  = 0.12f; // half-height (top/bottom tips)

        var verts = new vec3[]
        {
            new( 0,  h,  0),  // 0 top
            new( r,  0,  0),  // 1 +X
            new( 0,  0,  r),  // 2 +Z
            new(-r,  0,  0),  // 3 -X
            new( 0,  0, -r),  // 4 -Z
            new( 0, -h,  0),  // 5 bottom
        };

        var idx = new uint[]
        {
            // upper 4 faces
            0, 1, 2,
            0, 2, 3,
            0, 3, 4,
            0, 4, 1,
            // lower 4 faces
            5, 2, 1,
            5, 3, 2,
            5, 4, 3,
            5, 1, 4,
        };

        IndicatorMesh = new Mesh(gl, verts, indices: idx);
    }

    // ── Icon ──────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override string GetObjectIcon() => "Bone";
}
