using System.Text.Json;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;

internal static partial class Program
{
    private sealed class BackupClock : TimeProvider
    {
        private TimeSpan _elapsed;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _elapsed.Ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + _elapsed;
        public void Advance(TimeSpan time) => _elapsed += time;
    }

    private static ProjectManager BackupProject(Fixture fixture, string name = "test")
    {
        var manager = (ProjectManager)Activator.CreateInstance(typeof(ProjectManager), nonPublic: true)!;
        string path = fixture.File(name + ".nxProj", "{}");
        typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFolder))!.SetValue(manager, fixture.Root);
        typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFilePath))!.SetValue(manager, path);
        manager.Manifest.ProjectName = name;
        manager.SaveManifest();
        return manager;
    }

    private static string BackupFolder(ProjectManager project) =>
        Path.Combine(project.ProjectFolder, "backups", Path.GetFileNameWithoutExtension(project.ProjectFilePath));

    private static string[] BackupFiles(ProjectManager project) => Directory.Exists(BackupFolder(project))
        ? Directory.GetFiles(BackupFolder(project), "backup-*.nxProj")
        : [];

    private static void BackupPreferences(Fixture fixture)
    {
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.PreferencesState)!;
        Check(legacy.AutomaticBackups, "Legacy preferences enable backups");
        Equal(3, legacy.NumberOfBackupsToKeep, "Legacy retention default");
        Equal(5, legacy.BackupIntervalMinutes, "Legacy interval default");
        var panel = new PreferencesPanel();
        Equal(3, panel.NumberOfBackupsToKeep, "Panel retention default");
        Equal(5, panel.BackupIntervalMinutes, "Panel interval default");
        var state = new PreferencesState
        {
            AutomaticBackups = false, NumberOfBackupsToKeep = 7, BackupIntervalMinutes = 12
        };
        var restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(state, AppJsonContext.Default.PreferencesState),
            AppJsonContext.Default.PreferencesState)!;
        Check(!restored.AutomaticBackups, "Disabled preference persists");
        Equal(7, restored.NumberOfBackupsToKeep, "Retention persists");
        Equal(12, restored.BackupIntervalMinutes, "Interval persists");
    }

    private static void BackupSnapshot(Fixture fixture)
    {
        var manager = BackupProject(fixture);
        manager.Manifest.Assets.Add(new ProjectAssetEntry { RelativePath = @"images\texture.png" });
        manager.Manifest.ObjectLibrary.Add(new ProjectSceneObjectEntry { Name = "library-only" });
        manager.SaveManifest();
        string original = File.ReadAllText(manager.ProjectFilePath);
        string savedTime = manager.Manifest.LastSavedUtc;
        manager.SetDirty(true);
        var viewport = new Viewport();
        viewport.Camera.Distance = 123f;
        var root = new SceneObject { Name = "unsaved live object" };
        root.AddChild(new SceneObject { Name = "child" });
        viewport.SceneObjects.Add(root);
        var properties = new PropertiesPanel { SkyMode = "sphere" };
        var clock = new BackupClock();
        var service = new ProjectBackupService(clock);
        void Capture(ProjectManifest snapshot) =>
            ProjectSceneSerializer.WriteSceneToManifest(snapshot, viewport, propertiesPanel: properties);
        service.Update(manager, true, 3, 5, true, Capture);
        clock.Advance(TimeSpan.FromMinutes(5));
        service.Update(manager, true, 3, 5, true, Capture);
        string backup = BackupFiles(manager).Single();
        var restored = JsonSerializer.Deserialize(File.ReadAllText(backup), AppJsonContext.Default.ProjectManifest)!;
        Equal("unsaved live object", restored.SceneObjects.Single().Name, "Captures live rather than saved objects");
        Equal("child", restored.SceneObjects.Single().Children.Single().Name, "Captures hierarchy");
        Equal("sphere", restored.Settings.SkyMode, "Captures live project settings");
        Near(123f, restored.WorkCamera.Distance, "Captures work camera");
        Equal(@"images\texture.png", restored.Assets.Single().RelativePath, "Asset references stay project-relative");
        Check(restored.ObjectLibrary.Any(entry => entry.Name == "library-only"), "Existing library retained");
        Equal(original, File.ReadAllText(manager.ProjectFilePath), "Main save never overwritten");
        Equal(savedTime, manager.Manifest.LastSavedUtc, "Live last-saved timestamp unchanged");
        Equal(0, manager.Manifest.SceneObjects.Count, "Live manifest scene not replaced by snapshot");
        Equal(1, manager.Manifest.ObjectLibrary.Count, "Library capture isolated from live manifest");
        Check(manager.Manifest.Settings.SkyMode != "sphere", "Settings capture isolated from live manifest");
        Check(manager.IsDirty, "Backup does not clear unsaved flag");
        Check(!Directory.GetFiles(BackupFolder(manager)).Any(file => file.EndsWith(".tmp")), "No partial files");

        manager.SaveManifest();
        Check(!manager.IsDirty, "Normal save still clears unsaved flag");
        JsonSerializer.Deserialize(File.ReadAllText(manager.ProjectFilePath), AppJsonContext.Default.ProjectManifest);
    }

    private static void BackupScheduling(Fixture fixture)
    {
        var project = BackupProject(fixture);
        var clock = new BackupClock();
        var service = new ProjectBackupService(clock);
        int captures = 0;
        void Update(bool enabled = true, bool ready = true, int interval = 5) =>
            service.Update(project, enabled, 3, interval, ready, _ => captures++);
        Update();
        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        Update();
        Equal(0, captures, "No backup before exact interval");
        clock.Advance(TimeSpan.FromTicks(1));
        Update(ready: false);
        Equal(0, captures, "Busy editor defers due snapshot");
        Update();
        Equal(1, captures, "Due snapshot runs immediately after busy work");
        Update();
        Equal(1, captures, "No duplicate per-frame backup");
        clock.Advance(TimeSpan.FromMinutes(1));
        Update(interval: 1);
        Equal(2, captures, "Changed interval takes effect");
        clock.Advance(TimeSpan.FromMinutes(10));
        Update(enabled: false);
        Update();
        Equal(2, captures, "Re-enable starts a fresh interval");
        clock.Advance(TimeSpan.FromMinutes(5));
        Update();
        Equal(3, captures, "Re-enabled backups resume");

        project = BackupProject(fixture, "second");
        Update();
        Equal(3, captures, "Project switch starts a fresh interval");
        clock.Advance(TimeSpan.FromMinutes(5));
        Update();
        Equal(4, captures, "Second project gets its own backups");
        Equal(1, BackupFiles(project).Length, "Second project backup stored separately");

        // Reloading the same path replaces the manifest and must also restart the timer.
        typeof(ProjectManager).GetProperty(nameof(ProjectManager.Manifest))!.SetValue(project, new ProjectManifest());
        clock.Advance(TimeSpan.FromMinutes(5));
        Update();
        Equal(4, captures, "Same-path reload starts a fresh interval");
        clock.Advance(TimeSpan.FromMinutes(1));
        Update(interval: 0);
        Equal(5, captures, "Invalid interval has a one-minute minimum");
        Check(!project.IsDirty, "Backing up a clean project does not mark it dirty");

        var noProject = (ProjectManager)Activator.CreateInstance(typeof(ProjectManager), nonPublic: true)!;
        service.Update(noProject, true, 3, 5, true, _ => captures++);
        clock.Advance(TimeSpan.FromMinutes(10));
        service.Update(noProject, true, 3, 5, true, _ => captures++);
        Equal(5, captures, "No backup without an open project");
    }

    private static void BackupRetention(Fixture fixture)
    {
        var project = BackupProject(fixture);
        var clock = new BackupClock();
        var service = new ProjectBackupService(clock);
        int version = 0;
        void Update(int keep = 3) => service.Update(project, true, keep, 1, true,
            snapshot => snapshot.ProjectName = (++version).ToString());
        Update();
        for (int i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            Update();
        }
        var names = BackupFiles(project).Select(file =>
            JsonSerializer.Deserialize(File.ReadAllText(file), AppJsonContext.Default.ProjectManifest)!.ProjectName);
        Equal("3,4,5", string.Join(",", names.Order()), "Keeps exactly the latest three snapshots");
        string unrelated = fixture.File(Path.Combine("backups", "test", "manual.nxProj"), "{}");
        string otherProject = fixture.File(Path.Combine("backups", "another", "backup-old.nxProj"), "{}");
        clock.Advance(TimeSpan.FromMinutes(1));
        Update(1);
        Equal(1, BackupFiles(project).Length, "Lowered retention applies after next backup");
        Check(File.Exists(unrelated) && File.Exists(otherProject), "Rotation leaves unrelated files and projects alone");
        clock.Advance(TimeSpan.FromMinutes(1));
        Update(0);
        Equal(1, BackupFiles(project).Length, "Invalid retention still keeps one snapshot");
    }

    private static void BackupFailures(Fixture fixture)
    {
        var project = BackupProject(fixture);
        project.SetDirty(true);
        var clock = new BackupClock();
        var service = new ProjectBackupService(clock);
        bool fail = false;
        void Update() => service.Update(project, true, 1, 1, true, snapshot =>
        {
            if (fail)
            {
                var cyclic = new ProjectSceneObjectEntry();
                cyclic.Children.Add(cyclic);
                snapshot.SceneObjects.Add(cyclic);
            }
        });
        Update();
        clock.Advance(TimeSpan.FromMinutes(1));
        Update();
        string originalSave = File.ReadAllText(project.ProjectFilePath);
        string originalBackup = BackupFiles(project).Single();
        string originalContent = File.ReadAllText(originalBackup);
        fail = true;
        clock.Advance(TimeSpan.FromMinutes(1));
        Reject(Update, "Serialization failure must propagate");
        Equal(originalSave, File.ReadAllText(project.ProjectFilePath), "Failed backup leaves main save unchanged");
        Equal(originalContent, File.ReadAllText(originalBackup), "Failed backup does not prune last good snapshot");
        Equal(1, BackupFiles(project).Length, "Failed backup publishes no partial snapshot");
        Equal(1, Directory.GetFiles(BackupFolder(project)).Length, "Temporary file cleaned up after failure");
        Update();
        Equal(1, BackupFiles(project).Length, "Failed attempt is not retried on every frame");
        Check(project.IsDirty, "Failed backup preserves unsaved state");
        fail = false;
        clock.Advance(TimeSpan.FromMinutes(1));
        Update();
        Check(!File.Exists(originalBackup), "Successful retry rotates previous backup");

        string lastGoodBackup = BackupFiles(project).Single();
        clock.Advance(TimeSpan.FromMinutes(1));
        Reject(() => service.Update(project, true, 1, 1, true,
            _ => throw new InvalidDataException("Capture failed")), "Capture failure must propagate");
        Check(File.Exists(lastGoodBackup), "Capture failure preserves last good backup");
        Update();
        Equal(lastGoodBackup, BackupFiles(project).Single(), "Capture failure is not retried every frame");

        string blockedPath = fixture.File(Path.Combine("blocked", "backups"), "not a directory");
        typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFolder))!
            .SetValue(project, Path.GetDirectoryName(blockedPath));
        clock.Advance(TimeSpan.FromMinutes(1));
        bool reported = false;
        try { Update(); }
        catch (IOException) { reported = true; }
        Check(reported, "Filesystem errors must propagate to the notification handler");
        Equal(originalSave, File.ReadAllText(project.ProjectFilePath), "Filesystem failure preserves main save");
    }
}
