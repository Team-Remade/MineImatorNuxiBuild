#version 330 core

in vec2 vTexCoord;

uniform sampler2D uTexture;
uniform bool  uUseTexture;
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

void main() {
	float alpha = uAlpha;
	if (uUseTexture)
		alpha *= texture(uTexture, vTexCoord).a;

	if (alpha <= alphaCutoff())
		discard;
}