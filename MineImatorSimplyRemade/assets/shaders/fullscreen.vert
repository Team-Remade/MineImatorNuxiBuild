#version 450

// Full-screen triangle trick: draw 3 vertices with no vertex buffer at all.
// gl_VertexIndex 0,1,2 maps to a triangle that covers the entire NDC quad -
// same technique the old renderer's edge.vert already used, extracted here
// as a shared vertex shader for every screen-space post-process pass
// (ambient occlusion, indirect lighting, indirect denoise, and future
// glow/film-grain/edge passes once those are ported).
void main() {
    vec2 pos[3];
    pos[0] = vec2(-1.0, -1.0);
    pos[1] = vec2( 3.0, -1.0);
    pos[2] = vec2(-1.0,  3.0);
    gl_Position = vec4(pos[gl_VertexIndex], 0.0, 1.0);
}
