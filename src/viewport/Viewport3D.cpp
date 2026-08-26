#include "Viewport3D.hpp"

#include <algorithm>
#include <cmath>

#include <glad/glad.h>
#include <osg/Camera>
#include <osg/Geode>
#include <osg/MatrixTransform>
#include <osg/ShapeDrawable>
#include <osgViewer/GraphicsWindow>
#include <osgViewer/Viewer>

namespace {
constexpr float viewportWidthRatio = 0.75f;
constexpr float viewportHeightRatio = 0.70f;
}

Viewport3D::Viewport3D(int menuBarHeight) : menuBarHeight(menuBarHeight) {
}

Viewport3D::~Viewport3D() = default;

int Viewport3D::GetSceneX(int windowWidth) const {
    return 0;
}

int Viewport3D::GetSceneY(int windowHeight) const {
    // GL viewport Y is bottom-origin; the visible 3-D canvas occupies the
    // viewport panel below its header, i.e. from (menuBarHeight +
    // panelHeaderHeight) to that plus GetSceneHeight(), measured from the top.
    const int topY = menuBarHeight + panelHeaderHeight;
    const int glY = windowHeight - topY - GetSceneHeight(windowHeight);
    return glY > 0 ? glY : 0;
}

int Viewport3D::GetSceneWidth(int windowWidth) const {
    const int sceneWidth = static_cast<int>(static_cast<float>(windowWidth) * viewportWidthRatio);
    return sceneWidth > 0 ? sceneWidth : 1;
}

int Viewport3D::GetSceneHeight(int windowHeight) const {
    const int workspaceHeight = windowHeight - menuBarHeight;
    const int panelHeight = static_cast<int>(static_cast<float>(workspaceHeight) * viewportHeightRatio);
    const int sceneHeight = panelHeight - panelHeaderHeight;
    return sceneHeight > 0 ? sceneHeight : 1;
}

void Viewport3D::Init(int width, int windowHeight) {
    currentWidth = width;
    currentHeight = windowHeight;

    osg::ref_ptr<osg::Box> box = new osg::Box(osg::Vec3(0.0f, 0.0f, 0.0f), 1.0f);
    osg::ref_ptr<osg::ShapeDrawable> cubeDrawable = new osg::ShapeDrawable(box);
    cubeDrawable->setColor(osg::Vec4(0.2f, 0.7f, 1.0f, 1.0f));

    osg::ref_ptr<osg::Geode> geode = new osg::Geode();
    geode->addDrawable(cubeDrawable);

    osg::ref_ptr<osg::MatrixTransform> root = new osg::MatrixTransform();
    root->addChild(geode);

    const int sceneX = GetSceneX(width);
    const int sceneY = GetSceneY(windowHeight);
    const int sceneWidth = GetSceneWidth(width);
    const int sceneHeight = GetSceneHeight(windowHeight);
    graphicsWindow = new osgViewer::GraphicsWindowEmbedded(sceneX, sceneY, sceneWidth, sceneHeight);

    viewer = new osgViewer::Viewer();
    viewer->setThreadingModel(osgViewer::Viewer::SingleThreaded);
    viewer->setSceneData(root);
    viewer->setCameraManipulator(nullptr);
    viewer->setReleaseContextAtEndOfFrameHint(false);

    osg::Camera* camera = viewer->getCamera();
    camera->setGraphicsContext(graphicsWindow);
    camera->setViewport(new osg::Viewport(sceneX, sceneY, sceneWidth, sceneHeight));
    camera->setDrawBuffer(GL_BACK);
    camera->setReadBuffer(GL_BACK);
    ApplyCamera();
}

void Viewport3D::Resize(int width, int windowHeight) {
    currentWidth = width;
    currentHeight = windowHeight;
    const int sceneX = GetSceneX(width);
    const int sceneY = GetSceneY(windowHeight);
    const int sceneWidth = GetSceneWidth(width);
    const int sceneHeight = GetSceneHeight(windowHeight);

    if (graphicsWindow != nullptr) {
        graphicsWindow->resized(sceneX, sceneY, sceneWidth, sceneHeight);
        graphicsWindow->getEventQueue()->windowResize(sceneX, sceneY, sceneWidth, sceneHeight);
    }

    if (viewer != nullptr) {
        osg::Camera* camera = viewer->getCamera();
        if (camera != nullptr) {
            camera->setViewport(sceneX, sceneY, sceneWidth, sceneHeight);
        }
    }
    ApplyCamera();
}

void Viewport3D::RenderFrame() {
    if (viewer != nullptr) {
        viewer->frame();
    }
}

void Viewport3D::ApplyCamera() {
    if (viewer == nullptr) {
        return;
    }
    osg::Camera* cam = viewer->getCamera();
    if (cam == nullptr) {
        return;
    }
    const int sceneWidth = GetSceneWidth(currentWidth);
    const int sceneHeight = GetSceneHeight(currentHeight);
    const double aspect = static_cast<double>(sceneWidth) / static_cast<double>(sceneHeight);
    cam->setViewMatrix(camera.GetViewMatrix());
    cam->setProjectionMatrix(camera.GetProjectionMatrix(aspect));
}

bool Viewport3D::IsInsideScene(int mouseX, int mouseY) const {
    const int sceneWidth = GetSceneWidth(currentWidth);
    const int sceneHeight = GetSceneHeight(currentHeight);
    const int topY = menuBarHeight + panelHeaderHeight;
    return mouseX >= 0 && mouseX < sceneWidth &&
           mouseY >= topY && mouseY < topY + sceneHeight;
}

bool Viewport3D::HandleMouseButton(unsigned char button, bool down, int mouseX, int mouseY) {
    if (button == 1) { // left
        if (down) {
            if (!IsInsideScene(mouseX, mouseY)) {
                return false;
            }
            orbiting = true;
        } else {
            orbiting = false;
        }
        return down;
    }
    if (button == 2) { // middle
        if (down) {
            if (!IsInsideScene(mouseX, mouseY)) {
                return false;
            }
            panning = true;
        } else {
            panning = false;
        }
        return down;
    }
    if (button == 3) { // right -> free-fly
        if (down) {
            if (!IsInsideScene(mouseX, mouseY)) {
                return false;
            }
            freeFly = true;
        } else {
            freeFly = false;
        }
        return down;
    }
    return false;
}

bool Viewport3D::HandleMouseMotion(int mouseX, int mouseY, int deltaX, int deltaY) {
    if (freeFly) {
        constexpr double lookSensitivity = 0.003;
        camera.Look(deltaX * lookSensitivity, -deltaY * lookSensitivity);
        ApplyCamera();
        return true;
    }
    if (orbiting) {
        constexpr double orbitSpeed = 0.01;
        camera.Orbit(deltaX * orbitSpeed, -deltaY * orbitSpeed);
        ApplyCamera();
        return true;
    }
    if (panning) {
        const double panSpeed = camera.Distance * 0.0025;
        camera.Pan(-deltaX * panSpeed, deltaY * panSpeed);
        ApplyCamera();
        return true;
    }
    return false;
}

bool Viewport3D::HandleMouseWheel(int mouseX, int mouseY, int wheelY) {
    if (freeFly) {
        // During free-fly the wheel adjusts movement speed instead of zooming.
        const double factor = wheelY > 0 ? 1.3 : (1.0 / 1.3);
        for (int i = 0; i < std::abs(wheelY); ++i) {
            freeFlySpeed *= factor;
        }
        freeFlySpeed = std::clamp(freeFlySpeed, 0.1, 500.0);
        return true;
    }
    if (!IsInsideScene(mouseX, mouseY)) {
        return false;
    }
    camera.Zoom(wheelY * camera.Distance * 0.1);
    ApplyCamera();
    return true;
}

void Viewport3D::UpdateFreeFly(double deltaTime,
                               bool moveForward, bool moveBack, bool moveLeft, bool moveRight,
                               bool moveUp, bool moveDown, bool fast, bool slow) {
    if (!freeFly) {
        return;
    }
    // First-person scene cameras may use near-zero Distance; use a stable
    // baseline so movement speed stays sane in that case.
    const double distanceForSpeed = camera.Distance < 0.01 ? OrbitCamera::DefaultDistance : camera.Distance;
    double speed = freeFlySpeed * distanceForSpeed * 0.2;
    if (fast) {
        speed *= 2.5;
    } else if (slow) {
        speed *= 0.4;
    }

    double fwd = 0.0;
    double rt = 0.0;
    double up = 0.0;
    if (moveForward) fwd += speed * deltaTime;
    if (moveBack) fwd -= speed * deltaTime;
    if (moveRight) rt += speed * deltaTime;
    if (moveLeft) rt -= speed * deltaTime;
    if (moveUp) up += speed * deltaTime;
    if (moveDown) up -= speed * deltaTime;

    if (fwd != 0.0 || rt != 0.0 || up != 0.0) {
        camera.MoveFreeFly(fwd, rt, up);
        ApplyCamera();
    }
}

void Viewport3D::ResetCamera() {
    camera.ResetToDefaultPose();
    ApplyCamera();
}
