#version 330 core
out vec4 FragColor;
uniform sampler2D uScene;
uniform vec2 uResolution;
uniform float uTime;
uniform float uStrength;
uniform float uSpeed;
uniform float uScale;

float hash(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float noise(vec2 p)
{
    vec2 cell = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(cell), hash(cell + vec2(1.0, 0.0)), f.x),
        mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0)), f.x), f.y);
}

float turbulence(vec2 p)
{
    return (noise(p) + 0.5 * noise(p * 2.03 + vec2(17.1, 9.2))) / 1.5;
}

void main()
{
    vec2 uv = gl_FragCoord.xy / uResolution;
    // Work in shorter-side units so wave size and displacement are aspect-independent.
    float side = min(uResolution.x, uResolution.y);
    vec2 p = (uv - 0.5) * uResolution / side * (12.0 / max(abs(uScale), 0.001));
    p.y -= uTime * uSpeed * 1.5;
    vec2 flow = vec2(turbulence(p), turbulence(p + vec2(37.2, 61.7))) * 2.0 - 1.0;
    vec2 displaced = uv + flow * vec2(1.0, 0.35) * (0.025 * uStrength) * side / uResolution;
    vec2 halfTexel = 0.5 / uResolution;
    FragColor = texture(uScene, clamp(displaced, halfTexel, 1.0 - halfTexel));
}
