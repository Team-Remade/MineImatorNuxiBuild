#version 330 core

in vec3 vWorldPos;
in vec2 vTexCoord;

uniform vec3  uLightPos;
uniform float uFarPlane;
uniform sampler2D uTexture;
uniform bool  uUseTexture;
uniform float uAlpha;

void main() {
    float alpha = uAlpha;
    if (uUseTexture)
        alpha *= texture(uTexture, vTexCoord).a;

    if (alpha < 0.01)
        discard;

    float lightDistance = length(vWorldPos - uLightPos);
    gl_FragDepth = lightDistance / uFarPlane;
}