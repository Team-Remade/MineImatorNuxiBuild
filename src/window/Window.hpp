#pragma once

#include <SDL.h>

class Window {
public:
    Window(int width, int height);
    ~Window();

    bool Init(const char* title);
    void Shutdown();

    bool IsValid() const;
    bool PollEvent(SDL_Event& event);
    bool HandleResizeEvent(const SDL_Event& event);

    int GetWidth() const;
    int GetHeight() const;

    SDL_Window* GetHandle() const;
    void SwapBuffers();

private:
    int width;
    int height;
    SDL_Window* handle = nullptr;
    SDL_GLContext glContext = nullptr;
};
