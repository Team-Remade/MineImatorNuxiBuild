#version 330 core
in vec2 vNdc;
out vec4 FragColor;

uniform vec3 uCameraForward, uCameraRight, uCameraUp;
uniform float uTanHalfFov, uAspect;
uniform vec3 uHorizonDay, uZenithDay, uHorizonSunset, uZenithSunset, uHorizonNight, uZenithNight;
uniform sampler2D uSunTex, uMoonTex;
uniform vec3 uSunDirection, uMoonDirection;
uniform float uSunSize, uMoonSize;
uniform int uMoonPhase;

vec4 celestial(sampler2D tex, vec3 ray, vec3 direction, float angularSize, bool atlas)
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
        int phase = clamp(uMoonPhase, 0, 7);
        uv = (uv + vec2(float(phase % 4), float(phase / 4))) / vec2(4.0, 2.0);
    }
    return texture(tex, uv);
}

void main()
{
    vec3 ray = normalize(uCameraForward + vNdc.x * uAspect * uTanHalfFov * uCameraRight + vNdc.y * uTanHalfFov * uCameraUp);
    float elevation = uSunDirection.y;
    float night = 1.0 - smoothstep(-0.18, 0.02, elevation);
    float sunset = (1.0 - night) * (1.0 - smoothstep(0.05, 0.35, elevation));
    float vertical = smoothstep(-0.08, 0.72, max(ray.y, -0.08));
    vec3 dayColor = mix(uHorizonDay, uZenithDay, vertical);
    vec3 sunsetColor = mix(uHorizonSunset, uZenithSunset, vertical);
    vec3 nightColor = mix(uHorizonNight, uZenithNight, vertical);
    vec3 color = mix(mix(dayColor, sunsetColor, sunset), nightColor, night);

    vec4 sun = celestial(uSunTex, ray, uSunDirection, uSunSize, false);
    vec4 moon = celestial(uMoonTex, ray, uMoonDirection, uMoonSize, true);
    color = mix(color, sun.rgb, sun.a * (1.0 - night));
    color = mix(color, moon.rgb, moon.a * night);
    FragColor = vec4(color, 1.0);
}
