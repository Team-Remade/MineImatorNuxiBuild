#version 330 core

in vec3 vNormal;
in vec3 vFragPos;
in vec2 vTexCoord;

uniform vec3 uAlbedo;
uniform vec3 uLightDir;
uniform vec3 uLightColor;
uniform vec3 uAmbient;

// Texture support
uniform sampler2D uTexture;
uniform bool      uUseTexture;

out vec4 FragColor;

void main() {
    vec3 norm    = normalize(vNormal);
    float diff   = max(dot(norm, normalize(uLightDir)), 0.0);
    vec3 diffuse = diff * uLightColor;

    vec3 baseColor = uAlbedo;
    float alpha    = 1.0;

    if (uUseTexture) {
        vec4 texSample = texture(uTexture, vTexCoord);
        baseColor = texSample.rgb;
        alpha     = texSample.a;
    }

    vec3 result = (uAmbient + diffuse) * baseColor;
    FragColor   = vec4(result, alpha);
}
