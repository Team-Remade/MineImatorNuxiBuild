#version 330 core

out vec4 FragColor;

uniform sampler2D uScene;
uniform vec2 uResolution;
uniform float uFrame;
uniform float uStrength;
uniform float uSaturation;
uniform float uSize;

float hash(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

void main()
{
    vec2 uv = gl_FragCoord.xy / uResolution;
    vec4 scene = texture(uScene, uv);
    vec2 grainCell = floor(gl_FragCoord.xy / max(uSize, 0.25));
    float mono = hash(vec3(grainCell, uFrame + 17.0)) * 2.0 - 1.0;
    vec3 colorNoise = vec3(
        hash(vec3(grainCell + vec2(19.0, 7.0), uFrame + 3.0)),
        hash(vec3(grainCell + vec2(5.0, 23.0), uFrame + 11.0)),
        hash(vec3(grainCell + vec2(29.0, 13.0), uFrame + 29.0))) * 2.0 - 1.0;
    vec3 noise = mix(vec3(mono), colorNoise, clamp(uSaturation, 0.0, 1.0));
    FragColor = vec4(clamp(scene.rgb + noise * uStrength, 0.0, 1.0), scene.a);
}
