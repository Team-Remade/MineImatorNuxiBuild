#version 330 core

layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoord;

uniform mat4 uMVP;
uniform mat4 uModel;

out vec3 vNormal;
out vec3 vFragPos;
out vec2 vTexCoord;

void main() {
    vec4 worldPos   = uModel * vec4(aPos, 1.0);
    vFragPos        = worldPos.xyz;
    // Normal matrix: inverse-transpose of upper-left 3x3 of model matrix
    vNormal         = normalize(mat3(transpose(inverse(uModel))) * aNormal);
    vTexCoord       = aTexCoord;
    gl_Position     = uMVP * vec4(aPos, 1.0);
}
