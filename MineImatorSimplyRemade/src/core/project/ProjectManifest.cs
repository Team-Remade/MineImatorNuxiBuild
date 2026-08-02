using System;
using System.Collections.Generic;
using MineImatorSimplyRemade.core.mdl.meshes;

namespace MineImatorSimplyRemade.core.project;

public enum ProjectAssetType
{
    Unknown,
    Model,
    Image,
    Sound,
    Other
}

public class ProjectAssetEntry
{
    public string DisplayName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public ProjectAssetType AssetType { get; set; } = ProjectAssetType.Unknown;
    public string SourcePath { get; set; } = "";
    public bool StoredInProject { get; set; } = true;
}

public class RecentProjectEntry
{
    public string ProjectName { get; set; } = "";
    public string ProjectFilePath { get; set; } = "";
    public string LastOpenedUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public string ThumbnailPath { get; set; } = "";
}

public class RecentProjectsState
{
    public List<RecentProjectEntry> Projects { get; set; } = new();
}

public class ProjectManifest
{
    public string ProjectName { get; set; } = "Untitled Project";
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public string LastSavedUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public List<ProjectAssetEntry> Assets { get; set; } = new();
    public ProjectRenderSettings Settings { get; set; } = new();
    public ProjectWorkCameraState WorkCamera { get; set; } = new();
    // Index of the active camera used by the preview viewport: 0 = work camera, 1+ = spawned cameras
    public int ActivePreviewCameraIndex { get; set; } = 0;
    public List<ProjectSceneObjectEntry> SceneObjects { get; set; } = new();
    public List<ProjectSceneObjectEntry> ObjectLibrary { get; set; } = new();
    public List<string> SelectedObjectNames { get; set; } = new();
    public ProjectTimelineState Timeline { get; set; } = new();
    public List<ProjectAudioTrack> AudioTracks { get; set; } = new();
}

/// <summary>
/// One audio clip placed on the timeline.  References an imported sound asset
/// (matched by display name at load time) and stores playback settings.
/// </summary>
public class ProjectAudioTrack
{
    public string AssetDisplayName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Frame on the timeline where playback should start.</summary>
    public int StartFrame { get; set; } = 0;
    /// <summary>Offset (in seconds) into the source clip where playback starts.</summary>
    public float SourceOffsetSeconds { get; set; } = 0f;
    /// <summary>Linear gain (0 = silent, 1 = full).  Stored as 0..1.</summary>
    public float Volume { get; set; } = 1f;
    public bool Muted { get; set; } = false;
    public bool Loop { get; set; } = false;
    /// <summary>Optional cached duration (seconds) so the timeline can render the
    /// clip even before the audio engine has decoded the underlying file.</summary>
    public float CachedDurationSeconds { get; set; } = 0f;
}

public class ProjectWorkCameraState
{
    public ProjectVec3 Target { get; set; } = new()
    {
        X = 0f,
        Y = 0f,
        Z = 0f
    };

    public float Yaw { get; set; } = 0.5f;
    public float Pitch { get; set; } = 0.4f;
    public float Distance { get; set; } = 5f;
}

public class ProjectRenderSettings
{
    public bool AmbientOcclusionEnabled { get; set; } = true;
    public float AmbientOcclusionRadius { get; set; } = 12f;
    public float AmbientOcclusionStrength { get; set; } = 1f;
    public ProjectVec3 AmbientOcclusionColor { get; set; } = new() { X = 0f, Y = 0f, Z = 0f };
    public float AmbientOcclusionRatio { get; set; } = 0.222f;
    public float AmbientOcclusionRatioBalance { get; set; } = 0.35f;
    public bool ShadowsEnabled { get; set; } = true;
    public int SunShadowBufferSize { get; set; } = 2048;
    public int SpotShadowBufferSize { get; set; } = 1024;
    public int PointShadowBufferSize { get; set; } = 1024;
    public float ShadowBlurStrength { get; set; } = 1f;
    public Dictionary<string, List<ProjectBackgroundKeyframeEntry>> BackgroundKeyframes { get; set; } = new();
    public int ResolutionWidth { get; set; } = 1920;
    public int ResolutionHeight { get; set; } = 1080;
    public int Framerate { get; set; } = 30;
    public string RenderMode { get; set; } = "image";
    public string RenderImageFormat { get; set; } = "png";
    public string RenderVideoFormat { get; set; } = "mp4";
    public int RenderVideoBitrateKbps { get; set; } = 12000;
    public string RenderResolutionPreset { get; set; } = "1080P";
    public int TextureAnimationFps { get; set; } = 20;
    public bool UseSky { get; set; } = true;
    public bool UseAdvancedSky { get; set; } = false;
    public bool FogEnabled { get; set; } = true;
    public bool SkyFog { get; set; } = true;
    public bool CustomFogColor { get; set; } = false;
    public ProjectVec3 FogColor { get; set; } = new() { X = 0.5764706f, Y = 0.5764706f, Z = 1f };
    public bool CustomObjectFogColor { get; set; } = false;
    public ProjectVec3 ObjectFogColor { get; set; } = new() { X = 0.5764706f, Y = 0.5764706f, Z = 1f };
    public float FogDistance { get; set; } = 10000f;
    public float FogFadeSize { get; set; } = 2000f;
    public float FogHeight { get; set; } = 1250f;
    public bool HeightFog { get; set; } = false;
    public bool CustomHeightFogColor { get; set; } = false;
    public ProjectVec3 HeightFogColor { get; set; } = new() { X = 0.5764706f, Y = 0.5764706f, Z = 1f };
    public float HeightFogSize { get; set; } = 4000f;
    public float HeightFogOffset { get; set; } = -3850f;
    public ProjectVec3 SkyHorizonDay { get; set; } = new() { X = 0.72f, Y = 0.84f, Z = 1f };
    public ProjectVec3 SkyZenithDay { get; set; } = new() { X = 0.28f, Y = 0.55f, Z = 0.95f };
    public ProjectVec3 SkyHorizonSunset { get; set; } = new() { X = 1f, Y = 0.48f, Z = 0.2f };
    public ProjectVec3 SkyZenithSunset { get; set; } = new() { X = 0.22f, Y = 0.3f, Z = 0.62f };
    public ProjectVec3 SkyHorizonNight { get; set; } = new() { X = 0.055f, Y = 0.075f, Z = 0.16f };
    public ProjectVec3 SkyZenithNight { get; set; } = new() { X = 0.008f, Y = 0.012f, Z = 0.045f };
    public string SunTexture { get; set; } = "minecraft:environment/sun.png";
    public string MoonTexture { get; set; } = "minecraft:environment/moon_phases.png";
    public string CloudTexture { get; set; } = "minecraft:environment/clouds.png";
    public ProjectVec3 CloudColor { get; set; } = new() { X = 1f, Y = 1f, Z = 1f };
    public string CloudRenderMode { get; set; } = "3d";
    public float CloudSpeed { get; set; } = 0f;
    public float CloudOffsetX { get; set; } = 0f;
    public float CloudOffsetY { get; set; } = 0f;
    public float CloudHeight { get; set; } = 2294f;
    public float CloudBlockSize { get; set; } = 1536f;
    public float CloudThickness { get; set; } = 64f;
    public int MoonPhase { get; set; } = 0;
    public float SkyTime { get; set; } = 0f;
    public float SunSize { get; set; } = 16f;
    public ProjectVec3 SunAngle { get; set; } = new() { X = 135f, Y = 0f, Z = 0f };
    public float MoonSize { get; set; } = 16f;
    public ProjectVec3 MoonAngle { get; set; } = new() { X = 315f, Y = 0f, Z = 0f };
    public ProjectVec3 SunFillLightColor { get; set; } = new() { X = 1f, Y = 0.96862745f, Z = 0.89411765f };
    public float SunFillLightStrength { get; set; } = 0.25f;
    public bool SunFillLightCastsShadows { get; set; } = true;
    public ProjectVec3 MoonFillLightColor { get; set; } = new() { X = 0.6f, Y = 0.65f, Z = 1f };
    public float MoonFillLightStrength { get; set; } = 0.1f;
    public bool MoonFillLightCastsShadows { get; set; } = false;
    public string BackgroundRenderMode { get; set; } = "stretch";
    public bool StretchBackground { get; set; } = true;
    public float BackgroundScale { get; set; } = 1f;
    public float BackgroundRotationDegrees { get; set; } = 0f;
    public float BackgroundOffsetX { get; set; } = 0f;
    public float BackgroundOffsetY { get; set; } = 0f;
    public string BackgroundImagePath { get; set; } = "No image selected";
    public bool FloorVisible { get; set; } = true;
    public string FloorTextureAtlas { get; set; } = "block";
    public string FloorTileKey { get; set; } = "grass_block_top";
    public ProjectVec4 BackgroundColor { get; set; } = new()
    {
        X = 0.5764706f,
        Y = 0.5764706f,
        Z = 1f,
        W = 1f
    };
    public bool Twilight { get; set; } = true;
    public bool ShowStars { get; set; } = true;
    public float StarDensity { get; set; } = 1f;
    public float StarBrightness { get; set; } = 1f;
    public float StarTwinkleSpeed { get; set; } = 1f;
    public ProjectVec3 StarColor { get; set; } = new() { X = 1f, Y = 1f, Z = 1f };
    public ProjectVec3 NightCloudColor { get; set; } = new() { X = 1f, Y = 1f, Z = 1f };
    public ProjectVec3 AmbientLightColor { get; set; } = new()
    {
        X = 1f,
        Y = 1f,
        Z = 1f
    };
    public float AmbientLightStrength { get; set; } = 0.35f;
    public ProjectVec3 NightAmbientLightColor { get; set; } = new() { X = 0.05f, Y = 0.05f, Z = 0.2f };
    public float NightAmbientLightStrength { get; set; } = 0.15f;
    public ProjectVec3 FillLightColor { get; set; } = new()
    {
        X = 0.85f,
        Y = 0.85f,
        Z = 0.85f
    };
    public float FillLightStrength { get; set; } = 1f;
    public bool FillLightCastsShadows { get; set; } = true;
}

public class ProjectBackgroundKeyframeEntry
{
    public int Frame { get; set; }
    public string Value { get; set; } = "";
    public bool Discrete { get; set; }
}

public class ProjectTimelineState
{
    public int CurrentFrame { get; set; } = 0;
    public int MaxFrames { get; set; } = 300;
    public float FrameRate { get; set; } = 30f;
    public bool AutoKeyframe { get; set; } = false;
    public bool LoopPlayback { get; set; } = false;
    public int? PlaybackRegionStart { get; set; }
    public int? PlaybackRegionEnd { get; set; }
    public List<ProjectTimelineMarker> Markers { get; set; } = new();
}

public class ProjectTimelineMarker
{
    public int Frame { get; set; }
    public string Label { get; set; } = "Marker";
    public float Red { get; set; } = 0.9f;
    public float Green { get; set; } = 0.2f;
    public float Blue { get; set; } = 0.2f;
    public float Alpha { get; set; } = 1f;
}

public class ProjectKeyframeEntry
{
    public int Frame { get; set; }
    public float Value { get; set; }
    public string InterpolationType { get; set; } = "linear";
}

/// <summary>
/// Persists the current (non-keyframed) weight of a single shape key.
/// <see cref="MeshIndex"/> is a best-effort hint matching the object's
/// depth-first mesh order at save time; <see cref="Name"/> is the primary
/// match key on load so weights survive the model being re-imported with a
/// different mesh/morph-target order.
/// </summary>
public class ProjectShapeKeyWeightEntry
{
    public int MeshIndex { get; set; }
    public string Name { get; set; } = "";
    public float Weight { get; set; }
}

public class ProjectVec3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public class ProjectVec4
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }
}

public class ProjectSceneObjectEntry
{
    public string LibraryEntryId { get; set; } = "";
    public string LibrarySourceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public string SpawnCategory { get; set; } = "";
    public string BlockVariant { get; set; } = "";
    public string TextureType { get; set; } = "item";
    public string ResourcePackId { get; set; } = "";
    public string SourceAssetPath { get; set; } = "";
    public string AlbedoTexturePath { get; set; } = "";

    // Path to the character texture-variant image selected in the spawn menu
    // at import time (e.g. a "skin" chosen from a character's textures.nux
    // manifest). Re-applied on load after the model is re-imported from
    // SourceAssetPath, since the source file's own default texture is always
    // what gets loaded first.
    public string TextureOverridePath { get; set; } = "";

    // Block tiling (1 = no tiling). Only applied to objects in the Blocks
    // spawn category. Clamped to [1, 1000] per axis on load.
    public int TileX { get; set; } = 1;
    public int TileY { get; set; } = 1;
    public int TileZ { get; set; } = 1;

    // Primitive plane orientation. Only used for Primitives/Plane objects.
    public PlaneOrientation PrimitivePlaneOrientation { get; set; } = PlaneOrientation.XY;

    // Primitive cube UV mode. Only used for Primitives/Cube objects.
    // False = each face maps to the full texture; true = 3x2 cubemap unwrap.
    public bool PrimitiveCubeMapped { get; set; } = false;

    public ProjectVec3 Position { get; set; } = new();
    public ProjectVec3 Rotation { get; set; } = new();
    public ProjectVec3 Scale { get; set; } = new() { X = 1f, Y = 1f, Z = 1f };
    public ProjectVec3? BendAngle { get; set; }
    public ProjectVec3 PivotOffset { get; set; } = new() { X = 0f, Y = 0.5f, Z = 0f };

    public bool InheritPosition { get; set; } = true;
    public bool InheritRotation { get; set; } = true;
    public bool InheritScale { get; set; } = true;
    public bool InheritPivotOffset { get; set; } = false;
    public bool InheritVisibility { get; set; } = true;
    public bool ObjectVisible { get; set; } = true;
    public bool InvertFaces { get; set; } = false;
    public bool IsSelectable { get; set; } = true;
    public bool HideInSceneTree { get; set; } = false;

    // Material override data
    public bool HasMaterialOverrides { get; set; } = false;
    public ProjectVec4 AlbedoColor { get; set; } = new() { X = 1f, Y = 1f, Z = 1f, W = 1f };
    public ProjectVec4 BlendColor { get; set; } = new() { X = 1f, Y = 1f, Z = 1f, W = 1f };
    public ProjectVec4 MixColor { get; set; } = new() { X = 0f, Y = 0f, Z = 0f, W = 0f };
    public float Metallic { get; set; } = 0f;
    public float Roughness { get; set; } = 0.5f;
    public float Transparency { get; set; } = 0f;
    public bool DoubleSided { get; set; } = false;
    public bool EmissionEnabled { get; set; } = false;
    public ProjectVec4 EmissionColor { get; set; } = new() { X = 0f, Y = 0f, Z = 0f, W = 1f };
    public float EmissionEnergy { get; set; } = 1f;

    // Item-specific data
    public string ItemTileKey { get; set; } = "";
    public bool ItemIs3D { get; set; } = true;

    // Camera-specific data
    public float CameraFov { get; set; } = 70f;
    public float CameraNear { get; set; } = 0.05f;
    public float CameraFar { get; set; } = 4000f;
    public bool CameraActive { get; set; } = false;

    // Light-specific data
    public ProjectVec4 LightColor { get; set; } = new() { X = 1f, Y = 1f, Z = 1f, W = 1f };
    public float LightEnergy { get; set; } = 1f;
    public float LightRange { get; set; } = 5f;
    public float LightIndirectEnergy { get; set; } = 1f;
    public float LightSpecular { get; set; } = 0.5f;
    public bool LightShadowEnabled { get; set; } = true;
    // 0 = point, 1 = spot.  Stored as int for forward compatibility.
    public int LightType { get; set; } = 0;
    public float LightSpotAngle { get; set; } = 45f;
    public float LightSpotBlend { get; set; } = 5f;

    public Dictionary<string, List<ProjectKeyframeEntry>> Keyframes { get; set; } = new();

    // Current (non-keyframed) shape key weights. Only non-default (non-zero)
    // weights are stored; matched primarily by name on load (see
    // ProjectShapeKeyWeightEntry).
    public List<ProjectShapeKeyWeightEntry> ShapeKeyWeights { get; set; } = new();

    public List<ProjectSceneObjectEntry> Children { get; set; } = new();
}
