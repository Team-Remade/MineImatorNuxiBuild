using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.render;
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
    public vec3?     PartColorBlend;
    public float?    PartColorBlendAmount;
    public float?    PartColorAlpha;
    public float     PartDepth;
    public int[]?    TextureSize;
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
        get => new(
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

    /// <summary>Multiplicative blend colour loaded from color_blend.</summary>
    public vec3? ColorBlend { get; set; }

    /// <summary>Blend weight loaded from color_mix_percent (0-1, defaults to 1).</summary>
    public float? ColorBlendAmount { get; set; }

    /// <summary>Render priority depth from model's depth property.</summary>
    public float Depth { get; set; }

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>Stores the bend parameters for this bone (called by the loader).</summary>
    public void SetBendParameters(BendParams? bendParams, float lockBend)
    {
        BendParameters = bendParams;
        LockBend = Math.Clamp(lockBend, 0f, 1f);
        // SceneObject.GetWorldMatrix reads the inherited field. Keep it in sync
        // with the Mine-imator-facing property; otherwise lock_bend=0 is ignored.
        base.LockBend = LockBend;
    }

    public void SetLockBend(float lockBend)
    {
        LockBend = Math.Clamp(lockBend, 0f, 1f);
        base.LockBend = LockBend;
    }

    /// <summary>
    /// Returns this bone's bend with inherited default/current angles applied.
    /// This is also used during initial import, before any UI edit has caused a
    /// mesh regeneration.
    /// </summary>
    public BendParams? GetEffectiveBendParameters()
    {
        if (!BendParameters.HasValue) return null;
        var result = BendParameters.Value;
        result.Angle = GetEffectiveBendAngle();
        return result;
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
        return BendHelper.GetPartBendMatrix(bendParams, bendVector);
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
    public void OverrideTexture(uint textureId, uint onlyIfTextureId = 0)
    {
        // Update live meshes
        foreach (var mesh in Visuals.Where(mesh => mesh.TextureId != 0))
        {
            if (onlyIfTextureId != 0 && mesh.TextureId != onlyIfTextureId)
                continue;
            mesh.TextureId = textureId;
            mesh.AlbedoTexture = MineImatorLoader.ResolveVeldridTexture(textureId);
        }

        // Update stored shape data so the override survives RegenerateMeshes()
        foreach (var sd in _shapeDataList.Where(sd => sd.TextureId != 0))
        {
            if (onlyIfTextureId != 0 && sd.TextureId != onlyIfTextureId)
                continue;
            sd.TextureId = textureId;
        }
    }

    /// <summary>Updates the bend angle and regenerates all shape meshes.</summary>
    public void SetBendAngle(vec3 newAngle)
    {
        if (!BendParameters.HasValue) return;

        var bp = BendParameters.Value;
        // Never allow bend on axes disabled by the model's bend definition.
        if (!bp.AxisX) newAngle.x = 0f;
        if (!bp.AxisY) newAngle.y = 0f;
        if (!bp.AxisZ) newAngle.z = 0f;

        newAngle.x = ClampStoredAxis(newAngle.x, bp.DirectionMin.x, bp.DirectionMax.x, bp.InvertX, bp.AxisX);
        newAngle.y = ClampStoredAxis(newAngle.y, bp.DirectionMin.y, bp.DirectionMax.y, bp.InvertY, bp.AxisY);
        newAngle.z = ClampStoredAxis(newAngle.z, bp.DirectionMin.z, bp.DirectionMax.z, bp.InvertZ, bp.AxisZ);
        bp.Angle   = newAngle;
        BendParameters = bp;

        RegenerateMeshes();
    }

    /// <summary>
    /// Returns bend angles in authored/editable space (invert flags removed).
    /// Use this for UI and gizmo editing so invert behaves like Mine-imator.
    /// </summary>
    public vec3 GetEditableBendAngle()
    {
        if (!BendParameters.HasValue) return vec3.Zero;

        var bp = BendParameters.Value;
        return bp.InheritBend
            ? GetEffectiveEditableBendAngle()
            : StoredToEditable(bp.Angle, bp);
    }

    /// <summary>
    /// Sets bend angles in authored/editable space (pre-invert), then stores
    /// them in the internal effective space used by mesh generation.
    /// </summary>
    public void SetEditableBendAngle(vec3 editableAngle)
    {
        if (!BendParameters.HasValue) return;

        var bp = BendParameters.Value;

        if (!bp.AxisX) editableAngle.x = 0f;
        if (!bp.AxisY) editableAngle.y = 0f;
        if (!bp.AxisZ) editableAngle.z = 0f;

        editableAngle.x = Math.Clamp(editableAngle.x, bp.DirectionMin.x, bp.DirectionMax.x);
        editableAngle.y = Math.Clamp(editableAngle.y, bp.DirectionMin.y, bp.DirectionMax.y);
        editableAngle.z = Math.Clamp(editableAngle.z, bp.DirectionMin.z, bp.DirectionMax.z);

        vec3 localEditable = editableAngle;
        if (bp.InheritBend && Parent is MiBoneSceneObject parentBone && parentBone.BendParameters.HasValue)
            localEditable -= parentBone.GetEffectiveEditableBendAngle();

        vec3 stored = EditableToStored(localEditable, bp);

        SetBendAngle(stored);
    }

    /// <summary>Updates stored shape data and implicit bend widths for a model style change.</summary>
    public void SetModelBendStyle(BendStyle oldStyle, BendStyle newStyle)
    {
        foreach (var shapeData in _shapeDataList)
            shapeData.ModelBendStyle = newStyle;

        if (!BendParameters.HasValue || BendParameters.Value.ExplicitBendSize || oldStyle == newStyle)
            return;

        var bend = BendParameters.Value;
        // Modelbench leaves an omitted bend_size null and resolves it from the
        // current viewport style on every mesh generation. BendParams stores
        // the resolved value, so replace it directly instead of scaling it as
        // though it were a custom/authored size.
        bend.BendSize = newStyle == BendStyle.Blocky ? 1f : 4f;
        BendParameters = bend;
    }

    /// <summary>
    /// Rebuilds all mesh instances for this bone using the current bend angle.
    /// Also triggers regeneration on child bones whose InheritBend is true.
    /// </summary>
    public void RegenerateMeshes(bool propagateInheritedBends = true)
    {
        if (_shapeDataList.Count > 0)
        {
            Visuals.Clear();

            BendParams? effectiveBendParams = GetEffectiveBendParameters();

            var loader = MineImatorLoader.Instance;
            foreach (var mesh in _shapeDataList.Select(sd => loader.CreateShapeMeshPublic(
                         sd.PartName, sd.ShapeIndex, sd.Shape, sd.Model,
                         sd.TextureId, sd.AccumulatedScale, effectiveBendParams,
                         sd.ModelBendStyle, sd.PartColorBlend, sd.PartColorBlendAmount,
                         sd.PartColorAlpha, sd.PartDepth,
                         sd.TextureSize)).OfType<VeldridMesh>())
            {
                AddMesh(mesh);
            }
        }

        // Propagate to inheriting children
        if (!propagateInheritedBends)
            return;

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
        var bp = BendParameters.Value;
        vec3 editable = GetEffectiveEditableBendAngle();
        return EditableToStored(editable, bp);
    }

    private vec3 GetEffectiveEditableBendAngle()
    {
        if (!BendParameters.HasValue) return vec3.Zero;

        var bp = BendParameters.Value;
        vec3 editable = StoredToEditable(bp.Angle, bp);

        if (bp.InheritBend && Parent is MiBoneSceneObject parentBone && parentBone.BendParameters.HasValue)
            editable += parentBone.GetEffectiveEditableBendAngle();

        if (!bp.AxisX) editable.x = 0f;
        if (!bp.AxisY) editable.y = 0f;
        if (!bp.AxisZ) editable.z = 0f;

        editable.x = bp.AxisX ? Math.Clamp(editable.x, bp.DirectionMin.x, bp.DirectionMax.x) : 0f;
        editable.y = bp.AxisY ? Math.Clamp(editable.y, bp.DirectionMin.y, bp.DirectionMax.y) : 0f;
        editable.z = bp.AxisZ ? Math.Clamp(editable.z, bp.DirectionMin.z, bp.DirectionMax.z) : 0f;

        return editable;
    }

    private static vec3 StoredToEditable(vec3 stored, BendParams bp)
    {
        if (bp.InvertX) stored.x *= -1f;
        if (bp.InvertY) stored.y *= -1f;
        if (bp.InvertZ) stored.z *= -1f;
        return stored;
    }

    private static vec3 EditableToStored(vec3 editable, BendParams bp)
    {
        if (bp.InvertX) editable.x *= -1f;
        if (bp.InvertY) editable.y *= -1f;
        if (bp.InvertZ) editable.z *= -1f;
        return editable;
    }

    private static float ClampStoredAxis(float value, float dirMin, float dirMax, bool invert, bool axisEnabled)
    {
        if (!axisEnabled) return 0f;

        if (!invert)
            return Math.Clamp(value, dirMin, dirMax);

        // Direction limits are authored in pre-invert space.
        // Stored/internal values are post-invert, so their range is mirrored.
        float minMirrored = -dirMax;
        float maxMirrored = -dirMin;
        float low = Math.Min(minMirrored, maxMirrored);
        float high = Math.Max(minMirrored, maxMirrored);
        return Math.Clamp(value, low, high);
    }

    // ── Inheritance helpers ───────────────────────────────────────────────────

    public void InheritColorAlphaFromParent()
    {
        if (ColorAlpha.HasValue) return;
        if (Parent is MiBoneSceneObject parentBone)
            ColorAlpha = parentBone.ColorAlpha;
    }

    public void InheritColorBlendFromParent()
    {
        if (ColorBlend.HasValue) return;
        if (Parent is MiBoneSceneObject parentBone)
            ColorBlend = parentBone.ColorBlend;
    }

    public void InheritColorBlendAmountFromParent()
    {
        if (ColorBlendAmount.HasValue) return;
        if (Parent is MiBoneSceneObject parentBone)
            ColorBlendAmount = parentBone.ColorBlendAmount;
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

        // Keep this threshold in sync with GizmoMath.MatrixToEulerYXZ (Gizmo3D.cs): a loose
        // threshold here forces roll = 0 well before the true pole, which is only an exact
        // reconstruction of the matrix at cos(yaw) == 0 — everywhere else it introduces real
        // rotation error. Restrict the fallback to the true singularity.
        const float PoleEpsilon = 1e-6f;
        if (MathF.Abs(m.m02) < 1f - PoleEpsilon)
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
