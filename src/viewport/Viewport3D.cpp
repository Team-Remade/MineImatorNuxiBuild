#include "Viewport3D.hpp"

#include <glad/glad.h>
#include <osg/Camera>
#include <osg/Geode>
#include <osg/MatrixTransform>
#include <osg/ShapeDrawable>
#include <osgViewer/GraphicsWindow>
#include <osgViewer/Viewer>

Viewport3D::Viewport3D(int menuBarHeight) : menuBarHeight(menuBarHeight) {
}

Viewport3D::~Viewport3D() = default;

int Viewport3D::GetSceneHeight(int windowHeight) const {
    const int sceneHeight = windowHeight - menuBarHeight;
    return sceneHeight > 0 ? sceneHeight : 1;
}

void Viewport3D::Init(int width, int windowHeight) {
    osg::ref_ptr<osg::Box> box = new osg::Box(osg::Vec3(0.0f, 0.0f, -3.0f), 1.0f);
    osg::ref_ptr<osg::ShapeDrawable> cubeDrawable = new osg::ShapeDrawable(box);
    cubeDrawable->setColor(osg::Vec4(0.2f, 0.7f, 1.0f, 1.0f));

    osg::ref_ptr<osg::Geode> geode = new osg::Geode();
    geode->addDrawable(cubeDrawable);

    osg::ref_ptr<osg::MatrixTransform> root = new osg::MatrixTransform();
    root->addChild(geode);

    const int sceneHeight = GetSceneHeight(windowHeight);
    graphicsWindow = new osgViewer::GraphicsWindowEmbedded(0, 0, width, sceneHeight);

    viewer = new osgViewer::Viewer();
    viewer->setThreadingModel(osgViewer::Viewer::SingleThreaded);
    viewer->setSceneData(root);
    viewer->setCameraManipulator(nullptr);
    viewer->setReleaseContextAtEndOfFrameHint(false);

    osg::Camera* camera = viewer->getCamera();
    camera->setGraphicsContext(graphicsWindow);
    camera->setViewport(new osg::Viewport(0, 0, width, sceneHeight));
    camera->setDrawBuffer(GL_BACK);
    camera->setReadBuffer(GL_BACK);
    camera->setProjectionMatrixAsPerspective(45.0, static_cast<double>(width) / static_cast<double>(sceneHeight), 0.1, 1000.0);
    camera->setViewMatrixAsLookAt(osg::Vec3(0.0, -6.0, 1.5), osg::Vec3(0.0, 0.0, -3.0), osg::Vec3(0.0, 0.0, 1.0));
}

void Viewport3D::Resize(int width, int windowHeight) {
    const int sceneHeight = GetSceneHeight(windowHeight);

    if (graphicsWindow != nullptr) {
        graphicsWindow->resized(0, 0, width, sceneHeight);
        graphicsWindow->getEventQueue()->windowResize(0, 0, width, sceneHeight);
    }

    if (viewer != nullptr) {
        osg::Camera* camera = viewer->getCamera();
        if (camera != nullptr) {
            camera->setViewport(0, 0, width, sceneHeight);
            camera->setProjectionMatrixAsPerspective(45.0, static_cast<double>(width) / static_cast<double>(sceneHeight), 0.1, 1000.0);
        }
    }
}

void Viewport3D::RenderFrame() {
    if (viewer != nullptr) {
        viewer->frame();
    }
}
