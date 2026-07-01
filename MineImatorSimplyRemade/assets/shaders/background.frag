#version 330 core

in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uBackgroundTex;

void main()
{
    FragColor = texture(uBackgroundTex, vUV);
}
