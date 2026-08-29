#version 450

// Shared by pick.frag (color-coded object picking) and silhouette.frag
// (selection mask for the edge-outline pass) - both need the same UV-transformed
// geometry, just different fragment outputs. Skinning dropped, same as every
// other pass this migration (see simple.vert's migration note).
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoord;

layout(set = 0, binding = 0, std140) uniform PickUniforms {
    mat4  uMVP;
    vec2  uTexOffset;
    float uTexScaleV;
    float _pad0;
    vec2  uTexUvOffset;
    vec2  uTexUvRepeat;
    vec2  uTexUvMirror;
    vec2  _pad1;
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
