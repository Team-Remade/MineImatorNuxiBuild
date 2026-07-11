using System;
using System.Collections.Generic;

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
    public List<string> SelectedObjectNames { get; set; } = new();
    public ProjectTimelineState Timeline { get; set; } = new();
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
    public int ResolutionWidth { get; set; } = 1920;
    public int ResolutionHeight { get; set; } = 1080;
    public int Framerate { get; set; } = 30;
    public string RenderMode { get; set; } = "image";
    public string RenderImageFormat { get; set; } = "png";
    public string RenderVideoFormat { get; set; } = "mp4";
    public int RenderVideoBitrateKbps { get; set; } = 12000;
    public string RenderResolutionPreset { get; set; } = "1080P";
    public int TextureAnimationFps { get; set; } = 20;
    public bool UseSky { get; set; } = false;
    public bool UseAdvancedSky { get; set; } = false;
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
    public ProjectVec3 AmbientLightColor { get; set; } = new()
    {
        X = 1f,
        Y = 1f,
        Z = 1f
    };
    public float AmbientLightStrength { get; set; } = 0.35f;
    public ProjectVec3 FillLightColor { get; set; } = new()
    {
        X = 0.85f,
        Y = 0.85f,
        Z = 0.85f
    };
    public float FillLightStrength { get; set; } = 1f;
    public bool FillLightCastsShadows { get; set; } = true;
}

public class ProjectTimelineState
{
    public int CurrentFrame { get; set; } = 0;
    public int MaxFrames { get; set; } = 300;
    public float FrameRate { get; set; } = 30f;
    public bool AutoKeyframe { get; set; } = false;
}

public class ProjectKeyframeEntry
{
    public int Frame { get; set; }
    public float Value { get; set; }
    public string InterpolationType { get; set; } = "linear";
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
    public string Name { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public string SpawnCategory { get; set; } = "";
    public string BlockVariant { get; set; } = "";
    public string TextureType { get; set; } = "item";
    public string ResourcePackId { get; set; } = "";
    public string SourceAssetPath { get; set; } = "";

    public ProjectVec3 Position { get; set; } = new();
    public ProjectVec3 Rotation { get; set; } = new();
    public ProjectVec3 Scale { get; set; } = new() { X = 1f, Y = 1f, Z = 1f };
    public ProjectVec3 PivotOffset { get; set; } = new() { X = 0f, Y = 0.5f, Z = 0f };

    public bool InheritPosition { get; set; } = true;
    public bool InheritRotation { get; set; } = true;
    public bool InheritScale { get; set; } = true;
    public bool InheritPivotOffset { get; set; } = false;
    public bool InheritVisibility { get; set; } = true;
    public bool ObjectVisible { get; set; } = true;
    public bool IsSelectable { get; set; } = true;
    public bool HideInSceneTree { get; set; } = false;

    // Material override data
    public bool HasMaterialOverrides { get; set; } = false;
    public ProjectVec4 AlbedoColor { get; set; } = new() { X = 1f, Y = 1f, Z = 1f, W = 1f };
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

    // Light-specific data
    public ProjectVec4 LightColor { get; set; } = new() { X = 1f, Y = 1f, Z = 1f, W = 1f };
    public float LightEnergy { get; set; } = 1f;
    public float LightRange { get; set; } = 5f;
    public float LightIndirectEnergy { get; set; } = 1f;
    public float LightSpecular { get; set; } = 0.5f;
    public bool LightShadowEnabled { get; set; } = true;

    public Dictionary<string, List<ProjectKeyframeEntry>> Keyframes { get; set; } = new();

    public List<ProjectSceneObjectEntry> Children { get; set; } = new();
}
