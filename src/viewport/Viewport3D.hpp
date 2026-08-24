#pragma once

#include <osg/ref_ptr>

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

private:
    int menuBarHeight;
    osg::ref_ptr<osgViewer::Viewer> viewer;
    osg::ref_ptr<osgViewer::GraphicsWindowEmbedded> graphicsWindow;
};
