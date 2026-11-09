using System.Globalization;
using System.Text.Json;

namespace MineImatorSimplyRemade.core.project;

internal sealed class ProjectBackupService(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private ProjectManifest? _project;
    private string _projectPath = "";
    private long _lastAttempt;

    public void Update(ProjectManager project, bool enabled, int backupsToKeep, int intervalMinutes,
        bool canCapture, Action<ProjectManifest> captureScene)
    {
        long now = _clock.GetTimestamp();
        if (!ReferenceEquals(_project, project.Manifest) ||
            !string.Equals(_projectPath, project.ProjectFilePath, StringComparison.OrdinalIgnoreCase) ||
            !enabled || !project.HasProject)
        {
            _project = project.Manifest;
            _projectPath = project.ProjectFilePath;
            _lastAttempt = now;
            return;
        }

        if (!canCapture ||
            _clock.GetElapsedTime(_lastAttempt, now) < TimeSpan.FromMinutes(Math.Max(1, intervalMinutes)))
            return;

        // Failed attempts also wait an interval before retrying instead of spamming every frame.
        _lastAttempt = now;
        var snapshot = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(project.Manifest, AppJsonContext.Default.ProjectManifest),
            AppJsonContext.Default.ProjectManifest)
            ?? throw new InvalidDataException("Could not capture the project for backup.");
        captureScene(snapshot);
        snapshot.LastSavedUtc = _clock.GetUtcNow().ToString("o", CultureInfo.InvariantCulture);

        string folder = Path.Combine(project.ProjectFolder, "backups",
            Path.GetFileNameWithoutExtension(project.ProjectFilePath));
        Directory.CreateDirectory(folder);
        string timestamp = _clock.GetUtcNow().ToString("yyyyMMdd-HHmmssfffffff", CultureInfo.InvariantCulture);
        string path = Path.Combine(folder, $"backup-{timestamp}-{Guid.NewGuid():N}.nxProj");
        ProjectManager.WriteManifest(path, snapshot);

        // Never prune until the new backup is complete, and always retain that newest snapshot.
        foreach (string oldBackup in Directory.GetFiles(folder, "backup-*.nxProj")
                     .Where(file => !string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal)
                     .Skip(Math.Max(1, backupsToKeep) - 1))
            File.Delete(oldBackup);
    }
}
