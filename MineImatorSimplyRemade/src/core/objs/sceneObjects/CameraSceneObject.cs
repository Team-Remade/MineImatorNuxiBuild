using MineImatorSimplyRemade.core;

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

    /// <summary>
    /// The camera that drives this object's viewpoint.
    /// Its <see cref="Camera.Target"/> / <see cref="Camera.Yaw"/> / <see cref="Camera.Pitch"/>
    /// / <see cref="Camera.Distance"/> are kept in sync with the scene-object
    /// transform by <see cref="SyncCameraToTransform"/> and vice-versa.
    /// </summary>
    public Camera ViewCamera { get; } = new Camera();

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
