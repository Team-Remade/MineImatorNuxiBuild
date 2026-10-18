#version 330 core

out vec4 FragColor;
uniform sampler2D uScene;
uniform sampler2D uDepth;
uniform vec2 uResolution;
uniform vec2 uClip;
uniform vec4 uFocus; // depth, full sharp range, fade distance, blur percentage
uniform vec4 uBokeh; // anamorphic ratio, edge highlight, threshold, strength
uniform int uColorFringe;
uniform vec3 uFringeAngles;
uniform vec3 uFringeOffsets;

float linearDepth(vec2 uv)
{
    float d = texture(uDepth, uv).r;
    return uClip.x / (1.0 - d + d * uClip.x / uClip.y);
}

float blurAmount(float depth)
{
    float distanceFromFocus = max(0.0, abs(depth - uFocus.x) - uFocus.y * 0.5);
    return smoothstep(0.0, max(uFocus.z, 0.0001), distanceFromFocus);
}

vec4 sampleColor(vec2 uv, float blur)
{
    vec4 color = texture(uScene, uv);
    if (uColorFringe != 0 && blur > 0.0)
    {
        vec3 angles = radians(uFringeAngles);
        vec3 offsets = uFringeOffsets * (uResolution.y / 1080.0) * blur;
        color.r = texture(uScene, uv + vec2(cos(angles.r), sin(angles.r)) * offsets.r / uResolution).r;
        color.g = texture(uScene, uv + vec2(cos(angles.g), sin(angles.g)) * offsets.g / uResolution).g;
        color.b = texture(uScene, uv + vec2(cos(angles.b), sin(angles.b)) * offsets.b / uResolution).b;
    }
    return color;
}

void main()
{
    vec2 uv = gl_FragCoord.xy / uResolution;
    float centerDepth = linearDepth(uv);
    float centerBlur = blurAmount(centerDepth);
    float maxRadius = uResolution.y * 0.025 * clamp(uFocus.w / 100.0, 0.0, 1.0);
    if (maxRadius < 0.01)
    {
        FragColor = texture(uScene, uv);
        return;
    }

    float ratio = clamp(uBokeh.x, 0.1, 10.0);
    vec2 shape = vec2(sqrt(ratio), inversesqrt(ratio));
    vec4 center = sampleColor(uv, centerBlur);
    vec3 sum = center.rgb * center.a;
    float alphaSum = center.a;
    float weightSum = 1.0;

    // Gather a deterministic aperture disk; nearer blurred samples can spread
    // over sharp backgrounds, but backgrounds cannot bleed through sharp subjects.
    const int sampleCount = 96;
    for (int i = 0; i < sampleCount; ++i)
    {
        float radius = sqrt((float(i) + 0.5) / float(sampleCount));
        float angle = float(i) * 2.39996323;
        vec2 sampleUv = uv + vec2(cos(angle), sin(angle)) * radius * maxRadius * shape / uResolution;
        if (any(lessThan(sampleUv, vec2(0.0))) || any(greaterThan(sampleUv, vec2(1.0))))
            continue;

        float sampleDepth = linearDepth(sampleUv);
        float sampleBlur = blurAmount(sampleDepth);
        bool inFront = sampleDepth < centerDepth - max(0.001, centerDepth * 0.001);
        float aperture = inFront ? sampleBlur : centerBlur;
        float coverage = 1.0 - smoothstep(aperture, aperture + 1.0 / maxRadius, radius);
        if (coverage <= 0.0 || aperture <= 0.0)
            continue;

        vec4 color = sampleColor(sampleUv, sampleBlur);
        float luminance = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));
        float highlight = max(0.0, luminance - uBokeh.z) / max(0.001, 1.0 - uBokeh.z);
        float edge = smoothstep(0.6, 1.0, radius / max(aperture, 0.001));
        float weight = coverage * mix(1.0, 1.0 + 4.0 * edge, clamp(uBokeh.y, 0.0, 1.0));
        vec3 boosted = color.rgb * (1.0 + highlight * max(0.0, uBokeh.w) * sampleBlur);
        sum += boosted * color.a * weight;
        alphaSum += color.a * weight;
        weightSum += weight;
    }

    FragColor = vec4(sum / max(alphaSum, 0.00001), alphaSum / weightSum);
}
