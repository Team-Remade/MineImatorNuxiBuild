#version 450

layout(set = 0, binding = 0, std140) uniform LightRingUniforms {
    mat4  uView;
    mat4  uProj;
    vec3  uWorldPos;
    float uRange;
    vec4  uColor;
};

layout(location = 0) out vec4 FragColor;

void main() {
    FragColor = uColor;
}
