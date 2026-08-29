#version 450

// Billboard vertex shader: expands a unit quad's corners (in [-0.5, 0.5]
// local space) into world space using the camera's right/up vectors so the
// quad always faces the camera (spherical billboard) - used for light icons.
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec2 aTexCoord;

layout(set = 0, binding = 0, std140) uniform BillboardUniforms {
    mat4  uView;
    mat4  uProj;
    vec3  uWorldPos;
    float uSize;
    vec4  uTint;
};

layout(location = 0) out vec2 vTexCoord;

void main() {
    vec3 camRight = vec3(uView[0][0], uView[1][0], uView[2][0]);
    vec3 camUp    = vec3(uView[0][1], uView[1][1], uView[2][1]);

    vec3 worldCorner = uWorldPos
                     + camRight * aPos.x * uSize
                     + camUp    * aPos.y * uSize;

    gl_Position = uProj * uView * vec4(worldCorner, 1.0);
    vTexCoord   = aTexCoord;
}
