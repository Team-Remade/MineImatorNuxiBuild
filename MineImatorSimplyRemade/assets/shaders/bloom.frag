#version 330 core

uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform vec2 uTexelSize;
uniform vec2 uStep;
uniform float uThreshold;
uniform float uIntensity;
uniform vec3 uBlendColor;
uniform int uBlendMode;
uniform int uPass;

out vec4 FragColor;

void main()
{
    vec2 uv = gl_FragCoord.xy * uTexelSize;
    vec4 scene = texture(uScene, uv);
    if (uPass == 0)
    {
        float brightness = max(max(scene.r, scene.g), scene.b);
        float coverage = max(brightness - uThreshold, 0.0) / max(brightness, 0.00001);
        // Keep coverage with the premultiplied light so non-additive modes only affect highlights.
        FragColor = vec4(scene.rgb * coverage, coverage);
        return;
    }
    if (uPass == 1)
    {
        vec4 blur = scene * 0.22702703;
        blur += texture(uScene, uv + uStep) * 0.19459459;
        blur += texture(uScene, uv - uStep) * 0.19459459;
        blur += texture(uScene, uv + uStep * 2.0) * 0.12162162;
        blur += texture(uScene, uv - uStep * 2.0) * 0.12162162;
        blur += texture(uScene, uv + uStep * 3.0) * 0.05405405;
        blur += texture(uScene, uv - uStep * 3.0) * 0.05405405;
        blur += texture(uScene, uv + uStep * 4.0) * 0.01621622;
        blur += texture(uScene, uv - uStep * 4.0) * 0.01621622;
        FragColor = blur;
        return;
    }

    vec4 bloom = texture(uBloom, uv);
    vec3 light = bloom.rgb * uBlendColor;
    vec3 result;
    if (uBlendMode == 1)
        result = scene.rgb + light * uIntensity;
    else
    {
        vec3 color = light / max(bloom.a, 0.00001);
        vec3 blended = color;
        if (uBlendMode == 2)
            blended = scene.rgb + (1.0 - clamp(scene.rgb, 0.0, 1.0)) * color;
        else if (uBlendMode == 3)
            blended = scene.rgb * color;
        else if (uBlendMode == 4)
            blended = mix(2.0 * scene.rgb * color,
                1.0 - 2.0 * (1.0 - scene.rgb) * (1.0 - color), step(vec3(0.5), scene.rgb));
        result = mix(scene.rgb, blended, clamp(bloom.a * uIntensity, 0.0, 1.0));
    }
    FragColor = vec4(result, scene.a);
}
