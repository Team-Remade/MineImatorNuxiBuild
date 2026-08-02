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
    public bool ShadowsEnabled = true;
    public int SunShadowBufferSize = 2048;
    public int SpotShadowBufferSize = 1024;
    public int PointShadowBufferSize = 1024;
    public float ShadowBlurStrength = 1f;

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
        ShadowsEnabled = settings.ShadowsEnabled;
        SunShadowBufferSize = NormalizeShadowBufferSize(settings.SunShadowBufferSize, 2048);
        SpotShadowBufferSize = NormalizeShadowBufferSize(settings.SpotShadowBufferSize, 1024);
        PointShadowBufferSize = NormalizeShadowBufferSize(settings.PointShadowBufferSize, 1024);
        ShadowBlurStrength = Math.Clamp(settings.ShadowBlurStrength, 0f, 4f);
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
        manifest.Settings.ShadowsEnabled = ShadowsEnabled;
        manifest.Settings.SunShadowBufferSize = NormalizeShadowBufferSize(SunShadowBufferSize, 2048);
        manifest.Settings.SpotShadowBufferSize = NormalizeShadowBufferSize(SpotShadowBufferSize, 1024);
        manifest.Settings.PointShadowBufferSize = NormalizeShadowBufferSize(PointShadowBufferSize, 1024);
        manifest.Settings.ShadowBlurStrength = Math.Clamp(ShadowBlurStrength, 0f, 4f);

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

        var atlas = atlasSource == ItemAtlasSource.BlockAtlas ? TerrainAtlas.Textures : ItemsAtlas.Textures;
        return atlas.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
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
            ItemTileKey = source.ItemTileKey,
            ItemIs3D = source.ItemIs3D,
            PrimitivePlaneOrientation = source.PrimitivePlaneOrientation,
            PrimitivePlaneFaceCamera = source.PrimitivePlaneFaceCamera,
            PrimitiveCubeMapped = source.PrimitiveCubeMapped,
            CameraFov = source.CameraFov,
            CameraNear = source.CameraNear,
            CameraFar = source.CameraFar,
            CameraActive = source.CameraActive,
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
            bool resolutionChanged = ImGui.InputInt("##ResWidth", ref _resolutionWidth, 0, 0, ImGuiInputTextFlags.None);
            ImGui.SameLine();
            ImGui.Text(" x ");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            resolutionChanged |= ImGui.InputInt("##ResHeight", ref _resolutionHeight, 0, 0, ImGuiInputTextFlags.None);
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
            bool frameRateChanged = ImGui.InputInt("##Framerate", ref _framerate, 0, 0, ImGuiInputTextFlags.None);
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
            bool textureFpsChanged = ImGui.InputInt("##TexAnimSpeed", ref TextureAnimationFps, 0, 0, ImGuiInputTextFlags.None);
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
                        skyChanged |= ImGui.DragFloat("Density", ref StarDensity, 0.01f, 0f, 5f, "%.2f");
                        RegisterBackgroundKeyframeContext(nameof(StarDensity));
                        skyChanged |= ImGui.DragFloat("Brightness", ref StarBrightness, 0.01f, 0f, 5f, "%.2f");
                        RegisterBackgroundKeyframeContext(nameof(StarBrightness));
                        skyChanged |= ImGui.DragFloat("Twinkle Speed", ref StarTwinkleSpeed, 0.01f, 0f, 5f, "%.2f");
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
                    skyChanged |= ImGui.DragFloat("Cloud Speed", ref CloudSpeed, 1f, -10000f, 10000f, "%.0f px/s");
                    RegisterBackgroundKeyframeContext(nameof(CloudSpeed));
                    fixed (float* value = CloudOffset) skyChanged |= ImGui.DragFloat2("Cloud Offset", value, 1f, -100000f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudOffset));
                    skyChanged |= ImGui.DragFloat("Cloud Height", ref CloudHeight, 1f, 0f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudHeight));
                    skyChanged |= ImGui.DragFloat("Cloud Block Size", ref CloudBlockSize, 1f, 1f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudBlockSize));
                    skyChanged |= ImGui.DragFloat("Cloud Thickness", ref CloudThickness, 1f, 1f, 100000f, "%.0f px");
                    RegisterBackgroundKeyframeContext(nameof(CloudThickness));
                    skyChanged |= ImGui.SliderInt("Moon Phase", ref MoonPhase, 0, 7);
                    RegisterBackgroundKeyframeContext(nameof(MoonPhase));
                    skyChanged |= ImGui.SliderFloat("Time", ref SkyTime, 0f, 24f, "%.2f h");
                    RegisterBackgroundKeyframeContext(nameof(SkyTime));
                    skyChanged |= ImGui.DragFloat("Sun Size (degrees)", ref SunSize, 0.1f, 0.1f, 90f);
                    RegisterBackgroundKeyframeContext(nameof(SunSize));
                    fixed (float* value = SunAngle) skyChanged |= ImGui.DragFloat3("Sun Angle (XYZ)", value, 0.25f, -360f, 360f);
                    RegisterBackgroundKeyframeContext(nameof(SunAngle));
                    skyChanged |= ImGui.DragFloat("Moon Size (degrees)", ref MoonSize, 0.1f, 0.1f, 90f);
                    RegisterBackgroundKeyframeContext(nameof(MoonSize));
                    fixed (float* value = MoonAngle) skyChanged |= ImGui.DragFloat3("Moon Angle (XYZ)", value, 0.25f, -360f, 360f);
                    RegisterBackgroundKeyframeContext(nameof(MoonAngle));
                    skyChanged |= SkyColorEditor("Sun Fill Light", SunFillLightColor);
                    skyChanged |= ImGui.DragFloat("Sun Fill Strength", ref SunFillLightStrength, 0.01f, 0f, 5f);
                    RegisterBackgroundKeyframeContext(nameof(SunFillLightStrength));
                    skyChanged |= ImGui.Checkbox("Sun Fill Casts Shadows", ref SunFillLightCastsShadows);
                    RegisterBackgroundKeyframeContext(nameof(SunFillLightCastsShadows));
                    skyChanged |= SkyColorEditor("Moon Fill Light", MoonFillLightColor);
                    skyChanged |= ImGui.DragFloat("Moon Fill Strength", ref MoonFillLightStrength, 0.01f, 0f, 5f);
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
                    fogChanged |= ImGui.DragFloat("Distance", ref FogDistance, 10f, 0f, 1000000f, "%.0f px");
                    fogChanged |= ImGui.DragFloat("Fade Size", ref FogFadeSize, 10f, 1f, 1000000f, "%.0f px");
                    fogChanged |= ImGui.DragFloat("Height", ref FogHeight, 10f, -1000000f, 1000000f, "%.0f px");
                    fogChanged |= ImGui.Checkbox("Height Fog", ref HeightFog);
                    if (HeightFog)
                    {
                        fogChanged |= ImGui.Checkbox("Custom Height Fog Color", ref CustomHeightFogColor);
                        if (CustomHeightFogColor) fogChanged |= SkyColorEditor("Height Fog Color", HeightFogColor);
                        fogChanged |= ImGui.DragFloat("Height Fog Size", ref HeightFogSize, 10f, 1f, 1000000f, "%.0f px");
                        fogChanged |= ImGui.DragFloat("Height Fog Offset", ref HeightFogOffset, 10f, -1000000f, 1000000f, "%.0f px");
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
                if (ImGui.DragFloat("Background Scale", ref backgroundScale, 0.01f, 0.01f, 20f))
                {
                    BackgroundScale = Math.Clamp(backgroundScale, 0.01f, 20f);
                    backgroundChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(BackgroundScale));

                float backgroundRotation = BackgroundRotationDegrees;
                if (ImGui.DragFloat("Background Rotation", ref backgroundRotation, 0.25f, -360f, 360f))
                {
                    BackgroundRotationDegrees = Math.Clamp(backgroundRotation, -360f, 360f);
                    backgroundChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(BackgroundRotationDegrees));

                float offsetX = BackgroundOffset[0];
                if (ImGui.DragFloat("Background Offset X", ref offsetX, 0.005f, -3f, 3f))
                {
                    BackgroundOffset[0] = offsetX;
                    backgroundChanged = true;
                }
                RegisterBackgroundKeyframeContext(nameof(BackgroundOffset) + ".0");

                float offsetY = BackgroundOffset[1];
                if (ImGui.DragFloat("Background Offset Y", ref offsetY, 0.005f, -3f, 3f))
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
                if (ImGui.DragFloat("Ambient Strength", ref ambientStrength, 0.01f, 0f, 5f))
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
                if (ImGui.DragFloat("Night Ambient Strength", ref nightAmbientStrength, 0.01f, 0f, 5f))
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
                if (ImGui.DragFloat("Fill Strength", ref fillStrength, 0.01f, 0f, 5f))
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
            changed |= ImGui.DragFloat("Radius (px)", ref AmbientOcclusionRadius, 0.25f, 0f, 128f, "%.1f");
            changed |= PercentageSlider("Strength##ao", ref AmbientOcclusionStrength, 0f, 200f);
            changed |= ImGui.SliderInt("Samples##ao", ref AmbientOcclusionSampleCount, 1, 128);
            fixed (float* color = AmbientOcclusionColor)
                changed |= ImGui.ColorEdit3("Color##ao", color, ImGuiColorEditFlags.NoInputs);
            changed |= ImGui.DragFloat("Ratio##ao", ref AmbientOcclusionRatio, 0.001f, 0f, 1f, "%.3f");
            changed |= ImGui.DragFloat("Ratio Balance##ao", ref AmbientOcclusionRatioBalance, 0.005f, 0f, 1f, "%.3f");
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
        if (current is float floatValue && ImGui.DragFloat("Value##BackgroundKeyValue", ref floatValue, 0.01f))
            SetBackgroundPropertyValue(_selectedBackgroundKeyProperty, floatValue.ToString(CultureInfo.InvariantCulture), false);
        else if (current is int intValue && ImGui.InputInt("Value##BackgroundKeyValue", ref intValue))
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
                _currentObject.InheritPosition = inheritPos;

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
            if (ImGui.DragFloat("X##posX", ref posX, 0.1f))
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
            if (ImGui.DragFloat("Y##posY", ref posY, 0.1f))
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
            if (ImGui.DragFloat("Z##posZ", ref posZ, 0.1f))
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
                _currentObject.InheritRotation = inheritRot;

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
            if (ImGui.DragFloat("X##rotX", ref rotX, 0.5f))
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
            if (ImGui.DragFloat("Y##rotY", ref rotY, 0.5f))
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
            if (ImGui.DragFloat("Z##rotZ", ref rotZ, 0.5f))
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
                _currentObject.InheritScale = inheritScale;

            ImGui.Checkbox("Link Scale", ref _linkScale);

            vec3 curScale = (_currentObject is MiBoneSceneObject miScale)
                ? miScale.OffsetScale
                : _currentObject.LocalScale;
            float scaleX = curScale.x;
            float scaleY = curScale.y;
            float scaleZ = curScale.z;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            
            // Scale X
            if (ImGui.DragFloat("X##scaleX", ref scaleX, 0.01f, 0.001f, float.MaxValue))
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
            if (ImGui.DragFloat("Y##scaleY", ref scaleY, 0.01f, 0.001f, float.MaxValue))
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
            if (ImGui.DragFloat("Z##scaleZ", ref scaleZ, 0.01f, 0.001f, float.MaxValue))
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
            vec3 angle = bend.Angle;
            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);

            if (bend.AxisX)
            {
                float value = angle.x;
                if (ImGui.DragFloat("X##bendX", ref value, 0.5f, bend.DirectionMin.x, bend.DirectionMax.x))
                {
                    angle.x = value;
                    ApplyBend(bendBone, angle);
                }
            }
            if (bend.AxisY)
            {
                float value = angle.y;
                if (ImGui.DragFloat("Y##bendY", ref value, 0.5f, bend.DirectionMin.y, bend.DirectionMax.y))
                {
                    angle.y = value;
                    ApplyBend(bendBone, angle);
                }
            }
            if (bend.AxisZ)
            {
                float value = angle.z;
                if (ImGui.DragFloat("Z##bendZ", ref value, 0.5f, bend.DirectionMin.z, bend.DirectionMax.z))
                {
                    angle.z = value;
                    ApplyBend(bendBone, angle);
                }
            }
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##bendReset"))
                ApplyBend(bendBone, vec3.Zero);
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

            if (ImGui.DragInt("X##tileX", ref tileX, 1f, 1, SceneObject.MaxTilesPerAxis))
                tileChanged = true;
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "tile.x";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }

            if (ImGui.DragInt("Y##tileY", ref tileY, 1f, 1, SceneObject.MaxTilesPerAxis))
                tileChanged = true;
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "tile.y";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }

            if (ImGui.DragInt("Z##tileZ", ref tileZ, 1f, 1, SceneObject.MaxTilesPerAxis))
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

        // ── Pivot Offset ──────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Pivot Offset"))
        {
            bool inheritPivot = _currentObject.InheritPivotOffset;
            if (ImGui.Checkbox("Inherit Pivot##pivot", ref inheritPivot))
                _currentObject.InheritPivotOffset = inheritPivot;

            vec3 pivot = _currentObject.PivotOffset;
            float pivX = pivot.x * 16f;
            float pivY = pivot.y * 16f;
            float pivZ = pivot.z * 16f;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            if (ImGui.DragFloat("X##pivX", ref pivX, 0.1f))
                _currentObject.PivotOffset = new vec3(pivX / 16f, pivY / 16f, pivZ / 16f);

            if (ImGui.DragFloat("Y##pivY", ref pivY, 0.1f))
                _currentObject.PivotOffset = new vec3(pivX / 16f, pivY / 16f, pivZ / 16f);

            if (ImGui.DragFloat("Z##pivZ", ref pivZ, 0.1f))
                _currentObject.PivotOffset = new vec3(pivX / 16f, pivY / 16f, pivZ / 16f);
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##pivReset"))
                _currentObject.PivotOffset = vec3.Zero;
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
                var atlasSource = string.Equals(_currentObject.TextureType, "block", StringComparison.OrdinalIgnoreCase)
                    ? ItemAtlasSource.BlockAtlas
                    : ItemAtlasSource.ItemAtlas;

                string currentKey = ExtractItemTileKeyFromObjectType(_currentObject.ObjectType)
                                    ?? GetItemAtlasKeys(atlasSource).FirstOrDefault()
                                    ?? "";

                string atlasLabel = atlasSource == ItemAtlasSource.BlockAtlas ? "Block Atlas" : "Item Atlas";
                if (ImGui.BeginCombo("Item Atlas", atlasLabel))
                {
                    bool useItem = atlasSource == ItemAtlasSource.ItemAtlas;
                    if (ImGui.Selectable("Item Atlas", useItem))
                    {
                        atlasSource = ItemAtlasSource.ItemAtlas;
                        currentKey = GetItemAtlasKeys(atlasSource).FirstOrDefault() ?? currentKey;
                    }

                    bool useBlock = atlasSource == ItemAtlasSource.BlockAtlas;
                    if (ImGui.Selectable("Block Atlas", useBlock))
                    {
                        atlasSource = ItemAtlasSource.BlockAtlas;
                        currentKey = GetItemAtlasKeys(atlasSource).FirstOrDefault() ?? currentKey;
                    }

                    ImGui.EndCombo();
                }

                ImGui.Text("Item Image:");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##ItemImageKey", string.IsNullOrWhiteSpace(currentKey) ? "(none)" : currentKey))
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
                    if (ImGui.DragFloat("Energy##lightEnergy", ref energy, 0.05f, 0f, 100f))
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
                    if (ImGui.DragFloat("Range##lightRange", ref range, 0.1f, 0.01f, 500f))
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
                    if (ImGui.DragFloat("Indirect Energy##lightIndirect", ref indirect, 0.05f, 0f, 16f))
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

        if (ImGui.CollapsingHeader("Appearance"))
        {
            bool vis = _currentObject.ObjectVisible;
            if (ImGui.Checkbox("Visible", ref vis))
            {
                _currentObject.SetObjectVisible(vis);
                ApplyToDescendants(_currentObject, child => child.SetObjectVisible(vis));
                ProjectManager.Instance.SetDirty(true);
                Timeline?.RecordAutoKeyframe(_currentObject, "visible");
            }

            bool inheritVis = _currentObject.InheritVisibility;
            if (ImGui.Checkbox("Inherit Visibility", ref inheritVis))
            {
                _currentObject.InheritVisibility = inheritVis;
                ApplyToDescendants(_currentObject, child => child.InheritVisibility = inheritVis);
                ProjectManager.Instance.SetDirty(true);
            }

            // Hide Cast Shadows for cameras and point lights
            if (!(_currentObject is CameraSceneObject) && !(_currentObject is LightSceneObject))
            {
                bool castShadow = _currentObject.CastShadow;
                if (ImGui.Checkbox("Cast Shadows", ref castShadow))
                {
                    ApplyToSubtree(_currentObject, obj => obj.CastShadow = castShadow);
                    ProjectManager.Instance.SetDirty(true);
                }
            }

            bool invertFaces = _currentObject.InvertFaces;
            if (ImGui.Checkbox("Invert (Render Backfaces)", ref invertFaces))
            {
                ApplyToSubtree(_currentObject, obj => obj.InvertFaces = invertFaces);
                ProjectManager.Instance.SetDirty(true);
            }

            bool blurTexture = _currentObject.BlurTexture;
            if (ImGui.Checkbox("Blur Texture (Linear Filtering)", ref blurTexture))
            {
                ApplyToSubtree(_currentObject, obj => obj.BlurTexture = blurTexture);
                ProjectManager.Instance.SetDirty(true);
            }

            bool textureMipmaps = _currentObject.TextureMipmaps;
            if (ImGui.Checkbox("Texture Filtering (Mip Maps)", ref textureMipmaps))
            {
                ApplyToSubtree(_currentObject, obj => obj.TextureMipmaps = textureMipmaps);
                ProjectManager.Instance.SetDirty(true);
            }

            bool includeAo = _currentObject.IncludeInAmbientOcclusion;
            if (ImGui.Checkbox("Include In Ambient Occlusion", ref includeAo))
            {
                ApplyToSubtree(_currentObject, obj => obj.IncludeInAmbientOcclusion = includeAo);
                ProjectManager.Instance.SetDirty(true);
            }

            bool includeFog = _currentObject.IncludeInFog;
            if (ImGui.Checkbox("Include In Fog", ref includeFog))
            {
                ApplyToSubtree(_currentObject, obj => obj.IncludeInFog = includeFog);
                ProjectManager.Instance.SetDirty(true);
            }

            bool renderHighQuality = _currentObject.RenderInHighQuality;
            if (ImGui.Checkbox("Render In High Quality", ref renderHighQuality))
            {
                ApplyToSubtree(_currentObject, obj => obj.RenderInHighQuality = renderHighQuality);
                ProjectManager.Instance.SetDirty(true);
            }

            bool renderLowQuality = _currentObject.RenderInLowQuality;
            if (ImGui.Checkbox("Render In Low Quality", ref renderLowQuality))
            {
                ApplyToSubtree(_currentObject, obj => obj.RenderInLowQuality = renderLowQuality);
                ProjectManager.Instance.SetDirty(true);
            }

            float renderDepth = _currentObject.RenderDepthOffset;
            if (ImGui.DragFloat("Render Depth", ref renderDepth, 0.01f, -1000f, 1000f, "%.2f"))
            {
                ApplyToSubtree(_currentObject, obj => obj.RenderDepthOffset = renderDepth);
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
                    character.ModelBendStyle = sharpBends ? BendStyle.Blocky : BendStyle.Realistic;
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

    private void ApplyPosition(vec3 pos)
    {
        // MiBoneSceneObjects: pos is an offset from the base pose.
        // Plain BoneSceneObjects (GLB): pos is an offset from the rest pose.
        if (_currentObject is MiBoneSceneObject miPos)
            miPos.OffsetPosition = pos;
        else if (_currentObject is BoneSceneObject bone)
            bone.TargetPosition = pos;
        else
            _currentObject.SetLocalPosition(pos);
        Timeline?.RecordAutoKeyframe(_currentObject, "position.x");
        Timeline?.RecordAutoKeyframe(_currentObject, "position.y");
        Timeline?.RecordAutoKeyframe(_currentObject, "position.z");
    }

    private void ApplyRotation(vec3 rot)
    {
        if (_currentObject is MiBoneSceneObject miRot)
            miRot.OffsetRotation = rot;
        else if (_currentObject is BoneSceneObject bone)
            bone.TargetRotation = rot;
        else
            _currentObject.SetLocalRotation(rot);
        Timeline?.RecordAutoKeyframe(_currentObject, "rotation.x");
        Timeline?.RecordAutoKeyframe(_currentObject, "rotation.y");
        Timeline?.RecordAutoKeyframe(_currentObject, "rotation.z");
    }

    private void ApplyScale(vec3 scale)
    {
        if (_currentObject is MiBoneSceneObject miScale)
            miScale.OffsetScale = scale;
        else
            _currentObject.SetLocalScale(scale);
        Timeline?.RecordAutoKeyframe(_currentObject, "scale.x");
        Timeline?.RecordAutoKeyframe(_currentObject, "scale.y");
        Timeline?.RecordAutoKeyframe(_currentObject, "scale.z");
    }

    private void ApplyBend(MiBoneSceneObject bone, vec3 angle)
    {
        bone.SetBendAngle(angle);
        ProjectManager.Instance.SetDirty(true);
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

        _currentObject.TileX = tileX;
        _currentObject.TileY = tileY;
        _currentObject.TileZ = tileZ;

        Timeline?.RecordAutoKeyframe(_currentObject, "tile.x");
        Timeline?.RecordAutoKeyframe(_currentObject, "tile.y");
        Timeline?.RecordAutoKeyframe(_currentObject, "tile.z");

        if (SpawnMenu != null && string.Equals(_currentObject.SpawnCategory, "Blocks", StringComparison.Ordinal))
        {
            if (SpawnMenu.RebuildBlockMeshes(_currentObject))
                ProjectManager.Instance.SetDirty(true);
        }
    }

    private void ApplyCubeUvMapping(bool mapped)
    {
        if (_currentObject == null) return;

        var cubeMesh = _currentObject.Visuals.OfType<CubeMesh>().FirstOrDefault();
        if (cubeMesh != null)
        {
            cubeMesh.SetMapped(mapped);
            _currentObject.PrimitiveCubeMapped = mapped;
            ProjectManager.Instance.SetDirty(true);
            return;
        }

        if (Gl == null) return;

        uint existingTextureId = _currentObject.Visuals.FirstOrDefault()?.TextureId ?? 0;
        foreach (var mesh in _currentObject.Visuals.ToList())
            mesh.Dispose();
        _currentObject.Visuals.Clear();

        var rebuilt = new CubeMesh(Gl, mapped)
        {
            TextureId = existingTextureId
        };
        _currentObject.AddMesh(rebuilt);
        _currentObject.PrimitiveCubeMapped = mapped;
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
