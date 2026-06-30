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

public class ProjectManifest
{
    public string ProjectName { get; set; } = "Untitled Project";
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public string LastSavedUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public List<ProjectAssetEntry> Assets { get; set; } = new();
    public List<ProjectSceneObjectEntry> SceneObjects { get; set; } = new();
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

    public List<ProjectSceneObjectEntry> Children { get; set; } = new();
}
