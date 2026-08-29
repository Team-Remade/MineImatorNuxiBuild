#version 450

layout(location = 0) in vec3 vNormal;
layout(location = 1) in vec3 vFragPos;
layout(location = 2) in vec2 vTexCoord;
layout(location = 3) in vec4 vShadowCoord;

// MIGRATION NOTE (subsystem pass 2/N - "lighting uniforms"): adds the sun/moon
// fill directional lights and a simplified point-light array (position, range,
// color, energy only - no spot cones or per-light shadow-cubemap indices yet)
// on top of pass 1's base albedo/texture/emission/unlit path. Still NOT ported:
// spot-light cones, per-light shadow sampling, subsurface scattering, and
// distance/height fog - those remain "shadow passes" / SSS+fog follow-up
// subsystem passes (see the old simple.frag in git history for the exact math
// each of those restores).
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
    int   uUseShadowMap;
    int   _scenePad0;
    int   _scenePad1;
    int   _scenePad2;
};

layout(set = 0, binding = 1) uniform sampler2D uTextureSampler;

#define MAX_POINT_LIGHTS 32

// Simplified point-light array for this pass: PosRange.xyz = world position,
// PosRange.w = range; ColorEnergy.rgb = color, ColorEnergy.a = energy
// multiplier. Spot-cone direction/angles and per-light shadow-cubemap index
// (present in the old Mesh.PointLights tuple) are deferred to the shadow
// passes subsystem pass, where they're actually needed.
layout(set = 1, binding = 1, std140) uniform PointLightData {
    int  uPointLightCount;
    int  _plPad0;
    int  _plPad1;
    int  _plPad2;
    vec4 uPointLightPosRange[MAX_POINT_LIGHTS];
    vec4 uPointLightColorEnergy[MAX_POINT_LIGHTS];
};

// Directional shadow map (subsystem pass 3/N). Point-light shadow cubemaps,
// spot cones, and per-light shadow indices are NOT ported yet - that's its
// own follow-up pass (point lights above always render fully unshadowed).
layout(set = 1, binding = 2) uniform texture2D uShadowMapTexture;
layout(set = 1, binding = 3) uniform sampler uShadowMapSampler;

layout(location = 0) out vec4 FragColor;

float calculateShadow(vec3 norm, vec3 lightDir) {
    if (uUseShadowMap == 0) return 0.0;

    vec3 projCoords = vShadowCoord.xyz / max(vShadowCoord.w, 0.0001);
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float currentDepth = projCoords.z;
    float bias = max(0.0025 * (1.0 - dot(norm, lightDir)), 0.0007);

    ivec2 texSize = textureSize(sampler2D(uShadowMapTexture, uShadowMapSampler), 0);
    vec2 texelSize = 1.0 / vec2(texSize);

    float shadow = 0.0;
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            float pcfDepth = texture(sampler2D(uShadowMapTexture, uShadowMapSampler), projCoords.xy + vec2(x, y) * texelSize).r;
            shadow += currentDepth - bias > pcfDepth ? 1.0 : 0.0;
        }
    }

    return shadow / 9.0;
}

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
        // TODO(migration - point-shadow-cubemaps / SSS+fog subsystem passes):
        // spot-cone point lights, per-light shadow cubemaps, subsurface
        // scattering, distance/height fog still missing.
        vec3 norm = gl_FrontFacing ? normalize(vNormal) : -normalize(vNormal);

        float sunDiff  = max(dot(norm, normalize(uLightDir)), 0.0);
        float sunFill  = max(dot(norm, normalize(uSunFillLightDir)), 0.0);
        float moonFill = max(dot(norm, normalize(uMoonFillLightDir)), 0.0);

        // Matches the old renderer: whichever fill light is currently the
        // shadow caster (moon takes priority over sun, main light as a
        // fallback) determines which direction the PCF sample is offset
        // toward, but the resulting shadow factor gates all three lights'
        // visibility independently based on their own *CastsShadows flag.
        vec3 shadowLightDir = uMoonFillLightCastsShadows != 0 ? normalize(uMoonFillLightDir)
                             : uSunFillLightCastsShadows  != 0 ? normalize(uSunFillLightDir)
                             : normalize(uLightDir);
        float shadow = calculateShadow(norm, shadowLightDir);

        if (uShadowDebugMode == 1) {
            FragColor = uUseShadowMap != 0 ? vec4(vec3(shadow), 1.0) : vec4(1.0, 0.0, 1.0, 1.0);
            return;
        }

        float mainVisibility  = uMainLightCastsShadows      != 0 ? (1.0 - shadow) : 1.0;
        float sunVisibility   = uSunFillLightCastsShadows   != 0 ? (1.0 - shadow) : 1.0;
        float moonVisibility  = uMoonFillLightCastsShadows  != 0 ? (1.0 - shadow) : 1.0;

        vec3 lit = uAmbient
                 + sunDiff  * uLightColor        * mainVisibility
                 + sunFill  * uSunFillLightColor * sunVisibility
                 + moonFill * uMoonFillLightColor * moonVisibility;

        for (int i = 0; i < uPointLightCount; i++) {
            vec3  lightPos = uPointLightPosRange[i].xyz;
            float range    = uPointLightPosRange[i].w;
            vec3  toLight  = lightPos - vFragPos;
            float dist     = length(toLight);
            if (dist >= range) continue;

            float attenuation = clamp(1.0 - (dist / range), 0.0, 1.0);
            attenuation *= attenuation;

            vec3 lightDir = normalize(toLight);
            float diff = max(dot(norm, lightDir), 0.0);

            vec3 color  = uPointLightColorEnergy[i].rgb;
            float energy = uPointLightColorEnergy[i].a;
            lit += color * diff * attenuation * energy;
        }

        result = lit * baseColor;
    }

    if (uEmissionEnabled != 0) {
        result += (uEmissionColor * emissionMask) * max(uEmissionEnergy, 0.0);
    }

    FragColor = vec4(result, alpha);
}
