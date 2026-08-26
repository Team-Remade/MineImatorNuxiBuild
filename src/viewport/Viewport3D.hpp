#pragma once

#include <osg/ref_ptr>

#include "OrbitCamera.hpp"

namespace osgViewer {
    class Viewer;
    class GraphicsWindowEmbedded;
}

namespace osg {
    class Camera;
}

class Viewport3D {
public:
    explicit Viewport3D(int menuBarHeight);
    ~Viewport3D();

    void Init(int width, int windowHeight);
    void Resize(int width, int windowHeight);
    void RenderFrame();

    int GetSceneX(int windowWidth) const;
    int GetSceneY(int windowHeight) const;
    int GetSceneWidth(int windowWidth) const;
    int GetSceneHeight(int windowHeight) const;

    // Mouse navigation (screen coordinates, origin top-left).
    // Returns true when the event was consumed by the viewport.
    bool IsInsideScene(int mouseX, int mouseY) const;
    bool HandleMouseButton(unsigned char button, bool down, int mouseX, int mouseY);
    bool HandleMouseMotion(int mouseX, int mouseY, int deltaX, int deltaY);
    bool HandleMouseWheel(int mouseX, int mouseY, int wheelY);

    // Free-fly (first-person) navigation. Activated by holding the right mouse
    // button over the scene. Movement is applied per frame from the current
    // keyboard state; returns true while free-fly is active.
    bool IsFreeFlyActive() const { return freeFly; }
    void UpdateFreeFly(double deltaTime,
                       bool moveForward, bool moveBack, bool moveLeft, bool moveRight,
                       bool moveUp, bool moveDown, bool fast, bool slow);
    // Resets the camera to its default pose (bound to the R key in free-fly).
    void ResetCamera();

private:
    void ApplyCamera();

    int menuBarHeight;
    // Height of the viewport panel's own RmlUi header strip (panel-header in
    // menubar.rcss), which sits above the actual 3-D canvas but below the top
    // menu bar. The 3-D hit-test region and GL viewport must start below this
    // strip, or clicks on header controls (e.g. the spawn-menu button) get
    // swallowed as viewport navigation input instead of reaching RmlUi.
    static constexpr int panelHeaderHeight = 28;
    int currentWidth = 1;
    int currentHeight = 1;

    OrbitCamera camera;
    bool orbiting = false;
    bool panning = false;
    bool freeFly = false;
    double freeFlySpeed = 5.0;

    osg::ref_ptr<osgViewer::Viewer> viewer;
    osg::ref_ptr<osgViewer::GraphicsWindowEmbedded> graphicsWindow;
};
