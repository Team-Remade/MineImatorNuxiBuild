using System;
using System.Collections.Generic;

namespace MineImatorSimplyRemade.core.project;

public enum ProjectAssetType
{
    Unknown,
    Model,
    Image,
    Other
}

public class ProjectAssetEntry
{
    public string DisplayName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public ProjectAssetType AssetType { get; set; } = ProjectAssetType.Unknown;
    public string SourcePath { get; set; } = "";
}

public class ProjectManifest
{
    public string ProjectName { get; set; } = "Untitled Project";
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public string LastSavedUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public List<ProjectAssetEntry> Assets { get; set; } = new();
}
