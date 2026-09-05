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

layout(set = 0, binding = 3, std140) uniform MeshMaterial {
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
    // Subsystem pass 4/N ("SSS + fog"): per-mesh subsurface scattering amount/
    // radius/tint/highlight, and whether this mesh participates in fog at all
    // (matches the old Mesh.IncludeInFog flag - e.g. UI/gizmo overlays opt out).
    vec3  uSSSRadius;
    float uSSS;
    vec3  uSSSColor;
    float uSSSHighlight;
    float uSSSHighlightStrength;
    float uMetallic;
    float uRoughness;
    int   uIncludeInFog;
    int   _matPad1;
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

layout(set = 0, binding = 1) uniform texture2D uTextureTexture;
layout(set = 0, binding = 2) uniform sampler uTextureSampler;

#define MAX_POINT_LIGHTS 32

// Point-light array. PosRange.xyz = world position, PosRange.w = range;
// ColorEnergy.rgb = color, ColorEnergy.a = energy multiplier; DirFarPlane.xyz =
// spot direction (unit vector; irrelevant for omni point lights), DirFarPlane.w
// = this light's shadow cubemap far plane; SpotShadow.x/.y = cos(outer)/cos(inner)
// spot-cone angles (0 disables the cone test - full sphere), SpotShadow.z =
// shadow-cubemap index (-1 = no shadow, cast to int in the loop below).
layout(set = 1, binding = 1, std140) uniform PointLightData {
    int  uPointLightCount;
    int  _plPad0;
    int  _plPad1;
    int  _plPad2;
    vec4 uPointLightPosRange[MAX_POINT_LIGHTS];
    vec4 uPointLightColorEnergy[MAX_POINT_LIGHTS];
    vec4 uPointLightDirFarPlane[MAX_POINT_LIGHTS];
    vec4 uPointLightSpotShadow[MAX_POINT_LIGHTS];
};

// Directional shadow map (subsystem pass 3/N).
layout(set = 1, binding = 2) uniform texture2D uShadowMapTexture;
layout(set = 1, binding = 3) uniform sampler uShadowMapSampler;

// Global environment - subsurface-scattering multipliers/quality knobs and
// distance/height fog (subsystem pass 4/N). Per-mesh SSS amount/radius/tint
// live in MeshMaterial above; these are the scene-wide settings that used to
// be static fields on the old GL Mesh class (Mesh.FogEnabled, Mesh.SssRadius,
// etc.), now uploaded once per frame like everything else in SceneData.
layout(set = 1, binding = 4, std140) uniform SceneEnvironment {
    vec3  uCameraPosition;
    float _envPad0;
    int   uFogEnabled;
    int   uHeightFogEnabled;
    int   uSSSEnabled;
    int   uSSSBlurSamples;
    vec3  uFogColor;
    float uFogDistance;
    vec3  uHeightFogColor;
    float uFogFadeSize;
    float uFogHeight;
    float uHeightFogSize;
    float uHeightFogOffset;
    float uSSSStrength;
    float uSSSDesaturation;
    float uSSSColorThreshold;
    float uSSSHighlightSize;
    float uSSSGlobalHighlightStrength;
    float uSSSHighlightSharpness;
    float uSSSHighlightDesaturation;
    float uSSSHighlightColorThreshold;
    float uSSSAbsorption;
    vec3  uSSSGlobalRadius;
    float _envPad1;
};

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

float CSPhase(float dotView, float scatter) {
    float g = scatter;
    float g2 = g * g;
    float denom = 2.0 * (2.0 + g2) * pow(max(1.0 + g2 - 2.0 * g * dotView, 0.0001), 1.5);
    return (3.0 * (1.0 - g2) * (1.0 + dotView)) / max(denom, 0.0001);
}

float computeSssRim(float ndotView, float backLighting) {
    float edge = clamp(1.0 - ndotView, 0.0, 1.0);
    float sizeFactor = max(uSSSHighlightSize, 0.001);
    float rimBase = pow(edge, 1.0 / sizeFactor);
    float rimSharp = pow(rimBase, max(uSSSHighlightSharpness, 0.01));
    return rimSharp * clamp(backLighting, 0.0, 1.0);
}

vec3 estimateDirectionalSubsurface(vec3 norm, vec3 lightDir, vec3 lightColor, float sss) {
    if (uUseShadowMap == 0 || sss <= 0.0)
        return vec3(0.0);

    vec3 projCoords = vShadowCoord.xyz / max(vShadowCoord.w, 0.0001);
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return vec3(0.0);

    float currentDepth = projCoords.z;
    ivec2 texSize = textureSize(sampler2D(uShadowMapTexture, uShadowMapSampler), 0);
    vec2 texelSize = 1.0 / vec2(texSize);
    int sampleCount = clamp(uSSSBlurSamples, 0, 32);
    if (sampleCount <= 0) sampleCount = 1;

    float sampleDepth = 0.0;
    float weightSum = 0.0;
    for (int i = 0; i < 32; i++) {
        if (i >= sampleCount) break;

        float fi = float(i);
        float radius = sqrt(fi + 0.5) / sqrt(float(sampleCount));
        float theta = fi * 2.399963;
        vec2 disk = vec2(cos(theta), sin(theta));
        vec2 offset = disk * radius * texelSize;
        float w = 1.0 - radius;
        sampleDepth += texture(sampler2D(uShadowMapTexture, uShadowMapSampler), projCoords.xy + offset).r * w;
        weightSum += w;
    }
    sampleDepth /= max(weightSum, 0.0001);
    float bias = max(0.0025 * (1.0 - dot(norm, lightDir)), 0.0007);

    vec3 rad = max((uSSSRadius * uSSSGlobalRadius) * sss, vec3(0.0001));
    vec3 invLight = 1.0 / (max(lightColor, vec3(0.0001)) * rad + 0.001);
    vec3 dis = max((currentDepth + bias) - sampleDepth, 0.0) * invLight;
    vec3 base = pow(max(1.0 - pow(dis / rad, vec3(4.0)), 0.0), vec3(2.0))
              / (pow(dis, vec3(2.0)) + 1.0);

    return base;
}

vec3 estimatePointSubsurface(vec3 norm, vec3 lightDir, vec3 fragPos, vec3 lightPos, vec3 lightColor,
    float lightEnergy, float farPlane, int shadowIndex, float sss) {
    if (sss <= 0.0)
        return vec3(0.0);

    vec3 lightToFrag = fragPos - lightPos;
    float currentDepth = length(lightToFrag);
    if (currentDepth <= 0.0001 || currentDepth >= farPlane)
        return vec3(0.0);

    vec3 rad = max((uSSSRadius * uSSSGlobalRadius) * sss, vec3(0.0001));
    vec3 invLight = 1.0 / (max(lightColor * max(lightEnergy, 0.0001), vec3(0.0001)) * rad + 0.001);
    vec3 dis = vec3(0.0) * invLight;
    vec3 base = pow(max(1.0 - pow(dis / rad, vec3(4.0)), 0.0), vec3(2.0))
              / (pow(dis, vec3(2.0)) + 1.0);

    return base;
}

vec3 materialSpecular(vec3 base, vec3 norm, vec3 lightDir, vec3 viewDir, vec3 lightColor) {
    float ndotl = max(dot(norm, lightDir), 0.0);
    if (ndotl <= 0.0) return vec3(0.0);

    vec3 halfDir = normalize(lightDir + viewDir);
    float roughness = clamp(uRoughness, 0.04, 1.0);
    float shininess = mix(256.0, 2.0, roughness);
    float highlight = pow(max(dot(norm, halfDir), 0.0), shininess);
    vec3 reflectance = mix(vec3(0.04), base, clamp(uMetallic, 0.0, 1.0));
    return reflectance * highlight * lightColor * ndotl;
}

void main() {
    vec3 baseColor = uAlbedo;
    vec3 emissionMask = vec3(1.0);
    float alpha = 1.0;

    if (uUseTexture != 0) {
        vec4 texSample = texture(sampler2D(uTextureTexture, uTextureSampler), vTexCoord);
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
        // TODO(migration - indirect lighting/AO subsystem pass): screen-space
        // ambient occlusion and point-light indirect bounce are still missing.
        vec3 norm = gl_FrontFacing ? normalize(vNormal) : -normalize(vNormal);

        vec3 cameraDir = normalize(uCameraPosition - vFragPos);
        vec3 mainLightDir = normalize(uLightDir);
        vec3 sunFillDir = normalize(uSunFillLightDir);
        vec3 moonFillDir = normalize(uMoonFillLightDir);
        float sunDiff  = max(dot(norm, mainLightDir), 0.0);
        float sunFill  = max(dot(norm, sunFillDir), 0.0);
        float moonFill = max(dot(norm, moonFillDir), 0.0);

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
        vec3 specular = materialSpecular(baseColor, norm, mainLightDir, cameraDir, uLightColor) * mainVisibility
                  + materialSpecular(baseColor, norm, sunFillDir, cameraDir, uSunFillLightColor) * sunVisibility
                  + materialSpecular(baseColor, norm, moonFillDir, cameraDir, uMoonFillLightColor) * moonVisibility;

        float sssAmount = uSSSEnabled != 0 ? clamp(uSSS, 0.0, 1.0) : 0.0;
        vec3 viewDir = normalize(vFragPos - uCameraPosition);
        float ndotView = max(dot(norm, cameraDir), 0.0);

        for (int i = 0; i < uPointLightCount; i++) {
            vec3  lightPos = uPointLightPosRange[i].xyz;
            float range    = uPointLightPosRange[i].w;
            vec3  toLight  = lightPos - vFragPos;
            float dist     = length(toLight);
            if (dist >= range) continue;

            float attenuation = clamp(1.0 - (dist / range), 0.0, 1.0);
            attenuation *= attenuation;

            float spotCosOuter = uPointLightSpotShadow[i].x;
            float spotCosInner = uPointLightSpotShadow[i].y;
            int   shadowIndex  = int(uPointLightSpotShadow[i].z);

            float spot = 1.0;
            if (spotCosOuter > 0.0) {
                vec3  spotDir   = uPointLightDirFarPlane[i].xyz;
                vec3  toFragDir = toLight / max(dist, 0.0001);
                float cosToFrag = dot(-toFragDir, spotDir);
                if (cosToFrag <= spotCosInner) continue;
                if (cosToFrag < spotCosOuter) {
                    float t = (cosToFrag - spotCosInner) / max(spotCosOuter - spotCosInner, 0.0001);
                    spot = smoothstep(0.0, 1.0, t);
                }
            }

            vec3 lightDir = normalize(toLight);
            float diff = max(dot(norm, lightDir), 0.0);

            float farPlane = uPointLightDirFarPlane[i].w;
            float pointShadow = 0.0;

            vec3 color  = uPointLightColorEnergy[i].rgb;
            float energy = uPointLightColorEnergy[i].a;
            lit += color * diff * attenuation * spot * energy * (1.0 - pointShadow);
            specular += materialSpecular(baseColor, norm, lightDir, cameraDir, color) * attenuation * spot * energy * (1.0 - pointShadow);

            if (sssAmount > 0.0) {
                vec3 pointSubsurf = estimatePointSubsurface(norm, lightDir, vFragPos, lightPos, color, energy, farPlane, shadowIndex, sssAmount);
                pointSubsurf *= attenuation * spot;
                float transDif = max(0.0, dot(normalize(-norm), lightDir));
                vec3 pointSssTint = mix(color, vec3(1.0), clamp(uSSSColorThreshold, 0.0, 1.0));
                float pointSssLuma = dot(pointSssTint, vec3(0.299, 0.587, 0.114));
                pointSssTint = mix(pointSssTint, vec3(pointSssLuma), clamp(uSSSDesaturation, 0.0, 1.0));
                pointSubsurf *= max(uSSSStrength, 0.0);

                float phase = CSPhase(dot(viewDir, lightDir), uSSSAbsorption);
                float sizeFactor = max(uSSSHighlightSize, 0.001);
                float highlightMask = pow(transDif, 1.0 / sizeFactor);
                float highlightShape = pow(max(highlightMask, 0.0), max(uSSSHighlightSharpness, 0.01));
                float rim = computeSssRim(ndotView, transDif);
                vec3 pointHighlightTint = mix(color, vec3(1.0), clamp(uSSSHighlightColorThreshold, 0.0, 1.0));
                float pointHighlightLuma = dot(pointHighlightTint, vec3(0.299, 0.587, 0.114));
                pointHighlightTint = mix(pointHighlightTint, vec3(pointHighlightLuma), clamp(uSSSHighlightDesaturation, 0.0, 1.0));

                pointSubsurf += pointSubsurf * (uSSSHighlightStrength * max(uSSSGlobalHighlightStrength, 0.0)) * phase * highlightShape;
                lit += pointSssTint * max(energy, 0.0) * uSSSColor * transDif * pointSubsurf;
                lit += pointHighlightTint * (uSSSHighlightStrength * max(uSSSGlobalHighlightStrength, 0.0)) * phase * highlightShape * 0.25;
                lit += pointHighlightTint * (uSSSHighlightStrength * max(uSSSGlobalHighlightStrength, 0.0)) * rim * 0.35;
            }
        }

        result = lit * baseColor + specular;

        if (sssAmount > 0.0) {
            vec3 subsurf = estimateDirectionalSubsurface(norm, shadowLightDir, uLightColor, sssAmount);
            float transDif = max(0.0, dot(normalize(-norm), normalize(uLightDir)));
            vec3 sssTint = mix(uLightColor, vec3(1.0), clamp(uSSSColorThreshold, 0.0, 1.0));
            float sssLuma = dot(sssTint, vec3(0.299, 0.587, 0.114));
            sssTint = mix(sssTint, vec3(sssLuma), clamp(uSSSDesaturation, 0.0, 1.0));
            subsurf *= max(uSSSStrength, 0.0);

            float phase = CSPhase(dot(viewDir, normalize(uLightDir)), uSSSAbsorption);
            float sizeFactor = max(uSSSHighlightSize, 0.001);
            float highlightMask = pow(transDif, 1.0 / sizeFactor);
            float highlightShape = pow(max(highlightMask, 0.0), max(uSSSHighlightSharpness, 0.01));
            float rim = computeSssRim(ndotView, transDif);
            vec3 highlightTint = mix(uLightColor, vec3(1.0), clamp(uSSSHighlightColorThreshold, 0.0, 1.0));
            float highlightLuma = dot(highlightTint, vec3(0.299, 0.587, 0.114));
            highlightTint = mix(highlightTint, vec3(highlightLuma), clamp(uSSSHighlightDesaturation, 0.0, 1.0));

            subsurf += subsurf * (uSSSHighlightStrength * max(uSSSGlobalHighlightStrength, 0.0)) * phase * highlightShape;
            result += (sssTint * uSSSColor) * transDif * subsurf;
            result += highlightTint * (uSSSHighlightStrength * max(uSSSGlobalHighlightStrength, 0.0)) * phase * highlightShape * 0.25;
            result += highlightTint * (uSSSHighlightStrength * max(uSSSGlobalHighlightStrength, 0.0)) * rim * 0.35;
            result *= mix(vec3(1.0), uSSSColor, sssAmount);
        }
    }

    if (uEmissionEnabled != 0) {
        result += (uEmissionColor * emissionMask) * max(uEmissionEnergy, 0.0);
    }

    if (uFogEnabled != 0 && uIncludeInFog != 0) {
        float distancePixels = length(vFragPos - uCameraPosition) * 64.0;
        float distanceFog = smoothstep(max(uFogDistance - uFogFadeSize, 0.0), max(uFogDistance, 1.0), distancePixels);
        result = mix(result, uFogColor, distanceFog);
        if (uHeightFogEnabled != 0) {
            float worldHeightPixels = vFragPos.y * 64.0;
            float heightStart = uFogHeight + uHeightFogOffset;
            float heightFog = 1.0 - smoothstep(heightStart, heightStart + max(uHeightFogSize, 1.0), worldHeightPixels);
            result = mix(result, uHeightFogColor, heightFog);
        }
    }

    FragColor = vec4(result, alpha);
}
