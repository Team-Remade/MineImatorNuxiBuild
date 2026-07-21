#version 330 core

layout (location = 0) in vec3 aPos;
layout (location = 2) in vec2 aTexCoord;
layout (location = 3) in ivec4 aBoneIndices;
layout (location = 4) in vec4 aBoneWeights;

uniform mat4  uMVP;
uniform vec2  uTexOffset;
uniform float uTexScaleV;
uniform bool  uIsSkinned;
uniform mat4  uBoneMatrices[64];

out vec2 vTexCoord;

void main() {
    vec4 pos;
    if (uIsSkinned) {
        mat4 skinMatrix = mat4(0.0);
        for (int i = 0; i < 4; i++) {
            if (aBoneIndices[i] >= 0 && aBoneIndices[i] < 64 && aBoneWeights[i] > 0.0) {
                skinMatrix += aBoneWeights[i] * uBoneMatrices[aBoneIndices[i]];
            }
        }
        pos = skinMatrix * vec4(aPos, 1.0);
    } else {
        pos = vec4(aPos, 1.0);
    }

    vTexCoord = vec2(aTexCoord.x, aTexCoord.y * uTexScaleV + uTexOffset.y);
    gl_Position = uMVP * pos;
}
