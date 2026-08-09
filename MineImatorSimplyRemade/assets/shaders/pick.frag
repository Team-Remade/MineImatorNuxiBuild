#version 330 core

in vec2 vTexCoord;

uniform vec3  uPickColor;
uniform vec4  uBlendColor;
uniform float uAlpha;
uniform sampler2D uTexture;
uniform bool  uUseTexture;
uniform sampler2D uAlphaMask;
uniform bool  uUseAlphaMask;
uniform bool  uForceOpaque;

out vec4 FragColor;

void main() {
    float alpha = uForceOpaque ? 1.0 : (uAlpha * uBlendColor.a);

    if (!uForceOpaque && uUseTexture) {
        vec4 texSample = texture(uTexture, vTexCoord);
        alpha *= texSample.a;
    }
    if (!uForceOpaque && uUseAlphaMask)
        alpha *= texture(uAlphaMask, vTexCoord).a;

    if (alpha <= 0.0)
        discard;

    FragColor = vec4(uPickColor, 1.0);
}
