#version 330 core

// Camera-facing ring vertex shader.  Reuses the same billboard expansion
// technique as billboard.vert: extracts the camera's right/up vectors from the
// view matrix and expands the flat ring mesh in world space so it always faces
// the camera.

layout (location = 0) in vec3 aPos;

uniform mat4  uView;
uniform mat4  uProj;
uniform vec3  uWorldPos;
uniform float uRange;

void main() {
    vec3 camRight = vec3(uView[0][0], uView[1][0], uView[2][0]);
    vec3 camUp    = vec3(uView[0][1], uView[1][1], uView[2][1]);

    // The ring mesh lives on the XZ plane, so use aPos.x and aPos.z as the
    // billboard coordinates (aPos.y is always 0 for the ring vertices).
    vec3 worldPos = uWorldPos
                  + camRight * aPos.x * uRange
                  + camUp    * aPos.z * uRange;

    gl_Position = uProj * uView * vec4(worldPos, 1.0);
}
