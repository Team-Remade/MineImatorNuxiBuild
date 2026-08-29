#version 450

layout(location = 0) in vec2 vTexCoord;

layout(set = 0, binding = 0, std140) uniform BillboardUniforms {
    mat4  uView;
    mat4  uProj;
    vec3  uWorldPos;
    float uSize;
    vec4  uTint;
};

layout(set = 0, binding = 1) uniform texture2D uBillboardTexture;
layout(set = 0, binding = 2) uniform sampler uBillboardSampler;

layout(location = 0) out vec4 FragColor;

void main() {
    vec4 tex = texture(sampler2D(uBillboardTexture, uBillboardSampler), vTexCoord);
    FragColor = tex * uTint;
    if (FragColor.a < 0.01) discard;
}
