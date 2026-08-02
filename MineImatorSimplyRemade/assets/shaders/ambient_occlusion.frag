#version 330 core

out vec4 FragColor;

uniform sampler2D uDepth;
uniform vec2 uTexelSize;
uniform float uNear;
uniform float uFar;
uniform float uRadius;
uniform float uStrength;
uniform vec3 uColor;
uniform float uRatio;
uniform float uRatioBalance;
uniform int uOutputMode;

float linearDepth(float depth)
{
    float z = depth * 2.0 - 1.0;
    return (2.0 * uNear * uFar) / (uFar + uNear - z * (uFar - uNear));
}

void main()
{
    vec2 uv = gl_FragCoord.xy * uTexelSize;
    float rawCenter = texture(uDepth, uv).r;
    if (rawCenter >= 0.999999 || uRadius <= 0.0 || uStrength <= 0.0)
        discard;

    float center = linearDepth(rawCenter);
    float occlusion = 0.0;
    float weightSum = 0.0;
    const int sampleCount = 32;
    const float goldenAngle = 2.39996323;

    for (int i = 0; i < sampleCount; ++i)
    {
        float t = (float(i) + 0.5) / float(sampleCount);
        float sampleRadius = uRadius * mix(sqrt(t), t, uRatioBalance);
        vec2 direction = vec2(cos(float(i) * goldenAngle), sin(float(i) * goldenAngle));
        float sampleDepth = linearDepth(texture(uDepth, uv + direction * sampleRadius * uTexelSize).r);
        float delta = center - sampleDepth;
        float bias = max(0.002, center * uRatio * 0.002);
        float rangeWeight = 1.0 - smoothstep(0.0, max(0.01, center * 0.08 + uRadius * 0.01), abs(delta));
        float blocked = smoothstep(bias, bias * 4.0 + 0.001, delta);
        float weight = mix(1.0 - t, t, uRatioBalance) + 0.25;
        occlusion += blocked * rangeWeight * weight;
        weightSum += weight;
    }

    occlusion = clamp(occlusion / max(weightSum, 0.0001), 0.0, 1.0);
    occlusion = smoothstep(0.0, 1.0, occlusion);
    occlusion = pow(occlusion, mix(1.8, 0.55, uRatioBalance));

    float aoMask = clamp(occlusion * uStrength * 0.65, 0.0, 1.0);
    if (uOutputMode == 1)
    {
        FragColor = vec4(vec3(aoMask), 1.0);
        return;
    }

    FragColor = vec4(clamp(uColor, 0.0, 1.0), aoMask);
}
