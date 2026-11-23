#version 330 core

uniform sampler2D uNuxT;
uniform vec2 uOutputSize;

out vec4 FragColor;

void main()
{
    vec2 uv = gl_FragCoord.xy / uOutputSize;
    FragColor = texture(uNuxT, uv);
}