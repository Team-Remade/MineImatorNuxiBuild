using System.Numerics;
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

namespace MineImatorSimplyRemade.core.ui.Panels;

public class PropertiesPanel : UiPanel
{
    private const string NoImageSelected = "No image selected";
    private const string BackgroundModeStretch = "stretch";
    private const string BackgroundModeFit = "fit";
    private const string BackgroundModeOriginal = "original";

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
    public readonly float[] SubsurfaceRadiusRgb = [0.42f, 0.24f, 0.14f];
    public float SubsurfaceHighlightSize = 1f;
    public float SubsurfaceHighlightStrength = 1f;
    public float SubsurfaceHighlightSharpness = 2f;
    public float SubsurfaceHighlightDesaturation = 0f;
    public float SubsurfaceHighlightColorThreshold = 0f;
    public float SubsurfaceAbsorption = 0.35f;

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
            bool changed = ImGui.InputText(label, ref buffer, 64, ImGuiInputTextFlags.CharsScientific);

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
            label,
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
            bool changed = ImGui.InputText(label, ref buffer, 64, ImGuiInputTextFlags.CharsDecimal);

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
            label,
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

    public void SetRenderDimensionsAndFramerate(int width, int height, int framerate)
    {
        _resolutionWidth = Math.Max(1, width);
        _resolutionHeight = Math.Max(1, height);
        _framerate = Math.Clamp(framerate, 1, 120);

        if (ProjectManager.Instance.HasProject)
            WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
    }

    public void SetRenderExportSettings(string mode, string imageFormat, string videoFormat, int videoBitrateKbps, string resolutionPreset)
    {
        _renderMode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
        _renderImageFormat = string.IsNullOrWhiteSpace(imageFormat) ? "png" : imageFormat.Trim().ToLowerInvariant();
        _renderVideoFormat = string.IsNullOrWhiteSpace(videoFormat) ? "mp4" : videoFormat.Trim().ToLowerInvariant();
        _renderVideoBitrateKbps = Math.Clamp(videoBitrateKbps, 500, 200000);
        _renderResolutionPreset = string.IsNullOrWhiteSpace(resolutionPreset) ? "Custom" : resolutionPreset.Trim();

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

    // Scale link toggle
    private bool _linkScale = true;
    private int _cameraEffectToAddIndex;
    private static readonly string[] CameraEffectAddOptions = { "Camera Shake", "Film Grain" };
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
        SubsurfaceRadiusRgb[0] = Math.Clamp(settings.SubsurfaceRadiusR, 0.0001f, 8f);
        SubsurfaceRadiusRgb[1] = Math.Clamp(settings.SubsurfaceRadiusG, 0.0001f, 8f);
        SubsurfaceRadiusRgb[2] = Math.Clamp(settings.SubsurfaceRadiusB, 0.0001f, 8f);
        SubsurfaceHighlightSize = Math.Clamp(settings.SubsurfaceHighlightSize, 0f, 8f);
        SubsurfaceHighlightStrength = Math.Clamp(settings.SubsurfaceHighlightStrength, 0f, 8f);
        SubsurfaceHighlightSharpness = Math.Clamp(settings.SubsurfaceHighlightSharpness, 0.01f, 16f);
        SubsurfaceHighlightDesaturation = Math.Clamp(settings.SubsurfaceHighlightDesaturation, 0f, 1f);
        SubsurfaceHighlightColorThreshold = Math.Clamp(settings.SubsurfaceHighlightColorThreshold, 0f, 1f);
        SubsurfaceAbsorption = Math.Clamp(settings.SubsurfaceAbsorption, -0.95f, 0.95f);
        _resolutionWidth = Math.Max(1, settings.ResolutionWidth);
        _resolutionHeight = Math.Max(1, settings.ResolutionHeight);
        _framerate = Math.Clamp(settings.Framerate, 1, 120);
        _renderMode = string.Equals(settings.RenderMode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
        _renderImageFormat = string.IsNullOrWhiteSpace(settings.RenderImageFormat) ? "png" : settings.RenderImageFormat.Trim().ToLowerInvariant();
        _renderVideoFormat = string.IsNullOrWhiteSpace(settings.RenderVideoFormat) ? "mp4" : settings.RenderVideoFormat.Trim().ToLowerInvariant();
        _renderVideoBitrateKbps = Math.Clamp(settings.RenderVideoBitrateKbps, 500, 200000);
        _renderResolutionPreset = string.IsNullOrWhiteSpace(settings.RenderResolutionPreset) ? "Custom" : settings.RenderResolutionPreset.Trim();
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
        manifest.Settings.SubsurfaceRadiusR = Math.Clamp(SubsurfaceRadiusRgb[0], 0.0001f, 8f);
        manifest.Settings.SubsurfaceRadiusG = Math.Clamp(SubsurfaceRadiusRgb[1], 0.0001f, 8f);
        manifest.Settings.SubsurfaceRadiusB = Math.Clamp(SubsurfaceRadiusRgb[2], 0.0001f, 8f);
        manifest.Settings.SubsurfaceHighlightSize = Math.Clamp(SubsurfaceHighlightSize, 0f, 8f);
        manifest.Settings.SubsurfaceHighlightStrength = Math.Clamp(SubsurfaceHighlightStrength, 0f, 8f);
        manifest.Settings.SubsurfaceHighlightSharpness = Math.Clamp(SubsurfaceHighlightSharpness, 0.01f, 16f);
        manifest.Settings.SubsurfaceHighlightDesaturation = Math.Clamp(SubsurfaceHighlightDesaturation, 0f, 1f);
        manifest.Settings.SubsurfaceHighlightColorThreshold = Math.Clamp(SubsurfaceHighlightColorThreshold, 0f, 1f);
        manifest.Settings.SubsurfaceAbsorption = Math.Clamp(SubsurfaceAbsorption, -0.95f, 0.95f);

        manifest.Settings.ResolutionWidth = Math.Max(1, _resolutionWidth);
        manifest.Settings.ResolutionHeight = Math.Max(1, _resolutionHeight);
        manifest.Settings.Framerate = Math.Clamp(_framerate, 1, 120);
        manifest.Settings.RenderMode = string.Equals(_renderMode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
        manifest.Settings.RenderImageFormat = string.IsNullOrWhiteSpace(_renderImageFormat) ? "png" : _renderImageFormat.Trim().ToLowerInvariant();
        manifest.Settings.RenderVideoFormat = string.IsNullOrWhiteSpace(_renderVideoFormat) ? "mp4" : _renderVideoFormat.Trim().ToLowerInvariant();
        manifest.Settings.RenderVideoBitrateKbps = Math.Clamp(_renderVideoBitrateKbps, 500, 200000);
        manifest.Settings.RenderResolutionPreset = string.IsNullOrWhiteSpace(_renderResolutionPreset) ? "Custom" : _renderResolutionPreset.Trim();
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
            ImGui.TextDisabled("Object library is unavailable until the viewport is ready.");
            return;
        }

        EnsureObjectLibraryInitialized(projectManager.Manifest);

        var library = projectManager.Manifest.ObjectLibrary;
        var selected = FindLibraryEntryById(library, _selectedLibraryEntryId);

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ProjectObjectLibrarySearch", "Search spawned objects...", ref _librarySearch, 256);
        ImGui.Spacing();

        float treeHeight = MathF.Min(360f, MathF.Max(120f, ImGui.GetContentRegionAvail().Y * 0.6f));
        if (ImGui.BeginChild("##ProjectObjectLibraryTree", new Vector2(0f, treeHeight), ImGuiChildFlags.Borders))
        {
            bool anyVisible = false;
            foreach (var root in BuildLibraryTreeRoots(library).OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase))
                anyVisible |= RenderObjectLibraryTreeNode(root);

            if (!anyVisible)
                ImGui.TextDisabled("No library objects match your search.");
        }
        ImGui.EndChild();

        selected = FindLibraryEntryById(library, _selectedLibraryEntryId);
        if (selected != null)
        {
            int usageCount = CountLibraryUsage(selected.LibraryEntryId);
            string type = string.IsNullOrWhiteSpace(selected.ObjectType) ? "Object" : selected.ObjectType;
            ImGui.TextDisabled($"Type: {type}");
            ImGui.TextDisabled($"Used in scene: {usageCount}");

            if (ImGui.Button("Create In Scene From Base"))
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
            if (ImGui.Button("Delete From Library"))
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
            ImGui.TextDisabled("No objects have been spawned yet.");
        }

        bool hasSelectedLibraryObject = selected != null;
        if (!hasSelectedLibraryObject)
            ImGui.BeginDisabled();

        if (ImGui.Button("Duplicate Selected Library Object") && selected != null)
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
            Name = source.Name,
            ObjectType = source.ObjectType,
            SpawnCategory = source.SpawnCategory,
            BlockVariant = source.BlockVariant,
            TextureType = source.TextureType,
            ResourcePackId = source.ResourcePackId,
            SourceAssetPath = source.SourceAssetPath,
            AlbedoTexturePath = source.AlbedoTexturePath,
            TextureOverridePath = source.TextureOverridePath,
            TileX = source.TileX,
            TileY = source.TileY,
            TileZ = source.TileZ,
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
            InheritPivotOffset = source.InheritPivotOffset,
            InheritVisibility = source.InheritVisibility,
            ObjectVisible = source.ObjectVisible,
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
            CameraFov = source.CameraFov,
            CameraNear = source.CameraNear,
            CameraFar = source.CameraFar,
            CameraActive = source.CameraActive,
            CameraEffects = source.CameraEffects
                .Select(effect => new ProjectCameraEffectEntry
                {
                    Type = effect.Type,
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

    private bool RenderObjectLibraryTreeNode(ProjectSceneObjectEntry entry)
    {
        if (!ShouldShowLibraryEntry(entry))
            return false;

        bool isSelected = string.Equals(entry.LibraryEntryId, _selectedLibraryEntryId, StringComparison.OrdinalIgnoreCase);
        string label = $"{GetLibraryDisplayLabel(entry)} ({CountLibraryUsage(entry.LibraryEntryId)} in scene)##{entry.LibraryEntryId}";
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
                RenderObjectLibraryTreeNode(child);
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

            if (ContainsLibraryEntryId(manifest.ObjectLibrary, root.LibrarySourceId))
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

    private IEnumerable<SceneObject> EnumerateSceneObjects()
    {
        if (Viewport == null)
            yield break;

        foreach (var root in Viewport.SceneObjects)
        {
            yield return root;
            foreach (var child in EnumerateSceneObjectsRecursive(root))
                yield return child;
        }
    }

    private static IEnumerable<SceneObject> EnumerateSceneObjectsRecursive(SceneObject node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var nested in EnumerateSceneObjectsRecursive(child))
                yield return nested;
        }
    }

    private int CountLibraryUsage(string libraryEntryId)
    {
        if (string.IsNullOrWhiteSpace(libraryEntryId))
            return 0;

        return EnumerateSceneObjects().Count(obj =>
            string.Equals(obj.LibrarySourceId, libraryEntryId, StringComparison.OrdinalIgnoreCase));
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

    private static bool ContainsLibraryEntryId(IEnumerable<ProjectSceneObjectEntry> nodes, string libraryEntryId)
    {
        if (string.IsNullOrWhiteSpace(libraryEntryId))
            return false;

        foreach (var node in nodes)
        {
            if (string.Equals(node.LibraryEntryId, libraryEntryId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (ContainsLibraryEntryId(node.Children, libraryEntryId))
                return true;
        }

        return false;
    }

    private static void EnsureSceneLibrarySourceIdsRecursive(SceneObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.LibrarySourceId))
            obj.LibrarySourceId = string.IsNullOrWhiteSpace(obj.ObjectId) ? Guid.NewGuid().ToString("N") : obj.ObjectId;

        foreach (var child in obj.Children)
            EnsureSceneLibrarySourceIdsRecursive(child);
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
                string.Equals(Path.GetFullPath(a.SourcePath), fullSourcePath, StringComparison.OrdinalIgnoreCase));

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
        var sel = SelectionManager.Instance?.SelectedObjects;
        _currentObject = (sel != null && sel.Count > 0) ? sel[0] : null;
        
        // If object has a stored albedo texture path but hasn't loaded the texture yet, load it now
        if (_currentObject != null && 
            !string.IsNullOrEmpty(_currentObject.AlbedoTexturePath) &&
            string.Equals(_currentObject.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase))
        {
            // Check if any mesh already has the texture loaded
            bool hasTexture = _currentObject.Visuals.Any(mesh => mesh.TextureId != 0);

            // If not loaded, load it from the stored path
            if (!hasTexture)
            {
                string fullPath = Path.Combine(ProjectManager.Instance.ProjectFolder, _currentObject.AlbedoTexturePath);
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

        if (string.Equals(obj.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase) &&
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
                string fullPath = Path.Combine(ProjectManager.Instance.ProjectFolder, obj.AlbedoTexturePath);
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
        if (ImGui.Begin("Properties"))
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
                ImGui.OpenPopup("##prop_keyframe_ctx");
            }
            RenderPropertyContextMenu();
        }
        ImGui.End();
    }
    
    private void RenderProjectTab()
    {
        if (!ImGui.BeginTabItem("Project")) return;

        ImGui.Text("Project Properties");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Project Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var projectManager = ProjectManager.Instance;

            ImGui.Text("Project Name:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##ProjectName", ref _projectName, 256))
            {
                WriteProjectSettingsToManifest(projectManager.Manifest);
                projectManager.SetDirty(true);
            }

            ImGui.Spacing();
            ImGui.Text("Resolution:");
            ImGui.SetNextItemWidth(80);
            bool resolutionChanged = InputIntEditor("##ResWidth", ref _resolutionWidth, 1, 10, 1, int.MaxValue);
            ImGui.SameLine();
            ImGui.Text(" x ");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            resolutionChanged |= InputIntEditor("##ResHeight", ref _resolutionHeight, 1, 10, 1, int.MaxValue);
            ImGui.Text("Presets:");
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
            ImGui.Text("Framerate:");
            ImGui.SetNextItemWidth(80);
            bool frameRateChanged = InputIntEditor("##Framerate", ref _framerate, 1, 10, 1, 120);
            ImGui.SameLine();
            ImGui.Text(" fps");
            ImGui.Text("Presets:");
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
            ImGui.Text("Texture Animation Speed:");
            ImGui.SetNextItemWidth(80);
            bool textureFpsChanged = InputIntEditor("##TexAnimSpeed", ref TextureAnimationFps, 1, 10, 1, 120);
            ImGui.SameLine();
            ImGui.Text(" fps");
            ImGui.Text("Presets:");
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

        if (ImGui.CollapsingHeader("Library", ImGuiTreeNodeFlags.DefaultOpen))
            RenderObjectLibrarySection(ProjectManager.Instance);

        if (ImGui.CollapsingHeader("Render Settings"))
            RenderRenderSettings();

        if (ImGui.CollapsingHeader("Background Settings"))
        {
            unsafe
            {
                bool skyChanged = false;
                bool useSky = UseSky;
                if (ImGui.Checkbox("Minecraft Sky", ref useSky)) { UseSky = useSky; skyChanged = true; }
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
                    if (ImGui.Checkbox("Twilight", ref twilight)) { Twilight = twilight; skyChanged = true; }
                    RegisterBackgroundKeyframeContext(nameof(Twilight));
                    ImGui.Spacing();
                    ImGui.Text("Stars:");
                    bool showStars = ShowStars;
                    if (ImGui.Checkbox("Show Stars##stars", ref showStars)) { ShowStars = showStars; skyChanged = true; }
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
                    bool cloudModeOpen = ImGui.BeginCombo("Cloud Rendering", cloudModeLabel);
                    RegisterBackgroundKeyframeContext(nameof(CloudRenderMode));
                    if (cloudModeOpen)
                    {
                        if (ImGui.Selectable("3D", CloudRenderMode == "3d")) { CloudRenderMode = "3d"; skyChanged = true; }
                        if (ImGui.Selectable("Story Mode", CloudRenderMode == "story")) { CloudRenderMode = "story"; skyChanged = true; }
                        if (ImGui.Selectable("Flat", CloudRenderMode == "flat")) { CloudRenderMode = "flat"; skyChanged = true; }
                        ImGui.EndCombo();
                    }
                    skyChanged |= InputFloatEditor("Cloud Speed", ref CloudSpeed, 1f, -10000f, 10000f, "%.0f px/s");
                    RegisterBackgroundKeyframeContext(nameof(CloudSpeed));
                    fixed (float* value = CloudOffset) skyChanged |= ImGui.DragFloat2("Cloud Offset", value, 1f, -100000f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudOffset));
                    skyChanged |= InputFloatEditor("Cloud Height", ref CloudHeight, 1f, 0f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudHeight));
                    skyChanged |= InputFloatEditor("Cloud Block Size", ref CloudBlockSize, 1f, 1f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudBlockSize));
                    skyChanged |= InputFloatEditor("Cloud Thickness", ref CloudThickness, 1f, 1f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudThickness));
                    skyChanged |= ImGui.SliderInt("Moon Phase", ref MoonPhase, 0, 7);
                    RegisterBackgroundKeyframeContext(nameof(MoonPhase));
                    skyChanged |= ImGui.SliderFloat("Time", ref SkyTime, 0f, 24f, "%.2f h");
                    RegisterBackgroundKeyframeContext(nameof(SkyTime));
                    skyChanged |= InputFloatEditor("Sun Size (degrees)", ref SunSize, 0.1f, 0.1f, 90f);
                    RegisterBackgroundKeyframeContext(nameof(SunSize));
                    fixed (float* value = SunAngle) skyChanged |= ImGui.DragFloat3("Sun Angle (XYZ)", value, 0.25f, -360f, 360f);
                    RegisterBackgroundKeyframeContext(nameof(SunAngle));
                    skyChanged |= InputFloatEditor("Moon Size (degrees)", ref MoonSize, 0.1f, 0.1f, 90f);
                    RegisterBackgroundKeyframeContext(nameof(MoonSize));
                    fixed (float* value = MoonAngle) skyChanged |= ImGui.DragFloat3("Moon Angle (XYZ)", value, 0.25f, -360f, 360f);
                    RegisterBackgroundKeyframeContext(nameof(MoonAngle));
                    skyChanged |= SkyColorEditor("Sun Fill Light", SunFillLightColor);
                    skyChanged |= InputFloatEditor("Sun Fill Strength", ref SunFillLightStrength, 0.01f, 0f, 5f);
                    RegisterBackgroundKeyframeContext(nameof(SunFillLightStrength));
                    skyChanged |= ImGui.Checkbox("Sun Fill Casts Shadows", ref SunFillLightCastsShadows);
                    RegisterBackgroundKeyframeContext(nameof(SunFillLightCastsShadows));
                    skyChanged |= SkyColorEditor("Moon Fill Light", MoonFillLightColor);
                    skyChanged |= InputFloatEditor("Moon Fill Strength", ref MoonFillLightStrength, 0.01f, 0f, 5f);
                    RegisterBackgroundKeyframeContext(nameof(MoonFillLightStrength));
                    skyChanged |= ImGui.Checkbox("Moon Fill Casts Shadows", ref MoonFillLightCastsShadows);
                    RegisterBackgroundKeyframeContext(nameof(MoonFillLightCastsShadows));
                    ImGui.Unindent();
                }
                else
                    ImGui.TextDisabled("Solid background color is active.");

                if (skyChanged)
                {
                    Viewport?.ReloadSkyTextures();
                    WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                    ProjectManager.Instance.SetDirty(true);
                }

                ImGui.SeparatorText("Fog");
                bool fogChanged = ImGui.Checkbox("Enable Fog", ref FogEnabled);
                if (FogEnabled)
                {
                    ImGui.Indent();
                    fogChanged |= ImGui.Checkbox("Fog the Sky", ref SkyFog);
                    fogChanged |= ImGui.Checkbox("Custom Fog Color", ref CustomFogColor);
                    if (CustomFogColor) fogChanged |= SkyColorEditor("Fog Color", FogColor);
                    fogChanged |= ImGui.Checkbox("Custom Object Fog Color", ref CustomObjectFogColor);
                    if (CustomObjectFogColor) fogChanged |= SkyColorEditor("Object Fog Color", ObjectFogColor);
                    fogChanged |= InputFloatEditor("Distance", ref FogDistance, 10f, 0f, 1000000f, "%.0f px");
                    fogChanged |= InputFloatEditor("Fade Size", ref FogFadeSize, 10f, 1f, 1000000f, "%.0f px");
                    fogChanged |= InputFloatEditor("Height", ref FogHeight, 10f, -1000000f, 1000000f, "%.0f px");
                    fogChanged |= ImGui.Checkbox("Height Fog", ref HeightFog);
                    if (HeightFog)
                    {
                        fogChanged |= ImGui.Checkbox("Custom Height Fog Color", ref CustomHeightFogColor);
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
                ImGui.Text("Background Color:");
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
                ImGui.Text("Presets:");
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
                    if (ImGui.Button(presets[i].name))
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
                if (ImGui.Checkbox("Show Floor", ref floorVisible))
                {
                    FloorVisible = floorVisible;
                    ApplyFloorSettingsToViewport();
                    WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                    ProjectManager.Instance.SetDirty(true);
                }
                RegisterBackgroundKeyframeContext(nameof(FloorVisible));

                bool floorChanged = false;
                string floorAtlasLabel = NormalizeFloorAtlas(FloorTextureAtlas) == "item" ? "Item Atlas" : "Block Atlas";
                bool floorAtlasOpen = ImGui.BeginCombo("Floor Atlas", floorAtlasLabel);
                RegisterBackgroundKeyframeContext(nameof(FloorTextureAtlas));
                if (floorAtlasOpen)
                {
                    bool useBlock = NormalizeFloorAtlas(FloorTextureAtlas) == "block";
                    if (ImGui.Selectable("Block Atlas", useBlock))
                    {
                        FloorTextureAtlas = "block";
                        floorChanged = true;
                    }

                    bool useItem = NormalizeFloorAtlas(FloorTextureAtlas) == "item";
                    if (ImGui.Selectable("Item Atlas", useItem))
                    {
                        FloorTextureAtlas = "item";
                        floorChanged = true;
                    }

                    ImGui.EndCombo();
                }

                ImGui.Text("Floor Tile:");
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

                ImGui.Spacing();
                bool backgroundChanged = false;
                
                var imageAssets = GetBackgroundImageAssets();
                string selectedImageLabel = GetBackgroundImageLabel(BackgroundImagePath);
                ImGui.Text("Background Image:");
                ImGui.SetNextItemWidth(-1);
                bool backgroundImageOpen = ImGui.BeginCombo("##BackgroundImage", selectedImageLabel);
                RegisterBackgroundKeyframeContext(nameof(BackgroundImagePath));
                if (backgroundImageOpen)
                {
                    bool noneSelected = string.Equals(BackgroundImagePath, NoImageSelected, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(NoImageSelected, noneSelected))
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

                if (ImGui.Button("Import##backgroundImport") && ImportBackgroundImageFromDialog())
                    backgroundChanged = true;

                ImGui.SameLine();
                if (ImGui.Button("Clear##backgroundClear") && !string.Equals(BackgroundImagePath, NoImageSelected, StringComparison.OrdinalIgnoreCase))
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

                bool backgroundModeOpen = ImGui.BeginCombo("Background Mode", modeLabel);
                RegisterBackgroundKeyframeContext(nameof(BackgroundRenderMode));
                if (backgroundModeOpen)
                {
                    bool isStretch = string.Equals(BackgroundRenderMode, BackgroundModeStretch, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable("Stretch", isStretch))
                    {
                        BackgroundRenderMode = BackgroundModeStretch;
                        StretchBackground = true;
                        backgroundChanged = true;
                    }

                    bool isFit = string.Equals(BackgroundRenderMode, BackgroundModeFit, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable("Fit", isFit))
                    {
                        BackgroundRenderMode = BackgroundModeFit;
                        StretchBackground = false;
                        backgroundChanged = true;
                    }

                    bool isOriginal = string.Equals(BackgroundRenderMode, BackgroundModeOriginal, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable("Original", isOriginal))
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

                if (ImGui.Button("Reset Transform##backgroundResetTransform"))
                {
                    BackgroundScale = 1f;
                    BackgroundRotationDegrees = 0f;
                    BackgroundOffset[0] = 0f;
                    BackgroundOffset[1] = 0f;
                    backgroundChanged = true;
                }

                ImGui.Spacing();
                ImGui.Text("Ambient Light:");
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
                ImGui.Text("Night Ambient:");
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
                ImGui.Text("Fill Light:");
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
                if (ImGui.Checkbox("Fill Light Casts Shadows", ref fillLightCastsShadows))
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

        ImGui.Text("Ambient Occlusion");
        changed |= ImGui.Checkbox("Enabled##ambientOcclusion", ref AmbientOcclusionEnabled);
        if (AmbientOcclusionEnabled)
        {
            ImGui.Indent();
            changed |= InputFloatEditor("Radius (px)", ref AmbientOcclusionRadius, 0.25f, 0f, 128f, "%.1f");
            changed |= PercentageSlider("Strength##ao", ref AmbientOcclusionStrength, 0f, 200f);
            changed |= ImGui.SliderInt("Samples##ao", ref AmbientOcclusionSampleCount, 1, 128);
            fixed (float* color = AmbientOcclusionColor)
                changed |= ImGui.ColorEdit3("Color##ao", color, ImGuiColorEditFlags.NoInputs);
            changed |= InputFloatEditor("Ratio##ao", ref AmbientOcclusionRatio, 0.001f, 0f, 1f, "%.3f");
            changed |= InputFloatEditor("Ratio Balance##ao", ref AmbientOcclusionRatioBalance, 0.005f, 0f, 1f, "%.3f");
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.Text("Indirect Lighting");
        changed |= ImGui.Checkbox("Enabled##indirectLighting", ref IndirectLightingEnabled);
        if (IndirectLightingEnabled)
        {
            ImGui.Indent();

            string giModeLabel = string.Equals(GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase)
                ? "World Space (Experimental)"
                : "Screen Space";

            if (ImGui.BeginCombo("Global Illumination", giModeLabel))
            {
                bool screenSelected = !string.Equals(GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable("Screen Space", screenSelected))
                {
                    GlobalIlluminationMode = "screenspace";
                    changed = true;
                }

                bool worldSelected = string.Equals(GlobalIlluminationMode, "world", StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable("World Space (Experimental)", worldSelected))
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
            changed |= ImGui.Checkbox("Denoiser##indirect", ref IndirectLightingDenoiser);
            if (IndirectLightingDenoiser)
                changed |= ImGui.SliderFloat("Denoiser Strength##indirect", ref IndirectLightingDenoiserStrength, 0f, 200f, "%.0f%%");

            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.Text("Shadows");
        changed |= ImGui.Checkbox("Enabled##globalShadows", ref ShadowsEnabled);
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
        ImGui.Text("Glow");
        changed |= ImGui.Checkbox("Enabled##glow", ref GlowEnabled);
        if (GlowEnabled)
        {
            ImGui.Indent();
            changed |= PercentageSlider("Strength##glow", ref GlowStrength, 0f, 200f);
            changed |= InputFloatEditor("Size (px)##glow", ref GlowSize, 0.1f, 0f, 20f, "%.1f");
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.Text("Subsurface Scattering");
        changed |= ImGui.Checkbox("Enabled##sss", ref SubsurfaceEnabled);
        if (SubsurfaceEnabled)
        {
            ImGui.Indent();
            changed |= ImGui.SliderInt("Blur Samples##sss", ref SubsurfaceBlurSamples, 0, 32);
            changed |= PercentageSlider("Strength##sss", ref SubsurfaceStrength, 0f, 400f);
            changed |= PercentageSlider("Desaturation##sss", ref SubsurfaceDesaturation, 0f, 100f);
            changed |= PercentageSlider("Color Threshold##sss", ref SubsurfaceColorThreshold, 0f, 100f);
            fixed (float* sssRadius = SubsurfaceRadiusRgb)
                changed |= ImGui.DragFloat3("Radius RGB##sss", sssRadius, 0.01f, 0.0001f, 8f, "%.3f");
            changed |= InputFloatEditor("Highlight Size##sss", ref SubsurfaceHighlightSize, 0.01f, 0f, 8f, "%.2f");
            changed |= PercentageSlider("Highlight Strength##sss", ref SubsurfaceHighlightStrength, 0f, 800f);
            changed |= InputFloatEditor("Highlight Sharpness##sss", ref SubsurfaceHighlightSharpness, 0.05f, 0.01f, 16f, "%.2f");
            changed |= PercentageSlider("Highlight Desaturation##sss", ref SubsurfaceHighlightDesaturation, 0f, 100f);
            changed |= PercentageSlider("Highlight Color Threshold##sss", ref SubsurfaceHighlightColorThreshold, 0f, 100f);
            changed |= ImGui.SliderFloat("Absorption##sss", ref SubsurfaceAbsorption, -0.95f, 0.95f, "%.2f");
            ImGui.Unindent();
        }


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
        if (!ImGui.Combo(label, ref selected, labels, labels.Length))
            return false;
        value = values[selected];
        return true;
    }

    private static bool PercentageSlider(string label, ref float normalizedValue, float minimum, float maximum)
    {
        float percent = normalizedValue * 100f;
        if (!ImGui.SliderFloat(label, ref percent, minimum, maximum, "%.0f%%", ImGuiSliderFlags.None))
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
        ImGui.Text("Background Animation:");
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
        else if (current is bool boolValue && ImGui.Checkbox("Value##BackgroundKeyValue", ref boolValue))
            SetBackgroundPropertyValue(_selectedBackgroundKeyProperty, boolValue.ToString(), true);
        else if (current is string stringValue && ImGui.InputText("Value##BackgroundKeyValue", ref stringValue, 1024))
            SetBackgroundPropertyValue(_selectedBackgroundKeyProperty, stringValue, true);
        int frame = Timeline?.CurrentFrame ?? 0;
        if (ImGui.Button($"Add Keyframe (Frame {frame})")) AddBackgroundKeyframe(_selectedBackgroundKeyProperty, frame);
        ImGui.SameLine();
        if (ImGui.Button("Remove##BackgroundKey"))
        {
            var tracks = ProjectManager.Instance.Manifest.Settings.BackgroundKeyframes;
            if (tracks.TryGetValue(_selectedBackgroundKeyProperty, out var keys))
            {
                keys.RemoveAll(k => k.Frame == frame);
                if (keys.Count == 0) tracks.Remove(_selectedBackgroundKeyProperty);
                ProjectManager.Instance.SetDirty(true);
            }
        }
        if (ImGui.Button("Keyframe All Background Settings"))
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
            changed = ImGui.ColorEdit3(label, value, ImGuiColorEditFlags.NoInputs);
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
            : $"Vanilla / {Path.GetFileName(selected)}";
        bool open = ImGui.BeginCombo(label, preview);
        RegisterBackgroundKeyframeContext(label.StartsWith("Sun") ? nameof(SunTexture) : label.StartsWith("Moon") ? nameof(MoonTexture) : nameof(CloudTexture));
        if (open)
        {
            if (ImGui.Selectable($"Vanilla / {vanillaFile}", selected == vanilla))
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
        if (!ImGui.BeginTabItem("Object")) return;

        // ── Object header ─────────────────────────────────────────────────────
        if (_currentObject == null)
        {
            float windowWidth = ImGui.GetWindowWidth();
            string noSelText = "No object selected";
            float textWidth  = ImGui.CalcTextSize(noSelText).X;
            ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
            ImGui.TextDisabled(noSelText);
            ImGui.EndTabItem();
            return;
        }

        // Centered bold name label
        {
            float windowWidth = ImGui.GetWindowWidth();
            string displayName = _currentObject.GetDisplayName();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0.6f, 1f));
            float textWidth = ImGui.CalcTextSize(displayName).X;
            ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
            ImGui.Text(displayName);
            ImGui.PopStyleColor();
        }

        ImGui.Separator();

        ImGui.Spacing();

        // ── Position ──────────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Position"))
        {
            bool inheritPos = _currentObject.InheritPosition;
            if (ImGui.Checkbox("Inherit Position##pos", ref inheritPos))
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

            if (ImGui.Button("Reset##posReset"))
                ApplyPosition(vec3.Zero);
        }

        // ── Rotation ──────────────────────────────────────────────────────────
        // Point lights cannot have rotation (they're omni-directional); spot
        // lights use rotation to aim the cone so they are rotatable.
        bool canRotate = _currentObject is not LightSceneObject rotLight ||
                         rotLight.Type == LightType.Spot;
        if (canRotate && ImGui.CollapsingHeader("Rotation (degrees)"))
        {
            bool inheritRot = _currentObject.InheritRotation;
            if (ImGui.Checkbox("Inherit Rotation##rot", ref inheritRot))
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

            if (ImGui.Button("Reset##rotReset"))
                ApplyRotation(vec3.Zero);
        }

        // ── Scale ─────────────────────────────────────────────────────────────
        // Cameras and point lights cannot have their scale changed
        bool canScale = !(_currentObject is CameraSceneObject) && !(_currentObject is LightSceneObject);
        if (canScale && ImGui.CollapsingHeader("Scale"))
        {
            bool inheritScale = _currentObject.InheritScale;
            if (ImGui.Checkbox("Inherit Scale##scale", ref inheritScale))
                ApplyToSelectedObjects(obj => obj.InheritScale = inheritScale);

            ImGui.Checkbox("Link Scale", ref _linkScale);

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

            if (ImGui.Button("Reset##scaleReset"))
                ApplyScale(vec3.Ones);
        }

        // ── Bend ──────────────────────────────────────────────────────────────
        // Bend axes are authored per Mine-imator part. Do not offer controls
        // that the selected part cannot actually deform along.
        if (_currentObject is MiBoneSceneObject bendBone &&
            bendBone.BendParameters is BendParams bend &&
            (bend.AxisX || bend.AxisY || bend.AxisZ) &&
            ImGui.CollapsingHeader("Bend (degrees)"))
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

            if (ImGui.Button("Reset##bendReset"))
                ApplyBend(bendBone, vec3.Zero, "bend.x", "bend.y", "bend.z");
        }

        // ── Block Tiling ───────────────────────────────────────────────────────
        if (string.Equals(_currentObject.SpawnCategory, "Blocks", StringComparison.Ordinal) &&
            ImGui.CollapsingHeader("Block Tiling"))
        {
            ImGui.TextWrapped("Repeat the block along each axis. Each axis is limited to 1–1000.");
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
            ImGui.TextDisabled($"Total blocks: {_currentObject.GetEffectiveTileX() * _currentObject.GetEffectiveTileY() * _currentObject.GetEffectiveTileZ()}");

            if (ImGui.Button("Reset##tileReset"))
                ApplyBlockTiling(1, 1, 1);
        }

        if (string.Equals(_currentObject.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_currentObject.ObjectType, "Cube", StringComparison.OrdinalIgnoreCase) &&
            ImGui.CollapsingHeader("Cube"))
        {
            bool mapped = _currentObject.PrimitiveCubeMapped;
            if (ImGui.Checkbox("Mapped UVs##cubeMapped", ref mapped))
            {
                ApplyCubeUvMapping(mapped);
            }

            ImGui.TextDisabled("Off: each face fills the full texture.");
            ImGui.TextDisabled("On: unwrap to a 3x2 cubemap layout.");
        }

        if (string.Equals(_currentObject.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_currentObject.ObjectType, "Plane", StringComparison.OrdinalIgnoreCase) &&
            ImGui.CollapsingHeader("Plane"))
        {
            var planeMesh = _currentObject.Visuals.OfType<PlaneMesh>().FirstOrDefault();
            if (planeMesh != null)
            {
                string[] orientationOptions = { "XY", "XZ" };
                int orientationIndex = planeMesh.Orientation == PlaneOrientation.XZ ? 1 : 0;
                if (ImGui.Combo("Orientation##planeOrientation", ref orientationIndex, orientationOptions, orientationOptions.Length))
                {
                    planeMesh.SetOrientation(orientationIndex == 1 ? PlaneOrientation.XZ : PlaneOrientation.XY);
                    ProjectManager.Instance.SetDirty(true);
                }

                bool faceCamera = _currentObject.PrimitivePlaneFaceCamera;
                if (ImGui.Checkbox("Face Camera##planeFaceCamera", ref faceCamera))
                {
                    _currentObject.PrimitivePlaneFaceCamera = faceCamera;
                    ProjectManager.Instance.SetDirty(true);
                }
            }
            else
            {
                ImGui.TextDisabled("Plane mesh is unavailable.");
            }
        }

        if (_currentObject is ParticleSpawnerSceneObject particleSpawner &&
            ImGui.CollapsingHeader("Particles", ImGuiTreeNodeFlags.DefaultOpen))
        {
            EnsureObjectLibraryInitialized(ProjectManager.Instance.Manifest);

            var sourceEntries = new List<ProjectSceneObjectEntry>();
            CollectParticleSourceEntries(ProjectManager.Instance.Manifest.ObjectLibrary, sourceEntries);
            sourceEntries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            var selectedSource = FindLibraryEntryById(sourceEntries, particleSpawner.ParticleLibraryEntryId);
            string selectedLabel = selectedSource == null
                ? "(none)"
                : $"{selectedSource.Name} [{selectedSource.ObjectType}]";

            if (ImGui.BeginCombo("Particle Source", selectedLabel))
            {
                bool noneSelected = string.IsNullOrWhiteSpace(particleSpawner.ParticleLibraryEntryId);
                if (ImGui.Selectable("(none)", noneSelected))
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
            if (ImGui.Checkbox("Emitting", ref emitting))
            {
                particleSpawner.Emitting = emitting;
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.emitting");
                ProjectManager.Instance.SetDirty(true);
            }

            bool oneShot = particleSpawner.OneShot;
            if (ImGui.Checkbox("One Shot", ref oneShot))
            {
                particleSpawner.OneShot = oneShot;
                particleSpawner.ResetRuntime();
                Timeline?.RecordAutoKeyframe(_currentObject, "particle.one_shot");
                ProjectManager.Instance.SetDirty(true);
            }

            bool topLevelParticles = particleSpawner.TopLevelParticles;
            if (ImGui.Checkbox("Top Level Particles", ref topLevelParticles))
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

            string[] shapeOptions = { "Box", "Sphere" };
            int emissionShape = particleSpawner.EmissionShape == ParticleEmissionShape.Sphere ? 1 : 0;
            if (ImGui.Combo("Emission Shape", ref emissionShape, shapeOptions, shapeOptions.Length))
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
            if (ImGui.Checkbox("Directional Emission", ref directionalEmission))
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

            ImGui.TextDisabled($"Active particles: {particleSpawner.ActiveParticleCount}");

            if (ImGui.Button("Restart Particles"))
            {
                particleSpawner.ResetRuntime();
            }
        }

        // ── Pivot Offset ──────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Pivot Offset"))
        {
            bool inheritPivot = _currentObject.InheritPivotOffset;
            if (ImGui.Checkbox("Inherit Pivot##pivot", ref inheritPivot))
                ApplyToSelectedObjects(obj => obj.InheritPivotOffset = inheritPivot);

            vec3 pivot = _currentObject.PivotOffset;
            float pivX = pivot.x * 16f;
            float pivY = pivot.y * 16f;
            float pivZ = pivot.z * 16f;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            if (InputFloatEditor("X##pivX", ref pivX, 0.1f, float.MinValue, float.MaxValue))
                ApplyPivotOffset(new vec3(pivX / 16f, pivY / 16f, pivZ / 16f));

            if (InputFloatEditor("Y##pivY", ref pivY, 0.1f, float.MinValue, float.MaxValue))
                ApplyPivotOffset(new vec3(pivX / 16f, pivY / 16f, pivZ / 16f));

            if (InputFloatEditor("Z##pivZ", ref pivZ, 0.1f, float.MinValue, float.MaxValue))
                ApplyPivotOffset(new vec3(pivX / 16f, pivY / 16f, pivZ / 16f));
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##pivReset"))
                ApplyToSelectedObjects(obj => obj.PivotOffset = vec3.Zero);
        }

        // ── Material ──────────────────────────────────────────────────────────
        bool isLight = _currentObject is LightSceneObject || _currentObject is CameraSceneObject;
        if (!isLight && ImGui.CollapsingHeader("Material"))
        {
            // Ensure MaterialSettings exists when we need to write
            EnsureMaterialSettings();
            var mat = _currentObject.MaterialSettings;

            bool supportsResourcePack = string.Equals(_currentObject.SpawnCategory, "Blocks", StringComparison.Ordinal) ||
                                        string.Equals(_currentObject.SpawnCategory, "Scenery", StringComparison.Ordinal);
            bool supportsItemImage = string.Equals(_currentObject.SpawnCategory, "Items", StringComparison.Ordinal);

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
                if (ImGui.BeginCombo("Item Atlas", atlasLabel))
                {
                    bool useLocal = atlasSource == ItemAtlasSource.LocalAtlas;
                    if (_currentObject.TemporaryItemSheetColumns > 0 && _currentObject.TemporaryItemSheetRows > 0 &&
                        ImGui.Selectable("Local Atlas", useLocal))
                    {
                        atlasSource = ItemAtlasSource.LocalAtlas;
                        localItemSheetKeys = GetLocalItemSheetKeys(_currentObject).ToList();
                        currentKey = localItemSheetKeys.FirstOrDefault().Key ?? currentKey;
                    }

                    bool useItem = atlasSource == ItemAtlasSource.ItemAtlas;
                    if (ImGui.Selectable("Item Atlas", useItem))
                    {
                        atlasSource = ItemAtlasSource.ItemAtlas;
                        _currentObject.TextureType = "item";
                        currentKey = GetItemAtlasKeys(atlasSource).FirstOrDefault() ?? currentKey;
                    }

                    bool useBlock = atlasSource == ItemAtlasSource.BlockAtlas;
                    if (ImGui.Selectable("Block Atlas", useBlock))
                    {
                        atlasSource = ItemAtlasSource.BlockAtlas;
                        _currentObject.TextureType = "block";
                        currentKey = GetItemAtlasKeys(atlasSource).FirstOrDefault() ?? currentKey;
                    }

                    ImGui.EndCombo();
                }

                ImGui.Text("Item Image:");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##ItemImageKey", string.IsNullOrWhiteSpace(currentKey) ? "(none)" : currentKey))
                {
                    if (atlasSource == ItemAtlasSource.LocalAtlas)
                    {
                        localItemSheetKeys ??= GetLocalItemSheetKeys(_currentObject).ToList();
                        foreach (var entry in localItemSheetKeys)
                        {
                            bool selected = string.Equals(entry.Key, currentKey, StringComparison.Ordinal);
                            string label = $"Column {entry.Column}, Row {entry.Row + 1}##{entry.Key}";
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

                if (ImGui.Button("Load custom image...##ItemMaterialCustom", new Vector2(-1, 0)))
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
                    ImGui.SeparatorText("Item Sheet Slot");

                    int slotColumn = _currentObject.TemporaryItemSheetColumnIndex;
                    int slotRow = _currentObject.TemporaryItemSheetRowIndex;
                    bool slotChanged = false;

                    ImGui.PushItemWidth(-ImGui.CalcTextSize("Row").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);

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
                string currentPackId = MinecraftDataLoader.NormalizeResourcePackId(_currentObject.ResourcePackId);
                var packIds = MinecraftDataLoader.GetAvailableResourcePackIds().ToList();

                int selectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(currentPackId))
                {
                    int found = packIds.FindIndex(id => string.Equals(id, currentPackId, StringComparison.OrdinalIgnoreCase));
                    if (found >= 0)
                        selectedIndex = found + 1;
                }

                string selectedLabel = selectedIndex == 0 ? "Default" : packIds[selectedIndex - 1];
                if (ImGui.BeginCombo("Resource Pack", selectedLabel))
                {
                    bool isDefaultSelected = selectedIndex == 0;
                    if (ImGui.Selectable("Default", isDefaultSelected))
                    {
                        if (SpawnMenu != null && SpawnMenu.ApplyResourcePackToSpawnedObject(_currentObject, ""))
                        {
                            _currentObject.ResourcePackId = "";
                            ProjectManager.Instance.SetDirty(true);
                        }
                    }
                    if (isDefaultSelected)
                        ImGui.SetItemDefaultFocus();

                    for (int i = 0; i < packIds.Count; i++)
                    {
                        string id = packIds[i];
                        bool isSelected = selectedIndex == (i + 1);
                        if (ImGui.Selectable(id, isSelected))
                        {
                            if (SpawnMenu != null && SpawnMenu.ApplyResourcePackToSpawnedObject(_currentObject, id))
                            {
                                _currentObject.ResourcePackId = id;
                                ProjectManager.Instance.SetDirty(true);
                            }
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }

                    ImGui.EndCombo();
                }

                ImGui.Spacing();
            }

            // Albedo texture (for primitives and custom objects)
            if (string.Equals(_currentObject.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase))
            {
                // Refresh texture cache from scene
                RefreshLoadedTextures();
                
                ImGui.Text("Albedo Texture:");
                
                // Show current texture if any mesh has one
                uint currentTextureId = 0;
                foreach (var mesh in _currentObject.Visuals)
                {
                    if (mesh.TextureId != 0)
                    {
                        currentTextureId = mesh.TextureId;
                        break;
                    }
                }

                // Build list of available textures for dropdown
                string currentLabel = "(none)";
                if (currentTextureId != 0 && _loadedTexturePathCache.ContainsKey(currentTextureId))
                {
                    currentLabel = Path.GetFileName(_loadedTexturePathCache[currentTextureId]);
                }
                else if (currentTextureId == 0 && !string.IsNullOrEmpty(_currentObject.AlbedoTexturePath))
                {
                    // Texture path stored but ID is 0, show the filename
                    string fullPath = Path.Combine(ProjectManager.Instance.ProjectFolder, _currentObject.AlbedoTexturePath);
                    if (File.Exists(fullPath))
                    {
                        currentLabel = Path.GetFileName(fullPath);
                    }
                }

                // Texture dropdown
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##AlbedoTextureCombo", currentLabel))
                {
                    // "None" option
                    if (ImGui.Selectable("(none)", currentTextureId == 0))
                    {
                        // Clear texture from all meshes
                        foreach (var mesh in _currentObject.Visuals)
                        {
                            if (mesh.TextureId != 0)
                            {
                                Gl?.DeleteTexture(mesh.TextureId);
                                mesh.TextureId = 0;
                            }
                        }
                        _currentObject.AlbedoTexturePath = "";
                        ProjectManager.Instance.SetDirty(true);
                    }

                    // Show all loaded/imported textures as options
                    foreach (var (texId, path) in _loadedTexturePathCache)
                    {
                        if (texId == 0) continue;
                        string label = Path.GetFileName(path);
                        bool selected = (currentTextureId == texId);
                        if (ImGui.Selectable(label, selected))
                        {
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

                if (ImGui.Button("Load new texture...##AlbedoTexture", new Vector2(-1, 0)))
                {
                    var result = Dialog.FileOpen("png,jpg,jpeg,bmp,tga,gif,webp,tiff");
                    if (result.IsOk && !string.IsNullOrWhiteSpace(result.Path) && File.Exists(result.Path))
                    {
                        string resolvedPath = ResolveAlbedoTexturePathForProject(result.Path);
                        OnLoadAlbedoTextureForObject(_currentObject, resolvedPath);
                        ProjectManager.Instance.SetDirty(true);
                    }
                }

                if (currentTextureId != 0 && ImGui.Button("Clear texture##AlbedoTextureClear", new Vector2(-1, 0)))
                {
                    foreach (var mesh in _currentObject.Visuals.Where(mesh => mesh.TextureId != 0))
                    {
                        Gl?.DeleteTexture(mesh.TextureId);
                        mesh.TextureId = 0;
                    }
                    _currentObject.AlbedoTexturePath = "";
                    ProjectManager.Instance.SetDirty(true);
                }

                ImGui.Spacing();
            }

            // Alpha – skip for BoneSceneObject
            if (_currentObject is not BoneSceneObject)
            {
                float alpha = mat?.AlbedoColor.a ?? 1f;
                ImGui.SetNextItemWidth(-60f);
                if (ImGui.SliderFloat("Alpha", ref alpha, 0f, 1f))
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
                ImGui.Text(alpha.ToString("F2"));
            }

            // Albedo color
            {
                vec4 ac = mat?.AlbedoColor ?? new vec4(1f, 1f, 1f, 1f);
                var vec4 = new Vector4(ac.r, ac.g, ac.b, ac.a);
                if (ImGui.ColorEdit4("Albedo", ref vec4, ImGuiColorEditFlags.NoInputs))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.AlbedoColor = new vec4(vec4.X, vec4.Y, vec4.Z, vec4.W);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Mine-imator-style multiplicative blend colour.
            {
                vec4 bc = mat?.BlendColor ?? new vec4(1f, 1f, 1f, 1f);
                var color = new Vector3(bc.r, bc.g, bc.b);
                if (ImGui.ColorEdit3("Blend Color", ref color, ImGuiColorEditFlags.NoInputs))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.BlendColor = new vec4(color.X, color.Y, color.Z, 1f);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Mix colour uses its alpha channel as the interpolation amount.
            {
                vec4 mc = mat?.MixColor ?? new vec4(0f, 0f, 0f, 0f);
                var color = new Vector3(mc.r, mc.g, mc.b);
                if (ImGui.ColorEdit3("Mix Color", ref color, ImGuiColorEditFlags.NoInputs))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.MixColor = new vec4(color.X, color.Y, color.Z, mc.a);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }

                float mixAmount = mc.a;
                if (ImGui.SliderFloat("Mix Amount", ref mixAmount, 0f, 1f))
                {
                    EnsureMaterialSettings();
                    var c = _currentObject.MaterialSettings.MixColor;
                    _currentObject.MaterialSettings.MixColor = new vec4(c.r, c.g, c.b, mixAmount);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Metallic
            {
                float metallic = mat?.Metallic ?? 0f;
                if (ImGui.SliderFloat("Metallic", ref metallic, 0f, 1f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.Metallic = metallic;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Roughness
            {
                float roughness = mat?.Roughness ?? 0.5f;
                if (ImGui.SliderFloat("Roughness", ref roughness, 0f, 1f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.Roughness = roughness;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Emission enabled
            {
                bool emissionEnabled = mat?.EmissionEnabled ?? false;
                if (ImGui.Checkbox("Emission", ref emissionEnabled))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.EmissionEnabled = emissionEnabled;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Auto emission from Minecraft block metadata (schema/block imports)
            {
                bool autoEmission = mat?.AutoEmission ?? true;
                if (ImGui.Checkbox("Auto Emission", ref autoEmission))
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
            }

            // Emission color (no alpha)
            {
                vec4 ec = mat?.EmissionColor ?? new vec4(0f, 0f, 0f, 1f);
                var vec3 = new Vector3(ec.r, ec.g, ec.b);
                if (ImGui.ColorEdit3("Emission Color", ref vec3, ImGuiColorEditFlags.NoInputs))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.EmissionColor = new vec4(vec3.X, vec3.Y, vec3.Z, 1f);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Emission energy
            {
                float emEnergy = mat?.EmissionEnergy ?? 1f;
                if (ImGui.SliderFloat("Emission Energy", ref emEnergy, 0f, 10f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.EmissionEnergy = emEnergy;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Emission indirect-only toggle
            {
                bool emissionIndirectOnly = mat?.EmissionIndirectOnly ?? false;
                if (ImGui.Checkbox("Emission Indirect Only", ref emissionIndirectOnly))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.EmissionIndirectOnly = emissionIndirectOnly;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Subsurface scattering controls (inspired by Mine-imator high-light shaders).
            {
                float sss = mat?.Subsurface ?? 0f;
                if (ImGui.SliderFloat("Subsurface", ref sss, 0f, 1f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.Subsurface = sss;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }

                vec3 sssRadius = mat?.SubsurfaceRadius ?? new vec3(0.42f, 0.24f, 0.14f);
                var sssRadiusVec = new Vector3(sssRadius.x, sssRadius.y, sssRadius.z);
                if (ImGui.DragFloat3("Subsurface Radius RGB", ref sssRadiusVec, 0.01f, 0.0001f, 4f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.SubsurfaceRadius = new vec3(
                        Math.Max(0.0001f, sssRadiusVec.X),
                        Math.Max(0.0001f, sssRadiusVec.Y),
                        Math.Max(0.0001f, sssRadiusVec.Z));
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }

                vec4 sssColor = mat?.SubsurfaceColor ?? new vec4(1f, 1f, 1f, 1f);
                var sssColorVec = new Vector3(sssColor.r, sssColor.g, sssColor.b);
                if (ImGui.ColorEdit3("Subsurface Color", ref sssColorVec, ImGuiColorEditFlags.NoInputs))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.SubsurfaceColor = new vec4(sssColorVec.X, sssColorVec.Y, sssColorVec.Z, 1f);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }

                float sssHighlight = mat?.SubsurfaceHighlight ?? 0.35f;
                if (ImGui.SliderFloat("Subsurface Highlight", ref sssHighlight, -0.95f, 0.95f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.SubsurfaceHighlight = sssHighlight;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }

                float sssHighlightStrength = mat?.SubsurfaceHighlightStrength ?? 0.6f;
                if (ImGui.SliderFloat("Subsurface Highlight Strength", ref sssHighlightStrength, 0f, 4f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.SubsurfaceHighlightStrength = sssHighlightStrength;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Normal map (display only; file picking not yet implemented)
            {
                string normalName = (mat?.NormalTexture != 0) ? "(texture)" : "None";
                ImGui.Text("Normal: " + normalName);
                // TODO: open NativeFileDialog to pick a normal-map texture file
                if (ImGui.Button("Browse##normalBrowse"))
                {
                    // TODO: implement normal map file picker
                }
                ImGui.SameLine();
                if (ImGui.Button("Clear##normalClear") && mat != null)
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.NormalTexture = 0;
                    _currentObject.MaterialSettings.NormalEnabled  = false;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Double Sided
            {
                bool doubleSided = mat?.DoubleSided ?? false;
                if (ImGui.Checkbox("Double Sided", ref doubleSided))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.DoubleSided = doubleSided;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Texture UV transform
            {
                vec2 uvOffset = mat?.TextureOffset ?? vec2.Zero;
                var offset = new Vector2(uvOffset.x, uvOffset.y);
                if (ImGui.DragFloat2("Texture Offset HV", ref offset, 0.01f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.TextureOffset = new vec2(offset.X, offset.Y);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }

                vec2 uvRepeat = mat?.TextureRepeat ?? new vec2(1f, 1f);
                var repeat = new Vector2(uvRepeat.x, uvRepeat.y);
                if (ImGui.DragFloat2("Texture Repeat HV", ref repeat, 0.01f, 0.0001f, 256f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.TextureRepeat = new vec2(Math.Max(0.0001f, repeat.X), Math.Max(0.0001f, repeat.Y));
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }

                bvec2 uvMirror = mat?.TextureMirror ?? new bvec2(false, false);
                bool mirrorH = uvMirror.x;
                bool mirrorV = uvMirror.y;
                bool mirrorChanged = false;

                if (ImGui.Checkbox("Texture Mirror H", ref mirrorH))
                    mirrorChanged = true;

                if (ImGui.Checkbox("Texture Mirror V", ref mirrorV))
                    mirrorChanged = true;

                if (mirrorChanged)
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.TextureMirror = new bvec2(mirrorH, mirrorV);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            ImGui.Spacing();

            // Reset material
            if (ImGui.Button("Reset Material"))
            {
                EnsureMaterialSettings();
                var m = _currentObject.MaterialSettings;
                m.AlbedoColor     = new vec4(1f, 1f, 1f, 1f);
                m.BlendColor      = new vec4(1f, 1f, 1f, 1f);
                m.MixColor        = new vec4(0f, 0f, 0f, 0f);
                m.Metallic        = 0f;
                m.Roughness       = 0.5f;
                m.EmissionEnabled = false;
                m.EmissionEnergy  = 1f;
                m.Subsurface      = 0f;
                m.SubsurfaceRadius = new vec3(0.42f, 0.24f, 0.14f);
                m.SubsurfaceColor = new vec4(1f, 1f, 1f, 1f);
                m.SubsurfaceHighlight = 0.35f;
                m.SubsurfaceHighlightStrength = 0.6f;
                m.EmissionIndirectOnly = false;
                m.AutoEmission = true;
                m.NormalTexture   = 0;
                m.NormalEnabled   = false;
                m.DoubleSided     = false;
                m.TextureOffset   = vec2.Zero;
                m.TextureRepeat   = new vec2(1f, 1f);
                m.TextureMirror   = new bvec2(false, false);
                _currentObject.SetExplicitMaterialSettings();
                _currentObject.PropagateMaterialSettingsToChildren();
            }
        }

        // ── Shape Keys ────────────────────────────────────────────────────────
        if (!isLight && HasAnyShapeKeys(_currentObject) && ImGui.CollapsingHeader("Shape Keys"))
        {
            RenderShapeKeysSection(_currentObject);
        }

        // ── Light (shown only for LightSceneObject) ───────────────────────────
        if (_currentObject is LightSceneObject light)
        {
            if (ImGui.CollapsingHeader("Light"))
            {
                // Light type (point / spot)
                {
                    string[] typeOptions = { "Point", "Spot" };
                    int currentType = (int)light.Type;
                    if (ImGui.Combo("Type##lightType", ref currentType, typeOptions, typeOptions.Length))
                    {
                        light.Type = (LightType)currentType;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.type");
                    }
                }

                // Color
                {
                    var lc   = light.LightColor;
                    var vec3 = new Vector3(lc.r, lc.g, lc.b);
                    if (ImGui.ColorEdit3("Color##lightColor", ref vec3, ImGuiColorEditFlags.NoInputs))
                    {
                        light.LightColor = new vec4(vec3.X, vec3.Y, vec3.Z, 1);
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.color.r");
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.color.g");
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.color.b");
                    }
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
                    if (ImGui.SliderFloat("Specular##lightSpecular", ref specular, 0f, 1f))
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
                    if (ImGui.Checkbox("Cast Shadows##lightShadow", ref shadow))
                        light.LightShadowEnabled = shadow;
                }

                // Spot-only properties
                if (light.Type == LightType.Spot)
                {
                    float angle = light.LightSpotAngle;
                    if (ImGui.SliderFloat("Spot Angle##lightSpotAngle", ref angle, 1f, 170f))
                    {
                        light.LightSpotAngle = angle;
                        light.LightSpotBlend = Math.Min(light.LightSpotBlend, angle * 0.5f);
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.spot_angle");
                    }

                    float blend = light.LightSpotBlend;
                    float maxBlend = Math.Max(0f, light.LightSpotAngle * 0.5f);
                    if (ImGui.SliderFloat("Spot Blend##lightSpotBlend", ref blend, 0f, maxBlend))
                    {
                        light.LightSpotBlend = blend;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.spot_blend");
                    }
                }

                ImGui.Spacing();

                // Reset light
                if (ImGui.Button("Reset Light"))
                {
                    light.Type               = LightType.Point;
                    light.LightEnergy         = 1f;
                    light.LightRange          = 5f;
                    light.LightIndirectEnergy = 1f;
                    light.LightSpecular       = 0.5f;
                    light.LightShadowEnabled  = true;
                    light.LightColor          = new vec4(1f, 1f, 1f, 1f);
                    light.LightSpotAngle      = 45f;
                    light.LightSpotBlend      = 5f;
                }
            }
        }

        // ── Effects (shown only for CameraSceneObject) ────────────────────────
        if (_currentObject is CameraSceneObject cameraObject)
        {
            if (ImGui.CollapsingHeader("Effects"))
            {
                if (_cameraEffectToAddIndex < 0 || _cameraEffectToAddIndex >= CameraEffectAddOptions.Length)
                    _cameraEffectToAddIndex = 0;

                ImGui.TextUnformatted("Effect Type");
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.BeginCombo("##cameraEffectAddCombo", CameraEffectAddOptions[_cameraEffectToAddIndex]))
                {
                    for (int i = 0; i < CameraEffectAddOptions.Length; i++)
                    {
                        bool selected = i == _cameraEffectToAddIndex;
                        if (ImGui.Selectable(CameraEffectAddOptions[i], selected))
                            _cameraEffectToAddIndex = i;
                        if (selected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                if (ImGui.Button("Add Effect +##cameraEffectAddButton", new Vector2(-1f, 0f)))
                {
                    cameraObject.AddEffect((CameraEffectType)_cameraEffectToAddIndex);
                    ProjectManager.Instance.SetDirty(true);
                }

                if (cameraObject.Effects.Count == 0)
                {
                    ImGui.TextDisabled("No effects added.");
                }
                else
                {
                    int removeIndex = -1;

                    for (int i = 0; i < cameraObject.Effects.Count; i++)
                    {
                        var effect = cameraObject.Effects[i];
                        string title = effect.Type switch
                        {
                            CameraEffectType.CameraShake => $"Camera Shake##cameraEffect{i}",
                            CameraEffectType.FilmGrain => $"Film Grain##cameraEffect{i}",
                            _ => $"Effect {i + 1}##cameraEffect{i}"
                        };

                        if (ImGui.TreeNodeEx(title, ImGuiTreeNodeFlags.DefaultOpen))
                        {
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"Remove##cameraEffectRemove{i}"))
                                removeIndex = i;

                            if (effect.Type == CameraEffectType.CameraShake)
                            {
                                bool changed = false;
                                string basePath = $"camera.effect.{i}.shake";
                                int mode = (int)effect.Shake.Mode;
                                if (ImGui.Combo($"Mode##cameraShakeMode{i}", ref mode, CameraShakeModeOptions, CameraShakeModeOptions.Length))
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
                                if (ImGui.SliderFloat($"Trauma##cameraShakeTrauma{i}", ref trauma, 0f, 5f, "%.3f"))
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

                                if (ImGui.Button($"Reset##cameraShakeReset{i}"))
                                {
                                    effect.Shake.Mode = CameraShakeMode.Both;
                                    effect.Shake.Trauma = 1f;
                                    effect.Shake.Strength = new vec3(0.03f, 0.03f, 0.03f);
                                    effect.Shake.Speed = new vec3(3f, 3.5f, 2.5f);
                                    effect.Shake.Offset = vec3.Zero;
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.mode");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.trauma");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.strength.x");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.strength.y");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.strength.z");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.speed.x");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.speed.y");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.speed.z");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.offset.x");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.offset.y");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.offset.z");
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
                                if (ImGui.SliderFloat($"Strength##filmGrainStrength{i}", ref strength, 0f, 1f, "%.3f"))
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
                                if (ImGui.SliderFloat($"Saturation##filmGrainSaturation{i}", ref saturation, 0f, 1f, "%.3f"))
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
                                if (ImGui.SliderFloat($"Size##filmGrainSize{i}", ref size, 0.25f, 8f, "%.2f"))
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
                                if (ImGui.Button($"Reset##filmGrainReset{i}"))
                                {
                                    effect.FilmGrain = new FilmGrainSettings();
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.strength");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.saturation");
                                    Timeline?.RecordAutoKeyframe(cameraObject, $"{basePath}.size");
                                    changed = true;
                                }
                                if (changed) ProjectManager.Instance.SetDirty(true);
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
            }
        }

        if (ImGui.CollapsingHeader("Appearance"))
        {
            bool vis = _currentObject.ObjectVisible;
            if (ImGui.Checkbox("Visible", ref vis))
            {
                ApplyToSelectedObjects(obj =>
                {
                    obj.SetObjectVisible(vis);
                    Timeline?.RecordAutoKeyframe(obj, "visible");
                });
                ProjectManager.Instance.SetDirty(true);
            }

            bool inheritVis = _currentObject.InheritVisibility;
            if (ImGui.Checkbox("Inherit Visibility", ref inheritVis))
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
                if (ImGui.Checkbox("Cast Shadows", ref castShadow))
                {
                    ApplyToSelectedSubtrees(obj => obj.CastShadow = castShadow);
                    ProjectManager.Instance.SetDirty(true);
                }
            }

            bool invertFaces = _currentObject.InvertFaces;
            if (ImGui.Checkbox("Invert (Render Backfaces)", ref invertFaces))
            {
                ApplyToSelectedSubtrees(obj => obj.InvertFaces = invertFaces);
                ProjectManager.Instance.SetDirty(true);
            }

            bool blurTexture = _currentObject.BlurTexture;
            if (ImGui.Checkbox("Blur Texture (Linear Filtering)", ref blurTexture))
            {
                ApplyToSelectedSubtrees(obj => obj.BlurTexture = blurTexture);
                ProjectManager.Instance.SetDirty(true);
            }

            bool textureMipmaps = _currentObject.TextureMipmaps;
            if (ImGui.Checkbox("Texture Filtering (Mip Maps)", ref textureMipmaps))
            {
                ApplyToSelectedSubtrees(obj => obj.TextureMipmaps = textureMipmaps);
                ProjectManager.Instance.SetDirty(true);
            }

            bool includeAo = _currentObject.IncludeInAmbientOcclusion;
            if (ImGui.Checkbox("Include In Ambient Occlusion", ref includeAo))
            {
                ApplyToSelectedSubtrees(obj => obj.IncludeInAmbientOcclusion = includeAo);
                ProjectManager.Instance.SetDirty(true);
            }

            bool includeFog = _currentObject.IncludeInFog;
            if (ImGui.Checkbox("Include In Fog", ref includeFog))
            {
                ApplyToSelectedSubtrees(obj => obj.IncludeInFog = includeFog);
                ProjectManager.Instance.SetDirty(true);
            }

            bool renderHighQuality = _currentObject.RenderInHighQuality;
            if (ImGui.Checkbox("Render In High Quality", ref renderHighQuality))
            {
                ApplyToSelectedSubtrees(obj => obj.RenderInHighQuality = renderHighQuality);
                ProjectManager.Instance.SetDirty(true);
            }

            bool renderLowQuality = _currentObject.RenderInLowQuality;
            if (ImGui.Checkbox("Render In Low Quality", ref renderLowQuality))
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
                if (ImGui.Checkbox("Active Camera", ref active))
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
                if (ImGui.Checkbox("Sharp bends", ref sharpBends))
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
                    ImGui.SetTooltip(sharpBends
                        ? "Preserves Minecraft's blocky limb shape while bending."
                        : "Uses additional segments for a smoother bend.");
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
        vec3 origin = _currentObject?.PivotOffset ?? vec3.Zero;
        vec3 delta = pivot - origin;

        ApplyToSelectedObjects(obj => obj.PivotOffset = obj.PivotOffset + delta);
        ProjectManager.Instance.SetDirty(true);
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

        ImGui.Text(label);
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
                    if (fakeId != 0) // Avoid ID 0 which means "no texture"
                    {
                        _loadedTexturePathCache[fakeId] = filePath;
                    }
                }
            }
        }

        // Then scan primitives to find currently loaded textures and map their real IDs
        foreach (var obj in Viewport.SceneObjects)
        {
            ScanPrimitiveObjectForTextures(obj);
        }

        _cachedTextureIds = _loadedTexturePathCache.Keys.ToArray();
    }

    private void ScanPrimitiveObjectForTextures(SceneObject obj)
    {
        if (obj == null) return;

        // Only scan objects in the Primitives category
        if (string.Equals(obj.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase))
        {
            // Scan this object's meshes for loaded textures
            foreach (var mesh in obj.Visuals.Where(mesh => mesh.TextureId != 0 && !_loadedTexturePathCache.ContainsKey(mesh.TextureId)))
            {
                // Map the real texture ID to its path
                if (!string.IsNullOrEmpty(obj.AlbedoTexturePath))
                {
                    string fullPath = Path.Combine(ProjectManager.Instance.ProjectFolder, obj.AlbedoTexturePath);

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
            ScanPrimitiveObjectForTextures(child);
        }
    }

    /// <summary>
    /// Loads a texture from file and applies it as the albedo texture to all meshes in the current object.
    /// Supports PNG, JPG, BMP, TGA, GIF, WebP, and TIFF formats with RGBA color components.
    /// </summary>
    private unsafe void OnLoadAlbedoTextureForObject(SceneObject obj, string filePath)
    {
        if (obj == null || Gl == null || !File.Exists(filePath))
            return;

        try
        {
            // Flip image vertically on load for OpenGL Y-axis convention
            StbImage.stbi_set_flip_vertically_on_load(1);
            
            var bytes = File.ReadAllBytes(filePath);
            ImageResult img = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            
            // Reset to default behavior
            StbImage.stbi_set_flip_vertically_on_load(0);

            uint tex = Gl.GenTexture();
            Gl.BindTexture(GLEnum.Texture2D, tex);

            fixed (byte* p = img.Data)
                Gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba,
                    (uint)img.Width, (uint)img.Height,
                    0, GLEnum.Rgba, GLEnum.UnsignedByte, p);

            // Use linear filtering for better image quality
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            // Allow wrapping for tileable textures
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.Repeat);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.Repeat);
            Gl.BindTexture(GLEnum.Texture2D, 0);

            // Apply to all meshes
            foreach (var mesh in obj.Visuals)
            {
                // Delete old texture if exists
                if (mesh.TextureId != 0)
                    Gl.DeleteTexture(mesh.TextureId);
                
                mesh.TextureId = tex;
                
                // Configure material for proper rendering
                if (mesh.GetSurfaceCount() > 0)
                {
                    var material = mesh.SurfaceGetMaterial(0);
                    if (material is StandardMaterial stdMat)
                    {
                        stdMat.AlbedoColor = new vec4(1f, 1f, 1f, 1f); // White for full color pass-through
                    }
                }
            }

            // Store the relative path for persistence
            obj.AlbedoTexturePath = ProjectManager.Instance.ToProjectRelativePath(filePath);
        }
        catch (Exception ex)
        {
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
        foreach (var mesh in obj.GetMeshInstancesRecursively())
            if (mesh.HasShapeKeys) return true;
        return false;
    }

    /// <summary>
    /// Draws a scrollable list of every shape key on every mesh that belongs
    /// to <paramref name="obj"/> or its descendants.  Each key gets a slider
    /// in the range [-1, 1] (default 0); a "Reset All" button at the bottom
    /// snaps every weight back to 0.
    /// </summary>
    private void RenderShapeKeysSection(SceneObject obj)
    {
        var meshes = obj.GetMeshInstancesRecursively();
        int keyCount = 0;
        foreach (var m in meshes) if (m.HasShapeKeys) keyCount += m.ShapeKeys.Count;

        ImGui.TextWrapped("Drag any slider to blend in a shape key. " +
                          "Negative values invert the morph. Right-click a slider " +
                          "to add a keyframe.");
        ImGui.Spacing();

        // Search box — filters the list below by shape key name (case-insensitive).
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##shapekeySearch", "Search shape keys...", ref _shapeKeySearch, 128);
        ImGui.Spacing();

        if (ImGui.Button("Reset All Shape Keys##shapekeyResetAll"))
        {
            foreach (var m in meshes) m.ResetShapeKeys();
            ProjectManager.Instance.SetDirty(true);
        }
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
                        ImGui.TextDisabled($"Mesh {meshIndex}");
                        ImGui.Separator();
                        meshHeaderDrawn = true;
                    }

                    anyVisible = true;
                    float weight = sk.Weight;
                    string path  = $"shapekey.{meshIndex}.{i}";
                    string label = $"{sk.Name}##mesh{meshIndex}_key{i}";

                    ImGui.SetNextItemWidth(-60f);
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
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"0##reset{meshIndex}_{i}"))
                    {
                        mesh.SetShapeKeyWeight(i, 0f);
                        ProjectManager.Instance.SetDirty(true);
                        Timeline?.RecordAutoKeyframe(obj, path);
                    }
                }
                meshIndex++;
            }

            if (!anyVisible && !string.IsNullOrEmpty(_shapeKeySearch))
                ImGui.TextDisabled("No shape keys match your search.");
        }
        ImGui.EndChild();
    }

    // ── Property context menu ─────────────────────────────────────────────────

    private void RenderPropertyContextMenu()
    {
        if (!ImGui.BeginPopup("##prop_keyframe_ctx")) return;
        if (_ctxPropertyPath == null) { ImGui.EndPopup(); return; }

        ImGui.TextDisabled("Keyframe");
        ImGui.Separator();

        if (ImGui.MenuItem("Add Keyframe at Current Frame"))
        {
            if (_ctxPropertyPath.StartsWith("background:", StringComparison.Ordinal))
                AddBackgroundKeyframeGroup(_ctxPropertyPath[11..], Timeline?.CurrentFrame ?? 0);
            else if (_currentObject != null)
                Timeline?.AddKeyframeForProperty(_currentObject, _ctxPropertyPath, Timeline?.CurrentFrame ?? 0);
        }

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
