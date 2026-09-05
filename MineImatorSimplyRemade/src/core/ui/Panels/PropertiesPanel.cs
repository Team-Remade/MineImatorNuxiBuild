using System.Numerics;
using System.Globalization;
using System.Reflection;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.mdl.material.materials;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using NativeFileDialogSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// UI-agnostic model for the Properties panel (project settings, render
/// settings, background/sky, object library, per-object transforms).
/// The Avalonia view lives in <c>core/ui/Dock/PropertiesView.axaml</c>;
/// viewport-dependent behaviour is injected via the hook properties
/// until the Viewport itself is ported.
/// </summary>
public class PropertiesPanel
{
    private const string NoImageSelected = "No image selected";
    private const string BackgroundModeStretch = "stretch";
    private const string BackgroundModeFit = "fit";
    private const string BackgroundModeOriginal = "original";

    // ── Project tab state ─────────────────────────────────────────────────────

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

    /// <summary>Project display name; persisted via <see cref="WriteProjectSettingsToManifest"/>.</summary>
    public string ProjectName
    {
        get => _projectName;
        set => _projectName = string.IsNullOrWhiteSpace(value) ? "Untitled Project" : value;
    }

    /// <summary>Currently selected property path in the background-animation editor.</summary>
    public string SelectedBackgroundKeyProperty
    {
        get => _selectedBackgroundKeyProperty;
        set => _selectedBackgroundKeyProperty = string.IsNullOrWhiteSpace(value) ? "SkyTime" : value;
    }

    /// <summary>All keyframeable background property paths.</summary>
    public static IReadOnlyList<string> BackgroundKeyframePropertyPaths => BackgroundKeyframeProperties;
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

    /// <summary>Raised when the object library should be rebuilt by its view.</summary>
    public event Action? ObjectLibraryChanged;

    /// <summary>Filter text for the object library tree.</summary>
    public string LibrarySearch { get => _librarySearch; set => _librarySearch = value ?? ""; }

    /// <summary>LibraryEntryId of the selected library tree node.</summary>
    public string SelectedLibraryEntryId { get => _selectedLibraryEntryId; set => _selectedLibraryEntryId = value ?? ""; }

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

    private SceneObject? _currentObject;

    /// <summary>First selected object, refreshed by SelectionManager events.</summary>
    public SceneObject? CurrentObject => _currentObject;

    // ── Texture tracking for dropdown ────────────────────────────────────────
    private Dictionary<uint, string> _loadedTexturePathCache = new();

    /// <summary>Texture id → file path map built by <see cref="RefreshLoadedTextures"/>.</summary>
    public IReadOnlyDictionary<uint, string> LoadedTexturePaths => _loadedTexturePathCache;

    // ── Public wiring ─────────────────────────────────────────────────────────

    /// <summary>Set from MainWindow after both panels are initialised.</summary>
    public Timeline? Timeline { get; set; }
    public SpawnMenu? SpawnMenu { get; set; }

    // Viewport hooks — injected by the host until the Viewport is ported
    // (same pattern as Timeline.SceneObjectsProvider).

    /// <summary>Returns the viewport's root scene objects (mutable list).</summary>
    public Func<IList<SceneObject>>? SceneObjectsProvider { get; set; }

    /// <summary>Shows/hides the viewport ground plane.</summary>
    public Action<bool>? SetGroundPlaneVisible { get; set; }

    /// <summary>Sets the ground plane texture (atlas, tileKey); false if the tile is unknown.</summary>
    public Func<string, string, bool>? SetGroundPlaneTexture { get; set; }

    /// <summary>Sets the viewport background image (path, mode, scale, rotationDegrees, offset).</summary>
    public Action<string, string, float, float, Vector2>? SetBackgroundImage { get; set; }

    /// <summary>Spawns a scene object from a library entry (old ProjectSceneSerializer.SpawnObjectFromEntry call).</summary>
    public Func<ProjectSceneObjectEntry, SceneObject?>? SpawnFromLibraryEntry { get; set; }

    /// <summary>Reloads sun/moon/cloud sky textures in the viewport after a texture selection change.</summary>
    public Action? ReloadSkyTextures { get; set; }

    /// <summary>
    /// Pushes the clamped ambient/fill light values to the renderer. The old
    /// code wrote static <c>Mesh.GlobalAmbient*</c>/<c>GlobalFillLight*</c>
    /// globals; wire this once the Veldrid renderer exposes its equivalent.
    /// </summary>
    public Action<PropertiesPanel>? AmbientRendererSink { get; set; }

    /// <summary>Raised after project settings were (re)loaded from a manifest so views can refresh.</summary>
    public event Action? SettingsLoaded;

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
        SettingsLoaded?.Invoke();
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

    public void ApplyAmbientSettingsToRenderer()
    {
        AmbientLightColor[0] = Math.Clamp(AmbientLightColor[0], 0f, 1f);
        AmbientLightColor[1] = Math.Clamp(AmbientLightColor[1], 0f, 1f);
        AmbientLightColor[2] = Math.Clamp(AmbientLightColor[2], 0f, 1f);
        AmbientLightStrength = Math.Clamp(AmbientLightStrength, 0f, 5f);

        FillLightColor[0] = Math.Clamp(FillLightColor[0], 0f, 1f);
        FillLightColor[1] = Math.Clamp(FillLightColor[1], 0f, 1f);
        FillLightColor[2] = Math.Clamp(FillLightColor[2], 0f, 1f);
        FillLightStrength = Math.Clamp(FillLightStrength, 0f, 5f);

        AmbientRendererSink?.Invoke(this);
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

    public IEnumerable<string> GetFloorAtlasKeys()
    {
        if (NormalizeFloorAtlas(FloorTextureAtlas) == "item")
            ItemsAtlas.EnsureProjectCustomTexturesLoaded();

        var atlas = NormalizeFloorAtlas(FloorTextureAtlas) == "item" ? ItemsAtlas.Textures : TerrainAtlas.Textures;
        return atlas.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    public void ApplyFloorSettingsToViewport()
    {
        FloorTextureAtlas = NormalizeFloorAtlas(FloorTextureAtlas);
        SetGroundPlaneVisible?.Invoke(FloorVisible);

        if (SetGroundPlaneTexture == null)
            return;

        if (!SetGroundPlaneTexture(FloorTextureAtlas, FloorTileKey))
        {
            string? fallback = GetFloorAtlasKeys().FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                FloorTileKey = fallback;
                SetGroundPlaneTexture(FloorTextureAtlas, FloorTileKey);
            }
        }
    }

    public void ApplyBackgroundSettingsToViewport()
    {
        SetBackgroundImage?.Invoke(
            BackgroundImagePath,
            BackgroundRenderMode,
            BackgroundScale,
            BackgroundRotationDegrees,
            new Vector2(BackgroundOffset[0], BackgroundOffset[1]));
    }

    public IReadOnlyList<ProjectAssetEntry> GetBackgroundImageAssets()
    {
        return ProjectManager.Instance
            .GetProjectAssets()
            .Where(asset => asset.AssetType == ProjectAssetType.Image)
            .ToList();
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
            CameraTextureObjectId = source.CameraTextureObjectId,
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
            PrimitiveSphereSmooth = source.PrimitiveSphereSmooth,
            PrimitiveSphereSegments = source.PrimitiveSphereSegments,
            PrimitiveSphereRings = source.PrimitiveSphereRings,
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

    public bool ShouldShowLibraryEntry(ProjectSceneObjectEntry entry)
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

    public static string GetLibraryDisplayLabel(ProjectSceneObjectEntry entry)
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

        var sceneRoots = SceneObjectsProvider?.Invoke();
        if (sceneRoots == null)
            return;

        foreach (var root in sceneRoots)
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
        var sceneRoots = SceneObjectsProvider?.Invoke();
        if (sceneRoots == null)
            yield break;

        foreach (var root in sceneRoots)
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

    public int CountLibraryUsage(string libraryEntryId)
    {
        if (string.IsNullOrWhiteSpace(libraryEntryId))
            return 0;

        return EnumerateSceneObjects().Count(obj =>
            string.Equals(obj.LibrarySourceId, libraryEntryId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Root entries of the library tree sorted by name; ensures library ids are initialised.</summary>
    public IReadOnlyList<ProjectSceneObjectEntry> GetLibraryTreeRoots()
    {
        if (!ProjectManager.Instance.HasProject)
            return Array.Empty<ProjectSceneObjectEntry>();

        var manifest = ProjectManager.Instance.Manifest;
        EnsureObjectLibraryInitialized(manifest);
        return BuildLibraryTreeRoots(manifest.ObjectLibrary)
            .OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Synchronizes scene roots into the object library and refreshes its view.</summary>
    public void SynchronizeObjectLibrary()
    {
        GetLibraryTreeRoots();
        ObjectLibraryChanged?.Invoke();
    }

    public ProjectSceneObjectEntry? GetSelectedLibraryEntry()
    {
        if (!ProjectManager.Instance.HasProject)
            return null;

        var library = ProjectManager.Instance.Manifest?.ObjectLibrary;
        return library == null ? null : FindLibraryEntryById(library, _selectedLibraryEntryId);
    }

    /// <summary>Spawns a new scene object from a library entry's base state (old "Create In Scene From Base").</summary>
    public SceneObject? CreateInSceneFromLibraryEntry(ProjectSceneObjectEntry entry)
    {
        if (entry == null || SpawnFromLibraryEntry == null)
            return null;

        SceneObject? created = SpawnFromLibraryEntry(entry);
        if (created != null)
        {
            SelectionManager.Instance?.ClearSelection();
            SelectionManager.Instance?.SelectObject(created);
            ProjectManager.Instance.SetDirty(true);
        }

        return created;
    }

    /// <summary>Deletes a library entry and every scene object spawned from it.</summary>
    public void DeleteLibraryEntry(ProjectSceneObjectEntry entry)
    {
        if (entry == null || !ProjectManager.Instance.HasProject)
            return;

        var manifest = ProjectManager.Instance.Manifest;
        string nextSelectionId = GetNextLibrarySelectionIdAfterDeletion(manifest.ObjectLibrary, entry.LibraryEntryId);
        RemoveLibraryEntry(manifest, entry.LibraryEntryId);
        RemoveSceneObjectsFromLibrary(entry.LibraryEntryId);
        _selectedLibraryEntryId = nextSelectionId;
        ProjectManager.Instance.SetDirty(true);
    }

    /// <summary>Duplicates a library entry with fresh unique ids and a " Copy" name.</summary>
    public void DuplicateLibraryEntry(ProjectSceneObjectEntry entry)
    {
        if (entry == null || !ProjectManager.Instance.HasProject)
            return;

        var manifest = ProjectManager.Instance.Manifest;
        var newEntry = CloneLibraryEntryRecursive(entry);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectLibraryEntryIds(manifest.ObjectLibrary, usedIds);
        EnsureUniqueLibraryIdsRecursive(newEntry, usedIds);
        newEntry.Name = EnsureUniqueLibraryName(manifest.ObjectLibrary, (entry.Name ?? "Object") + " Copy");
        manifest.ObjectLibrary.Add(newEntry);
        _selectedLibraryEntryId = newEntry.LibraryEntryId;
        ProjectManager.Instance.SetDirty(true);
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
        var sceneRoots = SceneObjectsProvider?.Invoke();
        if (sceneRoots == null || string.IsNullOrWhiteSpace(libraryEntryId))
            return;

        foreach (var root in sceneRoots.ToList())
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
            SceneObjectsProvider?.Invoke()?.Remove(obj);
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

    public static string GetBackgroundImageLabel(string backgroundImagePath)
    {
        return string.IsNullOrWhiteSpace(backgroundImagePath) ||
               string.Equals(backgroundImagePath, NoImageSelected, StringComparison.OrdinalIgnoreCase)
            ? NoImageSelected
            : Path.GetFileName(backgroundImagePath);
    }

    public bool ImportBackgroundImageFromDialog()
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

    public object? GetBackgroundPropertyValue(string path)
    {
        var resolved = ResolveBackgroundProperty(path);
        if (!resolved.HasValue) return null;
        object? value = resolved.Value.field.GetValue(this);
        return resolved.Value.component >= 0 && value is float[] array ? array[resolved.Value.component] : value;
    }

    public void SetBackgroundPropertyValue(string path, string serialized, bool discrete, float blendValue = 0f, bool useBlend = false)
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

    public void AddBackgroundKeyframe(string path, int frame)
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

    /// <summary>Removes the keyframe at <paramref name="frame"/> from a background track (old "Remove" button).</summary>
    public void RemoveBackgroundKeyframe(string path, int frame)
    {
        var tracks = ProjectManager.Instance.Manifest?.Settings?.BackgroundKeyframes;
        if (tracks == null || !tracks.TryGetValue(path, out var keys))
            return;

        keys.RemoveAll(k => k.Frame == frame);
        if (keys.Count == 0)
            tracks.Remove(path);
        ProjectManager.Instance.SetDirty(true);
    }

    public void ApplyBackgroundAnimation(int frame)
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
    
    // ── Helpers ───────────────────────────────────────────────────────────────

    public IReadOnlyList<SceneObject> GetSelectedObjectsForEdit()
    {
        var selected = SelectionManager.Instance?.SelectedObjects;
        if (selected != null && selected.Count > 0)
            return selected;

        return _currentObject != null
            ? new List<SceneObject> { _currentObject }
            : Array.Empty<SceneObject>();
    }

    public void ApplyToSelectedObjects(Action<SceneObject> apply)
    {
        foreach (var obj in GetSelectedObjectsForEdit())
            apply(obj);
    }

    private void ApplyToSelectedSubtrees(Action<SceneObject> apply)
    {
        foreach (var obj in GetSelectedObjectsForEdit())
            ApplyToSubtree(obj, apply);
    }

    public void ApplyPosition(vec3 pos)
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

    public void ApplyRotation(vec3 rot)
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

    public void ApplyScale(vec3 scale)
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

    public void ApplyBend(MiBoneSceneObject bone, vec3 angle, params string[] propertyPaths)
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

    public void ApplyPivotOffset(vec3 pivot)
    {
        vec3 origin = _currentObject?.PivotOffset ?? vec3.Zero;
        vec3 delta = pivot - origin;

        ApplyToSelectedObjects(obj => obj.PivotOffset = obj.PivotOffset + delta);
        ProjectManager.Instance.SetDirty(true);
    }

    public static vec3 GetEditablePosition(SceneObject obj)
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

    public static vec3 GetEditableRotation(SceneObject obj)
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

    public static vec3 GetEditableScale(SceneObject obj)
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

    /// <summary>
    /// Applies new block-tile values to the currently selected object, clamps
    /// them to <see cref="SceneObject.MaxTilesPerAxis"/>, and rebuilds the
    /// block meshes to reflect the change.
    /// </summary>
    public void ApplyBlockTiling(int tileX, int tileY, int tileZ)
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

    public void ApplyTemporaryItemSheetSlot(int columnIndex, int rowIndex)
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

    public void ApplyCubeUvMapping(bool mapped)
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

            uint existingTextureId = obj.Visuals.FirstOrDefault()?.TextureId ?? 0;
            foreach (var mesh in obj.Visuals.ToList())
                mesh.Dispose();
            obj.Visuals.Clear();

            var rebuilt = new CubeMesh(mapped)
            {
                TextureId = existingTextureId,
                AlbedoTexture = MineImatorLoader.ResolveVeldridTexture(existingTextureId)
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
    public void RefreshLoadedTextures()
    {
        _loadedTexturePathCache.Clear();

        var sceneRoots = SceneObjectsProvider?.Invoke();
        if (sceneRoots == null || !ProjectManager.Instance.HasProject)
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
        foreach (var obj in sceneRoots)
        {
            ScanPrimitiveObjectForTextures(obj);
        }
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
    public void OnLoadAlbedoTextureForObject(SceneObject obj, string filePath)
    {
        if (obj == null || !File.Exists(filePath))
            return;

        try
        {
            uint tex = MineImatorLoader.Instance.LoadTextureFromFile(filePath);
            if (tex == 0)
                return;

            var veldridTexture = MineImatorLoader.ResolveVeldridTexture(tex);

            foreach (var mesh in obj.Visuals)
            {
                mesh.TextureId = tex;
                mesh.AlbedoTexture = veldridTexture;

                // White albedo for full color pass-through of the texture
                mesh.Albedo = System.Numerics.Vector3.One;
                mesh.Alpha = 1f;
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
    public static bool HasAnyShapeKeys(SceneObject obj)
    {
        foreach (var mesh in obj.GetMeshInstancesRecursively())
            if (mesh.HasShapeKeys) return true;
        return false;
    }

    public void AddBackgroundKeyframeGroup(string path, int frame)
    {
        if (GetBackgroundPropertyValue(path) is float[] values)
        {
            for (int i = 0; i < values.Length; i++) AddBackgroundKeyframe($"{path}.{i}", frame);
        }
        else AddBackgroundKeyframe(path, frame);
    }
}
