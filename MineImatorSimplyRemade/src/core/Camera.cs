using GlmSharp;

namespace MineImatorSimplyRemade.core;

/// <summary>
/// Perspective camera with orbit (turntable) controls.
///
/// The camera orbits a <see cref="Target"/> point in world space.
/// <see cref="Yaw"/> rotates around the world Y axis;
/// <see cref="Pitch"/> tilts up/down (clamped to ±89°).
/// <see cref="Distance"/> controls how far the camera sits from the target.
///
/// Call <see cref="Orbit"/>, <see cref="Pan"/>, or <see cref="Zoom"/> each frame
/// from viewport input, then call <see cref="GetViewMatrix"/> and
/// <see cref="GetProjectionMatrix"/> to obtain matrices ready to pass to shaders.
/// </summary>
public class Camera
{
    // ── Orbit state ───────────────────────────────────────────────────────────

    /// <summary>World-space point the camera orbits around.</summary>
    public vec3 Target = vec3.Zero;

    /// <summary>Horizontal rotation angle in radians.</summary>
    public float Yaw = 0.5f;

    /// <summary>Vertical tilt angle in radians (clamped to ±89°).</summary>
    public float Pitch = 0.4f;

    /// <summary>Distance from <see cref="Target"/> to the camera eye.</summary>
    public float Distance = 5f;

    // ── Projection state ──────────────────────────────────────────────────────

    /// <summary>Vertical field of view in radians.</summary>
    public float FovY = glm.Radians(60f);

    /// <summary>Near clip plane distance.</summary>
    public float Near = 0.1f;

    /// <summary>Far clip plane distance.</summary>
    public float Far = 1000f;

    // ── Derived position ──────────────────────────────────────────────────────

    /// <summary>Current world-space eye position (recomputed from orbit parameters).</summary>
    public vec3 Position => Target + OffsetFromTarget();

    private vec3 OffsetFromTarget()
    {
        float cosP = MathF.Cos(Pitch);
        return new vec3(
            cosP * MathF.Sin(Yaw),
            MathF.Sin(Pitch),
            cosP * MathF.Cos(Yaw)) * Distance;
    }

    // ── Input helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Orbits the camera around the target by the given delta angles (in radians).
    /// Pitch is clamped so the camera never flips past vertical.
    /// </summary>
    public void Orbit(float deltaYaw, float deltaPitch)
    {
        Yaw   -= deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -MathF.PI / 2f + 0.01f, MathF.PI / 2f - 0.01f);
    }

    /// <summary>
    /// Pans the target point in the camera's local right/up plane.
    /// <paramref name="deltaRight"/> and <paramref name="deltaUp"/> are world-unit amounts.
    /// </summary>
    public void Pan(float deltaRight, float deltaUp)
    {
        var view = GetViewMatrix();
        vec3 right = new vec3(view.m00, view.m10, view.m20);   // view row 0 (transposed)
        vec3 up    = new vec3(view.m01, view.m11, view.m21);   // view row 1 (transposed)
        // Because view = lookAt(eye, target, worldUp), the world-space right and up
        // are stored in the first two columns of the transpose of the rotation block,
        // which equals the first two rows of the view matrix.
        Target += right * deltaRight + up * deltaUp;
    }

    /// <summary>Adjusts the orbit distance (positive delta = zoom in).</summary>
    public void Zoom(float delta)
    {
        Distance = Math.Max(0.1f, Distance - delta);
    }

    // ── Matrix getters ────────────────────────────────────────────────────────

    /// <summary>Returns a right-handed look-at view matrix.</summary>
    public mat4 GetViewMatrix()
    {
        return mat4.LookAt(Position, Target, vec3.UnitY);
    }

    /// <summary>
    /// Returns a perspective projection matrix.
    /// <paramref name="aspectRatio"/> = width / height of the viewport.
    /// </summary>
    public mat4 GetProjectionMatrix(float aspectRatio)
    {
        return mat4.Perspective(FovY, aspectRatio, Near, Far);
    }
}
