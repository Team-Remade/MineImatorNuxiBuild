#version 330 core

layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aUV;

out vec2 vUV;

uniform int uMode;
uniform vec2 uViewportSize;
uniform vec2 uImageSize;
uniform float uUserScale;
uniform float uUserRotationRadians;
uniform vec2 uUserOffset;

void main()
{
    vec2 scaledPos = aPos;
    vec2 baseScale = vec2(1.0);

    if (uMode != 0)
    {
        float viewportW = max(uViewportSize.x, 1.0);
        float viewportH = max(uViewportSize.y, 1.0);
        float imageW = max(uImageSize.x, 1.0);
        float imageH = max(uImageSize.y, 1.0);

        vec2 originalScale = vec2(imageW / viewportW, imageH / viewportH);
        baseScale = originalScale;

        if (uMode == 1)
        {
            float fitFactor = 1.0 / max(originalScale.x, originalScale.y);
            baseScale *= fitFactor;
        }
    }

    float userScale = max(uUserScale, 0.0001);
    vec2 transformed = scaledPos * baseScale * userScale;

    float c = cos(uUserRotationRadians);
    float s = sin(uUserRotationRadians);
    mat2 rotation = mat2(c, -s, s, c);
    transformed = rotation * transformed;

    scaledPos = transformed + uUserOffset;

    gl_Position = vec4(scaledPos, 0.0, 1.0);
    vUV = aUV;
}
