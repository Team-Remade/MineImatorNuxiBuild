#version 330 core

out vec4 FragColor;

uniform sampler2D uScene;
uniform vec2 uResolution;
uniform float uRadius;
uniform float uSoftness;
uniform float uStrength;
uniform vec3 uColor;

void main()
{
    vec2 uv = gl_FragCoord.xy / uResolution;
    vec4 scene = texture(uScene, uv);
    // Image-relative ellipse: the center is zero and every corner is one.
    float distance = length((uv - 0.5) * 2.0) / sqrt(2.0);
    float radius = clamp(uRadius * 0.01, 0.0, 1.0);
    float width = (1.0 - radius) * clamp(uSoftness * 0.01, 0.0, 1.0);
    float mask = radius >= 1.0 ? 0.0
        : width <= 0.0 ? step(radius, distance)
        : smoothstep(radius, radius + width, distance);
    float amount = mask * clamp(uStrength * 0.01, 0.0, 1.0);
    FragColor = vec4(mix(scene.rgb, clamp(uColor, 0.0, 1.0), amount), scene.a);
}
