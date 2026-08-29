#version 450

layout(set = 0, binding = 0, std140) uniform GizmoUniforms {
    mat4 uMVP;
    vec4 uColor;
};

layout(location = 0) out vec4 FragColor;

void main() {
    FragColor = uColor;
}
