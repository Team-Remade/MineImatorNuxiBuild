#version 330 core

out vec4 FragColor;

uniform sampler2D uIndirectTex;
uniform sampler2D uDepth;
uniform vec2 uTexelSize;
uniform float uDenoiseStrength;
uniform float uNear;
uniform float uFar;

float linearDepth(float depth)
{
    float z = depth * 2.0 - 1.0;
    return (2.0 * uNear * uFar) / max(uFar + uNear - z * (uFar - uNear), 0.0001);
}

void main()
{
    vec2 uv = gl_FragCoord.xy * uTexelSize;
    float centerRawDepth = texture(uDepth, uv).r;
    float centerDepth = centerRawDepth >= 0.999999 ? uFar : linearDepth(centerRawDepth);

    float strength = clamp(uDenoiseStrength, 0.0, 200.0) / 200.0;
    float depthSigma = mix(0.35, 3.0, strength);

    vec3 accum = vec3(0.0);
    float weightSum = 0.0;

    for (int x = -2; x <= 2; ++x)
    {
        for (int y = -2; y <= 2; ++y)
        {
            vec2 offset = vec2(float(x), float(y));
            vec2 sampleUv = uv + offset * uTexelSize;

            float rawDepth = texture(uDepth, sampleUv).r;
            float sampleDepth = rawDepth >= 0.999999 ? uFar : linearDepth(rawDepth);
            float depthDelta = abs(sampleDepth - centerDepth);

            float spatial = exp(-dot(offset, offset) / 8.0);
            float depthWeight = exp(-depthDelta / max(depthSigma, 0.0001));
            float weight = spatial * depthWeight;

            accum += texture(uIndirectTex, sampleUv).rgb * weight;
            weightSum += weight;
        }
    }

    vec3 denoised = weightSum > 0.0001 ? accum / weightSum : texture(uIndirectTex, uv).rgb;
    FragColor = vec4(denoised, 1.0);
}
