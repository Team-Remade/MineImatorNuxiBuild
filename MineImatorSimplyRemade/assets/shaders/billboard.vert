#version 330 core

// Billboard vertex shader.
// Inputs are the four corners of a unit quad in [-0.5, 0.5] local space.
// The vertex shader expands them into world space using the camera's right/up
// vectors so the quad always faces the camera (spherical billboard).

layout (location = 0) in vec3 aPos;       // local-space quad corner
layout (location = 2) in vec2 aTexCoord;  // UV

uniform mat4  uView;
uniform mat4  uProj;
uniform vec3  uWorldPos;   // world-space center of the billboard
uniform float uSize;       // world-space size (width = height)

out vec2 vTexCoord;

void main() {
    // Extract camera right and up from the view matrix (column vectors of the
    // rotation part of the inverse-view, which is the transpose of the rotation
    // block of the view matrix).
    vec3 camRight = vec3(uView[0][0], uView[1][0], uView[2][0]);
    vec3 camUp    = vec3(uView[0][1], uView[1][1], uView[2][1]);

    // Expand the local corner into world space.
    vec3 worldCorner = uWorldPos
                     + camRight * aPos.x * uSize
                     + camUp    * aPos.y * uSize;

    gl_Position = uProj * uView * vec4(worldCorner, 1.0);
    vTexCoord   = aTexCoord;
}
