#version 330 core
out vec4 FragColor;

uniform sampler2D uScene;
uniform float uSize;

void main()
{
    ivec2 resolution = textureSize(uScene, 0);
    vec2 origin = floor(gl_FragCoord.xy / uSize) * uSize;
    vec2 end = min(origin + vec2(uSize), vec2(resolution));
    ivec2 samplePixel = min(ivec2(floor((origin + end) * 0.5)), resolution - 1);
    FragColor = texelFetch(uScene, samplePixel, 0);
}
