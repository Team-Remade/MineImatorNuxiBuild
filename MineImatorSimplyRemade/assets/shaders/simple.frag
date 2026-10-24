#version 330 core

in vec3 vNormal;
in vec3 vFragPos;
in vec2 vTexCoord;
in vec4 vShadowCoord;

uniform vec3  uAlbedo;
uniform vec4  uBlendColor;
uniform vec4  uMixColor;
uniform float uAlpha;
uniform bool  uAlphaClipEnabled;
uniform float uAlphaClipThreshold;
uniform bool  uBendEnabled;
uniform bool  uBendSharp;
uniform bool  uEmissionEnabled;
uniform vec3  uEmissionColor;
uniform float uEmissionEnergy;
uniform bool  uEmissionIndirectOnly;
uniform float uMetallic;
uniform float uRoughness;
uniform bool  uPbrEnabled;
uniform float uNormalStrength;
uniform float uOcclusionStrength;
uniform float uHeightScale;
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
uniform sampler2D uNormalTexture;
uniform bool      uUseNormalTexture;
uniform sampler2D uEmissionTexture;
uniform bool      uUseEmissionTexture;
uniform sampler2D uSurfaceTexture;
uniform bool      uUseSurfaceTexture;
uniform sampler2D uAlphaMask;
uniform bool      uUseAlphaMask;
uniform bool      uIsTextAlphaMask;
uniform vec4      uTextOutlineColor;

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

layout(location = 0) out vec4 FragColor;
layout(location = 1) out vec4 GiAlbedo;
layout(location = 2) out vec4 GiRadiance;

// Match the display-encoded scene / linear GI convention used by indirect_denoise.frag.
vec3 toLinear(vec3 color) { return pow(max(color, vec3(0.0)), vec3(2.2)); }
vec3 toDisplay(vec3 color) { return pow(max(color, vec3(0.0)), vec3(1.0 / 2.2)); }

vec3 safeDirection(vec3 direction, vec3 fallback)
{
    float lengthSquared = dot(direction, direction);
    return lengthSquared > 1e-20 ? direction * inversesqrt(lengthSquared) : fallback;
}

vec3 pbrLighting(vec3 normal, vec3 view, vec3 light, vec3 albedo, vec3 diffuseTint,
                 float roughness, float metallic, out vec3 diffuse)
{
    const float PI = 3.14159265359;
    float nDotL = max(dot(normal, light), 0.0);
    float nDotV = max(dot(normal, view), 0.0);
    diffuse = vec3(0.0);
    if (nDotL <= 0.0 || nDotV <= 0.0) return vec3(0.0);
    vec3 halfVector = safeDirection(view + light, normal);
    float nDotH = clamp(dot(normal, halfVector), 0.0, 1.0);
    float vDotH = clamp(dot(view, halfVector), 0.0, 1.0);
    vec3 f0 = mix(vec3(0.04), albedo, metallic);
    vec3 fresnel = f0 + (1.0 - f0) * pow(1.0 - vDotH, 5.0);
    float a = roughness * roughness;
    float a2 = a * a;
    // This form avoids cancellation at nDotH=1 with small roughness.
    float denominator = (1.0 - nDotH * nDotH) + nDotH * nDotH * a2;
    float distribution = a2 / max(PI * denominator * denominator, 1e-12);
    // Height-correlated Smith GGX visibility, already divided by 4*NdotL*NdotV.
    float smithV = nDotL * sqrt(nDotV * nDotV * (1.0 - a2) + a2);
    float smithL = nDotV * sqrt(nDotL * nDotL * (1.0 - a2) + a2);
    float visibility = 0.5 / max(smithV + smithL, 1e-6);
    diffuse = (1.0 - fresnel) * (1.0 - metallic) * albedo * diffuseTint * (nDotL / PI);
    return diffuse + distribution * visibility * fresnel * nDotL;
}

vec4 shadowBilinearWeights(vec2 fraction)
{
    return vec4((1.0 - fraction.x) * (1.0 - fraction.y),
                fraction.x * (1.0 - fraction.y),
                (1.0 - fraction.x) * fraction.y,
                fraction.x * fraction.y);
}

vec4 directionalShadowDepths(vec2 uv, out vec4 weights)
{
    vec2 size = vec2(textureSize(uShadowMap, 0));
    vec2 texel = uv * size - 0.5;
    vec2 baseUv = (floor(texel) + 0.5) / size;
    vec2 stepUv = 1.0 / size;
    weights = shadowBilinearWeights(fract(texel));
    return vec4(texture(uShadowMap, baseUv).r,
                texture(uShadowMap, baseUv + vec2(stepUv.x, 0.0)).r,
                texture(uShadowMap, baseUv + vec2(0.0, stepUv.y)).r,
                texture(uShadowMap, baseUv + stepUv).r);
}

float calculateShadow(vec3 norm, vec3 lightDir) {
    if (!uUseShadowMap) return 0.0;

    vec3 projCoords = vShadowCoord.xyz / max(vShadowCoord.w, 0.0001);
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.z < 0.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return 0.0;

    float currentDepth = projCoords.z;
    float bias = max(0.0025 * (1.0 - dot(norm, lightDir)), 0.0007);
    vec2 texelSize = 1.0 / vec2(textureSize(uShadowMap, 0));

    float shadow = 0.0;
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            vec2 sampleUv = projCoords.xy + vec2(x, y) * texelSize * uShadowBlurStrength;
            if (sampleUv.x < 0.0 || sampleUv.x > 1.0 || sampleUv.y < 0.0 || sampleUv.y > 1.0)
                continue;

            vec4 weights;
            vec4 depths = directionalShadowDepths(sampleUv, weights);
            // Interpolate visibility, not depth, so texel edges do not become hard steps.
            shadow += dot(vec4(lessThan(depths, vec4(currentDepth - bias))), weights);
        }
    }

    shadow /= 9.0;

    // Smoothly fade out shadow at the boundaries of the light space frustum
    vec2 edgeDist = min(projCoords.xy, vec2(1.0) - projCoords.xy);
    float edgeFade = clamp(min(edgeDist.x, edgeDist.y) * 20.0, 0.0, 1.0);
    return shadow * edgeFade;
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

float pointShadowMapSize(int shadowIndex)
{
    if (shadowIndex == 0) return float(textureSize(uPointShadowMaps[0], 0).x);
    if (shadowIndex == 1) return float(textureSize(uPointShadowMaps[1], 0).x);
    if (shadowIndex == 2) return float(textureSize(uPointShadowMaps[2], 0).x);
    if (shadowIndex == 3) return float(textureSize(uPointShadowMaps[3], 0).x);
    if (shadowIndex == 4) return float(textureSize(uPointShadowMaps[4], 0).x);
    if (shadowIndex == 5) return float(textureSize(uPointShadowMaps[5], 0).x);
    if (shadowIndex == 6) return float(textureSize(uPointShadowMaps[6], 0).x);
    if (shadowIndex == 7) return float(textureSize(uPointShadowMaps[7], 0).x);
    return 1.0;
}

vec4 pointShadowDepths(int shadowIndex, vec3 direction, float size, out vec4 weights)
{
    vec3 magnitude = abs(direction);
    vec3 face = direction / max(max(magnitude.x, magnitude.y), magnitude.z);
    vec3 axisU;
    vec3 axisV;
    if (magnitude.x >= magnitude.y && magnitude.x >= magnitude.z) {
        axisU = vec3(0.0, 1.0, 0.0);
        axisV = vec3(0.0, 0.0, 1.0);
    } else if (magnitude.y >= magnitude.z) {
        axisU = vec3(1.0, 0.0, 0.0);
        axisV = vec3(0.0, 0.0, 1.0);
    } else {
        axisU = vec3(1.0, 0.0, 0.0);
        axisV = vec3(0.0, 1.0, 0.0);
    }
    vec2 faceUv = vec2(dot(face, axisU), dot(face, axisV));
    vec2 texel = (faceUv * 0.5 + 0.5) * size - 0.5;
    vec2 center = (floor(texel) + 0.5) * (2.0 / size) - 1.0;
    vec3 baseDirection = face + axisU * (center.x - faceUv.x) + axisV * (center.y - faceUv.y);
    vec3 stepU = axisU * (2.0 / size);
    vec3 stepV = axisV * (2.0 / size);
    weights = shadowBilinearWeights(fract(texel));
    // Directions outside this face naturally sample its neighbor across cube seams.
    return vec4(samplePointShadowCube(shadowIndex, baseDirection),
                samplePointShadowCube(shadowIndex, baseDirection + stepU),
                samplePointShadowCube(shadowIndex, baseDirection + stepV),
                samplePointShadowCube(shadowIndex, baseDirection + stepU + stepV));
}

vec3 pointShadowSampleDirection(vec3 direction, int index, int sampleCount, float size)
{
    vec3 axis = abs(direction.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangent = normalize(cross(axis, direction));
    vec3 bitangent = cross(direction, tangent);
    float radius = sqrt(float(index) / float(sampleCount));
    float angle = float(index) * 2.399963;
    float spread = 2.0 * max(uShadowBlurStrength, 0.0) / size;
    return direction + (tangent * cos(angle) + bitangent * sin(angle)) * radius * spread;
}

float calculatePointShadow(int shadowIndex, vec3 fragPos, vec3 lightPos, float farPlane, vec3 norm, vec3 lightDir)
{
    if (shadowIndex < 0 || shadowIndex >= MAX_POINT_SHADOWS) return 0.0;

    vec3 lightToFrag = fragPos - lightPos;
    float currentDepth = length(lightToFrag);
    if (currentDepth <= 0.0001 || currentDepth >= farPlane) return 0.0;

    float bias = max(0.05 * (1.0 - dot(norm, lightDir)), 0.02);
    float size = pointShadowMapSize(shadowIndex);
    vec3 direction = lightToFrag / currentDepth;
    float shadow = 0.0;
    for (int i = 0; i < 9; i++) {
        vec3 sampleDirection = pointShadowSampleDirection(direction, i, 9, size);
        vec4 weights;
        vec4 depths = pointShadowDepths(shadowIndex, sampleDirection, size, weights) * farPlane;
        shadow += dot(vec4(lessThan(depths, vec4(currentDepth - bias))), weights);
    }
    return shadow / 9.0;
}

float HGPhase(float cosTheta, float scatter)
{
    float g = clamp(scatter, 0.0, 0.99);
    float g2 = g * g;
    return (1.0 - g2) / pow(max(1.0 + g2 - 2.0 * g * cosTheta, 0.001), 1.5);
}

vec3 subsurfaceTransmission(float thickness, float sss)
{
    // Mine-imator authors the radius in pixels, like imported positions (16 per block).
    vec3 radius = max(uSSSRadius * uSSSGlobalRadius * sss, vec3(0.001)) / 16.0;
    return exp(-max(thickness, 0.0) / radius);
}

vec3 filteredSubsurfaceTransmission(vec4 depths, vec4 weights, float currentDepth, float depthRange, float sss)
{
    vec3 transmission = vec3(0.0);
    for (int i = 0; i < 4; i++) {
        // Filter transmission after attenuation; empty texels must not invent entry surfaces.
        if (depths[i] < 1.0)
            transmission += subsurfaceTransmission((currentDepth - depths[i]) * depthRange, sss) * weights[i];
    }
    return transmission;
}

vec3 subsurfaceLighting(vec3 transmission, vec3 norm, vec3 lightDir, vec3 lightColor, vec3 viewDir)
{
    float backLighting = max(dot(-norm, lightDir), 0.0);
    float scatter = clamp((uSSSAbsorption + uSSSHighlight) * 0.5, 0.0, 0.99);
    scatter = pow(scatter, max(uSSSHighlightSize, 0.001));
    float phase = HGPhase(dot(viewDir, lightDir), scatter) / max(HGPhase(1.0, scatter), 0.001);
    phase = pow(clamp(phase, 0.0, 1.0), max(uSSSHighlightSharpness, 0.01));

    // Saturation controls must preserve light energy, especially for black lights.
    float lightLuma = dot(lightColor, vec3(0.299, 0.587, 0.114));
    vec3 tint = mix(lightColor, vec3(lightLuma), clamp(uSSSDesaturation, 0.0, 1.0));
    tint = mix(tint, vec3(lightLuma), clamp(uSSSColorThreshold, 0.0, 1.0));
    float highlightDesaturation = max(uSSSHighlightDesaturation, uSSSHighlightColorThreshold);
    tint = mix(tint, vec3(lightLuma), clamp(highlightDesaturation * phase, 0.0, 1.0));
    float strength = 1.25 * max(uSSSStrength, 0.0) * max(uSSSHighlightStrength, 0.0)
                   * max(uSSSGlobalHighlightStrength, 0.0);
    vec3 sssColor = uPbrEnabled && !uIsUnlit && !uIsTextAlphaMask ? toLinear(uSSSColor) : uSSSColor;
    return tint * sssColor * transmission * backLighting * phase * strength;
}

vec3 estimateDirectionalSubsurface(float sss)
{
    if (!uUseShadowMap || sss <= 0.0)
        return vec3(0.0);

    vec3 projCoords = vShadowCoord.xyz / max(vShadowCoord.w, 0.0001);
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z < 0.0 || projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
        return vec3(0.0);

    float currentDepth = projCoords.z;
    vec2 texelSize = 1.0 / vec2(textureSize(uShadowMap, 0));
    int sampleCount = clamp(uSSSBlurSamples, 0, 32);
    if (sampleCount <= 0)
        sampleCount = 1;

    // Orthographic shadow depths are normalized; recover the actual world distance.
    vec3 depthRow = vec3(uLightSpaceMatrix[0][2], uLightSpaceMatrix[1][2], uLightSpaceMatrix[2][2]);
    float depthRange = 2.0 / max(length(depthRow), 0.000001);
    vec3 transmission = vec3(0.0);
    float weightSum = 0.0;
    for (int i = 0; i < 32; i++) {
        if (i >= sampleCount)
            break;

        float fi = float(i);
        float radius = sampleCount == 1 ? 0.0 : sqrt(fi / float(sampleCount));
        float theta = fi * 2.399963;
        vec2 disk = vec2(cos(theta), sin(theta));
        vec2 offset = disk * radius * texelSize * max(uShadowBlurStrength, 0.0001);
        vec2 sampleUv = projCoords.xy + offset;
        if (sampleUv.x < 0.0 || sampleUv.x > 1.0 || sampleUv.y < 0.0 || sampleUv.y > 1.0)
            continue;

        float w = 1.0 - radius;
        vec4 weights;
        vec4 depths = directionalShadowDepths(sampleUv, weights);
        transmission += filteredSubsurfaceTransmission(depths, weights, currentDepth, depthRange, sss) * w;
        weightSum += w;
    }
    return transmission / max(weightSum, 0.0001);
}

vec3 estimatePointSubsurface(
    vec3 fragPos,
    vec3 lightPos,
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

    float size = pointShadowMapSize(shadowIndex);
    vec3 direction = lightToFrag / currentDepth;
    int sampleCount = clamp(uSSSBlurSamples, 1, 32);
    vec3 transmission = vec3(0.0);
    float weightSum = 0.0;
    for (int i = 0; i < 32; i++) {
        if (i >= sampleCount)
            break;
        vec3 sampleDirection = pointShadowSampleDirection(direction, i, sampleCount, size);
        vec4 weights;
        vec4 depths = pointShadowDepths(shadowIndex, sampleDirection, size, weights);
        float w = 1.0 - sqrt(float(i) / float(sampleCount));
        transmission += filteredSubsurfaceTransmission(depths, weights, currentDepth / farPlane, farPlane, sss) * w;
        weightSum += w;
    }
    return transmission / max(weightSum, 0.0001);
}

bool materialFrame(vec3 normal, out mat3 frame)
{
    frame = mat3(1.0);
    vec3 dpdx = dFdx(vFragPos);
    vec3 dpdy = dFdy(vFragPos);
    vec2 duvdx = dFdx(vTexCoord);
    vec2 duvdy = dFdy(vTexCoord);
    float determinant = duvdx.x * duvdy.y - duvdx.y * duvdy.x;
    // Missing/collinear UVs cannot define a tangent frame.
    float uvScale = sqrt(dot(duvdx, duvdx) * dot(duvdy, duvdy));
    if (abs(determinant) <= max(uvScale * 0.000001, 1e-20)) return false;
    vec3 tangent = (dpdx * duvdy.y - dpdy * duvdx.y) / determinant;
    vec3 bitangent = (dpdy * duvdx.x - dpdx * duvdy.x) / determinant;
    tangent -= normal * dot(normal, tangent);
    if (dot(tangent, tangent) <= 1e-20 || dot(bitangent, bitangent) <= 1e-20) return false;
    tangent = normalize(tangent);
    float handedness = dot(cross(normal, tangent), bitangent);
    if (abs(handedness) <= 1e-20) return false;
    bitangent = cross(normal, tangent) * sign(handedness);
    frame = mat3(tangent, bitangent, normal);
    return true;
}

vec2 materialUv(vec3 normal, vec2 uvDx, vec2 uvDy)
{
    if (!uPbrEnabled || uIsUnlit || uIsTextAlphaMask || !uUseSurfaceTexture || uHeightScale == 0.0)
        return vTexCoord;
    mat3 frame;
    if (!materialFrame(normal, frame)) return vTexCoord;
    vec3 view = transpose(frame) * safeDirection(uCameraPosition - vFragPos, normal);
    float height = textureGrad(uSurfaceTexture, vTexCoord, uvDx, uvDy).a - 0.5;
    // Fade at grazing angles and bound the offset even for malformed/extreme input.
    float grazingFade = smoothstep(0.05, 0.25, abs(view.z));
    vec2 offset = view.xy / max(abs(view.z), 0.2) * height * clamp(uHeightScale, -0.1, 0.1) * grazingFade;
    float offsetLength = length(offset);
    offset *= min(1.0, 0.05 / max(offsetLength, 1e-6));
    return vTexCoord - offset;
}

vec3 mappedNormal(vec3 normal, vec2 uv, vec2 uvDx, vec2 uvDy)
{
    if (!uUseNormalTexture) return normal;
    mat3 frame;
    if (!materialFrame(normal, frame)) return normal;
    vec3 sampleNormal = textureGrad(uNormalTexture, uv, uvDx, uvDy).rgb * 2.0 - 1.0;
    if (uNormalStrength <= 0.0) return normal;
    if (uNormalStrength != 1.0) {
        sampleNormal.xy *= min(uNormalStrength, 8.0);
        sampleNormal.z = max(sampleNormal.z, 0.0001);
    }
    vec3 mapped = frame * sampleNormal;
    return dot(mapped, mapped) > 1e-20 ? normalize(mapped) : normal;
}

void main() {
    GiAlbedo = vec4(0.0);
    GiRadiance = vec4(0.0);
    if (uShadowDebugMode == 2) {
        float alpha = 1.0;
        if (uUseTexture) {
            alpha = texture(uTexture, vTexCoord).a;
        }
        if (uUseAlphaMask) {
            alpha *= texture(uAlphaMask, vTexCoord).a;
        }
        alpha *= uAlpha * uBlendColor.a;
        if (alpha <= (uAlphaClipEnabled ? uAlphaClipThreshold : 0.0)) discard;

        vec3 n = normalize(vNormal);
        vec3 solidCol;
        if (abs(n.x) >= abs(n.y) && abs(n.x) >= abs(n.z)) {
            solidCol = n.x > 0.0 ? vec3(1.0, 0.25, 0.25) : vec3(0.2, 0.85, 0.95);
        } else if (abs(n.y) >= abs(n.z)) {
            solidCol = n.y > 0.0 ? vec3(0.25, 0.95, 0.25) : vec3(0.95, 0.25, 0.85);
        } else {
            solidCol = n.z > 0.0 ? vec3(0.25, 0.45, 1.0) : vec3(1.0, 0.95, 0.25);
        }
        FragColor = vec4(solidCol, 1.0);
        return;
    }

    bool pbr = uPbrEnabled && !uIsUnlit && !uIsTextAlphaMask;
    // Derivatives must be evaluated before the alpha discard, including for cutouts.
    vec3 vertexNormal = pbr ? safeDirection(vNormal, vec3(0.0, 1.0, 0.0)) : normalize(vNormal);
    if (!uIsUnlit && uBendEnabled && uBendSharp) {
        vec3 flatNormal = cross(dFdx(vFragPos), dFdy(vFragPos));
        flatNormal = pbr ? safeDirection(flatNormal, vertexNormal) : normalize(flatNormal);
        if (dot(flatNormal, vertexNormal) < 0.0) flatNormal = -flatNormal;
        vertexNormal = flatNormal;
    }
    vec2 uvDx = dFdx(vTexCoord);
    vec2 uvDy = dFdy(vTexCoord);
    vec2 uv = materialUv(gl_FrontFacing ? vertexNormal : -vertexNormal, uvDx, uvDy);
    vec3  baseColor = pbr ? toLinear(uAlbedo) : uAlbedo;
    vec3  emissionMask = vec3(1.0);
    float alpha     = 1.0;
    float textFillWeight = 1.0;

    if (uUseTexture) {
        vec4 texSample = pbr ? textureGrad(uTexture, uv, uvDx, uvDy) : texture(uTexture, vTexCoord);
        emissionMask = texSample.rgb;
        baseColor = pbr ? toLinear(texSample.rgb) * toLinear(uAlbedo) : texSample.rgb * uAlbedo;
        // Parallax changes material shading only, not coverage, depth or silhouettes.
        alpha     = pbr && uHeightScale != 0.0 ? texture(uTexture, vTexCoord).a : texSample.a;
    }
    if (uUseEmissionTexture)
        emissionMask = pbr ? textureGrad(uEmissionTexture, uv, uvDx, uvDy).rgb : texture(uEmissionTexture, vTexCoord).rgb;
    if (uUseAlphaMask) {
        vec4 textMask = texture(uAlphaMask, vTexCoord);
        if (uIsTextAlphaMask && textMask.a > 0.0) {
            // R stores glyph-fill coverage while A stores fill + outline.
            // Composite the material texture over the independently coloured
            // outline instead of tinting the outline with the material.
            textFillWeight = clamp(textMask.r / textMask.a, 0.0, 1.0);
        }
        alpha *= textMask.a;
    }

    baseColor *= pbr ? toLinear(uBlendColor.rgb) : uBlendColor.rgb;
    baseColor = mix(baseColor, pbr ? toLinear(uMixColor.rgb) : uMixColor.rgb, clamp(uMixColor.a, 0.0, 1.0));
    if (pbr) baseColor = clamp(baseColor, 0.0, 1.0);
    alpha *= uAlpha * uBlendColor.a;

    if (!uIsUnlit) vertexNormal = mappedNormal(vertexNormal, uv, uvDx, uvDy);

    // With no map the scalar sliders still work; their defaults give roughness=1, metallic=0.
    vec4 surface = pbr && uUseSurfaceTexture ? textureGrad(uSurfaceTexture, uv, uvDx, uvDy) : vec4(1.0, 1.0, 1.0, 0.5);
    float roughness = clamp(uRoughness * surface.g, 0.045, 1.0);
    float metallic = clamp(uMetallic * surface.b, 0.0, 1.0);
    float occlusion = pbr ? mix(1.0, clamp(surface.r, 0.0, 1.0), clamp(uOcclusionStrength, 0.0, 1.0)) : 1.0;

    // Modelbench discards only completely transparent fragments. Even very low
    // non-zero alpha must survive so it can write depth for render-depth masks.
    if (alpha <= (uAlphaClipEnabled ? uAlphaClipThreshold : 0.0)) discard;

    vec3 result = baseColor;
    vec3 linearAlbedo = pbr ? baseColor : pow(max(baseColor, 0.0), vec3(2.2));
    vec3 bounceSource = vec3(0.0);
    float giVisibility = uIsUnlit ? 0.0 : 1.0 - clamp(uMetallic, 0.0, 1.0);
    if (pbr) giVisibility = (1.0 - metallic) * occlusion;

    if (!uIsUnlit) {
    vec3 norm    = gl_FrontFacing ? vertexNormal : -vertexNormal;
    vec3 sunDir  = pbr ? safeDirection(uLightDir, norm) : normalize(uLightDir);
    float diff   = max(dot(norm, sunDir), 0.0);
    vec3 diffuse = diff * uLightColor;
    float sssAmount = uSSSEnabled ? max(uSSS, 0.0) : 0.0;
    vec3 viewDir = pbr ? -safeDirection(uCameraPosition - vFragPos, norm) : normalize(vFragPos - uCameraPosition);
    vec3 sssDirectTint = mix(vec3(1.0), uSSSColor, clamp(sssAmount, 0.0, 1.0));
    vec3 pbrView = safeDirection(uCameraPosition - vFragPos, norm);
    if (pbr) {
        sssDirectTint = mix(vec3(1.0), toLinear(uSSSColor), clamp(sssAmount, 0.0, 1.0));
        viewDir = -pbrView;
    }

    vec3 pointLightSum = vec3(0.0);
    vec3 pointBounceSum = vec3(0.0);
    vec3 pointIndirectSum = vec3(0.0);
    for (int i = 0; i < uPointLightCount; i++) {
        vec3  toLight   = uPointLightPos[i] - vFragPos;
        float dist      = length(toLight);
        float range     = uPointLightRange[i];

        if (dist >= range || (pbr && range <= 0.0)) continue;

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

        vec3 lightDir  = pbr ? safeDirection(toLight, norm) : normalize(toLight);
        float diffFact = max(dot(norm, lightDir), 0.0);
        float pointShadow = calculatePointShadow(uPointLightShadowIndex[i], vFragPos, uPointLightPos[i], range, norm, lightDir);

        vec3 pointDirect = uPointLightColor[i] * diffFact * attenuation * spot * uPointLightEnergy[i] * (1.0 - pointShadow);
        vec3 pointDiffuse = vec3(0.0);
        if (pbr) {
            vec3 radiance = max(uPointLightColor[i], vec3(0.0)) * attenuation * spot
                          * max(uPointLightEnergy[i], 0.0) * (1.0 - pointShadow);
            pointDirect = pbrLighting(norm, pbrView, lightDir, linearAlbedo, sssDirectTint, roughness, metallic, pointDiffuse) * radiance;
            pointDiffuse *= radiance;
        }

        if (sssAmount > 0.0) {
            vec3 pointSubsurf = estimatePointSubsurface(
                vFragPos,
                uPointLightPos[i],
                range,
                uPointLightShadowIndex[i],
                sssAmount);
            vec3 scatter = subsurfaceLighting(pointSubsurf, norm, lightDir, uPointLightColor[i], viewDir)
                         * attenuation * spot * max(uPointLightEnergy[i], 0.0);
            if (pbr) {
                scatter *= linearAlbedo * sssDirectTint * (1.0 - metallic);
                pointDiffuse += scatter;
            }
            pointDirect += scatter;
        }
        pointLightSum += pbr ? pointDirect : pointDirect * sssDirectTint;
        pointBounceSum += (pbr ? pointDiffuse : pointDirect * sssDirectTint) * max(uPointLightIndirectEnergy[i], 0.0);

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
    if (pbr) shadowLightDir = uMoonFillLightCastsShadows != 0 ? safeDirection(uMoonFillLightDir, norm)
                            : uSunFillLightCastsShadows != 0 ? safeDirection(uSunFillLightDir, norm) : sunDir;
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
    vec3 directionalLight = diffuse * mainVisibility + uSunFillLightColor * sunFillDiffuse * sunVisibility
                          + uMoonFillLightColor * moonFillDiffuse * moonVisibility;
    vec3 directionalDiffuse = vec3(0.0);
    if (pbr) {
        vec3 lightDiffuse;
        directionalLight = pbrLighting(norm, pbrView, sunDir, linearAlbedo, sssDirectTint, roughness, metallic, lightDiffuse)
                         * max(uLightColor, vec3(0.0)) * mainVisibility;
        directionalDiffuse += lightDiffuse * max(uLightColor, vec3(0.0)) * mainVisibility;
        directionalLight += pbrLighting(norm, pbrView, safeDirection(uSunFillLightDir, norm), linearAlbedo, sssDirectTint, roughness, metallic, lightDiffuse)
                          * max(uSunFillLightColor, vec3(0.0)) * sunVisibility;
        directionalDiffuse += lightDiffuse * max(uSunFillLightColor, vec3(0.0)) * sunVisibility;
        directionalLight += pbrLighting(norm, pbrView, safeDirection(uMoonFillLightDir, norm), linearAlbedo, sssDirectTint, roughness, metallic, lightDiffuse)
                          * max(uMoonFillLightColor, vec3(0.0)) * moonVisibility;
        directionalDiffuse += lightDiffuse * max(uMoonFillLightColor, vec3(0.0)) * moonVisibility;
    }

    if (sssAmount > 0.0) {
        vec3 subsurf = estimateDirectionalSubsurface(sssAmount);
        vec3 scatter = vec3(0.0);
        // There is one directional depth map. Never use it for a different light.
        if (uMoonFillLightCastsShadows != 0)
            scatter = subsurfaceLighting(subsurf, norm, shadowLightDir, uMoonFillLightColor, viewDir);
        else if (uSunFillLightCastsShadows != 0)
            scatter = subsurfaceLighting(subsurf, norm, shadowLightDir, uSunFillLightColor, viewDir);
        else if (uMainLightCastsShadows != 0)
            scatter = subsurfaceLighting(subsurf, norm, shadowLightDir, uLightColor, viewDir);
        if (pbr) {
            scatter *= linearAlbedo * sssDirectTint * (1.0 - metallic);
            directionalDiffuse += scatter;
        }
        directionalLight += scatter;
    }
    result = pbr ? max(uAmbient, vec3(0.0)) * linearAlbedo * (1.0 - metallic) * occlusion + directionalLight + pointLightSum
                 : (uAmbient + directionalLight * sssDirectTint + pointLightSum) * baseColor;
    // GI transports outgoing diffuse light, not ambient, fog or post-processing.
    bounceSource = pbr ? directionalDiffuse + pointBounceSum
                      : (directionalLight * sssDirectTint + pointBounceSum) * linearAlbedo * giVisibility;

    if (uIndirectWorldEnabled) {
        result += baseColor * pointIndirectSum * max(uIndirectWorldStrength, 0.0) * (pbr ? (1.0 - metallic) * occlusion : 1.0);
    }
    }
    if (uEmissionEnabled) {
        vec3 emission = (uEmissionColor * emissionMask) * max(uEmissionEnergy, 0.0);
        if (pbr) emission = toLinear(uEmissionColor) * toLinear(emissionMask) * max(uEmissionEnergy, 0.0);
        if (!uIsUnlit && !uEmissionIndirectOnly) result += emission;
        bounceSource += pbr ? emission : pow(max(uEmissionColor * emissionMask, 0.0), vec3(2.2)) * max(uEmissionEnergy, 0.0);
    }
    if (uFogEnabled) {
        float distancePixels = length(vFragPos - uCameraPosition) * 64.0;
        float distanceFog = smoothstep(max(uFogDistance - uFogFadeSize, 0.0), max(uFogDistance, 1.0), distancePixels);
        result = mix(result, pbr ? toLinear(uFogColor) : uFogColor, distanceFog);
        giVisibility *= 1.0 - distanceFog;
        if (uHeightFogEnabled) {
            float worldHeightPixels = vFragPos.y * 64.0;
            float heightStart = uFogHeight + uHeightFogOffset;
            float heightFog = 1.0 - smoothstep(heightStart, heightStart + max(uHeightFogSize, 1.0), worldHeightPixels);
            result = mix(result, pbr ? toLinear(uHeightFogColor) : uHeightFogColor, heightFog);
            giVisibility *= 1.0 - heightFog;
        }
    }
    // Apply the outline last so material textures, tint/mix, lighting and
    // emission affect only the glyph fill and cannot overwrite its colour.
    if (uIsTextAlphaMask)
        result = mix(uTextOutlineColor.rgb, result, textFillWeight);
    GiAlbedo = vec4(linearAlbedo, giVisibility * textFillWeight * alpha);
    GiRadiance = vec4(bounceSource * textFillWeight * alpha, alpha);
    // Leave HDR range intact; downstream scene effects own exposure/tone mapping.
    FragColor   = vec4(pbr ? toDisplay(result) : result, uAlphaClipEnabled ? 1.0 : alpha);
}
