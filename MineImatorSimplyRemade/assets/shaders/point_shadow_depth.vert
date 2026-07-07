#version 330 core

layout (location = 0) in vec3 aPos;
layout (location = 2) in vec2 aTexCoord;

uniform mat4  uLightViewProj;
uniform mat4  uModel;
uniform vec2  uTexOffset;
uniform float uTexScaleV;

out vec3 vWorldPos;
out vec2 vTexCoord;

void main() {
    vec4 worldPos = uModel * vec4(aPos, 1.0);
    vWorldPos = worldPos.xyz;
    vTexCoord = vec2(aTexCoord.x, aTexCoord.y * uTexScaleV + uTexOffset.y);
    gl_Position = uLightViewProj * worldPos;
}