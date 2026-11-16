#version 330 core

out vec4 FragColor;
uniform sampler2D uScene;
uniform sampler2D uDepth;
uniform sampler2D uNormals;
uniform vec2 uResolution;
uniform float uNear;
uniform float uFar;
uniform vec3 uColor;
uniform float uStrength;
uniform float uRadius;
uniform float uDepthThreshold;
uniform float uDepthFade;
uniform bool uNormalPass;
uniform float uNormalThreshold;
uniform float uNormalFade;
uniform int uMode;

float relativeDepthDifference(float actual, float expected) {
    // Allow two depth-buffer quantization steps when extrapolating the local plane.
    float residual = max(abs(actual - expected) - 2.0 / 16777215.0, 0.0);
    return residual * (uFar - uNear) /
        max(uFar - (uFar - uNear) * max(actual, expected), uNear);
}

float depthAt(ivec2 pixel) {
    return texelFetch(uDepth, clamp(pixel, ivec2(0), ivec2(uResolution) - 1), 0).r;
}

float depthSlope(float backward, float forward, int coordinate, int size) {
    if (coordinate == 0) return forward;
    if (coordinate == size - 1) return backward;
    // Use the smoother side so an actual silhouette does not become the plane estimate.
    return abs(backward) < abs(forward) ? backward : forward;
}

float edgeWeight(float difference, float threshold, float fade) {
    if (fade <= 0.0) return difference > threshold ? 1.0 : 0.0;
    return smoothstep(threshold, threshold + fade, difference);
}

vec3 blendColor(vec3 base, vec3 color) {
    if (uMode == 1) return base + color;
    if (uMode == 2) return 1.0 - (1.0 - base) * (1.0 - color);
    if (uMode == 3) return base * color;
    if (uMode == 4)
        return mix(2.0 * base * color, 1.0 - 2.0 * (1.0 - base) * (1.0 - color),
            step(vec3(0.5), base));
    return color;
}

void main() {
    vec2 vUV = gl_FragCoord.xy / uResolution;
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    vec4 scene = texture(uScene, vUV);
    float centerRaw = depthAt(pixel);
    vec2 slope = vec2(
        depthSlope(centerRaw - depthAt(pixel - ivec2(1, 0)),
            depthAt(pixel + ivec2(1, 0)) - centerRaw, pixel.x, int(uResolution.x)),
        depthSlope(centerRaw - depthAt(pixel - ivec2(0, 1)),
            depthAt(pixel + ivec2(0, 1)) - centerRaw, pixel.y, int(uResolution.y)));
    vec4 centerNormal = uNormalPass ? texelFetch(uNormals, pixel, 0) : vec4(0.0);
    float depthEdge = 0.0;
    float normalEdge = 0.0;
    // Sample the interior too, so increasing radius cannot skip thin geometry.
    for (int ring = 1; ring <= 32; ++ring) {
        float radius = min(float(ring), uRadius);
        for (int direction = 0; direction < 8; ++direction) {
            float angle = float(direction) * 0.78539816339;
            ivec2 neighborPixel = clamp(pixel + ivec2(round(vec2(cos(angle), sin(angle)) * radius)),
                ivec2(0), ivec2(uResolution) - 1);
            float raw = depthAt(neighborPixel);
            float expectedRaw = clamp(centerRaw + dot(slope, vec2(neighborPixel - pixel)), 0.0, 1.0);
            float difference = relativeDepthDifference(raw, expectedRaw);
            if (centerRaw >= 1.0 && raw >= 1.0) difference = 0.0;
            depthEdge = max(depthEdge, difference);
            if (uNormalPass && centerNormal.a > 0.0 && raw < 1.0) {
                vec4 neighbor = texelFetch(uNormals, neighborPixel, 0);
                if (neighbor.a > 0.0) {
                    vec3 a = normalize(centerNormal.xyz * 2.0 - 1.0);
                    vec3 b = normalize(neighbor.xyz * 2.0 - 1.0);
                    normalEdge = max(normalEdge, 0.5 * (1.0 - clamp(dot(a, b), -1.0, 1.0)));
                }
            }
        }
        if (float(ring) >= uRadius) break;
    }
    float mask = max(edgeWeight(depthEdge, uDepthThreshold, uDepthFade),
        uNormalPass ? edgeWeight(normalEdge, uNormalThreshold, uNormalFade) : 0.0);
    FragColor = vec4(mix(scene.rgb, blendColor(scene.rgb, uColor), mask * uStrength), scene.a);
}
