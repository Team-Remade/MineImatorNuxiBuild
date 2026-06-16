#version 330 core

in vec3 vNormal;
in vec3 vFragPos;

uniform vec3 uAlbedo;
uniform vec3 uLightDir;
uniform vec3 uLightColor;
uniform vec3 uAmbient;

out vec4 FragColor;

void main() {
    vec3 norm    = normalize(vNormal);
    float diff   = max(dot(norm, normalize(uLightDir)), 0.0);
    vec3 diffuse = diff * uLightColor;
    vec3 result  = (uAmbient + diffuse) * uAlbedo;
    FragColor    = vec4(result, 1.0);
}
