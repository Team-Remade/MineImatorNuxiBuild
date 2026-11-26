using GlmSharp;
using MineImatorSimplyRemade.core.mdl.material.materials;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// Veldrid counterparts of the camera-effect passes in <c>Viewport.*.cs</c>. Every builder mirrors the
/// OpenGL pass it replaces: the same settings, clamping, skip and validation rules, and the same shader
/// file, so the output matches the OpenGL renderer. Builders return effects in OpenGL application order
/// for one effect type; the caller concatenates the types in the order the viewport applies them.
/// </summary>
internal static class VeldridCameraEffects
{
    /// <summary>Samples <c>uScene</c> from the current image, lets the caller set uniforms, then writes the spare image.</summary>
    private sealed class PassEffect(string shader, Action<VeldridFullscreenPass, VeldridPostContext> configure) : IVeldridPostEffect
    {
        public void Apply(VeldridPostContext context)
        {
            var pass = context.Processor.GetPass(shader);
            pass.SetTexture("uScene", context.Current.SampleableTextureView, context.Processor.LinearClamp);
            configure(pass, context);
            context.DrawToOther(pass);
        }
    }

    /// <summary>Draws a shader that outputs only where it changes the picture (it discards elsewhere).</summary>
    private sealed class OverlayEffect(string shader, Action<VeldridFullscreenPass, VeldridPostContext> configure) : IVeldridPostEffect
    {
        public void Apply(VeldridPostContext context)
        {
            var pass = context.Processor.GetPass(shader);
            configure(pass, context);
            context.DrawOverCurrent(pass, VeldridFullscreenPass.BlendMode.Opaque);
        }
    }

    private static vec2 Resolution(VeldridPostContext context) => new(context.Width, context.Height);

    private static vec2 TexelSize(VeldridPostContext context) => new(1f / context.Width, 1f / context.Height);

    private static IEnumerable<CameraEffect> Enabled(CameraSceneObject? camera, CameraEffectType type) =>
        camera == null
            ? []
            : camera.Effects.Where(effect => effect.Enabled && effect.Type == type);

    private static void RequireFinite(string label, IEnumerable<string> keys, Func<string, float> value)
    {
        foreach (string key in keys)
            if (!float.IsFinite(value(key)))
                throw new InvalidDataException($"{label} {key} must be finite.");
    }

    /// <summary>Converts a premultiplied scene back to straight alpha when the background is transparent.</summary>
    public static IVeldridPostEffect AlphaResolve() =>
        new PassEffect("alpha_resolve.frag", (pass, context) => pass.Set("uTexelSize", TexelSize(context)));

    /// <summary>Tone mapping and look (see <c>ApplyColorManagementPass</c>). Null when colour management is off.</summary>
    public static IVeldridPostEffect? ColorManagement(bool enabled, int toneMapper, float exposure, float gamma, int look)
    {
        if (!enabled) return null;
        return new PassEffect("color_management.frag", (pass, context) =>
        {
            pass.Set("uTexelSize", TexelSize(context));
            pass.Set("uToneMapper", Math.Clamp(toneMapper, 0, 8));
            pass.Set("uExposure", exposure);
            pass.Set("uGamma", gamma);
            pass.Set("uLook", Math.Clamp(look, 0, 9));
        });
    }

    /// <summary>Colour Correction effects followed by the camera material (see <c>ApplyColorCorrectionPass</c>).</summary>
    public static List<IVeldridPostEffect> ColorCorrection(CameraSceneObject? camera)
    {
        var effects = new List<IVeldridPostEffect>();
        foreach (var effect in Enabled(camera, CameraEffectType.ColorCorrection))
            effects.Add(ColorAdjustment(effect.ColorCorrection, vec3.Ones, vec4.Zero, null));
        effects.AddRange(CameraMaterial(camera));
        return effects;
    }

    /// <summary>The camera's own blend colour, mix colour and colour operations (see <c>ApplyCameraMaterialPass</c>).</summary>
    public static List<IVeldridPostEffect> CameraMaterial(CameraSceneObject? camera)
    {
        var effects = new List<IVeldridPostEffect>();
        if (camera == null ||
            (camera.BlendColor == vec3.Ones && camera.MixColor.w == 0f && camera.ColorAdjustments.IsNeutral))
            return effects;
        effects.Add(ColorAdjustment(null, camera.BlendColor, camera.MixColor, camera.ColorAdjustments));
        return effects;
    }

    private static IVeldridPostEffect ColorAdjustment(ColorCorrectionSettings? settings, vec3 blendColor, vec4 mixColor,
        MaterialColorAdjustments? colorAdjustments)
    {
        var adjustments = colorAdjustments ?? new MaterialColorAdjustments();
        return new PassEffect("color_correction.frag", (pass, context) =>
        {
            pass.Set("uTexelSize", TexelSize(context));
            pass.Set("uContrast", settings?.Contrast ?? 100f);
            pass.Set("uBrightness", settings?.Brightness ?? 100f);
            pass.Set("uSaturation", settings?.Saturation ?? 100f);
            pass.Set("uVibrance", settings?.Vibrance ?? 0f);
            pass.Set("uHue", settings?.Hue ?? 0f);
            pass.Set("uInvertStrength", settings is { Invert: true } ? settings.InvertStrength : 0f);
            pass.Set("uColorBurn", settings?.ColorBurn ?? 0f);
            pass.Set("uBurnColor", new vec3(settings?.BurnColorR ?? 1f, settings?.BurnColorG ?? 1f, settings?.BurnColorB ?? 1f));
            pass.Set("uBlendColor", blendColor);
            pass.Set("uMixColor", mixColor);
            pass.Set("uMultiplyHsv", adjustments.MultiplyHsv);
            pass.Set("uAddRgb", adjustments.AddRgb);
            pass.Set("uAddHsv", adjustments.AddHsv);
            pass.Set("uSubtractRgb", adjustments.SubtractRgb);
            pass.Set("uSubtractHsv", adjustments.SubtractHsv);
        });
    }

    /// <summary>Vignette: image-relative ellipse tinted toward a colour (see <c>ApplyVignettePass</c>).</summary>
    public static List<IVeldridPostEffect> Vignette(CameraSceneObject? camera)
    {
        var effects = new List<IVeldridPostEffect>();
        foreach (var effect in Enabled(camera, CameraEffectType.Vignette))
        {
            var settings = effect.Vignette;
            if (settings.Strength <= 0f || settings.Radius >= 100f) continue;
            effects.Add(new PassEffect("vignette.frag", (pass, context) =>
            {
                pass.Set("uResolution", Resolution(context));
                pass.Set("uRadius", settings.Radius);
                pass.Set("uSoftness", settings.Softness);
                pass.Set("uStrength", settings.Strength);
                pass.Set("uColor", new vec3(settings.ColorR, settings.ColorG, settings.ColorB));
            }));
        }
        return effects;
    }

    /// <summary>Per-channel offsets and blur (see <c>ApplyChromaticAberrationPass</c>).</summary>
    public static List<IVeldridPostEffect> ChromaticAberration(CameraSceneObject? camera)
    {
        var effects = new List<IVeldridPostEffect>();
        foreach (var effect in Enabled(camera, CameraEffectType.ChromaticAberration))
        {
            var settings = effect.ChromaticAberration;
            RequireFinite("Chromatic aberration", ChromaticAberrationSettings.Controls.Select(control => control.Key), settings.GetValue);
            if (settings.BlurAmount == 0f && settings.RedOffset == 0f && settings.GreenOffset == 0f && settings.BlueOffset == 0f)
                continue;
            effects.Add(new PassEffect("chromatic_aberration.frag", (pass, context) =>
            {
                pass.Set("uResolution", Resolution(context));
                pass.Set("uBlurAmount", settings.BlurAmount);
                pass.Set("uDistortRgb", settings.DistortRgb);
                pass.Set("uOffsets", new vec3(settings.RedOffset, settings.GreenOffset, settings.BlueOffset));
            }));
        }
        return effects;
    }

    /// <summary>Lens zoom and barrel distortion (see <c>ApplyDistortPass</c>).</summary>
    public static List<IVeldridPostEffect> Distort(CameraSceneObject? camera)
    {
        var effects = new List<IVeldridPostEffect>();
        foreach (var effect in Enabled(camera, CameraEffectType.Distort))
        {
            var settings = effect.Distort;
            RequireFinite("Distort", DistortSettings.Controls.Select(control => control.Key), settings.GetValue);
            if (MathF.Abs(settings.ZoomAmount) == 100f && settings.DistortAmount == 0f) continue;
            effects.Add(new PassEffect("distort.frag", (pass, context) =>
            {
                pass.Set("uResolution", Resolution(context));
                pass.Set("uRepeatImage", settings.RepeatImage);
                pass.Set("uZoomAmount", settings.ZoomAmount);
                pass.Set("uDistortAmount", settings.DistortAmount);
            }));
        }
        return effects;
    }

    /// <summary>Animated heat shimmer (see <c>ApplyHeatDistortPass</c>); <paramref name="timeSeconds"/> is the timeline effect time.</summary>
    public static List<IVeldridPostEffect> HeatDistort(CameraSceneObject? camera, float timeSeconds)
    {
        var effects = new List<IVeldridPostEffect>();
        foreach (var effect in Enabled(camera, CameraEffectType.HeatDistort))
        {
            var settings = effect.HeatDistort;
            RequireFinite("Heat distort", HeatDistortSettings.Controls.Select(control => control.Key), settings.GetValue);
            if (settings.Strength == 0f) continue;
            effects.Add(new PassEffect("heat_distort.frag", (pass, context) =>
            {
                pass.Set("uResolution", Resolution(context));
                pass.Set("uTime", timeSeconds);
                pass.Set("uStrength", settings.Strength / 100f);
                pass.Set("uSpeed", settings.Speed / 100f);
                pass.Set("uScale", settings.Scale / 100f);
            }));
        }
        return effects;
    }

    /// <summary>FXAA with the project's quality settings (see <c>ApplyAntiAliasingPass</c>). Null when disabled.</summary>
    public static IVeldridPostEffect? AntiAliasing(bool enabled, float subpixel, float edgeThreshold, float minimumThreshold)
    {
        if (!enabled) return null;
        return new PassEffect("fxaa.frag", (pass, context) =>
        {
            pass.Set("uTexelSize", TexelSize(context));
            pass.Set("uSubpixel", subpixel);
            pass.Set("uEdgeThreshold", edgeThreshold);
            pass.Set("uMinimumThreshold", minimumThreshold);
        });
    }

    /// <summary>All Film Grain effects merged into one pass (see <c>ApplyFilmGrainPass</c>); null when none is enabled.</summary>
    public static IVeldridPostEffect? FilmGrain(CameraSceneObject? camera, int frame)
    {
        var grains = Enabled(camera, CameraEffectType.FilmGrain).ToList();
        if (grains.Count == 0) return null;
        foreach (var effect in grains)
            RequireFinite("Film grain", ["strength", "saturation", "size"], effect.FilmGrain.GetValue);
        float strength = grains.Sum(effect => effect.FilmGrain.Strength);
        float weight = MathF.Max(0.0001f, grains.Sum(effect => MathF.Abs(effect.FilmGrain.Strength)));
        float saturation = grains.Sum(effect => effect.FilmGrain.Saturation * MathF.Abs(effect.FilmGrain.Strength)) / weight;
        float size = grains.Sum(effect => effect.FilmGrain.Size * MathF.Abs(effect.FilmGrain.Strength)) / weight;
        return new PassEffect("film_grain.frag", (pass, context) =>
        {
            pass.Set("uResolution", Resolution(context));
            pass.Set("uFrame", (float)frame);
            pass.Set("uStrength", strength);
            pass.Set("uSaturation", saturation);
            pass.Set("uSize", size);
        });
    }

    /// <summary>Pixelate: nearest-sample blocks anchored at the bottom-left corner (see <c>ApplyPixelatePass</c>).</summary>
    public static List<IVeldridPostEffect> Pixelate(CameraSceneObject? camera)
    {
        var effects = new List<IVeldridPostEffect>();
        foreach (var effect in Enabled(camera, CameraEffectType.Pixelate))
        {
            float size = effect.Pixelate.Size;
            if (!float.IsFinite(size)) throw new InvalidDataException("Pixelate size must be finite.");
            size = MathF.Floor(Math.Clamp(size, 1f, 256f) + 0.5f);
            if (size <= 1f) continue;
            effects.Add(new PassEffect("pixelate.frag", (pass, _) => pass.Set("uSize", size)));
        }
        return effects;
    }

    /// <summary>Letterbox bars drawn over the finished image (see <c>ApplyCinematicBarsPass</c>).</summary>
    public static List<IVeldridPostEffect> CinematicBars(CameraSceneObject? camera)
    {
        var effects = new List<IVeldridPostEffect>();
        foreach (var effect in Enabled(camera, CameraEffectType.CinematicBars))
        {
            var settings = effect.CinematicBars;
            RequireFinite("Cinematic bars", CinematicBarsSettings.Controls.Select(control => control.Key), settings.GetValue);
            if (settings.Size <= 0f) continue;
            effects.Add(new OverlayEffect("cinematic_bars.frag", (pass, context) =>
            {
                pass.Set("uResolution", Resolution(context));
                pass.Set("uSize", Math.Clamp(settings.Size, 0f, 50f) / 100f);
                pass.Set("uRotation", Math.Clamp(settings.Rotation, -180f, 180f) * MathF.PI / 180f);
                pass.Set("uOffset", Math.Clamp(settings.Offset, -100f, 100f) / 100f);
                pass.Set("uColor", new vec3(Math.Clamp(settings.ColorR, 0f, 1f), Math.Clamp(settings.ColorG, 0f, 1f),
                    Math.Clamp(settings.ColorB, 0f, 1f)));
            }));
        }
        return effects;
    }
}
