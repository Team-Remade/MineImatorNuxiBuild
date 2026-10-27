using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>
/// A user-placed camera in the scene.
/// Carries its own <see cref="Camera"/> with full fly-controls so it can be used
/// as a render viewpoint in the Camera Viewport panel.
/// </summary>
public class CameraSceneObject : SceneObject
{
    public float Fov  = 70f;
    public float Near = 0.05f;
    public float Far  = 4000f;
    public bool ColorManagementOverrideEnabled;
    public bool ColorManagementEnabled = true;
    public int ColorManagementToneMapper = 1;
    public float ColorManagementExposure;
    public float ColorManagementGamma = 2.2f;
    public int ColorManagementLook;

    public List<CameraEffect> Effects { get; } = new();

    private bool _active;

    /// <summary>
    /// When true this camera is the preferred render output camera.  Render
    /// exporters select active cameras first; if no active camera is visible
    /// the first visible spawned camera is used instead.  Setting this to
    /// <c>true</c> clears the <c>Active</c> flag on every other camera in the
    /// same scene.
    /// </summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            RefreshActiveMesh();
        }
    }

    /// <summary>
    /// Sets <paramref name="target"/> as the only active camera in its scene
    /// and deactivates every other <see cref="CameraSceneObject"/>.  Walks up
    /// to the top-level scene objects when the camera is nested under a parent.
    /// </summary>
    public static void SetActiveExclusive(CameraSceneObject target)
    {
        if (target == null) return;

        var sceneRoot = FindSceneRoot(target);
        if (sceneRoot == null)
        {
            target._active = true;
            target.RefreshActiveMesh();
            return;
        }

        ApplyActiveToScene(sceneRoot, target);
    }

    private static SceneObject? FindSceneRoot(SceneObject obj)
    {
        SceneObject current = obj;
        while (current.Parent != null)
        {
            current = current.Parent;
        }
        return current;
    }

    private static void ApplyActiveToScene(SceneObject node, CameraSceneObject target)
    {
        if (node is CameraSceneObject cam)
        {
            bool shouldBeActive = ReferenceEquals(cam, target);
            if (cam._active != shouldBeActive)
            {
                cam._active = shouldBeActive;
                cam.RefreshActiveMesh();
            }
        }
        foreach (var child in node.Children)
            ApplyActiveToScene(child, target);
    }

    /// <summary>
    /// The camera that drives this object's viewpoint.
    /// Its <see cref="Camera.Target"/> / <see cref="Camera.Yaw"/> / <see cref="Camera.Pitch"/>
    /// / <see cref="Camera.Distance"/> are kept in sync with the scene-object
    /// transform by <see cref="SyncCameraToTransform"/> and vice-versa.
    /// </summary>
    public Camera ViewCamera { get; } = new Camera();

    public void AddEffect(CameraEffectType type)
    {
        switch (type)
        {
            case CameraEffectType.CameraShake:
                Effects.Add(new CameraEffect
                {
                    Type = CameraEffectType.CameraShake,
                    Shake = new CameraShakeSettings()
                });
                break;
            case CameraEffectType.FilmGrain:
                Effects.Add(new CameraEffect
                {
                    Type = CameraEffectType.FilmGrain,
                    FilmGrain = new FilmGrainSettings()
                });
                break;
            case CameraEffectType.DepthOfField:
                Effects.Add(new CameraEffect { Type = CameraEffectType.DepthOfField });
                break;
            case CameraEffectType.Bloom:
                Effects.Add(new CameraEffect { Type = CameraEffectType.Bloom });
                break;
        }
    }

    public static CameraEffect CloneEffect(CameraEffect source)
    {
        return new CameraEffect
        {
            Type = source.Type,
            DepthOfField = source.DepthOfField.Clone(),
            Bloom = source.Bloom.Clone(),
            Shake = new CameraShakeSettings
            {
                Mode = source.Shake.Mode,
                Trauma = source.Shake.Trauma,
                Strength = source.Shake.Strength,
                Speed = source.Shake.Speed,
                Offset = source.Shake.Offset
            },
            FilmGrain = new FilmGrainSettings
            {
                Strength = source.FilmGrain.Strength,
                Saturation = source.FilmGrain.Saturation,
                Size = source.FilmGrain.Size
            }
        };
    }

    /// <summary>
    /// Visual meshes that should be displayed while this camera is inactive.
    /// Populated by the spawn menu when the <c>Camera.glb</c> mesh is loaded.
    /// Hidden when <see cref="Active"/> becomes <c>true</c>.
    /// </summary>
    public List<Mesh> InactiveVisuals { get; } = new();

    /// <summary>
    /// Visual meshes that should be displayed while this camera is active.
    /// Populated by the spawn menu when the <c>CameraActive.glb</c> mesh is
    /// loaded.  Hidden when <see cref="Active"/> becomes <c>false</c>.
    /// </summary>
    public List<Mesh> ActiveVisuals { get; } = new();

    /// <summary>
    /// Shows or hides <see cref="ActiveVisuals"/> / <see cref="InactiveVisuals"/>
    /// based on the current value of <see cref="Active"/>.  Call this once
    /// after both visual sets have been populated by the spawn pipeline.
    /// </summary>
    public void RefreshActiveMesh()
    {
        SetVisualsVisible(ActiveVisuals,    _active);
        SetVisualsVisible(InactiveVisuals, !_active);
    }

    private static void SetVisualsVisible(IEnumerable<Mesh> meshes, bool visible)
    {
        foreach (var mesh in meshes)
        {
            if (mesh == null) continue;
            // Use Alpha=0 to make the mesh invisible while keeping its other
            // properties (Unlit, DepthTestDisabled, PickOnly) intact.  The mesh
            // still participates in the pick pass for click targeting.
            mesh.Alpha = visible ? 1f : 0f;
        }
    }

    /// <summary>
    /// Toggles the <see cref="Active"/> flag and (when becoming active) clears
    /// the active flag on every other camera in the scene.  Returns the new
    /// value of <see cref="Active"/>.
    /// </summary>
    public bool ToggleActive()
    {
        if (_active)
        {
            Active = false;
            return false;
        }

        SetActiveExclusive(this);
        return true;
    }

    /// <summary>
    /// Synchronises <see cref="ViewCamera"/> with the evaluated world transform,
    /// including the existing eye displacement by <see cref="PivotOffset"/>.
    /// </summary>
    public void SyncCameraToTransform()
    {
        var world = GetWorldMatrixNoPivot();
        var rotation = CopyConstraintSolver.Rotation(world);
        var backward = new GlmSharp.vec3(rotation.m20, rotation.m21, rotation.m22);
        var up = new GlmSharp.vec3(rotation.m10, rotation.m11, rotation.m12);
        float horizontal = MathF.Sqrt(backward.x * backward.x + backward.z * backward.z);
        float yaw = horizontal > 1e-6f ? MathF.Atan2(backward.x, backward.z) : 0f;
        float pitch = MathF.Atan2(backward.y, horizontal);

        // Scene Euler Z is not camera roll. Use the same zero-roll frame as Camera.GetViewMatrix.
        var right = horizontal >= 1e-4f
            ? new GlmSharp.vec3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw))
            : GlmSharp.vec3.UnitX;
        var baseUp = GlmSharp.vec3.Cross(right, -backward).Normalized;

        ViewCamera.Yaw      = yaw;
        ViewCamera.Pitch    = pitch;
        ViewCamera.Roll     = MathF.Atan2(GlmSharp.vec3.Dot(up, right), GlmSharp.vec3.Dot(up, baseUp));
        ViewCamera.Distance = 0.001f; // near-zero so eye ≈ Position

        // eye = Target + OffsetFromTarget()  →  Target = eye − OffsetFromTarget()
        // OffsetFromTarget() = (cosP·sinY, sinP, cosP·cosY) * Distance
        float cosP = MathF.Cos(pitch);
        var offset = new GlmSharp.vec3(
            cosP * MathF.Sin(yaw),
            MathF.Sin(pitch),
            cosP * MathF.Cos(yaw)) * ViewCamera.Distance;

        ViewCamera.Target = new GlmSharp.vec3(world.m30, world.m31, world.m32) - offset + PivotOffset;
    }

    /// <summary>
    /// Applies a look direction directly to the camera view state and keeps the
    /// scene-object transform in sync. This is used for gizmo-based rotation so
    /// camera yaw/pitch are updated from a stable forward vector instead of being
    /// reinterpreted through a generic 3-axis Euler conversion.
    /// </summary>
    public void ApplyLookDirection(GlmSharp.vec3 forward)
    {
        forward = forward.Normalized;

        float pitch = MathF.Asin(Math.Clamp(forward.y, -1f, 1f));
        float yaw   = MathF.Atan2(forward.x, forward.z);

        ViewCamera.Yaw   = yaw;
        ViewCamera.Pitch = pitch;
        ViewCamera.Distance = 0.001f;

        float cosP = MathF.Cos(pitch);
        var offset = new GlmSharp.vec3(
            cosP * MathF.Sin(yaw),
            MathF.Sin(pitch),
            cosP * MathF.Cos(yaw)) * ViewCamera.Distance;

        ViewCamera.Target = Position - offset + PivotOffset;
        SyncTransformFromCamera();
    }

    /// <summary>Writes viewport navigation back into the local scene transform.</summary>
    public void SyncTransformFromCamera()
    {
        var position = ViewCamera.Position - PivotOffset;
        if (InheritPosition && Parent != null)
        {
            var local = GetParentWorldTransform().Inverse * new GlmSharp.vec4(position, 1f);
            position = new GlmSharp.vec3(local.x, local.y, local.z);
        }
        SetLocalPosition(position);

        var world = GetWorldRotationEditFrame(out var prefix, out _, out var suffix);
        if (MathF.Abs(prefix.Determinant) <= 1e-8f || MathF.Abs(suffix.Determinant) <= 1e-8f)
            return;

        var desiredRotation = CopyConstraintSolver.Rotation(ViewCamera.GetViewMatrix().Transposed);
        // Rotate the existing basis to retain its scale before removing the parent/edit frame.
        var desiredBasis = desiredRotation * CopyConstraintSolver.Rotation(world).Transposed * world;
        var localRotation = CopyConstraintSolver.Rotation(prefix.Inverse * desiredBasis * suffix.Inverse);
        SetLocalRotation(MineImatorSimplyRemade.core.mdl.mineImator.MineImatorLoader.MatrixToEulerRzRyRx(localRotation));
    }
}
