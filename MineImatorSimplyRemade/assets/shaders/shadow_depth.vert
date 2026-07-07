#version 330 core

layout (location = 0) in vec3 aPos;
layout (location = 2) in vec2 aTexCoord;

uniform mat4  uMVP;
uniform vec2  uTexOffset;
uniform float uTexScaleV;

out vec2 vTexCoord;

void main() {
    vTexCoord = vec2(aTexCoord.x, aTexCoord.y * uTexScaleV + uTexOffset.y);
    gl_Position = uMVP * vec4(aPos, 1.0);
}