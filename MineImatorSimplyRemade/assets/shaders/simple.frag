#version 330 core

in vec3 vNormal;
in vec3 vFragPos;
in vec2 vTexCoord;

uniform vec3  uAlbedo;
uniform float uAlpha;
uniform vec3  uLightDir;
uniform vec3  uLightColor;
uniform vec3  uAmbient;

// Texture support
uniform sampler2D uTexture;
uniform bool      uUseTexture;

// ── Point lights ─────────────────────────────────────────────────────────────
// Maximum number of point lights processed per draw call.
#define MAX_POINT_LIGHTS 16

uniform int   uPointLightCount;
uniform vec3  uPointLightPos[MAX_POINT_LIGHTS];
uniform vec3  uPointLightColor[MAX_POINT_LIGHTS];
uniform float uPointLightRange[MAX_POINT_LIGHTS];
uniform float uPointLightEnergy[MAX_POINT_LIGHTS];

out vec4 FragColor;

void main() {
    vec3 norm    = normalize(vNormal);
    float diff   = max(dot(norm, normalize(uLightDir)), 0.0);
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

        pointLightSum += uPointLightColor[i] * diffFact * attenuation * uPointLightEnergy[i];
    }

    // Discard fully-transparent fragments so they don't write to the depth
    // buffer during the pre-pass, preventing transparent item-model pixels
    // from clipping geometry drawn afterwards (e.g. light billboards).
    if (alpha < 0.01) discard;

    vec3 result = (uAmbient + diffuse + pointLightSum) * baseColor;
    FragColor   = vec4(result, alpha);
}
