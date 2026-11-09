#version 330 core

uniform sampler2D uScene;
uniform sampler2D uWatermark;
uniform vec2 uFrameSize;
uniform vec4 uRect;
uniform float uOpacity;
out vec4 FragColor;

void main()
{
    vec4 scene = texture(uScene, gl_FragCoord.xy / uFrameSize);
    vec2 pixel = vec2(gl_FragCoord.x, uFrameSize.y - gl_FragCoord.y);
    vec2 uv = (pixel - uRect.xy) / uRect.zw;
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThanEqual(uv, vec2(1.0))))
    {
        FragColor = scene;
        return;
    }
    vec2 halfTexel = 0.5 / vec2(textureSize(uWatermark, 0));
    vec4 mark = texture(uWatermark, clamp(vec2(uv.x, 1.0 - uv.y), halfTexel, 1.0 - halfTexel));
    float alpha = mark.a * uOpacity;
    float outputAlpha = alpha + scene.a * (1.0 - alpha);
    // The scene has already been resolved to straight alpha.
    vec3 color = mark.rgb * alpha + scene.rgb * scene.a * (1.0 - alpha);
    FragColor = vec4(outputAlpha > 0.00001 ? color / outputAlpha : vec3(0.0), outputAlpha);
}
