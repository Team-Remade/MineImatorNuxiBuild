#ifndef WIND_CUSTOM_UNIFORMS
uniform bool uWindEnabled;
uniform int uWindKind;
uniform vec4 uWindParameters;
uniform vec2 uWindDirection;
uniform mat4 uWindModel;
uniform mat4 uWindInverseModel;
#endif

float windWeight(vec2 uv) {
    if (uWindKind == 3) return 1.0 - clamp(uv.y, 0.0, 1.0);
    if (uWindKind == 4) return 2.0 - clamp(uv.y, 0.0, 1.0);
    return 1.0;
}

vec3 windOffset(vec3 p, float weight) {
    float phase = dot(p.xz, vec2(0.73, 0.51));
    float flutter = sin(phase + uWindParameters.x)
        + 0.35 * sin(phase * 2.31 - uWindParameters.x * 1.73);
    float gust = sin(dot(p.xz, uWindDirection) * 0.42 - uWindParameters.z);
    if (uWindKind == 2) {
        float ripple = sin(phase * 1.7 + uWindParameters.x);
        return vec3(0.0, 0.04 * (ripple * uWindParameters.y + gust * uWindParameters.w), 0.0);
    }
    vec2 side = vec2(-uWindDirection.y, uWindDirection.x);
    vec2 sway = side * flutter * uWindParameters.y + uWindDirection * gust * uWindParameters.w;
    return vec3(sway.x, 0.12 * flutter * uWindParameters.y, sway.y) * (0.08 * weight);
}

vec4 windPosition(vec4 position, vec2 uv) {
    if (!uWindEnabled) return position;
    vec4 world = uWindModel * position;
    world.xyz += windOffset(world.xyz, windWeight(uv));
    return uWindInverseModel * world;
}

vec3 windNormal(vec4 position, vec3 normal, vec2 uv) {
    if (!uWindEnabled) return normal;
    vec3 n = normalize(normal);
    vec3 tangent = normalize(cross(n, abs(n.y) < 0.9 ? vec3(0, 1, 0) : vec3(1, 0, 0)));
    vec3 bitangent = cross(n, tangent);
    vec3 center = windPosition(position, uv).xyz;
    vec3 t = windPosition(position + vec4(tangent * 0.01, 0), uv).xyz - center;
    vec3 b = windPosition(position + vec4(bitangent * 0.01, 0), uv).xyz - center;
    return normalize(cross(t, b));
}
