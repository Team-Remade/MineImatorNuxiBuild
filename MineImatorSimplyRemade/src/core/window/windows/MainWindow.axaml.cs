using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Dock;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemade.core.window;

namespace MineImatorSimplyRemade.core.window.windows;

/// <summary>
/// Main application window.
///
/// MIGRATION STATUS: this is a partial port. What's ported and working: window shell,
/// menu bar wiring (delegates to the same handler methods the old ImGui version used),
/// app icon, About dialog, window title / unsaved-changes close confirmation, and the
/// toast notification mechanism. What's NOT ported yet (tracked as separate todo items,
/// since each is itself a substantial panel): the dockspace + all dockable panels
/// (Viewport, Scene Tree, Properties, Timeline, Content Browser, Spawn Menu), the
/// project home screen, the Render Output / Update / Resource Pack Import dialogs, and
/// undo/redo scene-snapshot tracking (all of which depend on the not-yet-ported
/// Viewport/Timeline/PropertiesPanel panels for their data).
/// </summary>
public partial class MainWindow : WindowBase
{
    private readonly ProjectManager _projectManager = ProjectManager.Instance;

    /// <summary>App-wide preferences model, persisted to disk. Edited through the
    /// ported <see cref="PreferencesView"/> dialog (see <see cref="OpenPreferencesDialog"/>).</summary>
    private readonly PreferencesPanel _preferences = new();

    /// <summary>The spawn menu's backing model. Owned here (not by the dock factory)
    /// because the spawn menu is a floating window, not a dock tool. Shared with
    /// ProjectSceneSerializer / PropertiesPanel once those hosts are wired.</summary>
    public SpawnMenu SpawnMenuModel { get; } = new();

    /// <summary>The viewport's backing model (scene objects, work camera, ground
    /// plane). Owned here so the scene survives dock-layout resets; rendered by
    /// <see cref="ViewportView"/> via <see cref="AppDockFactory"/>.</summary>
    public Viewport ViewportModel { get; } = new();

    private SpawnMenuWindow? _spawnMenuWindow;

    private readonly string _appTitle;
    private readonly string _aboutVersion;
    private string _lastAppliedWindowTitle = "";

    /// <summary>The dockspace layout's factory - exposes each panel's dock tool
    /// (e.g. <see cref="AppDockFactory.ViewportTool"/>) so a panel's porting pass can swap
    /// in its real content without rebuilding the layout.</summary>
    public AppDockFactory DockFactory { get; private set; } = null!;

    private bool _allowWindowClose;
    private bool _closeRequestedWhileDirty;

    private readonly DispatcherTimer _titleRefreshTimer;
    private const int MaxUndoEntries = 100;
    private const double UndoCommitDelaySeconds = 0.45;
    private readonly List<string> _undoSceneSnapshots = new();
    private readonly List<string> _redoSceneSnapshots = new();
    private string _lastHistorySnapshotJson = "";
    private string _lastHistoryFingerprint = "";
    private string _pendingHistorySnapshotJson = "";
    private string _pendingHistoryFingerprint = "";
    private DateTime _pendingHistoryChangedAtUtc;
    private bool _suppressHistoryTracking;

    public MainWindow()
    {
        InitializeComponent();

        _appTitle = "Mine Imator Nuxi";
        _aboutVersion = ResolveAppVersion();
        Title = _appTitle;

        _preferences.LoadPreferences();

        SpawnMenuModel.PreferencesPanel = _preferences;
        SpawnMenuModel.ProjectManager = _projectManager;

        // The global selection singleton must exist before the dock panels wire
        // their SelectionChanged subscriptions (and before the viewport creates
        // the gizmo, which registers itself with it).
        MineImatorSimplyRemadeNuxi.core.SelectionManager.Initialize();

        WireMenubar();
        WireDockLayout();
        WireHomeScreen();
        SetWindowIconFromEmbedded("icons.Icon");

        KeyDown += OnMainWindowKeyDown;
        Closing += OnMainWindowClosing;

        // Ported from RefreshWindowTitle()'s per-frame call in the old RenderUi() -
        // Avalonia has no per-frame hook, so a low-frequency timer takes its place.
        _titleRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _titleRefreshTimer.Tick += (_, _) =>
        {
            UpdateUndoRedoTracking();
            RefreshWindowTitle();
        };
        _titleRefreshTimer.Start();

        RefreshWindowTitle();
    }

    private void WireMenubar()
    {
        MenubarControl.NewProjectRequested = OpenNewProjectPopup;
        MenubarControl.OpenProjectRequested = OpenProjectFromDialog;
        MenubarControl.OpenRecentRequested = ShowHomeScreen;
        MenubarControl.SaveProjectRequested = SaveProjectWithScene;
        MenubarControl.SaveProjectAsRequested = OpenSaveAsPopup;
        MenubarControl.UndoRequested = PerformUndo;
        MenubarControl.RedoRequested = PerformRedo;
        MenubarControl.DuplicateRequested = () => DockFactory?.SceneTreeModel.DuplicateSelectedObjects();
        MenubarControl.DeleteRequested = () => DockFactory?.SceneTreeModel.DeleteSelectedObjects();
        MenubarControl.SpawnObjectRequested = OpenSpawnMenu;
        MenubarControl.ImportAssetRequested = ImportAssetFromDialog;
        MenubarControl.ImportResourcePackRequested = ImportResourcePackArchiveFromDialog;
        MenubarControl.ImportResourcePackFolderRequested = ImportResourcePackFolderFromDialog;
        MenubarControl.ResetLayoutRequested = ResetDockLayout;
        MenubarControl.ResetWorkCameraRequested = () => ViewportModel.Camera.ResetToDefaultPose();
        MenubarControl.HomeScreenRequested = ShowHomeScreen;
        MenubarControl.AboutRequested = OpenAboutDialog;
        MenubarControl.CheckForUpdatesRequested = () => { /* TODO(migration): update dialog */ };
        MenubarControl.ReportBugsRequested = OpenIssuesLink;
        MenubarControl.VisitForumsRequested = OpenForumsLink;
        MenubarControl.SupportUsRequested = OpenDonateLink;
        MenubarControl.RenderRequested = _ => { /* TODO(migration): render output dialog */ };
        MenubarControl.PreferencesRequested = OpenPreferencesDialog;
        MenubarControl.ExitRequested = () => Close();
    }

    // ── Dockspace layout ─────────────────────────────────────────────────────
    // Ported from SetupDefaultDockSpace()/RequestDockSpaceRebuild() (see
    // AppDockFactory's doc comment for the exact old ImGuiP.DockBuilder* split
    // this reproduces). Unlike the old version, there's no "only rebuild if no
    // imgui.ini exists yet" check - Dock.Avalonia's own layout persistence
    // (if/when wired up) would be a separate, explicit save/load pair rather
    // than an implicit ini file, so every launch currently starts from this
    // default layout.

    private void WireDockLayout()
    {
        DockFactory = new AppDockFactory(ViewportModel, OpenSpawnMenu, SpawnAssetFromContentBrowser,
            AddSoundToTimelineFromContentBrowser, ImportResourcePackArchiveFromDialog,
            ImportResourcePackFolderFromDialog);
        IRootDock layout = DockFactory.CreateLayout();
        DockFactory.InitLayout(layout);

        MainDockControl.Factory = DockFactory;
        MainDockControl.Layout = layout;

        // Wire the Scene Tree model to the global selection state now that the
        // layout (and thus the model) exists. Safe to call repeatedly on a
        // layout reset - Initialize() only subscribes when SelectionManager is
        // present.
        DockFactory.SceneTreeModel.Initialize();
        DockFactory.TimelineModel.Initialize();
        MineImatorSimplyRemadeNuxi.core.SelectionManager.Instance!.Timeline = DockFactory.TimelineModel;

        // Viewport model wiring: panel back-references plus the scene-object
        // hooks the other panels consume (formerly direct Viewport references
        // in the ImGui version).
        ViewportModel.PropertiesPanel = DockFactory.PropertiesModel;
        ViewportModel.PreferencesPanel = _preferences;
        ViewportModel.SpawnMenu = SpawnMenuModel;
        SpawnMenuModel.Viewport = ViewportModel;
        DockFactory.SceneTreeModel.SceneRoots = ViewportModel.SceneObjects;
        SpawnMenuModel.SceneChanged -= OnSpawnMenuSceneChanged;
        SpawnMenuModel.SceneChanged += OnSpawnMenuSceneChanged;
        DockFactory.TimelineModel.SceneObjectsProvider = () => ViewportModel.SceneObjects;

        // Properties panel: subscribe to selection + load current project
        // settings, then wire its viewport hooks.
        DockFactory.PropertiesModel.Timeline = DockFactory.TimelineModel;
        DockFactory.PropertiesModel.SpawnMenu = SpawnMenuModel;
        DockFactory.PropertiesModel.SceneObjectsProvider = () => ViewportModel.SceneObjects;
        DockFactory.PropertiesModel.SetGroundPlaneVisible = ViewportModel.SetGroundPlaneVisible;
        DockFactory.PropertiesModel.SetGroundPlaneTexture = ViewportModel.SetGroundPlaneTexture;
        DockFactory.PropertiesModel.SetBackgroundImage = ViewportModel.SetBackgroundImage;
        DockFactory.PropertiesModel.ReloadSkyTextures = ViewportModel.ReloadSkyTextures;
        DockFactory.PropertiesModel.SpawnFromLibraryEntry =
            entry => ProjectSceneSerializer.SpawnObjectFromEntry(entry, ViewportModel, SpawnMenuModel);
        DockFactory.PropertiesModel.Initialize();
    }

    private void OnSpawnMenuSceneChanged()
    {
        DockFactory?.SceneTreeModel.Refresh();
        DockFactory?.PropertiesModel.SynchronizeObjectLibrary();
    }

    private void ResetDockLayout() => WireDockLayout();

    private void WireHomeScreen()
    {
        HomeScreen.NewProjectRequested = OpenNewProjectPopup;
        HomeScreen.LoadProjectRequested = OpenProjectFromDialog;
        HomeScreen.OpenRecentRequested = OpenRecentProject;
        ShowHomeScreen();
    }

    private void ShowHomeScreen()
    {
        HomeScreen.Refresh();
        HomeScreen.IsVisible = true;
    }

    private void HideHomeScreen() => HomeScreen.IsVisible = false;

    // ── Spawn menu ──────────────────────────────────────────────────────────
    // The old renderer's SpawnMenu was a floating ImGui window toggled from the
    // viewport toolbar; until the Viewport is ported it opens from Edit >
    // Spawn Object... as a separate non-modal window sharing one model instance.

    private void OpenSpawnMenu()
    {
        if (_spawnMenuWindow != null)
        {
            _spawnMenuWindow.Activate();
            return;
        }

        _spawnMenuWindow = new SpawnMenuWindow(SpawnMenuModel);
        _spawnMenuWindow.Closed += (_, _) => _spawnMenuWindow = null;
        _spawnMenuWindow.Show(this);
    }

    private static string ResolveAppVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational!;

        string? fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (!string.IsNullOrWhiteSpace(fileVersion))
            return fileVersion!;

        return assembly.GetName().Version?.ToString() ?? "Unknown";
    }

    private void RefreshWindowTitle()
    {
        string title;
        if (_projectManager.HasProject)
        {
            string state = _projectManager.IsDirty ? "*" : "";
            title = $"{_appTitle} - {_projectManager.Manifest.ProjectName}{state}";
        }
        else
        {
            title = $"{_appTitle} - No Project";
        }

        if (string.Equals(title, _lastAppliedWindowTitle, StringComparison.Ordinal))
            return;

        Title = title;
        _lastAppliedWindowTitle = title;
    }

    // ── Toast notifications ─────────────────────────────────────────────────
    // Ported from RenderToast(): a short-lived success/error banner. Previously
    // drawn every frame from a fields-based timer check; now a DispatcherTimer
    // just hides the (already-visible) banner after a delay.

    public void ShowSuccessToast(string message) => ShowToast(message, Brushes.LightGreen);
    public void ShowErrorToast(string message) => ShowToast(message, Brushes.IndianRed);

    private void ShowToast(string message, IBrush color)
    {
        ToastText.Text = message;
        ToastText.Foreground = color;
        ToastBorder.IsVisible = true;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            ToastBorder.IsVisible = false;
            timer.Stop();
        };
        timer.Start();
    }

    // ── About dialog ─────────────────────────────────────────────────────────

    private async void OpenAboutDialog()
    {
        var dialog = new Window
        {
            Title = "About Mine Imator Nuxi Build",
            Width = 760,
            Height = 460,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var closeButton = new Button { Content = "Close", Width = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        closeButton.Click += (_, _) => dialog.Close();

        var donateButton = new Button { Content = "Donate (Ko-fi)", Width = 150 };
        donateButton.Click += (_, _) => OpenDonateLink();

        var discordButton = new Button { Content = "Join Discord", Width = 150 };
        discordButton.Click += (_, _) => OpenDiscordLink();

        var creditsScroll = new ScrollViewer
        {
            Height = 300,
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text =
                    "Mine Imator: David Andrei\n" +
                    "Mine Imator Development: David, Nimi, Marvin, Mbanders\n" +
                    "Mine Imator Beta Testing: 9redwoods, AnxiousCynic, Hozq, Jossamations, Rollo, SoundsDotZip, UpgradedMoon, _Mine_, Randi(11x)Stress, Alpha Toostrr, Cade [CaZaKoJa], Jnick, KeepOnChucking, SKIBBZ, Swooplezz, Vash, Nirwandra, Azaron\n" +
                    "Mine Imator Branding: Voxy\n\n" +
                    "Nuxi Project Management: frosty boi, AshFX\n" +
                    "Nuxi Development: frosty boi, Zandar, & Github Contributors\n" +
                    "Nuxi Beta Testing: AshFX, Pikan, Evelyn, Lolin\n" +
                    "Nuxi Branding: AshFX"
            }
        };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Mine Imator Nuxi Build", FontSize = 18, FontWeight = FontWeight.Bold },
                new TextBlock { Text = $"Version {_aboutVersion}", Foreground = Brushes.Gray },
                new Separator(),
                new TextBlock { Text = "Credits" },
                creditsScroll,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children = { donateButton, discordButton, closeButton }
                }
            }
        };

        await dialog.ShowDialog(this);
    }

    // ── Preferences dialog ─────────────────────────────────────────────────────
    // Ported from PreferencesPanel.Render(): the old ImGui version drew the
    // settings as a dockable/floating window. Avalonia gets a proper modal dialog
    // hosting the ported PreferencesView, bound to the shared _preferences model.

    private async void OpenPreferencesDialog()
    {
        var view = new PreferencesView();
        view.Bind(_preferences);

        var dialog = new Window
        {
            Title = "Preferences",
            Width = 520,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = view,
        };

        await dialog.ShowDialog(this);
    }

    // ── External links ───────────────────────────────────────────────────────

    private static void OpenLink(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch failures and keep the editor responsive.
        }
    }

    private static void OpenDonateLink() => OpenLink("https://ko-fi.com/forestw");
    private static void OpenDiscordLink() => OpenLink("https://discord.gg/eswvppFuAD");
    private static void OpenForumsLink() => OpenLink("https://www.mineimatorforums.com/");
    private static void OpenIssuesLink() => OpenLink("https://github.com/Team-Remade/MineImatorNuxiBuild/issues");

    // ── Project actions (kept intact from the old MainWindow - these are
    //    NativeFileDialogSharp/ProjectManager driven and have no ImGui dependency) ──

    private void OpenNewProjectPopup()
    {
        var projectName = new TextBox { Text = "Untitled Project" };
        var dialog = new Window { Title = "New Project", Width = 400, Height = 170, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var createButton = new Button { Content = "Create", Width = 100 };
        var cancelButton = new Button { Content = "Cancel", Width = 100 };
        createButton.Click += (_, _) =>
        {
            _projectManager.CreateNewProject(projectName.Text ?? "Untitled Project");
            ProjectSceneSerializer.LoadSceneFromManifest(_projectManager.Manifest, ViewportModel, SpawnMenuModel, DockFactory.TimelineModel, DockFactory.PropertiesModel);
            DockFactory.ContentBrowser?.Refresh();
            ResetUndoRedoHistory();
            RefreshWindowTitle();
            HideHomeScreen();
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Project name" }, projectName,
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { createButton, cancelButton } }
            }
        };
        dialog.ShowDialog(this);
    }

    private void OpenSaveAsPopup()
    {
        if (!_projectManager.HasProject)
        {
            OpenNewProjectPopup();
            return;
        }

        var projectName = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(_projectManager.Manifest.ProjectName)
                ? "Untitled Project"
                : _projectManager.Manifest.ProjectName
        };
        var dialog = new Window
        {
            Title = "Save Project As",
            Width = 400,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var saveButton = new Button { Content = "Save Copy", Width = 100 };
        var cancelButton = new Button { Content = "Cancel", Width = 100 };
        saveButton.Click += (_, _) =>
        {
            try
            {
                ProjectSceneSerializer.WriteSceneToManifest(_projectManager.Manifest, ViewportModel,
                    DockFactory.TimelineModel, DockFactory.PropertiesModel);
                _projectManager.SaveProjectAs(projectName.Text ?? "Untitled Project");
                RefreshWindowTitle();
                ShowSuccessToast($"Saved copy as {_projectManager.Manifest.ProjectName}");
                dialog.Close();
            }
            catch (Exception exception)
            {
                ShowErrorToast($"Save As failed: {exception.Message}");
            }
        };
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Project name (copy)" }, projectName,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { saveButton, cancelButton }
                }
            }
        };
        dialog.ShowDialog(this);
    }

    private void OpenProjectFromDialog()
    {
        var result = NativeFileDialogSharp.Dialog.FileOpen("nxProj");
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return;

        OpenProject(result.Path, "Failed to open project");
    }

    private void OpenRecentProject(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
        {
            _projectManager.RemoveRecentProject(projectFilePath);
            HomeScreen.Refresh();
            return;
        }

        OpenProject(projectFilePath, "Failed to open recent project");
    }

    private void OpenProject(string projectPath, string errorMessage)
    {
        if (!_projectManager.LoadProject(projectPath))
        {
            ShowErrorToast(errorMessage);
            return;
        }

        ReloadMinecraftDataForCurrentProject();
        ProjectSceneSerializer.LoadSceneFromManifest(_projectManager.Manifest, ViewportModel, SpawnMenuModel, DockFactory.TimelineModel, DockFactory.PropertiesModel);
        DockFactory.ContentBrowser?.Refresh();
        ResetUndoRedoHistory();
        RefreshWindowTitle();
        HideHomeScreen();
    }

    private void ImportAssetFromDialog()
    {
        if (!_projectManager.HasProject)
        {
            OpenNewProjectPopup();
            return;
        }

        var result = NativeFileDialogSharp.Dialog.FileOpen(
            "glb,gltf,fbx,obj,dae,3ds,blend,ply,stl,x3d,mimodel,miobject,png,jpg,jpeg,bmp,tga,gif,webp,tiff,wav,mp3,ogg,flac,m4a");
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return;

        try
        {
            _projectManager.AddAsset(result.Path, DetectAssetType(result.Path));
            DockFactory.ContentBrowser?.Refresh();
            ShowSuccessToast($"Imported {Path.GetFileName(result.Path)}");
        }
        catch (Exception exception)
        {
            ShowErrorToast($"Asset import failed: {exception.Message}");
        }
    }

    private static ProjectAssetType DetectAssetType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".gif" or ".webp" or ".tiff" => ProjectAssetType.Image,
            ".wav" or ".mp3" or ".ogg" or ".flac" or ".m4a" => ProjectAssetType.Sound,
            ".glb" or ".gltf" or ".fbx" or ".obj" or ".dae" or ".3ds" or ".blend" or ".ply" or ".stl" or ".x3d" or ".mimodel" or ".miobject" => ProjectAssetType.Model,
            _ => ProjectAssetType.Other
        };
    }

    private void ImportResourcePackArchiveFromDialog()
    {
        var result = NativeFileDialogSharp.Dialog.FileOpen("zip");
        if (result.IsOk && !string.IsNullOrWhiteSpace(result.Path))
            ImportResourcePack(result.Path);
    }

    private void ImportResourcePackFolderFromDialog()
    {
        var result = NativeFileDialogSharp.Dialog.FolderPicker();
        if (result.IsOk && !string.IsNullOrWhiteSpace(result.Path))
            ImportResourcePack(result.Path);
    }

    private void ImportResourcePack(string sourcePath)
    {
        if (!_projectManager.HasProject)
        {
            OpenNewProjectPopup();
            return;
        }

        try
        {
            string snapshot = CaptureSceneSnapshotJson();
            string importedPath = _projectManager.ImportResourcePack(sourcePath);
            ReloadMinecraftDataForCurrentProject();

            if (!string.IsNullOrWhiteSpace(snapshot))
                ApplySceneSnapshot(snapshot);

            DockFactory.SceneTreeModel.Refresh();
            DockFactory.ContentBrowser?.Refresh();
            ShowSuccessToast($"Imported resource pack {Path.GetFileName(importedPath)}");
        }
        catch (Exception exception)
        {
            ShowErrorToast($"Resource pack import failed: {exception.Message}");
        }
    }

    private void ReloadMinecraftDataForCurrentProject()
    {
        BlockRegistry.Initialize();
        TerrainAtlas.Initialize();
        ItemsAtlas.Initialize();
        SpawnMenuModel.RefreshExternalAssetOptions();
    }

    private void SpawnAssetFromContentBrowser(ProjectAssetEntry asset)
    {
        string path = _projectManager.GetAssetFullPath(asset);
        if (!File.Exists(path))
        {
            ShowErrorToast($"Asset is missing: {asset.DisplayName}");
            return;
        }

        var spawned = asset.AssetType == ProjectAssetType.Model
            ? SpawnMenuModel.SpawnCustomModelFromPath(path)
            : SpawnMenuModel.SpawnSchematicFromPathInteractive(path);
        if (spawned == null)
            ShowErrorToast($"Could not spawn {asset.DisplayName}");
    }

    private void AddSoundToTimelineFromContentBrowser(ProjectAssetEntry asset)
    {
        DockFactory.TimelineModel.AddAudioTrackFromAsset(asset);
        _projectManager.SetDirty(true);
        ShowSuccessToast($"Added {asset.DisplayName} to timeline");
    }

    private void SaveProjectWithScene()
    {
        if (!_projectManager.HasProject)
        {
            OpenNewProjectPopup();
            return;
        }

        try
        {
            ProjectSceneSerializer.WriteSceneToManifest(_projectManager.Manifest, ViewportModel,
                DockFactory.TimelineModel, DockFactory.PropertiesModel);
            _projectManager.SaveManifest();
            RefreshWindowTitle();
            ShowSuccessToast($"Saved {_projectManager.Manifest.ProjectName}");
        }
        catch (Exception exception)
        {
            ShowErrorToast($"Save failed: {exception.Message}");
        }
    }

    private void PerformUndo()
    {
        if (_undoSceneSnapshots.Count == 0)
            return;

        string currentSnapshot = CaptureSceneSnapshotJson();
        if (string.IsNullOrWhiteSpace(currentSnapshot))
            return;

        string targetSnapshot = _undoSceneSnapshots[^1];
        _undoSceneSnapshots.RemoveAt(_undoSceneSnapshots.Count - 1);
        PushSnapshot(_redoSceneSnapshots, currentSnapshot);

        if (!ApplySceneSnapshot(targetSnapshot))
        {
            _redoSceneSnapshots.RemoveAt(_redoSceneSnapshots.Count - 1);
            _undoSceneSnapshots.Add(targetSnapshot);
        }
    }

    private void PerformRedo()
    {
        if (_redoSceneSnapshots.Count == 0)
            return;

        string currentSnapshot = CaptureSceneSnapshotJson();
        if (string.IsNullOrWhiteSpace(currentSnapshot))
            return;

        string targetSnapshot = _redoSceneSnapshots[^1];
        _redoSceneSnapshots.RemoveAt(_redoSceneSnapshots.Count - 1);
        PushSnapshot(_undoSceneSnapshots, currentSnapshot);

        if (!ApplySceneSnapshot(targetSnapshot))
        {
            _undoSceneSnapshots.RemoveAt(_undoSceneSnapshots.Count - 1);
            _redoSceneSnapshots.Add(targetSnapshot);
        }
    }

    private string CaptureSceneSnapshotJson()
    {
        if (!_projectManager.HasProject)
            return "";

        var snapshot = new ProjectManifest
        {
            ProjectName = _projectManager.Manifest.ProjectName,
            CreatedUtc = _projectManager.Manifest.CreatedUtc,
            LastSavedUtc = _projectManager.Manifest.LastSavedUtc,
            Assets = new List<ProjectAssetEntry>(_projectManager.Manifest.Assets)
        };
        ProjectSceneSerializer.WriteSceneToManifest(snapshot, ViewportModel,
            DockFactory.TimelineModel, DockFactory.PropertiesModel);
        return JsonSerializer.Serialize(snapshot, AppJsonContext.Default.ProjectManifest);
    }

    private static string ComputeSnapshotFingerprint(string snapshotJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));

    private void ResetUndoRedoHistory()
    {
        _undoSceneSnapshots.Clear();
        _redoSceneSnapshots.Clear();
        _pendingHistorySnapshotJson = "";
        _pendingHistoryFingerprint = "";
        _lastHistorySnapshotJson = CaptureSceneSnapshotJson();
        _lastHistoryFingerprint = string.IsNullOrEmpty(_lastHistorySnapshotJson)
            ? ""
            : ComputeSnapshotFingerprint(_lastHistorySnapshotJson);
    }

    private static void PushSnapshot(List<string> history, string snapshot)
    {
        if (history.Count >= MaxUndoEntries)
            history.RemoveAt(0);
        history.Add(snapshot);
    }

    private void UpdateUndoRedoTracking()
    {
        if (_suppressHistoryTracking || !_projectManager.HasProject)
            return;

        string snapshot = CaptureSceneSnapshotJson();
        if (string.IsNullOrEmpty(snapshot))
            return;

        string fingerprint = ComputeSnapshotFingerprint(snapshot);
        if (string.IsNullOrEmpty(_lastHistoryFingerprint))
        {
            _lastHistorySnapshotJson = snapshot;
            _lastHistoryFingerprint = fingerprint;
            return;
        }

        if (fingerprint == _lastHistoryFingerprint)
        {
            _pendingHistorySnapshotJson = "";
            _pendingHistoryFingerprint = "";
            return;
        }

        if (fingerprint != _pendingHistoryFingerprint)
        {
            _pendingHistorySnapshotJson = snapshot;
            _pendingHistoryFingerprint = fingerprint;
            _pendingHistoryChangedAtUtc = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - _pendingHistoryChangedAtUtc).TotalSeconds < UndoCommitDelaySeconds)
            return;

        PushSnapshot(_undoSceneSnapshots, _lastHistorySnapshotJson);
        _redoSceneSnapshots.Clear();
        _lastHistorySnapshotJson = _pendingHistorySnapshotJson;
        _lastHistoryFingerprint = _pendingHistoryFingerprint;
        _pendingHistorySnapshotJson = "";
        _pendingHistoryFingerprint = "";
    }

    private bool ApplySceneSnapshot(string snapshotJson)
    {
        var snapshot = JsonSerializer.Deserialize(snapshotJson, AppJsonContext.Default.ProjectManifest);
        if (snapshot == null)
            return false;

        var preservedCamera = (ViewportModel.Camera.Target, ViewportModel.Camera.Yaw,
            ViewportModel.Camera.Pitch, ViewportModel.Camera.Distance);
        _suppressHistoryTracking = true;
        try
        {
            ProjectSceneSerializer.LoadSceneFromManifest(snapshot, ViewportModel, SpawnMenuModel,
                DockFactory.TimelineModel, DockFactory.PropertiesModel);
            ViewportModel.Camera.Target = preservedCamera.Target;
            ViewportModel.Camera.Yaw = preservedCamera.Yaw;
            ViewportModel.Camera.Pitch = preservedCamera.Pitch;
            ViewportModel.Camera.Distance = preservedCamera.Distance;
            DockFactory.SceneTreeModel.Refresh();
            MineImatorSimplyRemadeNuxi.core.SelectionManager.Instance?.RefreshSelection();
        }
        catch
        {
            return false;
        }
        finally
        {
            _suppressHistoryTracking = false;
        }

        // Rehydrating a scene can normalize runtime-only state (for example
        // timeline keyframe caches). Use its actual serialized form as the
        // next history baseline so the timer does not treat restoration as a
        // new edit and clear the redo stack.
        _lastHistorySnapshotJson = CaptureSceneSnapshotJson();
        _lastHistoryFingerprint = string.IsNullOrEmpty(_lastHistorySnapshotJson)
            ? ""
            : ComputeSnapshotFingerprint(_lastHistorySnapshotJson);
        _pendingHistorySnapshotJson = "";
        _pendingHistoryFingerprint = "";
        _projectManager.SetDirty(true);
        RefreshWindowTitle();
        return true;
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────────
    // Ported from HandleKeyboardShortcuts(): the old version polled
    // ImGui.IsKeyPressed(...) every frame; Avalonia is event-driven, so this is a
    // single KeyDown handler instead. F5/F7/F8/Delete/Space shortcuts that depend
    // on Viewport/Timeline are stubbed until those panels are ported.

    private void OnMainWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (!ctrl)
            return;

        switch (e.Key)
        {
            case Key.N:
                OpenNewProjectPopup();
                e.Handled = true;
                break;
            case Key.O:
                OpenProjectFromDialog();
                e.Handled = true;
                break;
            case Key.Z:
                if (shift) PerformRedo(); else PerformUndo();
                e.Handled = true;
                break;
            case Key.Y:
                PerformRedo();
                e.Handled = true;
                break;
            case Key.S:
                if (shift) OpenSaveAsPopup(); else SaveProjectWithScene();
                e.Handled = true;
                break;
        }
    }

    // ── Close confirmation ───────────────────────────────────────────────────
    // Ported from CanWindowClose()/the unsaved-changes modal that used to gate the
    // main.cs GLFW loop's exit condition. Avalonia has a real Closing event with a
    // cancellable args object, which is a much better fit than polling a bool every
    // frame.

    private async void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowWindowClose || !_projectManager.HasProject || !_projectManager.IsDirty)
            return;

        e.Cancel = true;

        if (_closeRequestedWhileDirty)
            return; // a confirmation dialog is already open

        _closeRequestedWhileDirty = true;
        try
        {
            bool discard = await ShowUnsavedChangesDialog();
            if (discard)
            {
                _allowWindowClose = true;
                Close();
            }
        }
        finally
        {
            _closeRequestedWhileDirty = false;
        }
    }

    private async System.Threading.Tasks.Task<bool> ShowUnsavedChangesDialog()
    {
        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 420,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        bool discard = false;

        var saveButton = new Button { Content = "Save", Width = 100 };
        var discardButton = new Button { Content = "Discard", Width = 100 };
        var cancelButton = new Button { Content = "Cancel", Width = 100 };

        saveButton.Click += (_, _) =>
        {
            SaveProjectWithScene();
            discard = true;
            dialog.Close();
        };
        discardButton.Click += (_, _) =>
        {
            discard = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            discard = false;
            dialog.Close();
        };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "This project has unsaved changes. Save before closing?", TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { saveButton, discardButton, cancelButton }
                }
            }
        };

        await dialog.ShowDialog(this);
        return discard;
    }
}
