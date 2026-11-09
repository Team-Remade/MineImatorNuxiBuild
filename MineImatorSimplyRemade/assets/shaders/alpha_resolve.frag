#version 330 core

uniform sampler2D uScene;
uniform vec2 uTexelSize;
out vec4 FragColor;

void main()
{
    vec4 scene = texture(uScene, gl_FragCoord.xy * uTexelSize);
    FragColor = vec4(scene.a > 0.00001 ? scene.rgb / scene.a : vec3(0.0), scene.a);
}
