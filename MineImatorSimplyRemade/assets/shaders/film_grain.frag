#version 450

layout(set = 0, binding = 0, std140) uniform FilmGrainUniforms {
    vec2  uResolution;
    float uFrame;
    float uStrength;
    float uSaturation;
    float uSize;
    vec2  _pad0;
};

layout(set = 0, binding = 1) uniform texture2D uSceneTexture;
layout(set = 0, binding = 2) uniform sampler uSceneSampler;

layout(location = 0) out vec4 FragColor;

float hash(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

void main()
{
    vec2 uv = gl_FragCoord.xy / uResolution;
    vec4 scene = texture(sampler2D(uSceneTexture, uSceneSampler), uv);
    vec2 grainCell = floor(gl_FragCoord.xy / max(uSize, 0.25));
    float mono = hash(vec3(grainCell, uFrame + 17.0)) * 2.0 - 1.0;
    vec3 colorNoise = vec3(
        hash(vec3(grainCell + vec2(19.0, 7.0), uFrame + 3.0)),
        hash(vec3(grainCell + vec2(5.0, 23.0), uFrame + 11.0)),
        hash(vec3(grainCell + vec2(29.0, 13.0), uFrame + 29.0))) * 2.0 - 1.0;
    vec3 noise = mix(vec3(mono), colorNoise, clamp(uSaturation, 0.0, 1.0));
    FragColor = vec4(clamp(scene.rgb + noise * uStrength, 0.0, 1.0), scene.a);
}
