#version 450

layout(set = 0, binding = 0, std140) uniform EdgeUniforms {
    vec2  uTexelSize;
    vec4  uEdgeColor;
    float uThreshold;
    vec3  _pad0;
};

layout(set = 0, binding = 1) uniform texture2D uMaskTexture;
layout(set = 0, binding = 2) uniform sampler uMaskSampler;

layout(location = 0) out vec4 FragColor;

float sampleMask(vec2 uv, float dx, float dy) {
    return texture(sampler2D(uMaskTexture, uMaskSampler), uv + vec2(dx, dy) * uTexelSize).r;
}

void main() {
    vec2 uv = gl_FragCoord.xy * uTexelSize;

    float tl = sampleMask(uv, -1.0,  1.0);
    float tm = sampleMask(uv,  0.0,  1.0);
    float tr = sampleMask(uv,  1.0,  1.0);
    float ml = sampleMask(uv, -1.0,  0.0);
    float mr = sampleMask(uv,  1.0,  0.0);
    float bl = sampleMask(uv, -1.0, -1.0);
    float bm = sampleMask(uv,  0.0, -1.0);
    float br = sampleMask(uv,  1.0, -1.0);

    float gx = -tl + tr - 2.0*ml + 2.0*mr - bl + br;
    float gy = -tl - 2.0*tm - tr + bl + 2.0*bm + br;

    float magnitude = sqrt(gx*gx + gy*gy);

    if (magnitude < uThreshold) discard;

    float alpha = clamp(magnitude, 0.0, 1.0) * uEdgeColor.a;
    FragColor = vec4(uEdgeColor.rgb, alpha);
}
