#version 330 core

uniform sampler2D uScene;
uniform vec2 uTexelSize;
uniform float uStrength;
uniform float uSize;
uniform vec2 uDirection;
uniform int uMode;

out vec4 FragColor;

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
        // Composite mode: output as-is so additive blending can apply strength.
        FragColor = vec4(texture(uScene, uv).rgb * max(uStrength, 0.0), 1.0);
        return;
    }

    if (uMode == 0)
    {
        // Threshold mode.
        FragColor = vec4(ExtractBright(texture(uScene, uv).rgb), 1.0);
        return;
    }

    // Blur mode (uMode == 1): 9-tap gaussian in one direction.
    vec2 axis = normalize(uDirection);
    if (length(axis) < 0.5)
        axis = vec2(1.0, 0.0);

    vec2 stepUv = axis * (max(uSize, 0.0) * uTexelSize);

    // Symmetric 9-tap kernel (sum = 1.0).
    float w0 = 0.22702703;
    float w1 = 0.19459459;
    float w2 = 0.12162162;
    float w3 = 0.05405405;
    float w4 = 0.01621622;

    vec3 blur = texture(uScene, uv).rgb * w0;
    blur += texture(uScene, uv + stepUv * 1.0).rgb * w1;
    blur += texture(uScene, uv - stepUv * 1.0).rgb * w1;
    blur += texture(uScene, uv + stepUv * 2.0).rgb * w2;
    blur += texture(uScene, uv - stepUv * 2.0).rgb * w2;
    blur += texture(uScene, uv + stepUv * 3.0).rgb * w3;
    blur += texture(uScene, uv - stepUv * 3.0).rgb * w3;
    blur += texture(uScene, uv + stepUv * 4.0).rgb * w4;
    blur += texture(uScene, uv - stepUv * 4.0).rgb * w4;

    FragColor = vec4(blur, 1.0);
}
