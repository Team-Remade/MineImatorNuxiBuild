#version 450

// MIGRATION NOTE (subsystem pass 4b/N): blurs indirect_lighting.frag's raw
// scratch texture and additively composites the result straight onto the
// main scene framebuffer (BlendState = additive when this pipeline is drawn)
// - unlike the raw pass, there's no read/write hazard here since the input
// (uIndirectTex) is a different texture than the output framebuffer.
layout(set = 0, binding = 0, std140) uniform DenoiseUniforms {
    vec2  uTexelSize;
    float uDenoiseStrength;
    float uNear;
    float uFar;
    float _pad0;
    float _pad1;
    float _pad2;
};

layout(set = 0, binding = 1) uniform texture2D uIndirectTexture;
layout(set = 0, binding = 2) uniform sampler uIndirectSampler;
layout(set = 0, binding = 3) uniform texture2D uDepthTexture;
layout(set = 0, binding = 4) uniform sampler uDepthSampler;

layout(location = 0) out vec4 FragColor;

float linearDepth(float depth)
{
    float z = depth * 2.0 - 1.0;
    return (2.0 * uNear * uFar) / max(uFar + uNear - z * (uFar - uNear), 0.0001);
}

void main()
{
    vec2 uv = gl_FragCoord.xy * uTexelSize;
    float centerRawDepth = texture(sampler2D(uDepthTexture, uDepthSampler), uv).r;
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

            float rawDepth = texture(sampler2D(uDepthTexture, uDepthSampler), sampleUv).r;
            float sampleDepth = rawDepth >= 0.999999 ? uFar : linearDepth(rawDepth);
            float depthDelta = abs(sampleDepth - centerDepth);

            float spatial = exp(-dot(offset, offset) / 8.0);
            float depthWeight = exp(-depthDelta / max(depthSigma, 0.0001));
            float weight = spatial * depthWeight;

            accum += texture(sampler2D(uIndirectTexture, uIndirectSampler), sampleUv).rgb * weight;
            weightSum += weight;
        }
    }

    vec3 denoised = weightSum > 0.0001 ? accum / weightSum : texture(sampler2D(uIndirectTexture, uIndirectSampler), uv).rgb;
    FragColor = vec4(denoised, 1.0);
}
