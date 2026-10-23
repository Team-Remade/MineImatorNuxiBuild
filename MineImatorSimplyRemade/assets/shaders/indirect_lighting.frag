#version 330 core

out vec4 FragColor;

uniform sampler2D uScene;
uniform sampler2D uDepth;
uniform sampler2D uAlbedo;
uniform vec2 uTexelSize;
uniform mat4 uProjection;
uniform mat4 uInverseProjection;
uniform float uPrecision;
uniform float uRayStep;
uniform int uSampleCount;

vec3 viewPosition(vec2 uv, float depth)
{
    vec4 p = uInverseProjection * vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    return p.xyz / p.w;
}

vec3 surfaceNormal(vec2 uv, vec3 p)
{
    // Select one-sided differences on the same surface, rather than crossing silhouettes.
    vec3 l = viewPosition(uv - vec2(uTexelSize.x, 0.0), texture(uDepth, uv - vec2(uTexelSize.x, 0.0)).r);
    vec3 r = viewPosition(uv + vec2(uTexelSize.x, 0.0), texture(uDepth, uv + vec2(uTexelSize.x, 0.0)).r);
    vec3 b = viewPosition(uv - vec2(0.0, uTexelSize.y), texture(uDepth, uv - vec2(0.0, uTexelSize.y)).r);
    vec3 t = viewPosition(uv + vec2(0.0, uTexelSize.y), texture(uDepth, uv + vec2(0.0, uTexelSize.y)).r);
    vec3 dx = abs(r.z - p.z) < abs(p.z - l.z) ? r - p : p - l;
    vec3 dy = abs(t.z - p.z) < abs(p.z - b.z) ? t - p : p - b;
    vec3 n = cross(dx, dy);
    if (dot(n, n) < 0.0000000001)
        return normalize(-p);
    n = normalize(n);
    return dot(n, -p) < 0.0 ? -n : n;
}

bool insideScreen(vec2 uv)
{
    return all(greaterThanEqual(uv, uTexelSize * 0.5)) && all(lessThanEqual(uv, 1.0 - uTexelSize * 0.5));
}

vec3 traceBounce(vec3 p, vec3 normal, vec3 direction, float jitter)
{
    float quality = clamp(uPrecision, 0.0, 1.0);
    float bias = max(0.002, -p.z * 0.0002);
    vec3 start = p + normal * bias;
    // A bounded ray, not a depth-dependent gather radius. Units are Minecraft blocks.
    float rayLength = 1000.0 / 16.0;
    if (direction.z > 0.0)
        rayLength = min(rayLength, max(0.0, (viewPosition(vec2(0.5), 0.0).z - start.z) / direction.z));
    vec3 end = start + direction * rayLength;
    vec4 h0 = uProjection * vec4(start, 1.0);
    vec4 h1 = uProjection * vec4(end, 1.0);
    if (h0.w <= 0.0 || h1.w <= 0.0 || rayLength <= bias)
        return vec3(0.0);

    vec2 uv0 = h0.xy / h0.w * 0.5 + 0.5;
    vec2 uv1 = h1.xy / h1.w * 0.5 + 0.5;
    vec3 q0 = start / h0.w;
    vec3 q1 = end / h1.w;
    float k0 = 1.0 / h0.w;
    float k1 = 1.0 / h1.w;
    // Clip in projected space, preserving reciprocal depth at the clipped endpoint.
    vec2 delta = uv1 - uv0;
    float clipT = 1.0;
    for (int axis = 0; axis < 2; ++axis)
    {
        if (delta[axis] > 0.000001)
            clipT = min(clipT, (1.0 - uTexelSize[axis] * 0.5 - uv0[axis]) / delta[axis]);
        else if (delta[axis] < -0.000001)
            clipT = min(clipT, (uTexelSize[axis] * 0.5 - uv0[axis]) / delta[axis]);
    }
    clipT = clamp(clipT, 0.0, 1.0);
    uv1 = mix(uv0, uv1, clipT);
    q1 = mix(q0, q1, clipT);
    k1 = mix(k0, k1, clipT);
    float screenLength = length((uv1 - uv0) / uTexelSize);
    if (screenLength < 1.0) return vec3(0.0);
    float stepT = max(uRayStep, 1.0) / screenLength;
    float previousT = 0.0;
    int steps = int(mix(24.0, 96.0, quality));
    float thickness = mix(0.25, 0.0625, quality);

    for (int i = 0; i < 96; ++i)
    {
        if (i >= steps) break;
        // Resolve nearby geometry first, then cover the rest of the segment at any resolution.
        float rayT = i < 8 ? (float(i) + 1.0 + jitter) * stepT
            : mix(min((8.0 + jitter) * stepT, 1.0), 1.0,
                (float(i - 8) + 1.0 + jitter) / float(steps - 8));
        rayT = min(rayT, 1.0);
        vec2 rayUv = mix(uv0, uv1, rayT);
        if (!insideScreen(rayUv)) break;
        float depth = texture(uDepth, rayUv).r;
        vec3 rayPos = mix(q0, q1, rayT) / mix(k0, k1, rayT);
        if (depth < 0.999999 && rayPos.z <= viewPosition(rayUv, depth).z)
        {
            // Refine the first depth crossing; a failed crossing is an occluder, not a later light source.
            float lo = previousT;
            float hi = rayT;
            for (int refine = 0; refine < 5; ++refine)
            {
                float mid = (lo + hi) * 0.5;
                vec2 midUv = mix(uv0, uv1, mid);
                float midDepth = texture(uDepth, midUv).r;
                vec3 midPos = mix(q0, q1, mid) / mix(k0, k1, mid);
                if (midDepth < 0.999999 && midPos.z <= viewPosition(midUv, midDepth).z)
                    hi = mid;
                else
                    lo = mid;
            }
            vec2 hitUv = mix(uv0, uv1, hi);
            vec3 hitPos = viewPosition(hitUv, texture(uDepth, hitUv).r);
            vec3 hitRayPos = mix(q0, q1, hi) / mix(k0, k1, hi);
            if (hitPos.z - hitRayPos.z > thickness || dot(hitPos - p, normal) <= bias * 2.0)
                return vec3(0.0);
            vec3 hitNormal = surfaceNormal(hitUv, hitPos);
            float facing = max(dot(hitNormal, -direction), 0.0);
            vec2 edge = min(hitUv, 1.0 - hitUv);
            float confidence = smoothstep(0.0, 0.05, min(edge.x, edge.y));
            return max(texture(uScene, hitUv).rgb, 0.0) * facing * confidence;
        }
        previousT = rayT;
        if (rayT >= 1.0) break;
    }
    // Offscreen, sky and exhausted rays have no known incoming radiance.
    return vec3(0.0);
}

void main()
{
    vec2 uv = gl_FragCoord.xy * uTexelSize;
    float depth = texture(uDepth, uv).r;
    if (depth >= 0.999999 || texture(uAlbedo, uv).a <= 0.0)
    {
        FragColor = vec4(0.0);
        return;
    }

    vec3 p = viewPosition(uv, depth);
    vec3 normal = surfaceNormal(uv, p);
    vec3 tangent = normalize(cross(abs(normal.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0), normal));
    vec3 bitangent = cross(normal, tangent);
    float noise = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453);
    int samples = clamp(uSampleCount, 1, 16);
    vec3 indirect = vec3(0.0);
    for (int i = 0; i < 16; ++i)
    {
        if (i >= samples) break;
        float xi = (float(i) + 0.5) / float(samples);
        float angle = float(i) * 2.39996323 + noise * 6.28318531;
        vec3 direction = tangent * (sqrt(xi) * cos(angle))
                       + bitangent * (sqrt(xi) * sin(angle)) + normal * sqrt(1.0 - xi);
        indirect += traceBounce(p, normal, direction, noise * 0.5);
    }
    // Cosine-weighted sampling cancels the diffuse cosine/PDF. Misses still count.
    FragColor = vec4(indirect / float(samples), 1.0);
}
