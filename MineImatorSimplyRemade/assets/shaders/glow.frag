#version 450

// MIGRATION NOTE (subsystem pass 5/N - "glow/film-grain/picking/overlays"):
// three-pass bloom (threshold-extract -> separable blur x2 -> additive
// composite), driven as a full-screen pass (see fullscreen.vert) by
// VeldridGlowPass, which ping-pongs between two scratch textures for the
// extract/blur stages before compositing additively onto the main scene.
layout(set = 0, binding = 0, std140) uniform GlowUniforms {
    vec2  uTexelSize;
    float uStrength;
    float uSize;
    vec2  uDirection;
    int   uMode;
    int   _pad0;
};

layout(set = 0, binding = 1) uniform texture2D uSceneTexture;
layout(set = 0, binding = 2) uniform sampler uSceneSampler;

layout(location = 0) out vec4 FragColor;

vec3 ExtractBright(vec3 color)
{
    float brightness = max(max(color.r, color.g), color.b);
    float threshold = 0.55;
    float softness = 0.10;
    float mask = smoothstep(threshold - softness, threshold + softness, brightness);
    return color * mask;
}

void main()
{
    vec2 uv = gl_FragCoord.xy * uTexelSize;

    if (uMode == 2)
    {
        FragColor = vec4(texture(sampler2D(uSceneTexture, uSceneSampler), uv).rgb * max(uStrength, 0.0), 1.0);
        return;
    }

    if (uMode == 0)
    {
        FragColor = vec4(ExtractBright(texture(sampler2D(uSceneTexture, uSceneSampler), uv).rgb), 1.0);
        return;
    }

    vec2 axis = normalize(uDirection);
    if (length(axis) < 0.5)
        axis = vec2(1.0, 0.0);

    vec2 stepUv = axis * (max(uSize, 0.0) * uTexelSize);

    float w0 = 0.22702703;
    float w1 = 0.19459459;
    float w2 = 0.12162162;
    float w3 = 0.05405405;
    float w4 = 0.01621622;

    vec3 blur = texture(sampler2D(uSceneTexture, uSceneSampler), uv).rgb * w0;
    blur += texture(sampler2D(uSceneTexture, uSceneSampler), uv + stepUv * 1.0).rgb * w1;
    blur += texture(sampler2D(uSceneTexture, uSceneSampler), uv - stepUv * 1.0).rgb * w1;
    blur += texture(sampler2D(uSceneTexture, uSceneSampler), uv + stepUv * 2.0).rgb * w2;
    blur += texture(sampler2D(uSceneTexture, uSceneSampler), uv - stepUv * 2.0).rgb * w2;
    blur += texture(sampler2D(uSceneTexture, uSceneSampler), uv + stepUv * 3.0).rgb * w3;
    blur += texture(sampler2D(uSceneTexture, uSceneSampler), uv - stepUv * 3.0).rgb * w3;
    blur += texture(sampler2D(uSceneTexture, uSceneSampler), uv + stepUv * 4.0).rgb * w4;
    blur += texture(sampler2D(uSceneTexture, uSceneSampler), uv - stepUv * 4.0).rgb * w4;

    FragColor = vec4(blur, 1.0);
}
