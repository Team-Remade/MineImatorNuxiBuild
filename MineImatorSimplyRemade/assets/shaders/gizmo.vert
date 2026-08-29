#version 450

layout (location = 0) in vec3 aPos;

layout(set = 0, binding = 0, std140) uniform GizmoUniforms {
    mat4 uMVP;
    vec4 uColor;
};

void main() {
    gl_Position = uMVP * vec4(aPos, 1.0);
}
