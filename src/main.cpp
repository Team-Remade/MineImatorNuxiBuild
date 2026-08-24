#include <cstdio>
#define SDL_MAIN_HANDLED
#include <SDL.h>
#include <glad/glad.h>
#include <RmlUi/Core.h>
#include <array>
#include "assets/EmbeddedFileInterface.hpp"
#include "ui/MenuBar.hpp"
#include "ui/RmlGlRenderer.hpp"
#include "viewport/Viewport3D.hpp"
#include "window/Window.hpp"

bool quit = false;
SDL_Event event;
Rml::Context* uiContext = nullptr;
Rml::ElementDocument* uiDocument = nullptr;
std::array<Rml::ElementDocument*, 4> panelDocuments{};

constexpr int initialWidth = 640;
constexpr int initialHeight = 480;
constexpr int menuBarHeight = 30;

Window window(initialWidth, initialHeight);
int width = initialWidth;
int height = initialHeight;
RmlGlRenderer renderInterface(&width, &height);
EmbeddedFileInterface embeddedFileInterface;
MenuBar menuBar;
Viewport3D viewport3D(menuBarHeight);

void GetOpenGLVersion() {
    const char* glVersion = reinterpret_cast<const char *>(glGetString(GL_VERSION));
    const char* glVendor = reinterpret_cast<const char *>(glGetString(GL_VENDOR));
    const char* glRenderer = reinterpret_cast<const char *>(glGetString(GL_RENDERER));
    const char* glslVersion = reinterpret_cast<const char *>(glGetString(GL_SHADING_LANGUAGE_VERSION));
    printf("OpenGL version: %s\n", glVersion);
    printf("OpenGL vendor: %s\n", glVendor);
    printf("OpenGL renderer: %s\n", glRenderer);
    printf("OpenGL Shading Language version: %s\n", glslVersion);
}

void Init() {
    if (!window.Init("Mine Imator Nuxi Build")) {
        printf("SDL window init failed: %s\n", SDL_GetError());
        exit(1);
    }

    width = window.GetWidth();
    height = window.GetHeight();

    if (!gladLoadGLLoader(SDL_GL_GetProcAddress)) {
        printf("gladLoadGLLoader failed\n");
        exit(1);
    }

    glViewport(0, 0, width, height);

    glEnable(GL_BLEND);
    glBlendEquation(GL_FUNC_ADD);
    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

    if (!renderInterface.Init()) {
        printf("Rml render interface init failed\n");
        exit(1);
    }

    Rml::SetRenderInterface(&renderInterface);
    Rml::SetFileInterface(&embeddedFileInterface);
    if (!Rml::Initialise()) {
        printf("Rml::Initialise failed\n");
        exit(1);
    }

    uiContext = Rml::CreateContext("main", Rml::Vector2i(width, height));
    if (!uiContext) {
        printf("Rml::CreateContext failed\n");
        exit(1);
    }

    const Rml::Span fontData(
        (GetEmbeddedNotoSansData()),
        GetEmbeddedNotoSansSize()
    );
    if (!Rml::LoadFontFace(fontData, "Noto Sans", Rml::Style::FontStyle::Normal)) {
        printf("Rml::LoadFontFace failed for embedded Noto Sans\n");
        exit(1);
    }

    uiDocument = uiContext->LoadDocument("assets/ui/menubar/main_menu.rml");
    if (!uiDocument) {
        printf("Failed to load assets/ui/menubar/main_menu.rml\n");
        exit(1);
    }

    menuBar.Init(uiDocument);

    constexpr std::array<const char*, 4> panelPaths = {
        "assets/ui/panels/viewport.rml",
        "assets/ui/panels/timeline.rml",
        "assets/ui/panels/scene_tree.rml",
        "assets/ui/panels/properties.rml"
    };
    for (size_t i = 0; i < panelPaths.size(); ++i) {
        panelDocuments[i] = uiContext->LoadDocument(panelPaths[i]);
        if (panelDocuments[i] == nullptr) {
            printf("Failed to load %s\n", panelPaths[i]);
            exit(1);
        }
        panelDocuments[i]->Show();
    }

    uiDocument->Show();

    viewport3D.Init(width, height);

    GetOpenGLVersion();
}

void Input() {
    while (window.PollEvent(event)) {
        switch (event.type) {
            case SDL_QUIT:
                quit = true;
                break;
            case SDL_WINDOWEVENT:
                if (window.HandleResizeEvent(event)) {
                    width = window.GetWidth();
                    height = window.GetHeight();
                    glViewport(0, 0, width, height);
                    viewport3D.Resize(width, height);
                    if (uiContext != nullptr) {
                        uiContext->SetDimensions(Rml::Vector2i(width, height));
                    }
                }
                break;
            case SDL_MOUSEMOTION:
                if (uiContext != nullptr) {
                    uiContext->ProcessMouseMove(event.motion.x, event.motion.y, 0);
                }
                break;
            case SDL_MOUSEBUTTONDOWN:
            case SDL_MOUSEBUTTONUP:
                if (uiContext != nullptr) {
                    const int buttonIndex = (event.button.button > 0) ? (event.button.button - 1) : 0;
                    const bool down = event.type == SDL_MOUSEBUTTONDOWN;
                    if (down) {
                        uiContext->ProcessMouseButtonDown(buttonIndex, 0);
                    } else {
                        uiContext->ProcessMouseButtonUp(buttonIndex, 0);
                    }
                }
                break;
        }
    }
}

void MainLoop() {
    while (!quit) {
        Input();

        const int sceneX = viewport3D.GetSceneX(width);
        const int sceneY = viewport3D.GetSceneY(height);
        const int sceneWidth = viewport3D.GetSceneWidth(width);
        const int sceneHeight = viewport3D.GetSceneHeight(height);

        glViewport(0, 0, width, height);
        glClearColor(0.09f, 0.10f, 0.14f, 1.0f);
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        glUseProgram(0);
        glBindVertexArray(0);
        glBindBuffer(GL_ARRAY_BUFFER, 0);
        glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, 0);
        glActiveTexture(GL_TEXTURE0);
        glBindTexture(GL_TEXTURE_2D, 0);

        glEnable(GL_DEPTH_TEST);
        glEnable(GL_SCISSOR_TEST);
        glViewport(sceneX, sceneY, sceneWidth, sceneHeight);
        glScissor(sceneX, sceneY, sceneWidth, sceneHeight);
        viewport3D.RenderFrame();
        glDisable(GL_SCISSOR_TEST);
        glDisable(GL_DEPTH_TEST);

        glBindFramebuffer(GL_FRAMEBUFFER, 0);
        glViewport(0, 0, width, height);
        glDisable(GL_SCISSOR_TEST);

        glEnable(GL_BLEND);
        glBlendEquation(GL_FUNC_ADD);
        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

        if (uiContext != nullptr) {
            uiContext->Update();
            uiContext->Render();
        }

        window.SwapBuffers();
        SDL_Delay(16);
    }
}

void Cleanup() {
    menuBar.Shutdown();

    for (Rml::ElementDocument*& panelDocument : panelDocuments) {
        if (panelDocument != nullptr) {
            panelDocument->Close();
            panelDocument = nullptr;
        }
    }

    if (uiDocument != nullptr) {
        uiDocument->Close();
        uiDocument = nullptr;
    }

    if (uiContext != nullptr) {
        Rml::RemoveContext("main");
        uiContext = nullptr;
    }

    Rml::Shutdown();
    renderInterface.ShutdownRenderer();

    window.Shutdown();
}

int main(int argc, char *argv[]) {
    SDL_SetMainReady();
    Init();
    if (!window.IsValid()) goto CLEANUP_AND_QUIT;

    MainLoop();

    CLEANUP_AND_QUIT:
    Cleanup();

    return 0;
}
