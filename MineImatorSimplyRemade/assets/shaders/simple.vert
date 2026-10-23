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
uniform bool  uBendEnabled;
uniform vec3  uBendAngle;
uniform vec3  uBendPivot;
uniform vec3  uBendShapePosition;
uniform mat4  uBendInverseShape;
uniform int   uBendSegmentAxis;
uniform int   uBendScaleAxis;
uniform float uBendAxisStart;
uniform float uBendStart;
uniform float uBendEnd;
uniform float uBendSize;
uniform bool  uBendInvert;
uniform bool  uBendSharp;
uniform bool  uBendPlane;

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

float bendComponent(vec3 value, int axis) {
    return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
}

float bendEase(float t) {
    if (t <= 0.0) return 0.0;
    if (t >= 1.0) return 1.0;
    float x = t * 2.0;
    // GLSL pow is undefined for negative bases, including integer exponents.
    return x < 1.0 ? 0.5 * pow(x, 5.0) : 1.0 - 0.5 * pow(2.0 - x, 5.0);
}

mat3 bendRotation(vec3 degrees) {
    vec3 a = radians(degrees);
    float cx = cos(a.x), sx = sin(a.x);
    float cy = cos(a.y), sy = sin(a.y);
    float cz = cos(a.z), sz = sin(a.z);
    mat3 rx = mat3(1, 0, 0, 0, cx, sx, 0, -sx, cx);
    mat3 ry = mat3(cy, 0, -sy, 0, 1, 0, sy, 0, cy);
    mat3 rz = mat3(cz, sz, 0, -sz, cz, 0, 0, 0, 1);
    return ry * rx * rz;
}

void applyBend(inout vec3 position, inout vec3 normal) {
    if (!uBendEnabled) return;
    if (all(equal(uBendAngle, vec3(0.0)))) return;
    vec3 localPosition = position - uBendShapePosition;
    vec3 sourcePosition = (uBendInverseShape * vec4(localPosition, 1.0)).xyz;
    float segmentPosition = bendComponent(sourcePosition, uBendSegmentAxis) - uBendAxisStart;
    float weight = segmentPosition < uBendStart ? 0.0 :
        segmentPosition >= uBendEnd ? 1.0 : 1.0 - (uBendEnd - segmentPosition) / uBendSize;
    if (uBendInvert) weight = 1.0 - weight;
    vec3 bendVector = uBendSharp
        ? uBendAngle * weight
        : vec3(uBendAngle.x * bendEase(weight), uBendAngle.y * weight, uBendAngle.z * bendEase(weight));
    // Recovering source coordinates through the inverse can move the first ring a few ULPs.
    bool initial = abs(segmentPosition) <= max(1e-7, abs(uBendAxisStart) * 1e-6);
    vec3 scale = vec3(1.0 + (!uBendPlane && !initial ? weight * 0.005 : 0.0));
    if (uBendSharp && (!uBendPlane || initial) && uBendScaleAxis >= 0 && segmentPosition > uBendStart && segmentPosition < uBendEnd) {
        float triangle = (weight <= 0.5 ? weight : 1.0 - weight) * 2.0;
        float angle = abs(bendComponent(uBendAngle, uBendScaleAxis));
        if (angle > 90.0) angle = 180.0 - angle;
        float correction = pow(triangle * clamp(angle / 90.0, 0.0, 1.0), 3.0) / 2.5;
        if (uBendScaleAxis != 0) scale.x += correction;
        if (uBendScaleAxis != 1) scale.y += correction;
        if (uBendScaleAxis != 2) scale.z += correction;
    }
    mat3 rotation = bendRotation(bendVector);
    position = uBendPivot + rotation * (scale * (localPosition - uBendPivot)) + uBendShapePosition;
    normal = normalize(rotation * (scale * normal));
}

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

    vec3 bentPosition = aPos;
    vec3 bentNormal = aNormal;
    applyBend(bentPosition, bentNormal);
    vec4 pos;
    vec3 normal;

    if (uIsSkinned) {
        mat4 skinMatrix = mat4(0.0);
        for (int i = 0; i < 4; i++) {
            if (aBoneIndices[i] >= 0 && aBoneIndices[i] < 64 && aBoneWeights[i] > 0.0) {
                skinMatrix += aBoneWeights[i] * uBoneMatrices[aBoneIndices[i]];
            }
        }
        pos    = skinMatrix * vec4(bentPosition, 1.0);
        normal = mat3(skinMatrix) * bentNormal;
    } else {
        pos    = vec4(bentPosition, 1.0);
        normal = bentNormal;
    }

    vec4 worldPos   = modelMat * pos;
    vFragPos        = worldPos.xyz;
    vNormal         = normalize(mat3(transpose(inverse(modelMat))) * normal);
    vec2 baseUv     = vec2(aTexCoord.x, aTexCoord.y * uTexScaleV + uTexOffset.y);
    vTexCoord       = applyUvTransform(baseUv);
    vShadowCoord    = uLightSpaceMatrix * worldPos;
    gl_Position     = uMVP * pos;
}
