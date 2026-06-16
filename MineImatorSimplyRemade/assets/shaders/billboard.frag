#version 330 core

in vec2 vTexCoord;

uniform sampler2D uTexture;
uniform vec4      uTint;   // multiplied with the texture sample (use vec4(1) for no tint)

out vec4 FragColor;

void main() {
    vec4 sample = texture(uTexture, vTexCoord);
    // Premultiply tint; discard fully transparent pixels so depth isn't written.
    FragColor = sample * uTint;
    if (FragColor.a < 0.01) discard;
}
