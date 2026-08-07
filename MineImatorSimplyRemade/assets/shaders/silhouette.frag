#version 330 core

in vec2 vTexCoord;

uniform vec4  uBlendColor;
uniform float uAlpha;
uniform sampler2D uTexture;
uniform bool  uUseTexture;
uniform bool  uForceOpaque;

out float FragMask;

void main() {
    float alpha = uForceOpaque ? 1.0 : (uAlpha * uBlendColor.a);

    if (!uForceOpaque && uUseTexture) {
        vec4 texSample = texture(uTexture, vTexCoord);
        alpha *= texSample.a;
    }

    if (alpha <= 0.0)
        discard;

    // Write 1.0 wherever a selected object covers a pixel.
    FragMask = 1.0;
}
