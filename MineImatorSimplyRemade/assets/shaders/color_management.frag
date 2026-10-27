#version 330 core

uniform sampler2D uScene;
uniform vec2 uTexelSize;
uniform int uToneMapper;
uniform float uExposure; // Stops, not a linear multiplier.
uniform float uGamma = 2.2;
uniform int uLook;

out vec4 FragColor;

const vec3 REC709 = vec3(0.2126, 0.7152, 0.0722);
const float MAX_HDR = 1000000.0;

float finiteOr(float value, float fallback) {
    return (isnan(value) || isinf(value)) ? fallback : value;
}

float safeChannel(float value) {
    if (isnan(value)) return 0.0;
    if (isinf(value)) return value > 0.0 ? MAX_HDR : 0.0;
    return clamp(value, 0.0, MAX_HDR);
}

vec3 safeColor(vec3 color) {
    return vec3(safeChannel(color.r), safeChannel(color.g), safeChannel(color.b));
}

vec3 reinhard(vec3 x) {
    return x / (1.0 + x);
}

vec3 acesFitted(vec3 x) {
    // Narkowicz's per-channel ACES fit, not the full ACES RRT/ODT.
    return (x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14);
}

vec3 uchimura(vec3 x) {
    const float P = 1.0;
    const float a = 1.0;
    const float m = 0.22;
    const float l = 0.4;
    const float c = 1.33;
    const float l0 = (P - m) * l / a;
    const float S0 = m + l0;
    const float S1 = m + a * l0;
    const float C2 = a * P / (P - S1);
    vec3 w0 = 1.0 - smoothstep(vec3(0.0), vec3(m), x);
    vec3 w2 = step(vec3(S0), x);
    vec3 w1 = 1.0 - w0 - w2;
    vec3 toe = m * pow(x / m, vec3(c));
    vec3 linear = m + a * (x - m);
    // Clamp the unused shoulder exponent to avoid overflow in deep shadows.
    vec3 shoulder = P - (P - S1) * exp(min(-C2 * (x - S0) / P, vec3(0.0)));
    return toe * w0 + linear * w1 + shoulder * w2;
}

vec3 lottes(vec3 x) {
    const float a = 1.6;
    const float d = 0.977;
    const float hdrMax = 8.0;
    const float midIn = 0.18;
    const float midOut = 0.267;
    float hA = pow(hdrMax, a);
    float hAD = pow(hdrMax, a * d);
    float mA = pow(midIn, a);
    float mAD = pow(midIn, a * d);
    float b = (-mA + hA * midOut) / ((hAD - mAD) * midOut);
    float c = (hAD * mA - hA * mAD * midOut) / ((hAD - mAD) * midOut);
    return pow(x, vec3(a)) / (pow(x, vec3(a * d)) * b + c);
}

vec3 hableCurve(vec3 x) {
    const float A = 0.15;
    const float B = 0.50;
    const float C = 0.10;
    const float D = 0.20;
    const float E = 0.02;
    const float F = 0.30;
    return ((x * (A * x + C * B) + D * E) /
            (x * (A * x + B) + D * F)) - E / F;
}

vec3 hable(vec3 x) {
    return hableCurve(2.0 * x) / hableCurve(vec3(11.2));
}

// GT7 Curve only: adapted from GTToneMappingCurveV2, not the full perceptual
// GT7 operator (no Rec.2020/UCS chroma processing or perceptual blending).
// Source: https://blog.selfshadow.com/publications/s2025-shading-course/pdi/supplemental/gt7_tone_mapping.cpp
//
// MIT License
//
// Copyright (c) 2025 Polyphony Digital Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
float gt7CurveChannel(float x) {
    const float peak = 2.5; // 250-nit SDR paper white / 100-nit reference.
    const float alpha = 0.25;
    const float gray = 0.538;
    const float linearSection = 0.444;
    const float toeStrength = 1.280;
    const float k = (linearSection - 1.0) / (alpha - 1.0);
    const float kA = peak * linearSection + peak * k;
    float kB = -peak * k * exp(linearSection / k);
    const float kC = -1.0 / (k * peak);
    float mapped;
    if (x < linearSection * peak) {
        float toe = gray * pow(x / gray, toeStrength);
        mapped = mix(toe, x, smoothstep(0.0, gray, x));
    } else {
        mapped = kA + kB * exp(x * kC);
    }
    // Match the published SDR scale and peak clamp. kA itself exceeds peak.
    return clamp(mapped, 0.0, peak) * 0.4;
}

vec3 gt7Curve(vec3 x) {
    return vec3(gt7CurveChannel(x.r), gt7CurveChannel(x.g), gt7CurveChannel(x.b));
}

vec3 pbrNeutral(vec3 color) {
    // Khronos PBR Neutral: preserve hue until highlight desaturation.
    const float startCompression = 0.8 - 0.04;
    const float desaturation = 0.15;
    float x = min(color.r, min(color.g, color.b));
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    color -= offset;
    float peak = max(color.r, max(color.g, color.b));
    if (peak < startCompression) return color;
    float d = 1.0 - startCompression;
    float newPeak = 1.0 - d * d / (peak + d - startCompression);
    color *= newPeak / peak;
    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    return mix(color, vec3(newPeak), g);
}

vec3 agxPolynomial(vec3 x) {
    vec3 x2 = x * x;
    vec3 x4 = x2 * x2;
    return -17.86 * x4 * x2 + 78.01 * x4 * x - 126.7 * x4
         + 92.06 * x2 * x - 28.72 * x2 + 4.361 * x - 0.00232;
}

vec3 agx(vec3 x) {
    // Column-major matrices for linear Rec.709 input/output.
    const mat3 inset = mat3(
        0.842479062253094,  0.0423282422610123, 0.0423756549057051,
        0.0784335999999992, 0.878468636469772,  0.0784336,
        0.0792237451477643, 0.0791661274605434, 0.879142973793104);
    const mat3 outset = mat3(
         1.19687900512017,   -0.0528968517574562, -0.0529716355144438,
        -0.0980208811401368,  1.15190312990417,   -0.0980434501171241,
        -0.0990297440797205, -0.0989611768448433,  1.15107367264116);
    const float minEV = -12.47393;
    const float maxEV = 4.026069;
    x = inset * x;
    x = clamp(log2(max(x, vec3(0.0000000001))), vec3(minEV), vec3(maxEV));
    x = (x - minEV) / (maxEV - minEV);
    x = outset * agxPolynomial(x);
    // The polynomial/outset result is display-referred. Return linear light
    // so looks and the user's final gamma do not double-encode it.
    return pow(clamp(x, 0.0, 1.0), vec3(2.2));
}

vec3 filmic(vec3 x) {
    // Hejl-Burgess-Dawson includes an approximate gamma-2.2 display encoding.
    x = max(x - 0.004, 0.0);
    vec3 display = (x * (6.2 * x + 0.5)) / (x * (6.2 * x + 1.7) + 0.06);
    return pow(clamp(display, 0.0, 1.0), vec3(2.2));
}

vec3 toneMap(vec3 color) {
    // 0 Reinhard, 1 ACES, 2 Uchimura, 3 Lottes, 4 Hable,
    // 5 GT7 Curve, 6 Khronos PBR Neutral, 7 AgX, 8 Filmic.
    if (uToneMapper == 1) return acesFitted(color);
    if (uToneMapper == 2) return uchimura(color);
    if (uToneMapper == 3) return lottes(color);
    if (uToneMapper == 4) return hable(color);
    if (uToneMapper == 5) return gt7Curve(color);
    if (uToneMapper == 6) return pbrNeutral(color);
    if (uToneMapper == 7) return agx(color);
    if (uToneMapper == 8) return filmic(color);
    return reinhard(color);
}

vec3 contrastAtMidgray(vec3 color, float contrast) {
    // A linear-light odds S-curve: fixes black, white, and 18% midgray.
    // Unlike a power curve about gray, low contrast does not darken white.
    const float pivot = 0.18;
    vec3 numerator = pow(color, vec3(contrast));
    vec3 denominator = numerator + pow(1.0 - color, vec3(contrast))
                     * pow(pivot / (1.0 - pivot), contrast - 1.0);
    return numerator / max(denominator, vec3(0.000001));
}

vec3 applyLook(vec3 color) {
    // 0 None, 1 Punchy, 2 Grayscale, 3 Very High Contrast,
    // 4 High Contrast, 5 Medium High Contrast, 6 Medium Contrast,
    // 7 Medium Low Contrast, 8 Low Contrast, 9 Very Low Contrast.
    if (uLook == 2) return vec3(dot(color, REC709));
    float contrast = 1.0;
    if (uLook == 1) contrast = 1.20;
    else if (uLook == 3) contrast = 1.70;
    else if (uLook == 4) contrast = 1.45;
    else if (uLook == 5) contrast = 1.25;
    else if (uLook == 6) contrast = 1.10;
    else if (uLook == 7) contrast = 0.90;
    else if (uLook == 8) contrast = 0.75;
    else if (uLook == 9) contrast = 0.60;
    else return color;
    color = contrastAtMidgray(color, contrast);
    if (uLook == 1) {
        float luminance = dot(color, REC709);
        color = mix(vec3(luminance), color, 1.15);
    }
    return clamp(color, 0.0, 1.0);
}

void main() {
    vec2 uv = gl_FragCoord.xy * uTexelSize;
    if (any(isnan(uv)) || any(isinf(uv))) uv = vec2(0.5);
    vec4 scene = texture(uScene, clamp(uv, 0.0, 1.0));
    // Scene buffers are gamma-2.2 encoded but may contain values above one.
    // Input decoding is deliberately independent of the output gamma setting.
    vec3 linear = pow(safeColor(scene.rgb), vec3(2.2));
    float exposure = clamp(finiteOr(uExposure, 0.0), -32.0, 32.0);
    linear = safeColor(linear * exp2(exposure));
    vec3 mapped = clamp(safeColor(toneMap(linear)), 0.0, 1.0);
    mapped = applyLook(mapped);
    float gamma = finiteOr(uGamma, 2.2);
    gamma = gamma > 0.0 ? clamp(gamma, 0.1, 10.0) : 2.2;
    vec3 display = pow(clamp(safeColor(mapped), 0.0, 1.0), vec3(1.0 / gamma));
    FragColor = vec4(clamp(display, 0.0, 1.0), clamp(finiteOr(scene.a, 1.0), 0.0, 1.0));
}
