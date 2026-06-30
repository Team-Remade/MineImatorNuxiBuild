using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MineImatorSimplyRemade.core.project;

public sealed class ProjectManager
{
    private static readonly Lazy<ProjectManager> _lazy = new(() => new ProjectManager());
    public static ProjectManager Instance => _lazy.Value;

    private const string ProjectFileExtension = ".nxProj";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private ProjectManager() { }

    public ProjectManifest Manifest { get; private set; } = new();
    public string ProjectFolder { get; private set; } = "";
    public string ProjectFilePath { get; private set; } = "";
    public string DefaultProjectRoot => Path.Combine(AppContext.BaseDirectory, "Projects");

    private string DataRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data"));

    public bool HasProject =>
        !string.IsNullOrEmpty(ProjectFolder) &&
        !string.IsNullOrEmpty(ProjectFilePath) &&
        Directory.Exists(ProjectFolder);

    public string ImagesFolder => Path.Combine(ProjectFolder, "images");
    public string ModelsFolder => Path.Combine(ProjectFolder, "models");
    public string SoundsFolder => Path.Combine(ProjectFolder, "sounds");
    public string OtherFolder => Path.Combine(ProjectFolder, "other");

    public void CreateNewProject(string projectName)
    {
        string safeName = MakeSafeName(projectName);
        Directory.CreateDirectory(DefaultProjectRoot);

        string uniqueFolderName = MakeUniqueFolderName(DefaultProjectRoot, safeName);
        string projectFolder = Path.Combine(DefaultProjectRoot, uniqueFolderName);

        ProjectFolder = projectFolder;
        ProjectFilePath = Path.Combine(projectFolder, uniqueFolderName + ProjectFileExtension);

        Manifest = new ProjectManifest
        {
            ProjectName = safeName,
            CreatedUtc = DateTime.UtcNow.ToString("o"),
            LastSavedUtc = DateTime.UtcNow.ToString("o"),
            Assets = new List<ProjectAssetEntry>()
        };

        Directory.CreateDirectory(ProjectFolder);
        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(ModelsFolder);
        Directory.CreateDirectory(SoundsFolder);
        Directory.CreateDirectory(OtherFolder);

        SaveManifest();
    }

    public void CreateNewProject(string projectFolder, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
        {
            CreateNewProject(projectName);
            return;
        }

        string safeName = MakeSafeName(projectName);
        ProjectFolder = Path.GetFullPath(projectFolder);
        Directory.CreateDirectory(ProjectFolder);

        string fileName = MakeSafeName(Path.GetFileName(ProjectFolder));
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = safeName;

        ProjectFilePath = Path.Combine(ProjectFolder, fileName + ProjectFileExtension);

        Manifest = new ProjectManifest
        {
            ProjectName = safeName,
            CreatedUtc = DateTime.UtcNow.ToString("o"),
            LastSavedUtc = DateTime.UtcNow.ToString("o"),
            Assets = new List<ProjectAssetEntry>()
        };

        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(ModelsFolder);
        Directory.CreateDirectory(SoundsFolder);
        Directory.CreateDirectory(OtherFolder);

        SaveManifest();
    }

    public bool LoadProject(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return false;

        string fullPath = Path.GetFullPath(projectPath);
        string projectFile;

        if (Directory.Exists(fullPath))
        {
            string[] candidates = Directory.GetFiles(fullPath, "*" + ProjectFileExtension);
            if (candidates.Length == 0)
                return false;
            projectFile = candidates[0];
        }
        else if (File.Exists(fullPath) &&
                 string.Equals(Path.GetExtension(fullPath), ProjectFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            projectFile = fullPath;
        }
        else
        {
            return false;
        }

        string json = File.ReadAllText(projectFile);
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(json);
        if (manifest == null)
            return false;

        ProjectFolder = Path.GetDirectoryName(projectFile) ?? "";
        ProjectFilePath = projectFile;
        Manifest = manifest;

        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(ModelsFolder);
        Directory.CreateDirectory(SoundsFolder);
        Directory.CreateDirectory(OtherFolder);

        return true;
    }

    public void SaveManifest()
    {
        if (!HasProject)
            throw new InvalidOperationException("No project is currently open.");

        Manifest.LastSavedUtc = DateTime.UtcNow.ToString("o");
        string json = JsonSerializer.Serialize(Manifest, JsonOptions);
        File.WriteAllText(ProjectFilePath, json);
    }

    public ProjectAssetEntry AddAsset(string sourcePath, ProjectAssetType assetType)
    {
        if (!HasProject)
            throw new InvalidOperationException("No project is currently open.");

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Source asset not found.", sourcePath);

        string fullSourcePath = Path.GetFullPath(sourcePath);
        string displayName = Path.GetFileName(fullSourcePath);

        bool fromDataDirectory = IsPathUnderDirectory(fullSourcePath, DataRoot);

        string relativePath = "";
        bool storedInProject = !fromDataDirectory;

        if (storedInProject)
        {
            relativePath = CopyAssetIntoProject(fullSourcePath, assetType);
        }

        var entry = new ProjectAssetEntry
        {
            DisplayName = displayName,
            RelativePath = relativePath,
            AssetType = assetType,
            SourcePath = fullSourcePath,
            StoredInProject = storedInProject
        };
        Manifest.Assets.Add(entry);
        SaveManifest();
        return entry;
    }

    public string GetAssetFullPath(ProjectAssetEntry asset)
    {
        if (asset.StoredInProject && !string.IsNullOrWhiteSpace(asset.RelativePath))
            return Path.Combine(ProjectFolder, asset.RelativePath);
        return asset.SourcePath;
    }

    public IReadOnlyList<ProjectAssetEntry> GetProjectAssets()
    {
        return Manifest.Assets.AsReadOnly();
    }

    private string CopyAssetIntoProject(string sourcePath, ProjectAssetType assetType)
    {
        if (assetType == ProjectAssetType.Model)
            return CopyModelIntoProject(sourcePath);

        string assetFolderName = GetAssetFolderName(assetType);
        string relativePath = Path.Combine(assetFolderName, Path.GetFileName(sourcePath));
        relativePath = MakeUniqueRelativePath(relativePath);

        string fullTargetPath = Path.Combine(ProjectFolder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullTargetPath) ?? ProjectFolder);
        File.Copy(sourcePath, fullTargetPath, overwrite: false);
        return relativePath;
    }

    private string CopyModelIntoProject(string sourcePath)
    {
        string modelName = Path.GetFileNameWithoutExtension(sourcePath);
        string modelFolderName = MakeUniqueFolderName(ModelsFolder, MakeSafeName(modelName));
        string modelFolder = Path.Combine(ModelsFolder, modelFolderName);
        Directory.CreateDirectory(modelFolder);

        string targetModelPath = Path.Combine(modelFolder, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, targetModelPath, overwrite: false);

        // Copy sibling textures into this model-specific folder to avoid conflicts
        // between files with identical names imported by different models.
        string sourceDir = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        if (Directory.Exists(sourceDir))
        {
            foreach (string textureFile in Directory.GetFiles(sourceDir))
            {
                if (!IsTextureFile(textureFile)) continue;

                string textureTarget = Path.Combine(modelFolder, Path.GetFileName(textureFile));
                if (!File.Exists(textureTarget))
                    File.Copy(textureFile, textureTarget, overwrite: false);
            }
        }

        return Path.Combine("models", modelFolderName, Path.GetFileName(sourcePath));
    }

    private static bool IsTextureFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".dds" or ".gif" or ".webp" or ".tiff";
    }

    private static bool IsPathUnderDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            return false;

        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullDir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.StartsWith(fullDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullPath, fullDir, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAssetFolderName(ProjectAssetType assetType)
    {
        return assetType switch
        {
            ProjectAssetType.Image => "images",
            ProjectAssetType.Sound => "sounds",
            ProjectAssetType.Other => "other",
            _ => "other"
        };
    }

    private static string MakeUniqueFolderName(string rootDirectory, string baseName)
    {
        string safeName = MakeSafeName(baseName);
        string result = safeName;
        int index = 1;
        while (Directory.Exists(Path.Combine(rootDirectory, result)))
            result = safeName + "_" + index++;
        return result;
    }

    private static string MakeSafeName(string text)
    {
        string safeName = string.Concat(text.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "UntitledProject";
        return safeName;
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
