#include "OrbitCamera.hpp"

#include <algorithm>
#include <cmath>

const osg::Vec3d OrbitCamera::DefaultTarget = osg::Vec3d(0.0, 0.0, 0.0);

osg::Vec3d OrbitCamera::OffsetFromTarget() const {
    const double cosP = std::cos(Pitch);
    return osg::Vec3d(
        cosP * std::sin(Yaw),
        std::sin(Pitch),
        cosP * std::cos(Yaw)) * Distance;
}

osg::Vec3d OrbitCamera::Position() const {
    return Target + OffsetFromTarget();
}

void OrbitCamera::Orbit(double deltaYaw, double deltaPitch) {
    Yaw -= deltaYaw;
    const double limit = 3.14159265358979323846 / 2.0 - 0.01;
    Pitch = std::clamp(Pitch - deltaPitch, -limit, limit);
}

void OrbitCamera::Look(double deltaYaw, double deltaPitch) {
    // Capture eye position before changing angles.
    const osg::Vec3d eye = Position();

    Yaw -= deltaYaw;
    const double limit = 3.14159265358979323846 / 2.0 - 0.01;
    Pitch = std::clamp(Pitch - deltaPitch, -limit, limit);

    // Recompute Target so the eye stays exactly where it was.
    Target = eye - OffsetFromTarget();
}

void OrbitCamera::MoveFreeFly(double forward, double right, double up) {
    // OffsetFromTarget() points FROM target TO eye (away from the scene);
    // negate it to get the look direction (into the scene).
    const double cosP = std::cos(Pitch);
    osg::Vec3d lookDir(
        cosP * std::sin(Yaw),
        std::sin(Pitch),
        cosP * std::cos(Yaw));
    lookDir.normalize();
    lookDir = -lookDir;

    // Right is perpendicular to the look direction on the XZ plane (no roll).
    osg::Vec3d rt = lookDir ^ osg::Vec3d(0.0, 1.0, 0.0);
    rt.normalize();

    // Translate eye and target together (rigid-body translation).
    const osg::Vec3d delta = lookDir * forward + rt * right + osg::Vec3d(0.0, 1.0, 0.0) * up;
    Target += delta;
}

void OrbitCamera::Pan(double deltaRight, double deltaUp) {
    const osg::Matrixd view = GetViewMatrix();
    // Because view = lookAt(eye, target, worldUp), the world-space right and up
    // are the first two rows of the rotation block of the view matrix.
    const osg::Vec3d right(view(0, 0), view(1, 0), view(2, 0));
    const osg::Vec3d up(view(0, 1), view(1, 1), view(2, 1));
    Target += right * deltaRight + up * deltaUp;
}

void OrbitCamera::Zoom(double delta) {
    Distance = std::max(0.1, Distance - delta);
}

void OrbitCamera::ResetToDefaultPose() {
    Target = DefaultTarget;
    Yaw = DefaultYaw;
    Pitch = DefaultPitch;
    Roll = 0.0;
    Distance = DefaultDistance;
}

osg::Matrixd OrbitCamera::GetViewMatrix() const {
    const osg::Vec3d position = Position();
    osg::Vec3d forward = Target - position;
    forward.normalize();

    osg::Vec3d right = forward ^ osg::Vec3d(0.0, 1.0, 0.0);
    if (right.length2() < 1e-8) {
        right = osg::Vec3d(1.0, 0.0, 0.0);
    } else {
        right.normalize();
    }

    osg::Vec3d up = right ^ forward;
    up.normalize();
    if (std::abs(Roll) > 1e-8) {
        up = up * std::cos(Roll) + right * std::sin(Roll);
        up.normalize();
    }

    osg::Matrixd view;
    view.makeLookAt(position, Target, up);
    return view;
}

osg::Matrixd OrbitCamera::GetProjectionMatrix(double aspectRatio) const {
    osg::Matrixd proj;
    proj.makePerspective(FovY * 180.0 / 3.14159265358979323846, aspectRatio, Near, Far);
    return proj;
}
