#version 330 core

uniform sampler2D uScene;
uniform sampler2D uGlow;
uniform sampler2D uBloom;
uniform sampler2D uDirt;
uniform vec2 uTexelSize;
uniform vec2 uStep;
uniform float uGlowStrength;
uniform bool uAffectedByBloom;
uniform bool uAffectedByGlow;
uniform float uIntensity;
uniform float uPower;
uniform bool uTransparentOutput;
uniform int uPass;

out vec4 FragColor;

void main()
{
    vec2 uv = gl_FragCoord.xy * uTexelSize;
    if (uPass == 0)
    {
        vec3 light = vec3(0.0);
        if (uAffectedByBloom) light += texture(uBloom, uv).rgb;
        if (uAffectedByGlow) light += texture(uGlow, uv).rgb * uGlowStrength;
        FragColor = vec4(light, 0.0);
        return;
    }
    if (uPass == 1)
    {
        vec3 light = texture(uScene, uv).rgb * 0.22702703;
        light += texture(uScene, uv + uStep).rgb * 0.19459459;
        light += texture(uScene, uv - uStep).rgb * 0.19459459;
        light += texture(uScene, uv + uStep * 2.0).rgb * 0.12162162;
        light += texture(uScene, uv - uStep * 2.0).rgb * 0.12162162;
        light += texture(uScene, uv + uStep * 3.0).rgb * 0.05405405;
        light += texture(uScene, uv - uStep * 3.0).rgb * 0.05405405;
        light += texture(uScene, uv + uStep * 4.0).rgb * 0.01621622;
        light += texture(uScene, uv - uStep * 4.0).rgb * 0.01621622;
        FragColor = vec4(light, 0.0);
        return;
    }

    vec4 scene = texture(uScene, uv);
    vec4 dirt = texture(uDirt, uv);
    vec3 light = max(texture(uBloom, uv).rgb, vec3(0.0));
    vec3 halo = pow(light, vec3(uPower)) * dirt.rgb * dirt.a * uIntensity;
    if (max(max(halo.r, halo.g), halo.b) <= 0.0)
    {
        FragColor = scene;
        return;
    }
    if (uTransparentOutput)
    {
        float coverage = clamp(max(max(halo.r, halo.g), halo.b), 0.0, 1.0);
        float alpha = scene.a + coverage * (1.0 - scene.a);
        FragColor = vec4((scene.rgb * scene.a + halo) / max(alpha, 0.00001), alpha);
    }
    else
        FragColor = vec4(scene.rgb + halo, scene.a);
}
