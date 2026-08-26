#pragma once

#include <osg/Vec3d>
#include <osg/Matrixd>

// Perspective camera with orbit (turntable) controls.
//
// Ported from the reference project's core/Camera.cs. The camera orbits a
// Target point in world space. Yaw rotates around the world Y axis; Pitch tilts
// up/down (clamped to +/-89 degrees). Distance controls how far the camera sits
// from the target.
//
// Call Orbit(), Pan(), or Zoom() from viewport input, then use GetViewMatrix()
// / GetProjectionMatrix() to obtain matrices for rendering.
class OrbitCamera {
public:
    static const osg::Vec3d DefaultTarget;
    static constexpr double DefaultYaw = 0.5;
    static constexpr double DefaultPitch = 0.4;
    static constexpr double DefaultDistance = 5.0;

    // Orbit state.
    osg::Vec3d Target = DefaultTarget;
    double Yaw = DefaultYaw;      // horizontal rotation, radians
    double Pitch = DefaultPitch;  // vertical tilt, radians (clamped to +/-89 deg)
    double Roll = 0.0;            // rotation around viewing axis, radians
    double Distance = DefaultDistance;

    // Projection state.
    double FovY = 60.0 * 3.14159265358979323846 / 180.0; // radians
    double Near = 0.1;
    double Far = 1000.0;

    // Current world-space eye position (derived from orbit parameters).
    osg::Vec3d Position() const;

    // Orbits the camera around the target by the given delta angles (radians).
    // Pitch is clamped so the camera never flips past vertical.
    void Orbit(double deltaYaw, double deltaPitch);

    // Rotates the camera in place (FPS-style look). The eye position stays
    // fixed; Target is repositioned Distance units ahead of the eye in the new
    // look direction. Pitch is clamped so the camera never flips past vertical.
    void Look(double deltaYaw, double deltaPitch);

    // Translates the camera in first-person (free-fly) mode. Both the eye and
    // the orbit Target move together so the pivot stays in front of the camera.
    // forward = move along look direction, right = strafe, up = world-Y move.
    void MoveFreeFly(double forward, double right, double up);

    // Pans the target point in the camera's local right/up plane (world units).
    void Pan(double deltaRight, double deltaUp);

    // Adjusts the orbit distance (positive delta = zoom in).
    void Zoom(double delta);

    // Restores the camera to its default work-camera spawn pose.
    void ResetToDefaultPose();

    // Right-handed look-at view matrix.
    osg::Matrixd GetViewMatrix() const;

    // Perspective projection matrix (aspectRatio = width / height).
    osg::Matrixd GetProjectionMatrix(double aspectRatio) const;

private:
    osg::Vec3d OffsetFromTarget() const;
};
