// Interpreter for ShaderGraphProgram data. See ShaderGraph.cs for the RGBA32F texel layout.
// simple.frag includes it after its material uniforms (uTexture, uAlbedo, uAlpha, uCameraPosition).
// nuxt.comp defines GRAPH_COMPUTE first and includes it after SampleTexture and the camera uniforms: there
// each graph owns one row of uShaderGraphData and the per-material state below is set per hit.
#ifndef GRAPH_COMPUTE
uniform int uShaderGraphEnabled, uShaderGraphCount, uShaderGraphOutputStart, uShaderGraphFlags;
uniform sampler2D uShaderGraphData;
uniform float uShaderGraphFrame;
uniform mat4 uModel;
uniform mat4 uShaderGraphView, uShaderGraphViewProj;
uniform vec3 uShaderGraphBoundsMin, uShaderGraphBoundsSize;
#else
// Graph rows come first; path polylines follow in the same texture, addressed by absolute row.
layout(binding = 15) uniform sampler2D uShaderGraphData;
int gGraphRow = 0, gGraphCount = 0, gGraphOutputStart = 0, gGraphFlags = 0;
float gGraphFrame = 0.0;
ivec4 gGraphPaths = ivec4(-1);
ivec4 gGraphTexture = ivec4(-1);
mat4 uModel = mat4(1.0);
vec3 uShaderGraphBoundsMin = vec3(0.0), uShaderGraphBoundsSize = vec3(1.0);
vec3 uAlbedo = vec3(1.0);
float uAlpha = 1.0;
#define uShaderGraphCount gGraphCount
#define uShaderGraphOutputStart gGraphOutputStart
#define uShaderGraphFlags gGraphFlags
#define uShaderGraphFrame gGraphFrame
#endif

#define GRAPH_REGISTERS 64
#define GRAPH_PI 3.14159265359
#define GRAPH_BUMP_LINKED 1
#define GRAPH_VOLUME 2
#define GRAPH_PATH_SAMPLES 64
// Extinction per block of a fully dense (100%) fog volume with Scattering plus Absorption of 1.
#define GRAPH_VOLUME_DENSITY 4.0

struct NodeSurface {
    vec3 color;
    float roughness;
    float metallic;
    vec3 emission;
    float height;
    float alpha;
    float ior;
    float transmission;
    float specular;
    float bumpStrength;
    float refractionDepth;
};

vec3 gGraphRegs[GRAPH_REGISTERS];
vec3 gGraphPosition;
vec2 gGraphUv;
// Fog volumes that ignore their object's scale run the graph at the object-space position multiplied by this, so
// one unit is one world block along each object axis. Generated coordinates scale outward from the centre of the
// object's bounds by the same factor (graphGeneratedCoordinates). The fog code sets it around runShaderGraph and
// resets it to 1 afterwards.
vec3 gGraphPositionScale = vec3(1.0);

// Scale of an object-to-world matrix along each of its axes, never zero.
vec3 graphMatrixScale(mat4 m) {
    return max(vec3(length(m[0].xyz), length(m[1].xyz), length(m[2].xyz)), vec3(1e-6));
}

float graphValue(vec3 v) { return dot(v, vec3(0.2126, 0.7152, 0.0722)); }

#ifdef GRAPH_COMPUTE
vec4 graphFetch(int i) { return texelFetch(uShaderGraphData, ivec2(i, gGraphRow), 0); }
#else
vec4 graphFetch(int i) { return texelFetch(uShaderGraphData, ivec2(i, 0), 0); }
#endif

vec3 graphInput(int at) {
    vec4 t = graphFetch(at);
    int s = int(t.x);
    if (s >= 1024) return vec3(graphValue(gGraphRegs[min(s - 1024, GRAPH_REGISTERS - 1)]));
    if (s >= 0) return gGraphRegs[min(s, GRAPH_REGISTERS - 1)];
    if (s == -2) return gGraphPosition;
    if (s == -3) return vec3(gGraphUv, 0.0);
    return t.yzw;
}

void graphStore(float reg, vec3 v) {
    int r = int(reg);
    if (r >= 0) gGraphRegs[min(r, GRAPH_REGISTERS - 1)] = clamp(v, vec3(-65536.0), vec3(65536.0));
}

// ── Host-specific inputs ─────────────────────────────────────────────────────

#ifdef GRAPH_COMPUTE
vec3 graphViewSpace(vec3 world) {
    vec3 d = world - uCameraPosition;
    return vec3(dot(d, uCameraRight), dot(d, uCameraUp), -dot(d, uCameraForward));
}

vec3 graphWindow(vec3 world) {
    vec3 v = graphViewSpace(world);
    float depth = max(-v.z, 1e-6), halfHeight = tan(uFov * 0.5), aspect = float(uWidth) / float(uHeight);
    return vec3(v.x / (depth * halfHeight * aspect) * 0.5 + 0.5, v.y / (depth * halfHeight) * 0.5 + 0.5, 0.0);
}

vec4 graphImage(vec2 coordinate, int mode) {
    bool outside = any(lessThan(coordinate, vec2(0.0))) || any(greaterThan(coordinate, vec2(1.0)));
    coordinate = mode == 0 ? fract(coordinate) : clamp(coordinate, vec2(0.0), vec2(0.99999));
    vec4 texel = gGraphTexture.x >= 0 ? SampleTexture(coordinate, gGraphTexture) : vec4(1.0, 0.0, 1.0, 1.0);
    if (mode == 2 && outside) texel = vec4(0.0);
    return texel;
}

// Returns (visibility, distance, progress) for the part of the path between start and end. Each path is
// one row of GRAPH_PATH_SAMPLES points evenly spaced along its length. An unresolved path is everywhere.
vec3 graphPath(int slot, vec3 world, float radius, float falloff, float start, float end) {
    if (slot < 0 || slot > 3 || gGraphPaths[slot] < 0) return vec3(1.0, 0.0, 0.0);
    int row = gGraphPaths[slot];
    start = clamp(start, 0.0, 1.0);
    end = clamp(end, 0.0, 1.0);
    float best = 1e30, bestProgress = 0.0;
    vec3 previous = texelFetch(uShaderGraphData, ivec2(0, row), 0).xyz;
    for (int i = 1; i < GRAPH_PATH_SAMPLES; ++i) {
        vec3 next = texelFetch(uShaderGraphData, ivec2(i, row), 0).xyz;
        float s0 = float(i - 1) / float(GRAPH_PATH_SAMPLES - 1), s1 = float(i) / float(GRAPH_PATH_SAMPLES - 1);
        float a = max(s0, start), b = min(s1, end);
        if (a <= b) {
            vec3 pa = mix(previous, next, (a - s0) / (s1 - s0)), pb = mix(previous, next, (b - s0) / (s1 - s0));
            vec3 ab = pb - pa;
            float length2 = dot(ab, ab);
            float t = length2 > 1e-12 ? clamp(dot(world - pa, ab) / length2, 0.0, 1.0) : 0.0;
            float d = distance(world, pa + ab * t);
            if (d < best) { best = d; bestProgress = mix(a, b, t); }
        }
        previous = next;
    }
    float visibility = falloff > 0.0 ? 1.0 - smoothstep(radius, radius + falloff, best) : (best <= radius ? 1.0 : 0.0);
    return vec3(visibility, min(best, 65536.0), bestProgress);
}
#else
vec3 graphViewSpace(vec3 world) { return (uShaderGraphView * vec4(world, 1.0)).xyz; }

vec3 graphWindow(vec3 world) {
    vec4 clip = uShaderGraphViewProj * vec4(world, 1.0);
    return vec3(clip.xy / max(abs(clip.w), 1e-6) * sign(clip.w) * 0.5 + 0.5, 0.0);
}

vec4 graphImage(vec2 coordinate, int mode) {
    vec2 dx = dFdx(coordinate), dy = dFdy(coordinate);
    bool outside = any(lessThan(coordinate, vec2(0.0))) || any(greaterThan(coordinate, vec2(1.0)));
    coordinate = mode == 0 ? fract(coordinate) : clamp(coordinate, 0.0, 1.0);
    vec4 texel = uUseTexture ? textureGrad(uTexture, coordinate, dx, dy) : vec4(1.0, 0.0, 1.0, 1.0);
    if (mode == 2 && outside) texel = vec4(0.0);
    return texel;
}

// The viewport has no path data: every point counts as on the path.
vec3 graphPath(int slot, vec3 world, float radius, float falloff, float start, float end) {
    return vec3(1.0, 0.0, 0.0);
}
#endif

// ── Noise ────────────────────────────────────────────────────────────────────

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

// Fractal noise with a fractional octave count, normalized to 0..1.
float graphFbm(vec3 p, float detail, float roughness) {
    detail = clamp(detail, 0.0, 15.0);
    roughness = clamp(roughness, 0.0, 1.0);
    int octaves = int(detail);
    float amplitude = 1.0, total = 0.0, sum = 0.0, frequency = 1.0;
    for (int k = 0; k <= 15; ++k) {
        if (k > octaves) break;
        sum += graphNoise(p * frequency + float(k) * 13.7) * amplitude;
        total += amplitude;
        amplitude *= roughness;
        frequency *= 2.0;
    }
    float remainder = detail - float(octaves);
    if (remainder > 0.0) {
        float extra = graphNoise(p * frequency + float(octaves + 1) * 13.7) * amplitude;
        return mix(sum / total, (sum + extra) / (total + amplitude), remainder);
    }
    return sum / total;
}

// A fourth dimension morphs smoothly between offset 3D slices, e.g. when W is driven by Time.
float graphFbm4(vec3 p, float w, float detail, float roughness) {
    if (w == 0.0) return graphFbm(p, detail, roughness);
    float cell = floor(w), t = w - cell;
    t = t * t * (3.0 - 2.0 * t);
    const vec3 slice = vec3(19.19, 7.31, 13.77);
    return mix(graphFbm(p + mod(cell, 256.0) * slice, detail, roughness),
               graphFbm(p + mod(cell + 1.0, 256.0) * slice, detail, roughness), t);
}

float graphNoiseTexture(vec3 p, float detail, float roughness, float distortion, float w, bool wantColor, out vec3 color) {
    if (distortion != 0.0)
        p += distortion * (vec3(graphNoise(p + vec3(13.5)), graphNoise(p + vec3(-7.2, 31.1, 5.4)),
                                graphNoise(p + vec3(23.7, -11.3, 3.9))) * 2.0 - 1.0);
    float fac = graphFbm4(p, w, detail, roughness);
    color = wantColor ? vec3(fac, graphFbm4(p + vec3(31.4, 7.7, 19.3), w, detail, roughness),
                             graphFbm4(p + vec3(-17.9, 23.1, 41.6), w, detail, roughness)) : vec3(fac);
    return fac;
}

float graphWave(vec3 p, int type, int direction, int profile, float distortion, float detail,
                float detailScale, float detailRoughness, float phase) {
    float n;
    if (type == 0) {
        n = direction == 0 ? p.x * 20.0 : direction == 1 ? p.y * 20.0 : direction == 2 ? p.z * 20.0
          : (p.x + p.y + p.z) * 10.0;
    } else {
        vec3 r = direction == 0 ? vec3(0.0, p.y, p.z) : direction == 1 ? vec3(p.x, 0.0, p.z)
               : direction == 2 ? vec3(p.x, p.y, 0.0) : p;
        n = length(r) * 20.0;
    }
    n += phase;
    if (distortion != 0.0) n += distortion * (graphFbm(p * detailScale, detail, detailRoughness) * 2.0 - 1.0);
    if (profile == 0) return 0.5 + 0.5 * sin(n - GRAPH_PI * 0.5);
    n /= 2.0 * GRAPH_PI;
    if (profile == 1) return n - floor(n);
    n -= floor(n + 0.5);
    return abs(n) * 2.0;
}

float graphMetric(vec3 d, int metric) {
    d = abs(d);
    return metric == 1 ? d.x + d.y + d.z : metric == 2 ? max(max(d.x, d.y), d.z) : length(d);
}

float graphVoronoi(vec3 coord, float randomness, float smoothness, int feature, int metric,
                   out vec3 color, out vec3 position) {
    vec3 cell = floor(coord), local = coord - cell;
    randomness = clamp(randomness, 0.0, 1.0);
    color = vec3(0.0);
    position = coord;
    if (feature == 3) {
        // Distance to the nearest cell border, measured along the bisector between cell points.
        vec3 closest = vec3(0.0);
        float best = 1e9;
        for (int z = -1; z <= 1; ++z) for (int y = -1; y <= 1; ++y) for (int x = -1; x <= 1; ++x) {
            vec3 o = vec3(x, y, z);
            vec3 v = o + graphHash(cell + o) * randomness - local;
            float d = dot(v, v);
            if (d < best) { best = d; closest = v; }
        }
        float edge = 1e9;
        for (int z = -1; z <= 1; ++z) for (int y = -1; y <= 1; ++y) for (int x = -1; x <= 1; ++x) {
            vec3 o = vec3(x, y, z);
            vec3 v = o + graphHash(cell + o) * randomness - local;
            vec3 between = v - closest;
            if (dot(between, between) > 1e-4) edge = min(edge, dot((closest + v) * 0.5, normalize(between)));
        }
        return edge;
    }
    if (feature == 2) {
        float s = max(clamp(smoothness, 0.0, 1.0) * 0.5, 1e-4);
        float smoothDistance = 8.0;
        vec3 smoothColor = vec3(0.0), smoothPosition = vec3(0.0);
        for (int z = -1; z <= 1; ++z) for (int y = -1; y <= 1; ++y) for (int x = -1; x <= 1; ++x) {
            vec3 o = vec3(x, y, z);
            vec3 point = o + graphHash(cell + o) * randomness;
            float d = graphMetric(point - local, metric);
            float h = smoothstep(0.0, 1.0, 0.5 + 0.5 * (smoothDistance - d) / s);
            float correction = s * h * (1.0 - h);
            smoothDistance = mix(smoothDistance, d, h) - correction;
            correction /= 1.0 + 3.0 * s;
            smoothColor = mix(smoothColor, graphHash(cell + o + 71.3), h) - correction;
            smoothPosition = mix(smoothPosition, point + cell, h) - correction;
        }
        color = smoothColor;
        position = smoothPosition;
        return smoothDistance;
    }
    float first = 1e9, second = 1e9;
    vec3 firstCell = vec3(0.0), secondCell = vec3(0.0), firstPoint = vec3(0.0), secondPoint = vec3(0.0);
    for (int z = -1; z <= 1; ++z) for (int y = -1; y <= 1; ++y) for (int x = -1; x <= 1; ++x) {
        vec3 o = vec3(x, y, z);
        vec3 point = o + graphHash(cell + o) * randomness;
        float d = graphMetric(point - local, metric);
        if (d < first) {
            second = first; secondCell = firstCell; secondPoint = firstPoint;
            first = d; firstCell = cell + o; firstPoint = point + cell;
        } else if (d < second) {
            second = d; secondCell = cell + o; secondPoint = point + cell;
        }
    }
    bool useSecond = feature == 1;
    color = graphHash((useSecond ? secondCell : firstCell) + 71.3);
    position = useSecond ? secondPoint : firstPoint;
    return useSecond ? second : first;
}

float graphGradient(vec3 p, int type) {
    float r;
    if (type == 0) r = p.x;
    else if (type == 1) { r = max(p.x, 0.0); r *= r; }
    else if (type == 2) { r = clamp(p.x, 0.0, 1.0); float t = r * r; r = 3.0 * t - 2.0 * t * r; }
    else if (type == 3) r = (p.x + p.y) * 0.5;
    else if (type == 4) r = max(0.999999 - length(p), 0.0);
    else if (type == 5) { r = max(0.999999 - length(p), 0.0); r *= r; }
    else r = atan(p.y, p.x) / (2.0 * GRAPH_PI) + 0.5;
    return clamp(r, 0.0, 1.0);
}

vec3 graphMagic(vec3 p, int depth, float distortion) {
    vec3 v = vec3(sin((p.x + p.y + p.z) * 5.0), cos((-p.x + p.y - p.z) * 5.0), -cos((-p.x - p.y + p.z) * 5.0));
    for (int i = 0; i < 10; ++i) {
        if (i >= depth) break;
        v *= distortion;
        int axis = i - 3 * (i / 3);
        if (axis == 0) v.y = -cos(v.x - v.y + v.z);
        else if (axis == 1) v.x = cos(v.x - v.y - v.z);
        else v.z = sin(-v.x - v.y - v.z);
    }
    if (distortion != 0.0) v /= 2.0 * distortion;
    return 0.5 - v;
}

// Returns (tint, mortar).
vec2 graphBrick(vec3 p, float mortarSize, float bias, float width, float height, float offset, float frequency) {
    width = max(width, 1e-4);
    height = max(height, 1e-4);
    float row = floor(p.y / height);
    float period = max(floor(frequency), 1.0);
    float shift = mod(row, period) < 0.5 ? offset * width : 0.0;
    float column = floor((p.x + shift) / width);
    float x = p.x + shift - column * width, y = p.y - row * height;
    float tint = clamp(graphHash(vec3(column, row, 3.7)).x + bias, 0.0, 1.0);
    float edge = min(min(x, y), min(width - x, height - y));
    return vec2(tint, edge < mortarSize ? 1.0 : 0.0);
}

// ── Color ────────────────────────────────────────────────────────────────────

vec3 graphRgbToHsv(vec3 c) {
    vec4 k = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(c.bg, k.wz), vec4(c.gb, k.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + 1e-10)), d / (q.x + 1e-10), q.x);
}

vec3 graphHsvToRgb(vec3 c) {
    vec3 p = abs(fract(c.xxx + vec3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
}

vec3 graphRgbToHsl(vec3 c) {
    float high = max(max(c.r, c.g), c.b), low = min(min(c.r, c.g), c.b);
    float l = (high + low) * 0.5, d = high - low;
    float s = d <= 0.0 ? 0.0 : d / max(1.0 - abs(2.0 * l - 1.0), 1e-6);
    return vec3(graphRgbToHsv(c).x, s, l);
}

vec3 graphHslToRgb(vec3 c) {
    vec3 rgb = clamp(abs(mod(c.x * 6.0 + vec3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0, 0.0, 1.0);
    return c.z + c.y * (rgb - 0.5) * (1.0 - abs(2.0 * c.z - 1.0));
}

vec3 graphBlend(int mode, float t, vec3 a, vec3 b) {
    float u = 1.0 - t;
    if (mode == 0) return mix(a, b, t);
    if (mode == 1) return mix(a, min(a, b), t);
    if (mode == 2) return mix(a, a * b, t);
    if (mode == 3) {
        vec3 tmp = u + t * b;
        return mix(vec3(0.0), clamp(1.0 - (1.0 - a) / max(tmp, 1e-6), 0.0, 1.0), step(1e-6, tmp));
    }
    if (mode == 4) return mix(a, max(a, b), t);
    if (mode == 5) return 1.0 - (u + t * (1.0 - b)) * (1.0 - a);
    if (mode == 6) {
        vec3 tmp = 1.0 - t * b;
        vec3 dodged = mix(vec3(1.0), min(a / max(tmp, 1e-6), 1.0), step(1e-6, tmp));
        return mix(a, dodged, step(1e-6, abs(a)));
    }
    if (mode == 7) return a + t * b;
    if (mode == 8) return mix(1.0 - (u + 2.0 * t * (1.0 - b)) * (1.0 - a), a * (u + 2.0 * t * b), step(a, vec3(0.4999999)));
    if (mode == 9) { vec3 screen = 1.0 - (1.0 - b) * (1.0 - a); return mix(a, (1.0 - a) * b * a + a * screen, t); }
    if (mode == 10) return a + t * (2.0 * b - 1.0);
    if (mode == 11) return mix(a, abs(a - b), t);
    if (mode == 12) return max(mix(a, a + b - 2.0 * a * b, t), 0.0);
    if (mode == 13) return a - t * b;
    if (mode == 14) return mix(a, u * a + t * a / max(abs(b), 1e-6) * sign(b), step(1e-6, abs(b)));
    vec3 hsvA = graphRgbToHsv(a), hsvB = graphRgbToHsv(b);
    if (mode == 15) { if (hsvB.y > 0.0) { hsvA.x = hsvB.x; return mix(a, graphHsvToRgb(hsvA), t); } return a; }
    if (mode == 16) { if (hsvA.y > 0.0) { hsvA.y = mix(hsvA.y, hsvB.y, t); return graphHsvToRgb(hsvA); } return a; }
    if (mode == 17) { if (hsvB.y > 0.0) { hsvA.xy = hsvB.xy; return mix(a, graphHsvToRgb(hsvA), t); } return a; }
    hsvA.z = mix(hsvA.z, hsvB.z, t);
    return graphHsvToRgb(hsvA);
}

vec3 graphRamp(float f, vec4 positions, int stops, int interpolation, vec3 c0, vec3 c1, vec3 c2, vec3 c3) {
    vec3 colors[4] = vec3[4](c0, c1, c2, c3);
    float p[4] = float[4](positions.x, positions.y, positions.z, positions.w);
    if (f <= p[0]) return c0;
    for (int k = 0; k < 3; ++k) {
        if (k + 1 >= stops) break;
        if (f < p[k + 1]) {
            if (interpolation == 1) return colors[k];
            float t = (f - p[k]) / max(p[k + 1] - p[k], 1e-6);
            if (interpolation == 2) t = t * t * (3.0 - 2.0 * t);
            return mix(colors[k], colors[k + 1], t);
        }
    }
    return colors[stops - 1];
}

vec3 graphBlackbody(float kelvin) {
    float t = clamp(kelvin, 800.0, 40000.0) / 100.0;
    vec3 c;
    c.r = t <= 66.0 ? 1.0 : clamp(1.29293618606 * pow(t - 60.0, -0.1332047592), 0.0, 1.0);
    c.g = t <= 66.0 ? clamp(0.39008157876 * log(t) - 0.63184144378, 0.0, 1.0)
                    : clamp(1.12989086089 * pow(t - 60.0, -0.0755148492), 0.0, 1.0);
    c.b = t >= 66.0 ? 1.0 : t <= 19.0 ? 0.0 : clamp(0.54320678911 * log(t - 10.0) - 1.19625408914, 0.0, 1.0);
    return c;
}

// ── Math and vectors ─────────────────────────────────────────────────────────

float graphSafeDivide(float a, float b) { return b != 0.0 ? a / b : 0.0; }

float graphSmoothMin(float a, float b, float c) {
    if (c == 0.0) return min(a, b);
    float h = max(c - abs(a - b), 0.0) / c;
    return min(a, b) - h * h * h * c * (1.0 / 6.0);
}

float graphMath(int op, float a, float b, float c) {
    switch (op) {
        case 0: return a + b;
        case 1: return a - b;
        case 2: return a * b;
        case 3: return graphSafeDivide(a, b);
        case 4: return a * b + c;
        case 5:
            if (a >= 0.0) return pow(a, b);
            if (fract(b) == 0.0) return pow(-a, b) * (mod(b, 2.0) == 0.0 ? 1.0 : -1.0);
            return 0.0;
        case 6: return a > 0.0 && b > 0.0 && b != 1.0 ? log(a) / log(b) : 0.0;
        case 7: return a > 0.0 ? sqrt(a) : 0.0;
        case 8: return a > 0.0 ? inversesqrt(a) : 0.0;
        case 9: return abs(a);
        case 10: return exp(min(a, 80.0));
        case 11: return min(a, b);
        case 12: return max(a, b);
        case 13: return a < b ? 1.0 : 0.0;
        case 14: return a > b ? 1.0 : 0.0;
        case 15: return sign(a);
        case 16: return abs(a - b) <= max(c, 1e-5) ? 1.0 : 0.0;
        case 17: return graphSmoothMin(a, b, c);
        case 18: return -graphSmoothMin(-a, -b, c);
        case 19: return floor(a + 0.5);
        case 20: return floor(a);
        case 21: return ceil(a);
        case 22: return trunc(a);
        case 23: return a - floor(a);
        case 24: return b != 0.0 ? a - b * trunc(a / b) : 0.0;
        case 25: return b != 0.0 ? a - b * floor(a / b) : 0.0;
        case 26: { float range = b - c; return range != 0.0 ? a - range * floor((a - c) / range) : c; }
        case 27: return b != 0.0 ? floor(a / b) * b : 0.0;
        case 28: return b != 0.0 ? abs(fract((a - b) / (b * 2.0)) * b * 2.0 - b) : 0.0;
        case 29: return sin(a);
        case 30: return cos(a);
        case 31: return tan(a);
        case 32: return asin(clamp(a, -1.0, 1.0));
        case 33: return acos(clamp(a, -1.0, 1.0));
        case 34: return atan(a);
        case 35: return atan(a, b);
        case 36: return sinh(clamp(a, -80.0, 80.0));
        case 37: return cosh(clamp(a, -80.0, 80.0));
        case 38: return tanh(a);
        case 39: return radians(a);
        default: return degrees(a);
    }
}

vec3 graphSafeDivide(vec3 a, vec3 b) {
    return vec3(graphSafeDivide(a.x, b.x), graphSafeDivide(a.y, b.y), graphSafeDivide(a.z, b.z));
}

// Generated coordinates of an object-space point: 0..1 across the object's bounds. A volume that ignores its
// object's scale passes that scale (1 otherwise). Generated then grows outward from the bounds' centre as the
// object scales, so a bigger object shows more of the same pattern (values below 0 and above 1) instead of
// stretching it, and scale 1 is exactly the plain bounds mapping.
vec3 graphGeneratedCoordinates(vec3 local, vec3 scale) {
    vec3 centre = uShaderGraphBoundsMin + 0.5 * uShaderGraphBoundsSize;
    return graphSafeDivide(local - uShaderGraphBoundsMin + (scale - 1.0) * (local - centre), uShaderGraphBoundsSize);
}

vec3 graphVectorMath(int op, vec3 a, vec3 b, vec3 c, float scale, out float value) {
    value = 0.0;
    switch (op) {
        case 0: return a + b;
        case 1: return a - b;
        case 2: return a * b;
        case 3: return graphSafeDivide(a, b);
        case 4: return a * b + c;
        case 5: return cross(a, b);
        case 6: { float l = dot(b, b); return l > 0.0 ? b * (dot(a, b) / l) : vec3(0.0); }
        case 7: return reflect(a, dot(b, b) > 0.0 ? normalize(b) : b);
        case 8: return refract(a, dot(b, b) > 0.0 ? normalize(b) : b, scale);
        case 9: return dot(c, b) < 0.0 ? a : -a;
        case 10: value = dot(a, b); return vec3(0.0);
        case 11: value = distance(a, b); return vec3(0.0);
        case 12: value = length(a); return vec3(0.0);
        case 13: return a * scale;
        case 14: return dot(a, a) > 0.0 ? normalize(a) : vec3(0.0);
        case 15: return abs(a);
        case 16: return min(a, b);
        case 17: return max(a, b);
        case 18: return floor(a);
        case 19: return ceil(a);
        case 20: return a - floor(a);
        case 21: return vec3(graphMath(24, a.x, b.x, 0.0), graphMath(24, a.y, b.y, 0.0), graphMath(24, a.z, b.z, 0.0));
        case 22: return vec3(graphMath(26, a.x, b.x, c.x), graphMath(26, a.y, b.y, c.y), graphMath(26, a.z, b.z, c.z));
        case 23: return vec3(graphMath(27, a.x, b.x, 0.0), graphMath(27, a.y, b.y, 0.0), graphMath(27, a.z, b.z, 0.0));
        case 24: return sin(a);
        case 25: return cos(a);
        default: return tan(a);
    }
}

mat3 graphEuler(vec3 degreesXyz) {
    vec3 r = radians(degreesXyz);
    vec3 c = cos(r), s = sin(r);
    mat3 x = mat3(1.0, 0.0, 0.0, 0.0, c.x, s.x, 0.0, -s.x, c.x);
    mat3 y = mat3(c.y, 0.0, -s.y, 0.0, 1.0, 0.0, s.y, 0.0, c.y);
    mat3 z = mat3(c.z, s.z, 0.0, -s.z, c.z, 0.0, 0.0, 0.0, 1.0);
    return z * y * x;
}

vec3 graphAxisAngle(vec3 v, vec3 axis, float angleDegrees) {
    if (dot(axis, axis) <= 0.0) return v;
    axis = normalize(axis);
    float a = radians(angleDegrees), c = cos(a), s = sin(a);
    return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
}

vec3 graphMapping(int type, vec3 v, vec3 location, vec3 rotation, vec3 scale) {
    mat3 r = graphEuler(rotation);
    if (type == 0) return r * (v * scale) + location;
    if (type == 1) return graphSafeDivide(transpose(r) * (v - location), scale);
    if (type == 2) return r * (v * scale);
    vec3 n = r * graphSafeDivide(v, scale);
    return dot(n, n) > 0.0 ? normalize(n) : n;
}

vec3 graphVectorRotate(int type, bool invert, vec3 v, vec3 center, vec3 axis, float angle, vec3 rotation) {
    vec3 p = v - center;
    if (type == 4) {
        mat3 r = graphEuler(rotation);
        return (invert ? transpose(r) : r) * p + center;
    }
    vec3 a = type == 1 ? vec3(1.0, 0.0, 0.0) : type == 2 ? vec3(0.0, 1.0, 0.0) : type == 3 ? vec3(0.0, 0.0, 1.0) : axis;
    return graphAxisAngle(p, a, invert ? -angle : angle) + center;
}

float graphMapRange(int type, bool clampResult, float value, float fromMin, float fromMax,
                    float toMin, float toMax, float steps) {
    float factor = graphSafeDivide(value - fromMin, fromMax - fromMin);
    if (type == 1) factor = steps > 0.0 ? floor(factor * (steps + 1.0)) / steps : 0.0;
    else if (type == 2) factor = fromMin > fromMax ? 1.0 - smoothstep(fromMax, fromMin, value) : smoothstep(fromMin, fromMax, value);
    else if (type == 3) {
        float x = clamp(fromMin > fromMax ? 1.0 - graphSafeDivide(value - fromMax, fromMin - fromMax) : factor, 0.0, 1.0);
        factor = x * x * x * (x * (x * 6.0 - 15.0) + 10.0);
    }
    float result = toMin + factor * (toMax - toMin);
    if (clampResult && type <= 1) result = clamp(result, min(toMin, toMax), max(toMin, toMax));
    return result;
}

float graphFresnelDielectric(float cosi, float eta) {
    float c = abs(cosi);
    float g = eta * eta - 1.0 + c * c;
    if (g <= 0.0) return 1.0;
    g = sqrt(g);
    float a = (g - c) / (g + c);
    float b = (c * (g + c) - 1.0) / (c * (g - c) + 1.0);
    return 0.5 * a * a * (1.0 + b * b);
}

// ── Evaluation ───────────────────────────────────────────────────────────────

#define IN(k) graphInput(at + (k))
#define INF(k) graphInput(at + (k)).x

// normal faces the viewer; incoming points from the surface towards the camera. Leaves the results in the
// registers for readShaderGraphSurface or readShaderGraphVolume.
void runShaderGraph(vec3 position, vec2 uv, vec3 world, vec3 normal, vec3 incoming,
                    bool backfacing, float time) {
    gGraphPosition = position;
    gGraphUv = uv;
    int pc = 1;
    for (int i = 0; i < uShaderGraphCount; ++i) {
        vec4 h = graphFetch(pc);
        int kind = int(h.x), mode = int(h.y), option = int(h.z), packed = int(h.w);
        int inputs = packed & 255, variant = packed >> 9;
        bool clampResult = (packed & 256) != 0;
        vec4 o0 = graphFetch(pc + 1), o1 = graphFetch(pc + 2), param = graphFetch(pc + 3);
        int at = pc + 4;
        pc = at + inputs;
        vec3 r0 = vec3(0.0), r1 = vec3(0.0), r2 = vec3(0.0), r3 = vec3(0.0);
        vec3 r4 = vec3(0.0), r5 = vec3(0.0), r6 = vec3(0.0);
        switch (kind) {
            case 1: r0 = vec3(param.x); break;
            case 2: r0 = param.xyz; break;
            case 3: r0 = position; break;
            case 4: r0 = vec3(uv, 0.0); break;
            case 5: {
                vec3 color;
                float fac = graphNoiseTexture(IN(0) * INF(1), INF(2), INF(3), INF(4), INF(5), o0.y >= 0.0, color);
                r0 = vec3(fac); r1 = color;
                break;
            }
            case 6:
                r0 = vec3(graphWave(IN(0) * INF(1), mode, option, variant, INF(2), INF(3), INF(4), INF(5), INF(6)));
                r1 = r0;
                break;
            case 7: {
                float scale = INF(1);
                vec3 color, cell;
                r0 = vec3(graphVoronoi(IN(0) * scale, INF(2), INF(3), mode, option, color, cell));
                r1 = color;
                r2 = scale != 0.0 ? cell / scale : vec3(0.0);
                break;
            }
            case 8: {
                vec3 p = IN(0) * INF(3);
                float fac = mod(floor(p.x) + floor(p.y) + floor(p.z), 2.0);
                r0 = mix(IN(2), IN(1), fac); r1 = vec3(fac);
                break;
            }
            case 9:
                r0 = graphRamp(INF(0), param, option + 2, mode, IN(1), IN(2), IN(3), IN(4));
                break;
            case 10:
                r0 = graphBlend(mode, clamp(INF(0), 0.0, 1.0), IN(1), IN(2));
                if (clampResult) r0 = clamp(r0, 0.0, 1.0);
                break;
            case 11: r0 = IN(0) + IN(1); break;
            case 12: r0 = IN(0) * IN(1); break;
            case 13: r0 = graphMapping(mode, IN(0), IN(1), IN(2), IN(3)); break;
            case 14: {
                r0 = graphGeneratedCoordinates(position / gGraphPositionScale, gGraphPositionScale);
                vec3 objectNormal = transpose(mat3(uModel)) * normal;
                r1 = dot(objectNormal, objectNormal) > 0.0 ? normalize(objectNormal) : normal;
                r2 = vec3(uv, 0.0);
                r3 = position;
                r4 = graphViewSpace(world);
                r5 = graphWindow(world);
                r6 = reflect(-incoming, normal);
                break;
            }
            case 15: r0 = world; r1 = normal; r2 = incoming; r3 = vec3(backfacing ? 1.0 : 0.0); break;
            case 16: {
                float eta = max(INF(0), 1e-5);
                r0 = vec3(graphFresnelDielectric(dot(incoming, normal), backfacing ? 1.0 / eta : eta));
                break;
            }
            case 17: {
                float blend = clamp(INF(0), 0.0, 1.0);
                float eta = max(1.0 - blend, 1e-5);
                float cosi = dot(incoming, normal);
                r0 = vec3(graphFresnelDielectric(cosi, backfacing ? eta : 1.0 / eta));
                float facing = abs(cosi);
                if (blend != 0.5) {
                    blend = clamp(blend, 0.0, 0.99999);
                    blend = blend < 0.5 ? 2.0 * blend : 0.5 / (1.0 - blend);
                    facing = pow(facing, blend);
                }
                r1 = vec3(1.0 - facing);
                break;
            }
            case 18: r0 = vec3(time); r1 = vec3(uShaderGraphFrame); break;
            case 19:
                r0 = uModel[3].xyz; r1 = uAlbedo; r2 = vec3(uAlpha);
                r3 = vec3(graphHash(uModel[3].xyz * 7.31 + 0.5).x);
                break;
            case 20: r0 = vec3(graphGradient(IN(0), mode)); r1 = r0; break;
            case 21: r0 = graphMagic(IN(0) * INF(1), mode, INF(2)); r1 = vec3((r0.x + r0.y + r0.z) / 3.0); break;
            case 22: {
                vec2 brick = graphBrick(IN(0) * INF(4), INF(5), INF(6), INF(7), INF(8), param.x, param.y);
                r0 = brick.y > 0.5 ? IN(3) : mix(IN(1), IN(2), brick.x);
                r1 = vec3(brick.y);
                break;
            }
            case 23: {
                vec3 p = IN(0) + INF(1) * vec3(0.618034, 1.324718, 0.754878);
                r0 = vec3(graphHash(p).x); r1 = graphHash(p.zxy + 17.17);
                break;
            }
            case 24: {
                vec4 texel = graphImage(IN(0).xy, mode);
                r0 = texel.rgb; r1 = vec3(texel.a);
                break;
            }
            case 25: { vec3 c = IN(1); r0 = mix(c, 1.0 - c, INF(0)); break; }
            case 26: {
                vec3 c = IN(4), hsv = graphRgbToHsv(c);
                hsv.x = fract(hsv.x + INF(0) - 0.5);
                hsv.y = clamp(hsv.y * INF(1), 0.0, 1.0);
                hsv.z *= INF(2);
                r0 = mix(c, graphHsvToRgb(hsv), INF(3));
                break;
            }
            case 27: {
                float contrast = INF(2);
                r0 = max((1.0 + contrast) * IN(0) + INF(1) - contrast * 0.5, 0.0);
                break;
            }
            case 28: r0 = pow(max(IN(0), 0.0), vec3(INF(1))); break;
            case 29: r0 = vec3(graphValue(IN(0))); break;
            case 30: {
                float v = graphMath(mode, INF(0), INF(1), INF(2));
                r0 = vec3(clampResult ? clamp(v, 0.0, 1.0) : v);
                break;
            }
            case 31: {
                float value;
                r0 = graphVectorMath(mode, IN(0), IN(1), IN(2), INF(3), value);
                r1 = vec3(value);
                break;
            }
            case 32: r0 = vec3(graphMapRange(mode, clampResult, INF(0), INF(1), INF(2), INF(3), INF(4), INF(5))); break;
            case 33: {
                float low = INF(1), high = INF(2);
                if (mode == 1 && low > high) { float swap = low; low = high; high = swap; }
                r0 = vec3(min(max(INF(0), low), high));
                break;
            }
            case 34: { vec3 v = IN(0); r0 = vec3(v.x); r1 = vec3(v.y); r2 = vec3(v.z); break; }
            case 35: r0 = vec3(INF(0), INF(1), INF(2)); break;
            case 36: {
                vec3 c = IN(0);
                if (mode == 1) c = graphRgbToHsv(c);
                else if (mode == 2) c = graphRgbToHsl(c);
                r0 = vec3(c.x); r1 = vec3(c.y); r2 = vec3(c.z);
                break;
            }
            case 37: {
                vec3 c = vec3(INF(0), INF(1), INF(2));
                r0 = mode == 1 ? graphHsvToRgb(c) : mode == 2 ? graphHslToRgb(c) : c;
                break;
            }
            case 38: r0 = graphVectorRotate(mode, clampResult, IN(0), IN(1), IN(2), INF(3), IN(4)); break;
            case 39: r0 = graphBlackbody(INF(0)); break;
            case 41: {
                vec3 query = graphPath(int(param.x), world, INF(0), INF(1), INF(2), INF(3));
                r0 = vec3(query.x); r1 = vec3(query.y); r2 = vec3(query.z);
                break;
            }
        }
        graphStore(o0.x, r0); graphStore(o0.y, r1); graphStore(o0.z, r2); graphStore(o0.w, r3);
        graphStore(o1.x, r4); graphStore(o1.y, r5); graphStore(o1.z, r6);
    }
}

#undef IN
#undef INF

NodeSurface readShaderGraphSurface() {
    int o = uShaderGraphOutputStart;
    vec3 emission = clamp(graphInput(o + 3), 0.0, 16.0) * max(graphInput(o + 9).x, 0.0);
    return NodeSurface(
        clamp(graphInput(o), 0.0, 1.0),
        clamp(graphInput(o + 1).x, 0.045, 1.0),
        clamp(graphInput(o + 2).x, 0.0, 1.0),
        clamp(emission, 0.0, 1024.0),
        graphInput(o + 4).x,
        clamp(graphInput(o + 5).x, 0.0, 1.0),
        max(graphInput(o + 6).x, 1.0),
        clamp(graphInput(o + 7).x, 0.0, 1.0),
        clamp(graphInput(o + 8).x, 0.0, 1.0),
        graphInput(o + 10).x,
        max(graphInput(o + 11).x, 0.0));
}

// A Fog Volume Output: a participating medium instead of a surface.
struct NodeVolume {
    vec3 color;
    vec3 extinctionColor;
    float density;
    float scattering;
    float absorption;
    float anisotropy;
    float softness;
    vec3 emission;
    float emissionStrength;
    float pathVisibility;
    float voxelSize;
    float voxelRandomness;
    float topCutoff;
};

// Voxel size, size randomness and top cutoff are plain settings: the sample position is snapped to the
// voxel grid before the graph runs, so they cannot come from the graph itself. w is 1 when the volume
// ignores its object's scale, which moves the voxel grid and the graph's position into world-sized units.
vec4 readShaderGraphVolumeSettings() { return graphFetch(uShaderGraphOutputStart + 10); }

NodeVolume readShaderGraphVolume() {
    int o = uShaderGraphOutputStart;
    NodeVolume v;
    v.color = clamp(graphInput(o), 0.0, 1.0);
    v.extinctionColor = clamp(graphInput(o + 1), 0.0, 1.0);
    v.density = clamp(graphInput(o + 2).x, 0.0, 1.0);
    v.scattering = clamp(graphInput(o + 3).x, 0.0, 16.0);
    v.absorption = clamp(graphInput(o + 4).x, 0.0, 16.0);
    v.anisotropy = clamp(graphInput(o + 5).x, -0.99, 0.99);
    v.softness = clamp(graphInput(o + 6).x, 0.0, 16.0);
    v.emission = clamp(graphInput(o + 7), 0.0, 16.0);
    v.emissionStrength = clamp(graphInput(o + 8).x, 0.0, 1024.0);
    v.pathVisibility = clamp(graphInput(o + 9).x, 0.0, 1.0);
    vec4 settings = readShaderGraphVolumeSettings();
    v.voxelSize = settings.x;
    v.voxelRandomness = settings.y;
    v.topCutoff = settings.z;
    return v;
}

#ifndef GRAPH_COMPUTE
// Fallback for draws that cannot march (simple.frag marches fog volumes when the viewport supplies the range and
// scene depth inputs): a fog volume draws as a translucent shell. Opacity assumes half a block of medium.
NodeSurface graphVolumePreview(NodeVolume v) {
    float optical = v.density * (v.scattering + v.absorption) * GRAPH_VOLUME_DENSITY * 0.5;
    float alpha = (1.0 - exp(-optical)) * v.pathVisibility;
    vec3 emission = clamp(v.emission * v.emissionStrength * v.density, 0.0, 1024.0);
    return NodeSurface(v.color, 1.0, 0.0, emission, 0.0, alpha, 1.0, 0.0, 0.0, 0.0, 0.0);
}

NodeSurface evaluateShaderGraph(vec3 position, vec2 uv, vec3 world, vec3 normal, vec3 incoming,
                                bool backfacing, float time) {
    runShaderGraph(position, uv, world, normal, incoming, backfacing, time);
    if ((uShaderGraphFlags & GRAPH_VOLUME) != 0)
        return graphVolumePreview(readShaderGraphVolume());
    return readShaderGraphSurface();
}

vec3 graphBump(vec3 normal, vec3 position, float height) {
    vec3 dx = dFdx(position), dy = dFdy(position);
    vec3 u = cross(dy, normal), v = cross(normal, dx);
    float determinant = dot(dx, u);
    vec3 gradient = (dFdx(height) * u + dFdy(height) * v) * sign(determinant);
    vec3 bumped = abs(determinant) * normal - 0.025 * gradient;
    return dot(bumped, bumped) > 1e-20 ? normalize(bumped) : normal;
}

#endif

// IOR and Specular (IOR level) set the dielectric reflectance, as in the Principled BSDF.
float graphDielectricF0(float ior, float specular) {
    float r = (ior - 1.0) / (ior + 1.0);
    return clamp(r * r * 2.0 * specular, 0.0, 1.0);
}
