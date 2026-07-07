#version 330 core

in vec2 vTexCoord;

uniform sampler2D uTexture;
uniform bool  uUseTexture;
uniform float uAlpha;

void main() {
	float alpha = uAlpha;
	if (uUseTexture)
		alpha *= texture(uTexture, vTexCoord).a;

	if (alpha < 0.01)
		discard;
}