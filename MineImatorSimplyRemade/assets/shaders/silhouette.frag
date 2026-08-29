#version 450

layout(location = 0) in vec2 vTexCoord;

// Same layout as pick.frag's PickMaterial (uPickColor unused here) so both
// passes can share one C# uniform struct/buffer when drawing the same mesh
// through both pipelines back-to-back.
layout(set = 0, binding = 1, std140) uniform PickMaterial {
    vec3  uPickColor;
    float uAlpha;
    vec4  uBlendColor;
    int   uUseTexture;
    int   uUseAlphaMask;
    int   uForceOpaque;
    int   _pad0;
};

layout(set = 0, binding = 2) uniform texture2D uTextureTexture;
layout(set = 0, binding = 3) uniform sampler uTextureSampler;
layout(set = 0, binding = 4) uniform texture2D uAlphaMaskTexture;
layout(set = 0, binding = 5) uniform sampler uAlphaMaskSampler;

layout(location = 0) out float FragMask;

void main() {
    float alpha = uForceOpaque != 0 ? 1.0 : (uAlpha * uBlendColor.a);

    if (uForceOpaque == 0 && uUseTexture != 0) {
        vec4 texSample = texture(sampler2D(uTextureTexture, uTextureSampler), vTexCoord);
        alpha *= texSample.a;
    }
    if (uForceOpaque == 0 && uUseAlphaMask != 0)
        alpha *= texture(sampler2D(uAlphaMaskTexture, uAlphaMaskSampler), vTexCoord).a;

    if (alpha <= 0.0)
        discard;

    FragMask = 1.0;
}
