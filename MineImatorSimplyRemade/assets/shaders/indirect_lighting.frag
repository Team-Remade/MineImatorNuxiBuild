#version 330 core

out vec4 FragColor;

uniform sampler2D uScene;
uniform sampler2D uDepth;
uniform vec2 uTexelSize;
uniform float uNear;
uniform float uFar;
uniform float uPrecision;
uniform float uRayStep;
uniform int uSampleCount;

float linearDepth(float depth)
{
    float z = depth * 2.0 - 1.0;
    return (2.0 * uNear * uFar) / max(uFar + uNear - z * (uFar - uNear), 0.0001);
}

void main()
{
    vec2 uv = gl_FragCoord.xy * uTexelSize;
    float rawCenterDepth = texture(uDepth, uv).r;
    if (rawCenterDepth >= 0.999999)
    {
        FragColor = vec4(0.0);
        return;
    }

    float centerDepth = linearDepth(rawCenterDepth);
    // Perspective makes a flat surface's depth change across the screen.  The
    // old test interpreted that ordinary slope as geometry behind the current
    // pixel, causing large planes (especially the ground) to bounce their own
    // already-lit colour back onto themselves like a full-screen bloom pass.
    // Hardware depth is planar in screen space for a planar primitive, so use
    // its local gradient to predict and reject samples on that same plane.
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

        float rawSampleDepth = texture(uDepth, sampleUv).r;
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

        vec3 sampleColor = texture(uScene, sampleUv).rgb;
        indirect += sampleColor * weight;
        weightSum += weight;
    }

    if (weightSum > 0.0001)
        indirect /= weightSum;

    indirect *= mix(0.35, 1.1, quality);
    FragColor = vec4(clamp(indirect, 0.0, 2.0), 1.0);
}
