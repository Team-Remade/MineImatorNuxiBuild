#version 450

layout(location = 0) in vec3 vNormal;
layout(location = 1) in vec3 vFragPos;
layout(location = 2) in vec2 vTexCoord;
layout(location = 3) in vec4 vShadowCoord;

// MIGRATION NOTE (subsystem pass 1/N - "basic mesh upload/draw"): this is a
// deliberately reduced port of the old simple.frag. The full fixed-function
// pipeline it replaces also computed: 32 point lights with spot cones and
// per-light shadow cubemaps, directional+point subsurface scattering,
// distance/height fog, and PCF-filtered directional shadows - none of that
// is wired up yet. This pass only ports the base albedo/texture/alpha/
// emission/unlit path so a mesh can be uploaded and drawn at all. The point
// light array, shadow sampling, SSS, and fog blocks/uniforms will be added
// back in the "lighting uniforms" and "shadow passes" follow-up subsystem
// passes, at which point the lit branch below gets its full contribution
// terms back (see the old simple.frag in git history for the exact math).
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

layout(set = 0, binding = 2, std140) uniform MeshMaterial {
    vec3  uAlbedo;
    float uAlpha;
    vec4  uBlendColor;
    vec4  uMixColor;
    int   uUseTexture;
    int   uIsUnlit;
    int   uEmissionEnabled;
    float uEmissionEnergy;
    vec3  uEmissionColor;
    float _matPad0;
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
};

layout(set = 0, binding = 1) uniform sampler2D uTextureSampler;

layout(location = 0) out vec4 FragColor;

void main() {
    vec3 baseColor = uAlbedo;
    vec3 emissionMask = vec3(1.0);
    float alpha = 1.0;

    if (uUseTexture != 0) {
        vec4 texSample = texture(uTextureSampler, vTexCoord);
        emissionMask = texSample.rgb;
        baseColor = texSample.rgb * uAlbedo;
        alpha = texSample.a;
    }

    baseColor *= uBlendColor.rgb;
    baseColor = mix(baseColor, uMixColor.rgb, clamp(uMixColor.a, 0.0, 1.0));
    alpha *= uAlpha * uBlendColor.a;

    if (alpha <= 0.0) discard;

    vec3 result = baseColor;

    if (uIsUnlit == 0) {
        // TODO(migration - lighting uniforms pass): directional sun/moon fill
        // lights, point lights, subsurface scattering. Using scene ambient +
        // a flat head-on directional term as a placeholder so lit meshes are
        // visible (if dim/flat) rather than pure black until that pass lands.
        vec3 norm = gl_FrontFacing ? normalize(vNormal) : -normalize(vNormal);
        float diff = max(dot(norm, normalize(uLightDir)), 0.0);
        result = (uAmbient + diff * uLightColor) * baseColor;
    }

    if (uEmissionEnabled != 0) {
        result += (uEmissionColor * emissionMask) * max(uEmissionEnergy, 0.0);
    }

    FragColor = vec4(result, alpha);
}
