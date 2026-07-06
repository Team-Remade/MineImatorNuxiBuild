using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MineImatorSimplyRemade;

namespace MineImatorSimplyRemade.core.project;

public sealed class ProjectManager
{
    private static readonly Lazy<ProjectManager> _lazy = new(() => new ProjectManager());
    public static ProjectManager Instance => _lazy.Value;

    private const string ProjectFileExtension = ".nxProj";
    private const int MaxRecentProjects = 100;
    private ProjectManager() { }

    public ProjectManifest Manifest { get; private set; } = new();
    public string ProjectFolder { get; private set; } = "";
    public string ProjectFilePath { get; private set; } = "";
    public bool IsDirty { get; private set; }
    public string DefaultProjectRoot => ResolveDefaultProjectRoot();
    public string RecentProjectsFilePath => Path.Combine(AppDataRoot, "recentProjects.json");

    private static string AppDocumentsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "MineImatorSimplyRemade");

    private static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MineImatorSimplyRemade");

    private string LegacyRecentProjectsFilePath => Path.Combine(AppContext.BaseDirectory, "recentProjects.json");

    private string DataRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data"));

    public bool HasProject =>
        !string.IsNullOrEmpty(ProjectFolder) &&
        !string.IsNullOrEmpty(ProjectFilePath) &&
        Directory.Exists(ProjectFolder);

    public string ImagesFolder => Path.Combine(ProjectFolder, "images");
    public string ModelsFolder => Path.Combine(ProjectFolder, "models");
    public string SoundsFolder => Path.Combine(ProjectFolder, "sounds");
    public string OtherFolder => Path.Combine(ProjectFolder, "other");
    public string ResourcePacksFolder => Path.Combine(ProjectFolder, "mods", "resourcepacks");

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
        MinecraftDataLoader.SetProjectRoot(ProjectFolder);

        TrackRecentProject(ProjectFilePath, Manifest.ProjectName);
        SaveManifest();
        SetDirty(false);
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
        MinecraftDataLoader.SetProjectRoot(ProjectFolder);

        TrackRecentProject(ProjectFilePath, Manifest.ProjectName);
        SaveManifest();
        SetDirty(false);
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
        var manifest = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectManifest);
        if (manifest == null)
            return false;

        ProjectFolder = Path.GetDirectoryName(projectFile) ?? "";
        ProjectFilePath = projectFile;
        Manifest = manifest;

        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(ModelsFolder);
        Directory.CreateDirectory(SoundsFolder);
        Directory.CreateDirectory(OtherFolder);
        MinecraftDataLoader.SetProjectRoot(ProjectFolder);

        TrackRecentProject(ProjectFilePath, Manifest.ProjectName);
        SetDirty(false);

        return true;
    }

    public void SaveManifest()
    {
        if (!HasProject)
            throw new InvalidOperationException("No project is currently open.");

        Manifest.LastSavedUtc = DateTime.UtcNow.ToString("o");
        var writerOptions = new JsonWriterOptions { Indented = true };
        using var stream = File.Create(ProjectFilePath);
        using var writer = new Utf8JsonWriter(stream, writerOptions);
        JsonSerializer.Serialize(writer, Manifest, AppJsonContext.Default.ProjectManifest);
        writer.Flush();
        SetDirty(false);
    }

    public void SaveProjectAs(string projectName)
    {
        if (!HasProject)
            throw new InvalidOperationException("No project is currently open.");

        string safeName = MakeSafeName(projectName);
        Directory.CreateDirectory(DefaultProjectRoot);

        string uniqueFolderName = MakeUniqueFolderName(DefaultProjectRoot, safeName);
        string destinationFolder = Path.Combine(DefaultProjectRoot, uniqueFolderName);
        string destinationProjectFile = Path.Combine(destinationFolder, uniqueFolderName + ProjectFileExtension);

        CopyDirectory(ProjectFolder, destinationFolder);

        string copiedOriginalProjectFile = Path.Combine(destinationFolder, Path.GetFileName(ProjectFilePath));
        if (!string.Equals(copiedOriginalProjectFile, destinationProjectFile, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(copiedOriginalProjectFile))
        {
            File.Move(copiedOriginalProjectFile, destinationProjectFile, overwrite: true);
        }

        ProjectFolder = destinationFolder;
        ProjectFilePath = destinationProjectFile;
        Manifest.ProjectName = safeName;
        MinecraftDataLoader.SetProjectRoot(ProjectFolder);

        TrackRecentProject(ProjectFilePath, Manifest.ProjectName);
        SaveManifest();
        SetDirty(false);
    }

    public void SetDirty(bool isDirty)
    {
        IsDirty = isDirty;
    }

    public string ImportResourcePack(string sourcePath)
    {
        if (!HasProject)
            throw new InvalidOperationException("No project is currently open.");

        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Resource pack path is empty.", nameof(sourcePath));

        string fullSourcePath = Path.GetFullPath(sourcePath);
        bool isDirectory = Directory.Exists(fullSourcePath);
        bool isFile = File.Exists(fullSourcePath);

        if (!isDirectory && !isFile)
            throw new FileNotFoundException("Resource pack source was not found.", sourcePath);

        Directory.CreateDirectory(ResourcePacksFolder);

        if (isDirectory)
        {
            string folderName = MakeSafeName(Path.GetFileName(fullSourcePath));
            if (string.IsNullOrWhiteSpace(folderName))
                folderName = "resourcepack";

            string destinationFolder = Path.Combine(ResourcePacksFolder, MakeUniqueFolderName(ResourcePacksFolder, folderName));
            CopyDirectory(fullSourcePath, destinationFolder);
            return destinationFolder;
        }

        string ext = Path.GetExtension(fullSourcePath);
        if (!string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resource pack import currently supports .zip files or unpacked folders.");

        string destinationPath = Path.Combine(ResourcePacksFolder, Path.GetFileName(fullSourcePath));
        destinationPath = MakeUniqueFilePath(destinationPath);
        File.Copy(fullSourcePath, destinationPath, overwrite: false);
        return destinationPath;
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

    public bool RemoveAsset(ProjectAssetEntry asset)
    {
        if (!HasProject)
            throw new InvalidOperationException("No project is currently open.");

        if (asset == null)
            return false;

        bool removed = Manifest.Assets.Remove(asset);
        if (!removed)
            return false;

        DeleteAssetFiles(asset);
        SaveManifest();
        return true;
    }

    public IReadOnlyList<RecentProjectEntry> GetRecentProjects()
    {
        var state = LoadRecentProjectsState();
        return state.Projects.AsReadOnly();
    }

    public void TrackRecentProject(string projectFilePath, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return;

        string fullProjectPath = Path.GetFullPath(projectFilePath);
        var state = LoadRecentProjectsState();

        RecentProjectEntry? existingEntry = state.Projects.FirstOrDefault(entry =>
            string.Equals(Path.GetFullPath(entry.ProjectFilePath), fullProjectPath, StringComparison.OrdinalIgnoreCase));

        string thumbnailPath = ResolveRecentProjectThumbnailPath(fullProjectPath, existingEntry?.ThumbnailPath ?? "");

        state.Projects.RemoveAll(entry =>
            string.Equals(Path.GetFullPath(entry.ProjectFilePath), fullProjectPath, StringComparison.OrdinalIgnoreCase));

        state.Projects.Insert(0, new RecentProjectEntry
        {
            ProjectName = string.IsNullOrWhiteSpace(projectName)
                ? Path.GetFileNameWithoutExtension(fullProjectPath)
                : projectName,
            ProjectFilePath = fullProjectPath,
            LastOpenedUtc = DateTime.UtcNow.ToString("o"),
            ThumbnailPath = thumbnailPath
        });

        if (state.Projects.Count > MaxRecentProjects)
            state.Projects = state.Projects.Take(MaxRecentProjects).ToList();

        SaveRecentProjectsState(state);
    }

    private static string ResolveRecentProjectThumbnailPath(string projectFilePath, string existingThumbnailPath)
    {
        if (!string.IsNullOrWhiteSpace(existingThumbnailPath))
        {
            string fullExistingPath = Path.GetFullPath(existingThumbnailPath);
            if (File.Exists(fullExistingPath))
                return fullExistingPath;
        }

        string projectFolder = Path.GetDirectoryName(projectFilePath) ?? "";
        if (string.IsNullOrWhiteSpace(projectFolder))
            return "";

        string fallbackThumbnailPath = Path.Combine(projectFolder, "thumbnail.png");
        return File.Exists(fallbackThumbnailPath) ? fallbackThumbnailPath : "";
    }

    public void UpdateRecentProjectThumbnail(string projectFilePath, string thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return;

        string fullProjectPath = Path.GetFullPath(projectFilePath);
        var state = LoadRecentProjectsState();
        var entry = state.Projects.FirstOrDefault(item =>
            string.Equals(Path.GetFullPath(item.ProjectFilePath), fullProjectPath, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return;

        entry.ThumbnailPath = string.IsNullOrWhiteSpace(thumbnailPath) ? "" : Path.GetFullPath(thumbnailPath);
        SaveRecentProjectsState(state);
    }

    public void RemoveRecentProject(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return;

        string fullProjectPath = Path.GetFullPath(projectFilePath);
        var state = LoadRecentProjectsState();
        state.Projects.RemoveAll(entry =>
            string.Equals(Path.GetFullPath(entry.ProjectFilePath), fullProjectPath, StringComparison.OrdinalIgnoreCase));
        SaveRecentProjectsState(state);
    }

    private RecentProjectsState LoadRecentProjectsState()
    {
        if (File.Exists(RecentProjectsFilePath))
            return ReadRecentProjectsStateFrom(RecentProjectsFilePath);

        if (File.Exists(LegacyRecentProjectsFilePath))
        {
            var legacyState = ReadRecentProjectsStateFrom(LegacyRecentProjectsFilePath);
            SaveRecentProjectsState(legacyState);
            return legacyState;
        }

        return new RecentProjectsState();
    }

    private RecentProjectsState ReadRecentProjectsStateFrom(string path)
    {
        if (!File.Exists(path))
            return new RecentProjectsState();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.RecentProjectsState)
                   ?? new RecentProjectsState();
        }
        catch
        {
            return new RecentProjectsState();
        }
    }

    private void SaveRecentProjectsState(RecentProjectsState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RecentProjectsFilePath) ?? AppDataRoot);

        var writerOptions = new JsonWriterOptions { Indented = true };
        using var stream = File.Create(RecentProjectsFilePath);
        using var writer = new Utf8JsonWriter(stream, writerOptions);
        JsonSerializer.Serialize(writer, state, AppJsonContext.Default.RecentProjectsState);
        writer.Flush();
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

    private void DeleteAssetFiles(ProjectAssetEntry asset)
    {
        string fullPath = GetAssetFullPath(asset);

        try
        {
            if (asset.StoredInProject && asset.AssetType == ProjectAssetType.Model)
            {
                string? assetFolder = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(assetFolder) && Directory.Exists(assetFolder))
                {
                    Directory.Delete(assetFolder, recursive: true);
                    return;
                }
            }

            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch
        {
            // Keep the manifest removal even if file deletion fails.
        }
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

    private static string ResolveDefaultProjectRoot()
    {
        string basePath = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        return Path.Combine(basePath, "Projects");
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

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (string filePath in Directory.GetFiles(sourceDir))
        {
            string destinationFile = Path.Combine(destinationDir, Path.GetFileName(filePath));
            File.Copy(filePath, destinationFile, overwrite: true);
        }

        foreach (string subDirectory in Directory.GetDirectories(sourceDir))
        {
            string destinationSubDirectory = Path.Combine(destinationDir, Path.GetFileName(subDirectory));
            CopyDirectory(subDirectory, destinationSubDirectory);
        }
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

    private static string MakeUniqueFilePath(string filePath)
    {
        if (!File.Exists(filePath))
            return filePath;

        string directory = Path.GetDirectoryName(filePath) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        string extension = Path.GetExtension(filePath);

        int index = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, fileName + "_" + index + extension);
            index++;
        } while (File.Exists(candidate));

        return candidate;
    }
}
