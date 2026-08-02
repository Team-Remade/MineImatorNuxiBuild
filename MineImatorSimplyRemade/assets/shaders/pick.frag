#version 330 core

in vec2 vTexCoord;

uniform vec3  uPickColor;
uniform vec4  uBlendColor;
uniform float uAlpha;
uniform sampler2D uTexture;
uniform bool  uUseTexture;

out vec4 FragColor;

void main() {
    float alpha = uAlpha * uBlendColor.a;

    if (uUseTexture) {
        vec4 texSample = texture(uTexture, vTexCoord);
        alpha *= texSample.a;
    }

    if (alpha <= 0.0)
        discard;

    FragColor = vec4(uPickColor, 1.0);
}
