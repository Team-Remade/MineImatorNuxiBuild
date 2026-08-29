#version 450

// MIGRATION NOTE (subsystem pass 1/N): skinning (aBoneIndices/aBoneWeights) and
// per-instance model matrices (aInstanceM0..3) are deliberately dropped from
// this pass's vertex inputs - they're a separate "skinning + instancing"
// follow-up subsystem pass (adding vertex buffers for them, plus wiring
// MeshUniforms.IsSkinned/UseInstancing back to something meaningful; both
// fields still exist in the uniform block below and default to 0/false).
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoord;

// MIGRATION NOTE: Veldrid (via Veldrid.SPIRV -> SPIR-V -> D3D11/Vulkan) does not
// support GLSL "default block" loose uniforms - every uniform must live in an
// explicit std140 uniform block with an explicit binding, matching a
// ResourceLayout created on the C# side in the same declaration order. This
// replaces the old individual `uniform mat4 uMVP;` etc. declarations with a
// single MeshUniforms block (see core/render/VeldridMeshUniforms.cs for the
// matching C# struct - field order/padding must stay in sync with this block).
layout(set = 0, binding = 0, std140) uniform MeshUniforms {
    mat4  uMVP;
    mat4  uModel;
    vec2  uTexOffset;
    float uTexScaleV;
    float _meshPad0;
    vec2  uTexUvOffset;
    vec2  uTexUvRepeat;
    vec2  uTexUvMirror;
    int   uIsSkinned;
    int   uUseInstancing;
    vec2  _meshPad1;
};

layout(set = 1, binding = 0, std140) uniform SceneData {
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
    int   uUseShadowMap;
    int   _scenePad0;
    int   _scenePad1;
    int   _scenePad2;
};

layout(location = 0) out vec3 vNormal;
layout(location = 1) out vec3 vFragPos;
layout(location = 2) out vec2 vTexCoord;
layout(location = 3) out vec4 vShadowCoord;

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
    // Skinning/instancing intentionally not applied yet - see migration note
    // above. uModel is always used regardless of uIsSkinned/uUseInstancing.
    mat4 modelMat = uModel;
    vec4 pos    = vec4(aPos, 1.0);
    vec3 normal = aNormal;

    vec4 worldPos   = modelMat * pos;
    vFragPos        = worldPos.xyz;
    vNormal         = normalize(mat3(transpose(inverse(modelMat))) * normal);
    vec2 baseUv     = vec2(aTexCoord.x, aTexCoord.y * uTexScaleV + uTexOffset.y);
    vTexCoord       = applyUvTransform(baseUv);
    vShadowCoord    = uLightSpaceMatrix * worldPos;
    gl_Position     = uMVP * pos;
}
