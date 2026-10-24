#version 330 core

in vec2 vTexCoord;

uniform sampler2D uTexture;
uniform bool  uUseTexture;
uniform float uAlpha;
uniform bool  uAlphaClipEnabled;
uniform float uAlphaClipThreshold;

void main() {
	float alpha = uAlpha;
	if (uUseTexture)
		alpha *= texture(uTexture, vTexCoord).a;

	if (alpha <= (uAlphaClipEnabled ? uAlphaClipThreshold : 0.0))
		discard;
}