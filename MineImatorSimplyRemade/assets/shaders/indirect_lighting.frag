#version 450

// MIGRATION NOTE (subsystem pass 4b/N): renders the raw (noisy) indirect
// bounce into a scratch texture - uScene is the main color target *as it
// stood after opaque geometry* (read-only here), so this must run into a
// separate render target rather than the main framebuffer to avoid a
// read/write hazard. indirect_denoise.frag (below) then blurs this and
// composites the result additively onto the main scene.
layout(set = 0, binding = 0, std140) uniform IndirectUniforms {
    vec2  uTexelSize;
    float uNear;
    float uFar;
    float uPrecision;
    float uRayStep;
    int   uSampleCount;
    int   _pad0;
    int   _pad1;
};

layout(set = 0, binding = 1) uniform texture2D uSceneTexture;
layout(set = 0, binding = 2) uniform sampler uSceneSampler;
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
    float rawCenterDepth = texture(sampler2D(uDepthTexture, uDepthSampler), uv).r;
    if (rawCenterDepth >= 0.999999)
    {
        FragColor = vec4(0.0);
        return;
    }

    float centerDepth = linearDepth(rawCenterDepth);
    vec2 rawDepthGradient = vec2(dFdx(rawCenterDepth), dFdy(rawCenterDepth));
    vec3 indirect = vec3(0.0);
    float weightSum = 0.0;

    int samples = clamp(uSampleCount, 4, 64);
    float quality = clamp(uPrecision, 0.0, 1.0);
    float rayStep = clamp(uRayStep, 1.0, 64.0);
    float baseRadius = mix(1.5, 9.0, quality) * rayStep;
    const float goldenAngle = 2.39996323;

    for (int i = 0; i < 64; ++i)
    {
        if (i >= samples)
            break;

        float t = (float(i) + 0.5) / float(samples);
        float radius = sqrt(t) * baseRadius;
        vec2 dir = vec2(cos(float(i) * goldenAngle), sin(float(i) * goldenAngle));
        vec2 sampleUv = uv + dir * radius * uTexelSize;

        if (sampleUv.x < 0.0 || sampleUv.x > 1.0 || sampleUv.y < 0.0 || sampleUv.y > 1.0)
            continue;

        float rawSampleDepth = texture(sampler2D(uDepthTexture, uDepthSampler), sampleUv).r;
        if (rawSampleDepth >= 0.999999)
            continue;

        vec2 sampleOffsetPixels = dir * radius;
        float predictedPlaneDepth = rawCenterDepth + dot(rawDepthGradient, sampleOffsetPixels);
        float planeResidual = rawSampleDepth - predictedPlaneDepth;
        float planeTolerance = max(0.00002, fwidth(rawCenterDepth) * 0.1);
        float separateSurfaceWeight = smoothstep(planeTolerance, planeTolerance * 4.0, planeResidual);
        if (separateSurfaceWeight <= 0.0001)
            continue;

        float sampleDepth = linearDepth(rawSampleDepth);
        float depthDelta = sampleDepth - centerDepth;

        float behindWeight = smoothstep(0.0, max(0.35, centerDepth * 0.04), depthDelta) * separateSurfaceWeight;
        float depthSimilarity = 1.0 - smoothstep(0.0, max(1.2, centerDepth * 0.12), abs(depthDelta));
        float radialWeight = 1.0 - t;
        float weight = behindWeight * depthSimilarity * radialWeight;

        if (weight <= 0.0001)
            continue;

        vec3 sampleColor = texture(sampler2D(uSceneTexture, uSceneSampler), sampleUv).rgb;
        indirect += sampleColor * weight;
        weightSum += weight;
    }

    if (weightSum > 0.0001)
        indirect /= weightSum;

    indirect *= mix(0.35, 1.1, quality);
    FragColor = vec4(clamp(indirect, 0.0, 2.0), 1.0);
}
