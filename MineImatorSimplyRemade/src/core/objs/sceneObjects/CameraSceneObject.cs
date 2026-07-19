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
    /// Synchronises <see cref="ViewCamera"/> so that its eye position matches
    /// <see cref="SceneObject.Position"/> and it looks in the direction encoded
    /// by <see cref="SceneObject.Rotation"/> (Euler XYZ in radians).
    ///
    /// The camera's target includes the <see cref="PivotOffset"/> so that the camera
    /// looks at the pivot point rather than the origin position.
    ///
    /// Sign mapping (derived from matching Camera.OffsetFromTarget against
    /// the mesh look-direction produced by GetLocalMatrix = rz*ry*rx):
    ///   ViewCamera.Yaw   =  Rotation.y   (same sign — both rotate around +Y)
    ///   ViewCamera.Pitch = -Rotation.x   (negated — RotateX pitches opposite to
    ///                                     the camera's OffsetFromTarget Y term)
    /// </summary>
    public void SyncCameraToTransform()
    {
        float yaw   =  Rotation.y;
        float pitch = -Rotation.x; // negated: mesh RotateX and camera pitch are opposite sign

        ViewCamera.Yaw      = yaw;
        ViewCamera.Pitch    = pitch;
        ViewCamera.Distance = 0.001f; // near-zero so eye ≈ Position

        // eye = Target + OffsetFromTarget()  →  Target = eye − OffsetFromTarget()
        // OffsetFromTarget() = (cosP·sinY, sinP, cosP·cosY) * Distance
        float cosP = MathF.Cos(pitch);
        var offset = new GlmSharp.vec3(
            cosP * MathF.Sin(yaw),
            MathF.Sin(pitch),
            cosP * MathF.Cos(yaw)) * ViewCamera.Distance;

        ViewCamera.Target = Position - offset + PivotOffset;
    }

    /// <summary>
    /// Reads <see cref="ViewCamera"/>'s current eye position and look direction
    /// back into the scene-object transform (<see cref="SceneObject.Position"/>
    /// and <see cref="SceneObject.Rotation"/>).
    /// Call this after the camera viewport moves the fly camera.
    /// </summary>
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

    public void SyncTransformFromCamera()
    {
        SetLocalPosition(ViewCamera.Position);

        // Inverse of SyncCameraToTransform:
        //   Rotation.y =  ViewCamera.Yaw
        //   Rotation.x = -ViewCamera.Pitch  (pitch sign flipped back)
        SetLocalRotation(new GlmSharp.vec3(-ViewCamera.Pitch, ViewCamera.Yaw, 0f));
    }
}
