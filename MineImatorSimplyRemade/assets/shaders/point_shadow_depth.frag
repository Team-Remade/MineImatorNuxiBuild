#version 450

layout(location = 0) in vec3 vWorldPos;
layout(location = 1) in vec2 vTexCoord;

layout(set = 0, binding = 0, std140) uniform PointShadowDepthUniforms {
    mat4  uLightViewProj;
    mat4  uModel;
    vec2  uTexOffset;
    float uTexScaleV;
    float uAlpha;
    vec2  uTexUvOffset;
    vec2  uTexUvRepeat;
    vec2  uTexUvMirror;
    int   uUseTexture;
    int   _pad0;
    vec3  uLightPos;
    float uFarPlane;
};

layout(set = 0, binding = 1) uniform sampler2D uTextureSampler;

layout(location = 0) out vec4 FragDistance;

void main() {
    float alpha = uAlpha;
    if (uUseTexture != 0)
        alpha *= texture(uTextureSampler, vTexCoord).a;

    if (alpha < 0.01)
        discard;

    // Stored as a plain color value (not gl_FragDepth) so this render target
    // can be a normal Sampled|RenderTarget cube texture instead of requiring
    // depth-format cube attachments (which Veldrid's cross-platform depth
    // formats don't uniformly support as cube render targets). Sampled back
    // directly as .r in simple.frag's samplePointShadowCube, same as the old
    // GL version's gl_FragDepth-into-a-depth-cubemap-sampled-as-color approach.
    float lightDistance = length(vWorldPos - uLightPos);
    float normalizedDistance = clamp(lightDistance / max(uFarPlane, 0.0001), 0.0, 1.0);
    FragDistance = vec4(normalizedDistance, 0.0, 0.0, 1.0);
}
