#version 450

layout(location = 0) in vec2 vTexCoord;

layout(set = 0, binding = 0, std140) uniform ShadowDepthUniforms {
    mat4  uMVP;
    vec2  uTexOffset;
    float uTexScaleV;
    float uAlpha;
    vec2  uTexUvOffset;
    vec2  uTexUvRepeat;
    vec2  uTexUvMirror;
    int   uUseTexture;
    int   _pad0;
};

layout(set = 0, binding = 1) uniform sampler2D uTextureSampler;

void main() {
    float alpha = uAlpha;
    if (uUseTexture != 0)
        alpha *= texture(uTextureSampler, vTexCoord).a;

    if (alpha < 0.01)
        discard;
}
