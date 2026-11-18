// Packed, topologically sorted instructions from ShaderGraphProgram.
uniform int uShaderGraphEnabled, uShaderGraphCount;
uniform vec4 uShaderGraphOps[32], uShaderGraphParams[32];
uniform ivec4 uShaderGraphOutputs;
uniform int uShaderGraphHeight;

struct NodeSurface {
    vec3 color;
    float roughness;
    float metallic;
    vec3 emission;
    float height;
};

float graphValue(vec3 v) { return dot(v, vec3(0.2126, 0.7152, 0.0722)); }

vec3 graphHash(vec3 p) {
    p = fract(p * vec3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return fract((p.xxy + p.yxx) * p.zyx);
}

float graphNoise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(mix(graphHash(i).x, graphHash(i + vec3(1,0,0)).x, f.x),
            mix(graphHash(i + vec3(0,1,0)).x, graphHash(i + vec3(1,1,0)).x, f.x), f.y),
        mix(mix(graphHash(i + vec3(0,0,1)).x, graphHash(i + vec3(1,0,1)).x, f.x),
            mix(graphHash(i + vec3(0,1,1)).x, graphHash(i + vec3(1,1,1)).x, f.x), f.y), f.z);
}

float graphFractal(vec3 p) {
    return (graphNoise(p) * 4.0 + graphNoise(p * 2.03 + 17.0) * 2.0 +
            graphNoise(p * 4.07 + 31.0)) / 7.0;
}

float graphCellEdge(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    float first = 8.0, second = 8.0;
    for (int z = -1; z <= 1; ++z)
    for (int y = -1; y <= 1; ++y)
    for (int x = -1; x <= 1; ++x) {
        vec3 cell = vec3(x, y, z);
        vec3 r = cell + graphHash(i + cell) - f;
        float distance2 = dot(r, r);
        second = min(second, max(first, distance2));
        first = min(first, distance2);
    }
    return sqrt(second) - sqrt(first);
}

NodeSurface evaluateShaderGraph(vec3 position, vec2 uv) {
    vec3 values[32];
    for (int i = 0; i < uShaderGraphCount; ++i) {
        vec4 op = uShaderGraphOps[i], param = uShaderGraphParams[i];
        int ax = int(op.y), bx = int(op.z), cx = int(op.w);
        vec3 a = ax >= 0 ? values[ax] : vec3(0.0);
        vec3 b = bx >= 0 ? values[bx] : vec3(0.0);
        vec3 c = cx >= 0 ? values[cx] : vec3(1.0);
        vec3 coord = ax >= 0 ? a : position;
        vec3 p = coord * param.x;
        vec3 v = vec3(0.0);
        switch (int(op.x)) {
            case 1: v = vec3(param.x); break;
            case 2: v = param.xyz; break;
            case 3: v = position; break;
            case 4: v = vec3(uv, 0.0); break;
            case 5: v = vec3(graphFractal(p)); break;
            case 6: v = vec3(0.5 + 0.5 * sin(p.x + p.y * 0.38 + p.z * 0.25 +
                       param.y * (graphFractal(coord * abs(param.x) * 0.35) - 0.5))); break;
            case 7: v = vec3(graphCellEdge(p)); break;
            case 8: v = vec3(mod(dot(floor(p), vec3(1.0)), 2.0)); break;
            case 9: v = mix(b, c, clamp((graphValue(a) - param.x) / max(param.y - param.x, 0.001), 0.0, 1.0)); break;
            case 10: v = mix(b, c, clamp(graphValue(a), 0.0, 1.0)); break;
            case 11: v = a + b; break;
            case 12: v = a * (bx >= 0 ? b : vec3(1.0)); break;
            case 13: v = p + param.yzw; break;
        }
        values[i] = clamp(v, vec3(-65536.0), vec3(65536.0));
    }
    ivec4 o = uShaderGraphOutputs;
    return NodeSurface(
        o.x >= 0 ? clamp(values[o.x], 0.0, 1.0) : vec3(0.7),
        o.y >= 0 ? clamp(graphValue(values[o.y]), 0.045, 1.0) : 0.5,
        o.z >= 0 ? clamp(graphValue(values[o.z]), 0.0, 1.0) : 0.0,
        o.w >= 0 ? clamp(values[o.w], 0.0, 16.0) : vec3(0.0),
        uShaderGraphHeight >= 0 ? graphValue(values[uShaderGraphHeight]) : 0.0);
}

vec3 graphBump(vec3 normal, vec3 position, float height) {
    vec3 dx = dFdx(position), dy = dFdy(position);
    vec3 u = cross(dy, normal), v = cross(normal, dx);
    float determinant = dot(dx, u);
    vec3 gradient = (dFdx(height) * u + dFdy(height) * v) * sign(determinant);
    vec3 bumped = abs(determinant) * normal - 0.025 * gradient;
    return dot(bumped, bumped) > 1e-20 ? normalize(bumped) : normal;
}
