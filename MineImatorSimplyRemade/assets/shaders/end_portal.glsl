// Original layered star field; no Minecraft shader code or textures are used.
vec3 portalHash(vec2 cell, float layer)
{
    vec3 p = fract(vec3(cell.x, cell.y, layer) * vec3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return fract((p.xxy + p.yxx) * p.zyx);
}

vec3 endPortalColor(vec3 position, vec3 camera, vec3 normal, float seconds)
{
    normal = normalize(normal);
    vec3 axis = abs(normal.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0);
    vec3 tangent = normalize(cross(axis, normal));
    vec3 bitangent = cross(normal, tangent);
    vec3 ray = position - camera;
    ray /= max(length(ray), 0.00001);
    vec2 projected = vec2(dot(ray, tangent), dot(ray, bitangent))
                   / max(abs(dot(ray, normal)), 0.25);
    vec3 color = vec3(0.005, 0.012, 0.018);
    for (int i = 0; i < 16; i++) {
        float layer = float(i);
        float angle = layer * 2.399963 + seconds * (0.006 + layer * 0.0007);
        mat2 rotation = mat2(cos(angle), -sin(angle), sin(angle), cos(angle));
        vec2 uv = rotation * projected * (5.0 + layer * 1.7);
        uv += vec2(layer * 13.7, layer * 9.3 + seconds * (0.035 + layer * 0.003));
        vec3 random = portalHash(floor(uv), layer);
        vec2 center = 0.2 + random.xy * 0.6;
        float distanceToStar = length(fract(uv) - center);
        float radius = mix(0.035, 0.13, random.z);
        float aa = max(length(fwidth(uv)) * 0.5, 0.002);
        float star = 1.0 - smoothstep(radius - aa, radius + aa, distanceToStar);
        star *= step(0.68, random.z);
        vec3 tint = mix(vec3(0.08, 0.32, 0.42), vec3(0.45, 0.78, 0.48), random.x);
        color += tint * star * (0.7 / (1.0 + layer * 0.10));
    }
    return color;
}
