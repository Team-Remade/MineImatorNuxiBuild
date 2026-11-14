vec3 materialRgbToHsv(vec3 color) {
    float hi = max(color.r, max(color.g, color.b));
    float lo = min(color.r, min(color.g, color.b));
    float chroma = hi - lo;
    float hue = 0.0;
    if (chroma > 0.0) {
        if (hi == color.r) hue = (color.g - color.b) / chroma;
        else if (hi == color.g) hue = (color.b - color.r) / chroma + 2.0;
        else hue = (color.r - color.g) / chroma + 4.0;
        hue = fract(hue / 6.0);
    }
    return vec3(hue, hi > 0.0 ? chroma / hi : 0.0, hi);
}

vec3 materialHsvToRgb(vec3 hsv) {
    float hue = fract(hsv.x) * 6.0;
    float saturation = clamp(hsv.y, 0.0, 1.0);
    float value = clamp(hsv.z, 0.0, 1.0);
    float chroma = value * saturation;
    float secondary = chroma * (1.0 - abs(mod(hue, 2.0) - 1.0));
    vec3 rgb;
    if (hue < 1.0) rgb = vec3(chroma, secondary, 0.0);
    else if (hue < 2.0) rgb = vec3(secondary, chroma, 0.0);
    else if (hue < 3.0) rgb = vec3(0.0, chroma, secondary);
    else if (hue < 4.0) rgb = vec3(0.0, secondary, chroma);
    else if (hue < 5.0) rgb = vec3(secondary, 0.0, chroma);
    else rgb = vec3(chroma, 0.0, secondary);
    return rgb + value - chroma;
}

bool hasMaterialColors(vec3 multiplyHsv, vec3 addRgb, vec3 addHsv, vec3 subtractRgb, vec3 subtractHsv) {
    return any(notEqual(multiplyHsv, vec3(1.0))) || any(notEqual(addRgb, vec3(0.0))) ||
        any(notEqual(addHsv, vec3(0.0))) || any(notEqual(subtractRgb, vec3(0.0))) ||
        any(notEqual(subtractHsv, vec3(0.0)));
}

vec3 applyMaterialColors(vec3 color, vec3 multiplyHsv, vec3 addRgb, vec3 addHsv, vec3 subtractRgb, vec3 subtractHsv) {
    if (any(notEqual(multiplyHsv, vec3(1.0))))
        color = materialHsvToRgb(materialRgbToHsv(max(color, vec3(0.0))) * multiplyHsv);
    if (any(notEqual(addRgb, vec3(0.0))))
        color = clamp(color + addRgb, 0.0, 1.0);
    if (any(notEqual(addHsv, vec3(0.0))))
        color = materialHsvToRgb(materialRgbToHsv(max(color, vec3(0.0))) + addHsv);
    if (any(notEqual(subtractRgb, vec3(0.0))))
        color = clamp(color - subtractRgb, 0.0, 1.0);
    if (any(notEqual(subtractHsv, vec3(0.0))))
        color = materialHsvToRgb(materialRgbToHsv(max(color, vec3(0.0))) - subtractHsv);
    return color;
}
