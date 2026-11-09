#version 330 core

uniform sampler2D uScene;
uniform vec2 uResolution;
uniform bool uRepeatImage;
uniform float uZoomAmount;
uniform float uDistortAmount;

out vec4 FragColor;

vec4 fetchScene(ivec2 pixel) {
    ivec2 size = ivec2(uResolution);
    if (uRepeatImage)
        pixel = (pixel % size + size) % size;
    else if (any(lessThan(pixel, ivec2(0))) || any(greaterThanEqual(pixel, size)))
        return vec4(0.0);
    vec4 color = texelFetch(uScene, pixel, 0);
    color.rgb *= color.a;
    return color;
}

void main() {
    vec2 offset = gl_FragCoord.xy / uResolution - 0.5;
    vec2 radial = offset * 2.0 * uResolution / min(uResolution.x, uResolution.y);
    float amount = clamp(uDistortAmount, -100.0, 100.0) / 100.0;
    // Both signs stay monotonic, avoiding folded images at strong distortion.
    float scale = sqrt(1.0 + abs(amount) * dot(radial, radial));
    if (amount > 0.0) scale = 1.0 / scale;
    vec2 uv = 0.5 + offset * scale / (clamp(uZoomAmount, 1.0, 1000.0) / 100.0);
    vec2 pixel = uv * uResolution - 0.5;
    ivec2 low = ivec2(floor(pixel));
    vec2 fraction = fract(pixel);
    vec4 color = mix(
        mix(fetchScene(low), fetchScene(low + ivec2(1, 0)), fraction.x),
        mix(fetchScene(low + ivec2(0, 1)), fetchScene(low + ivec2(1, 1)), fraction.x),
        fraction.y);
    if (color.a > 0.0) color.rgb /= color.a;
    FragColor = color;
}
