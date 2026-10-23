#version 330 core

out vec4 FragColor;

uniform sampler2D uIndirectTex;
uniform sampler2D uDepth;
uniform sampler2D uAlbedo;
uniform sampler2D uScene;
uniform vec2 uTexelSize;
uniform mat4 uInverseProjection;
uniform float uDenoiseStrength;
uniform float uBlurRadius;
uniform vec2 uDirection;
uniform float uStrength;
uniform int uMode; // 0: bilateral filter, 1: material-aware linear-light composite

vec3 viewPosition(vec2 uv, float depth)
{
    vec4 p = uInverseProjection * vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    return p.xyz / p.w;
}

vec3 surfaceNormal(vec2 uv, vec3 p)
{
    vec2 x = vec2(uTexelSize.x, 0.0);
    vec2 y = vec2(0.0, uTexelSize.y);
    vec3 l = viewPosition(uv - x, texture(uDepth, uv - x).r);
    vec3 r = viewPosition(uv + x, texture(uDepth, uv + x).r);
    vec3 b = viewPosition(uv - y, texture(uDepth, uv - y).r);
    vec3 t = viewPosition(uv + y, texture(uDepth, uv + y).r);
    vec3 dx = abs(r.z - p.z) < abs(p.z - l.z) ? r - p : p - l;
    vec3 dy = abs(t.z - p.z) < abs(p.z - b.z) ? t - p : p - b;
    vec3 n = cross(dx, dy);
    if (dot(n, n) < 0.0000000001) return normalize(-p);
    n = normalize(n);
    return dot(n, -p) < 0.0 ? -n : n;
}

vec3 compressRadiance(vec3 color)
{
    return color / (1.0 + dot(color, vec3(0.2126, 0.7152, 0.0722)));
}

void main()
{
    vec2 uv = gl_FragCoord.xy * uTexelSize;
    float depth = texture(uDepth, uv).r;
    vec4 material = texture(uAlbedo, uv);
    vec3 center = max(texture(uIndirectTex, uv).rgb, 0.0);
    if (uMode == 1)
    {
        vec3 scene = texture(uScene, uv).rgb;
        if (depth >= 0.999999 || material.a <= 0.0 || uStrength <= 0.0)
            FragColor = vec4(scene, 1.0);
        else
        {
            vec3 indirect = center * material.rgb * material.a * max(uStrength, 0.0);
            FragColor = vec4(pow(pow(max(scene, 0.0), vec3(2.2)) + indirect, vec3(1.0 / 2.2)), 1.0);
        }
        return;
    }
    if (depth >= 0.999999 || material.a <= 0.0)
    {
        FragColor = vec4(0.0);
        return;
    }
    if (uBlurRadius <= 0.0)
    {
        FragColor = vec4(center, 1.0);
        return;
    }

    vec3 p = viewPosition(uv, depth);
    vec3 normal = surfaceNormal(uv, p);
    float tolerance = max(0.5 / 16.0, abs(p.z) * 0.005);
    vec3 accum = compressRadiance(center);
    float weightSum = 1.0;
    // A separable filter for blur, or a 5x5 neighborhood for the optional denoiser.
    bool separable = dot(uDirection, uDirection) > 0.0;
    for (int x = -2; x <= 2; ++x)
    {
        for (int y = -2; y <= 2; ++y)
        {
            if ((x == 0 && y == 0) || (separable && y != 0)) continue;
            vec2 offset = separable ? uDirection * float(x) : vec2(x, y);
            vec2 sampleUv = uv + offset * uTexelSize * uBlurRadius;
            if (any(lessThan(sampleUv, uTexelSize * 0.5)) || any(greaterThan(sampleUv, 1.0 - uTexelSize * 0.5))) continue;
            float sampleDepth = texture(uDepth, sampleUv).r;
            vec4 sampleMaterial = texture(uAlbedo, sampleUv);
            if (sampleDepth >= 0.999999 || sampleMaterial.a <= 0.0) continue;
            vec3 samplePos = viewPosition(sampleUv, sampleDepth);
            float planeDistance = abs(dot(samplePos - p, normal));
            float depthWeight = 1.0 - smoothstep(tolerance * 0.25, tolerance, planeDistance);
            float normalWeight = smoothstep(0.8, 0.98, dot(normal, surfaceNormal(sampleUv, samplePos)));
            float materialWeight = exp(-dot(sampleMaterial.rgb - material.rgb, sampleMaterial.rgb - material.rgb) * 8.0);
            float weight = exp(-dot(offset, offset) / 4.0) * depthWeight * normalWeight * materialWeight;
            accum += compressRadiance(max(texture(uIndirectTex, sampleUv).rgb, 0.0)) * weight;
            weightSum += weight;
        }
    }
    vec3 compressed = accum / weightSum;
    vec3 filtered = compressed / max(1.0 - dot(compressed, vec3(0.2126, 0.7152, 0.0722)), 0.001);
    FragColor = vec4(mix(center, filtered, clamp(uDenoiseStrength / 200.0, 0.0, 1.0)), 1.0);
}
