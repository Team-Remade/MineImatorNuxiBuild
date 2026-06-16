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
    /// Rotates the camera in place (FPS-style look).
    /// The eye position stays fixed; <see cref="Target"/> is repositioned
    /// <see cref="Distance"/> units ahead of the eye in the new look direction.
    /// Pitch is clamped so the camera never flips past vertical.
    /// </summary>
    public void Look(float deltaYaw, float deltaPitch)
    {
        // Capture eye position before changing angles.
        vec3 eye = Position;

        Yaw   -= deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -MathF.PI / 2f + 0.01f, MathF.PI / 2f - 0.01f);

        // Recompute Target so the eye stays exactly where it was.
        // OffsetFromTarget() now uses the new Yaw/Pitch and points away from
        // the target, so Target = eye - newOffset.
        Target = eye - OffsetFromTarget();
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

    /// <summary>
    /// Translates the camera in first-person (free-fly) mode.
    /// Both the eye position and the orbit <see cref="Target"/> are moved together
    /// so the orbit pivot stays in front of the camera.
    /// <para>
    /// <paramref name="forward"/> is the world-unit distance to move along the
    /// camera's look direction (positive = forward into the scene).
    /// <paramref name="right"/> is the world-unit distance to strafe
    /// (positive = right).
    /// <paramref name="up"/> is the world-unit distance to move vertically
    /// along world Y (positive = up).
    /// </para>
    /// </summary>
    public void MoveFreeFly(float forward, float right, float up)
    {
        // OffsetFromTarget() points FROM target TO eye (i.e. away from the scene).
        // Negate it to get the camera's look direction (into the scene).
        float cosP = MathF.Cos(Pitch);
        vec3 lookDir = -new vec3(
            cosP * MathF.Sin(Yaw),
            MathF.Sin(Pitch),
            cosP * MathF.Cos(Yaw)).Normalized;

        // Right is perpendicular to the look direction on the XZ plane (no roll).
        vec3 rt = vec3.Cross(lookDir, vec3.UnitY).Normalized;

        // Translate both eye and target together (rigid body translation).
        vec3 delta = lookDir * forward + rt * right + vec3.UnitY * up;
        Target += delta;
        // Position is derived from Target + OffsetFromTarget(), so moving
        // Target moves the eye implicitly.
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

    // ── Ray casting / unprojection ─────────────────────────────────────────────

    /// <summary>
    /// Returns the world-space ray origin for a given screen pixel, assuming a
    /// perspective camera.  For perspective projection this is simply the eye position.
    /// <paramref name="screenPos"/> is an absolute screen coordinate.
    /// <paramref name="imageMin"/> and <paramref name="imageSize"/> describe the
    /// rectangle of the rendered viewport image in screen space.
    /// </summary>
    public vec3 ProjectRayOrigin(System.Numerics.Vector2 screenPos,
                                  System.Numerics.Vector2 imageMin,
                                  System.Numerics.Vector2 imageSize)
    {
        return Position; // perspective: ray always originates at the eye
    }

    /// <summary>
    /// Returns the world-space ray direction for a given screen pixel.
    /// The returned vector is normalised.
    /// </summary>
    public vec3 ProjectRayNormal(System.Numerics.Vector2 screenPos,
                                  System.Numerics.Vector2 imageMin,
                                  System.Numerics.Vector2 imageSize)
    {
        if (imageSize.X < 1 || imageSize.Y < 1) return new vec3(0, 0, -1);

        // Map screen position to NDC [-1, 1] (Y flipped: screen-Y increases down).
        float ndcX =  (screenPos.X - imageMin.X) / imageSize.X * 2f - 1f;
        float ndcY = -((screenPos.Y - imageMin.Y) / imageSize.Y * 2f - 1f);

        float aspect = imageSize.X / imageSize.Y;
        float tanHalfFov = MathF.Tan(FovY * 0.5f);

        // Ray in view space (looking along –Z in right-handed OpenGL convention).
        vec3 rayView = new vec3(ndcX * aspect * tanHalfFov,
                                 ndcY * tanHalfFov,
                                -1f).Normalized;

        // Transform to world space using the inverse view matrix.
        mat4 viewInv = GetViewMatrix().Inverse;
        // Rotate only (ignore translation) by treating as direction.
        vec4 rayWorld = viewInv * new vec4(rayView, 0f);
        return new vec3(rayWorld.x, rayWorld.y, rayWorld.z).Normalized;
    }

    /// <summary>
    /// Projects a world-space position to a 2D screen coordinate within the
    /// image rectangle defined by <paramref name="imageMin"/> / <paramref name="imageSize"/>.
    /// </summary>
    public System.Numerics.Vector2 UnprojectPosition(vec3 worldPos,
                                                      System.Numerics.Vector2 imageMin,
                                                      System.Numerics.Vector2 imageSize)
    {
        if (imageSize.X < 1 || imageSize.Y < 1) return imageMin;

        float aspect = imageSize.X / imageSize.Y;
        mat4 view = GetViewMatrix();
        mat4 proj = GetProjectionMatrix(aspect);

        // Clip space
        vec4 clip = proj * view * new vec4(worldPos, 1f);
        if (MathF.Abs(clip.w) < 1e-7f) return imageMin;

        // NDC
        float ndcX =  clip.x / clip.w;
        float ndcY = -clip.y / clip.w; // flip Y back to screen convention

        float screenX = imageMin.X + (ndcX * 0.5f + 0.5f) * imageSize.X;
        float screenY = imageMin.Y + (ndcY * 0.5f + 0.5f) * imageSize.Y;
        return new System.Numerics.Vector2(screenX, screenY);
    }
}
