using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MineImatorSimplyRemade.core.project;

namespace MineImatorSimplyRemade.core.project;

public sealed class ProjectManager
{
    private static readonly Lazy<ProjectManager> _lazy = new(() => new ProjectManager());
    public static ProjectManager Instance => _lazy.Value;

    private const string ManifestFileName = "project.json";

    private ProjectManager() { }

    public ProjectManifest Manifest { get; private set; } = new();
    public string ProjectFolder { get; private set; } = "";
    public string DefaultProjectRoot { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public bool HasProject => !string.IsNullOrEmpty(ProjectFolder) && Directory.Exists(ProjectFolder);

    public string ImagesFolder => Path.Combine(ProjectFolder, "images");
    public string ModelsFolder => Path.Combine(ProjectFolder, "models");

    public void CreateNewProject(string projectFolder, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            throw new ArgumentException("Project folder cannot be empty.", nameof(projectFolder));

        ProjectFolder = projectFolder;
        Manifest = new ProjectManifest
        {
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? "Untitled Project" : projectName,
            CreatedUtc = DateTime.UtcNow.ToString("o"),
            LastSavedUtc = DateTime.UtcNow.ToString("o"),
            Assets = new List<ProjectAssetEntry>()
        };

        Directory.CreateDirectory(ProjectFolder);
        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(ModelsFolder);

        SaveManifest();
    }

    public bool LoadProject(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            return false;

        string manifestPath = Path.Combine(projectFolder, ManifestFileName);
        if (!File.Exists(manifestPath))
            return false;

        string json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(json);
        if (manifest == null)
            return false;

        ProjectFolder = projectFolder;
        Manifest = manifest;
        return true;
    }

    public void SaveManifest()
    {
        if (!HasProject)
            throw new InvalidOperationException("No project is currently open.");

        Manifest.LastSavedUtc = DateTime.UtcNow.ToString("o");
        string json = JsonSerializer.Serialize(Manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(ProjectFolder, ManifestFileName), json);
    }

    public ProjectAssetEntry AddAsset(string sourcePath, ProjectAssetType assetType)
    {
        if (!HasProject)
            throw new InvalidOperationException("No project is currently open.");

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Source asset not found.", sourcePath);

        string displayName = Path.GetFileName(sourcePath);
        string destRelativePath = assetType == ProjectAssetType.Model
            ? Path.Combine("models", MakeUniqueFolderName(Path.GetFileNameWithoutExtension(sourcePath)), displayName)
            : Path.Combine("images", displayName);

        destRelativePath = MakeUniqueRelativePath(destRelativePath);
        string destFullPath = Path.Combine(ProjectFolder, destRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destFullPath) ?? ProjectFolder);
        File.Copy(sourcePath, destFullPath, overwrite: false);

        var entry = new ProjectAssetEntry
        {
            DisplayName = displayName,
            RelativePath = destRelativePath,
            AssetType = assetType,
            SourcePath = sourcePath
        };
        Manifest.Assets.Add(entry);
        SaveManifest();
        return entry;
    }

    public string GetAssetFullPath(ProjectAssetEntry asset)
    {
        return Path.Combine(ProjectFolder, asset.RelativePath);
    }

    public IReadOnlyList<ProjectAssetEntry> GetProjectAssets()
    {
        return Manifest.Assets.AsReadOnly();
    }

    private static string MakeUniqueFolderName(string baseName)
    {
        string safeName = string.Concat(baseName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Model";
        string result = safeName;
        int index = 1;
        while (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "models", result)))
            result = safeName + "_" + index++;
        return result;
    }

    private string MakeUniqueRelativePath(string relativePath)
    {
        string fullPath = Path.Combine(ProjectFolder, relativePath);
        if (!File.Exists(fullPath))
            return relativePath;

        string dir = Path.GetDirectoryName(relativePath) ?? string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(relativePath);
        string ext = Path.GetExtension(relativePath);
        int index = 1;

        string candidate;
        do
        {
            candidate = Path.Combine(dir, fileName + "_" + index + ext);
            index++;
        } while (File.Exists(Path.Combine(ProjectFolder, candidate)));

        return candidate;
    }
}
