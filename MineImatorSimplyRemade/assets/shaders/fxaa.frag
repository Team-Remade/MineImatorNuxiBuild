#version 330 core

out vec4 FragColor;
uniform sampler2D uScene;
uniform vec2 uTexelSize;
uniform float uSubpixel;
uniform float uEdgeThreshold;
uniform float uMinimumThreshold;

float luma(vec2 uv)
{
    return dot(texture(uScene, uv).rgb, vec3(0.299, 0.587, 0.114));
}

void main()
{
    vec2 uv = gl_FragCoord.xy * uTexelSize;
    vec4 center = texture(uScene, uv);
    float m = dot(center.rgb, vec3(0.299, 0.587, 0.114));
    float n = luma(uv + vec2(0.0, uTexelSize.y));
    float s = luma(uv - vec2(0.0, uTexelSize.y));
    float e = luma(uv + vec2(uTexelSize.x, 0.0));
    float w = luma(uv - vec2(uTexelSize.x, 0.0));
    float low = min(m, min(min(n, s), min(e, w)));
    float high = max(m, max(max(n, s), max(e, w)));
    float contrast = high - low;
    if (contrast < max(uMinimumThreshold, high * uEdgeThreshold))
    {
        FragColor = center;
        return;
    }

    float ne = luma(uv + uTexelSize);
    float nw = luma(uv + vec2(-uTexelSize.x, uTexelSize.y));
    float se = luma(uv + vec2(uTexelSize.x, -uTexelSize.y));
    float sw = luma(uv - uTexelSize);
    float horizontal = abs(nw + sw - 2.0 * w) + 2.0 * abs(n + s - 2.0 * m) + abs(ne + se - 2.0 * e);
    float vertical = abs(nw + ne - 2.0 * n) + 2.0 * abs(w + e - 2.0 * m) + abs(sw + se - 2.0 * s);
    bool isHorizontal = horizontal >= vertical;
    float negative = isHorizontal ? s : w;
    float positive = isHorizontal ? n : e;
    bool useNegative = abs(negative - m) >= abs(positive - m);
    float neighbor = useNegative ? negative : positive;
    float gradient = abs(neighbor - m);
    float edgeLuma = (neighbor + m) * 0.5;
    vec2 normal = isHorizontal ? vec2(0.0, uTexelSize.y) : vec2(uTexelSize.x, 0.0);
    if (useNegative) normal = -normal;
    vec2 along = isHorizontal ? vec2(uTexelSize.x, 0.0) : vec2(0.0, uTexelSize.y);
    vec2 edgeUv = uv + normal * 0.5;
    vec2 endNegative = edgeUv - along;
    vec2 endPositive = edgeUv + along;
    float deltaNegative = luma(endNegative) - edgeLuma;
    float deltaPositive = luma(endPositive) - edgeLuma;
    bool doneNegative = abs(deltaNegative) >= gradient * 0.25;
    bool donePositive = abs(deltaPositive) >= gradient * 0.25;

    // Search both directions along the edge, increasing stride for long silhouettes.
    const float steps[10] = float[10](1.0, 1.0, 1.0, 1.0, 1.5, 2.0, 2.0, 2.0, 4.0, 8.0);
    for (int i = 0; i < 10; ++i)
    {
        if (doneNegative && donePositive) break;
        if (!doneNegative)
        {
            endNegative -= along * steps[i];
            deltaNegative = luma(endNegative) - edgeLuma;
            doneNegative = abs(deltaNegative) >= gradient * 0.25;
        }
        if (!donePositive)
        {
            endPositive += along * steps[i];
            deltaPositive = luma(endPositive) - edgeLuma;
            donePositive = abs(deltaPositive) >= gradient * 0.25;
        }
    }

    float distanceNegative = isHorizontal ? uv.x - endNegative.x : uv.y - endNegative.y;
    float distancePositive = isHorizontal ? endPositive.x - uv.x : endPositive.y - uv.y;
    bool closerNegative = distanceNegative < distancePositive;
    float closestDelta = closerNegative ? deltaNegative : deltaPositive;
    float edgeBlend = 0.5 - min(distanceNegative, distancePositive) / (distanceNegative + distancePositive);
    if ((closestDelta < 0.0) == (m < edgeLuma)) edgeBlend = 0.0;

    float average = (2.0 * (n + s + e + w) + ne + nw + se + sw) / 12.0;
    float subpixel = clamp(abs(average - m) / contrast, 0.0, 1.0);
    subpixel = subpixel * subpixel * (3.0 - 2.0 * subpixel);
    subpixel = subpixel * subpixel * uSubpixel;
    FragColor = vec4(texture(uScene, uv + normal * max(edgeBlend, subpixel)).rgb, center.a);
}
