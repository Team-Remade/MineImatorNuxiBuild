using GlmSharp;
using MineImatorSimplyRemade.core.mdl.material.materials;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// Veldrid counterparts of the camera-effect passes in <c>Viewport.*.cs</c>. Every builder mirrors the
/// OpenGL pass it replaces: the same settings, clamping, skip and validation rules, and the same shader
/// file, so the output matches the OpenGL renderer. Builders return effects in OpenGL application order
/// for one effect type; the caller concatenates the types in the order the viewport applies them.
/// </summary>
internal static class VeldridCameraEffects
{
    private const PixelFormat HdrScratchFormat = PixelFormat.R16_G16_B16_A16_Float;
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

    private sealed class DepthOfFieldEffect(DepthOfFieldSettings settings, CameraSceneObject camera, float near, float far) : IVeldridPostEffect
    {
        public void Apply(VeldridPostContext context)
        {
            if (context.Scene.DepthTextureView == null) return;
            var pass = context.Processor.GetPass("depth_of_field.frag");
            pass.SetTexture("uScene", context.Current.SampleableTextureView, context.Processor.LinearClamp);
            pass.SetTexture("uDepth", context.Scene.DepthTextureView, context.Processor.PointClamp);
            pass.Set("uResolution", Resolution(context));
            pass.Set("uClip", new vec2(near, far));
            pass.Set("uBlades", new vec3(Math.Clamp(camera.BladeCount, 0, 16), camera.BladeAngle % 360f,
                Math.Clamp(camera.BladeStretch, -100f, 100f)));
            pass.Set("uFocus", new vec4(MathF.Max(0f, settings.Depth), MathF.Max(0f, settings.FocusRange),
                MathF.Max(0f, settings.FadeSize), Math.Clamp(settings.BlurSize, 0f, 100f)));
            pass.Set("uBokeh", new vec4(Math.Clamp(settings.AnamorphicRatio, 0.1f, 10f),
                Math.Clamp(settings.EdgeHighlight, 0f, 1f), Math.Clamp(settings.HighlightThreshold, 0f, 1f),
                Math.Clamp(settings.HighlightStrength, 0f, 10f)));
            pass.Set("uColorFringe", settings.ColorFringe ? 1 : 0);
            pass.Set("uFringeAngles", new vec3(settings.RedAngle, settings.GreenAngle, settings.BlueAngle));
            pass.Set("uFringeOffsets", new vec3(settings.RedOffset, settings.GreenOffset, settings.BlueOffset));
            context.DrawToOther(pass);
        }
    }

    private sealed class BloomEffect(BloomSettings settings, bool transparent, bool captureLensDirt) : IVeldridPostEffect
    {
        public void Apply(VeldridPostContext context)
        {
            var bloom = context.Scratch(0, HdrScratchFormat);
            var ping = context.Scratch(1, HdrScratchFormat);
            var pass = context.Processor.GetPass("bloom.frag");
            void SetCommon(TextureView source, TextureView bloomSource, int mode)
            {
                pass.SetTexture("uScene", source, context.Processor.LinearClamp);
                pass.SetTexture("uBloom", bloomSource, context.Processor.LinearClamp);
                pass.Set("uTexelSize", TexelSize(context));
                pass.Set("uThreshold", settings.Threshold / 100f);
                pass.Set("uIntensity", settings.Intensity / 100f);
                pass.Set("uBlendColor", new vec3(Math.Clamp(settings.BlendColorR, 0f, 1f),
                    Math.Clamp(settings.BlendColorG, 0f, 1f), Math.Clamp(settings.BlendColorB, 0f, 1f)));
                pass.Set("uBlendMode", Math.Clamp((int)settings.BlendMode, 0, 4));
                pass.Set("uTransparentOutput", transparent);
                pass.Set("uPass", mode);
            }

            SetCommon(context.Current.SampleableTextureView, context.Current.SampleableTextureView, 0);
            context.DrawTo(pass, bloom);

            float radius = MathF.Abs(settings.Radius) * 0.0004f * Math.Min(context.Width, context.Height);
            float ratio = MathF.Max(MathF.Abs(settings.AnamorphicRatio), 0.001f) / 100f;
            float horizontal = radius * MathF.Sqrt(ratio);
            float vertical = radius / MathF.Sqrt(ratio);
            int stages = Math.Clamp((int)MathF.Ceiling(MathF.Log2(MathF.Max(1f, MathF.Max(horizontal, vertical)))) + 1, 1, 10);
            for (int stage = 0; stage < stages && radius > 0f; stage++)
            {
                float scale = 0.5f / MathF.Pow(2f, stages - stage - 1);
                SetCommon(bloom.SampleableTextureView, bloom.SampleableTextureView, 1);
                pass.Set("uStep", new vec2(horizontal * scale / context.Width, 0f));
                context.DrawTo(pass, ping);

                SetCommon(ping.SampleableTextureView, ping.SampleableTextureView, 1);
                pass.Set("uStep", new vec2(0f, vertical * scale / context.Height));
                context.DrawTo(pass, bloom);
            }

            if (captureLensDirt)
            {
                SetCommon(bloom.SampleableTextureView, bloom.SampleableTextureView, 3);
                context.DrawTo(pass, context.Scratch(5, HdrScratchFormat));
            }

            SetCommon(context.Current.SampleableTextureView, bloom.SampleableTextureView, 2);
            context.DrawToOther(pass);
        }
    }

    private sealed class GlowEffect(float strength, float size, bool transparent, bool capture) : IVeldridPostEffect
    {
        public void Apply(VeldridPostContext context)
        {
            var glow = context.Scratch(0, HdrScratchFormat);
            var ping = context.Scratch(1, HdrScratchFormat);
            var pass = context.Processor.GetPass("glow.frag");
            void SetCommon(TextureView source, int mode)
            {
                pass.SetTexture("uScene", source, context.Processor.LinearClamp);
                pass.SetTexture("uBaseScene", context.Current.SampleableTextureView, context.Processor.LinearClamp);
                pass.Set("uTexelSize", TexelSize(context));
                pass.Set("uStrength", 1f);
                pass.Set("uSize", size);
                pass.Set("uDirection", new vec2(1f, 0f));
                pass.Set("uMode", mode);
                pass.Set("uTransparentOutput", transparent);
            }

            SetCommon(context.Current.SampleableTextureView, 0);
            context.DrawTo(pass, glow);
            for (int i = 0; i < 3; i++)
            {
                float radius = size / (1f + 1.333f * i);
                SetCommon(glow.SampleableTextureView, 1);
                pass.Set("uSize", radius);
                pass.Set("uDirection", new vec2(1f, 0f));
                context.DrawTo(pass, ping);

                SetCommon(ping.SampleableTextureView, 1);
                pass.Set("uSize", radius);
                pass.Set("uDirection", new vec2(0f, 1f));
                context.DrawTo(pass, glow);
            }

            if (capture) context.Copy(glow, context.Scratch(2, HdrScratchFormat));
            SetCommon(glow.SampleableTextureView, 2);
            pass.Set("uStrength", strength);
            if (transparent)
                context.DrawToOther(pass);
            else
                context.DrawOverCurrent(pass, VeldridFullscreenPass.BlendMode.Additive);
        }
    }

    private sealed class LensDirtEffect(LensDirtSettings settings, VeldridTexture dirt, float glowStrength,
        bool transparent, bool useBloom, bool useGlow) : IVeldridPostEffect
    {
        public void Apply(VeldridPostContext context)
        {
            var extracted = context.Scratch(3, HdrScratchFormat);
            var ping = context.Scratch(4, HdrScratchFormat);
            var pass = context.Processor.GetPass("lens_dirt.frag");
            var glow = useGlow ? context.Scratch(2, HdrScratchFormat).SampleableTextureView : context.Current.SampleableTextureView;
            var bloom = useBloom ? context.Scratch(5, HdrScratchFormat).SampleableTextureView : context.Current.SampleableTextureView;
            void SetCommon(TextureView scene, int mode)
            {
                pass.SetTexture("uScene", scene, context.Processor.LinearClamp);
                pass.SetTexture("uGlow", glow, context.Processor.LinearClamp);
                pass.SetTexture("uBloom", bloom, context.Processor.LinearClamp);
                pass.SetTexture("uDirt", dirt.TextureView, context.Processor.LinearClamp);
                pass.Set("uTexelSize", TexelSize(context));
                pass.Set("uGlowStrength", glowStrength);
                pass.Set("uAffectedByBloom", useBloom);
                pass.Set("uAffectedByGlow", useGlow);
                pass.Set("uIntensity", Math.Clamp(settings.Intensity, 0f, 1000f) / 100f);
                pass.Set("uPower", Math.Clamp(settings.Power, 1f, 1000f) / 100f);
                pass.Set("uTransparentOutput", transparent);
                pass.Set("uPass", mode);
            }

            SetCommon(context.Current.SampleableTextureView, 0);
            context.DrawTo(pass, extracted);
            float radius = Math.Clamp(settings.Radius, 0f, 100f) * 0.0004f * Math.Min(context.Width, context.Height);
            int stages = Math.Clamp((int)MathF.Ceiling(MathF.Log2(MathF.Max(1f, radius))) + 1, 1, 10);
            for (int stage = 0; stage < stages && radius > 0f; stage++)
            {
                float step = radius * 0.5f / MathF.Pow(2f, stages - stage - 1);
                SetCommon(extracted.SampleableTextureView, 1);
                pass.Set("uStep", new vec2(step / context.Width, 0f));
                context.DrawTo(pass, ping);
                SetCommon(ping.SampleableTextureView, 1);
                pass.Set("uStep", new vec2(0f, step / context.Height));
                context.DrawTo(pass, extracted);
            }

            SetCommon(context.Current.SampleableTextureView, 2);
            pass.SetTexture("uBloom", extracted.SampleableTextureView, context.Processor.LinearClamp);
            context.DrawToOther(pass);
        }
    }

    private static vec2 Resolution(VeldridPostContext context) => new(context.Width, context.Height);

    private static vec2 TexelSize(VeldridPostContext context) => new(1f / context.Width, 1f / context.Height);

    private static IEnumerable<CameraEffect> Enabled(CameraSceneObject? camera, CameraEffectType type) =>
        camera == null
            ? []
            : camera.Effects.Where(effect => effect.Enabled && effect.Type == type);

    public static List<IVeldridPostEffect> DepthOfField(CameraSceneObject? camera, float near, float far)
    {
        var effects = new List<IVeldridPostEffect>();
        if (camera == null || !float.IsFinite(near) || !float.IsFinite(far) || far <= near) return effects;
        foreach (var effect in Enabled(camera, CameraEffectType.DepthOfField))
            if (effect.DepthOfField.BlurSize > 0f)
                effects.Add(new DepthOfFieldEffect(effect.DepthOfField, camera, near, far));
        return effects;
    }

    public static List<IVeldridPostEffect> Bloom(CameraSceneObject? camera, bool transparent, bool captureLensDirt = false)
    {
        var effects = new List<IVeldridPostEffect>();
        foreach (var effect in Enabled(camera, CameraEffectType.Bloom))
        {
            var settings = effect.Bloom;
            if (settings.Intensity == 0f) continue;
            foreach (var control in BloomSettings.Controls)
                if (!float.IsFinite(settings.GetValue(control.Key)))
                    throw new InvalidDataException($"Bloom {control.Key} must be finite.");
            effects.Add(new BloomEffect(settings, transparent, captureLensDirt));
        }
        return effects;
    }

    public static IVeldridPostEffect? Glow(PropertiesPanel? settings, bool capture)
    {
        if (settings is not { GlowEnabled: true }) return null;
        float strength = Math.Clamp(settings.GlowStrength, 0f, 2f);
        float size = Math.Clamp(settings.GlowSize, 0f, 20f);
        return strength > 0f && size > 0f
            ? new GlowEffect(strength, size, settings.TransparentBackground, capture)
            : null;
    }

    public static IVeldridPostEffect LensDirt(LensDirtSettings settings, VeldridTexture dirt,
        float glowStrength, bool transparent, bool useBloom, bool useGlow) =>
        new LensDirtEffect(settings, dirt, glowStrength, transparent, useBloom, useGlow);

    public static IVeldridPostEffect Watermark(VeldridTexture image, float width, float height, float scale,
        float padding, int horizontalAlignment, int verticalAlignment, float opacity) =>
        new PassEffect("watermark.frag", (pass, context) =>
        {
            float markWidth = image.Width * scale, markHeight = image.Height * scale;
            float x = Align(width, markWidth, horizontalAlignment, padding);
            float y = Align(height, markHeight, verticalAlignment, padding);
            pass.Set("uFrameSize", new vec2(width, height));
            pass.Set("uRect", new vec4(x, y, markWidth, markHeight));
            pass.Set("uOpacity", Math.Clamp(opacity, 0f, 1f));
            pass.SetTexture("uWatermark", image.TextureView, context.Processor.LinearClamp);
        });

    private static float Align(float frameSize, float markSize, int alignment, float padding) => alignment switch
    {
        0 => padding,
        1 => (frameSize - markSize) * 0.5f,
        _ => frameSize - markSize - padding
    };

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
