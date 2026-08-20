#include "Window.hpp"

Window::Window(int width, int height) : width(width), height(height) {
}

Window::~Window() {
    Shutdown();
}

bool Window::Init(const char* title) {
    if (SDL_Init(SDL_INIT_EVERYTHING) != 0) {
        return false;
    }

    SDL_GL_SetAttribute(SDL_GL_CONTEXT_PROFILE_MASK, SDL_GL_CONTEXT_PROFILE_COMPATIBILITY);
    SDL_GL_SetAttribute(SDL_GL_CONTEXT_MAJOR_VERSION, 3);
    SDL_GL_SetAttribute(SDL_GL_CONTEXT_MINOR_VERSION, 3);
    SDL_GL_SetAttribute(SDL_GL_DOUBLEBUFFER, 1);
    SDL_GL_SetAttribute(SDL_GL_DEPTH_SIZE, 24);

    handle = SDL_CreateWindow(title, SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED, width, height, SDL_WINDOW_OPENGL | SDL_WINDOW_RESIZABLE);
    if (handle == nullptr) {
        SDL_Quit();
        return false;
    }

    glContext = SDL_GL_CreateContext(handle);
    if (glContext == nullptr) {
        SDL_DestroyWindow(handle);
        handle = nullptr;
        SDL_Quit();
        return false;
    }

    SDL_MaximizeWindow(handle);
    SDL_PumpEvents();
    SDL_GetWindowSize(handle, &width, &height);

    return true;
}

void Window::Shutdown() {
    if (glContext != nullptr) {
        SDL_GL_DeleteContext(glContext);
        glContext = nullptr;
    }

    if (handle != nullptr) {
        SDL_DestroyWindow(handle);
        handle = nullptr;
    }

    SDL_Quit();
}

bool Window::IsValid() const {
    return handle != nullptr && glContext != nullptr;
}

bool Window::PollEvent(SDL_Event& event) {
    return SDL_PollEvent(&event) != 0;
}

bool Window::HandleResizeEvent(const SDL_Event& event) {
    if (event.type == SDL_WINDOWEVENT && event.window.event == SDL_WINDOWEVENT_SIZE_CHANGED) {
        width = event.window.data1;
        height = event.window.data2;
        return true;
    }

    return false;
}

int Window::GetWidth() const {
    return width;
}

int Window::GetHeight() const {
    return height;
}

void Window::GetDrawableSize(int& drawableWidth, int& drawableHeight) const {
    if (handle != nullptr) {
        SDL_GL_GetDrawableSize(handle, &drawableWidth, &drawableHeight);
    } else {
        drawableWidth = 0;
        drawableHeight = 0;
    }
}

SDL_Window* Window::GetHandle() const {
    return handle;
}

void Window::SwapBuffers() {
    if (handle != nullptr) {
        SDL_GL_SwapWindow(handle);
    }
}
