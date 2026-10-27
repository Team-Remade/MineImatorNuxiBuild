#version 330 core

uniform sampler2D uScene;
uniform vec2 uTexelSize;
uniform float uContrast;
uniform float uBrightness;
uniform float uSaturation;
uniform float uVibrance;
uniform float uHue;
uniform float uInvertStrength;
uniform float uColorBurn;

out vec4 FragColor;

const vec3 REC709 = vec3(0.2126, 0.7152, 0.0722);

float finiteOr(float value, float fallback) {
    return (isnan(value) || isinf(value)) ? fallback : value;
}

vec3 shiftHue(vec3 color, float degrees) {
    float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float chroma = maximum - minimum;
    if (chroma <= 0.0 || degrees == 0.0) return color;

    float hue;
    if (maximum == color.r) hue = (color.g - color.b) / chroma;
    else if (maximum == color.g) hue = (color.b - color.r) / chroma + 2.0;
    else hue = (color.r - color.g) / chroma + 4.0;
    hue = fract(hue / 6.0 + mod(degrees, 360.0) / 360.0);
    vec3 channels = clamp(abs(fract(hue + vec3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0, 0.0, 1.0);
    return minimum + chroma * channels;
}

void main() {
    vec4 scene = texture(uScene, gl_FragCoord.xy * uTexelSize);
    // Percentages are intentionally unrestricted for these four controls.
    vec3 color = scene.rgb * (finiteOr(uBrightness, 100.0) / 100.0);
    color = (color - 0.5) * (finiteOr(uContrast, 100.0) / 100.0) + 0.5;
    float luminance = dot(color, REC709);
    color = mix(vec3(luminance), color, finiteOr(uSaturation, 100.0) / 100.0);

    // Vibrance favors muted colors instead of boosting already saturated colors equally.
    vec3 bounded = clamp(color, 0.0, 1.0);
    float maximum = max(bounded.r, max(bounded.g, bounded.b));
    float minimum = min(bounded.r, min(bounded.g, bounded.b));
    float saturation = maximum > 0.0 ? (maximum - minimum) / maximum : 0.0;
    luminance = dot(color, REC709);
    color = mix(vec3(luminance), color, 1.0 + finiteOr(uVibrance, 0.0) / 100.0 * (1.0 - saturation));
    color = shiftHue(color, finiteOr(uHue, 0.0));

    color = mix(color, 1.0 - color, clamp(finiteOr(uInvertStrength, 0.0) / 100.0, 0.0, 1.0));
    float burn = clamp(finiteOr(uColorBurn, 0.0) / 100.0, 0.0, 1.0);
    if (burn > 0.0) {
        vec3 base = clamp(color, 0.0, 1.0);
        vec3 burned = 1.0 - min(vec3(1.0), (1.0 - base) / max(base, vec3(0.000001)));
        color = mix(color, burned, burn);
    }
    FragColor = vec4(clamp(color, 0.0, 1.0), scene.a);
}
