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

    // Predict depth on the local surface tangent plane using linear depth gradients.
    // Flat surfaces shouldn't bounce their own lit color back onto themselves.
    vec2 depthGradient = vec2(dFdx(centerDepth), dFdy(centerDepth));

    // Suppress SSGI entirely at depth discontinuities (object edges).
    // At edges, dFdx/dFdy span a large depth jump, making the gradient unreliable
    // and causing nearby bright surface samples to bleed onto the edge pixel.
    float edgeSharpness = length(depthGradient);
    float edgeThreshold = max(0.5, centerDepth * 0.05);
    if (edgeSharpness > edgeThreshold)
    {
        FragColor = vec4(0.0);
        return;
    }

    vec3 indirect = vec3(0.0);
    float totalWeight = 0.0;

    int samples = clamp(uSampleCount, 4, 64);
    float quality = clamp(uPrecision, 0.0, 1.0);
    float rayStep = clamp(uRayStep, 1.0, 64.0);
    float baseRadius = mix(2.0, 10.0, quality) * rayStep;
    const float goldenAngle = 2.39996323;

    // Maximum physical distance (in linear depth units) across which indirect bounce can occur.
    // Scales moderately with depth and ray step, preventing distant background or fog from bleeding.
    float maxDistance = max(0.4, rayStep * 0.12 + centerDepth * 0.04);
    float planeTolerance = max(0.02, centerDepth * 0.001);

    for (int i = 0; i < 64; ++i)
    {
        if (i >= samples)
            break;

        float t = (float(i) + 0.5) / float(samples);
        float radius = sqrt(t) * baseRadius;
        vec2 dir = vec2(cos(float(i) * goldenAngle), sin(float(i) * goldenAngle));
        vec2 sampleOffsetPixels = dir * radius;
        vec2 sampleUv = uv + sampleOffsetPixels * uTexelSize;

        float radialWeight = 1.0 - t;
        totalWeight += radialWeight;

        if (sampleUv.x < 0.0 || sampleUv.x > 1.0 || sampleUv.y < 0.0 || sampleUv.y > 1.0)
            continue;

        float rawSampleDepth = texture(uDepth, sampleUv).r;
        if (rawSampleDepth >= 0.999999)
            continue;

        float sampleDepth = linearDepth(rawSampleDepth);

        // Reject self-bounce: samples on the same flat surface plane don't contribute.
        // The plane-fitted predicted depth tells us what depth a co-planar neighbor would have.
        // Only samples that deviate from this prediction (i.e. are on a different surface) are valid bounce sources.
        float predictedPlaneDepth = centerDepth + dot(depthGradient, sampleOffsetPixels);
        float planeDiff = abs(sampleDepth - predictedPlaneDepth);
        float separateSurfaceWeight = smoothstep(planeTolerance, planeTolerance * 3.0, planeDiff);
        if (separateSurfaceWeight <= 0.0001)
            continue;

        // Bounded depth distance: only nearby surfaces can bounce light.
        float depthDelta = abs(sampleDepth - centerDepth);
        if (depthDelta >= maxDistance)
            continue;

        // Directional occlusion: reject samples that are significantly closer to the camera than the receiver.
        // Such samples are "in front" and occlude the receiver rather than bouncing light toward it.
        // e.g. a white cube face seen by background grass pixels behind it.
        float depthDiff = centerDepth - sampleDepth; // positive = sample is in front of receiver
        if (depthDiff > maxDistance * 0.3)
            continue;

        float distanceFalloff = 1.0 - smoothstep(0.0, maxDistance, depthDelta);
        float sampleWeight = separateSurfaceWeight * distanceFalloff * radialWeight;

        if (sampleWeight <= 0.0001)
            continue;

        vec3 sampleColor = texture(uScene, sampleUv).rgb;
        // Clamp sample luminance to prevent overbright surfaces from dominating the gather.
        float sampleLum = dot(sampleColor, vec3(0.2126, 0.7152, 0.0722));
        if (sampleLum > 0.0001)
            sampleColor *= min(1.0, 0.6 / sampleLum);
        indirect += sampleColor * sampleWeight;
    }

    if (totalWeight > 0.0001)
        indirect /= totalWeight;

    // Attenuate indirect light in the far distance where fog dominates and depth precision drops.
    float distanceFade = 1.0 - smoothstep(uFar * 0.4, uFar * 0.85, centerDepth);
    indirect *= distanceFade;

    FragColor = vec4(clamp(indirect, 0.0, 2.0), 1.0);
}
