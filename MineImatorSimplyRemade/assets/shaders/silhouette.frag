#version 330 core

in vec2 vTexCoord;

uniform vec4  uBlendColor;
uniform float uAlpha;
uniform bool  uAlphaClipEnabled;
uniform float uAlphaClipThreshold;
uniform bool  uAlphaHashed;

float alphaCutoff()
{
    if (!uAlphaClipEnabled) return 0.0;
    if (!uAlphaHashed) return uAlphaClipThreshold;
    return fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
}
uniform sampler2D uTexture;
uniform bool  uUseTexture;
uniform sampler2D uAlphaMask;
uniform bool  uUseAlphaMask;
uniform bool  uForceOpaque;

out float FragMask;

void main() {
    float alpha = uForceOpaque ? 1.0 : (uAlpha * uBlendColor.a);

    if (!uForceOpaque && uUseTexture) {
        vec4 texSample = texture(uTexture, vTexCoord);
        alpha *= texSample.a;
    }
    if (!uForceOpaque && uUseAlphaMask)
        alpha *= texture(uAlphaMask, vTexCoord).a;

    if (alpha <= alphaCutoff())
        discard;

    // Write 1.0 wherever a selected object covers a pixel.
    FragMask = 1.0;
}
