using System.Numerics;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.mdl.material.materials;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using NativeFileDialogSharp;
using Silk.NET.OpenGL;
using StbImageSharp;
using TimelinePanel = MineImatorSimplyRemade.core.ui.Panels.Timeline;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class PropertiesPanel : UiPanel
{
    private const string NoImageSelected = "No image selected";
    private const string BackgroundModeStretch = "stretch";
    private const string BackgroundModeFit = "fit";
    private const string BackgroundModeOriginal = "original";
    private string _pbrImportError = "";

    // ── Project tab state ─────────────────────────────────────────────────────

    public Node Floor;

    public readonly float[] BackgroundColor = [0.5764706f, 0.5764706f, 1f, 1f];

    public bool UseSky = true;
    public bool UseAdvancedSky;
    public bool FogEnabled;
    public bool SkyFog = true;
    public bool CustomFogColor;
    public readonly float[] FogColor = [0.5764706f, 0.5764706f, 1f];
    public bool CustomObjectFogColor;
    public readonly float[] ObjectFogColor = [0.5764706f, 0.5764706f, 1f];
    public float FogDistance = 10000f;
    public float FogFadeSize = 2000f;
    public float FogHeight = 1250f;
    public bool HeightFog;
    public bool CustomHeightFogColor;
    public readonly float[] HeightFogColor = [0.5764706f, 0.5764706f, 1f];
    public float HeightFogSize = 4000f;
    public float HeightFogOffset = -3850f;
    public readonly float[] SkyHorizonDay = [0.72f, 0.84f, 1f];
    public readonly float[] SkyZenithDay = [0.28f, 0.55f, 0.95f];
    public readonly float[] SkyHorizonSunset = [1f, 0.48f, 0.2f];
    public readonly float[] SkyZenithSunset = [0.22f, 0.3f, 0.62f];
    public readonly float[] SkyHorizonNight = [0.055f, 0.075f, 0.16f];
    public readonly float[] SkyZenithNight = [0.008f, 0.012f, 0.045f];
    public string SunTexture = "minecraft:environment/sun.png";
    public string MoonTexture = "minecraft:environment/moon_phases.png";
    public string CloudTexture = "minecraft:environment/clouds.png";
    public readonly float[] CloudColor = [1f, 1f, 1f];
    public string CloudRenderMode = "3d";
    public float CloudSpeed;
    public readonly float[] CloudOffset = [0f, 0f];
    public float CloudHeight = 2294f;
    public float CloudBlockSize = 1536f;
    public float CloudThickness = 64f;
    public int MoonPhase;
    public float SkyTime;
    public float SunSize = 16f;
    public readonly float[] SunAngle = [135f, 0f, 0f];
    public float MoonSize = 16f;
    public readonly float[] MoonAngle = [315f, 0f, 0f];
    public readonly float[] SunFillLightColor = [1f, 0.96862745f, 0.89411765f];
    public float SunFillLightStrength = 0.25f;
    public bool SunFillLightCastsShadows = true;
    public readonly float[] MoonFillLightColor = [0.6f, 0.65f, 1f];
    public float MoonFillLightStrength = 0.1f;
    public bool MoonFillLightCastsShadows = false;
    public bool Twilight = true;
    public bool ShowStars = true;
    public float StarDensity = 1f;
    public float StarBrightness = 1f;
    public float StarTwinkleSpeed = 1f;
    public readonly float[] StarColor = [1f, 1f, 1f];
    public readonly float[] NightCloudColor = [1f, 1f, 1f];
    public readonly float[] AmbientLightColor = [1f, 1f, 1f];
    public float AmbientLightStrength = 0.35f;
    public readonly float[] NightAmbientLightColor = [0.05f, 0.05f, 0.2f];
    public float NightAmbientLightStrength = 0.15f;
    public readonly float[] FillLightColor = [0.85f, 0.85f, 0.85f];
    public float FillLightStrength = 1f;
    public bool FillLightCastsShadows = true;

    public bool AntiAliasingEnabled;
    public float AntiAliasingSubpixel = 0.75f;
    public float AntiAliasingEdgeThreshold = 0.125f;
    public float AntiAliasingMinimumThreshold = 0.03125f;
    public bool AmbientOcclusionEnabled = true;
    public float AmbientOcclusionRadius = 12f;
    public float AmbientOcclusionStrength = 1f;
    public int AmbientOcclusionSampleCount = 24;
    public readonly float[] AmbientOcclusionColor = [0f, 0f, 0f];
    public float AmbientOcclusionRatio = 0.222f;
    public float AmbientOcclusionRatioBalance = 0.35f;
    public bool IndirectLightingEnabled = true;
    public string GlobalIlluminationMode = "screenspace";
    public float IndirectLightingPrecision = 0.3f;
    public float IndirectLightingStrength = 1f;
    public float IndirectLightingRayStep = 3f;
    public float IndirectLightingBlurRadius = 1f;
    public bool IndirectLightingDenoiser = true;
    public float IndirectLightingDenoiserStrength = 100f;
    public bool ShadowsEnabled = true;
    public int SunShadowBufferSize = 2048;
    public int SpotShadowBufferSize = 1024;
    public int PointShadowBufferSize = 1024;
    public float ShadowBlurStrength = 1f;
    public bool GlowEnabled;
    public float GlowStrength = 0.6f;
    public float GlowSize = 6f;
    public bool SubsurfaceEnabled;
    public int SubsurfaceBlurSamples = 8;
    public float SubsurfaceStrength = 1f;
    public float SubsurfaceDesaturation = 0f;
    public float SubsurfaceColorThreshold = 0f;
    public readonly float[] SubsurfaceRadiusRgb = [1f, 1f, 1f];
    public float SubsurfaceHighlightSize = 1f;
    public float SubsurfaceHighlightStrength = 1f;
    public float SubsurfaceHighlightSharpness = 2f;
    public float SubsurfaceHighlightDesaturation = 0f;
    public float SubsurfaceHighlightColorThreshold = 0f;
    public float SubsurfaceAbsorption = 0.35f;
    public bool ColorManagementEnabled = false;
    public int ColorManagementToneMapper = 1;
    public float ColorManagementExposure = 0f;
    public float ColorManagementGamma = 2.2f;
    public int ColorManagementLook = 0;
    private static readonly string[] ColorManagementToneMapperOptions =
        ["text.panel.properties.tone_map_reinhard", "text.panel.properties.tone_map_aces", "text.panel.properties.tone_map_uchimura",
         "text.panel.properties.tone_map_lottes", "text.panel.properties.tone_map_hable", "text.panel.properties.tone_map_gt7_curve",
         "text.panel.properties.tone_map_pbr_neutral", "text.panel.properties.tone_map_agx", "text.panel.properties.tone_map_filmic"];
    private static readonly string[] ColorManagementLookOptions =
        ["text.common.none", "text.panel.properties.color_management_look_punchy", "text.panel.properties.color_management_look_grayscale",
         "text.panel.properties.color_management_look_very_high_contrast", "text.panel.properties.color_management_look_high_contrast",
         "text.panel.properties.color_management_look_medium_high_contrast", "text.panel.properties.color_management_look_medium_contrast",
         "text.panel.properties.color_management_look_medium_low_contrast", "text.panel.properties.color_management_look_low_contrast",
         "text.panel.properties.color_management_look_very_low_contrast"];

    private string _projectName = "Untitled Project";
    private string _selectedBackgroundKeyProperty = "SkyTime";
    private static readonly string[] BackgroundKeyframeProperties = BuildBackgroundKeyframeProperties();
    private int _resolutionWidth = 1920;
    private int _resolutionHeight = 1080;
    private int _framerate = 30;
    private string _renderMode = "image";
    private string _renderImageFormat = "png";
    private string _renderVideoFormat = "mp4";
    private int _renderVideoBitrateKbps = 12000;
    private string _renderResolutionPreset = "1080P";
    private bool _renderShowHiddenObjects;
    private string _librarySearch = "";
    private string _selectedLibraryEntryId = "";

    public int GetResolutionWidth()  => _resolutionWidth;
    public int GetResolutionHeight() => _resolutionHeight;
    public int GetFramerate()        => _framerate;
    public string GetRenderMode() => _renderMode;

    private static bool InputFloatEditor(string label, ref float value, float speed, float min, float max, string? format = null)
    {
        if (ImGui.GetIO().WantTextInput)
        {
            string buffer = value.ToString(format ?? "G", CultureInfo.InvariantCulture);
            string previousBuffer = buffer;
            ImGui.SetNextItemWidth(90f);
            bool changed = ImGui.InputText(Localization.DisplayLabel(label), ref buffer, 64, ImGuiInputTextFlags.CharsScientific);

            if (buffer != previousBuffer)
            {
                buffer = SanitizeNumericText(buffer, allowDecimal: true, allowExponent: true);
                if (float.TryParse(buffer, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedValue))
                {
                    value = SanitizeFloatValue(parsedValue, min, max);
                    return true;
                }

                value = SanitizeFloatValue(value, min, max);
            }

            return changed;
        }

        value = SanitizeFloatValue(value, min, max);

        bool changedByDrag = ImGui.DragFloat(
            Localization.DisplayLabel(label),
            ref value,
            speed,
            min,
            max,
            format ?? "%.3f",
            ImGuiSliderFlags.AlwaysClamp);

        if (changedByDrag)
            value = SanitizeFloatValue(value, min, max);

        return changedByDrag;
    }

    private static bool InputIntEditor(string label, ref int value, int step, int stepFast, int min, int max)
    {
        if (ImGui.GetIO().WantTextInput)
        {
            string buffer = value.ToString(CultureInfo.InvariantCulture);
            string previousBuffer = buffer;
            ImGui.SetNextItemWidth(90f);
            bool changed = ImGui.InputText(Localization.DisplayLabel(label), ref buffer, 64, ImGuiInputTextFlags.CharsDecimal);

            if (buffer != previousBuffer)
            {
                buffer = SanitizeNumericText(buffer, allowDecimal: false, allowExponent: false);
                if (int.TryParse(buffer, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedValue))
                {
                    value = SanitizeIntValue(parsedValue, min, max);
                    return true;
                }

                value = SanitizeIntValue(value, min, max);
            }

            return changed;
        }

        value = SanitizeIntValue(value, min, max);

        bool changedByDrag = ImGui.DragInt(
            Localization.DisplayLabel(label),
            ref value,
            step,
            min,
            max,
            "%d",
            ImGuiSliderFlags.AlwaysClamp);

        if (changedByDrag)
            value = SanitizeIntValue(value, min, max);

        return changedByDrag;
    }

    private static float SanitizeFloatValue(float value, float min, float max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            value = min;

        return Math.Clamp(value, min, max);
    }

    private static int SanitizeIntValue(int value, int min, int max)
    {
        return Math.Clamp(value, min, max);
    }

    private static string SanitizeNumericText(string text, bool allowDecimal, bool allowExponent)
    {
        return NumericExpressionParser.SanitizeText(text, allowDecimal, allowExponent);
    }
    public string GetRenderImageFormat() => _renderImageFormat;
    public string GetRenderVideoFormat() => _renderVideoFormat;
    public int GetRenderVideoBitrateKbps() => _renderVideoBitrateKbps;
    public string GetRenderResolutionPreset() => _renderResolutionPreset;
    public bool GetRenderShowHiddenObjects() => _renderShowHiddenObjects;

    public void SetRenderDimensionsAndFramerate(int width, int height, int framerate)
    {
        _resolutionWidth = Math.Max(1, width);
        _resolutionHeight = Math.Max(1, height);
        _framerate = Math.Clamp(framerate, 1, 120);

        if (ProjectManager.Instance.HasProject)
            WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
    }

    public void SetRenderExportSettings(string mode, string imageFormat, string videoFormat, int videoBitrateKbps, string resolutionPreset, bool showHiddenObjects)
    {
        _renderMode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
        _renderImageFormat = string.IsNullOrWhiteSpace(imageFormat) ? "png" : imageFormat.Trim().ToLowerInvariant();
        _renderVideoFormat = string.IsNullOrWhiteSpace(videoFormat) ? "mp4" : videoFormat.Trim().ToLowerInvariant();
        _renderVideoBitrateKbps = Math.Clamp(videoBitrateKbps, 500, 200000);
        _renderResolutionPreset = string.IsNullOrWhiteSpace(resolutionPreset) ? "Custom" : resolutionPreset.Trim();
        _renderShowHiddenObjects = showHiddenObjects;

        if (ProjectManager.Instance.HasProject)
            WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
    }

    public int    TextureAnimationFps  = 20;
    public string BackgroundImagePath  = NoImageSelected;
    public string BackgroundRenderMode = BackgroundModeStretch;
    public float  BackgroundScale      = 1f;
    public float  BackgroundRotationDegrees;
    public readonly float[] BackgroundOffset = [0f, 0f];
    public bool   StretchBackground    = true;
    public bool   FloorVisible         = true;
    public string FloorTextureAtlas    = "block";
    public string FloorTileKey         = "grass_block_top";

    // ── Object tab state ──────────────────────────────────────────────────────

    private SceneObject _currentObject;
    private readonly ObjectNameEditor _nameEditor = new();

    // Scale link toggle
    private bool _linkScale = true;
    private static readonly string[] CameraEffectAddOptions = { "Camera Shake", "Film Grain", "Depth of Field" };
    private static readonly string[] CameraShakeModeOptions = { "Rotational", "Positional", "Both" };

    // ── Right-click keyframe context menu ──────────────────────────────────
    
    private bool              _openPropContextMenu;
    private string?           _ctxPropertyPath;
    private Vector2 _ctxMenuPos;

    // ── Texture tracking for dropdown ────────────────────────────────────────
    private Dictionary<uint, string> _loadedTexturePathCache = new();

    // ── Shape key search filter ──────────────────────────────────────────────
    private string _shapeKeySearch = "";
    private uint[] _cachedTextureIds = Array.Empty<uint>();

    // ── Public wiring ─────────────────────────────────────────────────────────

    /// <summary>Set from MainWindow after both panels are initialised.</summary>
    public Timeline? Timeline { get; set; }
    public Viewport? Viewport { get; set; }
    public SpawnMenu? SpawnMenu { get; set; }

    /// <summary>
    /// Subscribe to SelectionManager events.  Call once from App.Initialize()
    /// after SelectionManager.Initialize() has been called.
    /// </summary>
    public void Initialize()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.SelectionChanged += OnSelectionChanged;

        LoadProjectSettingsFromManifest(ProjectManager.Instance.Manifest);
    }

    public void LoadProjectSettingsFromManifest(ProjectManifest manifest)
    {
        if (manifest == null)
            return;

        _projectName = string.IsNullOrWhiteSpace(manifest.ProjectName) ? "Untitled Project" : manifest.ProjectName;

        ProjectRenderSettings settings = manifest.Settings;
        AntiAliasingEnabled = settings.AntiAliasingEnabled;
        AntiAliasingSubpixel = float.IsFinite(settings.AntiAliasingSubpixel) ? Math.Clamp(settings.AntiAliasingSubpixel, 0f, 1f) : 0.75f;
        AntiAliasingEdgeThreshold = float.IsFinite(settings.AntiAliasingEdgeThreshold) ? Math.Clamp(settings.AntiAliasingEdgeThreshold, 0.03125f, 0.5f) : 0.125f;
        AntiAliasingMinimumThreshold = float.IsFinite(settings.AntiAliasingMinimumThreshold) ? Math.Clamp(settings.AntiAliasingMinimumThreshold, 0.001f, 0.25f) : 0.03125f;
        AmbientOcclusionEnabled = settings.AmbientOcclusionEnabled;
        AmbientOcclusionRadius = Math.Clamp(settings.AmbientOcclusionRadius, 0f, 128f);
        AmbientOcclusionStrength = Math.Clamp(settings.AmbientOcclusionStrength, 0f, 2f);
        AmbientOcclusionSampleCount = Math.Clamp(settings.AmbientOcclusionSampleCount, 1, 128);
        ReadColor(settings.AmbientOcclusionColor, AmbientOcclusionColor);
        AmbientOcclusionRatio = Math.Clamp(settings.AmbientOcclusionRatio, 0f, 1f);
        AmbientOcclusionRatioBalance = Math.Clamp(settings.AmbientOcclusionRatioBalance, 0f, 1f);
        IndirectLightingEnabled = settings.IndirectLightingEnabled;
        GlobalIlluminationMode = NormalizeGlobalIlluminationMode(settings.GlobalIlluminationMode);
        IndirectLightingPrecision = Math.Clamp(settings.IndirectLightingPrecision, 0f, 1f);
        IndirectLightingStrength = Math.Clamp(settings.IndirectLightingStrength, 0f, 4f);
        IndirectLightingRayStep = Math.Clamp(settings.IndirectLightingRayStep, 1f, 64f);
        IndirectLightingBlurRadius = Math.Clamp(settings.IndirectLightingBlurRadius, 0f, 8f);
        IndirectLightingDenoiser = settings.IndirectLightingDenoiser;
        IndirectLightingDenoiserStrength = Math.Clamp(settings.IndirectLightingDenoiserStrength, 0f, 200f);
        ShadowsEnabled = settings.ShadowsEnabled;
        SunShadowBufferSize = NormalizeShadowBufferSize(settings.SunShadowBufferSize, 2048);
        SpotShadowBufferSize = NormalizeShadowBufferSize(settings.SpotShadowBufferSize, 1024);
        PointShadowBufferSize = NormalizeShadowBufferSize(settings.PointShadowBufferSize, 1024);
        ShadowBlurStrength = Math.Clamp(settings.ShadowBlurStrength, 0f, 4f);
        GlowEnabled = settings.GlowEnabled;
        GlowStrength = Math.Clamp(settings.GlowStrength, 0f, 2f);
        GlowSize = Math.Clamp(settings.GlowSize, 0f, 20f);
        SubsurfaceEnabled = settings.SubsurfaceEnabled;
        SubsurfaceBlurSamples = Math.Clamp(settings.SubsurfaceBlurSamples, 0, 32);
        SubsurfaceStrength = Math.Clamp(settings.SubsurfaceStrength, 0f, 4f);
        SubsurfaceDesaturation = Math.Clamp(settings.SubsurfaceDesaturation, 0f, 1f);
        SubsurfaceColorThreshold = Math.Clamp(settings.SubsurfaceColorThreshold, 0f, 1f);
        SubsurfaceRadiusRgb[0] = Math.Clamp(settings.SubsurfaceRadiusR, 0f, 8f);
        SubsurfaceRadiusRgb[1] = Math.Clamp(settings.SubsurfaceRadiusG, 0f, 8f);
        SubsurfaceRadiusRgb[2] = Math.Clamp(settings.SubsurfaceRadiusB, 0f, 8f);
        SubsurfaceHighlightSize = Math.Clamp(settings.SubsurfaceHighlightSize, 0f, 8f);
        SubsurfaceHighlightStrength = Math.Clamp(settings.SubsurfaceHighlightStrength, 0f, 8f);
        SubsurfaceHighlightSharpness = Math.Clamp(settings.SubsurfaceHighlightSharpness, 0.01f, 16f);
        SubsurfaceHighlightDesaturation = Math.Clamp(settings.SubsurfaceHighlightDesaturation, 0f, 1f);
        SubsurfaceHighlightColorThreshold = Math.Clamp(settings.SubsurfaceHighlightColorThreshold, 0f, 1f);
        SubsurfaceAbsorption = Math.Clamp(settings.SubsurfaceAbsorption, -0.95f, 0.95f);
        ColorManagementEnabled = settings.ColorManagementEnabled;
        ColorManagementToneMapper = Math.Clamp(settings.ColorManagementToneMapper, 0, ColorManagementToneMapperOptions.Length - 1);
        ColorManagementExposure = float.IsFinite(settings.ColorManagementExposure) ? Math.Clamp(settings.ColorManagementExposure, -10f, 10f) : 0f;
        ColorManagementGamma = float.IsFinite(settings.ColorManagementGamma) ? Math.Clamp(settings.ColorManagementGamma, 0.1f, 5f) : 2.2f;
        ColorManagementLook = Math.Clamp(settings.ColorManagementLook, 0, ColorManagementLookOptions.Length - 1);
        _resolutionWidth = Math.Max(1, settings.ResolutionWidth);
        _resolutionHeight = Math.Max(1, settings.ResolutionHeight);
        _framerate = Math.Clamp(settings.Framerate, 1, 120);
        _renderMode = string.Equals(settings.RenderMode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
        _renderImageFormat = string.IsNullOrWhiteSpace(settings.RenderImageFormat) ? "png" : settings.RenderImageFormat.Trim().ToLowerInvariant();
        _renderVideoFormat = string.IsNullOrWhiteSpace(settings.RenderVideoFormat) ? "mp4" : settings.RenderVideoFormat.Trim().ToLowerInvariant();
        _renderVideoBitrateKbps = Math.Clamp(settings.RenderVideoBitrateKbps, 500, 200000);
        _renderResolutionPreset = string.IsNullOrWhiteSpace(settings.RenderResolutionPreset) ? "Custom" : settings.RenderResolutionPreset.Trim();
        _renderShowHiddenObjects = settings.RenderShowHiddenObjects;
        TextureAnimationFps = Math.Clamp(settings.TextureAnimationFps, 1, 240);
        UseSky = settings.UseSky;
        UseAdvancedSky = settings.UseAdvancedSky;
        FogEnabled = settings.FogEnabled; SkyFog = settings.SkyFog;
        CustomFogColor = settings.CustomFogColor; ReadColor(settings.FogColor, FogColor);
        CustomObjectFogColor = settings.CustomObjectFogColor; ReadColor(settings.ObjectFogColor, ObjectFogColor);
        FogDistance = Math.Max(0f, settings.FogDistance); FogFadeSize = Math.Max(1f, settings.FogFadeSize); FogHeight = settings.FogHeight;
        HeightFog = settings.HeightFog; CustomHeightFogColor = settings.CustomHeightFogColor;
        ReadColor(settings.HeightFogColor, HeightFogColor);
        HeightFogSize = Math.Max(1f, settings.HeightFogSize); HeightFogOffset = settings.HeightFogOffset;
        ReadColor(settings.SkyHorizonDay, SkyHorizonDay); ReadColor(settings.SkyZenithDay, SkyZenithDay);
        ReadColor(settings.SkyHorizonSunset, SkyHorizonSunset); ReadColor(settings.SkyZenithSunset, SkyZenithSunset);
        ReadColor(settings.SkyHorizonNight, SkyHorizonNight); ReadColor(settings.SkyZenithNight, SkyZenithNight);
        SunTexture = string.IsNullOrWhiteSpace(settings.SunTexture) ? "minecraft:environment/sun.png" : settings.SunTexture;
        MoonTexture = string.IsNullOrWhiteSpace(settings.MoonTexture) ? "minecraft:environment/moon_phases.png" : settings.MoonTexture;
        CloudTexture = string.IsNullOrWhiteSpace(settings.CloudTexture) ? "minecraft:environment/clouds.png" : settings.CloudTexture;
        ReadColor(settings.CloudColor, CloudColor);
        ReadColor(settings.NightCloudColor, NightCloudColor);
        Twilight = settings.Twilight;
        ShowStars = settings.ShowStars;
        StarDensity = Math.Clamp(settings.StarDensity, 0f, 5f);
        StarBrightness = Math.Clamp(settings.StarBrightness, 0f, 5f);
        StarTwinkleSpeed = Math.Clamp(settings.StarTwinkleSpeed, 0f, 5f);
        ReadColor(settings.StarColor, StarColor);
        CloudRenderMode = settings.CloudRenderMode is "story" or "flat" ? settings.CloudRenderMode : "3d";
        CloudSpeed = settings.CloudSpeed;
        CloudOffset[0] = settings.CloudOffsetX; CloudOffset[1] = settings.CloudOffsetY;
        CloudHeight = Math.Max(0f, settings.CloudHeight);
        CloudBlockSize = Math.Max(1f, settings.CloudBlockSize);
        CloudThickness = Math.Max(1f, settings.CloudThickness);
        MoonPhase = Math.Clamp(settings.MoonPhase, 0, 7);
        SkyTime = Math.Clamp(settings.SkyTime, 0f, 24f);
        SunSize = Math.Clamp(settings.SunSize, 0.1f, 90f); MoonSize = Math.Clamp(settings.MoonSize, 0.1f, 90f);
        ReadColor(settings.SunAngle, SunAngle); ReadColor(settings.MoonAngle, MoonAngle);
        ReadColor(settings.SunFillLightColor, SunFillLightColor);
        SunFillLightStrength = Math.Clamp(settings.SunFillLightStrength, 0f, 5f);
        SunFillLightCastsShadows = settings.SunFillLightCastsShadows;
        ReadColor(settings.MoonFillLightColor, MoonFillLightColor);
        MoonFillLightStrength = Math.Clamp(settings.MoonFillLightStrength, 0f, 5f);
        MoonFillLightCastsShadows = settings.MoonFillLightCastsShadows;
        BackgroundRenderMode = NormalizeBackgroundRenderMode(settings.BackgroundRenderMode);
        StretchBackground = settings.StretchBackground;
        if (string.IsNullOrWhiteSpace(settings.BackgroundRenderMode))
            BackgroundRenderMode = StretchBackground ? BackgroundModeStretch : BackgroundModeOriginal;

        BackgroundScale = Math.Clamp(settings.BackgroundScale, 0.01f, 20f);
        BackgroundRotationDegrees = Math.Clamp(settings.BackgroundRotationDegrees, -360f, 360f);
        BackgroundOffset[0] = settings.BackgroundOffsetX;
        BackgroundOffset[1] = settings.BackgroundOffsetY;
        BackgroundImagePath = string.IsNullOrWhiteSpace(settings.BackgroundImagePath)
            ? NoImageSelected
            : settings.BackgroundImagePath;
        FloorVisible = settings.FloorVisible;
        FloorTextureAtlas = NormalizeFloorAtlas(settings.FloorTextureAtlas);
        FloorTileKey = string.IsNullOrWhiteSpace(settings.FloorTileKey)
            ? "grass_block_top"
            : settings.FloorTileKey;

        ProjectVec4 bg = settings.BackgroundColor;
        BackgroundColor[0] = bg.X;
        BackgroundColor[1] = bg.Y;
        BackgroundColor[2] = bg.Z;
        BackgroundColor[3] = bg.W;

        ProjectVec3 ambient = settings.AmbientLightColor;
        AmbientLightColor[0] = ambient.X;
        AmbientLightColor[1] = ambient.Y;
        AmbientLightColor[2] = ambient.Z;
        AmbientLightStrength = settings.AmbientLightStrength;
        ProjectVec3 nightAmbient = settings.NightAmbientLightColor;
        NightAmbientLightColor[0] = nightAmbient.X;
        NightAmbientLightColor[1] = nightAmbient.Y;
        NightAmbientLightColor[2] = nightAmbient.Z;
        NightAmbientLightStrength = settings.NightAmbientLightStrength;
        
        ProjectVec3 fillLight = settings.FillLightColor;
        FillLightColor[0] = fillLight.X;
        FillLightColor[1] = fillLight.Y;
        FillLightColor[2] = fillLight.Z;
        FillLightStrength = settings.FillLightStrength;
        FillLightCastsShadows = settings.FillLightCastsShadows;

        ApplyFloorSettingsToViewport();
        ApplyBackgroundSettingsToViewport();
        ApplyAmbientSettingsToRenderer();
        Timeline?.SetFrameRate(_framerate);
    }

    public void WriteProjectSettingsToManifest(ProjectManifest manifest)
    {
        if (manifest == null)
            return;

        string normalizedName = string.IsNullOrWhiteSpace(_projectName) ? "Untitled Project" : _projectName.Trim();
        _projectName = normalizedName;
        manifest.ProjectName = normalizedName;

        manifest.Settings.AntiAliasingEnabled = AntiAliasingEnabled;
        manifest.Settings.AntiAliasingSubpixel = Math.Clamp(AntiAliasingSubpixel, 0f, 1f);
        manifest.Settings.AntiAliasingEdgeThreshold = Math.Clamp(AntiAliasingEdgeThreshold, 0.03125f, 0.5f);
        manifest.Settings.AntiAliasingMinimumThreshold = Math.Clamp(AntiAliasingMinimumThreshold, 0.001f, 0.25f);
        manifest.Settings.AmbientOcclusionEnabled = AmbientOcclusionEnabled;
        manifest.Settings.AmbientOcclusionRadius = Math.Clamp(AmbientOcclusionRadius, 0f, 128f);
        manifest.Settings.AmbientOcclusionStrength = Math.Clamp(AmbientOcclusionStrength, 0f, 2f);
        manifest.Settings.AmbientOcclusionSampleCount = Math.Clamp(AmbientOcclusionSampleCount, 1, 128);
        manifest.Settings.AmbientOcclusionColor = ToVec3(AmbientOcclusionColor);
        manifest.Settings.AmbientOcclusionRatio = Math.Clamp(AmbientOcclusionRatio, 0f, 1f);
        manifest.Settings.AmbientOcclusionRatioBalance = Math.Clamp(AmbientOcclusionRatioBalance, 0f, 1f);
        manifest.Settings.IndirectLightingEnabled = IndirectLightingEnabled;
        manifest.Settings.GlobalIlluminationMode = NormalizeGlobalIlluminationMode(GlobalIlluminationMode);
        manifest.Settings.IndirectLightingPrecision = Math.Clamp(IndirectLightingPrecision, 0f, 1f);
        manifest.Settings.IndirectLightingStrength = Math.Clamp(IndirectLightingStrength, 0f, 4f);
        manifest.Settings.IndirectLightingRayStep = Math.Clamp(IndirectLightingRayStep, 1f, 64f);
        manifest.Settings.IndirectLightingBlurRadius = Math.Clamp(IndirectLightingBlurRadius, 0f, 8f);
        manifest.Settings.IndirectLightingDenoiser = IndirectLightingDenoiser;
        manifest.Settings.IndirectLightingDenoiserStrength = Math.Clamp(IndirectLightingDenoiserStrength, 0f, 200f);
        manifest.Settings.ShadowsEnabled = ShadowsEnabled;
        manifest.Settings.SunShadowBufferSize = NormalizeShadowBufferSize(SunShadowBufferSize, 2048);
        manifest.Settings.SpotShadowBufferSize = NormalizeShadowBufferSize(SpotShadowBufferSize, 1024);
        manifest.Settings.PointShadowBufferSize = NormalizeShadowBufferSize(PointShadowBufferSize, 1024);
        manifest.Settings.ShadowBlurStrength = Math.Clamp(ShadowBlurStrength, 0f, 4f);
        manifest.Settings.GlowEnabled = GlowEnabled;
        manifest.Settings.GlowStrength = Math.Clamp(GlowStrength, 0f, 2f);
        manifest.Settings.GlowSize = Math.Clamp(GlowSize, 0f, 20f);
        manifest.Settings.SubsurfaceEnabled = SubsurfaceEnabled;
        manifest.Settings.SubsurfaceBlurSamples = Math.Clamp(SubsurfaceBlurSamples, 0, 32);
        manifest.Settings.SubsurfaceStrength = Math.Clamp(SubsurfaceStrength, 0f, 4f);
        manifest.Settings.SubsurfaceDesaturation = Math.Clamp(SubsurfaceDesaturation, 0f, 1f);
        manifest.Settings.SubsurfaceColorThreshold = Math.Clamp(SubsurfaceColorThreshold, 0f, 1f);
        manifest.Settings.SubsurfaceRadiusR = Math.Clamp(SubsurfaceRadiusRgb[0], 0f, 8f);
        manifest.Settings.SubsurfaceRadiusG = Math.Clamp(SubsurfaceRadiusRgb[1], 0f, 8f);
        manifest.Settings.SubsurfaceRadiusB = Math.Clamp(SubsurfaceRadiusRgb[2], 0f, 8f);
        manifest.Settings.SubsurfaceHighlightSize = Math.Clamp(SubsurfaceHighlightSize, 0f, 8f);
        manifest.Settings.SubsurfaceHighlightStrength = Math.Clamp(SubsurfaceHighlightStrength, 0f, 8f);
        manifest.Settings.SubsurfaceHighlightSharpness = Math.Clamp(SubsurfaceHighlightSharpness, 0.01f, 16f);
        manifest.Settings.SubsurfaceHighlightDesaturation = Math.Clamp(SubsurfaceHighlightDesaturation, 0f, 1f);
        manifest.Settings.SubsurfaceHighlightColorThreshold = Math.Clamp(SubsurfaceHighlightColorThreshold, 0f, 1f);
        manifest.Settings.SubsurfaceAbsorption = Math.Clamp(SubsurfaceAbsorption, -0.95f, 0.95f);
        manifest.Settings.ColorManagementEnabled = ColorManagementEnabled;
        manifest.Settings.ColorManagementToneMapper = Math.Clamp(ColorManagementToneMapper, 0, ColorManagementToneMapperOptions.Length - 1);
        manifest.Settings.ColorManagementExposure = float.IsFinite(ColorManagementExposure) ? Math.Clamp(ColorManagementExposure, -10f, 10f) : 0f;
        manifest.Settings.ColorManagementGamma = float.IsFinite(ColorManagementGamma) ? Math.Clamp(ColorManagementGamma, 0.1f, 5f) : 2.2f;
        manifest.Settings.ColorManagementLook = Math.Clamp(ColorManagementLook, 0, ColorManagementLookOptions.Length - 1);

        manifest.Settings.ResolutionWidth = Math.Max(1, _resolutionWidth);
        manifest.Settings.ResolutionHeight = Math.Max(1, _resolutionHeight);
        manifest.Settings.Framerate = Math.Clamp(_framerate, 1, 120);
        manifest.Settings.RenderMode = string.Equals(_renderMode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
        manifest.Settings.RenderImageFormat = string.IsNullOrWhiteSpace(_renderImageFormat) ? "png" : _renderImageFormat.Trim().ToLowerInvariant();
        manifest.Settings.RenderVideoFormat = string.IsNullOrWhiteSpace(_renderVideoFormat) ? "mp4" : _renderVideoFormat.Trim().ToLowerInvariant();
        manifest.Settings.RenderVideoBitrateKbps = Math.Clamp(_renderVideoBitrateKbps, 500, 200000);
        manifest.Settings.RenderResolutionPreset = string.IsNullOrWhiteSpace(_renderResolutionPreset) ? "Custom" : _renderResolutionPreset.Trim();
        manifest.Settings.RenderShowHiddenObjects = _renderShowHiddenObjects;
        manifest.Settings.TextureAnimationFps = Math.Clamp(TextureAnimationFps, 1, 240);
        manifest.Settings.UseSky = UseSky;
        manifest.Settings.UseAdvancedSky = UseAdvancedSky;
        manifest.Settings.FogEnabled = FogEnabled; manifest.Settings.SkyFog = SkyFog;
        manifest.Settings.CustomFogColor = CustomFogColor; manifest.Settings.FogColor = ToVec3(FogColor);
        manifest.Settings.CustomObjectFogColor = CustomObjectFogColor; manifest.Settings.ObjectFogColor = ToVec3(ObjectFogColor);
        manifest.Settings.FogDistance = Math.Max(0f, FogDistance); manifest.Settings.FogFadeSize = Math.Max(1f, FogFadeSize); manifest.Settings.FogHeight = FogHeight;
        manifest.Settings.HeightFog = HeightFog; manifest.Settings.CustomHeightFogColor = CustomHeightFogColor;
        manifest.Settings.HeightFogColor = ToVec3(HeightFogColor);
        manifest.Settings.HeightFogSize = Math.Max(1f, HeightFogSize); manifest.Settings.HeightFogOffset = HeightFogOffset;
        manifest.Settings.SkyHorizonDay = ToVec3(SkyHorizonDay); manifest.Settings.SkyZenithDay = ToVec3(SkyZenithDay);
        manifest.Settings.SkyHorizonSunset = ToVec3(SkyHorizonSunset); manifest.Settings.SkyZenithSunset = ToVec3(SkyZenithSunset);
        manifest.Settings.SkyHorizonNight = ToVec3(SkyHorizonNight); manifest.Settings.SkyZenithNight = ToVec3(SkyZenithNight);
        manifest.Settings.SunTexture = SunTexture; manifest.Settings.MoonTexture = MoonTexture;
        manifest.Settings.CloudTexture = CloudTexture;
        manifest.Settings.CloudColor = ToVec3(CloudColor);
        manifest.Settings.NightCloudColor = ToVec3(NightCloudColor);
        manifest.Settings.Twilight = Twilight;
        manifest.Settings.ShowStars = ShowStars;
        manifest.Settings.StarDensity = StarDensity;
        manifest.Settings.StarBrightness = StarBrightness;
        manifest.Settings.StarTwinkleSpeed = StarTwinkleSpeed;
        manifest.Settings.StarColor = ToVec3(StarColor);
        manifest.Settings.CloudRenderMode = CloudRenderMode;
        manifest.Settings.CloudSpeed = CloudSpeed;
        manifest.Settings.CloudOffsetX = CloudOffset[0]; manifest.Settings.CloudOffsetY = CloudOffset[1];
        manifest.Settings.CloudHeight = Math.Max(0f, CloudHeight);
        manifest.Settings.CloudBlockSize = Math.Max(1f, CloudBlockSize);
        manifest.Settings.CloudThickness = Math.Max(1f, CloudThickness);
        manifest.Settings.MoonPhase = Math.Clamp(MoonPhase, 0, 7);
        manifest.Settings.SkyTime = Math.Clamp(SkyTime, 0f, 24f);
        manifest.Settings.SunSize = Math.Clamp(SunSize, 0.1f, 90f); manifest.Settings.MoonSize = Math.Clamp(MoonSize, 0.1f, 90f);
        manifest.Settings.SunAngle = ToVec3(SunAngle); manifest.Settings.MoonAngle = ToVec3(MoonAngle);
        manifest.Settings.SunFillLightColor = ToVec3(SunFillLightColor);
        manifest.Settings.SunFillLightStrength = Math.Clamp(SunFillLightStrength, 0f, 5f);
        manifest.Settings.SunFillLightCastsShadows = SunFillLightCastsShadows;
        manifest.Settings.MoonFillLightColor = ToVec3(MoonFillLightColor);
        manifest.Settings.MoonFillLightStrength = Math.Clamp(MoonFillLightStrength, 0f, 5f);
        manifest.Settings.MoonFillLightCastsShadows = MoonFillLightCastsShadows;
        BackgroundRenderMode = NormalizeBackgroundRenderMode(BackgroundRenderMode);
        BackgroundScale = Math.Clamp(BackgroundScale, 0.01f, 20f);
        BackgroundRotationDegrees = Math.Clamp(BackgroundRotationDegrees, -360f, 360f);

        manifest.Settings.BackgroundRenderMode = BackgroundRenderMode;
        manifest.Settings.StretchBackground = string.Equals(BackgroundRenderMode, BackgroundModeStretch, StringComparison.OrdinalIgnoreCase);
        manifest.Settings.BackgroundScale = BackgroundScale;
        manifest.Settings.BackgroundRotationDegrees = BackgroundRotationDegrees;
        manifest.Settings.BackgroundOffsetX = BackgroundOffset[0];
        manifest.Settings.BackgroundOffsetY = BackgroundOffset[1];
        manifest.Settings.BackgroundImagePath = string.IsNullOrWhiteSpace(BackgroundImagePath)
            ? NoImageSelected
            : ProjectManager.Instance.ToProjectRelativePath(BackgroundImagePath);
        manifest.Settings.FloorVisible = FloorVisible;
        manifest.Settings.FloorTextureAtlas = NormalizeFloorAtlas(FloorTextureAtlas);
        manifest.Settings.FloorTileKey = string.IsNullOrWhiteSpace(FloorTileKey)
            ? "grass_block_top"
            : FloorTileKey;

        manifest.Settings.BackgroundColor = new ProjectVec4
        {
            X = BackgroundColor[0],
            Y = BackgroundColor[1],
            Z = BackgroundColor[2],
            W = BackgroundColor[3]
        };
        manifest.Settings.AmbientLightColor = new ProjectVec3
        {
            X = AmbientLightColor[0],
            Y = AmbientLightColor[1],
            Z = AmbientLightColor[2]
        };
        manifest.Settings.AmbientLightStrength = AmbientLightStrength;
        manifest.Settings.NightAmbientLightColor = new ProjectVec3
        {
            X = NightAmbientLightColor[0],
            Y = NightAmbientLightColor[1],
            Z = NightAmbientLightColor[2]
        };
        manifest.Settings.NightAmbientLightStrength = NightAmbientLightStrength;
        manifest.Settings.FillLightColor = new ProjectVec3
        {
            X = FillLightColor[0],
            Y = FillLightColor[1],
            Z = FillLightColor[2]
        };
        manifest.Settings.FillLightStrength = FillLightStrength;
        manifest.Settings.FillLightCastsShadows = FillLightCastsShadows;
    }

    private static void ReadColor(ProjectVec3 value, float[] target)
    {
        target[0] = value.X; target[1] = value.Y; target[2] = value.Z;
    }

    private static ProjectVec3 ToVec3(float[] value) => new() { X = value[0], Y = value[1], Z = value[2] };

    private static int NormalizeShadowBufferSize(int value, int fallback)
    {
        int[] sizes = [256, 512, 1024, 2048, 4096, 8192];
        return sizes.Contains(value) ? value : fallback;
    }

    private static string NormalizeGlobalIlluminationMode(string mode)
    {
        return string.Equals(mode, "world", StringComparison.OrdinalIgnoreCase)
            ? "world"
            : "screenspace";
    }

    private void ApplyAmbientSettingsToRenderer()
    {
        AmbientLightColor[0] = Math.Clamp(AmbientLightColor[0], 0f, 1f);
        AmbientLightColor[1] = Math.Clamp(AmbientLightColor[1], 0f, 1f);
        AmbientLightColor[2] = Math.Clamp(AmbientLightColor[2], 0f, 1f);
        AmbientLightStrength = Math.Clamp(AmbientLightStrength, 0f, 5f);

        Mesh.GlobalAmbientColor = new vec3(
            AmbientLightColor[0],
            AmbientLightColor[1],
            AmbientLightColor[2]);
        Mesh.GlobalAmbientStrength = AmbientLightStrength;
        
        FillLightColor[0] = Math.Clamp(FillLightColor[0], 0f, 1f);
        FillLightColor[1] = Math.Clamp(FillLightColor[1], 0f, 1f);
        FillLightColor[2] = Math.Clamp(FillLightColor[2], 0f, 1f);
        FillLightStrength = Math.Clamp(FillLightStrength, 0f, 5f);

        Mesh.GlobalFillLightColor = new vec3(
            FillLightColor[0],
            FillLightColor[1],
            FillLightColor[2]);
        Mesh.GlobalFillLightStrength = FillLightStrength;
        Mesh.DirectionalShadowEnabled = FillLightCastsShadows;
    }

    private static string NormalizeFloorAtlas(string atlas)
    {
        return string.Equals(atlas, "item", StringComparison.OrdinalIgnoreCase) ? "item" : "block";
    }

    private static string NormalizeBackgroundRenderMode(string mode)
    {
        if (string.Equals(mode, BackgroundModeFit, StringComparison.OrdinalIgnoreCase))
            return BackgroundModeFit;
        if (string.Equals(mode, BackgroundModeOriginal, StringComparison.OrdinalIgnoreCase))
            return BackgroundModeOriginal;
        return BackgroundModeStretch;
    }

    private static string? ExtractItemTileKeyFromObjectType(string objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType))
            return null;

        int open = objectType.IndexOf('[');
        int close = objectType.LastIndexOf(']');
        if (open < 0 || close <= open)
            return null;

        return objectType[(open + 1)..close];
    }

    private static IEnumerable<string> GetItemAtlasKeys(ItemAtlasSource atlasSource)
    {
        if (atlasSource == ItemAtlasSource.ItemAtlas)
            ItemsAtlas.EnsureProjectCustomTexturesLoaded();

        if (atlasSource == ItemAtlasSource.LocalAtlas)
            return Enumerable.Empty<string>();

        var atlas = atlasSource == ItemAtlasSource.BlockAtlas ? TerrainAtlas.Textures : ItemsAtlas.Textures;
        return atlas.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    private ItemAtlasSource GetObjectItemAtlasSource(SceneObject obj)
    {
        if (!string.Equals(obj.SpawnCategory, "Items", StringComparison.Ordinal))
            return ItemAtlasSource.ItemAtlas;

        if (string.Equals(obj.TextureType, "block", StringComparison.OrdinalIgnoreCase))
            return ItemAtlasSource.BlockAtlas;

        if (string.Equals(obj.TextureType, "local", StringComparison.OrdinalIgnoreCase) &&
            obj.TemporaryItemSheetColumns > 0 && obj.TemporaryItemSheetRows > 0)
            return ItemAtlasSource.LocalAtlas;

        return ItemAtlasSource.ItemAtlas;
    }

    private IEnumerable<(string Key, int Column, int Row)> GetLocalItemSheetKeys(SceneObject obj)
    {
        if (SpawnMenu == null || obj == null || obj.TemporaryItemSheetColumns <= 0 || obj.TemporaryItemSheetRows <= 0)
            yield break;

        for (int row = 0; row < obj.TemporaryItemSheetRows; row++)
        {
            for (int column = 0; column < obj.TemporaryItemSheetColumns; column++)
            {
                string? key = SpawnMenu.EnsureTemporaryItemSheetTile(obj, column, row);
                if (!string.IsNullOrWhiteSpace(key))
                    yield return (key, column, row);
            }
        }
    }

    private IEnumerable<string> GetFloorAtlasKeys()
    {
        if (NormalizeFloorAtlas(FloorTextureAtlas) == "item")
            ItemsAtlas.EnsureProjectCustomTexturesLoaded();

        var atlas = NormalizeFloorAtlas(FloorTextureAtlas) == "item" ? ItemsAtlas.Textures : TerrainAtlas.Textures;
        return atlas.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyFloorSettingsToViewport()
    {
        if (Viewport == null)
            return;

        FloorTextureAtlas = NormalizeFloorAtlas(FloorTextureAtlas);
        Viewport.SetGroundPlaneVisible(FloorVisible);

        if (!Viewport.SetGroundPlaneTexture(FloorTextureAtlas, FloorTileKey))
        {
            string? fallback = GetFloorAtlasKeys().FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                FloorTileKey = fallback;
                Viewport.SetGroundPlaneTexture(FloorTextureAtlas, FloorTileKey);
            }
        }
    }

    private void ApplyBackgroundSettingsToViewport()
    {
        Viewport?.SetBackgroundImage(
            BackgroundImagePath,
            BackgroundRenderMode,
            BackgroundScale,
            BackgroundRotationDegrees,
            new Vector2(BackgroundOffset[0], BackgroundOffset[1]));
    }

    private IReadOnlyList<ProjectAssetEntry> GetBackgroundImageAssets()
    {
        return ProjectManager.Instance
            .GetProjectAssets()
            .Where(asset => asset.AssetType == ProjectAssetType.Image)
            .ToList();
    }

    private void RenderObjectLibrarySection(ProjectManager projectManager)
    {
        if (Viewport == null || SpawnMenu == null)
        {
            UiText.Disabled(Localization.T("text.panel.properties.object_library_is_unavailable_until_the_viewport_is_ready"));
            return;
        }

        EnsureObjectLibraryInitialized(projectManager.Manifest);

        var library = projectManager.Manifest.ObjectLibrary;
        var libraryUsageCounts = BuildLibraryUsageCounts();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ProjectObjectLibrarySearch", Localization.T("text.panel.properties.search_spawned_objects"), ref _librarySearch, 256);
        ImGui.Spacing();

        float treeHeight = MathF.Min(360f, MathF.Max(120f, ImGui.GetContentRegionAvail().Y * 0.6f));
        if (ImGui.BeginChild("##ProjectObjectLibraryTree", new Vector2(0f, treeHeight), ImGuiChildFlags.Borders))
        {
            bool anyVisible = false;
            foreach (var root in BuildLibraryTreeRoots(library).OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase))
                anyVisible |= RenderObjectLibraryTreeNode(root, libraryUsageCounts);

            if (!anyVisible)
                UiText.Disabled(Localization.T("text.panel.properties.no_library_objects_match_your_search"));
        }
        ImGui.EndChild();

        var selected = FindLibraryEntryById(library, _selectedLibraryEntryId);
        if (selected != null)
        {
            int usageCount = GetLibraryUsageCount(libraryUsageCounts, selected.LibraryEntryId);
            string type = string.IsNullOrWhiteSpace(selected.ObjectType) ? "Object" : selected.ObjectType;
            UiText.Disabled(Localization.Format("text.panel.properties.type_2", type));
            UiText.Disabled(Localization.Format("text.panel.properties.used_in_scene", usageCount));

            if (ImGui.Button(Localization.Label("text.panel.properties.create_in_scene_from_base", "Create In Scene From Base")))
            {
                SceneObject? created = ProjectSceneSerializer.SpawnObjectFromEntry(selected, Viewport, SpawnMenu);
                if (created != null)
                {
                    SelectionManager.Instance?.ClearSelection();
                    SelectionManager.Instance?.SelectObject(created);
                    projectManager.SetDirty(true);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button(Localization.Label("text.panel.properties.delete_from_library", "Delete From Library")))
            {
                string nextSelectionId = GetNextLibrarySelectionIdAfterDeletion(projectManager.Manifest.ObjectLibrary, selected.LibraryEntryId);
                RemoveLibraryEntry(projectManager.Manifest, selected.LibraryEntryId);
                RemoveSceneObjectsFromLibrary(selected.LibraryEntryId);
                _selectedLibraryEntryId = nextSelectionId;
                projectManager.SetDirty(true);
            }
        }
        else if (library.Count == 0)
        {
            UiText.Disabled(Localization.T("text.panel.properties.no_objects_have_been_spawned_yet"));
        }

        bool hasSelectedLibraryObject = selected != null;
        if (!hasSelectedLibraryObject)
            ImGui.BeginDisabled();

        if (ImGui.Button(Localization.Label("text.panel.properties.duplicate_selected_library_object", "Duplicate Selected Library Object")) && selected != null)
        {
            var newEntry = CloneLibraryEntryRecursive(selected);
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectLibraryEntryIds(projectManager.Manifest.ObjectLibrary, usedIds);
            EnsureUniqueLibraryIdsRecursive(newEntry, usedIds);
            newEntry.Name = EnsureUniqueLibraryName(projectManager.Manifest.ObjectLibrary, (selected.Name ?? "Object") + " Copy");
            projectManager.Manifest.ObjectLibrary.Add(newEntry);
            _selectedLibraryEntryId = newEntry.LibraryEntryId;
            projectManager.SetDirty(true);
        }

        if (!hasSelectedLibraryObject)
            ImGui.EndDisabled();
    }

    private static ProjectSceneObjectEntry CloneLibraryEntryRecursive(ProjectSceneObjectEntry source)
    {
        var clone = new ProjectSceneObjectEntry
        {
            LibraryEntryId = source.LibraryEntryId,
            LibrarySourceId = source.LibrarySourceId,
            ObjectId = source.ObjectId,
            Name = source.Name,
            ObjectType = source.ObjectType,
            BoneName = source.BoneName,
            BoneEndOffset = source.BoneEndOffset == null ? null : new ProjectVec3
            {
                X = source.BoneEndOffset.X, Y = source.BoneEndOffset.Y, Z = source.BoneEndOffset.Z
            },
            BoneConstraints = source.BoneConstraints?.Select(constraint => new ProjectBoneConstraintEntry
            {
                Enabled = constraint.Enabled,
                Type = constraint.Type,
                InverseKinematics = new ProjectInverseKinematicsSettings
                {
                    TargetObjectId = constraint.InverseKinematics.TargetObjectId,
                    PoleObjectId = constraint.InverseKinematics.PoleObjectId,
                    ChainLength = constraint.InverseKinematics.ChainLength,
                    Iterations = constraint.InverseKinematics.Iterations,
                    PoleAngle = constraint.InverseKinematics.PoleAngle,
                    Influence = constraint.InverseKinematics.Influence,
                    LockOrientation = constraint.InverseKinematics.LockOrientation,
                    Bones = constraint.InverseKinematics.Bones.Select(participation => new ProjectIkBoneSettings
                    {
                        BoneObjectId = participation.BoneObjectId,
                        AllowRotation = participation.AllowRotation,
                        AllowBend = participation.AllowBend
                    }).ToList()
                }
            }).ToList(),
            CopyConstraints = source.CopyConstraints?.Select(constraint => new ProjectCopyConstraintEntry
            {
                Enabled = constraint.Enabled, Type = constraint.Type, TargetObjectId = constraint.TargetObjectId,
                Offset = constraint.Offset == null ? null : new ProjectVec3
                {
                    X = constraint.Offset.X, Y = constraint.Offset.Y, Z = constraint.Offset.Z
                },
                AxisX = constraint.AxisX, AxisY = constraint.AxisY, AxisZ = constraint.AxisZ,
                Blend = constraint.Blend, BentHalf = constraint.BentHalf, Rotate = constraint.Rotate
            }).ToList(),
            SpawnCategory = source.SpawnCategory,
            BlockVariant = source.BlockVariant,
            TextureType = source.TextureType,
            ResourcePackId = source.ResourcePackId,
            SourceAssetPath = source.SourceAssetPath,
            AlbedoTexturePath = source.AlbedoTexturePath,
            NormalTexturePath = source.NormalTexturePath,
            EmissionTexturePath = source.EmissionTexturePath,
            SurfaceTexturePath = source.SurfaceTexturePath,
            OcclusionTexturePath = source.OcclusionTexturePath,
            RoughnessTexturePath = source.RoughnessTexturePath,
            MetallicTexturePath = source.MetallicTexturePath,
            HeightTexturePath = source.HeightTexturePath,
            NormalResourcePackId = source.NormalResourcePackId,
            EmissionResourcePackId = source.EmissionResourcePackId,
            CameraTextureObjectId = source.CameraTextureObjectId,
            TextureOverridePath = source.TextureOverridePath,
            TileX = source.TileX,
            TileY = source.TileY,
            TileZ = source.TileZ,
            BlockMeshOriginVersion = source.BlockMeshOriginVersion,
            Position = new ProjectVec3 { X = source.Position.X, Y = source.Position.Y, Z = source.Position.Z },
            Rotation = new ProjectVec3 { X = source.Rotation.X, Y = source.Rotation.Y, Z = source.Rotation.Z },
            Scale = new ProjectVec3 { X = source.Scale.X, Y = source.Scale.Y, Z = source.Scale.Z },
            BendAngle = source.BendAngle == null
                ? null
                : new ProjectVec3 { X = source.BendAngle.X, Y = source.BendAngle.Y, Z = source.BendAngle.Z },
            PivotOffset = new ProjectVec3 { X = source.PivotOffset.X, Y = source.PivotOffset.Y, Z = source.PivotOffset.Z },
            InheritPosition = source.InheritPosition,
            InheritRotation = source.InheritRotation,
            InheritScale = source.InheritScale,
            UsePropertyInheritance = source.UsePropertyInheritance,
            InheritAlpha = source.InheritAlpha,
            InheritColor = source.InheritColor,
            InheritTexture = source.InheritTexture,
            InheritSurface = source.InheritSurface,
            InheritSubsurface = source.InheritSubsurface,
            InheritGlowColor = source.InheritGlowColor,
            InheritSelect = source.InheritSelect,
            InheritPose = source.InheritPose,
            ScaleResize = source.ScaleResize,
            LockBend = source.LockBend,
            LockToBentHalf = source.LockToBentHalf,
            InheritBend = source.InheritBend,
            InheritPivotOffset = source.InheritPivotOffset,
            PivotOffsetInheritsModelOrigin = source.PivotOffsetInheritsModelOrigin,
            InheritVisibility = source.InheritVisibility,
            ObjectVisible = source.ObjectVisible,
            IsHidden = source.IsHidden,
            CastShadow = source.CastShadow,
            InvertFaces = source.InvertFaces,
            BlurTexture = source.BlurTexture,
            TextureMipmaps = source.TextureMipmaps,
            IncludeInAmbientOcclusion = source.IncludeInAmbientOcclusion,
            IncludeInFog = source.IncludeInFog,
            RenderInHighQuality = source.RenderInHighQuality,
            RenderInLowQuality = source.RenderInLowQuality,
            RenderDepthOffset = source.RenderDepthOffset,
            IsSelectable = source.IsSelectable,
            HideInSceneTree = source.HideInSceneTree,
            HasMaterialOverrides = source.HasMaterialOverrides,
            AlbedoColor = new ProjectVec4 { X = source.AlbedoColor.X, Y = source.AlbedoColor.Y, Z = source.AlbedoColor.Z, W = source.AlbedoColor.W },
            BlendColor = new ProjectVec4 { X = source.BlendColor.X, Y = source.BlendColor.Y, Z = source.BlendColor.Z, W = source.BlendColor.W },
            MixColor = new ProjectVec4 { X = source.MixColor.X, Y = source.MixColor.Y, Z = source.MixColor.Z, W = source.MixColor.W },
            Metallic = source.Metallic,
            Roughness = source.Roughness,
            PbrEnabled = source.PbrEnabled,
            NormalStrength = source.NormalStrength,
            OcclusionStrength = source.OcclusionStrength,
            HeightScale = source.HeightScale,
            NormalEnabled = source.NormalEnabled,
            Transparency = source.Transparency,
            DoubleSided = source.DoubleSided,
            TextureOffsetH = source.TextureOffsetH,
            TextureOffsetV = source.TextureOffsetV,
            TextureRepeatH = source.TextureRepeatH,
            TextureRepeatV = source.TextureRepeatV,
            TextureMirrorH = source.TextureMirrorH,
            TextureMirrorV = source.TextureMirrorV,
            EmissionEnabled = source.EmissionEnabled,
            EmissionColor = new ProjectVec4 { X = source.EmissionColor.X, Y = source.EmissionColor.Y, Z = source.EmissionColor.Z, W = source.EmissionColor.W },
            EmissionEnergy = source.EmissionEnergy,
            Subsurface = source.Subsurface,
            SubsurfaceRadiusR = source.SubsurfaceRadiusR,
            SubsurfaceRadiusG = source.SubsurfaceRadiusG,
            SubsurfaceRadiusB = source.SubsurfaceRadiusB,
            SubsurfaceColor = new ProjectVec4 { X = source.SubsurfaceColor.X, Y = source.SubsurfaceColor.Y, Z = source.SubsurfaceColor.Z, W = source.SubsurfaceColor.W },
            SubsurfaceHighlight = source.SubsurfaceHighlight,
            SubsurfaceHighlightStrength = source.SubsurfaceHighlightStrength,
            EmissionIndirectOnly = source.EmissionIndirectOnly,
            AutoEmission = source.AutoEmission,
            ItemTileKey = source.ItemTileKey,
            ItemIs3D = source.ItemIs3D,
            ParticleLibraryEntryId = source.ParticleLibraryEntryId,
            ParticleLibraryDisplayName = source.ParticleLibraryDisplayName,
            ParticleEmitting = source.ParticleEmitting,
            ParticleOneShot = source.ParticleOneShot,
            ParticleAmount = source.ParticleAmount,
            ParticleSpawnRate = source.ParticleSpawnRate,
            ParticleLifetimeMin = source.ParticleLifetimeMin,
            ParticleLifetimeMax = source.ParticleLifetimeMax,
            ParticleSimulationSpeed = source.ParticleSimulationSpeed,
            ParticleLinearDamping = source.ParticleLinearDamping,
            ParticleAngularDamping = source.ParticleAngularDamping,
            ParticleEmissionShape = source.ParticleEmissionShape,
            ParticleUseDirectionalEmission = source.ParticleUseDirectionalEmission,
            ParticleDirection = new ProjectVec3
            {
                X = source.ParticleDirection.X,
                Y = source.ParticleDirection.Y,
                Z = source.ParticleDirection.Z
            },
            ParticleSpreadDegrees = source.ParticleSpreadDegrees,
            ParticleInitialSpeedMin = source.ParticleInitialSpeedMin,
            ParticleInitialSpeedMax = source.ParticleInitialSpeedMax,
            ParticleSpawnBoxExtents = new ProjectVec3
            {
                X = source.ParticleSpawnBoxExtents.X,
                Y = source.ParticleSpawnBoxExtents.Y,
                Z = source.ParticleSpawnBoxExtents.Z
            },
            ParticleInitialVelocityMin = new ProjectVec3
            {
                X = source.ParticleInitialVelocityMin.X,
                Y = source.ParticleInitialVelocityMin.Y,
                Z = source.ParticleInitialVelocityMin.Z
            },
            ParticleInitialVelocityMax = new ProjectVec3
            {
                X = source.ParticleInitialVelocityMax.X,
                Y = source.ParticleInitialVelocityMax.Y,
                Z = source.ParticleInitialVelocityMax.Z
            },
            ParticleGravity = new ProjectVec3
            {
                X = source.ParticleGravity.X,
                Y = source.ParticleGravity.Y,
                Z = source.ParticleGravity.Z
            },
            ParticleInitialRotationMinDegrees = new ProjectVec3
            {
                X = source.ParticleInitialRotationMinDegrees.X,
                Y = source.ParticleInitialRotationMinDegrees.Y,
                Z = source.ParticleInitialRotationMinDegrees.Z
            },
            ParticleInitialRotationMaxDegrees = new ProjectVec3
            {
                X = source.ParticleInitialRotationMaxDegrees.X,
                Y = source.ParticleInitialRotationMaxDegrees.Y,
                Z = source.ParticleInitialRotationMaxDegrees.Z
            },
            ParticleAngularVelocityMinDegrees = new ProjectVec3
            {
                X = source.ParticleAngularVelocityMinDegrees.X,
                Y = source.ParticleAngularVelocityMinDegrees.Y,
                Z = source.ParticleAngularVelocityMinDegrees.Z
            },
            ParticleAngularVelocityMaxDegrees = new ProjectVec3
            {
                X = source.ParticleAngularVelocityMaxDegrees.X,
                Y = source.ParticleAngularVelocityMaxDegrees.Y,
                Z = source.ParticleAngularVelocityMaxDegrees.Z
            },
            ParticleStartScaleMin = source.ParticleStartScaleMin,
            ParticleStartScaleMax = source.ParticleStartScaleMax,
            ParticleEndScaleMin = source.ParticleEndScaleMin,
            ParticleEndScaleMax = source.ParticleEndScaleMax,
            ParticleTopLevelParticles = source.ParticleTopLevelParticles,
            PrimitivePlaneOrientation = source.PrimitivePlaneOrientation,
            PrimitivePlaneFaceCamera = source.PrimitivePlaneFaceCamera,
            PrimitiveCubeMapped = source.PrimitiveCubeMapped,
            PrimitiveSphereSmooth = source.PrimitiveSphereSmooth,
            PrimitiveSphereSegments = source.PrimitiveSphereSegments,
            PrimitiveSphereRings = source.PrimitiveSphereRings,
            PrimitiveRadialSegments = source.PrimitiveRadialSegments,
            PrimitiveRadialSmooth = source.PrimitiveRadialSmooth,
            PrimitiveRadialClosed = source.PrimitiveRadialClosed,
            PrimitiveRadialMapped = source.PrimitiveRadialMapped,
            TextMeshFontPath = source.TextMeshFontPath,
            TextMeshBaseString = source.TextMeshBaseString,
            TextMeshStringOverride = source.TextMeshStringOverride,
            TextMeshExtruded = source.TextMeshExtruded,
            TextMeshExtrusionDepth = source.TextMeshExtrusionDepth,
            TextMeshFaceCamera = source.TextMeshFaceCamera,
            TextMeshHorizontalAlignment = source.TextMeshHorizontalAlignment,
            TextMeshVerticalAlignment = source.TextMeshVerticalAlignment,
            TextMeshAntialiasing = source.TextMeshAntialiasing,
            TextMeshFontSize = source.TextMeshFontSize,
            TextMeshOutlineEnabled = source.TextMeshOutlineEnabled,
            TextMeshOutlineColor = source.TextMeshOutlineColor,
            TextMeshOutlineThickness = source.TextMeshOutlineThickness,
            CameraFov = source.CameraFov,
            CameraNear = source.CameraNear,
            CameraFar = source.CameraFar,
            CameraActive = source.CameraActive,
            CameraColorManagementOverrideEnabled = source.CameraColorManagementOverrideEnabled,
            CameraColorManagementEnabled = source.CameraColorManagementEnabled,
            CameraColorManagementToneMapper = source.CameraColorManagementToneMapper,
            CameraColorManagementExposure = source.CameraColorManagementExposure,
            CameraColorManagementGamma = source.CameraColorManagementGamma,
            CameraColorManagementLook = source.CameraColorManagementLook,
            CameraEffects = source.CameraEffects
                .Select(effect => new ProjectCameraEffectEntry
                {
                    Type = effect.Type,
                    DepthOfField = effect.DepthOfField.Clone(),
                    Shake = new ProjectCameraShakeSettings
                    {
                        Mode = effect.Shake.Mode,
                        Trauma = effect.Shake.Trauma,
                        Strength = new ProjectVec3
                        {
                            X = effect.Shake.Strength.X,
                            Y = effect.Shake.Strength.Y,
                            Z = effect.Shake.Strength.Z
                        },
                        Speed = new ProjectVec3
                        {
                            X = effect.Shake.Speed.X,
                            Y = effect.Shake.Speed.Y,
                            Z = effect.Shake.Speed.Z
                        },
                        Offset = new ProjectVec3
                        {
                            X = effect.Shake.Offset.X,
                            Y = effect.Shake.Offset.Y,
                            Z = effect.Shake.Offset.Z
                        }
                    },
                    FilmGrain = new ProjectFilmGrainSettings
                    {
                        Strength = effect.FilmGrain.Strength,
                        Saturation = effect.FilmGrain.Saturation,
                        Size = effect.FilmGrain.Size
                    }
                })
                .ToList(),
            LightColor = new ProjectVec4 { X = source.LightColor.X, Y = source.LightColor.Y, Z = source.LightColor.Z, W = source.LightColor.W },
            LightEnergy = source.LightEnergy,
            LightRange = source.LightRange,
            LightIndirectEnergy = source.LightIndirectEnergy,
            LightSpecular = source.LightSpecular,
            LightShadowEnabled = source.LightShadowEnabled,
            LightType = source.LightType,
            LightSpotAngle = source.LightSpotAngle,
            LightSpotBlend = source.LightSpotBlend,
            Keyframes = source.Keyframes.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(k => new ProjectKeyframeEntry
                {
                    Frame = k.Frame,
                    Value = k.Value,
                    InterpolationType = k.InterpolationType
                }).ToList()),
            ShapeKeyWeights = source.ShapeKeyWeights.Select(weight => new ProjectShapeKeyWeightEntry
            {
                MeshIndex = weight.MeshIndex,
                Name = weight.Name,
                Weight = weight.Weight
            }).ToList()
        };

        foreach (var child in source.Children)
            clone.Children.Add(CloneLibraryEntryRecursive(child));

        return clone;
    }

    private bool RenderObjectLibraryTreeNode(
        ProjectSceneObjectEntry entry,
        IReadOnlyDictionary<string, int> libraryUsageCounts)
    {
        if (!ShouldShowLibraryEntry(entry))
            return false;

        bool isSelected = string.Equals(entry.LibraryEntryId, _selectedLibraryEntryId, StringComparison.OrdinalIgnoreCase);
        string displayLabel = GetLibraryDisplayLabel(entry);
        int usageCount = GetLibraryUsageCount(libraryUsageCounts, entry.LibraryEntryId);
        string label = Localization.Format("text.panel.properties.in_scene", displayLabel, usageCount) + $"###{displayLabel} ({usageCount} in scene)##{entry.LibraryEntryId}";
        bool hasChildren = entry.Children.Count > 0;

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen;
        if (isSelected)
            flags |= ImGuiTreeNodeFlags.Selected;
        if (!hasChildren)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

        bool open = ImGui.TreeNodeEx(label, flags);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            _selectedLibraryEntryId = entry.LibraryEntryId;

        if (hasChildren && open)
        {
            foreach (var child in entry.Children)
                RenderObjectLibraryTreeNode(child, libraryUsageCounts);
            ImGui.TreePop();
        }

        return true;
    }

    private bool ShouldShowLibraryEntry(ProjectSceneObjectEntry entry)
    {
        if (string.IsNullOrWhiteSpace(_librarySearch))
            return true;

        string type = string.IsNullOrWhiteSpace(entry.ObjectType) ? "Object" : entry.ObjectType;
        string name = string.IsNullOrWhiteSpace(entry.Name) ? type : entry.Name;
        if (name.IndexOf(_librarySearch, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (type.IndexOf(_librarySearch, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        foreach (var child in entry.Children)
            if (ShouldShowLibraryEntry(child))
                return true;

        return false;
    }

    private static List<ProjectSceneObjectEntry> BuildLibraryTreeRoots(IReadOnlyList<ProjectSceneObjectEntry> library)
    {
        var roots = new List<ProjectSceneObjectEntry>();
        var referencedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in library)
            CollectReferencedLibraryIds(entry, referencedIds);

        foreach (var entry in library)
        {
            if (string.IsNullOrWhiteSpace(entry.LibraryEntryId) || !referencedIds.Contains(entry.LibraryEntryId))
                roots.Add(entry);
        }

        return roots;
    }

    private static void CollectReferencedLibraryIds(ProjectSceneObjectEntry node, HashSet<string> referencedIds)
    {
        foreach (var child in node.Children)
        {
            if (!string.IsNullOrWhiteSpace(child.LibraryEntryId))
                referencedIds.Add(child.LibraryEntryId);
            CollectReferencedLibraryIds(child, referencedIds);
        }
    }

    private static string GetNextLibrarySelectionIdAfterDeletion(IReadOnlyList<ProjectSceneObjectEntry> library, string deletedLibraryEntryId)
    {
        if (string.IsNullOrWhiteSpace(deletedLibraryEntryId))
            return "";

        var target = FindLibraryEntryById(library, deletedLibraryEntryId);
        if (target == null)
            return "";

        var removedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectLibrarySubtreeIds(target, removedIds);

        var orderedIds = new List<string>();
        foreach (var root in BuildLibraryTreeRoots(library).OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase))
            AppendLibraryDisplayOrderIds(root, orderedIds);

        int deletedIndex = orderedIds.FindIndex(id => string.Equals(id, deletedLibraryEntryId, StringComparison.OrdinalIgnoreCase));
        if (deletedIndex < 0)
            return "";

        for (int i = deletedIndex + 1; i < orderedIds.Count; i++)
        {
            if (!removedIds.Contains(orderedIds[i]))
                return orderedIds[i];
        }

        for (int i = deletedIndex - 1; i >= 0; i--)
        {
            if (!removedIds.Contains(orderedIds[i]))
                return orderedIds[i];
        }

        return "";
    }

    private static void CollectLibrarySubtreeIds(ProjectSceneObjectEntry node, HashSet<string> ids)
    {
        if (!string.IsNullOrWhiteSpace(node.LibraryEntryId))
            ids.Add(node.LibraryEntryId);

        foreach (var child in node.Children)
            CollectLibrarySubtreeIds(child, ids);
    }

    private static void AppendLibraryDisplayOrderIds(ProjectSceneObjectEntry node, List<string> orderedIds)
    {
        if (!string.IsNullOrWhiteSpace(node.LibraryEntryId))
            orderedIds.Add(node.LibraryEntryId);

        foreach (var child in node.Children)
            AppendLibraryDisplayOrderIds(child, orderedIds);
    }

    private static string GetLibraryDisplayLabel(ProjectSceneObjectEntry entry)
    {
        string type = string.IsNullOrWhiteSpace(entry.ObjectType) ? "Object" : entry.ObjectType;
        string name = string.IsNullOrWhiteSpace(entry.Name) ? type : entry.Name;
        return $"{name} [{type}]";
    }

    private void EnsureObjectLibraryInitialized(ProjectManifest manifest)
    {
        manifest.ObjectLibrary ??= new List<ProjectSceneObjectEntry>();

        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.ObjectLibrary)
            EnsureUniqueLibraryIdsRecursive(entry, usedIds);

        if (Viewport == null)
            return;

        foreach (var root in Viewport.SceneObjects)
        {
            EnsureSceneLibrarySourceIdsRecursive(root);

            if (usedIds.Contains(root.LibrarySourceId))
                continue;

            var libraryEntry = ProjectSceneSerializer.SerializeObjectForLibrary(root);
            libraryEntry.LibraryEntryId = root.LibrarySourceId;
            libraryEntry.LibrarySourceId = root.LibrarySourceId;
            EnsureUniqueLibraryIdsRecursive(libraryEntry, usedIds);
            if (string.IsNullOrWhiteSpace(libraryEntry.Name))
                libraryEntry.Name = string.IsNullOrWhiteSpace(libraryEntry.ObjectType) ? "Object" : libraryEntry.ObjectType;
            manifest.ObjectLibrary.Add(libraryEntry);
        }
    }

    private static void EnsureUniqueLibraryIdsRecursive(ProjectSceneObjectEntry entry, HashSet<string> usedIds)
    {
        string id = entry.LibraryEntryId;
        if (string.IsNullOrWhiteSpace(id) || usedIds.Contains(id))
            id = Guid.NewGuid().ToString("N");

        entry.LibraryEntryId = id;
        entry.LibrarySourceId = id;
        if (string.IsNullOrWhiteSpace(entry.Name))
            entry.Name = string.IsNullOrWhiteSpace(entry.ObjectType) ? "Object" : entry.ObjectType;

        usedIds.Add(id);

        foreach (var child in entry.Children)
            EnsureUniqueLibraryIdsRecursive(child, usedIds);
    }

    private Dictionary<string, int> BuildLibraryUsageCounts()
    {
        var usageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (Viewport != null)
        {
            for (int i = 0; i < Viewport.SceneObjects.Count; i++)
                AccumulateLibraryUsageCounts(Viewport.SceneObjects[i], usageCounts);
        }

        return usageCounts;
    }

    private static void AccumulateLibraryUsageCounts(SceneObject obj, Dictionary<string, int> usageCounts)
    {
        if (!string.IsNullOrWhiteSpace(obj.LibrarySourceId))
        {
            usageCounts.TryGetValue(obj.LibrarySourceId, out int usageCount);
            usageCounts[obj.LibrarySourceId] = usageCount + 1;
        }

        for (int i = 0; i < obj.Children.Count; i++)
            AccumulateLibraryUsageCounts(obj.Children[i], usageCounts);
    }

    private static int GetLibraryUsageCount(
        IReadOnlyDictionary<string, int> libraryUsageCounts,
        string libraryEntryId)
    {
        return string.IsNullOrWhiteSpace(libraryEntryId) ||
               !libraryUsageCounts.TryGetValue(libraryEntryId, out int usageCount)
            ? 0
            : usageCount;
    }

    private static ProjectSceneObjectEntry? FindLibraryEntryById(IEnumerable<ProjectSceneObjectEntry> nodes, string libraryEntryId)
    {
        if (string.IsNullOrWhiteSpace(libraryEntryId))
            return null;

        foreach (var entry in nodes)
        {
            if (string.Equals(entry.LibraryEntryId, libraryEntryId, StringComparison.OrdinalIgnoreCase))
                return entry;

            var nested = FindLibraryEntryById(entry.Children, libraryEntryId);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static void CollectParticleSourceEntries(IEnumerable<ProjectSceneObjectEntry> nodes, List<ProjectSceneObjectEntry> output)
    {
        foreach (var node in nodes)
        {
            if (!string.Equals(node.SpawnCategory, "Particle Spawners", StringComparison.OrdinalIgnoreCase))
                output.Add(node);

            CollectParticleSourceEntries(node.Children, output);
        }
    }

    private static void RemoveLibraryEntry(ProjectManifest manifest, string libraryEntryId)
    {
        if (string.IsNullOrWhiteSpace(libraryEntryId))
            return;

        manifest.ObjectLibrary.RemoveAll(entry =>
            string.Equals(entry.LibraryEntryId, libraryEntryId, StringComparison.OrdinalIgnoreCase));

        foreach (var entry in manifest.ObjectLibrary)
            RemoveLibraryEntryRecursive(entry, libraryEntryId);
    }

    private static void RemoveLibraryEntryRecursive(ProjectSceneObjectEntry node, string libraryEntryId)
    {
        node.Children.RemoveAll(child =>
            string.Equals(child.LibraryEntryId, libraryEntryId, StringComparison.OrdinalIgnoreCase));

        foreach (var child in node.Children)
            RemoveLibraryEntryRecursive(child, libraryEntryId);
    }

    private static void CollectLibraryEntryIds(IEnumerable<ProjectSceneObjectEntry> nodes, HashSet<string> ids)
    {
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.LibraryEntryId))
                ids.Add(node.LibraryEntryId);
            CollectLibraryEntryIds(node.Children, ids);
        }
    }

    private static void EnsureSceneLibrarySourceIdsRecursive(SceneObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.LibrarySourceId))
            obj.LibrarySourceId = string.IsNullOrWhiteSpace(obj.ObjectId) ? Guid.NewGuid().ToString("N") : obj.ObjectId;

        for (int i = 0; i < obj.Children.Count; i++)
            EnsureSceneLibrarySourceIdsRecursive(obj.Children[i]);
    }

    private void CaptureCopyConstraintOffset(SceneObject owner, CopyConstraint constraint, string prefix)
    {
        SceneObject? FindTarget(SceneObject obj)
        {
            if (obj == owner) return null;
            if (obj.ObjectId == constraint.TargetObjectId) return obj;
            foreach (var child in obj.Children)
                if (FindTarget(child) is { } found) return found;
            return null;
        }
        if (Viewport == null || string.IsNullOrEmpty(constraint.TargetObjectId)) return;
        foreach (var root in Viewport.SceneObjects)
            if (FindTarget(root) is { } target)
            {
                CopyConstraintSolver.CaptureOffset(owner, constraint, target);
                foreach (string axis in new[] { "x", "y", "z" })
                    Timeline?.RecordAutoKeyframe(owner, $"{prefix}.offset.{axis}");
                break;
            }
    }

    private void DrawConstraints(SceneObject owner)
    {
        if (!ImGui.CollapsingHeader(Localization.DisplayLabel("Constraints"))) return;
        ImGui.PushID("constraints");
        float buttonSize = ImGui.GetFrameHeight();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttonSize));
        if (ImGui.Button("+##add", new Vector2(buttonSize))) ImGui.OpenPopup("##addMenu");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.DisplayText("Add Constraint"));
        if (ImGui.BeginPopup("##addMenu"))
        {
            foreach (var type in Enum.GetValues<CopyConstraintType>())
                if (ImGui.MenuItem(Localization.DisplayLabel(TimelinePanel.GetCopyConstraintLabel(type))))
                {
                    owner.CopyConstraints.Add(new CopyConstraint
                    {
                        Type = type, Offset = type == CopyConstraintType.Scale ? vec3.Ones : vec3.Zero
                    });
                    ProjectManager.Instance.SetDirty(true);
                }
            if (owner is BoneSceneObject bone && ImGui.MenuItem(Localization.DisplayLabel("Inverse Kinematics")))
            {
                bone.Constraints.Add(new BoneConstraint());
                ProjectManager.Instance.SetDirty(true);
            }
            ImGui.EndPopup();
        }

        var objects = new List<SceneObject>();
        void Collect(SceneObject obj)
        {
            // A target cannot depend on the constrained object's evaluated transform.
            if (obj == owner) return;
            objects.Add(obj);
            foreach (var child in obj.Children) Collect(child);
        }
        if (Viewport != null)
            foreach (var root in Viewport.SceneObjects) Collect(root);

        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f * UiTheme.Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f) * UiTheme.Scale);
        bool visible = ImGui.BeginChild("##list", Vector2.Zero,
            ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AlwaysUseWindowPadding);
        int removeIndex = -1;
        if (visible && owner.CopyConstraints.Count == 0 &&
            (owner is not BoneSceneObject emptyBone || emptyBone.Constraints.Count == 0))
            UiText.Disabled(Localization.DisplayText("No constraints added."));
        if (visible)
            for (int i = 0; i < owner.CopyConstraints.Count; i++)
            {
                var constraint = owner.CopyConstraints[i];
                string prefix = $"copy_constraint.{i}";
                ImGui.PushID(i);
                if (i > 0) ImGui.Separator();
                if (ImGui.TreeNodeEx(Localization.DisplayLabel(TimelinePanel.GetCopyConstraintLabel(constraint.Type)), ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton(Localization.DisplayLabel("Remove"))) removeIndex = i;
                    void Changed(string channel, bool recapture = false)
                    {
                        // Settings are assigned first; the evaluated owner pose is unchanged until the next solver pass.
                        if (recapture) CaptureCopyConstraintOffset(owner, constraint, prefix);
                        Timeline?.RecordAutoKeyframe(owner, $"{prefix}.{channel}");
                        ProjectManager.Instance.SetDirty(true);
                    }
                    bool enabled = constraint.Enabled;
                    if (ImGui.Checkbox(Localization.DisplayLabel("Enabled"), ref enabled))
                    {
                        constraint.Enabled = enabled;
                        Changed("enabled", enabled);
                    }
                    RegisterPropertyContext($"{prefix}.enabled");
                    var target = objects.FirstOrDefault(obj => obj.ObjectId == constraint.TargetObjectId);
                    string preview = string.IsNullOrEmpty(constraint.TargetObjectId) ? Localization.T("text.common.none")
                        : target?.Name ?? Localization.DisplayText("Missing object");
                    if (ImGui.BeginCombo(Localization.DisplayLabel("Target Object"), preview))
                    {
                        if (ImGui.Selectable(Localization.Label("text.common.none", "None"), string.IsNullOrEmpty(constraint.TargetObjectId)))
                        {
                            constraint.TargetObjectId = "";
                            Changed("target");
                        }
                        foreach (var candidate in objects)
                            if (ImGui.Selectable($"{candidate.Name}##{candidate.ObjectId}", candidate.ObjectId == constraint.TargetObjectId))
                            {
                                constraint.TargetObjectId = candidate.ObjectId;
                                Changed("target", true);
                            }
                        ImGui.EndCombo();
                    }
                    RegisterPropertyContext($"{prefix}.target");
                    target = objects.FirstOrDefault(obj => obj.ObjectId == constraint.TargetObjectId);
                    if (constraint.Type != CopyConstraintType.Scale && target is MiBoneSceneObject)
                    {
                        bool bentHalf = constraint.BentHalf;
                        if (ImGui.Checkbox(Localization.DisplayLabel("Bent Half"), ref bentHalf))
                        {
                            constraint.BentHalf = bentHalf;
                            Changed("bent_half", true);
                        }
                        RegisterPropertyContext($"{prefix}.bent_half");
                    }
                    if (constraint.Type == CopyConstraintType.Position)
                    {
                        bool rotate = constraint.Rotate;
                        if (ImGui.Checkbox(Localization.DisplayLabel("Rotate"), ref rotate))
                        {
                            constraint.Rotate = rotate;
                            Changed("rotate", true);
                        }
                        RegisterPropertyContext($"{prefix}.rotate");
                    }
                    float factor = constraint.Type == CopyConstraintType.Position ? 16f
                        : constraint.Type == CopyConstraintType.Rotation ? 180f / MathF.PI : 1f;
                    var offset = new Vector3(constraint.Offset.x, constraint.Offset.y, constraint.Offset.z) * factor;
                    string offsetLabel = constraint.Type == CopyConstraintType.Position ? "Offset (pixels)"
                        : constraint.Type == CopyConstraintType.Rotation ? "Offset (degrees)" : "Offset";
                    if (ImGui.DragFloat3(Localization.DisplayLabel(offsetLabel), ref offset, 0.1f))
                    {
                        constraint.Offset = new vec3(offset.X, offset.Y, offset.Z) / factor;
                        foreach (string axis in new[] { "x", "y", "z" }) Changed($"offset.{axis}");
                    }
                    RegisterPropertyContext($"{prefix}.offset.x", $"{prefix}.offset.y", $"{prefix}.offset.z");
                    for (int axis = 0; axis < 3; axis++)
                    {
                        string component = axis == 0 ? "x" : axis == 1 ? "y" : "z";
                        bool active = axis == 0 ? constraint.AxisX : axis == 1 ? constraint.AxisY : constraint.AxisZ;
                        if (axis > 0) ImGui.SameLine();
                        if (ImGui.Checkbox(Localization.DisplayLabel(axis == 0 ? "Axis X" : axis == 1 ? "Axis Y" : "Axis Z"), ref active))
                        {
                            if (axis == 0) constraint.AxisX = active;
                            else if (axis == 1) constraint.AxisY = active;
                            else constraint.AxisZ = active;
                            Changed($"axis.{component}");
                        }
                        RegisterPropertyContext($"{prefix}.axis.{component}");
                    }
                    float blend = constraint.Blend;
                    if (ImGui.SliderFloat(Localization.DisplayLabel("Blend"), ref blend, 0f, 1f))
                    {
                        constraint.Blend = blend;
                        Changed("blend");
                    }
                    RegisterPropertyContext($"{prefix}.blend");
                    if (string.IsNullOrEmpty(constraint.TargetObjectId))
                        UiText.Disabled(Localization.DisplayText("Select a target object to activate this constraint."));
                    else if (target == null)
                        UiText.Wrapped(Localization.DisplayText("Target is missing or is this object or one of its descendants."));
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
        if (removeIndex >= 0)
        {
            owner.CopyConstraints.RemoveAt(removeIndex);
            var shiftedTracks = owner.Keyframes.Where(pair => pair.Key.StartsWith("copy_constraint.", StringComparison.Ordinal))
                .Select(pair => (pair.Key, pair.Value, Parts: pair.Key.Split('.')))
                .Where(track => track.Parts.Length >= 3 && int.TryParse(track.Parts[1], out int index) && index >= removeIndex).ToArray();
            foreach (var track in shiftedTracks) owner.Keyframes.Remove(track.Key);
            foreach (var track in shiftedTracks)
            {
                int index = int.Parse(track.Parts[1], CultureInfo.InvariantCulture);
                if (index > removeIndex)
                    owner.Keyframes[$"copy_constraint.{index - 1}.{string.Join('.', track.Parts.Skip(2))}"] = track.Value;
            }
            Timeline?.RefreshFromScene();
            ProjectManager.Instance.SetDirty(true);
        }
        if (visible && owner is BoneSceneObject constraintBone && constraintBone.Constraints.Count > 0)
        {
            if (owner.CopyConstraints.Count > 0) ImGui.Separator();
            DrawBoneConstraints(constraintBone);
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopID();
    }

    private void DrawBoneConstraints(BoneSceneObject bone)
    {
        ImGui.PushID("boneConstraints");
        int removeIndex = -1;
        {
            var objects = new List<SceneObject>();
            void Collect(SceneObject obj)
            {
                objects.Add(obj);
                foreach (var child in obj.Children) Collect(child);
            }
            if (Viewport != null)
                foreach (var root in Viewport.SceneObjects) Collect(root);

            for (int i = 0; i < bone.Constraints.Count; i++)
            {
                var constraint = bone.Constraints[i];
                ImGui.PushID(i);
                if (i > 0) ImGui.Separator();
                if (ImGui.TreeNodeEx(Localization.DisplayLabel("Inverse Kinematics##constraint"), ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton(Localization.DisplayLabel("Remove"))) removeIndex = i;
                    bool enabled = constraint.Enabled;
                    bool changed = ImGui.Checkbox(Localization.DisplayLabel("Enabled"), ref enabled);
                    constraint.Enabled = enabled;
                    if (changed) Timeline?.RecordAutoKeyframe(bone, $"constraint.{i}.ik.enabled");
                    RegisterPropertyContext($"constraint.{i}.ik.enabled");
                    var ik = constraint.InverseKinematics;
                    var chain = new List<BoneSceneObject>();
                    for (SceneObject? current = bone; current is BoneSceneObject chainBone && chain.Count < Math.Clamp(ik.ChainLength, 1, 256); current = current.Parent)
                        chain.Add(chainBone);

                    bool IsDrivenObject(SceneObject candidate)
                    {
                        for (SceneObject? current = candidate; current != null; current = current.Parent)
                            if (current is BoneSceneObject currentBone && chain.Contains(currentBone)) return true;
                        return false;
                    }

                    bool ObjectSelector(string label, ref string objectId)
                    {
                        string selectedId = objectId;
                        string preview = string.IsNullOrEmpty(selectedId) ? Localization.T("text.common.none") :
                            objects.FirstOrDefault(obj => obj.ObjectId == selectedId)?.Name ?? Localization.DisplayText("Missing object");
                        bool selectionChanged = false;
                        if (ImGui.BeginCombo(Localization.DisplayLabel(label), preview))
                        {
                            if (ImGui.Selectable(Localization.Label("text.common.none", "None"), string.IsNullOrEmpty(selectedId)))
                            {
                                objectId = "";
                                selectionChanged = true;
                            }
                            foreach (var candidate in objects)
                            {
                                if (IsDrivenObject(candidate)) continue;
                                if (ImGui.Selectable($"{candidate.Name}##{candidate.ObjectId}", candidate.ObjectId == selectedId))
                                {
                                    objectId = candidate.ObjectId;
                                    selectionChanged = true;
                                }
                            }
                            ImGui.EndCombo();
                        }
                        return selectionChanged;
                    }

                    string targetId = ik.TargetObjectId;
                    string poleId = ik.PoleObjectId;
                    if (ObjectSelector("Target Object", ref targetId))
                    {
                        ik.TargetObjectId = targetId;
                        Timeline?.RecordAutoKeyframe(bone, $"constraint.{i}.ik.target");
                        changed = true;
                    }
                    RegisterPropertyContext($"constraint.{i}.ik.target");
                    if (ObjectSelector("Pole Object", ref poleId))
                    {
                        ik.PoleObjectId = poleId;
                        Timeline?.RecordAutoKeyframe(bone, $"constraint.{i}.ik.pole");
                        changed = true;
                    }
                    RegisterPropertyContext($"constraint.{i}.ik.pole");
                    bool lockOrientation = ik.LockOrientation;
                    if (ImGui.Checkbox(Localization.DisplayLabel("Lock Orientation"), ref lockOrientation))
                    {
                        ik.LockOrientation = lockOrientation;
                        Timeline?.RecordAutoKeyframe(bone, $"constraint.{i}.ik.lock_orientation");
                        changed = true;
                    }
                    RegisterPropertyContext($"constraint.{i}.ik.lock_orientation");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(Localization.DisplayText("Hold this bone's animated world orientation while the chain moves it. Its origin stays attached to its parent and Bend remains independent."));
                    int chainLength = ik.ChainLength;
                    if (ImGui.InputInt(Localization.DisplayLabel("Chain Length"), ref chainLength))
                    {
                        ik.ChainLength = Math.Clamp(chainLength, 1, 256);
                        Timeline?.RecordAutoKeyframe(bone, $"constraint.{i}.ik.chain_length");
                        changed = true;
                    }
                    RegisterPropertyContext($"constraint.{i}.ik.chain_length");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(Localization.DisplayText("Number of bones including this bone, walking up through bone parents. Bendable model parts include their internal joint."));
                    float influence = ik.Influence;
                    if (ImGui.SliderFloat(Localization.DisplayLabel("Influence"), ref influence, 0f, 1f))
                    {
                        ik.Influence = influence;
                        Timeline?.RecordAutoKeyframe(bone, $"constraint.{i}.ik.influence");
                        changed = true;
                    }
                    RegisterPropertyContext($"constraint.{i}.ik.influence");
                    float poleAngle = ik.PoleAngle;
                    if (ImGui.DragFloat(Localization.DisplayLabel("Pole Angle"), ref poleAngle, 0.5f, -360f, 360f, Localization.T("text.panel.properties.ik_angle_format")))
                    {
                        ik.PoleAngle = poleAngle;
                        Timeline?.RecordAutoKeyframe(bone, $"constraint.{i}.ik.pole_angle");
                        changed = true;
                    }
                    RegisterPropertyContext($"constraint.{i}.ik.pole_angle");
                    int iterations = ik.Iterations;
                    if (ImGui.InputInt(Localization.DisplayLabel("Iterations"), ref iterations))
                    {
                        ik.Iterations = Math.Clamp(iterations, 1, 256);
                        Timeline?.RecordAutoKeyframe(bone, $"constraint.{i}.ik.iterations");
                        changed = true;
                    }
                    RegisterPropertyContext($"constraint.{i}.ik.iterations");
                    UiText.Disabled(Localization.DisplayText("Chain Participation"));
                    for (int depth = 0; depth < chain.Count; depth++)
                    {
                        var chainBone = chain[depth];
                        ImGui.PushID(chainBone.ObjectId);
                        UiText.Wrapped(chainBone.Name);
                        var participation = ik.Bones.FirstOrDefault(entry => entry.BoneObjectId == chainBone.ObjectId);
                        bool rotate = participation?.AllowRotation ?? true;
                        bool bend = participation?.AllowBend ?? true;
                        bool rotationChanged = ImGui.Checkbox(Localization.DisplayLabel("Rotate"), ref rotate);
                        RegisterPropertyContext($"constraint.{i}.ik.bone.{depth}.rotate");
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip(Localization.DisplayText("Allow the IK solver to rotate this bone locally. Turning this off still follows parent rotation; use Lock Orientation to hold the constrained bone's world orientation."));
                        bool bendChanged = false;
                        if (chainBone is MiBoneSceneObject miBone && miBone.BendParameters.HasValue)
                        {
                            ImGui.SameLine();
                            bendChanged = ImGui.Checkbox(Localization.DisplayLabel("Bend"), ref bend);
                            RegisterPropertyContext($"constraint.{i}.ik.bone.{depth}.bend");
                        }
                        if (rotationChanged || bendChanged)
                        {
                            if (participation == null)
                            {
                                participation = new IkBoneSettings { BoneObjectId = chainBone.ObjectId };
                                ik.Bones.Add(participation);
                            }
                            participation.AllowRotation = rotate;
                            participation.AllowBend = bend;
                            if (rotationChanged) Timeline?.RecordAutoKeyframe(bone, $"constraint.{i}.ik.bone.{depth}.rotate");
                            if (bendChanged) Timeline?.RecordAutoKeyframe(bone, $"constraint.{i}.ik.bone.{depth}.bend");
                            changed = true;
                        }
                        ImGui.PopID();
                    }
                    if (string.IsNullOrEmpty(ik.TargetObjectId))
                        UiText.Disabled(Localization.DisplayText("Select a target object to activate IK."));
                    else if (objects.FirstOrDefault(obj => obj.ObjectId == ik.TargetObjectId) is not { } target || IsDrivenObject(target))
                        UiText.Wrapped(Localization.DisplayText("Target is missing or belongs to the driven chain."));
                    if (changed) ProjectManager.Instance.SetDirty(true);
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
            if (bone.Constraints.Count > 0)
            {
                ImGui.Separator();
                bool customEnd = bone.EndOffset.HasValue;
                if (ImGui.Checkbox(Localization.DisplayLabel("Custom Bone End"), ref customEnd))
                {
                    bone.EndOffset = customEnd ? new vec3(0f, 1f, 0f) : null;
                    Timeline?.RecordAutoKeyframe(bone, "bone.end_enabled");
                    ProjectManager.Instance.SetDirty(true);
                }
                RegisterPropertyContext("bone.end_enabled");
                if (bone.EndOffset is vec3 end)
                {
                    var value = new Vector3(end.x, end.y, end.z) * 16f;
                    if (ImGui.DragFloat3(Localization.DisplayLabel("End Offset"), ref value, 0.1f))
                    {
                        bone.EndOffset = new vec3(value.X, value.Y, value.Z) / 16f;
                        foreach (string axis in new[] { "x", "y", "z" })
                            Timeline?.RecordAutoKeyframe(bone, $"bone.end.{axis}");
                        ProjectManager.Instance.SetDirty(true);
                    }
                    RegisterPropertyContext("bone.end.x", "bone.end.y", "bone.end.z");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Localization.DisplayText("Bone-local endpoint in model pixels (16 per world unit). Imported end_offset is mapped here. Disable to target this bone's origin."));
            }
        }
        if (removeIndex >= 0)
        {
            bone.Constraints.RemoveAt(removeIndex);
            var shiftedTracks = bone.Keyframes.Where(pair => pair.Key.StartsWith("constraint.", StringComparison.Ordinal))
                .Select(pair => (pair.Key, pair.Value, Parts: pair.Key.Split('.')))
                .Where(track => track.Parts.Length >= 3 && int.TryParse(track.Parts[1], out int index) && index >= removeIndex)
                .ToArray();
            foreach (var track in shiftedTracks)
                bone.Keyframes.Remove(track.Key);
            foreach (var track in shiftedTracks)
            {
                int index = int.Parse(track.Parts[1], CultureInfo.InvariantCulture);
                if (index > removeIndex)
                    bone.Keyframes[$"constraint.{index - 1}.{string.Join('.', track.Parts.Skip(2))}"] = track.Value;
            }
            Timeline?.RefreshFromScene();
            ProjectManager.Instance.SetDirty(true);
        }
        ImGui.PopID();
    }

    private void RemoveSceneObjectsFromLibrary(string libraryEntryId)
    {
        if (Viewport == null || string.IsNullOrWhiteSpace(libraryEntryId))
            return;

        foreach (var root in Viewport.SceneObjects.ToList())
        {
            if (string.Equals(root.LibrarySourceId, libraryEntryId, StringComparison.OrdinalIgnoreCase))
            {
                DeleteSceneObjectRecursive(root);
                continue;
            }

            RemoveSceneObjectsFromLibraryRecursive(root, libraryEntryId);
        }
    }

    private void RemoveSceneObjectsFromLibraryRecursive(SceneObject parent, string libraryEntryId)
    {
        foreach (var child in parent.Children.ToList())
        {
            if (string.Equals(child.LibrarySourceId, libraryEntryId, StringComparison.OrdinalIgnoreCase))
            {
                DeleteSceneObjectRecursive(child);
                continue;
            }

            RemoveSceneObjectsFromLibraryRecursive(child, libraryEntryId);
        }
    }

    private void DeleteSceneObjectRecursive(SceneObject obj)
    {
        foreach (var child in obj.Children.ToList())
            DeleteSceneObjectRecursive(child);

        SelectionManager.Instance?.DeselectObject(obj);
        if (obj.Parent != null)
            obj.Parent.RemoveChild(obj);
        else
            Viewport?.SceneObjects.Remove(obj);
    }

    private static string EnsureUniqueLibraryName(IReadOnlyCollection<ProjectSceneObjectEntry> library, string desiredName)
    {
        string baseName = string.IsNullOrWhiteSpace(desiredName) ? "Object Copy" : desiredName.Trim();
        var used = new HashSet<string>(
            library.Select(entry => string.IsNullOrWhiteSpace(entry.Name) ? "" : entry.Name.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (!used.Contains(baseName))
            return baseName;

        int suffix = 2;
        while (used.Contains($"{baseName} {suffix}"))
            suffix++;

        return $"{baseName} {suffix}";
    }

    private static string GetBackgroundImageLabel(string backgroundImagePath)
    {
        return string.IsNullOrWhiteSpace(backgroundImagePath) ||
               string.Equals(backgroundImagePath, NoImageSelected, StringComparison.OrdinalIgnoreCase)
            ? NoImageSelected
            : Path.GetFileName(backgroundImagePath);
    }

    private bool ImportBackgroundImageFromDialog()
    {
        if (!ProjectManager.Instance.HasProject)
            return false;

        var result = Dialog.FileOpen("png,jpg,jpeg,bmp,tga,gif,webp,tiff");
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return false;

        ProjectAssetEntry entry = ProjectManager.Instance.AddAsset(result.Path, ProjectAssetType.Image);
        string importedPath = entry.StoredInProject && !string.IsNullOrWhiteSpace(entry.RelativePath)
            ? entry.RelativePath
            : ProjectManager.Instance.ToProjectRelativePath(entry.SourcePath);

        BackgroundImagePath = string.IsNullOrWhiteSpace(importedPath) ? NoImageSelected : importedPath;
        return true;
    }

    private string ResolveAlbedoTexturePathForProject(string sourcePath)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!ProjectManager.Instance.HasProject)
            return fullSourcePath;

        try
        {
            var existing = ProjectManager.Instance.GetProjectAssets().FirstOrDefault(a =>
                a.AssetType == ProjectAssetType.Image &&
                ProjectManager.Instance.AssetMatchesPath(a, fullSourcePath));

            var asset = existing ?? ProjectManager.Instance.AddAsset(fullSourcePath, ProjectAssetType.Image);
            return ProjectManager.Instance.GetAssetFullPath(asset);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not register albedo texture in project assets: {ex.Message}");
            return fullSourcePath;
        }
    }
    
    // ── Selection callback ────────────────────────────────────────────────────

    private void OnSelectionChanged()
    {
        _nameEditor.Commit();
        _pbrImportError = "";
        var sel = SelectionManager.Instance?.SelectedObjects;
        _currentObject = (sel != null && sel.Count > 0) ? sel[0] : null;
        
        // If object has a stored albedo texture path but hasn't loaded the texture yet, load it now
        if (_currentObject != null && 
            !string.IsNullOrEmpty(_currentObject.AlbedoTexturePath) &&
            SupportsAlbedoTexture(_currentObject))
        {
            // Check if any mesh already has the texture loaded
            bool hasTexture = _currentObject.Visuals.Any(mesh => mesh.TextureId != 0);

            // If not loaded, load it from the stored path
            if (!hasTexture)
            {
                string fullPath = ProjectManager.Instance.ResolveAssetPath(_currentObject.AlbedoTexturePath);
                if (File.Exists(fullPath))
                {
                    OnLoadAlbedoTextureForObject(_currentObject, fullPath);
                }
            }
        }
    }

    /// <summary>
    /// Loads pending albedo textures for all objects that have a stored path but
    /// no texture loaded yet. Called after a project scene is loaded.
    /// </summary>
    public void LoadPendingAlbedoTextures(IEnumerable<SceneObject> sceneObjects)
    {
        if (Gl == null) return;

        foreach (var root in sceneObjects)
            LoadPendingAlbedoTexturesRecursive(root);
    }

    private void LoadPendingAlbedoTexturesRecursive(SceneObject obj)
    {
        if (obj == null) return;

        if (SupportsAlbedoTexture(obj) &&
            !string.IsNullOrEmpty(obj.AlbedoTexturePath))
        {
            bool hasTexture = false;
            foreach (var mesh in obj.Visuals)
            {
                if (mesh.TextureId != 0)
                {
                    hasTexture = true;
                    break;
                }
            }

            if (!hasTexture)
            {
                string fullPath = ProjectManager.Instance.ResolveAssetPath(obj.AlbedoTexturePath);
                if (File.Exists(fullPath))
                    OnLoadAlbedoTextureForObject(obj, fullPath);
            }
        }

        foreach (var child in obj.Children)
            LoadPendingAlbedoTexturesRecursive(child);
    }
    
    public override void Render()
    {
        ApplyBackgroundAnimation(Timeline?.CurrentFrame ?? 0);
        if (ImGui.Begin(Localization.Label("text.common.properties", "Properties")))
        {
            if (ImGui.BeginTabBar("PropertiesTabs"))
            {
                RenderProjectTab();
                RenderObjectTab();
                ImGui.EndTabBar();
            }
            
            // Deferred context menu popup
            if (_openPropContextMenu)
            {
                _openPropContextMenu = false;
                _propertyContextObject = _currentObject;
                if (_propertyContextPaths?.FirstOrDefault() != _ctxPropertyPath)
                    _propertyContextPaths = null;
                if (_propertyContextBinding?.Path != _ctxPropertyPath)
                    _propertyContextBinding = null;
                ImGui.OpenPopup("##prop_keyframe_ctx");
            }
            RenderPropertyContextMenu();
        }
        _nameEditor.CommitIfHidden();
        ImGui.End();
    }
    
    private void RenderProjectTab()
    {
        if (!ImGui.BeginTabItem(Localization.Label("text.panel.properties.project", "Project"))) return;

        ImGui.TextUnformatted(Localization.T("text.panel.properties.project_properties"));
        ImGui.Separator();

        if (ImGui.CollapsingHeader(Localization.Label("text.panel.properties.project_settings", "Project Settings"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            var projectManager = ProjectManager.Instance;

            ImGui.TextUnformatted(Localization.T("text.panel.properties.project_name"));
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##ProjectName", ref _projectName, 256))
            {
                WriteProjectSettingsToManifest(projectManager.Manifest);
                projectManager.SetDirty(true);
            }

            ImGui.BeginDisabled(!projectManager.HasProject);
            if (ImGui.Button(Localization.Label("text.panel.properties.open_project_folder", "Open Project Folder")))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = projectManager.ProjectFolder,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Could not open project folder: {ex.Message}");
                }
            }
            ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.TextUnformatted(Localization.T("text.panel.properties.resolution"));
            ImGui.SetNextItemWidth(80);
            bool resolutionChanged = InputIntEditor("##ResWidth", ref _resolutionWidth, 1, 10, 1, int.MaxValue);
            ImGui.SameLine();
            ImGui.TextUnformatted(" x ");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            resolutionChanged |= InputIntEditor("##ResHeight", ref _resolutionHeight, 1, 10, 1, int.MaxValue);
            ImGui.TextUnformatted(Localization.T("text.panel.properties.presets"));
            if (ImGui.Button("720p"))  { _resolutionWidth = 1280; _resolutionHeight = 720; resolutionChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("1080p")) { _resolutionWidth = 1920; _resolutionHeight = 1080; resolutionChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("1440p")) { _resolutionWidth = 2560; _resolutionHeight = 1440; resolutionChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("4K"))    { _resolutionWidth = 3840; _resolutionHeight = 2160; resolutionChanged = true; }
            if (resolutionChanged)
            {
                _resolutionWidth = Math.Max(1, _resolutionWidth);
                _resolutionHeight = Math.Max(1, _resolutionHeight);
                WriteProjectSettingsToManifest(projectManager.Manifest);
                projectManager.SetDirty(true);
            }

            ImGui.Spacing();
            ImGui.TextUnformatted(Localization.T("text.panel.properties.framerate"));
            ImGui.SetNextItemWidth(80);
            bool frameRateChanged = InputIntEditor("##Framerate", ref _framerate, 1, 10, 1, 120);
            ImGui.SameLine();
            ImGui.TextUnformatted(Localization.T("text.panel.properties.fps"));
            ImGui.TextUnformatted(Localization.T("text.panel.properties.presets"));
            if (ImGui.Button("24"))  { _framerate = 24; frameRateChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("30"))  { _framerate = 30; frameRateChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("60"))  { _framerate = 60; frameRateChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("120")) { _framerate = 120; frameRateChanged = true; }
            if (frameRateChanged)
            {
                _framerate = Math.Clamp(_framerate, 1, 120);
                WriteProjectSettingsToManifest(projectManager.Manifest);
                projectManager.SetDirty(true);
            }

            ImGui.Spacing();
            ImGui.TextUnformatted(Localization.T("text.panel.properties.texture_animation_speed"));
            ImGui.SetNextItemWidth(80);
            bool textureFpsChanged = InputIntEditor("##TexAnimSpeed", ref TextureAnimationFps, 1, 10, 1, 120);
            ImGui.SameLine();
            ImGui.TextUnformatted(Localization.T("text.panel.properties.fps"));
            ImGui.TextUnformatted(Localization.T("text.panel.properties.presets"));
            if (ImGui.Button("10##tex")) { TextureAnimationFps = 10; textureFpsChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("20##tex")) { TextureAnimationFps = 20; textureFpsChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("30##tex")) { TextureAnimationFps = 30; textureFpsChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("60##tex")) { TextureAnimationFps = 60; textureFpsChanged = true; }
            if (textureFpsChanged)
            {
                TextureAnimationFps = Math.Clamp(TextureAnimationFps, 1, 240);
                WriteProjectSettingsToManifest(projectManager.Manifest);
                projectManager.SetDirty(true);
            }

        }

        if (ImGui.CollapsingHeader(Localization.Label("text.panel.properties.library", "Library"), ImGuiTreeNodeFlags.DefaultOpen))
            RenderObjectLibrarySection(ProjectManager.Instance);

        if (ImGui.CollapsingHeader(Localization.Label("text.panel.properties.render_settings", "Render Settings")))
            RenderRenderSettings();

        if (ImGui.CollapsingHeader(Localization.Label("text.panel.properties.background_settings", "Background Settings")))
        {
            unsafe
            {
                bool skyChanged = false;
                bool useSky = UseSky;
                if (ImGui.Checkbox(Localization.Label("text.panel.properties.minecraft_sky", "Minecraft Sky"), ref useSky)) { UseSky = useSky; skyChanged = true; }
                RegisterBackgroundKeyframeContext(nameof(UseSky));
                if (UseSky)
                {
                    ImGui.Indent();
                    skyChanged |= SkyColorEditor("Horizon Day", SkyHorizonDay);
                    skyChanged |= SkyColorEditor("Zenith Day", SkyZenithDay);
                    skyChanged |= SkyColorEditor("Horizon Sunset", SkyHorizonSunset);
                    skyChanged |= SkyColorEditor("Zenith Sunset", SkyZenithSunset);
                    skyChanged |= SkyColorEditor("Night Horizon", SkyHorizonNight);
                    skyChanged |= SkyColorEditor("Night Zenith", SkyZenithNight);
                    bool twilight = Twilight;
                    if (ImGui.Checkbox(Localization.Label("text.panel.properties.twilight", "Twilight"), ref twilight)) { Twilight = twilight; skyChanged = true; }
                    RegisterBackgroundKeyframeContext(nameof(Twilight));
                    ImGui.Spacing();
                    ImGui.TextUnformatted(Localization.T("text.panel.properties.stars"));
                    bool showStars = ShowStars;
                    if (ImGui.Checkbox(Localization.Label("text.panel.properties.show_stars", "Show Stars##stars"), ref showStars)) { ShowStars = showStars; skyChanged = true; }
                    RegisterBackgroundKeyframeContext(nameof(ShowStars));
                    if (ShowStars)
                    {
                        ImGui.Indent();
                        skyChanged |= InputFloatEditor("Density", ref StarDensity, 0.01f, 0f, 5f, "%.2f");
                        RegisterBackgroundKeyframeContext(nameof(StarDensity));
                        skyChanged |= InputFloatEditor("Brightness", ref StarBrightness, 0.01f, 0f, 5f, "%.2f");
                        RegisterBackgroundKeyframeContext(nameof(StarBrightness));
                        skyChanged |= InputFloatEditor("Twinkle Speed", ref StarTwinkleSpeed, 0.01f, 0f, 5f, "%.2f");
                        RegisterBackgroundKeyframeContext(nameof(StarTwinkleSpeed));
                        skyChanged |= SkyColorEditor("Star Color", StarColor);
                        ImGui.Unindent();
                    }
                    ImGui.Spacing();
                    skyChanged |= SkyColorEditor("Cloud Color", CloudColor);
                    skyChanged |= SkyColorEditor("Night Cloud Color", NightCloudColor);
                    ImGui.Spacing();
                    skyChanged |= SkyTextureSelector("Sun Texture", ref SunTexture, "sun.png");
                    skyChanged |= SkyTextureSelector("Moon Texture", ref MoonTexture, "moon_phases.png");
                    skyChanged |= SkyTextureSelector("Cloud Texture", ref CloudTexture, "clouds.png");
                    string cloudModeLabel = CloudRenderMode == "story" ? "Story Mode" : CloudRenderMode == "flat" ? "Flat" : "3D";
                    bool cloudModeOpen = ImGui.BeginCombo(Localization.Label("text.panel.properties.cloud_rendering", "Cloud Rendering"), Localization.DisplayText(cloudModeLabel));
                    RegisterBackgroundKeyframeContext(nameof(CloudRenderMode));
                    if (cloudModeOpen)
                    {
                        if (ImGui.Selectable(Localization.Label("text.panel.properties.3d", "3D"), CloudRenderMode == "3d")) { CloudRenderMode = "3d"; skyChanged = true; }
                        if (ImGui.Selectable(Localization.Label("text.panel.properties.story_mode", "Story Mode"), CloudRenderMode == "story")) { CloudRenderMode = "story"; skyChanged = true; }
                        if (ImGui.Selectable(Localization.Label("text.common.flat", "Flat"), CloudRenderMode == "flat")) { CloudRenderMode = "flat"; skyChanged = true; }
                        ImGui.EndCombo();
                    }
                    skyChanged |= InputFloatEditor("Cloud Speed", ref CloudSpeed, 1f, -10000f, 10000f, "%.0f px/s");
                    RegisterBackgroundKeyframeContext(nameof(CloudSpeed));
                    fixed (float* value = CloudOffset) skyChanged |= ImGui.DragFloat2(Localization.Label("text.panel.properties.cloud_offset", "Cloud Offset"), value, 1f, -100000f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudOffset));
                    skyChanged |= InputFloatEditor("Cloud Height", ref CloudHeight, 1f, 0f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudHeight));
                    skyChanged |= InputFloatEditor("Cloud Block Size", ref CloudBlockSize, 1f, 1f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudBlockSize));
                    skyChanged |= InputFloatEditor("Cloud Thickness", ref CloudThickness, 1f, 1f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudThickness));
                    skyChanged |= ImGui.SliderInt(Localization.Label("text.panel.properties.moon_phase", "Moon Phase"), ref MoonPhase, 0, 7);
                    RegisterBackgroundKeyframeContext(nameof(MoonPhase));
                    skyChanged |= ImGui.SliderFloat(Localization.Label("text.panel.properties.time", "Time"), ref SkyTime, 0f, 24f, "%.2f h");
                    RegisterBackgroundKeyframeContext(nameof(SkyTime));
                    skyChanged |= InputFloatEditor("Sun Size (degrees)", ref SunSize, 0.1f, 0.1f, 90f);
                    RegisterBackgroundKeyframeContext(nameof(SunSize));
                    fixed (float* value = SunAngle) skyChanged |= ImGui.DragFloat3(Localization.Label("text.panel.properties.sun_angle_xyz", "Sun Angle (XYZ)"), value, 0.25f, -360f, 360f);
                    RegisterBackgroundKeyframeContext(nameof(SunAngle));
                    skyChanged |= InputFloatEditor("Moon Size (degrees)", ref MoonSize, 0.1f, 0.1f, 90f);
                    RegisterBackgroundKeyframeContext(nameof(MoonSize));
                    fixed (float* value = MoonAngle) skyChanged |= ImGui.DragFloat3(Localization.Label("text.panel.properties.moon_angle_xyz", "Moon Angle (XYZ)"), value, 0.25f, -360f, 360f);
                    RegisterBackgroundKeyframeContext(nameof(MoonAngle));
                    skyChanged |= SkyColorEditor("Sun Fill Light", SunFillLightColor);
                    skyChanged |= InputFloatEditor("Sun Fill Strength", ref SunFillLightStrength, 0.01f, 0f, 5f);
                    RegisterBackgroundKeyframeContext(nameof(SunFillLightStrength));
                    skyChanged |= ImGui.Checkbox(Localization.Label("text.panel.properties.sun_fill_casts_shadows", "Sun Fill Casts Shadows"), ref SunFillLightCastsShadows);
                    RegisterBackgroundKeyframeContext(nameof(SunFillLightCastsShadows));
                    skyChanged |= SkyColorEditor("Moon Fill Light", MoonFillLightColor);
                    skyChanged |= InputFloatEditor("Moon Fill Strength", ref MoonFillLightStrength, 0.01f, 0f, 5f);
                    RegisterBackgroundKeyframeContext(nameof(MoonFillLightStrength));
                    skyChanged |= ImGui.Checkbox(Localization.Label("text.panel.properties.moon_fill_casts_shadows", "Moon Fill Casts Shadows"), ref MoonFillLightCastsShadows);
                    RegisterBackgroundKeyframeContext(nameof(MoonFillLightCastsShadows));
                    ImGui.Unindent();
                }
                else
                    UiText.Disabled(Localization.T("text.panel.properties.solid_background_color_is_active"));

                if (skyChanged)
                {
                    Viewport?.ReloadSkyTextures();
                    WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                    ProjectManager.Instance.SetDirty(true);
                }

                ImGui.SeparatorText(Localization.T("text.panel.properties.fog"));
                bool fogChanged = ImGui.Checkbox(Localization.Label("text.panel.properties.enable_fog", "Enable Fog"), ref FogEnabled);
                if (FogEnabled)
                {
                    ImGui.Indent();
                    fogChanged |= ImGui.Checkbox(Localization.Label("text.panel.properties.fog_the_sky", "Fog the Sky"), ref SkyFog);
                    fogChanged |= ImGui.Checkbox(Localization.Label("text.panel.properties.custom_fog_color", "Custom Fog Color"), ref CustomFogColor);
                    if (CustomFogColor) fogChanged |= SkyColorEditor("Fog Color", FogColor);
                    fogChanged |= ImGui.Checkbox(Localization.Label("text.panel.properties.custom_object_fog_color", "Custom Object Fog Color"), ref CustomObjectFogColor);
                    if (CustomObjectFogColor) fogChanged |= SkyColorEditor("Object Fog Color", ObjectFogColor);
                    fogChanged |= InputFloatEditor("Distance", ref FogDistance, 10f, 0f, 1000000f, "%.0f px");
                    fogChanged |= InputFloatEditor("Fade Size", ref FogFadeSize, 10f, 1f, 1000000f, "%.0f px");
                    fogChanged |= InputFloatEditor("Height", ref FogHeight, 10f, -1000000f, 1000000f, "%.0f px");
                    fogChanged |= ImGui.Checkbox(Localization.Label("text.panel.properties.height_fog", "Height Fog"), ref HeightFog);
                    if (HeightFog)
                    {
                        fogChanged |= ImGui.Checkbox(Localization.Label("text.panel.properties.custom_height_fog_color", "Custom Height Fog Color"), ref CustomHeightFogColor);
                        if (CustomHeightFogColor) fogChanged |= SkyColorEditor("Height Fog Color", HeightFogColor);
                        fogChanged |= InputFloatEditor("Height Fog Size", ref HeightFogSize, 10f, 1f, 1000000f, "%.0f px");
                        fogChanged |= InputFloatEditor("Height Fog Offset", ref HeightFogOffset, 10f, -1000000f, 1000000f, "%.0f px");
                    }
                    ImGui.Unindent();
                }
                if (fogChanged)
                {
                    WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                    ProjectManager.Instance.SetDirty(true);
                }

                ImGui.Separator();
                ImGui.TextUnformatted(Localization.T("text.panel.properties.background_color"));
                ImGui.SetNextItemWidth(-1);
                fixed (byte* label = "##BackgroundColor"u8)
                fixed (float* bgColorPtr = BackgroundColor)
                {
                    if (ImGui.ColorEdit4(label, bgColorPtr, ImGuiColorEditFlags.NoInputs))
                    {
                        WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                        ProjectManager.Instance.SetDirty(true);
                    }
                }
                RegisterBackgroundKeyframeContext(nameof(BackgroundColor));

                ImGui.Spacing();
                ImGui.TextUnformatted(Localization.T("text.panel.properties.presets"));
                var presets = new (string name, float r, float g, float b, float a)[]
                {
                    ("Dawn",    1f,          0.7f,        0.5f,  1f),
                    ("Morning", 0.6f,        0.8f,        1f,    1f),
                    ("Day",     0.5764706f,  0.5764706f,  1f,    1f),
                    ("Sunset",  1f,          0.5f,        0.3f,  1f),
                    ("Dusk",    0.3f,        0.4f,        0.7f,  1f),
                    ("Night",   0.05f,       0.05f,       0.15f, 1f)
                };
                for (int i = 0; i < presets.Length; i++)
                {
                    if (i > 0) ImGui.SameLine();
                    if (ImGui.Button(Localization.DisplayLabel(presets[i].name)))
                    {
                        BackgroundColor[0] = presets[i].r;
                        BackgroundColor[1] = presets[i].g;
                        BackgroundColor[2] = presets[i].b;
                        BackgroundColor[3] = presets[i].a;
                        WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                        ProjectManager.Instance.SetDirty(true);
                    }
                }
                ImGui.Spacing();

                bool floorVisible = FloorVisible;
                if (ImGui.Checkbox(Localization.Label("text.panel.properties.show_floor", "Show Floor"), ref floorVisible))
                {
                    FloorVisible = floorVisible;
                    ApplyFloorSettingsToViewport();
                    WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                    ProjectManager.Instance.SetDirty(true);
                }
                RegisterBackgroundKeyframeContext(nameof(FloorVisible));

                if (FloorVisible)
                {
                    bool floorChanged = false;
                    string floorAtlasLabel = NormalizeFloorAtlas(FloorTextureAtlas) == "item" ? "Item Atlas" : "Block Atlas";
                    bool floorAtlasOpen = ImGui.BeginCombo(Localization.Label("text.panel.properties.floor_atlas", "Floor Atlas"), Localization.DisplayText(floorAtlasLabel));
                    RegisterBackgroundKeyframeContext(nameof(FloorTextureAtlas));
                    if (floorAtlasOpen)
                    {
                        bool useBlock = NormalizeFloorAtlas(FloorTextureAtlas) == "block";
                        if (ImGui.Selectable(Localization.Label("text.common.block_atlas", "Block Atlas"), useBlock))
                        {
                            FloorTextureAtlas = "block";
                            floorChanged = true;
                        }

                        bool useItem = NormalizeFloorAtlas(FloorTextureAtlas) == "item";
                        if (ImGui.Selectable(Localization.Label("text.common.item_atlas", "Item Atlas"), useItem))
                        {
                            FloorTextureAtlas = "item";
                            floorChanged = true;
                        }

                        ImGui.EndCombo();
                    }

                    ImGui.TextUnformatted(Localization.T("text.panel.properties.floor_tile"));
                    ImGui.SetNextItemWidth(-1);
                    bool floorTileOpen = ImGui.BeginCombo("##FloorTile", FloorTileKey);
                    RegisterBackgroundKeyframeContext(nameof(FloorTileKey));
                    if (floorTileOpen)
                    {
                        foreach (string key in GetFloorAtlasKeys())
                        {
                            bool selected = string.Equals(key, FloorTileKey, StringComparison.Ordinal);
                            if (ImGui.Selectable(key, selected))
                            {
                                FloorTileKey = key;
                                floorChanged = true;
                            }
                        }
                        ImGui.EndCombo();
                    }

                    if (floorChanged)
                    {
                        ApplyFloorSettingsToViewport();
                        WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                        ProjectManager.Instance.SetDirty(true);
                    }
                }

                ImGui.Spacing();
                bool backgroundChanged = false;
                
                var imageAssets = GetBackgroundImageAssets();
                string selectedImageLabel = GetBackgroundImageLabel(BackgroundImagePath);
                ImGui.TextUnformatted(Localization.T("text.panel.properties.background_image"));
                ImGui.SetNextItemWidth(-1);
                bool backgroundImageOpen = ImGui.BeginCombo("##BackgroundImage", selectedImageLabel == NoImageSelected ? Localization.T("text.common.no_image_selected") : selectedImageLabel);
                RegisterBackgroundKeyframeContext(nameof(BackgroundImagePath));
                if (backgroundImageOpen)
                {
                    bool noneSelected = string.Equals(BackgroundImagePath, NoImageSelected, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(Localization.Label("text.common.no_image_selected", "No image selected"), noneSelected))
                    {
                        BackgroundImagePath = NoImageSelected;
                        backgroundChanged = true;
                    }

                    foreach (var asset in imageAssets)
                    {
                        string candidatePath = !string.IsNullOrWhiteSpace(asset.RelativePath)
                            ? asset.RelativePath
                            : asset.SourcePath;
                        if (string.IsNullOrWhiteSpace(candidatePath))
                            continue;

                        bool selected = string.Equals(candidatePath, BackgroundImagePath, StringComparison.OrdinalIgnoreCase);
                        string optionLabel = string.IsNullOrWhiteSpace(asset.DisplayName)
                            ? Path.GetFileName(candidatePath)
                            : asset.DisplayName;

                        if (ImGui.Selectable(optionLabel + "##" + candidatePath, selected))
                        {
                            BackgroundImagePath = candidatePath;
                            backgroundChanged = true;
                        }
                    }

                    ImGui.EndCombo();
                }

                if (ImGui.Button(Localization.Label("text.common.import", "Import##backgroundImport")) && ImportBackgroundImageFromDialog())
                    backgroundChanged = true;

                ImGui.SameLine();
                if (ImGui.Button(Localization.Label("text.common.clear", "Clear##backgroundClear")) && !string.Equals(BackgroundImagePath, NoImageSelected, StringComparison.OrdinalIgnoreCase))
                {
                    BackgroundImagePath = NoImageSelected;
                    backgroundChanged = true;
                }

                if (backgroundChanged)
                {
                    ApplyBackgroundSettingsToViewport();
                    WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                    ProjectManager.Instance.SetDirty(true);
                }

                string modeLabel = BackgroundRenderMode switch
                {
                    BackgroundModeFit => "Fit",
                    BackgroundModeOriginal => "Original",
                    _ => "Stretch"
                };

                bool backgroundModeOpen = ImGui.BeginCombo(Localization.Label("text.panel.properties.background_mode", "Background Mode"), Localization.DisplayText(modeLabel));
                RegisterBackgroundKeyframeContext(nameof(BackgroundRenderMode));
                if (backgroundModeOpen)
                {
                    bool isStretch = string.Equals(BackgroundRenderMode, BackgroundModeStretch, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(Localization.Label("text.panel.properties.stretch", "Stretch"), isStretch))
                    {
                        BackgroundRenderMode = BackgroundModeStretch;
                        StretchBackground = true;
                        backgroundChanged = true;
                    }

                    bool isFit = string.Equals(BackgroundRenderMode, BackgroundModeFit, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(Localization.Label("text.panel.properties.fit", "Fit"), isFit))
                    {
                        BackgroundRenderMode = BackgroundModeFit;
                        StretchBackground = false;
                        backgroundChanged = true;
                    }

                    bool isOriginal = string.Equals(BackgroundRenderMode, BackgroundModeOriginal, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(Localization.Label("text.panel.properties.original", "Original"), isOriginal))
                    {
                        BackgroundRenderMode = BackgroundModeOriginal;
                        StretchBackground = false;
                        backgroundChanged = true;
                    }

                    ImGui.EndCombo();
                }

                float backgroundScale = BackgroundScale;
                if (InputFloatEditor("Background Scale", ref backgroundScale, 0.01f, 0.01f, 20f))
                {
                    BackgroundScale = Math.Clamp(backgroundScale, 0.01f, 20f);
                    backgroundChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(BackgroundScale));

                float backgroundRotation = BackgroundRotationDegrees;
                if (InputFloatEditor("Background Rotation", ref backgroundRotation, 0.25f, -360f, 360f))
                {
                    BackgroundRotationDegrees = Math.Clamp(backgroundRotation, -360f, 360f);
                    backgroundChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(BackgroundRotationDegrees));

                float offsetX = BackgroundOffset[0];
                if (InputFloatEditor("Background Offset X", ref offsetX, 0.005f, -3f, 3f))
                {
                    BackgroundOffset[0] = offsetX;
                    backgroundChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(BackgroundOffset) + ".0");

                float offsetY = BackgroundOffset[1];
                if (InputFloatEditor("Background Offset Y", ref offsetY, 0.005f, -3f, 3f))
                {
                    BackgroundOffset[1] = offsetY;
                    backgroundChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(BackgroundOffset) + ".1");

                ImGui.Spacing();
                ImGui.TextUnformatted(Localization.T("text.panel.properties.ambient_light"));
                bool ambientChanged = false;
                ImGui.SetNextItemWidth(-1);
                fixed (byte* ambientLabel = "##AmbientLightColor"u8)
                fixed (float* ambientColorPtr = AmbientLightColor)
                {
                    if (ImGui.ColorEdit3(ambientLabel, ambientColorPtr, ImGuiColorEditFlags.NoInputs))
                        ambientChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(AmbientLightColor));

                float ambientStrength = AmbientLightStrength;
                ImGui.SetNextItemWidth(120f);
                if (InputFloatEditor("Ambient Strength", ref ambientStrength, 0.01f, 0f, 5f))
                {
                    AmbientLightStrength = ambientStrength;
                    ambientChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(AmbientLightStrength));

                ImGui.Spacing();
                ImGui.TextUnformatted(Localization.T("text.panel.properties.night_ambient"));
                ImGui.SetNextItemWidth(-1);
                fixed (byte* nightAmbientLabel = "##NightAmbientLightColor"u8)
                fixed (float* nightAmbientColorPtr = NightAmbientLightColor)
                {
                    if (ImGui.ColorEdit3(nightAmbientLabel, nightAmbientColorPtr, ImGuiColorEditFlags.NoInputs))
                        ambientChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(NightAmbientLightColor));

                float nightAmbientStrength = NightAmbientLightStrength;
                ImGui.SetNextItemWidth(120f);
                if (InputFloatEditor("Night Ambient Strength", ref nightAmbientStrength, 0.01f, 0f, 5f))
                {
                    NightAmbientLightStrength = nightAmbientStrength;
                    ambientChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(NightAmbientLightStrength));

                ImGui.Spacing();
                ImGui.TextUnformatted(Localization.T("text.panel.properties.fill_light"));
                ImGui.SetNextItemWidth(-1);
                fixed (byte* fillLabel = "##FillLightColor"u8)
                fixed (float* fillColorPtr = FillLightColor)
                {
                    if (ImGui.ColorEdit3(fillLabel, fillColorPtr, ImGuiColorEditFlags.NoInputs))
                        ambientChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(FillLightColor));

                float fillStrength = FillLightStrength;
                ImGui.SetNextItemWidth(120f);
                if (InputFloatEditor("Fill Strength", ref fillStrength, 0.01f, 0f, 5f))
                {
                    FillLightStrength = fillStrength;
                    ambientChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(FillLightStrength));

                bool fillLightCastsShadows = FillLightCastsShadows;
                if (ImGui.Checkbox(Localization.Label("text.panel.properties.fill_light_casts_shadows", "Fill Light Casts Shadows"), ref fillLightCastsShadows))
                {
                    FillLightCastsShadows = fillLightCastsShadows;
                    ambientChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(FillLightCastsShadows));

                if (ambientChanged)
                {
                    ApplyAmbientSettingsToRenderer();
                    WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                    ProjectManager.Instance.SetDirty(true);
                }
            }
        }

        ImGui.EndTabItem();
    }

    private unsafe void RenderRenderSettings()
    {
        bool changed = false;

        ImGui.TextUnformatted(Localization.T("text.panel.properties.anti_aliasing"));
        changed |= ImGui.Checkbox(Localization.Label("text.panel.properties.enabled", "Enabled##antiAliasing"), ref AntiAliasingEnabled);
        if (AntiAliasingEnabled)
        {
            ImGui.Indent();
            changed |= PercentageSlider("Subpixel Smoothing##antiAliasing", ref AntiAliasingSubpixel, 0f, 100f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Localization.T("text.panel.properties.anti_aliasing_subpixel_smoothing_tooltip"));
            changed |= ImGui.SliderFloat(Localization.Label("text.panel.properties.anti_aliasing_edge_threshold", "Edge Threshold##antiAliasing"), ref AntiAliasingEdgeThreshold, 0.03125f, 0.5f, "%.3f");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Localization.T("text.panel.properties.anti_aliasing_edge_threshold_tooltip"));
            changed |= ImGui.SliderFloat(Localization.Label("text.panel.properties.anti_aliasing_minimum_threshold", "Minimum Threshold##antiAliasing"), ref AntiAliasingMinimumThreshold, 0.001f, 0.25f, "%.3f");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Localization.T("text.panel.properties.anti_aliasing_minimum_threshold_tooltip"));
            ImGui.TextWrapped(Localization.T("text.panel.properties.anti_aliasing_description"));
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Localization.T("text.panel.properties.ambient_occlusion"));
        changed |= ImGui.Checkbox(Localization.Label("text.panel.properties.enabled", "Enabled##ambientOcclusion"), ref AmbientOcclusionEnabled);
        if (AmbientOcclusionEnabled)
        {
            ImGui.Indent();
            changed |= InputFloatEditor("Radius (px)", ref AmbientOcclusionRadius, 0.25f, 0f, 128f, "%.1f");
            changed |= PercentageSlider("Strength##ao", ref AmbientOcclusionStrength, 0f, 200f);
            changed |= ImGui.SliderInt(Localization.Label("text.panel.properties.samples", "Samples##ao"), ref AmbientOcclusionSampleCount, 1, 128);
            fixed (float* color = AmbientOcclusionColor)
                changed |= ImGui.ColorEdit3(Localization.Label("text.common.color", "Color##ao"), color, ImGuiColorEditFlags.NoInputs);
            changed |= InputFloatEditor("Ratio##ao", ref AmbientOcclusionRatio, 0.001f, 0f, 1f, "%.3f");
            changed |= InputFloatEditor("Ratio Balance##ao", ref AmbientOcclusionRatioBalance, 0.005f, 0f, 1f, "%.3f");
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Localization.T("text.panel.properties.indirect_lighting"));
        changed |= ImGui.Checkbox(Localization.Label("text.panel.properties.enabled", "Enabled##indirectLighting"), ref IndirectLightingEnabled);
        if (IndirectLightingEnabled)
        {
            ImGui.Indent();

            string giModeLabel = string.Equals(GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase)
                ? "World Space (Experimental)"
                : "Screen Space";

            if (ImGui.BeginCombo(Localization.Label("text.panel.properties.global_illumination", "Global Illumination"), Localization.DisplayText(giModeLabel)))
            {
                bool screenSelected = !string.Equals(GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(Localization.Label("text.panel.properties.screen_space", "Screen Space"), screenSelected))
                {
                    GlobalIlluminationMode = "screenspace";
                    changed = true;
                }

                bool worldSelected = string.Equals(GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(Localization.Label("text.panel.properties.world_space_experimental", "World Space (Experimental)"), worldSelected))
                {
                    GlobalIlluminationMode = "world";
                    changed = true;
                }
                ImGui.EndCombo();
            }

            changed |= PercentageSlider("Precision##indirect", ref IndirectLightingPrecision, 0f, 100f);
            changed |= PercentageSlider("Strength##indirect", ref IndirectLightingStrength, 0f, 400f);
            changed |= InputFloatEditor("Ray Step##indirect", ref IndirectLightingRayStep, 0.1f, 1f, 64f, "%.1f");
            changed |= InputFloatEditor("Blur Radius##indirect", ref IndirectLightingBlurRadius, 0.05f, 0f, 8f, "%.2f");
            changed |= ImGui.Checkbox(Localization.Label("text.panel.properties.denoiser", "Denoiser##indirect"), ref IndirectLightingDenoiser);
            if (IndirectLightingDenoiser)
                changed |= ImGui.SliderFloat(Localization.Label("text.panel.properties.denoiser_strength", "Denoiser Strength##indirect"), ref IndirectLightingDenoiserStrength, 0f, 200f, "%.0f%%");

            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Localization.T("text.panel.properties.shadows"));
        changed |= ImGui.Checkbox(Localization.Label("text.panel.properties.enabled", "Enabled##globalShadows"), ref ShadowsEnabled);
        if (ShadowsEnabled)
        {
            ImGui.Indent();
            changed |= ShadowBufferCombo("Sun lamps", ref SunShadowBufferSize);
            changed |= ShadowBufferCombo("Spot lights", ref SpotShadowBufferSize);
            changed |= ShadowBufferCombo("Point lights", ref PointShadowBufferSize);
            changed |= PercentageSlider("Blur Strength", ref ShadowBlurStrength, 0f, 400f);
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Localization.T("text.panel.properties.glow"));
        changed |= ImGui.Checkbox(Localization.Label("text.panel.properties.enabled", "Enabled##glow"), ref GlowEnabled);
        if (GlowEnabled)
        {
            ImGui.Indent();
            changed |= PercentageSlider("Strength##glow", ref GlowStrength, 0f, 200f);
            changed |= InputFloatEditor("Size (px)##glow", ref GlowSize, 0.1f, 0f, 20f, "%.1f");
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Localization.T("text.panel.properties.subsurface_scattering"));
        changed |= ImGui.Checkbox(Localization.Label("text.panel.properties.enabled", "Enabled##sss"), ref SubsurfaceEnabled);
        if (SubsurfaceEnabled)
        {
            ImGui.Indent();
            changed |= ImGui.SliderInt(Localization.Label("text.panel.properties.blur_samples", "Blur Samples##sss"), ref SubsurfaceBlurSamples, 0, 32);
            changed |= PercentageSlider("Strength##sss", ref SubsurfaceStrength, 0f, 400f);
            changed |= PercentageSlider("Desaturation##sss", ref SubsurfaceDesaturation, 0f, 100f);
            changed |= PercentageSlider("Color Threshold##sss", ref SubsurfaceColorThreshold, 0f, 100f);
            fixed (float* sssRadius = SubsurfaceRadiusRgb)
                changed |= ImGui.DragFloat3(Localization.Label("text.panel.properties.radius_rgb", "Radius RGB##sss"), sssRadius, 0.01f, 0f, 8f, "%.3f");
            changed |= InputFloatEditor("Highlight Size##sss", ref SubsurfaceHighlightSize, 0.01f, 0f, 8f, "%.2f");
            changed |= PercentageSlider("Highlight Strength##sss", ref SubsurfaceHighlightStrength, 0f, 800f);
            changed |= InputFloatEditor("Highlight Sharpness##sss", ref SubsurfaceHighlightSharpness, 0.05f, 0.01f, 16f, "%.2f");
            changed |= PercentageSlider("Highlight Desaturation##sss", ref SubsurfaceHighlightDesaturation, 0f, 100f);
            changed |= PercentageSlider("Highlight Color Threshold##sss", ref SubsurfaceHighlightColorThreshold, 0f, 100f);
            changed |= ImGui.SliderFloat(Localization.Label("text.panel.properties.absorption", "Absorption##sss"), ref SubsurfaceAbsorption, -0.95f, 0.95f, "%.2f");
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Localization.T("text.panel.properties.color_management"));
        changed |= DrawColorManagementControls(ref ColorManagementEnabled, ref ColorManagementToneMapper,
            ref ColorManagementExposure, ref ColorManagementGamma, ref ColorManagementLook);

        if (changed)
        {
            WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
            ProjectManager.Instance.SetDirty(true);
        }
    }

    private static bool ShadowBufferCombo(string label, ref int value)
    {
        string[] labels = ["256", "512", "1024", "2048", "4096", "8192"];
        int[] values = [256, 512, 1024, 2048, 4096, 8192];
        int selected = Math.Max(0, Array.IndexOf(values, value));
        if (!ImGui.Combo(Localization.DisplayLabel(label), ref selected, labels, labels.Length))
            return false;
        value = values[selected];
        return true;
    }

    private static bool PercentageSlider(string label, ref float normalizedValue, float minimum, float maximum)
    {
        float percent = normalizedValue * 100f;
        if (!ImGui.SliderFloat(Localization.DisplayLabel(label), ref percent, minimum, maximum, "%.0f%%", ImGuiSliderFlags.None))
            return false;
        normalizedValue = percent / 100f;
        return true;
    }

    private static string[] BuildBackgroundKeyframeProperties()
    {
        string[] scalars =
        [
            nameof(UseSky), nameof(SunTexture), nameof(MoonTexture), nameof(CloudTexture), nameof(MoonPhase), nameof(SkyTime),
            nameof(SunSize), nameof(MoonSize), nameof(SunFillLightStrength), nameof(SunFillLightCastsShadows),
            nameof(MoonFillLightStrength), nameof(MoonFillLightCastsShadows),
            nameof(CloudRenderMode), nameof(CloudSpeed), nameof(CloudHeight), nameof(CloudBlockSize), nameof(CloudThickness),
            nameof(FloorVisible), nameof(FloorTextureAtlas), nameof(FloorTileKey), nameof(BackgroundImagePath),
            nameof(BackgroundRenderMode), nameof(BackgroundScale), nameof(BackgroundRotationDegrees),
            nameof(AmbientLightStrength), nameof(NightAmbientLightStrength), nameof(FillLightStrength), nameof(FillLightCastsShadows),
            nameof(Twilight), nameof(ShowStars), nameof(StarDensity), nameof(StarBrightness), nameof(StarTwinkleSpeed)
        ];
        string[] vectors =
        [
            nameof(SkyHorizonDay), nameof(SkyZenithDay), nameof(SkyHorizonSunset), nameof(SkyZenithSunset),
            nameof(SkyHorizonNight), nameof(SkyZenithNight), nameof(SunAngle), nameof(MoonAngle),
            nameof(SunFillLightColor), nameof(MoonFillLightColor), nameof(CloudColor), nameof(NightCloudColor),
            nameof(CloudOffset), nameof(BackgroundColor),
            nameof(BackgroundOffset), nameof(AmbientLightColor), nameof(NightAmbientLightColor), nameof(FillLightColor),
            nameof(StarColor)
        ];
        var result = new List<string>(scalars);
        foreach (string vector in vectors)
        {
            int count = vector == nameof(BackgroundColor) ? 4 : vector is nameof(CloudOffset) or nameof(BackgroundOffset) ? 2 : 3;
            for (int i = 0; i < count; i++) result.Add($"{vector}.{i}");
        }
        return result.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private (FieldInfo field, int component)? ResolveBackgroundProperty(string path)
    {
        string fieldName = path;
        int component = -1;
        int dot = path.LastIndexOf('.');
        if (dot > 0 && int.TryParse(path[(dot + 1)..], out int parsed))
        {
            fieldName = path[..dot];
            component = parsed;
        }
        FieldInfo? field = typeof(PropertiesPanel).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        return field == null ? null : (field, component);
    }

    private object? GetBackgroundPropertyValue(string path)
    {
        var resolved = ResolveBackgroundProperty(path);
        if (!resolved.HasValue) return null;
        object? value = resolved.Value.field.GetValue(this);
        return resolved.Value.component >= 0 && value is float[] array ? array[resolved.Value.component] : value;
    }

    private void SetBackgroundPropertyValue(string path, string serialized, bool discrete, float blendValue = 0f, bool useBlend = false)
    {
        var resolved = ResolveBackgroundProperty(path);
        if (!resolved.HasValue) return;
        FieldInfo field = resolved.Value.field;
        if (resolved.Value.component >= 0 && field.GetValue(this) is float[] array)
        {
            array[resolved.Value.component] = useBlend ? blendValue : float.Parse(serialized, CultureInfo.InvariantCulture);
            return;
        }
        if (field.FieldType == typeof(float)) field.SetValue(this, useBlend ? blendValue : float.Parse(serialized, CultureInfo.InvariantCulture));
        else if (field.FieldType == typeof(int)) field.SetValue(this, int.Parse(serialized, CultureInfo.InvariantCulture));
        else if (field.FieldType == typeof(bool)) field.SetValue(this, bool.Parse(serialized));
        else if (field.FieldType == typeof(string)) field.SetValue(this, serialized);
    }

    private void AddBackgroundKeyframe(string path, int frame)
    {
        object? value = GetBackgroundPropertyValue(path);
        if (value == null) return;
        var tracks = ProjectManager.Instance.Manifest.Settings.BackgroundKeyframes;
        if (!tracks.TryGetValue(path, out var keys)) tracks[path] = keys = new List<ProjectBackgroundKeyframeEntry>();
        bool discrete = value is string or bool or int;
        string serialized = value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? "";
        var key = keys.FirstOrDefault(k => k.Frame == frame);
        if (key == null) keys.Add(new ProjectBackgroundKeyframeEntry { Frame = frame, Value = serialized, Discrete = discrete });
        else { key.Value = serialized; key.Discrete = discrete; }
        keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        ProjectManager.Instance.SetDirty(true);
    }

    private void DrawBackgroundKeyframeControls()
    {
        ImGui.TextUnformatted(Localization.T("text.panel.properties.background_animation"));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##BackgroundKeyProperty", _selectedBackgroundKeyProperty))
        {
            foreach (string path in BackgroundKeyframeProperties)
                if (ImGui.Selectable(path, path == _selectedBackgroundKeyProperty)) _selectedBackgroundKeyProperty = path;
            ImGui.EndCombo();
        }
        object? current = GetBackgroundPropertyValue(_selectedBackgroundKeyProperty);
        if (current is float floatValue && InputFloatEditor("Value##BackgroundKeyValue", ref floatValue, 0.01f, float.MinValue, float.MaxValue))
            SetBackgroundPropertyValue(_selectedBackgroundKeyProperty, floatValue.ToString(CultureInfo.InvariantCulture), false);
        else if (current is int intValue && InputIntEditor("Value##BackgroundKeyValue", ref intValue, 1, 10, int.MinValue, int.MaxValue))
            SetBackgroundPropertyValue(_selectedBackgroundKeyProperty, intValue.ToString(CultureInfo.InvariantCulture), true);
        else if (current is bool boolValue && ImGui.Checkbox(Localization.Label("text.panel.properties.value", "Value##BackgroundKeyValue"), ref boolValue))
            SetBackgroundPropertyValue(_selectedBackgroundKeyProperty, boolValue.ToString(), true);
        else if (current is string stringValue && ImGui.InputText(Localization.Label("text.panel.properties.value", "Value##BackgroundKeyValue"), ref stringValue, 1024))
            SetBackgroundPropertyValue(_selectedBackgroundKeyProperty, stringValue, true);
        int frame = Timeline?.CurrentFrame ?? 0;
        if (ImGui.Button(Localization.Format("text.panel.properties.add_keyframe_frame", frame) + "###AddBackgroundKeyframe")) AddBackgroundKeyframe(_selectedBackgroundKeyProperty, frame);
        ImGui.SameLine();
        if (ImGui.Button(Localization.Label("text.common.remove", "Remove##BackgroundKey")))
        {
            var tracks = ProjectManager.Instance.Manifest.Settings.BackgroundKeyframes;
            if (tracks.TryGetValue(_selectedBackgroundKeyProperty, out var keys))
            {
                keys.RemoveAll(k => k.Frame == frame);
                if (keys.Count == 0) tracks.Remove(_selectedBackgroundKeyProperty);
                ProjectManager.Instance.SetDirty(true);
            }
        }
        if (ImGui.Button(Localization.Label("text.panel.properties.keyframe_all_background_settings", "Keyframe All Background Settings")))
            foreach (string path in BackgroundKeyframeProperties) AddBackgroundKeyframe(path, frame);
    }

    private void ApplyBackgroundAnimation(int frame)
    {
        var tracks = ProjectManager.Instance.Manifest?.Settings?.BackgroundKeyframes;
        if (tracks == null || tracks.Count == 0) return;
        bool applied = false;
        foreach (var (path, keys) in tracks)
        {
            if (keys.Count == 0) continue;
            ProjectBackgroundKeyframeEntry? previous = keys.Where(k => k.Frame <= frame).OrderByDescending(k => k.Frame).FirstOrDefault();
            ProjectBackgroundKeyframeEntry? next = keys.Where(k => k.Frame >= frame).OrderBy(k => k.Frame).FirstOrDefault();
            if (previous == null) continue;
            if (previous.Discrete || next == null || next.Frame == previous.Frame)
                SetBackgroundPropertyValue(path, previous.Value, true);
            else
            {
                float a = float.Parse(previous.Value, CultureInfo.InvariantCulture);
                float b = float.Parse(next.Value, CultureInfo.InvariantCulture);
                float t = (frame - previous.Frame) / (float)(next.Frame - previous.Frame);
                SetBackgroundPropertyValue(path, previous.Value, false, a + (b - a) * t, true);
            }
            applied = true;
        }
        if (applied)
        {
            ApplyAmbientSettingsToRenderer();
            ApplyFloorSettingsToViewport();
            ApplyBackgroundSettingsToViewport();
        }
    }

    private unsafe bool SkyColorEditor(string label, float[] color)
    {
        bool changed;
        fixed (float* value = color)
            changed = ImGui.ColorEdit3(Localization.DisplayLabel(label), value, ImGuiColorEditFlags.NoInputs);
        string path = label switch
        {
            "Horizon Day" => nameof(SkyHorizonDay), "Zenith Day" => nameof(SkyZenithDay),
            "Horizon Sunset" => nameof(SkyHorizonSunset), "Zenith Sunset" => nameof(SkyZenithSunset),
            "Night Horizon" => nameof(SkyHorizonNight), "Night Zenith" => nameof(SkyZenithNight),
            "Cloud Color" => nameof(CloudColor), "Night Cloud Color" => nameof(NightCloudColor),
            "Star Color" => nameof(StarColor),
            "Sun Fill Light" => nameof(SunFillLightColor),
            "Moon Fill Light" => nameof(MoonFillLightColor), _ => label
        };
        RegisterBackgroundKeyframeContext(path);
        return changed;
    }

    private bool SkyTextureSelector(string label, ref string selected, string vanillaFile)
    {
        bool changed = false;
        string vanilla = $"minecraft:environment/{vanillaFile}";
        string preview = selected.StartsWith("resourcepack:", StringComparison.OrdinalIgnoreCase)
            ? selected[13..]
            : Localization.Format("text.panel.properties.vanilla", Path.GetFileName(selected));
        bool open = ImGui.BeginCombo(Localization.DisplayLabel(label), preview);
        RegisterBackgroundKeyframeContext(label.StartsWith("Sun") ? nameof(SunTexture) : label.StartsWith("Moon") ? nameof(MoonTexture) : nameof(CloudTexture));
        if (open)
        {
            if (ImGui.Selectable(Localization.Format("text.panel.properties.vanilla", vanillaFile) + $"###Vanilla / {vanillaFile}", selected == vanilla))
            {
                selected = vanilla;
                changed = true;
            }

            foreach (var file in MinecraftDataLoader.EnumerateResourcePackFiles("assets", ".png"))
            {
                string normalized = file.RelativePath.Replace('\\', '/');
                if (!normalized.Contains("/textures/environment/", StringComparison.OrdinalIgnoreCase)) continue;
                string key = MinecraftDataLoader.BuildResourcePackTextureKey(file.PackName, normalized);
                string option = $"{file.PackName} / {Path.GetFileName(normalized)}";
                if (ImGui.Selectable(option, selected == key))
                {
                    selected = key;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }
        return changed;
    }
    
    private void RenderObjectTab()
    {
        if (!ImGui.BeginTabItem(Localization.Label("text.common.object", "Object"))) return;

        // ── Object header ─────────────────────────────────────────────────────
        if (_currentObject == null)
        {
            float windowWidth = ImGui.GetWindowWidth();
            string noSelText = Localization.T("text.common.no_object_selected");
            float textWidth  = ImGui.CalcTextSize(noSelText).X;
            ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
            UiText.Disabled(noSelText);
            ImGui.EndTabItem();
            return;
        }

        ImGui.TextUnformatted(Localization.Label("text.common.name", "Name"));
        ImGui.SetNextItemWidth(-1);
        _nameEditor.Render(_currentObject, "##objectName");

        ImGui.Separator();

        ImGui.Spacing();

        // ── Position ──────────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader(Localization.Label("text.common.position", "Position")))
        {
            bool inheritPos = _currentObject.InheritPosition;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.inherit_position", "Inherit Position##pos"), ref inheritPos))
                ApplyToSelectedObjects(obj => obj.InheritPosition = inheritPos);

            // MiBoneSceneObjects expose an offset from their model base pose (always zero at load).
            // Plain BoneSceneObjects (GLB) use TargetPosition as an offset from rest pose.
            vec3 rawPos = (_currentObject is MiBoneSceneObject miPos)
                ? miPos.OffsetPosition
                : (_currentObject is BoneSceneObject bonePos)
                    ? bonePos.TargetPosition
                    : _currentObject.LocalPosition;

            float posX = rawPos.x * 16f;
            float posY = rawPos.y * 16f;
            float posZ = rawPos.z * 16f;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            
            // Position X
            if (InputFloatEditor("X##posX", ref posX, 0.1f, float.MinValue, float.MaxValue))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "position.x";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Position Y
            if (InputFloatEditor("Y##posY", ref posY, 0.1f, float.MinValue, float.MaxValue))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "position.y";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Position Z
            if (InputFloatEditor("Z##posZ", ref posZ, 0.1f, float.MinValue, float.MaxValue))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "position.z";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            ImGui.PopItemWidth();

        }

        // ── Rotation ──────────────────────────────────────────────────────────
        // Point lights cannot have rotation (they're omni-directional); spot
        // lights use rotation to aim the cone so they are rotatable.
        bool canRotate = _currentObject is not LightSceneObject rotLight ||
                         rotLight.Type == LightType.Spot;
        if (canRotate && ImGui.CollapsingHeader(Localization.Label("text.panel.properties.rotation_degrees", "Rotation (degrees)")))
        {
            bool inheritRot = _currentObject.InheritRotation;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.inherit_rotation", "Inherit Rotation##rot"), ref inheritRot))
                ApplyToSelectedObjects(obj => obj.InheritRotation = inheritRot);

            vec3 rawRot = (_currentObject is MiBoneSceneObject miRot)
                ? miRot.OffsetRotation
                : (_currentObject is BoneSceneObject boneRot)
                    ? boneRot.TargetRotation
                    : _currentObject.LocalRotation;

            float rotX = rawRot.x * (180f / MathF.PI);
            float rotY = rawRot.y * (180f / MathF.PI);
            float rotZ = rawRot.z * (180f / MathF.PI);

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            
            // Rotation X
            if (InputFloatEditor("X##rotX", ref rotX, 0.5f, float.MinValue, float.MaxValue))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "rotation.x";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Rotation Y
            if (InputFloatEditor("Y##rotY", ref rotY, 0.5f, float.MinValue, float.MaxValue))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "rotation.y";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Rotation Z
            if (InputFloatEditor("Z##rotZ", ref rotZ, 0.5f, float.MinValue, float.MaxValue))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "rotation.z";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            ImGui.PopItemWidth();

        }

        // ── Scale ─────────────────────────────────────────────────────────────
        // Cameras and point lights cannot have their scale changed
        bool canScale = !(_currentObject is CameraSceneObject) && !(_currentObject is LightSceneObject);
        if (canScale && ImGui.CollapsingHeader(Localization.Label("text.common.scale", "Scale")))
        {
            bool inheritScale = _currentObject.InheritScale;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.inherit_scale", "Inherit Scale##scale"), ref inheritScale))
                ApplyToSelectedObjects(obj => obj.InheritScale = inheritScale);

            ImGui.Checkbox(Localization.Label("text.panel.properties.link_scale", "Link Scale"), ref _linkScale);

            vec3 curScale = (_currentObject is MiBoneSceneObject miScale)
                ? miScale.OffsetScale
                : _currentObject.LocalScale;
            float scaleX = curScale.x;
            float scaleY = curScale.y;
            float scaleZ = curScale.z;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            
            // Scale X
            if (InputFloatEditor("X##scaleX", ref scaleX, 0.01f, 0.001f, float.MaxValue))
            {
                scaleX = MathF.Max(scaleX, 0.001f);
                if (_linkScale)
                {
                    float delta = scaleX - curScale.x;
                    scaleY = MathF.Max(curScale.y + delta, 0.001f);
                    scaleZ = MathF.Max(curScale.z + delta, 0.001f);
                }
                ApplyScale(new vec3(scaleX, scaleY, scaleZ));
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "scale.x";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Scale Y
            if (InputFloatEditor("Y##scaleY", ref scaleY, 0.01f, 0.001f, float.MaxValue))
            {
                scaleY = MathF.Max(scaleY, 0.001f);
                if (_linkScale)
                {
                    float delta = scaleY - curScale.y;
                    scaleX = MathF.Max(curScale.x + delta, 0.001f);
                    scaleZ = MathF.Max(curScale.z + delta, 0.001f);
                }
                ApplyScale(new vec3(scaleX, scaleY, scaleZ));
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "scale.y";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Scale Z
            if (InputFloatEditor("Z##scaleZ", ref scaleZ, 0.01f, 0.001f, float.MaxValue))
            {
                scaleZ = MathF.Max(scaleZ, 0.001f);
                if (_linkScale)
                {
                    float delta = scaleZ - curScale.z;
                    scaleX = MathF.Max(curScale.x + delta, 0.001f);
                    scaleY = MathF.Max(curScale.y + delta, 0.001f);
                }
                ApplyScale(new vec3(scaleX, scaleY, scaleZ));
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "scale.z";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            ImGui.PopItemWidth();

        }

        // ── Bend ──────────────────────────────────────────────────────────────
        // Bend axes are authored per Mine-imator part. Do not offer controls
        // that the selected part cannot actually deform along.
        if (_currentObject is MiBoneSceneObject bendBone &&
            bendBone.BendParameters is BendParams bend &&
            (bend.AxisX || bend.AxisY || bend.AxisZ) &&
            ImGui.CollapsingHeader(Localization.Label("text.panel.properties.bend_degrees", "Bend (degrees)")))
        {
            vec3 angle = bendBone.GetEditableBendAngle();
            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);

            if (bend.AxisX)
            {
                float value = angle.x;
                if (InputFloatEditor("X##bendX", ref value, 0.5f, bend.DirectionMin.x, bend.DirectionMax.x))
                {
                    angle.x = value;
                    ApplyBend(bendBone, angle, "bend.x");
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _ctxPropertyPath = "bend.x";
                    _ctxMenuPos = ImGui.GetMousePos();
                    _openPropContextMenu = true;
                }
            }
            if (bend.AxisY)
            {
                float value = angle.y;
                if (InputFloatEditor("Y##bendY", ref value, 0.5f, bend.DirectionMin.y, bend.DirectionMax.y))
                {
                    angle.y = value;
                    ApplyBend(bendBone, angle, "bend.y");
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _ctxPropertyPath = "bend.y";
                    _ctxMenuPos = ImGui.GetMousePos();
                    _openPropContextMenu = true;
                }
            }
            if (bend.AxisZ)
            {
                float value = angle.z;
                if (InputFloatEditor("Z##bendZ", ref value, 0.5f, bend.DirectionMin.z, bend.DirectionMax.z))
                {
                    angle.z = value;
                    ApplyBend(bendBone, angle, "bend.z");
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _ctxPropertyPath = "bend.z";
                    _ctxMenuPos = ImGui.GetMousePos();
                    _openPropContextMenu = true;
                }
            }
            ImGui.PopItemWidth();

        }

        // ── Block Tiling ───────────────────────────────────────────────────────
        if (string.Equals(_currentObject.SpawnCategory, "Blocks", StringComparison.Ordinal) &&
            ImGui.CollapsingHeader(Localization.Label("text.panel.properties.block_tiling", "Block Tiling")))
        {
            UiText.Wrapped(Localization.T("text.panel.properties.repeat_the_block_along_each_axis_each_axis_is_limited_to_1_1000"));
            ImGui.Spacing();

            int tileX = _currentObject.TileX;
            int tileY = _currentObject.TileY;
            int tileZ = _currentObject.TileZ;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);

            bool tileChanged = false;

            if (InputIntEditor("X##tileX", ref tileX, 1, 10, 1, SceneObject.MaxTilesPerAxis))
                tileChanged = true;
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "tile.x";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }

            if (InputIntEditor("Y##tileY", ref tileY, 1, 10, 1, SceneObject.MaxTilesPerAxis))
                tileChanged = true;
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "tile.y";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }

            if (InputIntEditor("Z##tileZ", ref tileZ, 1, 10, 1, SceneObject.MaxTilesPerAxis))
                tileChanged = true;
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "tile.z";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }

            ImGui.PopItemWidth();

            if (tileChanged)
                ApplyBlockTiling(tileX, tileY, tileZ);

            ImGui.Spacing();
            UiText.Disabled(Localization.Format("text.panel.properties.total_blocks", _currentObject.GetEffectiveTileX() * _currentObject.GetEffectiveTileY() * _currentObject.GetEffectiveTileZ()));

        }

        if (string.Equals(_currentObject.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_currentObject.ObjectType, "Cube", StringComparison.OrdinalIgnoreCase) &&
            ImGui.CollapsingHeader(Localization.Label("text.common.cube", "Cube")))
        {
            bool mapped = _currentObject.PrimitiveCubeMapped;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.mapped_uvs", "Mapped UVs##cubeMapped"), ref mapped))
            {
                ApplyCubeUvMapping(mapped);
            }

            UiText.Disabled(Localization.T("text.panel.properties.off_each_face_fills_the_full_texture"));
            UiText.Disabled(Localization.T("text.panel.properties.on_unwrap_to_a_3x2_cubemap_layout"));
        }

        if (string.Equals(_currentObject.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_currentObject.ObjectType, "Plane", StringComparison.OrdinalIgnoreCase) &&
            ImGui.CollapsingHeader(Localization.Label("text.common.plane", "Plane")))
        {
            var planeMesh = _currentObject.Visuals.OfType<PlaneMesh>().FirstOrDefault();
            if (planeMesh != null)
            {
                string[] orientationOptions = { "XY", "XZ" };
                int orientationIndex = planeMesh.Orientation == PlaneOrientation.XZ ? 1 : 0;
                if (ImGui.Combo(Localization.Label("text.common.orientation", "Orientation##planeOrientation"), ref orientationIndex, orientationOptions, orientationOptions.Length))
                {
                    planeMesh.SetOrientation(orientationIndex == 1 ? PlaneOrientation.XZ : PlaneOrientation.XY);
                    ProjectManager.Instance.SetDirty(true);
                }

                bool faceCamera = _currentObject.PrimitivePlaneFaceCamera;
                if (ImGui.Checkbox(Localization.Label("text.panel.properties.face_camera", "Face Camera##planeFaceCamera"), ref faceCamera))
                {
                    _currentObject.PrimitivePlaneFaceCamera = faceCamera;
                    ProjectManager.Instance.SetDirty(true);
                }
            }
            else
            {
                UiText.Disabled(Localization.T("text.panel.properties.plane_mesh_is_unavailable"));
            }
        }

        if (string.Equals(_currentObject.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_currentObject.ObjectType, "Sphere", StringComparison.OrdinalIgnoreCase) &&
            ImGui.CollapsingHeader(Localization.Label("text.common.sphere", "Sphere")))
        {
            bool smooth = _currentObject.PrimitiveSphereSmooth;
            int segments = _currentObject.PrimitiveSphereSegments;
            int rings = _currentObject.PrimitiveSphereRings;
            bool changed = ImGui.Checkbox(Localization.Label("text.common.smooth_shading", "Smooth Shading##sphereSmooth"), ref smooth);
            changed |= ImGui.DragInt(Localization.Label("text.common.segments", "Segments##sphereSegments"), ref segments, 1f, 3, 256);
            changed |= ImGui.DragInt(Localization.Label("text.common.rings", "Rings##sphereRings"), ref rings, 1f, 2, 254);

            if (changed)
            {
                segments = Math.Clamp(segments, 3, 256);
                rings = Math.Clamp(rings, 2, 254);
                _currentObject.PrimitiveSphereSmooth = smooth;
                _currentObject.PrimitiveSphereSegments = segments;
                _currentObject.PrimitiveSphereRings = rings;
                _currentObject.Visuals.OfType<SphereMesh>().FirstOrDefault()?.SetGeometry(segments, rings, smooth);
                ProjectManager.Instance.SetDirty(true);
            }
        }

        if (string.Equals(_currentObject.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(_currentObject.ObjectType, "Cylinder", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(_currentObject.ObjectType, "Cone", StringComparison.OrdinalIgnoreCase)) &&
            ImGui.CollapsingHeader(Localization.DisplayLabel(_currentObject.ObjectType + "##radialGeometry")))
        {
            int segments = _currentObject.PrimitiveRadialSegments;
            bool smooth = _currentObject.PrimitiveRadialSmooth;
            bool closed = _currentObject.PrimitiveRadialClosed;
            bool mapped = _currentObject.PrimitiveRadialMapped;
            bool changed = ImGui.DragInt(Localization.DisplayLabel("Detail##radialSegments"), ref segments, 1f, 3, 256);
            changed |= ImGui.Checkbox(Localization.Label("text.common.smooth_shading", "Smooth Shading##radialSmooth"), ref smooth);
            changed |= ImGui.Checkbox(Localization.DisplayLabel("Closed##radialClosed"), ref closed);
            bool mappedChanged = ImGui.Checkbox(Localization.Label("text.panel.properties.mapped_uvs", "Mapped UVs##radialMapped"), ref mapped);
            changed |= mappedChanged;
            if (changed)
            {
                segments = Math.Clamp(segments, 3, 256);
                _currentObject.PrimitiveRadialSegments = segments;
                _currentObject.PrimitiveRadialSmooth = smooth;
                _currentObject.PrimitiveRadialClosed = closed;
                _currentObject.PrimitiveRadialMapped = mapped;
                if (mappedChanged)
                {
                    _currentObject.Visuals.OfType<CylinderMesh>().FirstOrDefault()?.SetMapped(mapped);
                    _currentObject.Visuals.OfType<ConeMesh>().FirstOrDefault()?.SetMapped(mapped);
                }
                _currentObject.Visuals.OfType<CylinderMesh>().FirstOrDefault()?.SetGeometry(segments, smooth, closed);
                _currentObject.Visuals.OfType<ConeMesh>().FirstOrDefault()?.SetGeometry(segments, smooth, closed);
                ProjectManager.Instance.SetDirty(true);
            }
        }

        if (string.Equals(_currentObject.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_currentObject.ObjectType, "Text Mesh", StringComparison.OrdinalIgnoreCase) &&
            ImGui.CollapsingHeader(Localization.Label("text.common.text_mesh", "Text Mesh"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            string baseText = _currentObject.TextMeshBaseString;
            string textOverride = _currentObject.TextMeshStringOverride;
            bool extruded = _currentObject.TextMeshExtruded;
            float extrusionDepth = _currentObject.TextMeshExtrusionDepth;
            bool faceCamera = _currentObject.TextMeshFaceCamera;
            int horizontalAlignment = _currentObject.TextMeshHorizontalAlignment;
            int verticalAlignment = _currentObject.TextMeshVerticalAlignment;
            bool antialiasing = _currentObject.TextMeshAntialiasing;
            float fontSize = _currentObject.TextMeshFontSize;
            bool outlineEnabled = _currentObject.TextMeshOutlineEnabled;
            float outlineThickness = _currentObject.TextMeshOutlineThickness;
            Vector4 outlineColor = new(_currentObject.TextMeshOutlineColor.x, _currentObject.TextMeshOutlineColor.y,
                _currentObject.TextMeshOutlineColor.z, _currentObject.TextMeshOutlineColor.w);
            bool rebuild = ImGui.InputText(Localization.Label("text.panel.properties.base_string", "Base String"), ref baseText, 4096);
            rebuild |= ImGui.InputText(Localization.Label("text.panel.properties.string_override", "String Override"), ref textOverride, 4096);
            UiText.Disabled(Localization.T("text.panel.properties.leave_override_empty_to_use_the_base_string_animate_this_value_for_text_changes"));

            string fontLabel = string.Equals(_currentObject.TextMeshFontPath, "minecraftia", StringComparison.OrdinalIgnoreCase)
                ? "Minecraftia" : string.IsNullOrWhiteSpace(_currentObject.TextMeshFontPath)
                    ? Localization.T("text.panel.properties.system_sans_serif") : Path.GetFileName(_currentObject.TextMeshFontPath);
            ImGui.TextUnformatted(Localization.Format("text.panel.properties.font", fontLabel));
            ImGui.SameLine();
            if (ImGui.Button(Localization.Label("text.panel.properties.choose_font", "Choose Font...##textMeshFont")))
            {
                var result = Dialog.FileOpen("ttf,otf");
                if (result.IsOk)
                {
                    _currentObject.TextMeshFontPath = result.Path;
                    Timeline?.RecordAutoKeyframe(_currentObject, "text.font");
                    rebuild = true;
                }
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) { _ctxPropertyPath = "text.font"; _openPropContextMenu = true; }
            ImGui.SameLine();
            if (ImGui.Button("Minecraftia##textMeshFontMinecraftia"))
            {
                _currentObject.TextMeshFontPath = "minecraftia";
                Timeline?.RecordAutoKeyframe(_currentObject, "text.font");
                rebuild = true;
            }
            ImGui.SameLine();
            if (ImGui.Button(Localization.Label("text.panel.properties.system_default", "System Default##textMeshFontDefault")))
            {
                _currentObject.TextMeshFontPath = "";
                Timeline?.RecordAutoKeyframe(_currentObject, "text.font");
                rebuild = true;
            }

            string[] horizontalOptions = [Localization.T("text.panel.properties.left"), Localization.T("text.panel.properties.center"), Localization.T("text.panel.properties.right")];
            if (ImGui.Combo(Localization.Label("text.panel.properties.horizontal_alignment", "Horizontal Alignment"), ref horizontalAlignment, horizontalOptions, horizontalOptions.Length))
            { _currentObject.TextMeshHorizontalAlignment = horizontalAlignment; Timeline?.RecordAutoKeyframe(_currentObject, "text.horizontal_alignment"); rebuild = true; }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) { _ctxPropertyPath = "text.horizontal_alignment"; _openPropContextMenu = true; }
            string[] verticalOptions = [Localization.T("text.panel.properties.top"), Localization.T("text.panel.properties.center"), Localization.T("text.panel.properties.bottom")];
            if (ImGui.Combo(Localization.Label("text.panel.properties.vertical_alignment", "Vertical Alignment"), ref verticalAlignment, verticalOptions, verticalOptions.Length))
            { _currentObject.TextMeshVerticalAlignment = verticalAlignment; Timeline?.RecordAutoKeyframe(_currentObject, "text.vertical_alignment"); rebuild = true; }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) { _ctxPropertyPath = "text.vertical_alignment"; _openPropContextMenu = true; }
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.font_antialiasing", "Font Antialiasing"), ref antialiasing))
            { _currentObject.TextMeshAntialiasing = antialiasing; Timeline?.RecordAutoKeyframe(_currentObject, "text.antialiasing"); rebuild = true; }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) { _ctxPropertyPath = "text.antialiasing"; _openPropContextMenu = true; }
            if (ImGui.DragFloat(Localization.Label("text.panel.properties.font_size", "Font Size"), ref fontSize, 1f, 1f, 512f, "%.1f"))
            { _currentObject.TextMeshFontSize = Math.Clamp(fontSize, 1f, 512f); Timeline?.RecordAutoKeyframe(_currentObject, "text.font_size"); rebuild = true; }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) { _ctxPropertyPath = "text.font_size"; _openPropContextMenu = true; }
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.outline", "Outline"), ref outlineEnabled))
            { _currentObject.TextMeshOutlineEnabled = outlineEnabled; Timeline?.RecordAutoKeyframe(_currentObject, "text.outline_enabled"); rebuild = true; }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) { _ctxPropertyPath = "text.outline_enabled"; _openPropContextMenu = true; }
            if (outlineEnabled)
            {
                if (ImGui.ColorEdit4(Localization.Label("text.panel.properties.outline_color", "Outline Color"), ref outlineColor, ImGuiColorEditFlags.NoInputs))
                {
                    _currentObject.TextMeshOutlineColor = new vec4(outlineColor.X, outlineColor.Y, outlineColor.Z, outlineColor.W);
                    Timeline?.RecordAutoKeyframe(_currentObject, "text.outline.r"); Timeline?.RecordAutoKeyframe(_currentObject, "text.outline.g");
                    Timeline?.RecordAutoKeyframe(_currentObject, "text.outline.b"); Timeline?.RecordAutoKeyframe(_currentObject, "text.outline.a"); rebuild = true;
                }
                if (ImGui.DragFloat(Localization.Label("text.panel.properties.outline_thickness", "Outline Thickness"), ref outlineThickness, 0.1f, 0f, 64f, "%.1f"))
                { _currentObject.TextMeshOutlineThickness = Math.Clamp(outlineThickness, 0f, 64f); Timeline?.RecordAutoKeyframe(_currentObject, "text.outline_thickness"); rebuild = true; }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) { _ctxPropertyPath = "text.outline_thickness"; _openPropContextMenu = true; }
            }

            rebuild |= ImGui.Checkbox(Localization.Label("text.panel.properties.3d_extruded", "3D (Extruded)"), ref extruded);
            if (extruded)
                rebuild |= ImGui.DragFloat(Localization.Label("text.panel.properties.extrusion_depth", "Extrusion Depth"), ref extrusionDepth, 0.005f, 0.001f, 10f, "%.3f");
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.face_camera", "Face Camera"), ref faceCamera))
            {
                _currentObject.TextMeshFaceCamera = faceCamera;
                ProjectManager.Instance.SetDirty(true);
            }
            if (rebuild)
            {
                _currentObject.TextMeshBaseString = baseText;
                _currentObject.TextMeshStringOverride = textOverride;
                _currentObject.TextMeshExtruded = extruded;
                _currentObject.TextMeshExtrusionDepth = Math.Clamp(extrusionDepth, 0.001f, 10f);
                TextMeshFactory.Rebuild(_currentObject, Gl);
                ProjectManager.Instance.SetDirty(true);
            }
        }

        if (_currentObject is ParticleSpawnerSceneObject particleSpawner &&
            ImGui.CollapsingHeader(Localization.Label("text.panel.properties.particles", "Particles"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            EnsureObjectLibraryInitialized(ProjectManager.Instance.Manifest);

            var sourceEntries = new List<ProjectSceneObjectEntry>();
            CollectParticleSourceEntries(ProjectManager.Instance.Manifest.ObjectLibrary, sourceEntries);
            sourceEntries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            var selectedSource = FindLibraryEntryById(sourceEntries, particleSpawner.ParticleLibraryEntryId);
            string selectedLabel = selectedSource == null
                ? "(none)"
                : $"{selectedSource.Name} [{selectedSource.ObjectType}]";

            if (ImGui.BeginCombo(Localization.Label("text.common.particle_source", "Particle Source"), selectedLabel == "(none)" ? Localization.T("text.panel.properties.none") : selectedLabel))
            {
                bool noneSelected = string.IsNullOrWhiteSpace(particleSpawner.ParticleLibraryEntryId);
                if (ImGui.Selectable(Localization.Label("text.panel.properties.none", "(none)"), noneSelected))
                {
                    particleSpawner.SetParticleSource("", "");
                    ProjectManager.Instance.SetDirty(true);
                }

                foreach (var entry in sourceEntries)
                {
                    bool isSelected = string.Equals(entry.LibraryEntryId, particleSpawner.ParticleLibraryEntryId, StringComparison.OrdinalIgnoreCase);
                    string option = $"{entry.Name} [{entry.ObjectType}]##{entry.LibraryEntryId}";
                    if (ImGui.Selectable(option, isSelected))
                    {
                        particleSpawner.SetParticleSource(entry.LibraryEntryId, entry.Name);
                        ProjectManager.Instance.SetDirty(true);
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            bool emitting = particleSpawner.Emitting;
            if (ImGui.Checkbox(Localization.Label("text.common.emitting", "Emitting"), ref emitting))
            {
                particleSpawner.Emitting = emitting;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.emitting");
                ProjectManager.Instance.SetDirty(true);
            }

            bool oneShot = particleSpawner.OneShot;
            if (ImGui.Checkbox(Localization.Label("text.common.one_shot", "One Shot"), ref oneShot))
            {
                particleSpawner.OneShot = oneShot;
                particleSpawner.ResetRuntime();
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.one_shot");
                ProjectManager.Instance.SetDirty(true);
            }

            bool topLevelParticles = particleSpawner.TopLevelParticles;
            if (ImGui.Checkbox(Localization.Label("text.common.top_level_particles", "Top Level Particles"), ref topLevelParticles))
            {
                particleSpawner.TopLevelParticles = topLevelParticles;
                particleSpawner.ResetRuntime();
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.top_level_particles");
                ProjectManager.Instance.SetDirty(true);
            }

            int amount = particleSpawner.Amount;
            if (InputIntEditor("Amount", ref amount, 1, 10, 1, 10000))
            {
                particleSpawner.Amount = amount;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.amount");
                ProjectManager.Instance.SetDirty(true);
            }

            float spawnRate = particleSpawner.SpawnRate;
            if (InputFloatEditor("Spawn Rate", ref spawnRate, 0.1f, 0f, 10000f, "%.2f"))
            {
                particleSpawner.SpawnRate = spawnRate;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.spawn_rate");
                ProjectManager.Instance.SetDirty(true);
            }

            float lifeMin = particleSpawner.LifetimeMin;
            if (InputFloatEditor("Lifetime Min", ref lifeMin, 0.01f, 0.01f, 120f, "%.2f"))
            {
                particleSpawner.LifetimeMin = lifeMin;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.lifetime_min");
                ProjectManager.Instance.SetDirty(true);
            }

            float lifeMax = particleSpawner.LifetimeMax;
            if (InputFloatEditor("Lifetime Max", ref lifeMax, 0.01f, 0.01f, 120f, "%.2f"))
            {
                particleSpawner.LifetimeMax = lifeMax;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.lifetime_max");
                ProjectManager.Instance.SetDirty(true);
            }

            float simSpeed = particleSpawner.SimulationSpeed;
            if (InputFloatEditor("Simulation Speed", ref simSpeed, 0.01f, 0f, 32f, "%.2f"))
            {
                particleSpawner.SimulationSpeed = simSpeed;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.simulation_speed");
                ProjectManager.Instance.SetDirty(true);
            }

            float linearDamping = particleSpawner.LinearDamping;
            if (InputFloatEditor("Linear Damping", ref linearDamping, 0.01f, 0f, 100f, "%.3f"))
            {
                particleSpawner.LinearDamping = linearDamping;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.linear_damping");
                ProjectManager.Instance.SetDirty(true);
            }

            float angularDamping = particleSpawner.AngularDamping;
            if (InputFloatEditor("Angular Damping", ref angularDamping, 0.01f, 0f, 100f, "%.3f"))
            {
                particleSpawner.AngularDamping = angularDamping;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.angular_damping");
                ProjectManager.Instance.SetDirty(true);
            }

            string[] shapeOptions = { Localization.T("text.panel.properties.box"), Localization.T("text.common.sphere") };
            int emissionShape = particleSpawner.EmissionShape == ParticleEmissionShape.Sphere ? 1 : 0;
            if (ImGui.Combo(Localization.Label("text.common.emission_shape", "Emission Shape"), ref emissionShape, shapeOptions, shapeOptions.Length))
            {
                particleSpawner.EmissionShape = emissionShape == 1
                    ? ParticleEmissionShape.Sphere
                    : ParticleEmissionShape.Box;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.emission_shape");
                ProjectManager.Instance.SetDirty(true);
            }

            vec3 spawnExtents = particleSpawner.SpawnBoxExtents;
            if (EditVec3Editor(_currentObject, "particleSpawnExtents", ref spawnExtents, 0.01f, 0f, 1000f, "%.3f", "Spawn Extents", "particle.spawn_extents"))
            {
                particleSpawner.SpawnBoxExtents = spawnExtents;
                ProjectManager.Instance.SetDirty(true);
            }

            bool directionalEmission = particleSpawner.UseDirectionalEmission;
            if (ImGui.Checkbox(Localization.Label("text.common.directional_emission", "Directional Emission"), ref directionalEmission))
            {
                particleSpawner.UseDirectionalEmission = directionalEmission;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.directional_emission");
                ProjectManager.Instance.SetDirty(true);
            }

            if (particleSpawner.UseDirectionalEmission)
            {
                vec3 direction = particleSpawner.Direction;
                if (EditVec3Editor(_currentObject, "particleDirection", ref direction, 0.01f, -1f, 1f, "%.3f", "Direction", "particle.direction"))
                {
                    particleSpawner.Direction = direction;
                    ProjectManager.Instance.SetDirty(true);
                }

                float spread = particleSpawner.SpreadDegrees;
                if (InputFloatEditor("Spread (degrees)", ref spread, 0.1f, 0f, 180f, "%.2f"))
                {
                    particleSpawner.SpreadDegrees = spread;
                    Timeline?.RecordAutoKeyframe(_currentObject, "particle.spread");
                    ProjectManager.Instance.SetDirty(true);
                }

                float speedMin = particleSpawner.InitialSpeedMin;
                if (InputFloatEditor("Speed Min", ref speedMin, 0.01f, 0f, 10000f, "%.3f"))
                {
                    particleSpawner.InitialSpeedMin = speedMin;
                    Timeline?.RecordAutoKeyframe(_currentObject, "particle.speed_min");
                    ProjectManager.Instance.SetDirty(true);
                }

                float speedMax = particleSpawner.InitialSpeedMax;
                if (InputFloatEditor("Speed Max", ref speedMax, 0.01f, 0f, 10000f, "%.3f"))
                {
                    particleSpawner.InitialSpeedMax = speedMax;
                    Timeline?.RecordAutoKeyframe(_currentObject, "particle.speed_max");
                    ProjectManager.Instance.SetDirty(true);
                }
            }
            else
            {
                vec3 velocityMin = particleSpawner.InitialVelocityMin;
                if (EditVec3Editor(_currentObject, "particleVelMin", ref velocityMin, 0.01f, -1000f, 1000f, "%.3f", "Velocity Min", "particle.velocity_min"))
                {
                    particleSpawner.InitialVelocityMin = velocityMin;
                    ProjectManager.Instance.SetDirty(true);
                }

                vec3 velocityMax = particleSpawner.InitialVelocityMax;
                if (EditVec3Editor(_currentObject, "particleVelMax", ref velocityMax, 0.01f, -1000f, 1000f, "%.3f", "Velocity Max", "particle.velocity_max"))
                {
                    particleSpawner.InitialVelocityMax = velocityMax;
                    ProjectManager.Instance.SetDirty(true);
                }
            }

            vec3 gravity = particleSpawner.Gravity;
            if (EditVec3Editor(_currentObject, "particleGravity", ref gravity, 0.01f, -1000f, 1000f, "%.3f", "Gravity", "particle.gravity"))
            {
                particleSpawner.Gravity = gravity;
                ProjectManager.Instance.SetDirty(true);
            }

            vec3 rotMin = particleSpawner.InitialRotationMinDegrees;
            if (EditVec3Editor(_currentObject, "particleRotMin", ref rotMin, 0.5f, -3600f, 3600f, "%.2f", "Initial Rotation Min", "particle.rotation_min"))
            {
                particleSpawner.InitialRotationMinDegrees = rotMin;
                ProjectManager.Instance.SetDirty(true);
            }

            vec3 rotMax = particleSpawner.InitialRotationMaxDegrees;
            if (EditVec3Editor(_currentObject, "particleRotMax", ref rotMax, 0.5f, -3600f, 3600f, "%.2f", "Initial Rotation Max", "particle.rotation_max"))
            {
                particleSpawner.InitialRotationMaxDegrees = rotMax;
                ProjectManager.Instance.SetDirty(true);
            }

            vec3 angVelMin = particleSpawner.AngularVelocityMinDegrees;
            if (EditVec3Editor(_currentObject, "particleAngVelMin", ref angVelMin, 0.5f, -3600f, 3600f, "%.2f", "Angular Velocity Min", "particle.angular_velocity_min"))
            {
                particleSpawner.AngularVelocityMinDegrees = angVelMin;
                ProjectManager.Instance.SetDirty(true);
            }

            vec3 angVelMax = particleSpawner.AngularVelocityMaxDegrees;
            if (EditVec3Editor(_currentObject, "particleAngVelMax", ref angVelMax, 0.5f, -3600f, 3600f, "%.2f", "Angular Velocity Max", "particle.angular_velocity_max"))
            {
                particleSpawner.AngularVelocityMaxDegrees = angVelMax;
                ProjectManager.Instance.SetDirty(true);
            }

            float startScaleMin = particleSpawner.StartScaleMin;
            if (InputFloatEditor("Start Scale Min", ref startScaleMin, 0.01f, 0.001f, 1000f, "%.3f"))
            {
                particleSpawner.StartScaleMin = startScaleMin;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.start_scale_min");
                ProjectManager.Instance.SetDirty(true);
            }

            float startScaleMax = particleSpawner.StartScaleMax;
            if (InputFloatEditor("Start Scale Max", ref startScaleMax, 0.01f, 0.001f, 1000f, "%.3f"))
            {
                particleSpawner.StartScaleMax = startScaleMax;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.start_scale_max");
                ProjectManager.Instance.SetDirty(true);
            }

            float endScaleMin = particleSpawner.EndScaleMin;
            if (InputFloatEditor("End Scale Min", ref endScaleMin, 0.01f, 0.001f, 1000f, "%.3f"))
            {
                particleSpawner.EndScaleMin = endScaleMin;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.end_scale_min");
                ProjectManager.Instance.SetDirty(true);
            }

            float endScaleMax = particleSpawner.EndScaleMax;
            if (InputFloatEditor("End Scale Max", ref endScaleMax, 0.01f, 0.001f, 1000f, "%.3f"))
            {
                particleSpawner.EndScaleMax = endScaleMax;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.end_scale_max");
                ProjectManager.Instance.SetDirty(true);
            }

            UiText.Disabled(Localization.Format("text.panel.properties.active_particles", particleSpawner.ActiveParticleCount));

            if (ImGui.Button(Localization.Label("text.panel.properties.restart_particles", "Restart Particles")))
            {
                particleSpawner.ResetRuntime();
            }
        }

        // ── Pivot Offset ──────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader(Localization.Label("text.panel.properties.pivot_offset", "Pivot Offset")))
        {
            bool inheritPivot = _currentObject.InheritPivot;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.inherit_pivot", "Inherit Pivot##pivot"), ref inheritPivot))
                ApplyToSelectedObjects(obj => obj.InheritPivot = inheritPivot);

            vec3 pivot = GetEditablePivotOffset(_currentObject);
            float pivX = pivot.x * 16f;
            float pivY = pivot.y * 16f;
            float pivZ = pivot.z * 16f;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            if (InputFloatEditor("X##pivX", ref pivX, 0.1f, float.MinValue, float.MaxValue))
                ApplyPivotOffset(new vec3(pivX / 16f, pivY / 16f, pivZ / 16f));
            RegisterPropertyContext("pivot.x");

            if (InputFloatEditor("Y##pivY", ref pivY, 0.1f, float.MinValue, float.MaxValue))
                ApplyPivotOffset(new vec3(pivX / 16f, pivY / 16f, pivZ / 16f));
            RegisterPropertyContext("pivot.y");

            if (InputFloatEditor("Z##pivZ", ref pivZ, 0.1f, float.MinValue, float.MaxValue))
                ApplyPivotOffset(new vec3(pivX / 16f, pivY / 16f, pivZ / 16f));
            RegisterPropertyContext("pivot.z");
            ImGui.PopItemWidth();
        }

        if (ImGui.CollapsingHeader(Localization.Label("text.panel.properties.property_inheritance", "Property Inheritance")))
        {
            void InheritanceCheckbox(string label, bool value, Action<SceneObject, bool> apply, bool material = true)
            {
                if (!ImGui.Checkbox(label, ref value)) return;

                ApplyToSelectedObjects(obj =>
                {
                    apply(obj, value);
                    if (material)
                    {
                        obj.UsePropertyInheritance = true;
                        obj.PropagateMaterialSettingsToChildren();
                    }
                });
                ProjectManager.Instance.SetDirty(true);
            }

            if (_currentObject.Parent is MiBoneSceneObject { BendParameters: not null })
            {
                bool internalPart = _currentObject is MiBoneSceneObject;
                ImGui.BeginDisabled(internalPart);
                InheritanceCheckbox(Localization.Label("text.panel.properties.lock_to_bent_half", "Lock to Bent Half"),
                    internalPart ? _currentObject.LockBend > 0f : _currentObject.LockToBentHalf,
                    (obj, value) =>
                    {
                        if (obj is not MiBoneSceneObject)
                            obj.LockToBentHalf = value;
                    }, material: false);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    UiText.Tooltip(Localization.T(internalPart
                        ? "text.panel.properties.lock_to_bent_half_model_help"
                        : "text.panel.properties.lock_to_bent_half_help"));
            }

            InheritanceCheckbox(Localization.Label("text.panel.properties.inherit_alpha", "Inherit Alpha"),
                _currentObject.InheritAlpha, (obj, value) => obj.InheritAlpha = value);
            InheritanceCheckbox(Localization.Label("text.panel.properties.inherit_color", "Inherit Color"),
                _currentObject.InheritColor, (obj, value) => obj.InheritColor = value);
            InheritanceCheckbox(Localization.Label("text.panel.properties.inherit_texture", "Inherit Texture"),
                _currentObject.InheritTexture, (obj, value) => obj.InheritTexture = value);
            InheritanceCheckbox(Localization.Label("text.panel.properties.inherit_surface", "Inherit Surface"),
                _currentObject.InheritSurface, (obj, value) => obj.InheritSurface = value);
            InheritanceCheckbox(Localization.Label("text.panel.properties.inherit_subsurface", "Inherit Subsurface"),
                _currentObject.InheritSubsurface, (obj, value) => obj.InheritSubsurface = value);
            InheritanceCheckbox(Localization.Label("text.panel.properties.inherit_glow_color", "Inherit Glow Color"),
                _currentObject.InheritGlowColor, (obj, value) => obj.InheritGlowColor = value);
            InheritanceCheckbox(Localization.Label("text.panel.properties.inherit_select", "Inherit Selection"),
                _currentObject.InheritSelect, (obj, value) => obj.InheritSelect = value, material: false);
            if (_currentObject is CharacterSceneObject poseModel &&
                poseModel.BoneObjects.Values.Any(bone => bone is MiBoneSceneObject))
            {
                InheritanceCheckbox(Localization.Label("text.panel.properties.inherit_pose", "Inherit Pose"),
                    poseModel.InheritPose, (obj, value) =>
                    {
                        if (obj is CharacterSceneObject model &&
                            model.BoneObjects.Values.Any(bone => bone is MiBoneSceneObject))
                            model.InheritPose = value;
                    }, material: false);
                if (ImGui.IsItemHovered())
                    UiText.Tooltip(Localization.T("text.panel.properties.inherit_pose_help"));
                if (poseModel.Parent is not CharacterSceneObject parentModel ||
                    !parentModel.BoneObjects.Values.Any(bone => bone is MiBoneSceneObject))
                    ImGui.TextWrapped(Localization.T("text.panel.properties.inherit_pose_parent_hint"));
            }
        }

        // ── Material ──────────────────────────────────────────────────────────
        bool isLight = _currentObject is LightSceneObject || _currentObject is CameraSceneObject;
        if (!isLight && ImGui.CollapsingHeader(Localization.Label("text.panel.properties.material", "Material")))
        {
            // Ensure MaterialSettings exists when we need to write
            EnsureMaterialSettings();
            var mat = _currentObject.MaterialSettings;
            var materialObject = _currentObject;

            bool supportsResourcePack = string.Equals(_currentObject.SpawnCategory, "Blocks", StringComparison.Ordinal) ||
                                        string.Equals(_currentObject.SpawnCategory, "Scenery", StringComparison.Ordinal);
            bool supportsItemImage = string.Equals(_currentObject.SpawnCategory, "Items", StringComparison.Ordinal);

            if (SupportsAlbedoTexture(_currentObject))
            {
                DrawPbrMaterialImport();
                RefreshLoadedTextures();
            }
            mat = _currentObject.MaterialSettings;

            if (!string.IsNullOrEmpty(_pbrImportError))
                ImGui.TextWrapped(_pbrImportError);

            if (ImGui.TreeNodeEx(Localization.Label("text.panel.properties.material_base_color_texture", "Base Color & Texture"),
                ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (supportsItemImage)
                {
                    var atlasSource = GetObjectItemAtlasSource(_currentObject);
                    var localItemSheetKeys = atlasSource == ItemAtlasSource.LocalAtlas
                        ? GetLocalItemSheetKeys(_currentObject).ToList()
                        : null;

                    string currentKey = !string.IsNullOrWhiteSpace(_currentObject.ItemTileKey)
                                        ? _currentObject.ItemTileKey
                                        : ExtractItemTileKeyFromObjectType(_currentObject.ObjectType)
                                        ?? (atlasSource == ItemAtlasSource.LocalAtlas
                                            ? localItemSheetKeys?.FirstOrDefault().Key
                                            : GetItemAtlasKeys(atlasSource).FirstOrDefault())
                                        ?? "";

                    string atlasLabel = atlasSource switch
                    {
                        ItemAtlasSource.BlockAtlas => "Block Atlas",
                        ItemAtlasSource.LocalAtlas => "Local Atlas",
                        _ => "Item Atlas"
                    };
                    if (ImGui.BeginCombo(Localization.Label("text.common.item_atlas", "Item Atlas"), Localization.DisplayText(atlasLabel)))
                    {
                        bool useLocal = atlasSource == ItemAtlasSource.LocalAtlas;
                        if (_currentObject.TemporaryItemSheetColumns > 0 && _currentObject.TemporaryItemSheetRows > 0 &&
                            ImGui.Selectable(Localization.Label("text.panel.properties.local_atlas", "Local Atlas"), useLocal))
                        {
                            atlasSource = ItemAtlasSource.LocalAtlas;
                            localItemSheetKeys = GetLocalItemSheetKeys(_currentObject).ToList();
                            currentKey = localItemSheetKeys.FirstOrDefault().Key ?? currentKey;
                        }

                        bool useItem = atlasSource == ItemAtlasSource.ItemAtlas;
                        if (ImGui.Selectable(Localization.Label("text.common.item_atlas", "Item Atlas"), useItem))
                        {
                            atlasSource = ItemAtlasSource.ItemAtlas;
                            _currentObject.TextureType = "item";
                            currentKey = GetItemAtlasKeys(atlasSource).FirstOrDefault() ?? currentKey;
                        }

                        bool useBlock = atlasSource == ItemAtlasSource.BlockAtlas;
                        if (ImGui.Selectable(Localization.Label("text.common.block_atlas", "Block Atlas"), useBlock))
                        {
                            atlasSource = ItemAtlasSource.BlockAtlas;
                            _currentObject.TextureType = "block";
                            currentKey = GetItemAtlasKeys(atlasSource).FirstOrDefault() ?? currentKey;
                        }

                        ImGui.EndCombo();
                    }

                    ImGui.TextUnformatted(Localization.T("text.panel.properties.item_image"));
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.BeginCombo("##ItemImageKey", string.IsNullOrWhiteSpace(currentKey) ? Localization.T("text.panel.properties.none") : currentKey))
                    {
                        if (atlasSource == ItemAtlasSource.LocalAtlas)
                        {
                            localItemSheetKeys ??= GetLocalItemSheetKeys(_currentObject).ToList();
                            foreach (var entry in localItemSheetKeys)
                            {
                                bool selected = string.Equals(entry.Key, currentKey, StringComparison.Ordinal);
                                string label = Localization.Format("text.panel.properties.column_row", entry.Column, entry.Row + 1) + $"###Column {entry.Column}, Row {entry.Row + 1}##{entry.Key}";
                                if (ImGui.Selectable(label, selected))
                                {
                                    _currentObject.TextureType = "local";
                                    if (SpawnMenu != null && SpawnMenu.ApplyTemporaryItemSheetSlotToSpawnedObject(_currentObject, entry.Column, entry.Row))
                                    {
                                        ProjectManager.Instance.SetDirty(true);
                                        currentKey = _currentObject.ItemTileKey;
                                    }
                                }
                                if (selected)
                                    ImGui.SetItemDefaultFocus();
                            }
                        }
                        else
                        {
                            foreach (string key in GetItemAtlasKeys(atlasSource))
                            {
                                bool selected = string.Equals(key, currentKey, StringComparison.Ordinal);
                                if (ImGui.Selectable(key, selected))
                                {
                                    if (SpawnMenu != null && SpawnMenu.ApplyItemTextureToSpawnedObject(_currentObject, atlasSource, key))
                                    {
                                        ProjectManager.Instance.SetDirty(true);
                                        currentKey = key;
                                    }
                                }
                                if (selected)
                                    ImGui.SetItemDefaultFocus();
                            }
                        }

                        ImGui.EndCombo();
                    }

                    if (ImGui.Button(Localization.Label("text.common.load_custom_image", "Load custom image...##ItemMaterialCustom"), new Vector2(-1, 0)))
                    {
                        string? customKey = SpawnMenu?.ImportCustomItemImageFromDialogForProperties();
                        if (!string.IsNullOrWhiteSpace(customKey) &&
                            SpawnMenu != null &&
                            SpawnMenu.ApplyItemTextureToSpawnedObject(_currentObject, ItemAtlasSource.ItemAtlas, customKey))
                        {
                            ProjectManager.Instance.SetDirty(true);
                        }
                    }

                    if (_currentObject.TemporaryItemSheetColumns > 0 && _currentObject.TemporaryItemSheetRows > 0)
                    {
                        ImGui.SeparatorText(Localization.T("text.panel.properties.item_sheet_slot"));

                        int slotColumn = _currentObject.TemporaryItemSheetColumnIndex;
                        int slotRow = _currentObject.TemporaryItemSheetRowIndex;
                        bool slotChanged = false;

                        ImGui.PushItemWidth(-ImGui.CalcTextSize(Localization.T("text.panel.properties.row")).X - ImGui.GetStyle().ItemInnerSpacing.X * 2);

                        if (InputIntEditor("Column##itemSlotColumn", ref slotColumn, 1, 4, 0, _currentObject.TemporaryItemSheetColumns - 1))
                            slotChanged = true;
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            _ctxPropertyPath = "item.slot";
                            _ctxMenuPos = ImGui.GetMousePos();
                            _openPropContextMenu = true;
                        }

                        if (InputIntEditor("Row##itemSlotRow", ref slotRow, 1, 4, 0, _currentObject.TemporaryItemSheetRows - 1))
                            slotChanged = true;
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            _ctxPropertyPath = "item.custom_slot";
                            _ctxMenuPos = ImGui.GetMousePos();
                            _openPropContextMenu = true;
                        }

                        ImGui.PopItemWidth();

                        if (slotChanged)
                            ApplyTemporaryItemSheetSlot(slotColumn, slotRow);
                    }

                    ImGui.Spacing();
                }

                if (supportsResourcePack)
                {
                    string candidateSelection = MinecraftDataLoader.NormalizeResourcePackStack(_currentObject.ResourcePackId);
                    var packIds = MinecraftDataLoader.GetAvailableResourcePackIds().ToList();
                    if (ResourcePackStackSelector.Render("ObjectResourcePacks", ref candidateSelection, "Resource Packs:", packIds) &&
                        SpawnMenu != null && SpawnMenu.ApplyResourcePackToSpawnedObject(_currentObject, candidateSelection))
                    {
                        ProjectManager.Instance.SetDirty(true);
                    }

                    ImGui.Spacing();
                }

                // Albedo texture (for renderable geometry)
                if (SupportsAlbedoTexture(_currentObject))
                {
                    ImGui.TextUnformatted(Localization.T("text.panel.properties.albedo_texture"));

                    uint currentTextureId = GetDisplayedAlbedoTextureId(_currentObject);

                    // Build list of available textures for dropdown
                    string currentLabel = Localization.T("text.panel.properties.none");
                    var cameraTextures = Viewport?.GetSpawnedCamerasPublic() ?? [];
                    var selectedCameraTexture = cameraTextures.FirstOrDefault(camera =>
                        string.Equals(camera.ObjectId, _currentObject.CameraTextureObjectId, StringComparison.Ordinal));
                    if (selectedCameraTexture != null)
                    {
                        currentLabel = selectedCameraTexture.Name;
                    }
                    else if (!string.IsNullOrWhiteSpace(_currentObject.AlbedoTexturePath))
                    {
                        string fullPath = ProjectManager.Instance.ResolveAssetPath(_currentObject.AlbedoTexturePath);
                        currentLabel = Path.GetFileName(fullPath);
                    }
                    else if (currentTextureId != 0)
                    {
                        string? texturePath = MineImatorLoader.Instance.GetTexturePath(currentTextureId);
                        if (texturePath == null) _loadedTexturePathCache.TryGetValue(currentTextureId, out texturePath);
                        currentLabel = Path.GetFileName(texturePath ?? $"Texture_{currentTextureId}");
                    }

                    // Texture dropdown
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.BeginCombo("##AlbedoTextureCombo", currentLabel))
                    {
                        // "None" option
                        bool hasAssignedTexture = selectedCameraTexture != null ||
                                                  currentTextureId != 0 ||
                                                  !string.IsNullOrWhiteSpace(_currentObject.AlbedoTexturePath);
                        if (ImGui.Selectable(Localization.Label("text.panel.properties.none", "(none)"), !hasAssignedTexture))
                        {
                            // Clear texture from all meshes
                            foreach (var mesh in _currentObject.Visuals)
                            {
                                if (mesh.TextureId != 0)
                                {
                                    mesh.TextureId = 0;
                                }
                            }
                            _currentObject.AlbedoTexturePath = "";
                            _currentObject.CameraTextureObjectId = "";
                            _currentObject.RefreshInheritedMaterials();
                            ProjectManager.Instance.SetDirty(true);
                        }

                        // Camera views are named exactly like their scene-tree objects.
                        foreach (var camera in cameraTextures)
                        {
                            bool selected = ReferenceEquals(camera, selectedCameraTexture);
                            if (ImGui.Selectable(camera.Name, selected))
                            {
                                _currentObject.CameraTextureObjectId = camera.ObjectId;
                                _currentObject.AlbedoTexturePath = "";
                                ProjectManager.Instance.SetDirty(true);
                            }
                            if (selected) ImGui.SetItemDefaultFocus();
                        }

                        if (cameraTextures.Count > 0)
                            ImGui.Separator();

                        // Show all loaded/imported textures as options
                        foreach (var (texId, path) in _loadedTexturePathCache)
                        {
                            if (texId == 0) continue;
                            string label = Path.GetFileName(path);
                            bool selected = (currentTextureId == texId);
                            if (ImGui.Selectable(label, selected))
                            {
                                _currentObject.CameraTextureObjectId = "";
                                // Check if this is an actual loaded texture or just an imported file
                                bool isLoadedTexture = false;
                                foreach (var mesh in _currentObject.Visuals)
                                {
                                    if (mesh.TextureId == texId)
                                    {
                                        isLoadedTexture = true;
                                        break;
                                    }
                                }

                                if (isLoadedTexture)
                                {
                                    // Already loaded, just set it
                                    foreach (var mesh in _currentObject.Visuals)
                                    {
                                        mesh.TextureId = texId;
                                    }
                                    if (!string.IsNullOrEmpty(path))
                                    {
                                        _currentObject.AlbedoTexturePath = ProjectManager.Instance.ToProjectRelativePath(path);
                                    }
                                }
                                else
                                {
                                    // File hasn't been loaded yet, load it now
                                    if (File.Exists(path))
                                    {
                                        OnLoadAlbedoTextureForObject(_currentObject, path);
                                    }
                                }
                                ProjectManager.Instance.SetDirty(true);
                            }
                            if (selected)
                                ImGui.SetItemDefaultFocus();
                        }

                        ImGui.EndCombo();
                    }

                    if (ImGui.Button(Localization.Label("text.panel.properties.load_new_texture", "Load new texture...##AlbedoTexture"), new Vector2(-1, 0)))
                    {
                        var result = Dialog.FileOpen("png,jpg,jpeg,bmp,tga,gif,hdr,exr");
                        if (result.IsOk && !string.IsNullOrWhiteSpace(result.Path) && File.Exists(result.Path))
                        {
                            string resolvedPath = ResolveAlbedoTexturePathForProject(result.Path);
                            OnLoadAlbedoTextureForObject(_currentObject, resolvedPath);
                            _currentObject.CameraTextureObjectId = "";
                            ProjectManager.Instance.SetDirty(true);
                        }
                    }

                    if ((currentTextureId != 0 || !string.IsNullOrEmpty(_currentObject.AlbedoTexturePath) ||
                         !string.IsNullOrEmpty(_currentObject.CameraTextureObjectId)) &&
                        ImGui.Button(Localization.Label("text.panel.properties.clear_texture", "Clear texture##AlbedoTextureClear"), new Vector2(-1, 0)))
                    {
                        foreach (var mesh in _currentObject.Visuals.Where(mesh => mesh.TextureId != 0))
                        {
                            mesh.TextureId = 0;
                        }
                        _currentObject.AlbedoTexturePath = "";
                        _currentObject.CameraTextureObjectId = "";
                        _currentObject.RefreshInheritedMaterials();
                        ProjectManager.Instance.SetDirty(true);
                    }

                    ImGui.Spacing();
                }

                // Alpha – skip for BoneSceneObject
                if (_currentObject is not BoneSceneObject)
                {
                    float alpha = mat?.AlbedoColor.a ?? 1f;
                    ImGui.SetNextItemWidth(-60f);
                    if (ImGui.SliderFloat(Localization.Label("text.panel.properties.material_opacity", "Alpha"), ref alpha, 0f, 1f))
                    {
                        EnsureMaterialSettings();
                        var c = _currentObject.MaterialSettings.AlbedoColor;
                        _currentObject.MaterialSettings.AlbedoColor = new vec4(c.r, c.g, c.b, alpha);
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                        Timeline?.RecordAutoKeyframe(_currentObject, "material.alpha");
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _ctxPropertyPath = "material.alpha";
                        _ctxMenuPos = ImGui.GetMousePos();
                        _openPropContextMenu = true;
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted(alpha.ToString("F2"));
                }

                // Albedo color
                {
                    vec4 ac = mat?.AlbedoColor ?? new vec4(1f, 1f, 1f, 1f);
                    var vec4 = new Vector4(ac.r, ac.g, ac.b, ac.a);
                    if (ImGui.ColorEdit4(Localization.Label("text.panel.properties.material_base_color", "Albedo"), ref vec4, ImGuiColorEditFlags.NoInputs))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.AlbedoColor = new vec4(vec4.X, vec4.Y, vec4.Z, vec4.W);
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }
                    RegisterPropertyContext("material.albedo", () => materialObject.MaterialSettings.AlbedoColor, value =>
                    {
                        materialObject.SetExplicitMaterialSettings();
                        var color = (vec4)value;
                        materialObject.MaterialSettings.AlbedoColor = new vec4(
                            Math.Clamp(color.x, 0f, 1f), Math.Clamp(color.y, 0f, 1f),
                            Math.Clamp(color.z, 0f, 1f), Math.Clamp(color.w, 0f, 1f));
                        materialObject.SetExplicitMaterialSettings();
                        materialObject.PropagateMaterialSettingsToChildren();
                    }, new vec4(1f, 1f, 1f, 1f));
                }

                // Mine-imator-style multiplicative blend colour.
                {
                    vec4 bc = mat?.BlendColor ?? new vec4(1f, 1f, 1f, 1f);
                    var color = new Vector3(bc.r, bc.g, bc.b);
                    if (ImGui.ColorEdit3(Localization.Label("text.panel.properties.blend_color", "Blend Color"), ref color, ImGuiColorEditFlags.NoInputs))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.BlendColor = new vec4(color.X, color.Y, color.Z, 1f);
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }
                    RegisterPropertyContext("material.blend.r", "material.blend.g", "material.blend.b");
                }

                // Mix colour uses its alpha channel as the interpolation amount.
                {
                    vec4 mc = mat?.MixColor ?? new vec4(0f, 0f, 0f, 0f);
                    var color = new Vector3(mc.r, mc.g, mc.b);
                    if (ImGui.ColorEdit3(Localization.Label("text.panel.properties.mix_color", "Mix Color"), ref color, ImGuiColorEditFlags.NoInputs))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.MixColor = new vec4(color.X, color.Y, color.Z, mc.a);
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }
                    RegisterPropertyContext("material.mix.r", "material.mix.g", "material.mix.b");

                    float mixAmount = mc.a;
                    if (ImGui.SliderFloat(Localization.Label("text.panel.properties.mix_amount", "Mix Amount"), ref mixAmount, 0f, 1f))
                    {
                        EnsureMaterialSettings();
                        var c = _currentObject.MaterialSettings.MixColor;
                        _currentObject.MaterialSettings.MixColor = new vec4(c.r, c.g, c.b, mixAmount);
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }
                    RegisterPropertyContext("material.mix_percent");
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx(Localization.Label("text.panel.properties.material_surface", "Surface"),
                ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Metallic
                {
                    float metallic = mat?.Metallic ?? 0f;
                    if (ImGui.SliderFloat(Localization.Label("text.panel.properties.metallic", "Metallic"), ref metallic, 0f, 1f))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.Metallic = metallic;
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }
                    RegisterPropertyContext("material.metallic");
                }

                // Roughness
                {
                    float roughness = mat?.Roughness ?? 0.5f;
                    if (ImGui.SliderFloat(Localization.Label("text.panel.properties.roughness", "Roughness"), ref roughness, 0f, 1f))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.Roughness = roughness;
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }
                    RegisterPropertyContext("material.roughness");
                }

                // Alpha Mode
                {
                    int alphaMode = (int)(mat?.AlphaMode ?? MaterialAlphaMode.Blend);
                    string[] alphaModes = [
                        Localization.Label("text.panel.properties.alpha_blend", "Alpha Blend"),
                        Localization.Label("text.panel.properties.alpha_clip", "Alpha Clip")
                    ];
                    if (ImGui.Combo(Localization.Label("text.panel.properties.alpha_mode", "Alpha Mode"), ref alphaMode,
                            alphaModes, alphaModes.Length))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.AlphaMode = (MaterialAlphaMode)alphaMode;
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }

                    if (alphaMode == (int)MaterialAlphaMode.Clip)
                    {
                        float threshold = mat?.AlphaClipThreshold ?? 0.5f;
                        if (ImGui.SliderFloat(Localization.Label("text.panel.properties.alpha_clip_threshold", "Clip Threshold"),
                                ref threshold, 0f, 1f))
                        {
                            EnsureMaterialSettings();
                            _currentObject.MaterialSettings.AlphaClipThreshold = threshold;
                            _currentObject.SetExplicitMaterialSettings();
                            _currentObject.PropagateMaterialSettingsToChildren();
                        }
                    }
                }

                // Double Sided
                {
                    bool doubleSided = mat?.DoubleSided ?? false;
                    if (ImGui.Checkbox(Localization.Label("text.panel.properties.double_sided", "Double Sided"), ref doubleSided))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.DoubleSided = doubleSided;
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }
                    RegisterPropertyContext("material.double_sided", () => materialObject.MaterialSettings.DoubleSided, value =>
                    {
                        materialObject.SetExplicitMaterialSettings();
                        materialObject.MaterialSettings.DoubleSided = (bool)value;
                        materialObject.SetExplicitMaterialSettings();
                        materialObject.PropagateMaterialSettingsToChildren();
                    }, false);
                }

                ImGui.TreePop();
            }

            if (SupportsAlbedoTexture(_currentObject) &&
                ImGui.TreeNodeEx(Localization.Label("text.panel.properties.material_surface_maps", "Surface Maps"), ImGuiTreeNodeFlags.SpanAvailWidth))
            {
                DrawPbrSurfaceControls();
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx(Localization.Label("text.common.emission", "Material Emission"), ImGuiTreeNodeFlags.SpanAvailWidth))
            {
                // Emission enabled
                {
                    bool emissionEnabled = mat?.EmissionEnabled ?? false;
                    if (ImGui.Checkbox(Localization.Label("text.panel.properties.material_emission_enabled", "Emission"), ref emissionEnabled))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.EmissionEnabled = emissionEnabled;
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }
                    RegisterPropertyContext("material.emission_enabled", () => materialObject.MaterialSettings.EmissionEnabled, value =>
                    {
                        materialObject.SetExplicitMaterialSettings();
                        materialObject.MaterialSettings.EmissionEnabled = (bool)value;
                        materialObject.SetExplicitMaterialSettings();
                        materialObject.PropagateMaterialSettingsToChildren();
                    }, false);
                }

                // Auto emission from Minecraft block metadata (schema/block imports)
                {
                    bool autoEmission = mat?.AutoEmission ?? true;
                    if (ImGui.Checkbox(Localization.Label("text.panel.properties.auto_emission", "Auto Emission"), ref autoEmission))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.AutoEmission = autoEmission;
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();

                        // Rebuild spawned Minecraft-derived meshes so merged schematic
                        // buckets and per-mesh auto-emission levels are refreshed.
                        if (SpawnMenu != null)
                        {
                            if (string.Equals(_currentObject.SpawnCategory, "Blocks", StringComparison.Ordinal))
                            {
                                SpawnMenu.RebuildBlockMeshes(_currentObject);
                            }
                            else if (string.Equals(_currentObject.SpawnCategory, "Scenery", StringComparison.Ordinal) &&
                                     !string.IsNullOrWhiteSpace(_currentObject.SourceAssetPath))
                            {
                                SpawnMenu.ApplyResourcePackToSpawnedObject(_currentObject, _currentObject.ResourcePackId);
                            }
                        }
                    }
                    RegisterPropertyContext("material.auto_emission", () => materialObject.MaterialSettings.AutoEmission, value =>
                    {
                        materialObject.SetExplicitMaterialSettings();
                        materialObject.MaterialSettings.AutoEmission = (bool)value;
                        materialObject.SetExplicitMaterialSettings();
                        materialObject.PropagateMaterialSettingsToChildren();
                        if (SpawnMenu != null)
                        {
                            if (string.Equals(materialObject.SpawnCategory, "Blocks", StringComparison.Ordinal))
                                SpawnMenu.RebuildBlockMeshes(materialObject);
                            else if (string.Equals(materialObject.SpawnCategory, "Scenery", StringComparison.Ordinal) &&
                                     !string.IsNullOrWhiteSpace(materialObject.SourceAssetPath))
                                SpawnMenu.ApplyResourcePackToSpawnedObject(materialObject, materialObject.ResourcePackId);
                        }
                    }, true);
                }

                // Emission color (no alpha)
                if (mat?.EmissionEnabled == true)
                {
                    vec4 ec = mat?.EmissionColor ?? new vec4(0f, 0f, 0f, 1f);
                    var vec3 = new Vector3(ec.r, ec.g, ec.b);
                    if (ImGui.ColorEdit3(Localization.Label("text.panel.properties.emission_color", "Emission Color"), ref vec3, ImGuiColorEditFlags.NoInputs))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.EmissionColor = new vec4(vec3.X, vec3.Y, vec3.Z, 1f);
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }
                    RegisterPropertyContext("material.glow.r", "material.glow.g", "material.glow.b");
                }

                // Emission energy
                if (mat?.EmissionEnabled == true)
                {
                    float emEnergy = mat?.EmissionEnergy ?? 1f;
                    if (ImGui.SliderFloat(Localization.Label("text.panel.properties.emission_energy", "Emission Energy"), ref emEnergy, 0f, 10f))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.EmissionEnergy = emEnergy;
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }
                    RegisterPropertyContext("material.emission_energy", () => materialObject.MaterialSettings.EmissionEnergy, value =>
                    {
                        materialObject.SetExplicitMaterialSettings();
                        materialObject.MaterialSettings.EmissionEnergy = Math.Clamp((float)value, 0f, 10f);
                        materialObject.SetExplicitMaterialSettings();
                        materialObject.PropagateMaterialSettingsToChildren();
                    }, 1f);
                }

                // Emission indirect-only toggle
                if (mat?.EmissionEnabled == true || (mat?.AutoEmission ?? true))
                {
                    bool emissionIndirectOnly = mat?.EmissionIndirectOnly ?? false;
                    if (ImGui.Checkbox(Localization.Label("text.panel.properties.emission_indirect_only", "Emission Indirect Only"), ref emissionIndirectOnly))
                    {
                        EnsureMaterialSettings();
                        _currentObject.MaterialSettings.EmissionIndirectOnly = emissionIndirectOnly;
                        _currentObject.SetExplicitMaterialSettings();
                        _currentObject.PropagateMaterialSettingsToChildren();
                    }
                    RegisterPropertyContext("material.emission_indirect_only", () => materialObject.MaterialSettings.EmissionIndirectOnly, value =>
                    {
                        materialObject.SetExplicitMaterialSettings();
                        materialObject.MaterialSettings.EmissionIndirectOnly = (bool)value;
                        materialObject.SetExplicitMaterialSettings();
                        materialObject.PropagateMaterialSettingsToChildren();
                    }, false);
                }

                if (SupportsAlbedoTexture(_currentObject))
                    DrawAuxiliaryTextureSelector(false);

                ImGui.TreePop();
            }

            // Subsurface scattering controls (inspired by Mine-imator high-light shaders).
            if (ImGui.TreeNodeEx(Localization.Label("text.panel.properties.material_subsurface_scattering", "Subsurface Scattering"), ImGuiTreeNodeFlags.SpanAvailWidth))
            {
                float sss = mat?.Subsurface ?? 0f;
                if (ImGui.DragFloat(Localization.Label("text.panel.properties.subsurface", "Subsurface"), ref sss, 0.1f, 0f, float.MaxValue, "%.2f px"))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.Subsurface = Math.Max(sss, 0f);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
                RegisterPropertyContext("material.subsurface");

                vec3 sssRadius = mat?.SubsurfaceRadius ?? vec3.Ones;
                var sssRadiusVec = new Vector3(sssRadius.x, sssRadius.y, sssRadius.z);
                if (ImGui.DragFloat3(Localization.Label("text.panel.properties.subsurface_radius_rgb", "Subsurface Radius RGB"), ref sssRadiusVec, 0.01f, 0f, 4f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.SubsurfaceRadius = new vec3(
                        Math.Max(0f, sssRadiusVec.X),
                        Math.Max(0f, sssRadiusVec.Y),
                        Math.Max(0f, sssRadiusVec.Z));
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
                RegisterPropertyContext("material.subsurface_radius.r", "material.subsurface_radius.g", "material.subsurface_radius.b");

                vec4 sssColor = mat?.SubsurfaceColor ?? new vec4(1f, 1f, 1f, 1f);
                var sssColorVec = new Vector3(sssColor.r, sssColor.g, sssColor.b);
                if (ImGui.ColorEdit3(Localization.Label("text.panel.properties.subsurface_color", "Subsurface Color"), ref sssColorVec, ImGuiColorEditFlags.NoInputs))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.SubsurfaceColor = new vec4(sssColorVec.X, sssColorVec.Y, sssColorVec.Z, 1f);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
                RegisterPropertyContext("material.subsurface_color.r", "material.subsurface_color.g", "material.subsurface_color.b");

                float sssHighlight = mat?.SubsurfaceHighlight ?? 0.35f;
                if (ImGui.SliderFloat(Localization.Label("text.panel.properties.subsurface_highlight", "Subsurface Highlight"), ref sssHighlight, -0.95f, 0.95f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.SubsurfaceHighlight = sssHighlight;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
                RegisterPropertyContext("material.subsurface_highlight", () => materialObject.MaterialSettings.SubsurfaceHighlight, value =>
                {
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.MaterialSettings.SubsurfaceHighlight = Math.Clamp((float)value, -0.95f, 0.95f);
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.PropagateMaterialSettingsToChildren();
                }, 0.35f);

                float sssHighlightStrength = mat?.SubsurfaceHighlightStrength ?? 0.6f;
                if (ImGui.SliderFloat(Localization.Label("text.panel.properties.subsurface_highlight_strength", "Subsurface Highlight Strength"), ref sssHighlightStrength, 0f, 4f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.SubsurfaceHighlightStrength = sssHighlightStrength;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
                RegisterPropertyContext("material.subsurface_highlight_strength", () => materialObject.MaterialSettings.SubsurfaceHighlightStrength, value =>
                {
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.MaterialSettings.SubsurfaceHighlightStrength = Math.Clamp((float)value, 0f, 4f);
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.PropagateMaterialSettingsToChildren();
                }, 0.6f);
                ImGui.TreePop();
            }

            // Texture UV transform
            if (ImGui.TreeNodeEx(Localization.Label("text.panel.properties.material_texture_mapping", "Texture Mapping"), ImGuiTreeNodeFlags.SpanAvailWidth))
            {
                vec2 uvOffset = mat?.TextureOffset ?? vec2.Zero;
                var offset = new Vector2(uvOffset.x, uvOffset.y);
                if (ImGui.DragFloat2(Localization.Label("text.panel.properties.texture_offset_hv", "Texture Offset HV"), ref offset, 0.01f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.TextureOffset = new vec2(offset.X, offset.Y);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
                RegisterPropertyContext("material.texture_offset", () => materialObject.MaterialSettings.TextureOffset, value =>
                {
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.MaterialSettings.TextureOffset = (vec2)value;
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.PropagateMaterialSettingsToChildren();
                }, vec2.Zero);

                vec2 uvRepeat = mat?.TextureRepeat ?? new vec2(1f, 1f);
                var repeat = new Vector2(uvRepeat.x, uvRepeat.y);
                if (ImGui.DragFloat2(Localization.Label("text.panel.properties.texture_repeat_hv", "Texture Repeat HV"), ref repeat, 0.01f, 0.0001f, 256f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.TextureRepeat = new vec2(Math.Max(0.0001f, repeat.X), Math.Max(0.0001f, repeat.Y));
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
                RegisterPropertyContext("material.texture_repeat", () => materialObject.MaterialSettings.TextureRepeat, value =>
                {
                    var textureRepeat = (vec2)value;
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.MaterialSettings.TextureRepeat = new vec2(
                        Math.Clamp(textureRepeat.x, 0.0001f, 256f), Math.Clamp(textureRepeat.y, 0.0001f, 256f));
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.PropagateMaterialSettingsToChildren();
                }, new vec2(1f, 1f));

                bvec2 uvMirror = mat?.TextureMirror ?? new bvec2(false, false);
                bool mirrorH = uvMirror.x;
                bool mirrorV = uvMirror.y;
                bool mirrorChanged = false;

                if (ImGui.Checkbox(Localization.Label("text.panel.properties.texture_mirror_h", "Texture Mirror H"), ref mirrorH))
                    mirrorChanged = true;
                RegisterPropertyContext("material.texture_mirror_h", () => materialObject.MaterialSettings.TextureMirror.x, value =>
                {
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.MaterialSettings.TextureMirror = new bvec2((bool)value, materialObject.MaterialSettings.TextureMirror.y);
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.PropagateMaterialSettingsToChildren();
                }, false);

                if (ImGui.Checkbox(Localization.Label("text.panel.properties.texture_mirror_v", "Texture Mirror V"), ref mirrorV))
                    mirrorChanged = true;
                RegisterPropertyContext("material.texture_mirror_v", () => materialObject.MaterialSettings.TextureMirror.y, value =>
                {
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.MaterialSettings.TextureMirror = new bvec2(materialObject.MaterialSettings.TextureMirror.x, (bool)value);
                    materialObject.SetExplicitMaterialSettings();
                    materialObject.PropagateMaterialSettingsToChildren();
                }, false);

                if (mirrorChanged)
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.TextureMirror = new bvec2(mirrorH, mirrorV);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
                ImGui.TreePop();
            }

        }

        // ── Shape Keys ────────────────────────────────────────────────────────
        if (!isLight && HasAnyShapeKeys(_currentObject) && ImGui.CollapsingHeader(Localization.Label("text.panel.properties.shape_keys", "Shape Keys")))
        {
            RenderShapeKeysSection(_currentObject);
        }

        // ── Light (shown only for LightSceneObject) ───────────────────────────
        if (_currentObject is LightSceneObject light)
        {
            if (ImGui.CollapsingHeader(Localization.Label("text.common.light", "Light")))
            {
                // Light type (point / spot)
                {
                    string[] typeOptions = { Localization.T("text.panel.properties.point"), Localization.T("text.panel.properties.spot") };
                    int currentType = (int)light.Type;
                    if (ImGui.Combo(Localization.Label("text.panel.properties.type", "Type##lightType"), ref currentType, typeOptions, typeOptions.Length))
                    {
                        light.Type = (LightType)currentType;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.type");
                    }
                    RegisterPropertyContext("light.type", () => light.Type, value => light.Type = (LightType)value, LightType.Point);
                }

                // Color
                {
                    var lc   = light.LightColor;
                    var vec3 = new Vector3(lc.r, lc.g, lc.b);
                    if (ImGui.ColorEdit3(Localization.Label("text.common.color", "Color##lightColor"), ref vec3, ImGuiColorEditFlags.NoInputs))
                    {
                        light.LightColor = new vec4(vec3.X, vec3.Y, vec3.Z, 1);
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.color.r");
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.color.g");
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.color.b");
                    }
                    RegisterPropertyContext("light.color.r", "light.color.g", "light.color.b");
                }

                // Energy
                {
                    float energy = light.LightEnergy;
                    if (InputFloatEditor("Energy##lightEnergy", ref energy, 0.05f, 0f, 100f))
                    {
                        light.LightEnergy = energy;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.energy");
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _ctxPropertyPath = "light.energy";
                        _ctxMenuPos = ImGui.GetMousePos();
                        _openPropContextMenu = true;
                    }
                }

                // Range
                {
                    float range = light.LightRange;
                    if (InputFloatEditor("Range##lightRange", ref range, 0.1f, 0.01f, 500f))
                    {
                        light.LightRange = range;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.range");
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _ctxPropertyPath = "light.range";
                        _ctxMenuPos = ImGui.GetMousePos();
                        _openPropContextMenu = true;
                    }
                }

                // Indirect Energy
                {
                    float indirect = light.LightIndirectEnergy;
                    if (InputFloatEditor("Indirect Energy##lightIndirect", ref indirect, 0.05f, 0f, 16f))
                    {
                        light.LightIndirectEnergy = indirect;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.indirect_energy");
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _ctxPropertyPath = "light.indirect_energy";
                        _ctxMenuPos = ImGui.GetMousePos();
                        _openPropContextMenu = true;
                    }
                }

                // Specular
                {
                    float specular = light.LightSpecular;
                    if (ImGui.SliderFloat(Localization.Label("text.common.specular", "Specular##lightSpecular"), ref specular, 0f, 1f))
                    {
                        light.LightSpecular = specular;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.specular");
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _ctxPropertyPath = "light.specular";
                        _ctxMenuPos = ImGui.GetMousePos();
                        _openPropContextMenu = true;
                    }
                }

                // Cast Shadows
                {
                    bool shadow = light.LightShadowEnabled;
                    if (ImGui.Checkbox(Localization.Label("text.panel.properties.cast_shadows", "Cast Shadows##lightShadow"), ref shadow))
                        light.LightShadowEnabled = shadow;
                    RegisterPropertyContext("light.shadow_enabled", () => light.LightShadowEnabled,
                        value => light.LightShadowEnabled = (bool)value, true);
                }

                // Spot-only properties
                if (light.Type == LightType.Spot)
                {
                    float angle = light.LightSpotAngle;
                    if (ImGui.SliderFloat(Localization.Label("text.common.spot_angle", "Spot Angle##lightSpotAngle"), ref angle, 1f, 170f))
                    {
                        light.LightSpotAngle = angle;
                        light.LightSpotBlend = Math.Min(light.LightSpotBlend, angle * 0.5f);
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.spot_angle");
                    }
                    RegisterPropertyContext("light.spot_angle");

                    float blend = light.LightSpotBlend;
                    float maxBlend = Math.Max(0f, light.LightSpotAngle * 0.5f);
                    if (ImGui.SliderFloat(Localization.Label("text.common.spot_blend", "Spot Blend##lightSpotBlend"), ref blend, 0f, maxBlend))
                    {
                        light.LightSpotBlend = blend;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.spot_blend");
                    }
                    RegisterPropertyContext("light.spot_blend");
                }
            }
        }

        DrawConstraints(_currentObject);

        // ── Effects (shown only for CameraSceneObject) ────────────────────────
        if (_currentObject is CameraSceneObject cameraObject)
        {
            if (ImGui.CollapsingHeader(Localization.Label("text.panel.properties.effects", "Effects")))
            {
                float addButtonSize = ImGui.GetFrameHeight();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - addButtonSize));
                if (ImGui.Button("+##cameraEffectAddButton", new Vector2(addButtonSize)))
                    ImGui.OpenPopup("##cameraEffectAddMenu");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Localization.T("text.panel.properties.add_effect"));

                if (ImGui.BeginPopup("##cameraEffectAddMenu"))
                {
                    for (int i = 0; i < CameraEffectAddOptions.Length; i++)
                    {
                        if (ImGui.MenuItem(Localization.DisplayLabel(CameraEffectAddOptions[i])))
                        {
                            cameraObject.AddEffect((CameraEffectType)i);
                            ProjectManager.Instance.SetDirty(true);
                        }
                    }
                    ImGui.EndPopup();
                }

                ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f * UiTheme.Scale);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f) * UiTheme.Scale);
                bool effectsVisible = ImGui.BeginChild("##cameraEffectList", Vector2.Zero,
                    ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AlwaysUseWindowPadding);
                if (effectsVisible && cameraObject.Effects.Count == 0)
                {
                    UiText.Disabled(Localization.T("text.panel.properties.no_effects_added"));
                }
                else if (effectsVisible)
                {
                    int removeIndex = -1;

                    for (int i = 0; i < cameraObject.Effects.Count; i++)
                    {
                        if (i > 0)
                        {
                            ImGui.Spacing();
                            ImGui.Separator();
                            ImGui.Spacing();
                        }

                        var effect = cameraObject.Effects[i];
                        string title = effect.Type switch
                        {
                            CameraEffectType.CameraShake => Localization.T("text.panel.properties.camera_shake") + $"###Camera Shake##cameraEffect{i}",
                            CameraEffectType.FilmGrain => Localization.T("text.panel.properties.film_grain") + $"###Film Grain##cameraEffect{i}",
                            CameraEffectType.DepthOfField => Localization.T("text.panel.properties.depth_of_field") + $"###Depth of Field##cameraEffect{i}",
                            _ => Localization.Format("text.panel.properties.effect", i + 1) + $"###Effect {i + 1}##cameraEffect{i}"
                        };

                        if (ImGui.TreeNodeEx(title, ImGuiTreeNodeFlags.DefaultOpen))
                        {
                            ImGui.SameLine();
                            if (ImGui.SmallButton(Localization.T("text.common.remove") + $"###Remove##cameraEffectRemove{i}"))
                                removeIndex = i;

                            if (effect.Type == CameraEffectType.CameraShake)
                            {
                                bool changed = false;
                                string basePath = $"camera.effect.{i}.shake";
                                int mode = (int)effect.Shake.Mode;
                                if (ImGui.Combo(Localization.T("text.common.mode") + $"###Mode##cameraShakeMode{i}", ref mode, CameraShakeModeOptions.Select(Localization.DisplayText).ToArray(), CameraShakeModeOptions.Length))
                                {
                                    effect.Shake.Mode = (CameraShakeMode)Math.Clamp(mode, 0, CameraShakeModeOptions.Length - 1);
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.mode");
                                    changed = true;
                                }
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                {
                                    _ctxPropertyPath = $"{basePath}.mode";
                                    _ctxMenuPos = ImGui.GetMousePos();
                                    _openPropContextMenu = true;
                                }

                                float trauma = effect.Shake.Trauma;
                                if (ImGui.SliderFloat(Localization.T("text.common.trauma") + $"###Trauma##cameraShakeTrauma{i}", ref trauma, 0f, 5f, "%.3f"))
                                {
                                    effect.Shake.Trauma = trauma;
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.trauma");
                                    changed = true;
                                }
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                {
                                    _ctxPropertyPath = $"{basePath}.trauma";
                                    _ctxMenuPos = ImGui.GetMousePos();
                                    _openPropContextMenu = true;
                                }

                                vec3 strength = effect.Shake.Strength;
                                if (EditVec3Editor(cameraObject, $"shakeStrength{i}", ref strength, 0.001f, -100f, 100f, "%.3f", "Strength", $"{basePath}.strength"))
                                {
                                    effect.Shake.Strength = strength;
                                    changed = true;
                                }

                                vec3 speed = effect.Shake.Speed;
                                if (EditVec3Editor(cameraObject, $"shakeSpeed{i}", ref speed, 0.01f, -200f, 200f, "%.3f", "Speed", $"{basePath}.speed"))
                                {
                                    effect.Shake.Speed = speed;
                                    changed = true;
                                }

                                vec3 offset = effect.Shake.Offset;
                                if (EditVec3Editor(cameraObject, $"shakeOffset{i}", ref offset, 0.01f, -1000f, 1000f, "%.3f", "Offset", $"{basePath}.offset"))
                                {
                                    effect.Shake.Offset = offset;
                                    changed = true;
                                }

                                if (changed)
                                    ProjectManager.Instance.SetDirty(true);
                            }
                            else if (effect.Type == CameraEffectType.FilmGrain)
                            {
                                bool changed = false;
                                string basePath = $"camera.effect.{i}.film_grain";
                                float strength = effect.FilmGrain.Strength;
                                if (ImGui.SliderFloat(Localization.T("text.common.strength") + $"###Strength##filmGrainStrength{i}", ref strength, 0f, 1f, "%.3f"))
                                {
                                    effect.FilmGrain.Strength = strength;
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.strength");
                                    changed = true;
                                }
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                {
                                    _ctxPropertyPath = $"{basePath}.strength";
                                    _ctxMenuPos = ImGui.GetMousePos();
                                    _openPropContextMenu = true;
                                }
                                float saturation = effect.FilmGrain.Saturation;
                                if (ImGui.SliderFloat(Localization.T("text.common.saturation") + $"###Saturation##filmGrainSaturation{i}", ref saturation, 0f, 1f, "%.3f"))
                                {
                                    effect.FilmGrain.Saturation = saturation;
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.saturation");
                                    changed = true;
                                }
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                {
                                    _ctxPropertyPath = $"{basePath}.saturation";
                                    _ctxMenuPos = ImGui.GetMousePos();
                                    _openPropContextMenu = true;
                                }
                                float size = effect.FilmGrain.Size;
                                if (ImGui.SliderFloat(Localization.T("text.common.size") + $"###Size##filmGrainSize{i}", ref size, 0.25f, 8f, "%.2f"))
                                {
                                    effect.FilmGrain.Size = size;
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.size");
                                    changed = true;
                                }
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                {
                                    _ctxPropertyPath = $"{basePath}.size";
                                    _ctxMenuPos = ImGui.GetMousePos();
                                    _openPropContextMenu = true;
                                }
                                if (changed) ProjectManager.Instance.SetDirty(true);
                            }

                            if (effect.Type == CameraEffectType.DepthOfField)
                            {
                                ImGui.PushID(i);
                                string basePath = $"camera.effect.{i}.depth_of_field";
                                if (ImGui.Button(Localization.Label("text.panel.properties.focus_foreground", "Focus Foreground")))
                                    Viewport?.RequestDepthOfFieldFocus(cameraObject, effect, DepthOfFieldFocusTarget.Foreground);
                                ImGui.SameLine();
                                if (ImGui.Button(Localization.Label("text.panel.properties.focus_background", "Focus Background")))
                                    Viewport?.RequestDepthOfFieldFocus(cameraObject, effect, DepthOfFieldFocusTarget.Background);
                                if (ImGui.Button(Localization.Label("text.panel.properties.pick_focus_point", "Pick Focus Point")))
                                    Viewport?.RequestDepthOfFieldFocus(cameraObject, effect, DepthOfFieldFocusTarget.PickPoint);
                                if (Viewport?.IsDepthOfFieldFocusPending(cameraObject, effect) == true)
                                {
                                    if (ImGui.Button(Localization.Label("text.panel.properties.cancel_focus_selection", "Cancel Focus Selection")))
                                        Viewport.CancelDepthOfFieldFocus();
                                }
                                if (!string.IsNullOrEmpty(Viewport?.DepthOfFieldFocusMessage))
                                    UiText.Wrapped(Localization.DisplayText(Viewport.DepthOfFieldFocusMessage));

                                foreach (var control in DepthOfFieldSettings.Controls)
                                {
                                    if (!effect.DepthOfField.ColorFringe &&
                                        control.Key is "red_angle" or "green_angle" or "blue_angle" or "red_offset" or "green_offset" or "blue_offset")
                                        continue;

                                    float value = effect.DepthOfField.GetValue(control.Key);
                                    Vector4? channelColor = control.Key switch
                                    {
                                        "red_angle" or "red_offset" => new Vector4(0.90f, 0.25f, 0.25f, 1f),
                                        "green_angle" or "green_offset" => new Vector4(0.20f, 0.75f, 0.35f, 1f),
                                        "blue_angle" or "blue_offset" => new Vector4(0.25f, 0.50f, 0.95f, 1f),
                                        _ => null
                                    };
                                    if (channelColor is Vector4 tint)
                                    {
                                        var colors = ImGui.GetStyle().Colors;
                                        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Lerp(colors[(int)ImGuiCol.FrameBg], tint, 0.25f));
                                        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Lerp(colors[(int)ImGuiCol.FrameBgHovered], tint, 0.35f));
                                        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Lerp(colors[(int)ImGuiCol.FrameBgActive], tint, 0.45f));
                                        ImGui.PushStyleColor(ImGuiCol.SliderGrab, tint);
                                        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Vector4.Lerp(tint, Vector4.One, 0.20f));
                                    }
                                    bool changed;
                                    if (control.Key == "color_fringe")
                                    {
                                        bool enabled = effect.DepthOfField.ColorFringe;
                                        changed = ImGui.Checkbox(Localization.DisplayLabel(control.Label), ref enabled);
                                        value = enabled ? 1f : 0f;
                                    }
                                    else if (control.Key is "depth" or "focus_range" or "fade_size")
                                        changed = ImGui.DragFloat(Localization.DisplayLabel(control.Label), ref value, 0.05f, control.Min, control.Max, "%.3f", ImGuiSliderFlags.AlwaysClamp);
                                    else
                                        changed = ImGui.SliderFloat(Localization.DisplayLabel(control.Label), ref value, control.Min, control.Max, "%.2f", ImGuiSliderFlags.AlwaysClamp);

                                    if (channelColor.HasValue)
                                        ImGui.PopStyleColor(5);

                                    if (changed)
                                    {
                                        effect.DepthOfField.SetValue(control.Key, value);
                                        Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.{control.Key}");
                                        ProjectManager.Instance.SetDirty(true);
                                    }
                                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                    {
                                        _ctxPropertyPath = $"{basePath}.{control.Key}";
                                        _ctxMenuPos = ImGui.GetMousePos();
                                        _openPropContextMenu = true;
                                    }
                                }
                                ImGui.PopID();
                            }

                            ImGui.TreePop();
                        }
                    }

                    if (removeIndex >= 0 && removeIndex < cameraObject.Effects.Count)
                    {
                        cameraObject.Effects.RemoveAt(removeIndex);
                        ProjectManager.Instance.SetDirty(true);
                    }
                }
                ImGui.EndChild();
                ImGui.PopStyleVar(2);
            }
        }

        if (_currentObject is CameraSceneObject selectedCamera &&
            ImGui.CollapsingHeader(Localization.Label("text.common.camera", "Camera")))
        {
            float fov = selectedCamera.Fov;
            if (InputFloatEditor("Field of View (degrees)", ref fov, 0.1f, 1f, 179f, "%.1f"))
            {
                ApplyToSelectedObjects(obj =>
                {
                    if (obj is CameraSceneObject camera)
                    {
                        camera.Fov = fov;
                        Timeline?.RecordAutoKeyframe(camera, "camera.fov");
                    }
                });
                ProjectManager.Instance.SetDirty(true);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "camera.fov";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
        }

        if (_currentObject is CameraSceneObject customCamera &&
            ImGui.CollapsingHeader(Localization.Label("text.common.custom", "Custom##cameraCustom")))
        {
            ImGui.TextUnformatted(Localization.T("text.panel.properties.color_management"));
            bool overrideEnabled = customCamera.ColorManagementOverrideEnabled;
            bool changed = ImGui.Checkbox(Localization.Label("text.panel.properties.camera_custom_color_management",
                "Custom Color Management##cameraCustom"), ref overrideEnabled);
            void ApplyOverride(object value) => ApplyToSelectedObjects(obj =>
            {
                if (obj is CameraSceneObject camera)
                    camera.ColorManagementOverrideEnabled = (bool)value;
            });
            if (changed) ApplyOverride(overrideEnabled);
            RegisterPropertyContext("camera.custom.color_management.override", () => customCamera.ColorManagementOverrideEnabled,
                ApplyOverride, false);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Localization.T("text.panel.properties.camera_custom_color_management_tooltip"));
            if (overrideEnabled)
            {
                bool enabled = customCamera.ColorManagementEnabled;
                int toneMapper = customCamera.ColorManagementToneMapper;
                float exposure = customCamera.ColorManagementExposure;
                float gamma = customCamera.ColorManagementGamma;
                int look = customCamera.ColorManagementLook;
                ImGui.Indent();
                changed |= DrawColorManagementControls(ref enabled, ref toneMapper, ref exposure, ref gamma, ref look, customCamera);
                ImGui.Unindent();
            }
            if (changed) ProjectManager.Instance.SetDirty(true);
        }

        if (ImGui.CollapsingHeader(Localization.Label("text.common.appearance", "Appearance")))
        {
            int blendMode = (int)(_currentObject.MaterialSettings?.BlendMode ?? MaterialBlendMode.Normal);
            string[] blendModes = [
                Localization.Label("text.panel.properties.blend_normal", "Normal"),
                Localization.Label("text.panel.properties.blend_add", "Add"),
                Localization.Label("text.panel.properties.blend_subtract", "Subtract"),
                Localization.Label("text.panel.properties.blend_multiply", "Multiply"),
                Localization.Label("text.panel.properties.blend_screen", "Screen")
            ];
            if (ImGui.Combo(Localization.Label("text.panel.properties.blend_mode", "Blend Mode"), ref blendMode,
                    blendModes, blendModes.Length))
            {
                ApplyToSelectedSubtrees(obj =>
                {
                    obj.SetExplicitMaterialSettings();
                    obj.MaterialSettings.BlendMode = (MaterialBlendMode)blendMode;
                    obj.PropagateMaterialSettingsToChildren();
                });
                ProjectManager.Instance.SetDirty(true);
            }

            bool vis = _currentObject.ObjectVisible;
            if (ImGui.Checkbox(Localization.Label("text.common.visible", "Visible"), ref vis))
            {
                ApplyToSelectedObjects(obj =>
                {
                    obj.SetObjectVisible(vis);
                    Timeline?.RecordAutoKeyframe(obj, "visible");
                });
                ProjectManager.Instance.SetDirty(true);
            }

            bool inheritVis = _currentObject.InheritVisibility;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.inherit_visibility", "Inherit Visibility"), ref inheritVis))
            {
                ApplyToSelectedObjects(obj =>
                {
                    obj.InheritVisibility = inheritVis;
                    ApplyToDescendants(obj, child => child.InheritVisibility = inheritVis);
                });
                ProjectManager.Instance.SetDirty(true);
            }

            // Hide Cast Shadows for cameras and point lights
            if (!(_currentObject is CameraSceneObject) && !(_currentObject is LightSceneObject))
            {
                bool castShadow = _currentObject.CastShadow;
                if (ImGui.Checkbox(Localization.Label("text.panel.properties.cast_shadows", "Cast Shadows"), ref castShadow))
                {
                    ApplyToSelectedSubtrees(obj => obj.CastShadow = castShadow);
                    ProjectManager.Instance.SetDirty(true);
                }
            }

            bool invertFaces = _currentObject.InvertFaces;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.invert_render_backfaces", "Invert (Render Backfaces)"), ref invertFaces))
            {
                ApplyToSelectedSubtrees(obj => obj.InvertFaces = invertFaces);
                ProjectManager.Instance.SetDirty(true);
            }

            bool blurTexture = _currentObject.BlurTexture;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.blur_texture_linear_filtering", "Blur Texture (Linear Filtering)"), ref blurTexture))
            {
                ApplyToSelectedSubtrees(obj => obj.BlurTexture = blurTexture);
                ProjectManager.Instance.SetDirty(true);
            }

            bool textureMipmaps = _currentObject.TextureMipmaps;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.texture_filtering_mip_maps", "Texture Filtering (Mip Maps)"), ref textureMipmaps))
            {
                ApplyToSelectedSubtrees(obj => obj.TextureMipmaps = textureMipmaps);
                ProjectManager.Instance.SetDirty(true);
            }

            bool includeAo = _currentObject.IncludeInAmbientOcclusion;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.include_in_ambient_occlusion", "Include In Ambient Occlusion"), ref includeAo))
            {
                ApplyToSelectedSubtrees(obj => obj.IncludeInAmbientOcclusion = includeAo);
                ProjectManager.Instance.SetDirty(true);
            }

            bool includeFog = _currentObject.IncludeInFog;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.include_in_fog", "Include In Fog"), ref includeFog))
            {
                ApplyToSelectedSubtrees(obj => obj.IncludeInFog = includeFog);
                ProjectManager.Instance.SetDirty(true);
            }

            bool renderHighQuality = _currentObject.RenderInHighQuality;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.render_in_high_quality", "Render In High Quality"), ref renderHighQuality))
            {
                ApplyToSelectedSubtrees(obj => obj.RenderInHighQuality = renderHighQuality);
                ProjectManager.Instance.SetDirty(true);
            }

            bool renderLowQuality = _currentObject.RenderInLowQuality;
            if (ImGui.Checkbox(Localization.Label("text.panel.properties.render_in_low_quality", "Render In Low Quality"), ref renderLowQuality))
            {
                ApplyToSelectedSubtrees(obj => obj.RenderInLowQuality = renderLowQuality);
                ProjectManager.Instance.SetDirty(true);
            }

            float renderDepth = _currentObject.RenderDepthOffset;
            if (InputFloatEditor("Render Depth", ref renderDepth, 0.01f, -1000f, 1000f, "%.2f"))
            {
                ApplyToSelectedSubtrees(obj => obj.RenderDepthOffset = renderDepth);
                ProjectManager.Instance.SetDirty(true);
            }

            // Active toggle for cameras. When enabled, this camera is the
            // preferred render-output camera and every other camera is
            // deactivated. The "active" state is also a keyframable property.
            if (_currentObject is CameraSceneObject activeCam)
            {
                bool active = activeCam.Active;
                if (ImGui.Checkbox(Localization.Label("text.panel.properties.active_camera", "Active Camera"), ref active))
                {
                    if (active)
                        CameraSceneObject.SetActiveExclusive(activeCam);
                    else
                        activeCam.Active = false;

                    Timeline?.RecordAutoKeyframe(activeCam, "camera.active");
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _ctxPropertyPath = "camera.active";
                    _ctxMenuPos = ImGui.GetMousePos();
                    _openPropContextMenu = true;
                }
            }

            // Mine-imator models can switch between Modelbench's block-preserving
            // sharp bends and the more finely segmented smooth bend style.
            if (_currentObject is CharacterSceneObject character &&
                character.BoneObjects.Values.Any(bone => bone is MiBoneSceneObject))
            {
                bool sharpBends = character.ModelBendStyle == BendStyle.Blocky;
                if (ImGui.Checkbox(Localization.Label("text.panel.properties.sharp_bends", "Sharp bends"), ref sharpBends))
                {
                    ApplyToSelectedObjects(obj =>
                    {
                        if (obj is CharacterSceneObject selectedCharacter &&
                            selectedCharacter.BoneObjects.Values.Any(bone => bone is MiBoneSceneObject))
                        {
                            selectedCharacter.ModelBendStyle = sharpBends ? BendStyle.Blocky : BendStyle.Realistic;
                        }
                    });
                    ProjectManager.Instance.SetDirty(true);
                }

                if (ImGui.IsItemHovered())
                    UiText.Tooltip(sharpBends
                        ? Localization.T("text.panel.properties.preserves_minecraft_s_blocky_limb_shape_while_bending")
                        : Localization.T("text.panel.properties.uses_additional_segments_for_a_smoother_bend"));
            }
        }

        ImGui.EndTabItem();
    }
    
    // ── Helpers ───────────────────────────────────────────────────────────────

    private IReadOnlyList<SceneObject> GetSelectedObjectsForEdit()
    {
        var selected = SelectionManager.Instance?.SelectedObjects;
        if (selected != null && selected.Count > 0)
            return selected;

        return _currentObject != null
            ? new List<SceneObject> { _currentObject }
            : Array.Empty<SceneObject>();
    }

    private bool DrawColorManagementControls(ref bool enabled, ref int toneMapper, ref float exposure,
        ref float gamma, ref int look, CameraSceneObject? camera = null)
    {
        bool Edit<T>(bool edited, string property, T value, Func<CameraSceneObject, T> get,
            Action<CameraSceneObject, T> set, T defaultValue) where T : notnull
        {
            if (camera != null)
            {
                void Apply(object newValue) => ApplyToSelectedObjects(obj =>
                {
                    if (obj is CameraSceneObject selectedCamera)
                        set(selectedCamera, (T)newValue);
                });
                if (edited) Apply(value);
                RegisterPropertyContext("camera.custom.color_management." + property, () => get(camera), Apply, defaultValue);
            }
            return edited;
        }

        ImGui.PushID(camera == null ? "colorManagement" : "cameraColorManagement");
        bool changed = Edit(ImGui.Checkbox(Localization.Label("text.panel.properties.enabled", "Enabled"), ref enabled),
            "enabled", enabled, obj => obj.ColorManagementEnabled, (obj, value) => obj.ColorManagementEnabled = value, true);
        if (enabled)
        {
            ImGui.Indent();
            changed |= Edit(ImGui.Combo(Localization.Label("text.panel.properties.color_management_tone_map", "Tone Map"), ref toneMapper,
                ColorManagementToneMapperOptions.Select(key => Localization.T(key)).ToArray(), ColorManagementToneMapperOptions.Length),
                "tone_mapper", toneMapper, obj => obj.ColorManagementToneMapper,
                (obj, value) => obj.ColorManagementToneMapper = Math.Clamp(value, 0, ColorManagementToneMapperOptions.Length - 1), 1);
            changed |= Edit(InputFloatEditor("Exposure (stops)", ref exposure, 0.05f, -10f, 10f),
                "exposure", exposure, obj => obj.ColorManagementExposure,
                (obj, value) => obj.ColorManagementExposure = SanitizeFloatValue(value, -10f, 10f), 0f);
            changed |= Edit(InputFloatEditor("Gamma", ref gamma, 0.01f, 0.1f, 5f),
                "gamma", gamma, obj => obj.ColorManagementGamma,
                (obj, value) => obj.ColorManagementGamma = SanitizeFloatValue(value, 0.1f, 5f), 2.2f);
            changed |= Edit(ImGui.Combo(Localization.Label("text.panel.properties.color_management_look", "Look"), ref look,
                ColorManagementLookOptions.Select(key => Localization.T(key)).ToArray(), ColorManagementLookOptions.Length),
                "look", look, obj => obj.ColorManagementLook,
                (obj, value) => obj.ColorManagementLook = Math.Clamp(value, 0, ColorManagementLookOptions.Length - 1), 0);
            ImGui.Unindent();
        }
        ImGui.PopID();
        return changed;
    }

    private void ApplyToSelectedObjects(Action<SceneObject> apply)
    {
        foreach (var obj in GetSelectedObjectsForEdit())
            apply(obj);
    }

    private void ApplyToSelectedSubtrees(Action<SceneObject> apply)
    {
        foreach (var obj in GetSelectedObjectsForEdit())
            ApplyToSubtree(obj, apply);
    }

    private void ApplyPosition(vec3 pos)
    {
        vec3 origin = GetEditablePosition(_currentObject);
        vec3 delta = pos - origin;

        // MiBoneSceneObjects: position is an offset from the base pose.
        // Plain BoneSceneObjects (GLB): position is an offset from the rest pose.
        ApplyToSelectedObjects(obj =>
        {
            vec3 target = GetEditablePosition(obj) + delta;
            SetEditablePosition(obj, target);

            Timeline?.RecordAutoKeyframe(obj, "position.x");
            Timeline?.RecordAutoKeyframe(obj, "position.y");
            Timeline?.RecordAutoKeyframe(obj, "position.z");
        });
        ProjectManager.Instance.SetDirty(true);
    }

    private void ApplyRotation(vec3 rot)
    {
        vec3 origin = GetEditableRotation(_currentObject);
        vec3 delta = rot - origin;

        ApplyToSelectedObjects(obj =>
        {
            vec3 target = GetEditableRotation(obj) + delta;
            SetEditableRotation(obj, target);

            Timeline?.RecordAutoKeyframe(obj, "rotation.x");
            Timeline?.RecordAutoKeyframe(obj, "rotation.y");
            Timeline?.RecordAutoKeyframe(obj, "rotation.z");
        });
        ProjectManager.Instance.SetDirty(true);
    }

    private void ApplyScale(vec3 scale)
    {
        vec3 origin = GetEditableScale(_currentObject);
        vec3 delta = scale - origin;

        ApplyToSelectedObjects(obj =>
        {
            vec3 cur = GetEditableScale(obj);
            vec3 target = new vec3(
                MathF.Max(cur.x + delta.x, 0.001f),
                MathF.Max(cur.y + delta.y, 0.001f),
                MathF.Max(cur.z + delta.z, 0.001f));
            SetEditableScale(obj, target);

            Timeline?.RecordAutoKeyframe(obj, "scale.x");
            Timeline?.RecordAutoKeyframe(obj, "scale.y");
            Timeline?.RecordAutoKeyframe(obj, "scale.z");
        });
        ProjectManager.Instance.SetDirty(true);
    }

    private void ApplyBend(MiBoneSceneObject bone, vec3 angle, params string[] propertyPaths)
    {
        vec3 origin = bone.GetEditableBendAngle();
        vec3 delta = angle - origin;
        bool changedAny = false;
        ApplyToSelectedObjects(obj =>
        {
            if (obj is not MiBoneSceneObject selectedBone)
                return;

            selectedBone.SetEditableBendAngle(selectedBone.GetEditableBendAngle() + delta);
            foreach (var propertyPath in propertyPaths)
                Timeline?.RecordAutoKeyframe(selectedBone, propertyPath);
            changedAny = true;
        });

        if (!changedAny)
        {
            bone.SetEditableBendAngle(angle);
            foreach (var propertyPath in propertyPaths)
                Timeline?.RecordAutoKeyframe(bone, propertyPath);
            changedAny = true;
        }

        if (changedAny)
            ProjectManager.Instance.SetDirty(true);
    }

    private void ApplyPivotOffset(vec3 pivot)
    {
        vec3 origin = _currentObject != null ? GetEditablePivotOffset(_currentObject) : vec3.Zero;
        vec3 delta = pivot - origin;

        ApplyToSelectedObjects(obj => obj.PivotOffset += obj.SpawnCategory == "Blocks" ? -delta : delta);
        ProjectManager.Instance.SetDirty(true);
    }

    private static vec3 GetEditablePivotOffset(SceneObject obj)
    {
        // Blocks expose a pivot in model coordinates; storage is the opposite visual translation.
        return obj.SpawnCategory == "Blocks" ? -obj.PivotOffset : obj.PivotOffset;
    }

    private static vec3 GetEditablePosition(SceneObject obj)
    {
        if (obj is MiBoneSceneObject miPos)
            return miPos.OffsetPosition;
        if (obj is BoneSceneObject bonePos)
            return bonePos.TargetPosition;
        return obj.LocalPosition;
    }

    private static void SetEditablePosition(SceneObject obj, vec3 value)
    {
        if (obj is MiBoneSceneObject miPos)
            miPos.OffsetPosition = value;
        else if (obj is BoneSceneObject bonePos)
            bonePos.TargetPosition = value;
        else
            obj.SetLocalPosition(value);
    }

    private static vec3 GetEditableRotation(SceneObject obj)
    {
        if (obj is MiBoneSceneObject miRot)
            return miRot.OffsetRotation;
        if (obj is BoneSceneObject boneRot)
            return boneRot.TargetRotation;
        return obj.LocalRotation;
    }

    private static void SetEditableRotation(SceneObject obj, vec3 value)
    {
        if (obj is MiBoneSceneObject miRot)
            miRot.OffsetRotation = value;
        else if (obj is BoneSceneObject boneRot)
            boneRot.TargetRotation = value;
        else
            obj.SetLocalRotation(value);
    }

    private static vec3 GetEditableScale(SceneObject obj)
    {
        if (obj is MiBoneSceneObject miScale)
            return miScale.OffsetScale;
        return obj.LocalScale;
    }

    private static void SetEditableScale(SceneObject obj, vec3 value)
    {
        if (obj is MiBoneSceneObject miScale)
            miScale.OffsetScale = value;
        else
            obj.SetLocalScale(value);
    }

    private bool EditVec3Editor(SceneObject keyframeObject, string idPrefix, ref vec3 value, float speed, float min, float max, string format, string label, string keyframePathPrefix)
    {
        bool changed = false;
        bool xChanged = false;
        bool yChanged = false;
        bool zChanged = false;
        float x = value.x;
        float y = value.y;
        float z = value.z;

        ImGui.TextUnformatted(Localization.DisplayText(label));
        ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
        if (InputFloatEditor($"X##{idPrefix}X", ref x, speed, min, max, format))
        {
            changed = true;
            xChanged = true;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _ctxPropertyPath = $"{keyframePathPrefix}.x";
            _ctxMenuPos = ImGui.GetMousePos();
            _openPropContextMenu = true;
        }

        if (InputFloatEditor($"Y##{idPrefix}Y", ref y, speed, min, max, format))
        {
            changed = true;
            yChanged = true;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _ctxPropertyPath = $"{keyframePathPrefix}.y";
            _ctxMenuPos = ImGui.GetMousePos();
            _openPropContextMenu = true;
        }

        if (InputFloatEditor($"Z##{idPrefix}Z", ref z, speed, min, max, format))
        {
            changed = true;
            zChanged = true;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _ctxPropertyPath = $"{keyframePathPrefix}.z";
            _ctxMenuPos = ImGui.GetMousePos();
            _openPropContextMenu = true;
        }
        ImGui.PopItemWidth();

        if (changed)
        {
            value = new vec3(x, y, z);

            if (xChanged)
                Timeline?.RecordAutoKeyframe(keyframeObject, $"{keyframePathPrefix}.x");
            if (yChanged)
                Timeline?.RecordAutoKeyframe(keyframeObject, $"{keyframePathPrefix}.y");
            if (zChanged)
                Timeline?.RecordAutoKeyframe(keyframeObject, $"{keyframePathPrefix}.z");
        }

        return changed;
    }

    /// <summary>
    /// Applies new block-tile values to the currently selected object, clamps
    /// them to <see cref="SceneObject.MaxTilesPerAxis"/>, and rebuilds the
    /// block meshes to reflect the change.
    /// </summary>
    private void ApplyBlockTiling(int tileX, int tileY, int tileZ)
    {
        if (_currentObject == null) return;

        tileX = Math.Clamp(tileX, 1, SceneObject.MaxTilesPerAxis);
        tileY = Math.Clamp(tileY, 1, SceneObject.MaxTilesPerAxis);
        tileZ = Math.Clamp(tileZ, 1, SceneObject.MaxTilesPerAxis);

        bool dirty = false;
        ApplyToSelectedObjects(obj =>
        {
            if (!string.Equals(obj.SpawnCategory, "Blocks", StringComparison.Ordinal))
                return;

            obj.TileX = tileX;
            obj.TileY = tileY;
            obj.TileZ = tileZ;

            Timeline?.RecordAutoKeyframe(obj, "tile.x");
            Timeline?.RecordAutoKeyframe(obj, "tile.y");
            Timeline?.RecordAutoKeyframe(obj, "tile.z");

            if (SpawnMenu != null && SpawnMenu.RebuildBlockMeshes(obj))
                dirty = true;
        });

        if (dirty)
            ProjectManager.Instance.SetDirty(true);
    }

    private void ApplyTemporaryItemSheetSlot(int columnIndex, int rowIndex)
    {
        if (_currentObject == null || SpawnMenu == null)
            return;

        bool changedAny = false;
        ApplyToSelectedObjects(obj =>
        {
            if (!SpawnMenu.ApplyTemporaryItemSheetSlotToSpawnedObject(obj, columnIndex, rowIndex))
                return;

            Timeline?.RecordAutoKeyframe(obj, "item.slot");
            Timeline?.RecordAutoKeyframe(obj, "item.custom_slot");
            changedAny = true;
        });

        if (changedAny)
            ProjectManager.Instance.SetDirty(true);
    }

    private void ApplyCubeUvMapping(bool mapped)
    {
        if (_currentObject == null) return;

        bool changedAny = false;
        ApplyToSelectedObjects(obj =>
        {
            if (!string.Equals(obj.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(obj.ObjectType, "Cube", StringComparison.OrdinalIgnoreCase))
                return;

            var cubeMesh = obj.Visuals.OfType<CubeMesh>().FirstOrDefault();
            if (cubeMesh != null)
            {
                cubeMesh.SetMapped(mapped);
                obj.PrimitiveCubeMapped = mapped;
                changedAny = true;
                return;
            }

            if (Gl == null)
                return;

            uint existingTextureId = obj.Visuals.FirstOrDefault()?.TextureId ?? 0;
            foreach (var mesh in obj.Visuals.ToList())
                mesh.Dispose();
            obj.Visuals.Clear();

            var rebuilt = new CubeMesh(Gl, mapped)
            {
                TextureId = existingTextureId
            };
            obj.AddMesh(rebuilt);
            obj.PrimitiveCubeMapped = mapped;
            changedAny = true;
        });

        if (changedAny)
            ProjectManager.Instance.SetDirty(true);
    }

    /// <summary>
    /// Applies an appearance-setting mutation to this object and all current descendants.
    /// </summary>
    private static void ApplyToSubtree(SceneObject root, Action<SceneObject> apply)
    {
        if (root == null) return;

        apply(root);
        foreach (var child in root.GetAllDescendants())
            apply(child);
    }

    /// <summary>
    /// Applies an appearance-setting mutation to descendants only.
    /// </summary>
    private static void ApplyToDescendants(SceneObject root, Action<SceneObject> apply)
    {
        if (root == null) return;

        foreach (var child in root.GetAllDescendants())
            apply(child);
    }

    /// <summary>
    /// Ensures <see cref="SceneObject.MaterialSettings"/> is non-null before writing.
    /// </summary>
    private void EnsureMaterialSettings()
    {
        if (_currentObject == null) return;
        if (_currentObject.MaterialSettings == null)
            _currentObject.MaterialSettings = new MaterialSettings();
    }

    /// <summary>
    /// Builds a cache of user-imported textures. Scans the project's images folder
    /// to find all imported texture files. Also maps currently loaded texture IDs to their paths.
    /// This ensures imported textures are always shown in the dropdown, even if not currently applied.
    /// </summary>
    private void RefreshLoadedTextures()
    {
        _loadedTexturePathCache.Clear();

        if (Viewport?.SceneObjects == null || !ProjectManager.Instance.HasProject)
            return;

        foreach (var asset in ProjectManager.Instance.GetProjectAssets().Where(asset => asset.AssetType == ProjectAssetType.Image))
        {
            string path = ProjectManager.Instance.GetAssetFullPath(asset);
            uint id = (uint)path.GetHashCode() & 0x7FFFFFFF;
            if (id != 0 && File.Exists(path)) _loadedTexturePathCache[id] = path;
        }

        // First, scan for imported texture files in the project images folder
        string texturesFolder = Path.Combine(ProjectManager.Instance.ProjectFolder, "images");
        if (Directory.Exists(texturesFolder))
        {
            foreach (var filePath in Directory.EnumerateFiles(texturesFolder))
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".gif" or ".webp" or ".tiff")
                {
                    // Use the relative path as a stable key for imported textures
                    string relativePath = Path.GetRelativePath(ProjectManager.Instance.ProjectFolder, filePath);
                    // Create a stable "fake" ID based on the filename hash
                    uint fakeId = (uint)relativePath.GetHashCode() & 0x7FFFFFFF; // Ensure positive
                    if (fakeId != 0 && !_loadedTexturePathCache.Values.Contains(filePath, StringComparer.OrdinalIgnoreCase))
                    {
                        _loadedTexturePathCache[fakeId] = filePath;
                    }
                }
            }
        }

        // Then scan geometry to find currently loaded textures and map their real IDs
        foreach (var obj in Viewport.SceneObjects)
        {
            ScanGeometryObjectForTextures(obj);
        }

        _cachedTextureIds = _loadedTexturePathCache.Keys.ToArray();
    }

    private static bool SupportsAlbedoTexture(SceneObject obj)
        => obj is CharacterSceneObject || obj.Visuals.Any(mesh => !mesh.PickOnly) ||
           obj.Children.Any(child => child.UsePropertyInheritance && child.InheritTexture && SupportsAlbedoTexture(child));

    private static uint GetDisplayedAlbedoTextureId(SceneObject obj)
    {
        bool hasVisuals = false;
        foreach (var mesh in obj.Visuals)
        {
            if (mesh.PickOnly) continue;
            hasVisuals = true;
            if (mesh.TextureId != 0) return mesh.TextureId;
        }
        if (!hasVisuals)
            foreach (var child in obj.Children)
            {
                uint texture = GetDisplayedAlbedoTextureId(child);
                if (texture != 0) return texture;
            }
        return 0;
    }

    private void ScanGeometryObjectForTextures(SceneObject obj)
    {
        if (obj == null) return;

        if (SupportsAlbedoTexture(obj))
        {
            // Scan this object's meshes for loaded textures
            foreach (var mesh in obj.Visuals.Where(mesh => mesh.TextureId != 0 && !_loadedTexturePathCache.ContainsKey(mesh.TextureId)))
            {
                // Map the real texture ID to its path
                string? loadedPath = MineImatorLoader.Instance.GetTexturePath(mesh.TextureId);
                if (!string.IsNullOrEmpty(loadedPath) || !string.IsNullOrEmpty(obj.AlbedoTexturePath))
                {
                    string fullPath = loadedPath ?? ProjectManager.Instance.ResolveAssetPath(obj.AlbedoTexturePath);

                    // Replace any existing fake/import entry that points to the same file
                    // so this texture appears only once in the dropdown.
                    uint duplicateKey = 0;
                    foreach (var (existingKey, existingPath) in _loadedTexturePathCache)
                    {
                        if (existingKey == mesh.TextureId) continue;
                        if (string.Equals(Path.GetFullPath(existingPath), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase))
                        {
                            duplicateKey = existingKey;
                            break;
                        }
                    }

                    if (duplicateKey != 0)
                        _loadedTexturePathCache.Remove(duplicateKey);

                    _loadedTexturePathCache[mesh.TextureId] = fullPath;
                }
                else
                {
                    // Fallback if no path is stored
                    _loadedTexturePathCache[mesh.TextureId] = $"Texture_{mesh.TextureId}";
                }
            }
        }

        // Scan children recursively
        foreach (var child in obj.Children)
        {
            ScanGeometryObjectForTextures(child);
        }
    }

    private void DrawAuxiliaryTextureSelector(bool normal)
    {
        string slot = normal ? "Normal" : "Emission";
        string ownPath = normal ? _currentObject.NormalTexturePath : _currentObject.EmissionTexturePath;
        string ownPack = normal ? _currentObject.NormalResourcePackId : _currentObject.EmissionResourcePackId;
        string effectivePath = _currentObject.GetEffectiveAuxiliaryTexturePath(normal);
        string effectivePack = _currentObject.GetEffectiveAuxiliaryResourcePackId(normal);
        string currentLabel = !string.IsNullOrEmpty(effectivePack) ? effectivePack : string.IsNullOrEmpty(effectivePath)
            ? Localization.T("text.panel.properties.none")
            : Path.GetFileName(effectivePath);

        ImGui.PushID(slot + "Texture");
        ImGui.TextUnformatted(Localization.T(normal
            ? "text.panel.properties.normal_texture"
            : "text.panel.properties.emission_texture"));
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##TextureCombo", currentLabel))
        {
            if (ImGui.Selectable(Localization.T("text.panel.properties.none"), string.IsNullOrEmpty(ownPath) && string.IsNullOrEmpty(ownPack)))
                AssignAuxiliaryTexture(normal, "");

            if (_currentObject.SpawnCategory is "Blocks" or "Items" or "Scenery")
            {
                ImGui.SeparatorText(Localization.T("text.panel.properties.resource_pack"));
                foreach (string packId in MinecraftDataLoader.GetAvailableResourcePackIds())
                {
                    bool selected = string.Equals(effectivePack, packId, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(packId + "##pack", selected))
                        AssignAuxiliaryTexture(normal, "", packId);
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.Separator();
            }

            var paths = _loadedTexturePathCache.Values
                .Concat(ProjectManager.Instance.GetProjectAssets()
                    .Where(asset => asset.AssetType == ProjectAssetType.Image)
                    .Select(asset => ProjectManager.Instance.GetAssetFullPath(asset)))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                bool selected = string.Equals(ProjectManager.Instance.ResolveAssetPath(effectivePath),
                    path, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(Path.GetFileName(path) + "##" + path, selected))
                    AssignAuxiliaryTexture(normal, path);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (ImGui.Button(Localization.Label("text.panel.properties.load_new_texture", "Load new texture..."), new Vector2(-1, 0)))
        {
            var result = Dialog.FileOpen("png,jpg,jpeg,bmp,tga,gif,hdr,exr");
            if (result.IsOk && !string.IsNullOrWhiteSpace(result.Path) && File.Exists(result.Path))
                AssignAuxiliaryTexture(normal, ResolveAlbedoTexturePathForProject(result.Path));
        }
        if ((!string.IsNullOrEmpty(ownPath) || !string.IsNullOrEmpty(ownPack)) &&
            ImGui.Button(Localization.Label("text.panel.properties.clear_texture", "Clear texture"), new Vector2(-1, 0)))
            AssignAuxiliaryTexture(normal, "");
        ImGui.PopID();
        ImGui.Spacing();
    }

    private void AssignAuxiliaryTexture(bool normal, string path, string resourcePackId = "")
    {
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                path = ProjectManager.Instance.ResolveAssetPath(path);
                string name = Path.GetFileNameWithoutExtension(path);
                bool directX = normal && (name.Contains("nor_dx", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("normaldx", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("normal_dx", StringComparison.OrdinalIgnoreCase));
                path = ResolveAlbedoTexturePathForProject(PbrMaterialImporter.ImportTexture(path, color: !normal, directXNormal: directX));
                if (Gl == null || _currentObject.LoadTexture(path, reload: true) == 0)
                    throw new InvalidDataException($"Could not load material texture '{Path.GetFileName(path)}'.");
                path = ProjectManager.Instance.ToProjectRelativePath(path);
                _pbrImportError = "";
            }
            catch (Exception ex)
            {
                _pbrImportError = ex.Message;
                return;
            }
        }
        EnsureMaterialSettings();
        _currentObject.SetExplicitMaterialSettings();
        if (normal)
        {
            _currentObject.NormalTexturePath = path;
            _currentObject.NormalResourcePackId = MinecraftDataLoader.NormalizeResourcePackId(resourcePackId);
            _currentObject.MaterialSettings.NormalEnabled = !string.IsNullOrEmpty(path) || !string.IsNullOrEmpty(resourcePackId);
        }
        else
        {
            _currentObject.EmissionTexturePath = path;
            _currentObject.EmissionResourcePackId = MinecraftDataLoader.NormalizeResourcePackId(resourcePackId);
            if (!string.IsNullOrEmpty(path) || !string.IsNullOrEmpty(resourcePackId))
            {
                var settings = _currentObject.MaterialSettings;
                settings.EmissionEnabled = true;
                if (settings.EmissionColor.x == 0f && settings.EmissionColor.y == 0f && settings.EmissionColor.z == 0f)
                    settings.EmissionColor = new vec4(1f);
                if (settings.EmissionEnergy <= 0f) settings.EmissionEnergy = 1f;
            }
        }
        _pbrImportError = "";
        _currentObject.PropagateMaterialSettingsToChildren();
        ProjectManager.Instance.SetDirty(true);
    }

    private void DrawPbrMaterialImport()
    {
        if (!SupportsAlbedoTexture(_currentObject)) return;
        if (ImGui.Button(Localization.Label("text.panel.properties.import_pbr", "Import PBR texture set..."), new Vector2(-1, 0)))
        {
            var result = Dialog.FileOpen("png,jpg,jpeg,bmp,tga,gif,hdr,exr");
            if (result.IsOk && !string.IsNullOrWhiteSpace(result.Path))
            {
                try
                {
                    var imported = PbrMaterialImporter.Import(result.Path);
                    string albedo = string.IsNullOrEmpty(imported.AlbedoPath) ? "" : ResolveAlbedoTexturePathForProject(imported.AlbedoPath);
                    string normal = string.IsNullOrEmpty(imported.NormalPath) ? "" : ResolveAlbedoTexturePathForProject(imported.NormalPath);
                    string emission = string.IsNullOrEmpty(imported.EmissionPath) ? "" : ResolveAlbedoTexturePathForProject(imported.EmissionPath);
                    string surface = string.IsNullOrEmpty(imported.SurfacePath) ? "" : ResolveAlbedoTexturePathForProject(imported.SurfacePath);
                    // Validate every GPU upload before replacing the current material.
                    foreach (string path in new[] { albedo, normal, emission, surface })
                        if (!string.IsNullOrEmpty(path) && _currentObject.LoadTexture(path, reload: true) == 0)
                            throw new InvalidDataException($"Could not load material texture '{Path.GetFileName(path)}'.");

                    EnsureMaterialSettings();
                    _currentObject.SetExplicitMaterialSettings();
                    if (!string.IsNullOrEmpty(albedo)) OnLoadAlbedoTextureForObject(_currentObject, albedo);
                    _currentObject.NormalTexturePath = ProjectManager.Instance.ToProjectRelativePath(normal);
                    _currentObject.EmissionTexturePath = ProjectManager.Instance.ToProjectRelativePath(emission);
                    _currentObject.SurfaceTexturePath = ProjectManager.Instance.ToProjectRelativePath(surface);
                    _currentObject.OcclusionTexturePath = RegisterSurfaceSource(imported.OcclusionPath);
                    _currentObject.RoughnessTexturePath = RegisterSurfaceSource(imported.RoughnessPath);
                    _currentObject.MetallicTexturePath = RegisterSurfaceSource(imported.MetallicPath);
                    _currentObject.HeightTexturePath = RegisterSurfaceSource(imported.HeightPath);
                    _currentObject.NormalResourcePackId = "";
                    _currentObject.EmissionResourcePackId = "";
                    var material = _currentObject.MaterialSettings;
                    material.PbrEnabled = true;
                    material.NormalEnabled = !string.IsNullOrEmpty(normal);
                    material.NormalStrength = 1f;
                    material.OcclusionStrength = 1f;
                    material.HeightScale = imported.HasHeight ? 0.02f : 0f;
                    material.Roughness = imported.HasRoughness ? 1f : 0.5f;
                    material.Metallic = imported.HasMetallic ? 1f : 0f;
                    material.EmissionEnabled = !string.IsNullOrEmpty(emission);
                    material.EmissionColor = new vec4(1f);
                    material.EmissionEnergy = 1f;
                    material.AutoEmission = false;
                    _currentObject.PropagateMaterialSettingsToChildren();
                    ProjectManager.Instance.SetDirty(true);
                    _pbrImportError = "";
                }
                catch (Exception ex)
                {
                    _pbrImportError = ex.Message;
                    Console.Error.WriteLine($"PBR material import failed: {ex.Message}");
                }
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Localization.T("text.panel.properties.import_pbr_hint"));
        ImGui.Spacing();
    }

    private void DrawPbrSurfaceControls()
    {
        var settings = _currentObject.MaterialSettings;
        bool enabled = settings.PbrEnabled;
        float normalStrength = settings.NormalStrength;
        float occlusionStrength = settings.OcclusionStrength;
        float heightScale = settings.HeightScale;
        bool pbrChanged = ImGui.Checkbox(Localization.Label("text.panel.properties.pbr_enabled", "PBR shading"), ref enabled);
        RegisterPropertyContext("material.pbr_enabled");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T("text.panel.properties.pbr_hint"));
        bool normalChanged = false;
        bool occlusionChanged = false;
        bool heightChanged = false;
        bool changed = pbrChanged;
        ImGui.Spacing();

        DrawAuxiliaryTextureSelector(true);
        if (enabled)
        {
            normalChanged = ImGui.SliderFloat(Localization.Label("text.panel.properties.normal_strength", "Normal strength"), ref normalStrength, 0f, 2f);
            changed |= normalChanged;
            RegisterPropertyContext("material.normal_strength");
        }

        ImGui.Separator();
        ImGui.TextWrapped(Localization.T("text.panel.properties.individual_surface_hint"));
        ImGui.TextWrapped(Localization.T("text.panel.properties.surface_multiplier_hint"));
        DrawSurfaceTextureSelector(1);
        DrawSurfaceTextureSelector(2);
        DrawSurfaceTextureSelector(0);
        if (enabled)
        {
            occlusionChanged = ImGui.SliderFloat(Localization.Label("text.panel.properties.occlusion_strength", "Material AO"), ref occlusionStrength, 0f, 1f);
            changed |= occlusionChanged;
            RegisterPropertyContext("material.occlusion_strength");
        }

        DrawSurfaceTextureSelector(3);
        if (enabled)
        {
            heightChanged = ImGui.SliderFloat(Localization.Label("text.panel.properties.height_scale", "Height / parallax"), ref heightScale, 0f, 0.1f, "%.3f");
            changed |= heightChanged;
            RegisterPropertyContext("material.height_scale");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T("text.panel.properties.height_hint"));
            string surfacePath = _currentObject.GetEffectiveSurfaceTexturePath();
            if (!string.IsNullOrEmpty(surfacePath))
            {
                ImGui.Separator();
                ImGui.TextWrapped(Localization.T("text.panel.properties.surface_texture") + ": " + Path.GetFileName(surfacePath));
                if (!string.IsNullOrEmpty(_currentObject.SurfaceTexturePath) &&
                    ImGui.Button(Localization.Label("text.panel.properties.clear_surface", "Clear surface maps")))
                {
                    _currentObject.SurfaceTexturePath = "";
                    _currentObject.OcclusionTexturePath = null;
                    _currentObject.RoughnessTexturePath = null;
                    _currentObject.MetallicTexturePath = null;
                    _currentObject.HeightTexturePath = null;
                    heightScale = 0f;
                    heightChanged = true;
                    changed = true;
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T("text.panel.properties.surface_hint"));
            }
        }
        if (changed)
        {
            _currentObject.SetExplicitMaterialSettings();
            settings = _currentObject.MaterialSettings;
            settings.PbrEnabled = enabled;
            settings.NormalStrength = normalStrength;
            settings.OcclusionStrength = occlusionStrength;
            settings.HeightScale = heightScale;
            _currentObject.PropagateMaterialSettingsToChildren();
            if (pbrChanged) Timeline?.RecordAutoKeyframe(_currentObject, "material.pbr_enabled");
            if (normalChanged) Timeline?.RecordAutoKeyframe(_currentObject, "material.normal_strength");
            if (occlusionChanged) Timeline?.RecordAutoKeyframe(_currentObject, "material.occlusion_strength");
            if (heightChanged) Timeline?.RecordAutoKeyframe(_currentObject, "material.height_scale");
            ProjectManager.Instance.SetDirty(true);
        }
    }

    private string? RegisterSurfaceSource(string? path) => string.IsNullOrEmpty(path) ? path
        : ProjectManager.Instance.ToProjectRelativePath(ResolveAlbedoTexturePathForProject(path));

    private void DrawSurfaceTextureSelector(int channel)
    {
        var source = _currentObject.GetEffectiveSurfaceTextureSource();
        string? path = channel switch
        {
            0 => source?.OcclusionTexturePath,
            1 => source?.RoughnessTexturePath,
            2 => source?.MetallicTexturePath,
            _ => source?.HeightTexturePath
        };
        string labelKey = channel switch
        {
            0 => "text.panel.properties.occlusion_texture",
            1 => "text.panel.properties.roughness_texture",
            2 => "text.panel.properties.metallic_texture",
            _ => "text.panel.properties.height_texture"
        };
        bool hasMap = source != null && path != "";
        string currentLabel = !hasMap ? Localization.T("text.panel.properties.none")
            : path == null ? Localization.T("text.panel.properties.baked_surface_map") : Path.GetFileName(path);
        ImGui.PushID("SurfaceMap" + channel);
        ImGui.TextUnformatted(Localization.T(labelKey));
        ImGui.TextWrapped(currentLabel);
        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(path)) ImGui.SetTooltip(path);
        float availableWidth = ImGui.GetContentRegionAvail().X;
        float buttonWidth = hasMap ? Math.Max(1f, (availableWidth - ImGui.GetStyle().ItemSpacing.X) * 0.5f) : availableWidth;
        if (ImGui.Button(Localization.Label("text.panel.properties.load_new_texture", "Load new texture..."), new Vector2(buttonWidth, 0)))
        {
            var result = Dialog.FileOpen("png,jpg,jpeg,bmp,tga,gif,hdr,exr");
            if (result.IsOk && !string.IsNullOrWhiteSpace(result.Path)) AssignSurfaceTexture(channel, result.Path);
        }
        if (hasMap)
        {
            ImGui.SameLine();
            if (ImGui.Button(Localization.Label("text.panel.properties.clear_texture", "Clear texture"), new Vector2(buttonWidth, 0)))
                AssignSurfaceTexture(channel, "");
        }
        ImGui.PopID();
        ImGui.Spacing();
    }

    private void AssignSurfaceTexture(int channel, string path)
    {
        try
        {
            var source = _currentObject.GetEffectiveSurfaceTextureSource();
            string existing = ProjectManager.Instance.ResolveAssetPath(source?.SurfaceTexturePath ?? "");
            string packed = PbrMaterialImporter.SetSurfaceMap(existing, channel, path);
            string surface = string.IsNullOrEmpty(packed) ? "" : ResolveAlbedoTexturePathForProject(packed);
            if (!string.IsNullOrEmpty(surface) && (Gl == null || _currentObject.LoadTexture(surface, reload: true) == 0))
                throw new InvalidDataException("Could not load the updated surface texture.");
            string? assigned = RegisterSurfaceSource(path);
            // A child gets a local copy of the packed channels and their source labels.
            string? occlusion = source == null ? "" : source.OcclusionTexturePath;
            string? roughness = source == null ? "" : source.RoughnessTexturePath;
            string? metallic = source == null ? "" : source.MetallicTexturePath;
            string? height = source == null ? "" : source.HeightTexturePath;
            EnsureMaterialSettings();
            _currentObject.SetExplicitMaterialSettings();
            _currentObject.SurfaceTexturePath = ProjectManager.Instance.ToProjectRelativePath(surface);
            _currentObject.OcclusionTexturePath = channel == 0 ? assigned : occlusion;
            _currentObject.RoughnessTexturePath = channel == 1 ? assigned : roughness;
            _currentObject.MetallicTexturePath = channel == 2 ? assigned : metallic;
            _currentObject.HeightTexturePath = channel == 3 ? assigned : height;
            var material = _currentObject.MaterialSettings;
            if (!string.IsNullOrEmpty(path))
            {
                material.PbrEnabled = true;
                if (channel == 1) material.Roughness = 1f;
                if (channel == 2) material.Metallic = 1f;
                if (channel == 3 && material.HeightScale <= 0f) material.HeightScale = 0.02f;
            }
            else if (channel == 3) material.HeightScale = 0f;
            _currentObject.PropagateMaterialSettingsToChildren();
            ProjectManager.Instance.SetDirty(true);
            _pbrImportError = "";
        }
        catch (Exception ex)
        {
            _pbrImportError = ex.Message;
            Console.Error.WriteLine($"PBR map assignment failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads a texture from file and applies it as the albedo texture to all meshes in the current object.
    /// Supports PNG, JPG, BMP, TGA, GIF, WebP, and TIFF formats with RGBA color components.
    /// </summary>
    private void OnLoadAlbedoTextureForObject(SceneObject obj, string filePath)
    {
        if (obj == null || Gl == null || !File.Exists(filePath))
            return;

        try
        {
            if (Path.GetExtension(filePath).ToLowerInvariant() is ".hdr" or ".exr")
                filePath = ResolveAlbedoTexturePathForProject(PbrMaterialImporter.ImportTexture(filePath, color: true));
            uint tex = obj.LoadTexture(filePath, reload: true);
            if (tex == 0) return;

            // Apply to all meshes
            foreach (var mesh in obj.Visuals)
            {
                mesh.TextureId = tex;
            }

            // Store the relative path for persistence
            obj.AlbedoTexturePath = ProjectManager.Instance.ToProjectRelativePath(filePath);
            obj.CameraTextureObjectId = "";
            obj.RefreshInheritedMaterials();
            _pbrImportError = "";
        }
        catch (Exception ex)
        {
            _pbrImportError = ex.Message;
            Console.Error.WriteLine($"Failed to load albedo texture '{filePath}': {ex.Message}");
        }
    }

    // ── Shape keys (morph targets) ────────────────────────────────────────────

    /// <summary>
    /// Returns true when <paramref name="obj"/> or any of its descendants owns
    /// a <see cref="Mesh"/> with at least one shape key defined.  Used to
    /// decide whether to render the Shape Keys section.
    /// </summary>
    private static bool HasAnyShapeKeys(SceneObject obj)
    {
        foreach (var mesh in obj.Visuals)
            if (mesh.HasShapeKeys) return true;
        var children = obj.Children;
        for (int i = 0; i < children.Count; i++)
            if (HasAnyShapeKeys(children[i])) return true;
        return false;
    }

    /// <summary>
    /// Draws a scrollable list of every shape key on every mesh that belongs
    /// to <paramref name="obj"/> or its descendants.  Each key gets a slider
    /// in the range [-1, 1] (default 0).
    /// </summary>
    private void RenderShapeKeysSection(SceneObject obj)
    {
        var meshes = obj.GetMeshInstancesRecursively();
        int keyCount = 0;
        foreach (var m in meshes) if (m.HasShapeKeys) keyCount += m.ShapeKeys.Count;

        UiText.Wrapped(Localization.T("text.panel.properties.drag_any_slider_to_blend_in_a_shape_key_negative_values_invert_the_morph_right"));
        ImGui.Spacing();

        // Search box — filters the list below by shape key name (case-insensitive).
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##shapekeySearch", Localization.T("text.panel.properties.search_shape_keys"), ref _shapeKeySearch, 128);
        ImGui.Spacing();

        // Scrollable child region keeps the section from blowing up the
        // properties panel when a model exposes dozens of morph targets
        // (a common case for facial-rig GLBs).
        float scrollHeight = MathF.Min(320f, MathF.Max(60f, 24f * keyCount + 40f));
        if (ImGui.BeginChild("##shapekeyScroll", new Vector2(0, scrollHeight), ImGuiChildFlags.Borders))
        {
            bool anyVisible = false;
            int meshIndex = 0;
            foreach (var mesh in meshes)
            {
                if (!mesh.HasShapeKeys) { meshIndex++; continue; }

                bool meshHeaderDrawn = false;

                for (int i = 0; i < mesh.ShapeKeys.Count; i++)
                {
                    var sk = mesh.ShapeKeys[i];
                    if (!string.IsNullOrEmpty(_shapeKeySearch) &&
                        sk.Name.IndexOf(_shapeKeySearch, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (meshes.Count > 1 && !meshHeaderDrawn)
                    {
                        UiText.Disabled(Localization.Format("text.panel.properties.mesh", meshIndex));
                        ImGui.Separator();
                        meshHeaderDrawn = true;
                    }

                    anyVisible = true;
                    float weight = sk.Weight;
                    string path  = $"shapekey.{meshIndex}.{i}";
                    string label = $"{sk.Name}##mesh{meshIndex}_key{i}";

                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.SliderFloat(label, ref weight, -1f, 1f, "%.2f"))
                    {
                        mesh.SetShapeKeyWeight(i, weight);
                        ProjectManager.Instance.SetDirty(true);
                        Timeline?.RecordAutoKeyframe(obj, path);
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _ctxPropertyPath = path;
                        _ctxMenuPos = ImGui.GetMousePos();
                        _openPropContextMenu = true;
                    }
                }
                meshIndex++;
            }

            if (!anyVisible && !string.IsNullOrEmpty(_shapeKeySearch))
                UiText.Disabled(Localization.T("text.panel.properties.no_shape_keys_match_your_search"));
        }
        ImGui.EndChild();
    }

    // ── Property context menu ─────────────────────────────────────────────────

    private sealed record PropertyContextBinding(string Path, Func<object> Get, Action<object> Set, object Default);
    private PropertyContextBinding? _propertyContextBinding;
    private string[]? _propertyContextPaths;
    private SceneObject? _propertyContextObject;

    private void RegisterPropertyContext(params string[] paths)
    {
        if (!ImGui.IsItemClicked(ImGuiMouseButton.Right) || paths.Length == 0) return;
        _ctxPropertyPath = paths[0];
        _propertyContextPaths = paths;
        _propertyContextBinding = null;
        _ctxMenuPos = ImGui.GetMousePos();
        _openPropContextMenu = true;
    }

    private void RegisterPropertyContext(string path, Func<object> getValue, Action<object> setValue, object defaultValue)
    {
        if (!ImGui.IsItemClicked(ImGuiMouseButton.Right)) return;
        RegisterPropertyContext(path);
        _propertyContextBinding = new(path, getValue, setValue, defaultValue);
    }

    private string[] GetContextPropertyGroup(string path)
    {
        if (_propertyContextPaths is { Length: > 1 } && _propertyContextPaths[0] == path)
            return _propertyContextPaths;
        int dot = path.LastIndexOf('.');
        if (dot < 0) return [path];
        string prefix = path[..dot];
        string component = path[(dot + 1)..];
        if (path.StartsWith("background:", StringComparison.Ordinal))
        {
            if (int.TryParse(component, out _) && GetBackgroundPropertyValue(prefix[11..]) is float[] array)
                return Enumerable.Range(0, array.Length).Select(i => $"{prefix}.{i}").ToArray();
        }
        else if (component is "x" or "y" or "z")
            return [$"{prefix}.x", $"{prefix}.y", $"{prefix}.z"];
        else if (component is "r" or "g" or "b" or "a")
            return component == "a" || prefix == "text.outline"
                ? [$"{prefix}.r", $"{prefix}.g", $"{prefix}.b", $"{prefix}.a"]
                : [$"{prefix}.r", $"{prefix}.g", $"{prefix}.b"];
        return [path];
    }

    private object GetContextPropertyValue(string path, bool defaultValue = false)
    {
        if (path.StartsWith("background:", StringComparison.Ordinal))
            return (defaultValue ? new PropertiesPanel() : this).GetBackgroundPropertyValue(path[11..])!;
        var obj = _currentObject!;
        int axis = path.EndsWith(".y", StringComparison.Ordinal) ? 1 : path.EndsWith(".z", StringComparison.Ordinal) ? 2 : 0;
        if (path.StartsWith("pivot.", StringComparison.Ordinal))
            return defaultValue ? (obj.SpawnCategory == "Blocks" && axis != 1 ? 8f : 0f) : GetEditablePivotOffset(obj)[axis] * 16f;
        if (path.StartsWith("tile.", StringComparison.Ordinal))
            return defaultValue ? 1f : (float)(axis == 0 ? obj.GetEffectiveTileX() : axis == 1 ? obj.GetEffectiveTileY() : obj.GetEffectiveTileZ());
        if (defaultValue && (path.StartsWith("bend.", StringComparison.Ordinal) || path.StartsWith("shapekey.", StringComparison.Ordinal)))
            return 0f;
        if (TimelinePanel.TryGetCopyConstraint(obj, path, out var copy, out string copyChannel))
        {
            float factor = copy.Type == CopyConstraintType.Position ? 16f
                : copy.Type == CopyConstraintType.Rotation ? 180f / MathF.PI : 1f;
            if (defaultValue)
                return copyChannel switch
                {
                    "target" => "",
                    "enabled" or "axis.x" or "axis.y" or "axis.z" or "blend" => 1f,
                    _ when copyChannel.StartsWith("offset.", StringComparison.Ordinal) && copy.Type == CopyConstraintType.Scale => 1f,
                    _ => 0f
                };
            object current = Timeline!.GetPropertyValue(obj, path);
            return copyChannel.StartsWith("offset.", StringComparison.Ordinal)
                ? Convert.ToSingle(current, CultureInfo.InvariantCulture) * factor : current;
        }
        if (defaultValue && path.StartsWith("constraint.", StringComparison.Ordinal))
            return path.Split('.').Last() switch
            {
                "target" or "pole" => "",
                "enabled" or "influence" or "rotate" or "bend" => 1f,
                "chain_length" => 2f,
                "iterations" => 32f,
                _ => 0f
            };
        if (defaultValue && path.StartsWith("bone.end", StringComparison.Ordinal))
            return path == "bone.end.y" ? 16f : 0f;
        if (defaultValue)
        {
            // Detached defaults preserve the model's base pose and never touch scene resources.
            SceneObject defaults = obj is CameraSceneObject ? new CameraSceneObject()
                : obj is LightSceneObject ? new LightSceneObject()
                : obj is ParticleSpawnerSceneObject ? new ParticleSpawnerSceneObject() : new SceneObject();
            defaults.MaterialSettings = new MaterialSettings();
            if (defaults is CameraSceneObject defaultCamera && obj is CameraSceneObject sourceCamera)
                foreach (var effect in sourceCamera.Effects)
                    defaultCamera.Effects.Add(new CameraEffect { Type = effect.Type });
            return Timeline!.GetPropertyValue(defaults, path);
        }
        object value = Timeline!.GetPropertyValue(obj, path);
        return path.StartsWith("position.", StringComparison.Ordinal) || path.StartsWith("bone.end.", StringComparison.Ordinal)
            ? Convert.ToSingle(value, CultureInfo.InvariantCulture) * 16f : value;
    }

    private void SetContextPropertyValue(string path, object value, bool recordKeyframe = true, bool resetValue = false)
    {
        if (path.StartsWith("background:", StringComparison.Ordinal))
        {
            string backgroundPath = path[11..];
            if (value is float[] array)
                for (int i = 0; i < array.Length; i++)
                    SetContextPropertyValue($"{path}.{i}", array[i]);
            else
            {
                if (value is float number)
                    value = backgroundPath switch
                    {
                        nameof(BackgroundScale) => Math.Clamp(number, 0.01f, 20f),
                        nameof(BackgroundRotationDegrees) => Math.Clamp(number, -360f, 360f),
                        "BackgroundOffset.0" or "BackgroundOffset.1" => Math.Clamp(number, -3f, 3f),
                        _ => number
                    };
                SetBackgroundPropertyValue(backgroundPath, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", value is not float);
            }
            ApplyFloorSettingsToViewport();
            ApplyBackgroundSettingsToViewport();
            ApplyAmbientSettingsToRenderer();
            WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
            return;
        }
        if (_currentObject == null || Timeline == null) return;
        int axis = path.EndsWith(".y", StringComparison.Ordinal) ? 1 : path.EndsWith(".z", StringComparison.Ordinal) ? 2 : 0;
        foreach (var obj in GetSelectedObjectsForEdit())
        {
            // Indexed effect/mesh paths are local to the object that opened the menu.
            if (obj != _currentObject && (path.StartsWith("camera.effect.", StringComparison.Ordinal) || path.StartsWith("constraint.", StringComparison.Ordinal) || path.StartsWith("copy_constraint.", StringComparison.Ordinal) || path.StartsWith("shapekey.", StringComparison.Ordinal)))
                continue;
            if (path.StartsWith("pivot.", StringComparison.Ordinal))
            {
                var pivot = GetEditablePivotOffset(obj);
                pivot[axis] = resetValue ? (obj.SpawnCategory == "Blocks" && axis != 1 ? 0.5f : 0f)
                    : Convert.ToSingle(value, CultureInfo.InvariantCulture) / 16f;
                obj.PivotOffset = obj.SpawnCategory == "Blocks" ? -pivot : pivot;
            }
            else if (path.StartsWith("tile.", StringComparison.Ordinal))
            {
                if (obj.SpawnCategory != "Blocks") continue;
                int count = (int)Math.Clamp(MathF.Round(Convert.ToSingle(value, CultureInfo.InvariantCulture)), 1f, SceneObject.MaxTilesPerAxis);
                if (axis == 0) obj.TileX = count; else if (axis == 1) obj.TileY = count; else obj.TileZ = count;
                SpawnMenu?.RebuildBlockMeshes(obj);
            }
            else
            {
                object stored = value;
                bool isCopyConstraint = TimelinePanel.TryGetCopyConstraint(obj, path, out var copy, out string copyChannel);
                if (isCopyConstraint && copyChannel.StartsWith("offset.", StringComparison.Ordinal))
                {
                    float factor = copy.Type == CopyConstraintType.Position ? 16f
                        : copy.Type == CopyConstraintType.Rotation ? 180f / MathF.PI : 1f;
                    stored = Convert.ToSingle(value, CultureInfo.InvariantCulture) / factor;
                }
                else if (path.StartsWith("position.", StringComparison.Ordinal) || path.StartsWith("bone.end.", StringComparison.Ordinal))
                    stored = Convert.ToSingle(value, CultureInfo.InvariantCulture) / 16f;
                else if (path.StartsWith("scale.", StringComparison.Ordinal))
                    stored = MathF.Max(0.001f, Convert.ToSingle(value, CultureInfo.InvariantCulture));
                else if (value is not string)
                {
                    float number = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                    stored = path switch
                    {
                        "light.energy" => Math.Clamp(number, 0f, 100f),
                        "light.range" => Math.Clamp(number, 0.01f, 500f),
                        "light.indirect_energy" => Math.Clamp(number, 0f, 16f),
                        "light.specular" => Math.Clamp(number, 0f, 1f),
                        "light.spot_angle" => Math.Clamp(number, 1f, 170f),
                        "light.spot_blend" when obj is LightSceneObject light => Math.Clamp(number, 0f, MathF.Max(0f, light.LightSpotAngle * 0.5f)),
                        _ when path.StartsWith("light.color.", StringComparison.Ordinal) => Math.Clamp(number, 0f, 1f),
                        _ when path.StartsWith("shapekey.", StringComparison.Ordinal) => Math.Clamp(number, -1f, 1f),
                        _ => number
                    };
                    if (path.StartsWith("camera.effect.", StringComparison.Ordinal))
                    {
                        string[] parts = path.Split('.');
                        if (parts[3] == "depth_of_field")
                        {
                            var control = DepthOfFieldSettings.Controls.First(c => c.Key == parts[4]);
                            stored = Math.Clamp(number, control.Min, control.Max);
                        }
                        else if (parts[3] == "film_grain")
                            stored = parts[4] == "size" ? Math.Clamp(number, 0.25f, 8f) : Math.Clamp(number, 0f, 1f);
                        else if (parts[3] == "shake")
                            stored = parts[4] switch
                            {
                                "trauma" => Math.Clamp(number, 0f, 5f),
                                "strength" => Math.Clamp(number, -100f, 100f),
                                "speed" => Math.Clamp(number, -200f, 200f),
                                "offset" => Math.Clamp(number, -1000f, 1000f),
                                _ => number
                            };
                    }
                }
                Timeline.SetPropertyValue(obj, path, stored);
                if (isCopyConstraint && (copyChannel is "target" or "bent_half" or "rotate" || copyChannel == "enabled" && copy.Enabled))
                    CaptureCopyConstraintOffset(obj, copy, path[..path.LastIndexOf('.')]);
                if (path == "light.spot_angle" && obj is LightSceneObject spot)
                    spot.LightSpotBlend = MathF.Min(spot.LightSpotBlend, spot.LightSpotAngle * 0.5f);
                if (obj is ParticleSpawnerSceneObject particle && path.StartsWith("particle.", StringComparison.Ordinal))
                    particle.ResetRuntime();
            }
            if (recordKeyframe) RecordContextPropertyKeyframes(obj, [path]);
        }
    }

    private void RecordContextPropertyKeyframes(SceneObject obj, string[] paths)
    {
        // Imported rotation channels are coupled, so capture them only after the entire edit.
        if (paths.Any(path => path.StartsWith("rotation.", StringComparison.Ordinal)))
            foreach (string axis in new[] { "x", "y", "z" })
                Timeline?.RecordAutoKeyframe(obj, $"rotation.{axis}");
        foreach (string path in paths)
        {
            if (path.StartsWith("background:", StringComparison.Ordinal) || path.StartsWith("rotation.", StringComparison.Ordinal) ||
                path.StartsWith("pivot.", StringComparison.Ordinal) || path.StartsWith("tile.", StringComparison.Ordinal)) continue;
            if (obj != _currentObject && (path.StartsWith("camera.effect.", StringComparison.Ordinal) || path.StartsWith("constraint.", StringComparison.Ordinal) || path.StartsWith("copy_constraint.", StringComparison.Ordinal) || path.StartsWith("shapekey.", StringComparison.Ordinal))) continue;
            Timeline?.RecordAutoKeyframe(obj, path);
            if (path == "light.spot_angle") Timeline?.RecordAutoKeyframe(obj, "light.spot_blend");
        }
    }

    private void ApplyContextPropertyValues(string[] paths, object[] values, bool resetValue = false)
    {
        for (int i = 0; i < paths.Length; i++)
            SetContextPropertyValue(paths[i], values[i], false, resetValue);
        foreach (var obj in GetSelectedObjectsForEdit())
            RecordContextPropertyKeyframes(obj, paths);
    }

    private static object[] GetPropertyComponents(object value) => value switch
    {
        vec2 v => [v.x, v.y],
        vec3 v => [v.x, v.y, v.z],
        vec4 v => [v.x, v.y, v.z, v.w],
        bvec2 v => [v.x, v.y],
        float[] v => v.Cast<object>().ToArray(),
        _ => [value]
    };

    private static object JoinPropertyComponents(object original, object[] values) => original switch
    {
        vec2 => new vec2((float)values[0], (float)values[1]),
        vec3 => new vec3((float)values[0], (float)values[1], (float)values[2]),
        vec4 => new vec4((float)values[0], (float)values[1], (float)values[2], (float)values[3]),
        bvec2 => new bvec2((bool)values[0], (bool)values[1]),
        float[] => values.Cast<float>().ToArray(),
        _ => values[0]
    };

    private static bool TryParsePropertyClipboard(string? text, object[] current, out object[] values)
    {
        values = new object[current.Length];
        if (text == null) return false;
        string[] tokens = current.Length == 1 && current[0] is string ? [text]
            : text.Split([',', ';', '\t', '\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != current.Length && tokens.Length != 1) return false;
        for (int i = 0; i < current.Length; i++)
        {
            string token = tokens[tokens.Length == 1 ? 0 : i];
            if (current[i] is string) values[i] = token;
            else if (current[i] is bool)
            {
                if (!bool.TryParse(token, out bool boolean)) return false;
                values[i] = boolean;
            }
            else if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && float.IsFinite(number))
            {
                Type type = current[i].GetType();
                if (type.IsEnum)
                {
                    if (!Enum.IsDefined(type, (int)number) || number != MathF.Truncate(number)) return false;
                    values[i] = Enum.ToObject(type, (int)number);
                }
                else if (current[i] is int)
                {
                    if (number < int.MinValue || number >= 2147483648f) return false;
                    values[i] = (int)MathF.Round(number);
                }
                else values[i] = number;
            }
            else return false;
        }
        return true;
    }

    private unsafe void RenderPropertyValueActions(string[] paths, PropertyContextBinding? binding = null)
    {
        object? aggregate = binding?.Get();
        object[] current = binding != null ? GetPropertyComponents(aggregate!)
            : paths.Select(path => GetContextPropertyValue(path)).ToArray();
        object[] defaults = binding != null ? GetPropertyComponents(binding.Default)
            : paths.Select(path => GetContextPropertyValue(path, true)).ToArray();
        // Array/color widgets use a single path but still edit all their components together.
        if (binding == null && current.Length == 1 && current[0] is float[])
        {
            aggregate = current[0];
            current = GetPropertyComponents(current[0]);
            defaults = GetPropertyComponents(defaults[0]);
        }
        void Apply(object[] values, bool resetValue = false)
        {
            if (binding != null) binding.Set(JoinPropertyComponents(aggregate!, values));
            else if (aggregate != null) SetContextPropertyValue(paths[0], JoinPropertyComponents(aggregate, values));
            else ApplyContextPropertyValues(paths, values, resetValue);
            ProjectManager.Instance.SetDirty(true);
        }
        void Copy()
        {
            ImGui.SetClipboardText(string.Join(", ", current.Select(value => value is Enum
                ? Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture))));
        }
        // The native clipboard can return null when no text is available.
        byte* clipboardPointer = ImGui.GetClipboardText();
        string? clipboardText = clipboardPointer == null ? null
            : System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)clipboardPointer);
        bool canPaste = TryParsePropertyClipboard(clipboardText, current, out var pasted);
        if (ImGui.MenuItem("Copy")) Copy();
        if (ImGui.MenuItem("Cut")) { Copy(); Apply(defaults, true); }
        if (ImGui.MenuItem("Paste", "", false, canPaste)) Apply(pasted);
        if (ImGui.MenuItem("Reset Value")) Apply(defaults, true);
    }

    private void RenderPropertyContextMenu()
    {
        if (!ImGui.BeginPopup("##prop_keyframe_ctx")) return;
        if (_ctxPropertyPath == null) { ImGui.EndPopup(); return; }
        if (!_ctxPropertyPath.StartsWith("background:", StringComparison.Ordinal) && _currentObject != _propertyContextObject)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        var binding = _propertyContextBinding?.Path == _ctxPropertyPath ? _propertyContextBinding : null;
        string[] group = GetContextPropertyGroup(_ctxPropertyPath);
        bool canEdit = binding != null || _ctxPropertyPath.StartsWith("background:", StringComparison.Ordinal) || (_currentObject != null && Timeline != null);
        if (canEdit)
        {
            RenderPropertyValueActions(_propertyContextPaths?.FirstOrDefault() == _ctxPropertyPath ? _propertyContextPaths : [_ctxPropertyPath], binding);
            if (binding == null && group.Length > 1 && !(_propertyContextPaths?.Length > 1 && _propertyContextPaths[0] == _ctxPropertyPath) && ImGui.BeginMenu("All Values"))
            {
                RenderPropertyValueActions(group);
                ImGui.EndMenu();
            }
        }
        ImGui.Separator();

        ImGui.BeginDisabled(binding != null || _ctxPropertyPath.StartsWith("pivot.", StringComparison.Ordinal) || _ctxPropertyPath.StartsWith("tile.", StringComparison.Ordinal));
        if (ImGui.MenuItem(Localization.Label("text.panel.properties.add_keyframe_at_current_frame", "Add Keyframe at Current Frame")))
        {
            if (_ctxPropertyPath.StartsWith("background:", StringComparison.Ordinal))
                AddBackgroundKeyframeGroup(_ctxPropertyPath[11..], Timeline?.CurrentFrame ?? 0);
            else if (_currentObject != null)
                foreach (string path in _propertyContextPaths?.FirstOrDefault() == _ctxPropertyPath ? _propertyContextPaths : [_ctxPropertyPath])
                    Timeline?.AddKeyframeForProperty(_currentObject, path, Timeline?.CurrentFrame ?? 0);
        }
        ImGui.EndDisabled();

        ImGui.EndPopup();
    }

    private void RegisterBackgroundKeyframeContext(string path)
    {
        if (!ImGui.IsItemClicked(ImGuiMouseButton.Right)) return;
        _ctxPropertyPath = "background:" + path;
        _ctxMenuPos = ImGui.GetMousePos();
        _openPropContextMenu = true;
    }

    private void AddBackgroundKeyframeGroup(string path, int frame)
    {
        if (GetBackgroundPropertyValue(path) is float[] values)
        {
            for (int i = 0; i < values.Length; i++) AddBackgroundKeyframe($"{path}.{i}", frame);
        }
        else AddBackgroundKeyframe(path, frame);
    }
}
