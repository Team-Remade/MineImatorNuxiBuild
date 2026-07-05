using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using System;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>
/// Holds data needed to regenerate a single shape mesh on a Mine Imator bone.
/// Stored so that when the bend angle changes the mesh can be rebuilt.
/// </summary>
public class BoneShapeData
{
    public string    PartName;
    public int       ShapeIndex;
    public MiShape   Shape;
    public MiModel   Model;
    public uint      TextureId;
    public vec3      AccumulatedScale;
    public BendStyle ModelBendStyle;
    public float?    PartColorAlpha;
    public int       PartDepth;
}

/// <summary>
/// A scene object representing a single bone imported from a Mine Imator .mimodel file.
///
/// Bones store their model-loaded transform as a "base pose" and expose a
/// user-editable delta on top of it.  The Properties panel reads/writes
/// <see cref="OffsetPosition"/> / <see cref="OffsetRotation"/> / <see cref="OffsetScale"/>
/// which are always zero / one at load time.
/// </summary>
public class MiBoneSceneObject : BoneSceneObject
{
    public MiBoneSceneObject()
    {
        // Mine Imator shapes are already positioned relative to their bone origin.
        // The default SceneObject pivot (0, 0.5, 0) must not displace them.
        PivotOffset = vec3.Zero;
    }

    // ── Base pose (set once by the loader, never changed by the user) ─────────

    private vec3  _basePosePosition = vec3.Zero;
    private vec3  _basePoseRotation = vec3.Zero;
    private vec3  _basePoseScale    = vec3.Ones;

    /// <summary>
    /// Stores the model-loaded transform as the base pose and resets the user
    /// offsets to zero / one.  Call this after <see cref="SceneObject.SetLocalPosition"/>,
    /// <see cref="SceneObject.SetLocalRotation"/>, and <see cref="SceneObject.SetLocalScale"/>
    /// have been applied by the loader.
    /// </summary>
    public void CommitBasePose()
    {
        _basePosePosition = LocalPosition;
        _basePoseRotation = LocalRotation;
        _basePoseScale    = LocalScale;
        // Offsets start at zero / one — no extra work needed since the properties
        // below derive them on demand.
    }

    // ── User-editable offsets (what the Properties panel reads and writes) ─────

    /// <summary>Position delta relative to the base pose (displayed in the UI).</summary>
    public vec3 OffsetPosition
    {
        get => LocalPosition - _basePosePosition;
        set => SetLocalPosition(_basePosePosition + value);
    }

    /// <summary>Rotation delta relative to the base pose (displayed in the UI).</summary>
    public vec3 OffsetRotation
    {
        get
        {
            // Mine-imator offsets are authored relative to the imported base pose in
            // parent/model space. Derive delta with right-side inverse so mirrored
            // base poses (e.g. Y=180) keep the expected up/down direction.
            mat4 baseRot = BuildRotationMatrix(_basePoseRotation);
            mat4 localRot = BuildRotationMatrix(LocalRotation);
            mat4 deltaRot = localRot * baseRot.Inverse;
            return MatrixToEulerRzRyRx(deltaRot);
        }
        set
        {
            mat4 baseRot = BuildRotationMatrix(_basePoseRotation);
            mat4 deltaRot = BuildRotationMatrix(value);
            mat4 composed = deltaRot * baseRot;
            SetLocalRotation(MatrixToEulerRzRyRx(composed));
        }
    }

    /// <summary>
    /// Scale relative to the base pose (displayed in the UI, 1 = no change).
    /// Stored as a multiplier: LocalScale = basePoseScale * OffsetScale.
    /// </summary>
    public vec3 OffsetScale
    {
        get => new vec3(
            _basePoseScale.x != 0 ? LocalScale.x / _basePoseScale.x : LocalScale.x,
            _basePoseScale.y != 0 ? LocalScale.y / _basePoseScale.y : LocalScale.y,
            _basePoseScale.z != 0 ? LocalScale.z / _basePoseScale.z : LocalScale.z);
        set => SetLocalScale(new vec3(
            _basePoseScale.x * value.x,
            _basePoseScale.y * value.y,
            _basePoseScale.z * value.z));
    }

    // ── Bend data ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The bend parameters parsed from the MiPart's bend JSON (null if no bend).
    /// The Angle field is the current editable angle.
    /// </summary>
    public BendParams? BendParameters { get; private set; }

    /// <summary>lock_bend value from the model (0–1, default 1).</summary>
    public new float LockBend { get; private set; } = 1f;

    /// <summary>Shape data list used to regenerate meshes when bend angle changes.</summary>
    private readonly List<BoneShapeData> _shapeDataList = new();

    // ── color_alpha / depth ───────────────────────────────────────────────────

    /// <summary>Alpha override loaded from model's color_alpha property.</summary>
    public float? ColorAlpha { get; set; }

    /// <summary>Render priority depth from model's depth property.</summary>
    public int Depth { get; set; }

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>Stores the bend parameters for this bone (called by the loader).</summary>
    public void SetBendParameters(BendParams? bendParams, float lockBend)
    {
        BendParameters = bendParams;
        LockBend = lockBend;
    }

    /// <summary>
    /// Returns the bend transform at the end of this bone's bend region.
    /// Child objects use this to stay attached to the bent half of the limb.
    /// </summary>
    public mat4 GetBentHalfTransform(vec3 shapePosition)
    {
        if (!BendParameters.HasValue)
            return mat4.Identity;

        var bendParams = BendParameters.Value;
        var bendVector = BendHelper.GetBendVector(GetEffectiveBendAngle(), 1.0f);
        return BendHelper.GetBendMatrix(bendParams, bendVector, shapePosition);
    }

    /// <summary>Registers shape data so meshes can be regenerated when the bend angle changes.</summary>
    public void RegisterShapeData(BoneShapeData data)
    {
        _shapeDataList.Add(data);
    }

    /// <summary>
    /// Replaces the texture on all current visual meshes (that have a texture)
    /// <em>and</em> updates the stored <see cref="BoneShapeData"/> entries so the
    /// override persists through future <see cref="RegenerateMeshes"/> calls.
    /// Only affects meshes/shapes that already carry a non-zero texture.
    /// </summary>
    public void OverrideTexture(uint textureId)
    {
        // Update live meshes
        foreach (var mesh in Visuals)
        {
            if (mesh.TextureId != 0)
                mesh.TextureId = textureId;
        }

        // Update stored shape data so the override survives RegenerateMeshes()
        foreach (var sd in _shapeDataList)
        {
            if (sd.TextureId != 0)
                sd.TextureId = textureId;
        }
    }

    /// <summary>Updates the bend angle and regenerates all shape meshes.</summary>
    public void SetBendAngle(vec3 newAngle)
    {
        if (!BendParameters.HasValue) return;

        var bp = BendParameters.Value;
        newAngle.x = Math.Clamp(newAngle.x, bp.DirectionMin.x, bp.DirectionMax.x);
        newAngle.y = Math.Clamp(newAngle.y, bp.DirectionMin.y, bp.DirectionMax.y);
        newAngle.z = Math.Clamp(newAngle.z, bp.DirectionMin.z, bp.DirectionMax.z);
        bp.Angle   = newAngle;
        BendParameters = bp;

        RegenerateMeshes();
    }

    /// <summary>
    /// Rebuilds all mesh instances for this bone using the current bend angle.
    /// Also triggers regeneration on child bones whose InheritBend is true.
    /// </summary>
    public void RegenerateMeshes()
    {
        if (_shapeDataList.Count > 0)
        {
            Visuals.Clear();

            BendParams? effectiveBendParams = null;
            if (BendParameters.HasValue)
            {
                var bp = BendParameters.Value;
                bp.Angle           = GetEffectiveBendAngle();
                effectiveBendParams = bp;
            }

            var loader = MineImatorLoader.Instance;
            foreach (var sd in _shapeDataList)
            {
                var mesh = loader.CreateShapeMeshPublic(
                    sd.PartName, sd.ShapeIndex, sd.Shape, sd.Model,
                    sd.TextureId, sd.AccumulatedScale, effectiveBendParams,
                    sd.ModelBendStyle, sd.PartColorAlpha, sd.PartDepth);

                if (mesh != null) AddMesh(mesh);
            }
        }

        // Propagate to inheriting children
        foreach (var child in GetChildrenObjects())
        {
            if (child is MiBoneSceneObject childBone &&
                childBone.BendParameters.HasValue &&
                childBone.BendParameters.Value.InheritBend)
            {
                childBone.RegenerateMeshes();
            }
        }
    }

    private vec3 GetEffectiveBendAngle()
    {
        if (!BendParameters.HasValue) return vec3.Zero;
        var angle = BendParameters.Value.Angle;
        if (BendParameters.Value.InheritBend && Parent is MiBoneSceneObject parentBone && parentBone.BendParameters.HasValue)
            angle += parentBone.GetEffectiveBendAngle();
        return angle;
    }

    // ── Inheritance helpers ───────────────────────────────────────────────────

    public void InheritColorAlphaFromParent()
    {
        if (ColorAlpha.HasValue) return;
        if (Parent is MiBoneSceneObject parentBone)
            ColorAlpha = parentBone.ColorAlpha;
    }

    public void InheritDepthFromParent()
    {
        if (Depth != 0) return;
        if (Parent is MiBoneSceneObject parentBone)
            Depth = parentBone.Depth;
    }

    // ── Icon ──────────────────────────────────────────────────────────────────

    public override string GetObjectIcon() => "Bone";

    private static mat4 BuildRotationMatrix(vec3 rot)
    {
        mat4 rx = mat4.RotateX(rot.x);
        mat4 ry = mat4.RotateY(rot.y);
        mat4 rz = mat4.RotateZ(rot.z);
        // Match SceneObject.GetLocalMatrix rotation convention: R = Rz * Ry * Rx.
        return rz * ry * rx;
    }

    private static vec3 MatrixToEulerRzRyRx(mat4 m)
    {
        // Decompose for R = Rz * Ry * Rx to keep parity with SceneObject/Gizmo.
        float yaw = MathF.Asin(-Math.Clamp(m.m02, -1f, 1f));
        float pitch;
        float roll;

        if (MathF.Abs(m.m02) < 0.9999f)
        {
            pitch = MathF.Atan2(m.m12, m.m22);
            roll = MathF.Atan2(m.m01, m.m00);
        }
        else
        {
            // Gimbal-lock fallback: preserve pitch, collapse roll to 0.
            pitch = MathF.Atan2(-m.m21, m.m11);
            roll = 0f;
        }

        return new vec3(pitch, yaw, roll);
    }
}
