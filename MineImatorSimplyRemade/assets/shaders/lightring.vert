#version 450

// Camera-facing ring vertex shader - same billboard-expansion technique as
// billboard.vert, but the ring mesh lives on the XZ plane (aPos.y is always 0),
// so aPos.x/aPos.z are used as the billboard coordinates instead of aPos.x/aPos.y.
layout (location = 0) in vec3 aPos;

layout(set = 0, binding = 0, std140) uniform LightRingUniforms {
    mat4  uView;
    mat4  uProj;
    vec3  uWorldPos;
    float uRange;
    vec4  uColor;
};

void main() {
    vec3 camRight = vec3(uView[0][0], uView[1][0], uView[2][0]);
    vec3 camUp    = vec3(uView[0][1], uView[1][1], uView[2][1]);

    vec3 worldPos = uWorldPos
                  + camRight * aPos.x * uRange
                  + camUp    * aPos.z * uRange;

    gl_Position = uProj * uView * vec4(worldPos, 1.0);
}
