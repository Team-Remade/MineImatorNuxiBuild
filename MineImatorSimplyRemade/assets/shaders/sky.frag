#version 330 core
in vec2 vNdc;
out vec4 FragColor;

uniform vec3 uCameraForward, uCameraRight, uCameraUp;
uniform vec3 uCameraPosition;
uniform float uTanHalfFov, uAspect;
uniform float uCameraNear, uCameraFar;
uniform vec3 uHorizonDay, uZenithDay, uHorizonSunset, uZenithSunset, uHorizonNight, uZenithNight;
uniform sampler2D uSunTex, uMoonTex, uCloudTex;
uniform vec3 uCloudColor, uNightCloudColor;
uniform int uCloudMode;
uniform vec2 uCloudOffset;
uniform float uCloudHeight, uCloudBlockSize, uCloudThickness;
uniform vec3 uSunDirection, uMoonDirection;
uniform float uSunSize, uMoonSize;
uniform int uMoonPhase;
uniform bool uMoonAtlasEnabled;
uniform bool uShowStars, uTwilight;
uniform float uStarBrightness, uStarDensity, uStarTwinkleSpeed;
uniform vec3 uStarColor;
uniform float uTime;
uniform bool uFogEnabled;
uniform vec3 uFogColor;
uniform bool uCloudFogEnabled;
uniform vec3 uObjectFogColor;
uniform float uFogDistance, uFogFadeSize;

vec4 celestial(sampler2D tex, vec3 ray, vec3 direction, float angularSize, bool atlas, int phase)
{
    vec3 upRef = abs(direction.y) > 0.98 ? vec3(0, 0, 1) : vec3(0, 1, 0);
    vec3 right = normalize(cross(direction, upRef));
    vec3 up = normalize(cross(right, direction));
    float radius = tan(radians(angularSize) * 0.5);
    float facing = dot(ray, direction);
    if (facing <= 0.0) return vec4(0.0);
    vec2 uv = vec2(dot(ray, right), dot(ray, up)) / (facing * radius) * 0.5 + 0.5;
    if (any(lessThan(uv, vec2(0))) || any(greaterThan(uv, vec2(1)))) return vec4(0.0);
    if (atlas) {
        int moonPhase = clamp(phase, 0, 7);
        uv = (uv + vec2(float(moonPhase % 4), float(moonPhase / 4))) / vec2(4.0, 2.0);
    }
    return texture(tex, uv);
}

vec4 raycastClouds(vec3 ray, float night, out float hitDistance)
{
    hitDistance = -1.0;
    float bottom = uCloudHeight;
    float top = bottom + max(uCloudThickness, 1.0) * 4.0;
    float cellSize = max(uCloudBlockSize, 1.0);
    float textureSpan = cellSize * 256.0;
    vec3 cameraPixels = uCameraPosition * 64.0;
    if (abs(ray.y) < 0.0001) return vec4(0.0);

    vec3 cloudColorBlend = mix(uCloudColor, uNightCloudColor, night);

    if (uCloudMode == 2)
    {
        float planeDistance = (bottom - cameraPixels.y) / ray.y;
        if (planeDistance <= 0.0) return vec4(0.0);
        vec3 world = cameraPixels + ray * planeDistance;
        vec4 texel = texture(uCloudTex, (world.xz + uCloudOffset) / textureSpan);
        float fade = 1.0 - smoothstep(11520.0, 26880.0, planeDistance);
        hitDistance = planeDistance;
        return vec4(cloudColorBlend * texel.rgb * mix(0.28, 1.0, 1.0 - night), texel.a * fade);
    }

    float a = (bottom - cameraPixels.y) / ray.y;
    float b = (top - cameraPixels.y) / ray.y;
    float enter = max(min(a, b), 0.0);
    float leave = max(a, b);
    if (leave <= enter) return vec4(0.0);

    float stepSize = max((leave - enter) / 72.0, 2.0);
    float previousMask = 0.0;
    for (int i = 0; i < 72; i++)
    {
        float distanceAlongRay = enter + float(i) * stepSize;
        if (distanceAlongRay > leave) break;
        vec3 world = cameraPixels + ray * distanceAlongRay;
        vec2 uv = (world.xz + uCloudOffset) / textureSpan;
        vec4 texel = texture(uCloudTex, uv);
        float mask = step(0.1, texel.a);
        if (mask > 0.5 && previousMask < 0.5)
        {
            float left = texture(uCloudTex, uv - vec2(cellSize / textureSpan, 0)).a;
            float right = texture(uCloudTex, uv + vec2(cellSize / textureSpan, 0)).a;
            float back = texture(uCloudTex, uv - vec2(0, cellSize / textureSpan)).a;
            float front = texture(uCloudTex, uv + vec2(0, cellSize / textureSpan)).a;
            vec3 normal;
            if (abs(world.y - bottom) < stepSize * abs(ray.y) + 0.08) normal = vec3(0, -1, 0);
            else if (abs(world.y - top) < stepSize * abs(ray.y) + 0.08) normal = vec3(0, 1, 0);
            else normal = normalize(vec3(left - right, 0.0, back - front) + vec3(0.0001));

            float faceLight = normal.y > 0.5 ? 1.0 : (normal.y < -0.5 ? 0.62 : 0.78);
            float distanceFade = 1.0 - smoothstep(11520.0, 26880.0, distanceAlongRay);
            vec3 cloud = cloudColorBlend * texel.rgb * faceLight * mix(0.28, 1.0, 1.0 - night);
            float storyFade = uCloudMode == 1
                ? clamp((top - world.y) / max(top - bottom, 1.0), 0.0, 1.0)
                : 1.0;
            if (storyFade > 0.015)
            {
                hitDistance = distanceAlongRay;
                return vec4(cloud, texel.a * distanceFade * storyFade);
            }
            previousMask = 0.0;
            continue;
        }
        previousMask = mask;
    }
    return vec4(0.0);
}

vec2 hash2(vec2 p)
{
    p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
    return fract(sin(p) * 43758.5453123);
}

float starField(vec3 ray, float night)
{
    if (!uShowStars || night <= 0.0) return 0.0;
    vec3 dir = normalize(ray);
    if (dir.y < 0.0) return 0.0;

    float u = atan(dir.z, dir.x) / (2.0 * 3.14159265) + 0.5;
    float v = acos(clamp(dir.y, -1.0, 1.0)) / 3.14159265;

    float density = max(uStarDensity, 0.01);
    vec2 uv = vec2(u, v) * density * 200.0;
    vec2 gv = fract(uv) - 0.5;
    vec2 id = floor(uv);

    float star = 0.0;
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            vec2 offset = vec2(x, y);
            vec2 cell = id + offset;
            vec2 r = hash2(cell);
            vec2 starOffset = r - 0.5;
            vec2 delta = starOffset - gv + offset;
            float dist = length(delta);
            float size = 0.04 * mix(0.3, 1.5, r.x);
            if (dist < size)
            {
                float brightness = 1.0 - dist / size;
                float twinkle = 0.6 + 0.4 * sin(r.y * 6.2831853 + uTime * uStarTwinkleSpeed * 0.3);
                brightness *= twinkle;
                star += brightness;
            }
        }
    }
    return star * uStarBrightness * night * 0.6;
}

void main()
{
    vec3 ray = normalize(uCameraForward + vNdc.x * uAspect * uTanHalfFov * uCameraRight + vNdc.y * uTanHalfFov * uCameraUp);
    float elevation = uSunDirection.y;
    float night = 1.0 - smoothstep(-0.18, 0.02, elevation);
    float sunset = uTwilight ? (1.0 - night) * (1.0 - smoothstep(0.05, 0.35, elevation)) : 0.0;
    float vertical = smoothstep(-0.08, 0.72, max(ray.y, -0.08));
    vec3 dayColor = mix(uHorizonDay, uZenithDay, vertical);
    vec3 sunsetColor = mix(uHorizonSunset, uZenithSunset, vertical);
    vec3 nightColor = mix(uHorizonNight, uZenithNight, vertical);
    vec3 color = mix(mix(dayColor, sunsetColor, sunset), nightColor, night);

    vec4 sun = celestial(uSunTex, ray, uSunDirection, uSunSize, false, 0);
    vec4 moon = celestial(uMoonTex, ray, uMoonDirection, uMoonSize, uMoonAtlasEnabled, uMoonPhase);
    color = mix(color, sun.rgb, sun.a * (1.0 - night));
    color = mix(color, moon.rgb, moon.a * night);

    float starIntensity = starField(ray, night);
    color += uStarColor * starIntensity;

    float cloudDistance;
    vec4 clouds = raycastClouds(ray, night, cloudDistance);
    if (uCloudFogEnabled && cloudDistance > 0.0) {
        float cloudFog = smoothstep(max(uFogDistance - uFogFadeSize, 0.0), max(uFogDistance, 1.0), cloudDistance);
        clouds.rgb = mix(clouds.rgb, uObjectFogColor, cloudFog);
    }
    color = mix(color, clouds.rgb, clouds.a * 0.78);
    if (uFogEnabled) {
        float skyFog = 1.0 - smoothstep(0.0, 0.65, abs(ray.y));
        color = mix(color, uFogColor, skyFog);
    }
    gl_FragDepth = 1.0;
    if (cloudDistance > 0.0 && clouds.a > 0.001)
    {
        float viewDistance = max((cloudDistance / 64.0) * dot(ray, uCameraForward), uCameraNear);
        gl_FragDepth = uCameraFar / (uCameraFar - uCameraNear)
                     - (uCameraFar * uCameraNear) / ((uCameraFar - uCameraNear) * viewDistance);
    }
    FragColor = vec4(color, 1.0);
}
