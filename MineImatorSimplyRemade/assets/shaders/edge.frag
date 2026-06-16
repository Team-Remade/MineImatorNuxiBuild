#version 330 core

// The single-channel silhouette mask (white = selected object).
uniform sampler2D uMask;
// Viewport size in pixels, used to compute per-texel offsets.
uniform vec2 uTexelSize;
// Colour to paint the detected edges.
uniform vec4 uEdgeColor;
// Minimum Sobel gradient magnitude to treat as an edge (0–1 range).
uniform float uThreshold;

out vec4 FragColor;

// Sample the mask at a texel offset from the current fragment.
float sample(vec2 uv, float dx, float dy) {
    return texture(uMask, uv + vec2(dx, dy) * uTexelSize).r;
}

void main() {
    // Reconstruct UV from gl_FragCoord (no varyings in the full-screen triangle).
    // gl_FragCoord is in window space (pixels); divide by viewport size = [0,1].
    // uTexelSize = 1.0 / viewport so we can derive size from it.
    vec2 uv = gl_FragCoord.xy * uTexelSize;

    // 3×3 Sobel kernels
    //  Gx            Gy
    // -1  0 +1      -1 -2 -1
    // -2  0 +2       0  0  0
    // -1  0 +1      +1 +2 +1

    float tl = sample(uv, -1.0,  1.0);
    float tm = sample(uv,  0.0,  1.0);
    float tr = sample(uv,  1.0,  1.0);
    float ml = sample(uv, -1.0,  0.0);
    float mr = sample(uv,  1.0,  0.0);
    float bl = sample(uv, -1.0, -1.0);
    float bm = sample(uv,  0.0, -1.0);
    float br = sample(uv,  1.0, -1.0);

    float gx = -tl + tr - 2.0*ml + 2.0*mr - bl + br;
    float gy = -tl - 2.0*tm - tr + bl + 2.0*bm + br;

    float magnitude = sqrt(gx*gx + gy*gy);

    if (magnitude < uThreshold) discard;

    // Scale alpha by magnitude so stronger gradients are more opaque.
    float alpha = clamp(magnitude, 0.0, 1.0) * uEdgeColor.a;
    FragColor = vec4(uEdgeColor.rgb, alpha);
}
