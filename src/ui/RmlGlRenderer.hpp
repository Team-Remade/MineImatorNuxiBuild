#pragma once

#include <glad/glad.h>
#include <RmlUi/Core.h>

#include <unordered_map>
#include <vector>

struct UiGeometry {
    std::vector<Rml::Vertex> vertices;
    std::vector<int> indices;
};

class RmlGlRenderer : public Rml::RenderInterface {
public:
    explicit RmlGlRenderer(int* width, int* height);

    bool Init();
    void ShutdownRenderer();

    Rml::CompiledGeometryHandle CompileGeometry(Rml::Span<const Rml::Vertex> vertices, Rml::Span<const int> indices) override;
    void RenderGeometry(Rml::CompiledGeometryHandle geometry, Rml::Vector2f translation, Rml::TextureHandle texture) override;
    void ReleaseGeometry(Rml::CompiledGeometryHandle geometry) override;

    Rml::TextureHandle LoadTexture(Rml::Vector2i& texture_dimensions, const Rml::String& source) override;
    Rml::TextureHandle GenerateTexture(Rml::Span<const Rml::byte> source, Rml::Vector2i source_dimensions) override;
    void ReleaseTexture(Rml::TextureHandle texture) override;

    void EnableScissorRegion(bool enable) override;
    void SetScissorRegion(Rml::Rectanglei region) override;

private:
    GLuint CompileShader(GLenum shaderType, const char* source);

    int* width;
    int* height;

    GLuint shaderProgram = 0;
    GLuint vao = 0;
    GLuint vbo = 0;
    GLuint ebo = 0;

    Rml::CompiledGeometryHandle nextGeometryHandle = 1;
    Rml::TextureHandle nextTextureHandle = 1;

    std::unordered_map<Rml::CompiledGeometryHandle, UiGeometry> geometries;
    std::unordered_map<Rml::TextureHandle, GLuint> textures;
};