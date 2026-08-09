#version 330 core

in vec3 vNormal;
in vec3 vFragPos;
in vec2 vTexCoord;
in vec4 vShadowCoord;

uniform vec3  uAlbedo;
uniform vec4  uBlendColor;
uniform vec4  uMixColor;
uniform float uAlpha;
uniform bool  uEmissionEnabled;
uniform vec3  uEmissionColor;
uniform float uEmissionEnergy;
uniform float uSSS;
uniform vec3  uSSSRadius;
uniform vec3  uSSSColor;
uniform float uSSSHighlight;
uniform float uSSSHighlightStrength;
uniform bool  uSSSEnabled;
uniform int   uSSSBlurSamples;
uniform float uSSSStrength;
uniform float uSSSDesaturation;
uniform float uSSSColorThreshold;
uniform vec3  uSSSGlobalRadius;
uniform float uSSSHighlightSize;
uniform float uSSSGlobalHighlightStrength;
uniform float uSSSHighlightSharpness;
uniform float uSSSHighlightDesaturation;
uniform float uSSSHighlightColorThreshold;
uniform float uSSSAbsorption;
uniform sampler2D uShadowMap;
uniform bool  uUseShadowMap;
uniform float uShadowBlurStrength;

uniform bool  uIsUnlit;
uniform vec3 uCameraPosition, uFogColor, uHeightFogColor;
uniform bool uFogEnabled, uHeightFogEnabled;
uniform float uFogDistance, uFogFadeSize, uFogHeight, uHeightFogSize, uHeightFogOffset;

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

uniform sampler2D uTexture;
uniform bool      uUseTexture;
uniform sampler2D uAlphaMask;
uniform bool      uUseAlphaMask;

#define MAX_POINT_LIGHTS 32
#define MAX_POINT_SHADOWS 8

uniform int   uPointLightCount;
uniform vec3  uPointLightPos[MAX_POINT_LIGHTS];
uniform vec3  uPointLightColor[MAX_POINT_LIGHTS];
uniform float uPointLightRange[MAX_POINT_LIGHTS];
uniform float uPointLightEnergy[MAX_POINT_LIGHTS];
uniform float uPointLightIndirectEnergy[MAX_POINT_LIGHTS];
uniform int   uPointLightShadowIndex[MAX_POINT_LIGHTS];
uniform vec3  uPointLightDir[MAX_POINT_LIGHTS];
uniform float uPointLightSpotCosOuter[MAX_POINT_LIGHTS];
uniform float uPointLightSpotCosInner[MAX_POINT_LIGHTS];
uniform samplerCube uPointShadowMaps[MAX_POINT_SHADOWS];
uniform bool  uIndirectWorldEnabled;
uniform float uIndirectWorldStrength;

out vec4 FragColor;

float calculateShadow(vec3 norm, vec3 lightDir) {
    if (!uUseShadowMap) return 0.0;

    vec3 projCoords = vShadowCoord.xyz / max(vShadowCoord.w, 0.0001);
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float currentDepth = projCoords.z;
    float bias = max(0.0025 * (1.0 - dot(norm, lightDir)), 0.0007);
    vec2 texelSize = 1.0 / vec2(textureSize(uShadowMap, 0));

    float shadow = 0.0;
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            float pcfDepth = texture(uShadowMap, projCoords.xy + vec2(x, y) * texelSize * uShadowBlurStrength).r;
            shadow += currentDepth - bias > pcfDepth ? 1.0 : 0.0;
        }
    }

    return shadow / 9.0;
}

float samplePointShadowCube(int shadowIndex, vec3 lightToFrag)
{
    if (shadowIndex == 0) return texture(uPointShadowMaps[0], lightToFrag).r;
    if (shadowIndex == 1) return texture(uPointShadowMaps[1], lightToFrag).r;
    if (shadowIndex == 2) return texture(uPointShadowMaps[2], lightToFrag).r;
    if (shadowIndex == 3) return texture(uPointShadowMaps[3], lightToFrag).r;
    if (shadowIndex == 4) return texture(uPointShadowMaps[4], lightToFrag).r;
    if (shadowIndex == 5) return texture(uPointShadowMaps[5], lightToFrag).r;
    if (shadowIndex == 6) return texture(uPointShadowMaps[6], lightToFrag).r;
    if (shadowIndex == 7) return texture(uPointShadowMaps[7], lightToFrag).r;
    return 1.0;
}

float calculatePointShadow(int shadowIndex, vec3 fragPos, vec3 lightPos, float farPlane, vec3 norm, vec3 lightDir)
{
    if (shadowIndex < 0 || shadowIndex >= MAX_POINT_SHADOWS) return 0.0;

    vec3 lightToFrag = fragPos - lightPos;
    float currentDepth = length(lightToFrag);
    if (currentDepth <= 0.0001 || currentDepth >= farPlane) return 0.0;

    float closestDepth = samplePointShadowCube(shadowIndex, lightToFrag) * farPlane;
    float bias = max(0.05 * (1.0 - dot(norm, lightDir)), 0.02);
    return currentDepth - bias > closestDepth ? 1.0 : 0.0;
}

float CSPhase(float dotView, float scatter)
{
    float g = scatter;
    float g2 = g * g;
    float denom = 2.0 * (2.0 + g2) * pow(max(1.0 + g2 - 2.0 * g * dotView, 0.0001), 1.5);
    return (3.0 * (1.0 - g2) * (1.0 + dotView)) / max(denom, 0.0001);
}

float computeSssRim(float ndotView, float backLighting)
{
    float edge = clamp(1.0 - ndotView, 0.0, 1.0);
    float sizeFactor = max(uSSSHighlightSize, 0.001);
    float rimBase = pow(edge, 1.0 / sizeFactor);
    float rimSharp = pow(rimBase, max(uSSSHighlightSharpness, 0.01));
    return rimSharp * clamp(backLighting, 0.0, 1.0);
}

vec3 estimateDirectionalSubsurface(vec3 norm, vec3 lightDir, vec3 lightColor, float sss)
{
    if (!uUseShadowMap || sss <= 0.0)
        return vec3(0.0);

    vec3 projCoords = vShadowCoord.xyz / max(vShadowCoord.w, 0.0001);
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return vec3(0.0);

    float currentDepth = projCoords.z;
    vec2 texelSize = 1.0 / vec2(textureSize(uShadowMap, 0));
    int sampleCount = clamp(uSSSBlurSamples, 0, 32);
    if (sampleCount <= 0)
        sampleCount = 1;

    float sampleDepth = 0.0;
    float weightSum = 0.0;
    for (int i = 0; i < 32; i++) {
        if (i >= sampleCount)
            break;

        float fi = float(i);
        float radius = sqrt(fi + 0.5) / sqrt(float(sampleCount));
        float theta = fi * 2.399963;
        vec2 disk = vec2(cos(theta), sin(theta));
        vec2 offset = disk * radius * texelSize * max(uShadowBlurStrength, 0.0001);
        float w = 1.0 - radius;
        sampleDepth += texture(uShadowMap, projCoords.xy + offset).r * w;
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

vec3 estimatePointSubsurface(
    vec3 norm,
    vec3 lightDir,
    vec3 fragPos,
    vec3 lightPos,
    vec3 lightColor,
    float lightEnergy,
    float farPlane,
    int shadowIndex,
    float sss)
{
    if (shadowIndex < 0 || shadowIndex >= MAX_POINT_SHADOWS || sss <= 0.0)
        return vec3(0.0);

    vec3 lightToFrag = fragPos - lightPos;
    float currentDepth = length(lightToFrag);
    if (currentDepth <= 0.0001 || currentDepth >= farPlane)
        return vec3(0.0);

    float closestDepth = samplePointShadowCube(shadowIndex, lightToFrag) * farPlane;
    float bias = max(0.05 * (1.0 - dot(norm, lightDir)), 0.02);

    vec3 rad = max((uSSSRadius * uSSSGlobalRadius) * sss, vec3(0.0001));
    vec3 invLight = 1.0 / (max(lightColor * max(lightEnergy, 0.0001), vec3(0.0001)) * rad + 0.001);
    vec3 dis = max((currentDepth + bias) - closestDepth, 0.0) * invLight;
    vec3 base = pow(max(1.0 - pow(dis / rad, vec3(4.0)), 0.0), vec3(2.0))
              / (pow(dis, vec3(2.0)) + 1.0);

    return base;
}

void main() {
    vec3  baseColor = uAlbedo;
    vec3  emissionMask = vec3(1.0);
    float alpha     = 1.0;

    if (uUseTexture) {
        vec4 texSample = texture(uTexture, vTexCoord);
        emissionMask = texSample.rgb;
        baseColor = texSample.rgb * uAlbedo;
        alpha     = texSample.a;
    }
    if (uUseAlphaMask)
        alpha *= texture(uAlphaMask, vTexCoord).a;

    baseColor *= uBlendColor.rgb;
    baseColor = mix(baseColor, uMixColor.rgb, clamp(uMixColor.a, 0.0, 1.0));
    alpha *= uAlpha * uBlendColor.a;

    // Modelbench discards only completely transparent fragments. Even very low
    // non-zero alpha must survive so it can write depth for render-depth masks.
    if (alpha <= 0.0) discard;

    vec3 result = baseColor;

    if (!uIsUnlit) {
    vec3 norm    = gl_FrontFacing ? normalize(vNormal) : -normalize(vNormal);
    vec3 sunDir  = normalize(uLightDir);
    float diff   = max(dot(norm, sunDir), 0.0);
    vec3 diffuse = diff * uLightColor;
    float sssAmount = (uSSSEnabled ? clamp(uSSS, 0.0, 1.0) : 0.0);
    vec3 viewDir = normalize(vFragPos - uCameraPosition);
    vec3 cameraDir = normalize(uCameraPosition - vFragPos);
    float ndotView = max(dot(norm, cameraDir), 0.0);

    vec3 pointLightSum = vec3(0.0);
    vec3 pointIndirectSum = vec3(0.0);
    for (int i = 0; i < uPointLightCount; i++) {
        vec3  toLight   = uPointLightPos[i] - vFragPos;
        float dist      = length(toLight);
        float range     = uPointLightRange[i];

        if (dist >= range) continue;

        float attenuation = clamp(1.0 - (dist / range), 0.0, 1.0);
        attenuation *= attenuation;

        float spot = 1.0;
        if (uPointLightSpotCosOuter[i] > 0.0) {
            vec3  toFragDir   = toLight / max(dist, 0.0001);
            float cosToFrag   = dot(-toFragDir, uPointLightDir[i]);
            if (cosToFrag <= uPointLightSpotCosInner[i]) {
                continue;
            }
            if (cosToFrag < uPointLightSpotCosOuter[i]) {
                float t = (cosToFrag - uPointLightSpotCosInner[i])
                        / max(uPointLightSpotCosOuter[i] - uPointLightSpotCosInner[i], 0.0001);
                spot = smoothstep(0.0, 1.0, t);
            }
        }

        vec3 lightDir  = normalize(toLight);
        float diffFact = max(dot(norm, lightDir), 0.0);
        float pointShadow = calculatePointShadow(uPointLightShadowIndex[i], vFragPos, uPointLightPos[i], range, norm, lightDir);

        pointLightSum += uPointLightColor[i] * diffFact * attenuation * spot * uPointLightEnergy[i] * (1.0 - pointShadow);

        if (sssAmount > 0.0) {
            vec3 pointSubsurf = estimatePointSubsurface(
                norm,
                lightDir,
                vFragPos,
                uPointLightPos[i],
                uPointLightColor[i],
                uPointLightEnergy[i],
                range,
                uPointLightShadowIndex[i],
                sssAmount);
            pointSubsurf *= attenuation * spot;
            float transDif = max(0.0, dot(normalize(-norm), lightDir));
            vec3 pointSssTint = mix(uPointLightColor[i], vec3(1.0), clamp(uSSSColorThreshold, 0.0, 1.0));
            float pointSssLuma = dot(pointSssTint, vec3(0.299, 0.587, 0.114));
            pointSssTint = mix(pointSssTint, vec3(pointSssLuma), clamp(uSSSDesaturation, 0.0, 1.0));
            pointSubsurf *= max(uSSSStrength, 0.0);

            float phase = CSPhase(dot(viewDir, lightDir), uSSSAbsorption);
            float sizeFactor = max(uSSSHighlightSize, 0.001);
            float highlightMask = pow(transDif, 1.0 / sizeFactor);
            float highlightShape = pow(max(highlightMask, 0.0), max(uSSSHighlightSharpness, 0.01));
            float rim = computeSssRim(ndotView, transDif);
            vec3 pointHighlightTint = mix(uPointLightColor[i], vec3(1.0), clamp(uSSSHighlightColorThreshold, 0.0, 1.0));
            float pointHighlightLuma = dot(pointHighlightTint, vec3(0.299, 0.587, 0.114));
            pointHighlightTint = mix(pointHighlightTint, vec3(pointHighlightLuma), clamp(uSSSHighlightDesaturation, 0.0, 1.0));

            pointSubsurf += pointSubsurf * (uSSSHighlightStrength * max(uSSSGlobalHighlightStrength, 0.0)) * phase * highlightShape;
            pointLightSum += pointSssTint * max(uPointLightEnergy[i], 0.0) * uSSSColor * transDif * pointSubsurf;
            pointLightSum += pointHighlightTint * (uSSSHighlightStrength * max(uSSSGlobalHighlightStrength, 0.0)) * phase * highlightShape * 0.25;
            pointLightSum += pointHighlightTint * (uSSSHighlightStrength * max(uSSSGlobalHighlightStrength, 0.0)) * rim * 0.35;
        }

        if (uIndirectWorldEnabled) {
            float indirectEnergy = max(uPointLightIndirectEnergy[i], 0.0);
            float diffuseWrap = max(dot(norm, lightDir) * 0.5 + 0.5, 0.0);
            float shadowVisibility = 1.0 - (pointShadow * 0.5);
            pointIndirectSum += uPointLightColor[i] * indirectEnergy * attenuation * spot * diffuseWrap * shadowVisibility;
        }
    }

    vec3 shadowLightDir = uMoonFillLightCastsShadows != 0 ? normalize(uMoonFillLightDir)
                        : uSunFillLightCastsShadows  != 0 ? normalize(uSunFillLightDir)
                        : sunDir;
    float shadow = calculateShadow(norm, shadowLightDir);

    if (uShadowDebugMode == 1) {
        FragColor = uUseShadowMap
            ? vec4(vec3(shadow), 1.0)
            : vec4(1.0, 0.0, 1.0, 1.0);
        return;
    }

    float sunFillDiffuse = max(dot(norm, normalize(uSunFillLightDir)), 0.0);
    float moonFillDiffuse = max(dot(norm, normalize(uMoonFillLightDir)), 0.0);
    float mainVisibility = uMainLightCastsShadows != 0 ? (1.0 - shadow) : 1.0;
    float sunVisibility = uSunFillLightCastsShadows != 0 ? (1.0 - shadow) : 1.0;
    float moonVisibility = uMoonFillLightCastsShadows != 0 ? (1.0 - shadow) : 1.0;
    result = (uAmbient + diffuse * mainVisibility + uSunFillLightColor * sunFillDiffuse * sunVisibility + uMoonFillLightColor * moonFillDiffuse * moonVisibility + pointLightSum) * baseColor;

    if (sssAmount > 0.0) {
        vec3 subsurf = estimateDirectionalSubsurface(norm, shadowLightDir, uLightColor, sssAmount);
        float transDif = max(0.0, dot(normalize(-norm), sunDir));
        vec3 sssTint = mix(uLightColor, vec3(1.0), clamp(uSSSColorThreshold, 0.0, 1.0));
        float sssLuma = dot(sssTint, vec3(0.299, 0.587, 0.114));
        sssTint = mix(sssTint, vec3(sssLuma), clamp(uSSSDesaturation, 0.0, 1.0));
        subsurf *= max(uSSSStrength, 0.0);

        float phase = CSPhase(dot(viewDir, sunDir), uSSSAbsorption);
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

    if (uIndirectWorldEnabled) {
        result += baseColor * pointIndirectSum * max(uIndirectWorldStrength, 0.0);
    }
    if (uEmissionEnabled) {
        result += (uEmissionColor * emissionMask) * max(uEmissionEnergy, 0.0);
    }
    }
    if (uFogEnabled) {
        float distancePixels = length(vFragPos - uCameraPosition) * 64.0;
        float distanceFog = smoothstep(max(uFogDistance - uFogFadeSize, 0.0), max(uFogDistance, 1.0), distancePixels);
        result = mix(result, uFogColor, distanceFog);
        if (uHeightFogEnabled) {
            float worldHeightPixels = vFragPos.y * 64.0;
            float heightStart = uFogHeight + uHeightFogOffset;
            float heightFog = 1.0 - smoothstep(heightStart, heightStart + max(uHeightFogSize, 1.0), worldHeightPixels);
            result = mix(result, uHeightFogColor, heightFog);
        }
    }
    FragColor   = vec4(result, alpha);
}
