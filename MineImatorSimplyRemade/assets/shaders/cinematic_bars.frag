#version 330 core
out vec4 FragColor;

uniform vec2 uResolution;
uniform float uSize;
uniform float uRotation;
uniform float uOffset;
uniform vec3 uColor;

void main()
{
    vec2 position = vec2(gl_FragCoord.x, uResolution.y - gl_FragCoord.y) - uResolution * 0.5;
    float distance = dot(position, vec2(-sin(uRotation), cos(uRotation))) / uResolution.y;
    if (abs(distance - uOffset) < 0.5 - uSize)
        discard;
    FragColor = vec4(uColor, 1.0);
}
