#version 330 core

layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoord;
layout (location = 3) in ivec4 aBoneIndices;
layout (location = 4) in vec4 aBoneWeights;

// Instance model matrix (4 consecutive vec4 attributes, divisor = 1).
layout (location = 5) in vec4 aInstanceM0;
layout (location = 6) in vec4 aInstanceM1;
layout (location = 7) in vec4 aInstanceM2;
layout (location = 8) in vec4 aInstanceM3;

uniform mat4  uMVP;
uniform mat4  uModel;
uniform vec2  uTexOffset;
uniform float uTexScaleV;
uniform vec2  uTexUvOffset;
uniform vec2  uTexUvRepeat;
uniform vec2  uTexUvMirror;
uniform bool  uIsSkinned;
uniform bool  uUseInstancing;
uniform mat4  uBoneMatrices[64];

layout(std140) uniform SceneData {
    mat4  uLightSpaceMatrix;
    vec3  uAmbient;
    float _pad0;
    vec3  uLightDir;
    float _pad1;
    vec3  uLightColor;
    float _pad2;
    vec3  uSunFillLightDir;
    float _pad3;
    vec3  uSunFillLightColor;
    float _pad4;
    vec3  uMoonFillLightDir;
    float _pad5;
    vec3  uMoonFillLightColor;
    float _pad6;
    int   uShadowDebugMode;
    int   uMainLightCastsShadows;
    int   uSunFillLightCastsShadows;
    int   uMoonFillLightCastsShadows;
};

out vec3 vNormal;
out vec3 vFragPos;
out vec2 vTexCoord;
out vec4 vShadowCoord;

vec2 applyUvTransform(vec2 uv)
{
    vec2 repeated = uv * uTexUvRepeat;

    if (uTexUvMirror.x > 0.5)
        repeated.x = abs(fract(repeated.x * 0.5) * 2.0 - 1.0);

    if (uTexUvMirror.y > 0.5)
        repeated.y = abs(fract(repeated.y * 0.5) * 2.0 - 1.0);

    return repeated + uTexUvOffset;
}

void main() {
    mat4 modelMat = uUseInstancing
        ? mat4(aInstanceM0, aInstanceM1, aInstanceM2, aInstanceM3)
        : uModel;

    vec4 pos;
    vec3 normal;

    if (uIsSkinned) {
        mat4 skinMatrix = mat4(0.0);
        for (int i = 0; i < 4; i++) {
            if (aBoneIndices[i] >= 0 && aBoneIndices[i] < 64 && aBoneWeights[i] > 0.0) {
                skinMatrix += aBoneWeights[i] * uBoneMatrices[aBoneIndices[i]];
            }
        }
        pos    = skinMatrix * vec4(aPos, 1.0);
        normal = mat3(skinMatrix) * aNormal;
    } else {
        pos    = vec4(aPos, 1.0);
        normal = aNormal;
    }

    vec4 worldPos   = modelMat * pos;
    vFragPos        = worldPos.xyz;
    vNormal         = normalize(mat3(transpose(inverse(modelMat))) * normal);
    vec2 baseUv     = vec2(aTexCoord.x, aTexCoord.y * uTexScaleV + uTexOffset.y);
    vTexCoord       = applyUvTransform(baseUv);
    vShadowCoord    = uLightSpaceMatrix * worldPos;
    gl_Position     = uMVP * pos;
}
