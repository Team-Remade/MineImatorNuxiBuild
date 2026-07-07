#version 330 core

in vec3 vNormal;
in vec3 vFragPos;
in vec2 vTexCoord;
in vec4 vShadowCoord;

uniform vec3  uAlbedo;
uniform float uAlpha;
uniform bool  uEmissionEnabled;
uniform vec3  uEmissionColor;
uniform float uEmissionEnergy;
uniform vec3  uLightDir;
uniform vec3  uLightColor;
uniform vec3  uAmbient;
uniform sampler2D uShadowMap;
uniform bool  uUseShadowMap;
uniform int   uShadowDebugMode;

// Texture support
uniform sampler2D uTexture;
uniform bool      uUseTexture;

// ── Point lights ─────────────────────────────────────────────────────────────
// Maximum number of point lights processed per draw call.
#define MAX_POINT_LIGHTS 16
#define MAX_POINT_SHADOWS 4

uniform int   uPointLightCount;
uniform vec3  uPointLightPos[MAX_POINT_LIGHTS];
uniform vec3  uPointLightColor[MAX_POINT_LIGHTS];
uniform float uPointLightRange[MAX_POINT_LIGHTS];
uniform float uPointLightEnergy[MAX_POINT_LIGHTS];
uniform int   uPointLightShadowIndex[MAX_POINT_LIGHTS];
uniform samplerCube uPointShadowMaps[MAX_POINT_SHADOWS];

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
            float pcfDepth = texture(uShadowMap, projCoords.xy + vec2(x, y) * texelSize).r;
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

void main() {
    vec3 norm    = normalize(vNormal);
    vec3 sunDir  = normalize(uLightDir);
    float diff   = max(dot(norm, sunDir), 0.0);
    vec3 diffuse = diff * uLightColor;

    vec3  baseColor = uAlbedo;
    float alpha     = 1.0;

    if (uUseTexture) {
        vec4 texSample = texture(uTexture, vTexCoord);
        baseColor = texSample.rgb;
        alpha     = texSample.a;
    }

    // Multiply by the per-mesh alpha so transparency works for both
    // textured and flat-colour meshes.
    alpha *= uAlpha;

    // Accumulate point-light contributions.
    vec3 pointLightSum = vec3(0.0);
    for (int i = 0; i < uPointLightCount; i++) {
        vec3  toLight   = uPointLightPos[i] - vFragPos;
        float dist      = length(toLight);
        float range     = uPointLightRange[i];

        // Skip if the fragment is outside the light's range.
        if (dist >= range) continue;

        // Smooth quadratic falloff: 1 at center, 0 at range boundary.
        float attenuation = clamp(1.0 - (dist / range), 0.0, 1.0);
        attenuation *= attenuation;

        vec3 lightDir  = normalize(toLight);
        float diffFact = max(dot(norm, lightDir), 0.0);
        float pointShadow = calculatePointShadow(uPointLightShadowIndex[i], vFragPos, uPointLightPos[i], range, norm, lightDir);

        pointLightSum += uPointLightColor[i] * diffFact * attenuation * uPointLightEnergy[i] * (1.0 - pointShadow);
    }

    // Discard fully-transparent fragments so they don't write to the depth
    // buffer during the pre-pass, preventing transparent item-model pixels
    // from clipping geometry drawn afterwards (e.g. light billboards).
    if (alpha < 0.01) discard;

    float shadow = calculateShadow(norm, sunDir);
    if (uShadowDebugMode == 1) {
        FragColor = uUseShadowMap
            ? vec4(vec3(shadow), 1.0)
            : vec4(1.0, 0.0, 1.0, 1.0);
        return;
    }

    vec3 result = (uAmbient + diffuse * (1.0 - shadow) + pointLightSum) * baseColor;
    if (uEmissionEnabled) {
        result += uEmissionColor * max(uEmissionEnergy, 0.0);
    }
    FragColor   = vec4(result, alpha);
}
