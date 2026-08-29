#version 450

// Shared by pick.frag (color-coded object picking) and silhouette.frag
// (selection mask for the edge-outline pass) - both need the same UV-transformed
// geometry, just different fragment outputs.
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoord;
layout (location = 3) in ivec4 aBoneIndices;
layout (location = 4) in vec4 aBoneWeights;

layout(set = 0, binding = 0, std140) uniform PickUniforms {
    mat4  uMVP;
    vec2  uTexOffset;
    float uTexScaleV;
    int   uIsSkinned;
    vec2  uTexUvOffset;
    vec2  uTexUvRepeat;
    vec2  uTexUvMirror;
    vec2  _pad1;
};

layout(set = 0, binding = 6, std140) uniform BoneMatrices {
    mat4 uBoneMatrices[64];
};

layout(location = 0) out vec2 vTexCoord;

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
    vec4 pos;
    if (uIsSkinned != 0) {
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

    vec2 baseUv = vec2(aTexCoord.x, aTexCoord.y * uTexScaleV + uTexOffset.y);
    vTexCoord = applyUvTransform(baseUv);
    gl_Position = uMVP * pos;
}
