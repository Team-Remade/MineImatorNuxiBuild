#version 450

// MIGRATION NOTE (subsystem pass 3/N - "shadow passes: directional map"):
// skinning intentionally dropped from this pass too (same reason as
// simple.vert - see its migration note); reintroduced in the "skinning +
// instancing" follow-up pass alongside simple.vert/frag. aNormal (location 1)
// is unused here but kept in the input layout so this shader can bind
// VeldridMesh's existing interleaved position/normal/uv vertex buffer without
// needing a second, depth-pass-only vertex layout/buffer.
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoord;

layout(set = 0, binding = 0, std140) uniform ShadowDepthUniforms {
    mat4  uMVP;
    vec2  uTexOffset;
    float uTexScaleV;
    float uAlpha;
    vec2  uTexUvOffset;
    vec2  uTexUvRepeat;
    vec2  uTexUvMirror;
    int   uUseTexture;
    int   _pad0;
};

layout(location = 0) out vec2 vTexCoord;

vec2 applyUvTransform(vec2 uv)
{
    vec2 repeated = uv * uTexUvRepeat;

    if (uTexUvMirror.x > 0.5)
        repeated.x = abs(fract(repeated.x * 0.5) * 2.0 - 1.0);

    if (uTexUvMirror.y > 0.5)
        repeated.y = abs(fract(repeated.y * 0.5) * 2.0 - 1.0);

    return repeated + uTexUvOffset;
}

void main() {
    vec2 baseUv = vec2(aTexCoord.x, aTexCoord.y * uTexScaleV + uTexOffset.y);
    vTexCoord = applyUvTransform(baseUv);
    gl_Position = uMVP * vec4(aPos, 1.0);
}
