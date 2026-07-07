#version 330 core

layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoord;

uniform mat4  uMVP;
uniform mat4  uModel;
uniform mat4  uLightSpaceMatrix;
uniform vec2  uTexOffset;   // per-frame UV offset for animated textures (0,0 = static)
uniform float uTexScaleV;   // V scale for spritesheet (frameH / totalH), 1.0 = static

out vec3 vNormal;
out vec3 vFragPos;
out vec2 vTexCoord;
out vec4 vShadowCoord;

void main() {
    vec4 worldPos   = uModel * vec4(aPos, 1.0);
    vFragPos        = worldPos.xyz;
    // Normal matrix: inverse-transpose of upper-left 3x3 of model matrix
    vNormal         = normalize(mat3(transpose(inverse(uModel))) * aNormal);
    // Scale V into the frame's slice of the spritesheet, then shift to the right frame
    vTexCoord       = vec2(aTexCoord.x, aTexCoord.y * uTexScaleV + uTexOffset.y);
    vShadowCoord    = uLightSpaceMatrix * worldPos;
    gl_Position     = uMVP * vec4(aPos, 1.0);
}
