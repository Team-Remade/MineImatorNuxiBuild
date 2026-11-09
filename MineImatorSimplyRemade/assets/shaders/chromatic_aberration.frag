#version 330 core

uniform sampler2D uScene;
uniform vec2 uResolution;
uniform float uBlurAmount;
uniform bool uDistortRgb;
uniform vec3 uOffsets;

out vec4 FragColor;

vec4 sampleScene(vec2 uv) {
    vec2 pixel = clamp(uv * uResolution - 0.5, vec2(0.0), uResolution - 1.0);
    ivec2 low = ivec2(floor(pixel));
    ivec2 high = min(low + 1, ivec2(uResolution) - 1);
    vec2 fraction = fract(pixel);
    vec4 a = texelFetch(uScene, low, 0);
    vec4 b = texelFetch(uScene, ivec2(high.x, low.y), 0);
    vec4 c = texelFetch(uScene, ivec2(low.x, high.y), 0);
    vec4 d = texelFetch(uScene, high, 0);
    a.rgb *= a.a;
    b.rgb *= b.a;
    c.rgb *= c.a;
    d.rgb *= d.a;
    vec4 color = mix(mix(a, b, fraction.x), mix(c, d, fraction.x), fraction.y);
    if (color.a > 0.0) color.rgb /= color.a;
    return color;
}

float sampleChannel(vec2 uv, int channel, vec2 blurStep) {
    if (uBlurAmount <= 0.0)
        return sampleScene(uv)[channel];

    float color = 0.0;
    float coverage = 0.0;
    const float weights[5] = float[5](1.0, 4.0, 6.0, 4.0, 1.0);
    for (int y = -2; y <= 2; ++y) {
        for (int x = -2; x <= 2; ++x) {
            vec4 sampleColor = sampleScene(uv + vec2(x, y) * blurStep);
            float weight = weights[x + 2] * weights[y + 2] * sampleColor.a;
            color += sampleColor[channel] * weight;
            coverage += weight;
        }
    }
    // Hidden RGB in transparent pixels must not bleed into the blurred channels.
    return coverage > 0.0 ? color / coverage : 0.0;
}

void main() {
    vec2 uv = gl_FragCoord.xy / uResolution;
    vec4 scene = texelFetch(uScene, ivec2(gl_FragCoord.xy), 0);
    vec2 direction = uDistortRgb ? uv - 0.5 : vec2(1.0, 0.0);
    vec3 offsets = clamp(uOffsets, -100.0, 100.0) / 100.0;
    float radius = clamp(uBlurAmount, 0.0, 100.0) / 100.0 * min(uResolution.x, uResolution.y) * 0.02;
    vec2 blurStep = radius * 0.5 / uResolution;
    vec3 color;
    for (int channel = 0; channel < 3; ++channel)
        color[channel] = sampleChannel(uv + direction * offsets[channel], channel, blurStep);
    FragColor = vec4(color, scene.a);
}
