#include "RmlGlRenderer.hpp"

#include <cstddef>
#include <cstdio>
#include <vector>

#define STB_IMAGE_IMPLEMENTATION
#include <stb/stb_image.h>

RmlGlRenderer::RmlGlRenderer(int* width, int* height)
    : width(width), height(height) {
}

bool RmlGlRenderer::Init() {
    const char* vertexShaderSource = R"(
            #version 330 core
            layout (location = 0) in vec2 inPosition;
            layout (location = 1) in vec4 inColor;
            layout (location = 2) in vec2 inTexCoord;

            out vec4 fragColor;
            out vec2 fragTexCoord;

            uniform vec2 uScreenSize;
            uniform vec2 uTranslation;

            void main() {
                vec2 pos = inPosition + uTranslation;
                vec2 ndc = vec2((pos.x / uScreenSize.x) * 2.0 - 1.0, 1.0 - (pos.y / uScreenSize.y) * 2.0);
                gl_Position = vec4(ndc, 0.0, 1.0);
                fragColor = inColor;
                fragTexCoord = inTexCoord;
            }
        )";

    const char* fragmentShaderSource = R"(
            #version 330 core
            in vec4 fragColor;
            in vec2 fragTexCoord;

            out vec4 outColor;

            uniform sampler2D uTexture;
            uniform bool uUseTexture;

            void main() {
                vec4 sampled = uUseTexture ? texture(uTexture, fragTexCoord) : vec4(1.0);
                outColor = fragColor * sampled;
            }
        )";

    const GLuint vertexShader = CompileShader(GL_VERTEX_SHADER, vertexShaderSource);
    const GLuint fragmentShader = CompileShader(GL_FRAGMENT_SHADER, fragmentShaderSource);
    if (vertexShader == 0 || fragmentShader == 0) {
        return false;
    }

    shaderProgram = glCreateProgram();
    glAttachShader(shaderProgram, vertexShader);
    glAttachShader(shaderProgram, fragmentShader);
    glLinkProgram(shaderProgram);

    GLint linkStatus = GL_FALSE;
    glGetProgramiv(shaderProgram, GL_LINK_STATUS, &linkStatus);
    glDeleteShader(vertexShader);
    glDeleteShader(fragmentShader);
    if (linkStatus != GL_TRUE) {
        char buffer[512] = {};
        glGetProgramInfoLog(shaderProgram, sizeof(buffer), nullptr, buffer);
        printf("Rml shader link failed: %s\n", buffer);
        return false;
    }

    glGenVertexArrays(1, &vao);
    glGenBuffers(1, &vbo);
    glGenBuffers(1, &ebo);

    glBindVertexArray(vao);
    glBindBuffer(GL_ARRAY_BUFFER, vbo);
    glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, ebo);

    glEnableVertexAttribArray(0);
    glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, sizeof(Rml::Vertex), (void*)offsetof(Rml::Vertex, position));
    glEnableVertexAttribArray(1);
    glVertexAttribPointer(1, 4, GL_UNSIGNED_BYTE, GL_TRUE, sizeof(Rml::Vertex), (void*)offsetof(Rml::Vertex, colour));
    glEnableVertexAttribArray(2);
    glVertexAttribPointer(2, 2, GL_FLOAT, GL_FALSE, sizeof(Rml::Vertex), (void*)offsetof(Rml::Vertex, tex_coord));

    glBindVertexArray(0);
    return true;
}

void RmlGlRenderer::ShutdownRenderer() {
    for (const auto& item : textures) {
        glDeleteTextures(1, &item.second);
    }
    textures.clear();
    geometries.clear();

    if (ebo != 0) glDeleteBuffers(1, &ebo);
    if (vbo != 0) glDeleteBuffers(1, &vbo);
    if (vao != 0) glDeleteVertexArrays(1, &vao);
    if (shaderProgram != 0) glDeleteProgram(shaderProgram);
}

Rml::CompiledGeometryHandle RmlGlRenderer::CompileGeometry(Rml::Span<const Rml::Vertex> vertices, Rml::Span<const int> indices) {
    const Rml::CompiledGeometryHandle handle = nextGeometryHandle++;
    geometries[handle] = UiGeometry{std::vector<Rml::Vertex>(vertices.begin(), vertices.end()), std::vector<int>(indices.begin(), indices.end())};
    return handle;
}

void RmlGlRenderer::RenderGeometry(Rml::CompiledGeometryHandle geometry, Rml::Vector2f translation, Rml::TextureHandle texture) {
    const auto geometryIt = geometries.find(geometry);
    if (geometryIt == geometries.end()) {
        return;
    }

    glUseProgram(shaderProgram);
    glUniform2f(glGetUniformLocation(shaderProgram, "uScreenSize"), static_cast<float>(*width), static_cast<float>(*height));
    glUniform2f(glGetUniformLocation(shaderProgram, "uTranslation"), translation.x, translation.y);

    const GLuint glTexture = texture != 0 ? textures[texture] : 0;
    const bool useTexture = glTexture != 0;
    glUniform1i(glGetUniformLocation(shaderProgram, "uUseTexture"), useTexture ? 1 : 0);

    if (useTexture) {
        glActiveTexture(GL_TEXTURE0);
        glBindTexture(GL_TEXTURE_2D, glTexture);
        glUniform1i(glGetUniformLocation(shaderProgram, "uTexture"), 0);
    }

    const UiGeometry& uiGeometry = geometryIt->second;
    glBindVertexArray(vao);
    glBindBuffer(GL_ARRAY_BUFFER, vbo);
    glBufferData(GL_ARRAY_BUFFER, static_cast<GLsizeiptr>(uiGeometry.vertices.size() * sizeof(Rml::Vertex)), uiGeometry.vertices.data(), GL_STREAM_DRAW);
    glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, ebo);
    glBufferData(GL_ELEMENT_ARRAY_BUFFER, static_cast<GLsizeiptr>(uiGeometry.indices.size() * sizeof(int)), uiGeometry.indices.data(), GL_STREAM_DRAW);
    glDrawElements(GL_TRIANGLES, static_cast<GLsizei>(uiGeometry.indices.size()), GL_UNSIGNED_INT, nullptr);
}

void RmlGlRenderer::ReleaseGeometry(Rml::CompiledGeometryHandle geometry) {
    geometries.erase(geometry);
}

Rml::TextureHandle RmlGlRenderer::LoadTexture(Rml::Vector2i& texture_dimensions, const Rml::String& source) {
    texture_dimensions = Rml::Vector2i(0, 0);
    Rml::FileInterface* fileInterface = Rml::GetFileInterface();
    if (!fileInterface) {
        return 0;
    }

    Rml::FileHandle file = fileInterface->Open(source);
    if (!file) {
        printf("Failed to open texture file: %s\n", source.c_str());
        return 0;
    }

    const size_t length = fileInterface->Length(file);
    if (length == 0) {
        fileInterface->Close(file);
        return 0;
    }

    std::vector<unsigned char> buffer(length);
    const size_t bytesRead = fileInterface->Read(buffer.data(), length, file);
    fileInterface->Close(file);

    if (bytesRead == 0) {
        return 0;
    }

    int x = 0;
    int y = 0;
    int channels = 0;
    stbi_uc* data = stbi_load_from_memory(buffer.data(), static_cast<int>(bytesRead), &x, &y, &channels, 4);
    if (!data) {
        printf("Failed to decode image: %s\n", source.c_str());
        return 0;
    }

    texture_dimensions.x = x;
    texture_dimensions.y = y;

    const Rml::TextureHandle handle = GenerateTexture(Rml::Span<const Rml::byte>(reinterpret_cast<const Rml::byte*>(data), static_cast<size_t>(x * y * 4)), texture_dimensions);
    stbi_image_free(data);

    return handle;
}

Rml::TextureHandle RmlGlRenderer::GenerateTexture(Rml::Span<const Rml::byte> source, Rml::Vector2i source_dimensions) {
    GLuint texture = 0;
    glGenTextures(1, &texture);
    glBindTexture(GL_TEXTURE_2D, texture);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
    glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, source_dimensions.x, source_dimensions.y, 0, GL_RGBA, GL_UNSIGNED_BYTE, source.data());

    const Rml::TextureHandle handle = nextTextureHandle++;
    textures[handle] = texture;
    return handle;
}

void RmlGlRenderer::ReleaseTexture(Rml::TextureHandle texture) {
    auto textureIt = textures.find(texture);
    if (textureIt == textures.end()) {
        return;
    }

    GLuint glTexture = textureIt->second;
    glDeleteTextures(1, &glTexture);
    textures.erase(textureIt);
}

void RmlGlRenderer::EnableScissorRegion(bool enable) {
    if (enable) {
        glEnable(GL_SCISSOR_TEST);
    } else {
        glDisable(GL_SCISSOR_TEST);
    }
}

void RmlGlRenderer::SetScissorRegion(Rml::Rectanglei region) {
    const int x = region.Left();
    const int y = *height - region.Bottom();
    glScissor(x, y, region.Width(), region.Height());
}

GLuint RmlGlRenderer::CompileShader(GLenum shaderType, const char* source) {
    GLuint shader = glCreateShader(shaderType);
    glShaderSource(shader, 1, &source, nullptr);
    glCompileShader(shader);

    GLint success = GL_FALSE;
    glGetShaderiv(shader, GL_COMPILE_STATUS, &success);
    if (success == GL_TRUE) {
        return shader;
    }

    char buffer[512] = {};
    glGetShaderInfoLog(shader, sizeof(buffer), nullptr, buffer);
    printf("Rml shader compile failed: %s\n", buffer);
    glDeleteShader(shader);
    return 0;
}