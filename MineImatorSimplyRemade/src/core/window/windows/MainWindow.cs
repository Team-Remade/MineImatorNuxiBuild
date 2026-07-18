using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.render;
using MineImatorSimplyRemade.core.startup;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemade.core.update;
using MineImatorSimplyRemadeNuxi.core;
using NativeFileDialogSharp;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.window.windows;

public class MainWindow : Window
{
    private readonly record struct SplashTextSegment(string Text, bool Strikethrough);

    public static bool IsAnimationRenderExportActive { get; private set; }

    private enum ProjectDialogMode
    {
        NewProject,
        SaveAs
    }

    private enum ToastKind
    {
        Success,
        Error
    }

    private enum PendingSaveAction
    {
        None,
        Save
    }

    private enum RenderMode
    {
        Image,
        Video
    }

    private enum ResourcePackImportStage
    {
        None,
        CopyPack,
        ReloadBlocks,
        ReloadTerrain,
        ReloadItems,
        RefreshUi,
        Complete
    }

    private readonly record struct ResolutionPreset(string Name, int Width, int Height);

    public static Random Rnd = new Random();

    private static readonly string ImGuiIniPath = "imgui.ini";
    private static readonly string SplashImagePath = Path.Combine(AppContext.BaseDirectory, "data/splashes", "splash.png");
    private static readonly string SplashCreditPath = Path.Combine(AppContext.BaseDirectory, "data/splashes", "credit.txt");
    private static readonly string SplashTextPath = Path.Combine(AppContext.BaseDirectory, "data/splashes", "splash.txt");

    public const string ViewportDockId = "Viewport";
    public const string SceneTreeDockId = "Scene Tree";
    public const string PropertiesDockId = "Properties";
    public const string TimelineDockId = "Timeline";
    public const string ContentBrowserDockId = "Content Browser";

    private bool _dockSpaceInitialized;
    private bool _dockSpaceRebuildRequested;
    private SpawnMenu? _spawnMenu;
    private Viewport? _cameraViewport;
    private ContentBrowser? _contentBrowser;
    private Viewport? _mainViewport;
    private Timeline? _timeline;
    private PropertiesPanel? _propertiesPanel;

    private readonly Menubar _menubar;
    private readonly ProjectManager _projectManager = ProjectManager.Instance;
    private readonly Dictionary<string, uint> _thumbnailTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _homeSplashPool = new();
    private readonly List<SplashTextSegment> _homeSplashSegments = new();
    private string _homeSplashPlainText = "Splash Screen Placeholder";
    private string _homeSplashCreditText = "(unassigned)";

    private bool _openProjectDialogPopup;
    private bool _openAboutPopup;
    private bool _openRenderPopup;
    private bool _showProjectHome = true;
    private string _newProjectNameBuffer = "Untitled Project";
    private ProjectDialogMode _projectDialogMode = ProjectDialogMode.NewProject;

    private RenderMode _renderPopupMode = RenderMode.Image;
    private int _renderPopupWidth = 1920;
    private int _renderPopupHeight = 1080;
    private int _renderPopupFramerate = 30;
    private int _renderPopupBitrateKbps = 12000;
    private bool _renderPopupHighQuality = true;
    private string _renderPopupImageFormat = "png";
    private string _renderPopupVideoFormat = "mp4";
    private string _renderPopupPreset = "1080P";

    private bool _renderJobActive;
    private bool _renderJobFinished;
    private RenderMode _renderJobMode = RenderMode.Image;
    private string _renderJobStatus = "";
    private string _renderJobOutputPath = "";
    private int _renderFrameCurrent;
    private int _renderFrameTotal = 1;
    private int _renderJobWidth;
    private int _renderJobHeight;
    private int _renderTimelineRestoreFrame;
    private bool _renderVideoWarmupFrame;
    private string _renderTempFramesDir = "";
    private Task<string>? _renderEncodeTask;
    private bool _closeRenderPopupRequested;
    private double _renderJobStartedAtSeconds;
    private uint _renderPreviewTexture;
    private int _renderPreviewWidth;
    private int _renderPreviewHeight;
    private RenderExporter? _renderExporter;

    private bool _openResourcePackImportPopup;
    private bool _resourcePackImportActive;
    private bool _resourcePackImportFinished;
    private ResourcePackImportStage _resourcePackImportStage = ResourcePackImportStage.None;
    private string _resourcePackImportSourcePath = "";
    private string _resourcePackImportedPath = "";
    private string _resourcePackImportStatus = "";
    private string _resourcePackImportDetail = "";
    private string _resourcePackImportError = "";
    private float _resourcePackImportProgress;

    // Update checker fields
    private bool _openUpdatePopup;
    private bool _updateCheckInProgress;
    private UpdateChecker.UpdateCheckResult? _lastUpdateCheckResult;
    private bool _updateDownloadInProgress;
    private float _updateDownloadProgress;
    private string _updateDownloadStatus = "";
    private Task? _updateCheckTask;
    private Task? _updateDownloadTask;

    private static readonly ResolutionPreset[] RenderResolutionPresets =
    [
        new("Avatar 512x512", 512, 512),
        new("VGA 640x480", 640, 480),
        new("720P", 1280, 720),
        new("1080P", 1920, 1080),
        new("1440P", 2560, 1440),
        new("720P Cinematic", 1280, 544),
        new("1080P Cinematic", 1920, 816),
        new("1440P Cinematic", 2560, 1088),
        new("1600P Cinematic", 3200, 1360),
        new("UW4k Cinematic", 3840, 1600),
        new("UW5k Cinematic", 5120, 2160),
        new("Preview 960x400", 960, 400)
    ];

    private static readonly int[] RenderFramerateOptions = [12, 24, 25, 30, 48, 50, 60, 120];
    private static readonly int[] RenderBitrateOptionsKbps = [4000, 8000, 12000, 20000, 40000, 80000];

    private string _toastMessage = "";
    private ToastKind _toastKind = ToastKind.Success;
    private double _toastExpiresAtSeconds;
    private bool _showSavingToast;

    private PendingSaveAction _pendingSaveAction;
    private bool _pendingSavePrimed;

    private bool _showUnsavedChangesDialog;
    private bool _handleCloseWithUnsavedChanges;
    private bool _allowWindowClose;

    private SceneTree? _sceneTree;
    private PreferencesPanel? _preferencesPanel;

    private readonly string _appTitle;
    private readonly string _aboutVersion;
    private string _lastAppliedWindowTitle = "";
    private string _savedSceneFingerprint = "";
    private double _nextDirtyCheckAtSeconds;
    private const double DirtyCheckIntervalSeconds = 0.35;

    private const int MaxUndoEntries = 100;
    private const double UndoCommitDelaySeconds = 0.45;

    private readonly List<string> _undoSceneSnapshots = new();
    private readonly List<string> _redoSceneSnapshots = new();

    private string _lastHistorySnapshotJson = "";
    private string _lastHistoryFingerprint = "";
    private string _pendingHistorySnapshotJson = "";
    private string _pendingHistoryFingerprint = "";
    private double _pendingHistoryChangedAtSeconds;
    private bool _suppressHistoryTracking;

    private readonly UiPanel[] _panels =
    [
        new Viewport(),
        new Timeline(),
        new ContentBrowser(),
        new SceneTree(),
        new PropertiesPanel(),
        new PreferencesPanel()
    ];

    public MainWindow(int width, int height, string title, Glfw glfw, GL? gl = null, bool visible = true) : base(width, height, title, glfw, gl!, visible)
    {
        _appTitle = title;
        _menubar = new Menubar();
        _menubar.NewProjectRequested = OpenNewProjectPopup;
        _menubar.OpenProjectRequested = OpenProjectFromDialog;
        _menubar.OpenRecentRequested = () =>
        {
            PickRandomHomeSplash();
            _showProjectHome = true;
        };
        _menubar.SaveProjectRequested = SaveProjectWithScene;
        _menubar.SaveProjectAsRequested = OpenSaveAsPopup;
        _menubar.UndoRequested = PerformUndo;
        _menubar.RedoRequested = PerformRedo;
        _menubar.DuplicateRequested = () => _sceneTree?.DuplicateSelectedObjects();
        _menubar.DeleteRequested = () => _sceneTree?.DeleteSelectedObjects();
        _menubar.ImportAssetRequested = ImportAssetFromDialog;
        _menubar.ImportResourcePackRequested = ImportResourcePackArchiveFromDialog;
        _menubar.ImportResourcePackFolderRequested = ImportResourcePackFolderFromDialog;
        _menubar.ResetLayoutRequested = RequestDockSpaceRebuild;
        _menubar.ResetWorkCameraRequested = () => _mainViewport?.Camera.ResetToDefaultPose();
        _menubar.HomeScreenRequested = () =>
        {
            PickRandomHomeSplash();
            _showProjectHome = true;
        };
        _menubar.AboutRequested = OpenAboutPopup;
        _menubar.CheckForUpdatesRequested = OpenUpdatePopup;
        _menubar.ReportBugsRequested = OpenIssuesLink;
        _menubar.VisitForumsRequested = OpenForumsLink;
        _menubar.SupportUsRequested = OpenDonateLink;
        _menubar.RenderRequested = OpenRenderPopup;
        _menubar.PreferencesRequested = () => _preferencesPanel?.ToggleVisibility();
        _menubar.ExitRequested = RequestApplicationExit;
        _aboutVersion = ResolveAppVersion();

        ImageResult icon;
        int rng = Rnd.Next(1, 1000);
        if (rng == 777)
            icon = LoadEmbeddedImage("icons.chegg");
        else if (rng < 500)
            icon = LoadEmbeddedImage("icons.Icon");
        else
            icon = LoadEmbeddedImage(Rnd.Next(0, 1) == 1 ? "icons.tamari" : "icons.prism");

        SetWindowIcon(icon);
        LoadHomeSplashes();
        LoadHomeSplashCredit();
        PickRandomHomeSplash();
        RefreshWindowTitle();
    }

    public Viewport? GetCameraViewport() => _cameraViewport;

    public override void SetGL(GL gl)
    {
        base.SetGL(gl);
    }

    public void InitializeRuntime(Action<StartupProgressState>? progress = null)
    {
        GL gl = GL;

        const int totalSteps = 7;

        void ReportStep(int currentStep, string phase, string status, float progressWithinStep, string detail = "")
        {
            float clampedStepProgress = Math.Clamp(progressWithinStep, 0f, 1f);
            progress?.Invoke(new StartupProgressState
            {
                Title = "Preparing Mine Imator Simply Remade",
                CurrentStep = currentStep,
                TotalSteps = totalSteps,
                Phase = phase,
                Status = status,
                Detail = detail,
                Progress = ((currentStep - 1) + clampedStepProgress) / totalSteps
            });
        }

        SelectionManager.Initialize();
        ReportStep(1, "Bootstrapping editor services", "Selection state ready.", 1f);

        BlockRegistry.Initialize((value, detail) => ReportStep(2, "Indexing Minecraft data", "Loading block registry...", value, detail));
        ReportStep(2, "Indexing Minecraft data", "Block registry ready.", 1f, $"Loaded version {BlockRegistry.LoadedVersion}");

        TerrainAtlas.Initialize(gl, (value, detail) => ReportStep(3, "Uploading block textures", "Building terrain atlas...", value, detail));
        ReportStep(3, "Uploading block textures", "Terrain atlas ready.", 1f, $"{TerrainAtlas.Textures.Count} texture(s) available");

        ItemsAtlas.Initialize(gl, (value, detail) => ReportStep(4, "Uploading item textures", "Building item atlas...", value, detail));
        ReportStep(4, "Uploading item textures", "Item atlas ready.", 1f, $"{ItemsAtlas.Textures.Count} tile(s) available");

        CharacterRegistry.Initialize((value, detail) => ReportStep(5, "Discovering characters", "Scanning model libraries...", value, detail));
        ReportStep(5, "Discovering characters", "Character registry ready.", 1f, $"{CharacterRegistry.Characters.Count} character(s) found");

        ReportStep(6, "Preparing runtime", "Binding model import systems...", 0f);
        MineImatorLoader.Instance.Initialize(gl);
        ReportStep(6, "Preparing runtime", "Model import systems ready.", 1f);

        ReportStep(7, "Constructing editor UI", "Creating panels and viewports...", 0f);

        Viewport? viewport = null;
        SceneTree? sceneTree = null;
        PropertiesPanel? propertiesPanel = null;
        PreferencesPanel? preferencesPanel = null;
        Timeline? timeline = null;

        foreach (UiPanel panel in _panels)
        {
            panel.Gl = gl;
            switch (panel)
            {
                case Viewport vp:
                    viewport = vp;
                    _mainViewport = vp;
                    vp.InitFramebuffer(1, 1);
                    vp.InitGroundPlane();
                    ReportStep(7, "Constructing editor UI", "Creating panels and viewports...", 0.30f, "Viewport framebuffer initialized");
                    break;
                case SceneTree st:
                    sceneTree = st;
                    _sceneTree = st;
                    break;
                case PropertiesPanel pp:
                    propertiesPanel = pp;
                    _propertiesPanel = pp;
                    break;
                case PreferencesPanel pref:
                    preferencesPanel = pref;
                    _preferencesPanel = pref;
                    break;
                case ContentBrowser cb:
                    _contentBrowser = cb;
                    break;
                case Timeline tl:
                    timeline = tl;
                    _timeline = tl;
                    break;
            }
        }

        if (sceneTree != null && viewport != null)
            sceneTree.Viewport = viewport;
        if (viewport != null && propertiesPanel != null)
            viewport.PropertiesPanel = propertiesPanel;
        if (viewport != null && preferencesPanel != null)
            viewport.PreferencesPanel = preferencesPanel;
        if (timeline != null && viewport != null)
            timeline.Viewport = viewport;
        if (timeline != null && propertiesPanel != null)
            propertiesPanel.Timeline = timeline;
        if (viewport != null && propertiesPanel != null)
            propertiesPanel.Viewport = viewport;
        if (timeline != null)
            SelectionManager.Instance.Timeline = timeline;

        if (viewport != null)
        {
            unsafe
            {
                viewport.GlfwApi = Glfw;
                viewport.GlfwWindow = windowHandle;
            }
        }

        sceneTree?.Initialize();
        propertiesPanel?.Initialize();
        timeline?.Initialize();
        ReportStep(7, "Constructing editor UI", "Creating panels and viewports...", 0.65f, "Panels initialized");

        if (viewport != null)
        {
            _spawnMenu = new SpawnMenu
            {
                Gl = gl,
                Viewport = viewport,
                ProjectManager = _projectManager,
                PreferencesPanel = preferencesPanel
            };
            if (propertiesPanel != null)
                propertiesPanel.SpawnMenu = _spawnMenu;
            viewport.SpawnMenu = _spawnMenu;
            if (_contentBrowser != null)
            {
                _contentBrowser.SpawnMenu = _spawnMenu;
                _contentBrowser.ImportResourcePackRequested = ImportResourcePackArchiveFromDialog;
                _contentBrowser.ImportResourcePackFolderRequested = ImportResourcePackFolderFromDialog;
            }
        }

        ReportStep(7, "Constructing editor UI", "Creating panels and viewports...", 0.82f, "Spawn tools connected");

        if (viewport != null)
        {
            _cameraViewport = new Viewport
            {
                Gl = gl,
                IsPreviewViewport = true,
                MainViewport = viewport,
                OverlaysEnabled = false
            };
            unsafe
            {
                _cameraViewport.GlfwApiPreview = Glfw;
                _cameraViewport.GlfwWindowPreview = windowHandle;
            }
            _cameraViewport.InitPreviewViewport(320, 200);
            viewport.PreviewViewport = _cameraViewport;
        }

        ReportStep(7, "Constructing editor UI", "Editor ready.", 1f, "Main window will appear shortly");

        // Load preferences after all UI is ready
        if (_preferencesPanel != null)
        {
            _preferencesPanel.LoadPreferences();
        }
    }

    protected override void RenderUi()
    {
        if (_mainViewport != null)
            _mainViewport.SuppressInlinePreviewViewport = _showProjectHome;

        HandleKeyboardShortcuts();
        ProcessPendingSaveAction();
        UpdateDirtyStateFromScene();
        RefreshWindowTitle();
        AdvanceRenderJob();
        AdvanceResourcePackImportJob();

        _menubar.Render();
        RenderProjectDialogs();
        RenderResourcePackImportPopup();
        RenderUnsavedChangesDialog();

        ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(mainViewport.WorkPos);
        ImGui.SetNextWindowSize(mainViewport.WorkSize);
        ImGui.SetNextWindowViewport(mainViewport.ID);

        ImGuiWindowFlags dockWindowFlags =
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoBackground;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("##DockSpaceWindow", dockWindowFlags);
        ImGui.PopStyleVar(3);

        uint dockspaceId = ImGui.GetID("##MainDockSpace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);

        bool shouldSetupDefaultDockspace = _dockSpaceRebuildRequested || (!_dockSpaceInitialized && !File.Exists(ImGuiIniPath));
        if (shouldSetupDefaultDockspace)
        {
            SetupDefaultDockSpace(dockspaceId, mainViewport.WorkSize);
            _dockSpaceInitialized = true;
            _dockSpaceRebuildRequested = false;
        }

        ImGui.End();

        foreach (UiPanel panel in _panels)
            panel.Render();

        _spawnMenu?.Render();
        UpdateUndoRedoTracking();
        RenderProjectHomeScreen();
        RenderToast();
    }

    private void RequestDockSpaceRebuild()
    {
        _dockSpaceRebuildRequested = true;
    }

    private void UpdateDirtyStateFromScene()
    {
        if (!_projectManager.HasProject || _mainViewport == null)
        {
            _projectManager.SetDirty(false);
            _savedSceneFingerprint = "";
            return;
        }

        double now = GetNowSeconds();
        if (now < _nextDirtyCheckAtSeconds)
            return;

        _nextDirtyCheckAtSeconds = now + DirtyCheckIntervalSeconds;

        string currentFingerprint = BuildSceneFingerprint();
        if (string.IsNullOrEmpty(currentFingerprint))
            return;

        if (string.IsNullOrEmpty(_savedSceneFingerprint))
            _savedSceneFingerprint = currentFingerprint;

        _projectManager.SetDirty(!string.Equals(currentFingerprint, _savedSceneFingerprint, StringComparison.Ordinal));
    }

    private void CaptureCurrentSceneAsSavedState()
    {
        if (!_projectManager.HasProject || _mainViewport == null)
        {
            _savedSceneFingerprint = "";
            _projectManager.SetDirty(false);
            return;
        }

        _savedSceneFingerprint = BuildSceneFingerprint();
        _projectManager.SetDirty(false);
    }

    private string BuildSceneFingerprint()
    {
        if (_mainViewport == null)
            return "";

        string snapshotJson = CaptureSceneSnapshotJson(out string fingerprint);
        if (string.IsNullOrEmpty(snapshotJson))
            return "";

        return fingerprint;
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

        SetWindowTitle(title);
        _lastAppliedWindowTitle = title;
    }

    private void HandleKeyboardShortcuts()
    {
        ImGuiIOPtr io = ImGui.GetIO();

        if (!io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F5, false))
        {
            if (_cameraViewport?.IsVisible == true)
                _cameraViewport.ToggleHighQualityPreview();
            else
                _mainViewport?.ToggleHighQualityPreview();
            return;
        }

        if (!io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F6, false))
        {
            _mainViewport?.ToggleShadowDebugMode();
            return;
        }

        if (!io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F7, false))
        {
            OpenRenderPopup(Menubar.RenderRequestKind.Image);
            return;
        }

        if (!io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F8, false))
        {
            OpenRenderPopup(Menubar.RenderRequestKind.Video);
            return;
        }

        if (!io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.Delete, false))
        {
            // Check if Timeline window is hovered to decide whether to delete keyframes or objects
            bool timelineHovered = false;
            if (_timeline != null && _timeline.WindowSize.X > 0 && _timeline.WindowSize.Y > 0)
            {
                var mousePos = ImGui.GetMousePos();
                var winPos = _timeline.WindowPos;
                var winSize = _timeline.WindowSize;
                timelineHovered = mousePos.X >= winPos.X && mousePos.X < winPos.X + winSize.X &&
                                 mousePos.Y >= winPos.Y && mousePos.Y < winPos.Y + winSize.Y;
            }

            if (timelineHovered)
            {
                _timeline?.DeleteSelectedKeyframes();
            }
            else
            {
                _sceneTree?.DeleteSelectedObjects();
            }
            return;
        }

        if (!io.KeyCtrl || io.WantTextInput)
            return;

        if (ImGui.IsKeyPressed(ImGuiKey.N))
        {
            OpenNewProjectPopup();
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.O))
        {
            OpenProjectFromDialog();
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.D))
        {
            _sceneTree?.DuplicateSelectedObjects();
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Z))
        {
            if (io.KeyShift)
                PerformRedo();
            else
                PerformUndo();
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Y))
        {
            PerformRedo();
            return;
        }

        if (io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.S))
        {
            OpenSaveAsPopup();
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.S))
            RequestSaveProject();
    }

    private void ProcessPendingSaveAction()
    {
        if (_pendingSaveAction == PendingSaveAction.None)
            return;

        if (!_pendingSavePrimed)
        {
            _pendingSavePrimed = true;
            _showSavingToast = true;
            return;
        }

        _pendingSavePrimed = false;
        _showSavingToast = false;

        if (_pendingSaveAction == PendingSaveAction.Save)
        {
            _pendingSaveAction = PendingSaveAction.None;
            SaveProjectWithSceneInternal();
        }
    }

    private void RequestSaveProject()
    {
        if (_pendingSaveAction != PendingSaveAction.None)
            return;

        _pendingSaveAction = PendingSaveAction.Save;
        _pendingSavePrimed = false;
    }

    private unsafe void RenderProjectHomeScreen()
    {
        if (!_showProjectHome)
            return;

        ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(mainViewport.WorkPos);
        ImGui.SetNextWindowSize(mainViewport.WorkSize);
        ImGui.SetNextWindowViewport(mainViewport.ID);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(28f, 24f));

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove;

        ImGui.Begin("Project Home Screen", flags);
        ImGui.PopStyleVar(3);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 splashMin = ImGui.GetCursorScreenPos();
        Vector2 splashSize = new Vector2(MathF.Min(ImGui.GetContentRegionAvail().X, 920f), 180f);
        Vector2 splashMax = splashMin + splashSize;
        uint splashTexture = GetThumbnailTexture(SplashImagePath);

        if (splashTexture != 0)
        {
            ImGui.SetCursorScreenPos(splashMin);
            ImGui.Image(new ImTextureRef(texId: (ulong)splashTexture), splashSize, Vector2.Zero, Vector2.One);
        }
        else
        {
            drawList.AddRectFilled(splashMin, splashMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0.16f, 0.18f, 0.22f, 1f)), 18f);
            ImGui.Dummy(splashSize);
        }

        drawList.AddRect(splashMin, splashMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0.34f, 0.40f, 0.48f, 1f)), 18f, ImDrawFlags.RoundCornersAll, 2f);
        drawList.AddText(
            splashMin + new Vector2(24f, 24f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.95f, 0.98f, 1f)),
            "Mine Imator Simply Remade");
        DrawHomeSplashText(
            drawList,
            splashMin + new Vector2(24f, 56f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.70f, 0.75f, 0.82f, 1f)));

        ImGui.TextDisabled($"Splash art credits: {_homeSplashCreditText}");
        ImGui.Spacing();

        float leftWidth = MathF.Min(320f, ImGui.GetContentRegionAvail().X * 0.34f);
        float recentWidth = ImGui.GetContentRegionAvail().X - leftWidth - ImGui.GetStyle().ItemSpacing.X;
        float bodyHeight = MathF.Max(260f, ImGui.GetContentRegionAvail().Y - 8f);

        ImGui.BeginChild("##projectActions", new Vector2(leftWidth, bodyHeight), ImGuiChildFlags.Borders);
        ImGui.Text("Projects");
        ImGui.Separator();
        ImGui.TextWrapped("Create a fresh project, load an existing one, or reopen something recent.");
        ImGui.Spacing();

        if (ImGui.Button("New Project", new Vector2(-1, 36f)))
            OpenNewProjectPopup();
        if (ImGui.Button("Load Project", new Vector2(-1, 36f)))
            OpenProjectFromDialog();

        ImGui.Spacing();
        if (_projectManager.HasProject)
        {
            ImGui.TextDisabled("Current project");
            ImGui.TextWrapped(_projectManager.Manifest.ProjectName);
            ImGui.TextWrapped(_projectManager.ProjectFilePath);
        }
        else
        {
            ImGui.TextDisabled("No project currently open.");
        }
        ImGui.EndChild();
        ImGui.SameLine();

        ImGui.BeginChild("##recentProjects", new Vector2(recentWidth, bodyHeight), ImGuiChildFlags.Borders);
        ImGui.Text("Recent Projects");
        ImGui.SameLine();
        ImGui.TextDisabled("(max 100)");
        ImGui.Separator();

        var recents = _projectManager.GetRecentProjects();
        if (recents.Count == 0)
        {
            ImGui.TextDisabled("No recent projects yet.");
        }
        else
        {
            const float cardWidth = 210f;
            const float thumbHeight = 96f;
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            int columns = Math.Max(1, (int)((recentWidth - 24f) / (cardWidth + spacing)));

            for (int i = 0; i < recents.Count; i++)
            {
                RecentProjectEntry recent = recents[i];
                ImGui.BeginChild($"##recentCard{i}", new Vector2(cardWidth, 190f), ImGuiChildFlags.Borders);

                Vector2 thumbMin = ImGui.GetCursorScreenPos();
                Vector2 thumbSize = new Vector2(cardWidth - 16f, thumbHeight);
                Vector2 thumbMax = thumbMin + thumbSize;
                drawList.AddRectFilled(thumbMin, thumbMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0.19f, 0.20f, 0.24f, 1f)), 10f);
                drawList.AddRect(thumbMin, thumbMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0.36f, 0.40f, 0.45f, 1f)), 10f, ImDrawFlags.RoundCornersAll, 1.5f);

                uint thumbnailTexture = GetThumbnailTexture(recent.ThumbnailPath);
                if (thumbnailTexture != 0)
                {
                    ImGui.Image(new ImTextureRef(texId: (ulong)thumbnailTexture), thumbSize, Vector2.Zero, Vector2.One);
                }
                else
                {
                    drawList.AddText(thumbMin + new Vector2(18f, 36f), ImGui.ColorConvertFloat4ToU32(new Vector4(0.82f, 0.84f, 0.88f, 1f)), "Thumbnail Placeholder");
                    ImGui.Dummy(thumbSize);
                }

                ImGui.TextWrapped(recent.ProjectName);
                ImGui.TextDisabled(Path.GetFileName(recent.ProjectFilePath));
                ImGui.TextDisabled(recent.LastOpenedUtc);

                bool exists = File.Exists(recent.ProjectFilePath);
                if (!exists)
                    ImGui.BeginDisabled();
                if (ImGui.Button("Open", new Vector2(-1, 28f)))
                    OpenRecentProject(recent.ProjectFilePath);
                if (!exists)
                    ImGui.EndDisabled();

                if (ImGui.Button("Remove From Recent", new Vector2(-1, 24f)))
                {
                    _projectManager.RemoveRecentProject(recent.ProjectFilePath);
                    ImGui.EndChild();
                    if ((i + 1) % columns != 0)
                        ImGui.SameLine();
                    continue;
                }

                if (!exists)
                {
                    ImGui.TextColored(new Vector4(0.92f, 0.55f, 0.42f, 1f), "Missing from disk");
                }

                ImGui.EndChild();
                if ((i + 1) % columns != 0)
                    ImGui.SameLine();
            }
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private void RenderProjectDialogs()
    {
        if (_openProjectDialogPopup)
        {
            _openProjectDialogPopup = false;
            ImGui.OpenPopup(GetProjectDialogTitle());
        }

        if (_openAboutPopup)
        {
            _openAboutPopup = false;
            ImGui.OpenPopup("About Mine Imator Nuxi Build");
        }

        bool popupOpen = true;
        string popupTitle = GetProjectDialogTitle();
        if (ImGui.BeginPopupModal(popupTitle, ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text(_projectDialogMode == ProjectDialogMode.SaveAs ? "Project name (copy)" : "Project name");
            ImGui.SetNextItemWidth(320f);
            ImGui.InputText("##newProjectName", ref _newProjectNameBuffer, 128);

            string actionLabel = _projectDialogMode == ProjectDialogMode.SaveAs ? "Save Copy" : "Create";
            if (ImGui.Button(actionLabel, new Vector2(120, 0)))
            {
                string name = string.IsNullOrWhiteSpace(_newProjectNameBuffer) ? "Untitled Project" : _newProjectNameBuffer.Trim();
                if (ExecuteProjectDialogAction(name))
                    ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        RenderAboutPopup();
        RenderUpdatePopup();

        RenderRenderPopup();
    }

    private void OpenAboutPopup()
    {
        _openAboutPopup = true;
    }

    private void OpenUpdatePopup()
    {
        _openUpdatePopup = true;
        
        // Start checking for updates if not already in progress
        if (!_updateCheckInProgress && _updateCheckTask == null)
        {
            _updateCheckInProgress = true;
            _lastUpdateCheckResult = null;
            _updateCheckTask = Task.Run(async () =>
            {
                try
                {
                    _lastUpdateCheckResult = await UpdateChecker.CheckForUpdatesAsync();
                }
                catch (Exception ex)
                {
                    _lastUpdateCheckResult = new UpdateChecker.UpdateCheckResult
                    {
                        Success = false,
                        Message = $"Error: {ex.Message}"
                    };
                }
                finally
                {
                    _updateCheckInProgress = false;
                }
            });
        }
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

    private void RenderAboutPopup()
    {
        bool popupOpen = true;
        if (!ImGui.BeginPopupModal("About Mine Imator Nuxi Build", ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.Text("Mine Imator Nuxi Build");
        ImGui.TextDisabled($"Version {_aboutVersion}");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("Credits");

        if (ImGui.BeginChild("##aboutCredits", new Vector2(720f, 300f), ImGuiChildFlags.Borders))
        {
            ImGui.TextWrapped("Mine Imator: David Andrei");
            ImGui.TextWrapped("Mine Imator Development: David, Nimi, Marvin, Mbanders");
            ImGui.TextWrapped("Mine Imator Beta Testing: 9redwoods, AnxiousCynic, Hozq, Jossamations, Rollo, SoundsDotZip, UpgradedMoon, _Mine_, Randi(11x)Stress, Alpha Toostrr, Cade [CaZaKoJa], Jnick, KeepOnChucking, SKIBBZ, Swooplezz, Vash, Nirwandra, Azaron");
            ImGui.TextWrapped("Mine Imator Branding: Voxy");
            ImGui.Spacing();
            ImGui.TextWrapped("Nuxi Project Management: frosty boi, AshFX");
            ImGui.TextWrapped("Nuxi Development: frosty boi, Zandar, & Github Contributors");
            ImGui.TextWrapped("Nuxi Beta Testing: AshFX, Pikan, Evelyn, Lolin");
            ImGui.TextWrapped("Nuxi Branding: AshFX");
        }
        ImGui.EndChild();

        ImGui.Spacing();
        if (ImGui.Button("Donate (Ko-fi)", new Vector2(150f, 0f)))
            OpenDonateLink();

        ImGui.SameLine();
        if (ImGui.Button("Join Discord", new Vector2(150f, 0f)))
            OpenDiscordLink();

        ImGui.SameLine();
        if (ImGui.Button("Close", new Vector2(120f, 0f)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void RenderUpdatePopup()
    {
        if (!_openUpdatePopup)
            return;

        bool isOpen = true;
        if (!ImGui.BeginPopupModal("Check for Updates", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!isOpen) _openUpdatePopup = false;
            return;
        }

        if (_updateCheckInProgress || _updateCheckTask != null && !_updateCheckTask.IsCompleted)
        {
            ImGui.Text("Checking for updates...");
            // Simple loading animation using text
            var spinnerChars = new[] { "|", "/", "—", "\\" };
            var frame = (int)((DateTime.UtcNow.Ticks / 100000000) % 4);
            ImGui.TextDisabled(spinnerChars[frame]);
        }
        else if (_lastUpdateCheckResult != null)
        {
            if (!_lastUpdateCheckResult.Success)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "Check Failed");
                ImGui.TextWrapped(_lastUpdateCheckResult.Message ?? "Unknown error");
            }
            else if (_lastUpdateCheckResult.UpdateAvailable)
            {
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1), "Update Available!");
                ImGui.Text($"Current Version: {UpdateChecker.GetCurrentVersion()}");
                ImGui.Text($"Available Version: {_lastUpdateCheckResult.AvailableVersion}");
                ImGui.Spacing();
                
                if (!string.IsNullOrWhiteSpace(_lastUpdateCheckResult.AvailableVersionName))
                {
                    ImGui.TextDisabled("Release Name:");
                    ImGui.TextWrapped(_lastUpdateCheckResult.AvailableVersionName);
                    ImGui.Spacing();
                }

                if (!string.IsNullOrWhiteSpace(_lastUpdateCheckResult.ChangeLog))
                {
                    ImGui.TextDisabled("Changelog:");
                    if (ImGui.BeginChild("##updateChangelog", new Vector2(600f, 200f), ImGuiChildFlags.Borders))
                    {
                        ImGui.TextWrapped(_lastUpdateCheckResult.ChangeLog);
                        ImGui.EndChild();
                    }
                    ImGui.Spacing();
                }

                if (_updateDownloadInProgress || _updateDownloadTask != null && !_updateDownloadTask.IsCompleted)
                {
                    ImGui.ProgressBar(_updateDownloadProgress, new Vector2(-1, 0), $"{(_updateDownloadProgress * 100):F1}%");
                    ImGui.TextWrapped(_updateDownloadStatus);
                }
                else
                {
                    if (ImGui.Button("Install Update", new Vector2(150, 0)))
                    {
                        if (!string.IsNullOrWhiteSpace(_lastUpdateCheckResult.DownloadUrl))
                        {
                            _updateDownloadInProgress = true;
                            _updateDownloadProgress = 0;
                            _updateDownloadStatus = "Installing update...";
                            _updateDownloadTask = Task.Run(async () =>
                            {
                                try
                                {
                                    var (success, message, needsRestart) = await UpdateChecker.InstallUpdateWhileRunningAsync(
                                        _lastUpdateCheckResult.DownloadUrl,
                                        (downloaded, total) =>
                                        {
                                            _updateDownloadProgress = total > 0 ? (float)downloaded / total : 0;
                                            _updateDownloadStatus = $"Progress: {FormatBytes(downloaded)} / {FormatBytes(total)}";
                                        });

                                    if (success && needsRestart)
                                    {
                                        _updateDownloadStatus = message;
                                        // Show restart prompt on next render
                                    }
                                    else if (success)
                                    {
                                        _updateDownloadStatus = $"Success: {message}";
                                    }
                                    else
                                    {
                                        _updateDownloadStatus = $"Error: {message}";
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _updateDownloadStatus = $"Error: {ex.Message}";
                                }
                                finally
                                {
                                    _updateDownloadInProgress = false;
                                }
                            });
                        }
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Visit Release", new Vector2(120, 0)))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "https://github.com/Team-Remade/MineImatorNuxiBuild/releases",
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1), "Up to Date!");
                ImGui.Text($"You are running the latest version ({UpdateChecker.GetCurrentVersion()})");
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Close", new Vector2(120, 0)))
        {
            ImGui.CloseCurrentPopup();
            _openUpdatePopup = false;
        }

        ImGui.EndPopup();
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static void OpenDonateLink()
    {
        const string donateUrl = "https://ko-fi.com/forestw";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = donateUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch failures and keep the editor responsive.
        }
    }

    private static void OpenDiscordLink()
    {
        const string discordInviteUrl = "https://discord.gg/eswvppFuAD";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = discordInviteUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch failures and keep the editor responsive.
        }
    }

    private static void OpenForumsLink()
    {
        const string forumsUrl = "https://www.mineimatorforums.com/";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = forumsUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch failures and keep the editor responsive.
        }
    }

    private static void OpenIssuesLink()
    {
        const string issuesUrl = "https://github.com/Team-Remade/MineImatorNuxiBuild/issues";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = issuesUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch failures and keep the editor responsive.
        }
    }

    private void OpenRenderPopup(Menubar.RenderRequestKind kind)
    {
        _renderPopupMode = kind == Menubar.RenderRequestKind.Video ? RenderMode.Video : RenderMode.Image;

        if (_propertiesPanel != null)
        {
            _renderPopupWidth = _propertiesPanel.GetResolutionWidth();
            _renderPopupHeight = _propertiesPanel.GetResolutionHeight();
            _renderPopupFramerate = _propertiesPanel.GetFramerate();

            _renderPopupMode = string.Equals(_propertiesPanel.GetRenderMode(), "video", StringComparison.OrdinalIgnoreCase)
                ? RenderMode.Video
                : _renderPopupMode;
            _renderPopupImageFormat = _propertiesPanel.GetRenderImageFormat();
            _renderPopupVideoFormat = _propertiesPanel.GetRenderVideoFormat();
            _renderPopupBitrateKbps = _propertiesPanel.GetRenderVideoBitrateKbps();
            _renderPopupPreset = _propertiesPanel.GetRenderResolutionPreset();
        }

        _openRenderPopup = true;
    }

    private void RenderRenderPopup()
    {
        if (_openRenderPopup)
        {
            _openRenderPopup = false;
            ImGui.OpenPopup("Render Output");
        }

        if (_renderJobActive)
            ImGui.OpenPopup("Render Output");

        bool popupOpen = true;
        if (!ImGui.BeginPopupModal("Render Output", ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (_closeRenderPopupRequested)
        {
            _closeRenderPopupRequested = false;
            _renderJobFinished = false;
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        if (_renderJobActive || _renderJobFinished)
            RenderRenderProgressSection();

        bool settingsChanged = false;
        bool controlsDisabled = _renderJobActive;
        if (controlsDisabled)
            ImGui.BeginDisabled();

        string modeLabel = _renderPopupMode == RenderMode.Video ? "Video" : "Image";
        if (ImGui.BeginCombo("Output Type", modeLabel))
        {
            bool imageSelected = _renderPopupMode == RenderMode.Image;
            if (ImGui.Selectable("Image", imageSelected))
            {
                _renderPopupMode = RenderMode.Image;
                settingsChanged = true;
            }

            bool videoSelected = _renderPopupMode == RenderMode.Video;
            if (ImGui.Selectable("Video", videoSelected))
            {
                _renderPopupMode = RenderMode.Video;
                settingsChanged = true;
            }

            ImGui.EndCombo();
        }

        string selectedPresetLabel = _renderPopupPreset;
        if (ImGui.BeginCombo("Resolution Preset", selectedPresetLabel))
        {
            foreach (ResolutionPreset preset in RenderResolutionPresets)
            {
                bool selected = string.Equals(_renderPopupPreset, preset.Name, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(preset.Name, selected))
                {
                    _renderPopupPreset = preset.Name;
                    _renderPopupWidth = preset.Width;
                    _renderPopupHeight = preset.Height;
                    settingsChanged = true;
                }
            }

            bool customSelected = string.Equals(_renderPopupPreset, "Custom", StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable("Custom", customSelected))
            {
                _renderPopupPreset = "Custom";
                settingsChanged = true;
            }

            ImGui.EndCombo();
        }

        int width = _renderPopupWidth;
        int height = _renderPopupHeight;
        bool widthChanged = ImGui.InputInt("Width", ref width, 0, 0, ImGuiInputTextFlags.None);
        bool heightChanged = ImGui.InputInt("Height", ref height, 0, 0, ImGuiInputTextFlags.None);
        if (widthChanged || heightChanged)
        {
            _renderPopupWidth = Math.Max(1, width);
            _renderPopupHeight = Math.Max(1, height);
            _renderPopupPreset = "Custom";
            settingsChanged = true;
        }

        bool highQuality = _renderPopupHighQuality;
        if (ImGui.Checkbox("Render high quality", ref highQuality))
            _renderPopupHighQuality = highQuality;

        ImGui.TextDisabled(_renderPopupHighQuality
            ? "Rendered mode: exports include lighting and shadows."
            : "Unrendered mode: exports keep the current fast viewport setup.");

        if (_renderPopupMode == RenderMode.Image)
        {
            string formatLabel = _renderPopupImageFormat.ToUpperInvariant();
            if (ImGui.BeginCombo("File Format", formatLabel))
            {
                foreach (string candidate in new[] { "png", "jpg", "webp" })
                {
                    bool selected = string.Equals(_renderPopupImageFormat, candidate, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(candidate.ToUpperInvariant(), selected))
                    {
                        _renderPopupImageFormat = candidate;
                        settingsChanged = true;
                    }
                }

                ImGui.EndCombo();
            }
        }
        else
        {
            string formatLabel = _renderPopupVideoFormat.ToUpperInvariant();
            if (ImGui.BeginCombo("File Format", formatLabel))
            {
                foreach (string candidate in new[] { "mp4", "webm" })
                {
                    bool selected = string.Equals(_renderPopupVideoFormat, candidate, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(candidate.ToUpperInvariant(), selected))
                    {
                        _renderPopupVideoFormat = candidate;
                        settingsChanged = true;
                    }
                }

                ImGui.EndCombo();
            }

            string fpsLabel = $"{_renderPopupFramerate} fps";
            if (ImGui.BeginCombo("Framerate", fpsLabel))
            {
                foreach (int fps in RenderFramerateOptions)
                {
                    bool selected = _renderPopupFramerate == fps;
                    if (ImGui.Selectable($"{fps} fps", selected))
                    {
                        _renderPopupFramerate = fps;
                        settingsChanged = true;
                    }
                }

                ImGui.EndCombo();
            }

            string bitrateLabel = $"{_renderPopupBitrateKbps} kbps";
            if (ImGui.BeginCombo("Bitrate", bitrateLabel))
            {
                foreach (int bitrate in RenderBitrateOptionsKbps)
                {
                    bool selected = _renderPopupBitrateKbps == bitrate;
                    if (ImGui.Selectable($"{bitrate} kbps", selected))
                    {
                        _renderPopupBitrateKbps = bitrate;
                        settingsChanged = true;
                    }
                }

                ImGui.EndCombo();
            }
        }

        if (settingsChanged)
            ApplyRenderPopupToProjectSettings();

        if (ImGui.Button(_renderPopupMode == RenderMode.Video ? "Start Render" : "Start Render", new Vector2(140, 0)))
        {
            StartRenderJob();
        }

        if (controlsDisabled)
            ImGui.EndDisabled();

        ImGui.SameLine();
        if (_renderJobActive)
        {
            if (ImGui.Button("Abort", new Vector2(120, 0)))
                CancelRenderJob("Render canceled");
        }
        else
        {
            if (ImGui.Button(_renderJobFinished ? "Close" : "Cancel", new Vector2(120, 0)))
            {
                _renderJobFinished = false;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndPopup();
    }

    private void AdvanceResourcePackImportJob()
    {
        if (!_resourcePackImportActive)
            return;

        try
        {
            switch (_resourcePackImportStage)
            {
                case ResourcePackImportStage.CopyPack:
                    _resourcePackImportStatus = "Copying resource pack into project...";
                    _resourcePackImportDetail = Path.GetFileName(_resourcePackImportSourcePath);
                    _resourcePackImportProgress = 0.08f;
                    _resourcePackImportedPath = _projectManager.ImportResourcePack(_resourcePackImportSourcePath);
                    _resourcePackImportProgress = 0.20f;
                    _resourcePackImportStage = ResourcePackImportStage.ReloadBlocks;
                    break;

                case ResourcePackImportStage.ReloadBlocks:
                    _resourcePackImportStatus = "Reloading block registry...";
                    BlockRegistry.Initialize((value, detail) =>
                    {
                        _resourcePackImportProgress = 0.20f + value * 0.22f;
                        _resourcePackImportDetail = detail;
                    });
                    _resourcePackImportProgress = 0.42f;
                    _resourcePackImportStage = ResourcePackImportStage.ReloadTerrain;
                    break;

                case ResourcePackImportStage.ReloadTerrain:
                    _resourcePackImportStatus = "Reloading terrain textures...";
                    TerrainAtlas.Initialize(GL, (value, detail) =>
                    {
                        _resourcePackImportProgress = 0.42f + value * 0.28f;
                        _resourcePackImportDetail = detail;
                    });
                    _resourcePackImportProgress = 0.70f;
                    _resourcePackImportStage = ResourcePackImportStage.ReloadItems;
                    break;

                case ResourcePackImportStage.ReloadItems:
                    _resourcePackImportStatus = "Reloading item textures...";
                    ItemsAtlas.Initialize(GL, (value, detail) =>
                    {
                        _resourcePackImportProgress = 0.70f + value * 0.24f;
                        _resourcePackImportDetail = detail;
                    });
                    _resourcePackImportProgress = 0.94f;
                    _resourcePackImportStage = ResourcePackImportStage.RefreshUi;
                    break;

                case ResourcePackImportStage.RefreshUi:
                    _resourcePackImportStatus = "Refreshing spawn menu options...";
                    _resourcePackImportDetail = "Syncing source selectors";
                    _spawnMenu?.RefreshExternalAssetOptions();
                    _resourcePackImportProgress = 1f;
                    _resourcePackImportStage = ResourcePackImportStage.Complete;
                    break;

                case ResourcePackImportStage.Complete:
                    _resourcePackImportActive = false;
                    _resourcePackImportFinished = true;
                    _resourcePackImportStatus = "Resource pack imported successfully.";
                    _resourcePackImportDetail = Path.GetFileName(_resourcePackImportedPath);
                    _resourcePackImportError = "";
                    ShowSuccessToast($"Imported resource pack: {Path.GetFileName(_resourcePackImportedPath)}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _resourcePackImportActive = false;
            _resourcePackImportFinished = true;
            _resourcePackImportError = ex.Message;
            _resourcePackImportStatus = "Resource pack import failed.";
            _resourcePackImportDetail = ex.Message;
            ShowErrorToast($"Resource pack import failed: {ex.Message}");
        }
    }

    private void RenderResourcePackImportPopup()
    {
        if (_openResourcePackImportPopup)
        {
            _openResourcePackImportPopup = false;
            ImGui.OpenPopup("Import Resource Pack");
        }

        if (_resourcePackImportActive || _resourcePackImportFinished)
            ImGui.OpenPopup("Import Resource Pack");

        bool popupOpen = true;
        if (!ImGui.BeginPopupModal("Import Resource Pack", ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        float clampedProgress = Math.Clamp(_resourcePackImportProgress, 0f, 1f);
        ImGui.Text(_resourcePackImportStatus);
        ImGui.ProgressBar(clampedProgress, new Vector2(360f, 0f), $"{clampedProgress * 100f:0.0}%");

        if (!string.IsNullOrWhiteSpace(_resourcePackImportDetail))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(_resourcePackImportDetail);
        }

        ImGui.Spacing();
        if (_resourcePackImportActive)
        {
            ImGui.TextDisabled("Please wait while assets are reloaded...");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(_resourcePackImportError))
                ImGui.TextColored(new Vector4(0.92f, 0.38f, 0.38f, 1f), _resourcePackImportError);

            if (ImGui.Button("Close", new Vector2(120f, 0f)))
            {
                _resourcePackImportFinished = false;
                _resourcePackImportStage = ResourcePackImportStage.None;
                _resourcePackImportSourcePath = "";
                _resourcePackImportedPath = "";
                _resourcePackImportStatus = "";
                _resourcePackImportDetail = "";
                _resourcePackImportError = "";
                _resourcePackImportProgress = 0f;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndPopup();
    }

    private unsafe void RenderRenderProgressSection()
    {
        float progress = _renderFrameTotal <= 0 ? 0f : Math.Clamp(_renderFrameCurrent / (float)_renderFrameTotal, 0f, 1f);
        double elapsed = Math.Max(0d, GetNowSeconds() - _renderJobStartedAtSeconds);

        ImGui.SeparatorText("Progress");
        ImGui.TextWrapped(_renderJobStatus);
        ImGui.ProgressBar(progress, new Vector2(360f, 0f), $"{progress * 100f:0.0}%");
        ImGui.Text($"Frames: {_renderFrameCurrent}/{_renderFrameTotal}");
        ImGui.Text($"Elapsed: {elapsed:0.0}s");

        if (_renderPreviewTexture != 0 && _renderPreviewWidth > 0 && _renderPreviewHeight > 0)
        {
            ImGui.Text("Preview:");
            float maxWidth = 360f;
            float previewWidth = maxWidth;
            float previewHeight = previewWidth * (_renderPreviewHeight / (float)_renderPreviewWidth);
            if (previewHeight > 220f)
            {
                previewHeight = 220f;
                previewWidth = previewHeight * (_renderPreviewWidth / (float)_renderPreviewHeight);
            }

            ImGui.Image(
                new ImTextureRef(texId: (ulong)_renderPreviewTexture),
                new Vector2(previewWidth, previewHeight),
                new Vector2(0, 0),
                new Vector2(1, 1));
        }

        ImGui.Separator();
    }

    private void ApplyRenderPopupToProjectSettings()
    {
        if (_propertiesPanel == null)
            return;

        _renderPopupWidth = Math.Max(1, _renderPopupWidth);
        _renderPopupHeight = Math.Max(1, _renderPopupHeight);
        _renderPopupFramerate = Math.Clamp(_renderPopupFramerate, 1, 120);
        _renderPopupBitrateKbps = Math.Clamp(_renderPopupBitrateKbps, 500, 200000);

        string mode = _renderPopupMode == RenderMode.Video ? "video" : "image";
        _propertiesPanel.SetRenderDimensionsAndFramerate(_renderPopupWidth, _renderPopupHeight, _renderPopupFramerate);
        _propertiesPanel.SetRenderExportSettings(mode, _renderPopupImageFormat, _renderPopupVideoFormat, _renderPopupBitrateKbps, _renderPopupPreset);

        _projectManager.SetDirty(true);
    }

    private void StartRenderJob()
    {
        if (_cameraViewport == null)
        {
            ShowErrorToast("Render failed: camera viewport is not initialized");
            return;
        }

        if (!_projectManager.HasProject)
        {
            ShowErrorToast("Open or create a project before rendering");
            return;
        }

        if (_renderJobActive)
            return;

        int width = Math.Max(1, _renderPopupWidth);
        int height = Math.Max(1, _renderPopupHeight);

        if (_renderPopupMode == RenderMode.Video)
        {
            if ((width & 1) != 0) width -= 1;
            if ((height & 1) != 0) height -= 1;
            width = Math.Max(2, width);
            height = Math.Max(2, height);
        }

        string rendersDir = Path.Combine(_projectManager.ProjectFolder, "renders");
        Directory.CreateDirectory(rendersDir);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string baseName = MakeSafeRenderBaseName(_projectManager.Manifest.ProjectName);

        string target;
        string? selectedOutput;
        if (_renderPopupMode == RenderMode.Video)
        {
            target = Path.Combine(rendersDir, $"{baseName}_{stamp}.{_renderPopupVideoFormat}");
            selectedOutput = PickRenderOutputPath(target, _renderPopupVideoFormat);
        }
        else
        {
            target = Path.Combine(rendersDir, $"{baseName}_{stamp}.{_renderPopupImageFormat}");
            selectedOutput = PickRenderOutputPath(target, _renderPopupImageFormat);
        }

        if (string.IsNullOrWhiteSpace(selectedOutput))
            return;

        string normalizedPath = EnsureOutputExtension(selectedOutput, _renderPopupMode == RenderMode.Video ? _renderPopupVideoFormat : _renderPopupImageFormat);

        try
        {
            IsAnimationRenderExportActive = false;
            _renderExporter = new RenderExporter(_cameraViewport);
            _renderJobMode = _renderPopupMode;
            _renderJobActive = true;
            _renderJobFinished = false;
            _renderJobStatus = "Starting render...";
            _renderJobOutputPath = normalizedPath;
            _renderJobStartedAtSeconds = GetNowSeconds();
            _renderJobWidth = width;
            _renderJobHeight = height;
            _renderFrameCurrent = 0;
            _renderVideoWarmupFrame = false;
            _closeRenderPopupRequested = false;

            if (_renderJobMode == RenderMode.Video)
            {
                IsAnimationRenderExportActive = true;
                if (_timeline == null)
                    throw new InvalidOperationException("Timeline is not initialized.");

                _renderTimelineRestoreFrame = _timeline.CurrentFrame;
                _renderFrameTotal = _timeline.MaxFrames + 1;
                _timeline.SetCurrentFrameForRender(0);
                _renderVideoWarmupFrame = true;
                _renderTempFramesDir = Path.Combine(rendersDir, $"_render_frames_{stamp}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(_renderTempFramesDir);
                _renderEncodeTask = null;
            }
            else
            {
                _renderTimelineRestoreFrame = _timeline?.CurrentFrame ?? 0;
                _renderFrameTotal = 1;
                _renderTempFramesDir = "";
                _renderEncodeTask = null;
            }

            _renderPopupWidth = width;
            _renderPopupHeight = height;
            ApplyRenderPopupToProjectSettings();
        }
        catch (Exception ex)
        {
            IsAnimationRenderExportActive = false;
            _renderJobActive = false;
            _renderJobFinished = true;
            _renderJobStatus = $"Render failed: {ex.Message}";
            ShowErrorToast(_renderJobStatus);
        }
    }

    private void AdvanceRenderJob()
    {
        if (!_renderJobActive || _cameraViewport == null || _renderExporter == null)
            return;

        try
        {
            if (_renderJobMode == RenderMode.Image)
            {
                _renderJobStatus = "Rendering image...";
                if (!_cameraViewport.CaptureCurrentViewRgb((uint)_renderJobWidth, (uint)_renderJobHeight, _renderPopupHighQuality, out byte[] frame))
                    throw new InvalidOperationException("Failed to capture render frame.");

                UpdateRenderPreviewTexture(frame, _renderJobWidth, _renderJobHeight);
                _renderExporter.ExportImageFromRgb(_renderJobWidth, _renderJobHeight, _renderPopupImageFormat, _renderJobOutputPath, frame);

                _renderFrameCurrent = 1;
                CompleteRenderJobSuccess();
                return;
            }

            if (_timeline == null)
                throw new InvalidOperationException("Render session is missing timeline state.");

            if (_renderVideoWarmupFrame)
            {
                _renderVideoWarmupFrame = false;
                _renderJobStatus = "Preparing frame 0...";
                return;
            }

            if (_renderFrameCurrent < _renderFrameTotal)
            {
                _timeline.SetCurrentFrameForRender(_renderFrameCurrent);
                if (!_cameraViewport.CaptureCurrentViewRgb((uint)_renderJobWidth, (uint)_renderJobHeight, _renderPopupHighQuality, out byte[] frame))
                    throw new InvalidOperationException($"Failed to capture frame {_renderFrameCurrent}.");

                UpdateRenderPreviewTexture(frame, _renderJobWidth, _renderJobHeight);

                string framePath = Path.Combine(_renderTempFramesDir, $"frame_{_renderFrameCurrent:D6}.ppm");
                WritePpmFrame(framePath, frame, _renderJobWidth, _renderJobHeight);

                int justRenderedFrame = _renderFrameCurrent;
                _renderFrameCurrent++;
                _renderJobStatus = $"Rendered frame {justRenderedFrame}/{_renderFrameTotal - 1}...";
            }

            if (_renderFrameCurrent >= _renderFrameTotal && _renderEncodeTask == null)
            {
                _renderJobStatus = "Encoding video...";
                string tempDir = _renderTempFramesDir;
                string outputPath = _renderJobOutputPath;
                int fps = _renderPopupFramerate;
                int bitrate = _renderPopupBitrateKbps;
                string format = _renderPopupVideoFormat;
                _renderEncodeTask = Task.Run(() => RenderExporter.EncodePpmSequenceToVideo(tempDir, fps, bitrate, format, outputPath));
            }

            if (_renderEncodeTask != null)
            {
                if (!_renderEncodeTask.IsCompleted)
                    return;

                if (_renderEncodeTask.IsFaulted)
                    throw _renderEncodeTask.Exception?.GetBaseException() ?? new InvalidOperationException("Video encoding failed.");

                CompleteRenderJobSuccess();
            }
        }
        catch (Exception ex)
        {
            FailRenderJob(ex.Message);
        }
    }

    private void CompleteRenderJobSuccess()
    {
        IsAnimationRenderExportActive = false;
        if (_timeline != null)
            _timeline.SetCurrentFrame(_renderTimelineRestoreFrame);

        _renderEncodeTask = null;
        _renderExporter = null;
        _renderJobActive = false;
        _renderJobFinished = true;
        _renderJobStatus = $"Render complete: {Path.GetFileName(_renderJobOutputPath)}";
        _closeRenderPopupRequested = true;
        CleanupRenderTempFrames();
        ShowSuccessToast($"Rendered: {Path.GetFileName(_renderJobOutputPath)}");
    }

    private void CancelRenderJob(string message)
    {
        IsAnimationRenderExportActive = false;
        if (_timeline != null)
            _timeline.SetCurrentFrame(_renderTimelineRestoreFrame);

        _renderEncodeTask = null;
        _renderExporter = null;
        _renderJobActive = false;
        _renderJobFinished = true;
        _renderJobStatus = message;
        _closeRenderPopupRequested = true;
        CleanupRenderTempFrames();
    }

    private void FailRenderJob(string error)
    {
        CancelRenderJob($"Render failed: {error}");
        ShowErrorToast(_renderJobStatus);
    }

    private unsafe void UpdateRenderPreviewTexture(byte[] rgbPixels, int width, int height)
    {
        if (GL == null)
            return;

        if (_renderPreviewTexture == 0)
        {
            GL.GenTextures(1, out _renderPreviewTexture);
            GL.BindTexture(GLEnum.Texture2D, _renderPreviewTexture);
            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }
        else
        {
            GL.BindTexture(GLEnum.Texture2D, _renderPreviewTexture);
        }

        GL.PixelStore(GLEnum.UnpackAlignment, 1);
        fixed (byte* p = rgbPixels)
        {
            if (_renderPreviewWidth != width || _renderPreviewHeight != height)
            {
                GL.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb8, (uint)width, (uint)height, 0, PixelFormat.Rgb, GLEnum.UnsignedByte, p);
                _renderPreviewWidth = width;
                _renderPreviewHeight = height;
            }
            else
            {
                GL.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, (uint)width, (uint)height, PixelFormat.Rgb, GLEnum.UnsignedByte, p);
            }
        }
        GL.PixelStore(GLEnum.UnpackAlignment, 4);
        GL.BindTexture(GLEnum.Texture2D, 0);
    }

    private void CleanupRenderTempFrames()
    {
        if (string.IsNullOrWhiteSpace(_renderTempFramesDir))
            return;

        try
        {
            if (Directory.Exists(_renderTempFramesDir))
                Directory.Delete(_renderTempFramesDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of temporary render frames.
        }

        _renderTempFramesDir = "";
    }

    private static void WritePpmFrame(string filePath, byte[] rgbPixels, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        byte[] header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");

        using var stream = File.Create(filePath);
        stream.Write(header, 0, header.Length);
        stream.Write(rgbPixels, 0, rgbPixels.Length);
    }

    private static string? PickRenderOutputPath(string defaultPath, string format)
    {
        string extension = NormalizeOutputExtension(format);
        var result = Dialog.FileSave(extension, defaultPath);
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return null;

        return result.Path;
    }

    private static string EnsureOutputExtension(string outputPath, string format)
    {
        string extension = NormalizeOutputExtension(format);
        string expected = "." + extension;

        if (string.Equals(Path.GetExtension(outputPath), expected, StringComparison.OrdinalIgnoreCase))
            return outputPath;

        return Path.ChangeExtension(outputPath, expected);
    }

    private static string NormalizeOutputExtension(string format)
    {
        string normalized = (format ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "jpeg" => "jpg",
            "png" => "png",
            "jpg" => "jpg",
            "webp" => "webp",
            "mp4" => "mp4",
            "webm" => "webm",
            _ => "png"
        };
    }

    private static string MakeSafeRenderBaseName(string projectName)
    {
        string fallback = string.IsNullOrWhiteSpace(projectName) ? "Render" : projectName.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            fallback = fallback.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(fallback) ? "Render" : fallback;
    }

    private void OpenProjectFromDialog()
    {
        var result = Dialog.FileOpen("nxProj");
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return;

        if (_projectManager.LoadProject(result.Path))
        {
            _showProjectHome = false;
            LoadProjectScene();
            CaptureCurrentSceneAsSavedState();
            RefreshWindowTitle();
        }
        else
        {
            ShowErrorToast("Failed to open project");
        }
    }

    private void OpenRecentProject(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return;

        if (!File.Exists(projectFilePath))
        {
            _projectManager.RemoveRecentProject(projectFilePath);
            return;
        }

        if (_projectManager.LoadProject(projectFilePath))
        {
            _showProjectHome = false;
            LoadProjectScene();
            CaptureCurrentSceneAsSavedState();
            RefreshWindowTitle();
        }
        else
        {
            ShowErrorToast("Failed to open recent project");
        }
    }

    private void ImportAssetFromDialog()
    {
        if (!_projectManager.HasProject)
        {
            OpenNewProjectPopup();
            return;
        }

        var result = Dialog.FileOpen("glb,gltf,fbx,obj,dae,3ds,blend,ply,stl,x3d,mimodel,miobject,png,jpg,jpeg,bmp,tga,gif,webp,tiff,wav,mp3,ogg,flac,m4a");
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return;

        _projectManager.AddAsset(result.Path, DetectAssetType(result.Path));
    }

    private void ImportResourcePackArchiveFromDialog()
    {
        if (!_projectManager.HasProject)
        {
            OpenNewProjectPopup();
            return;
        }

        var result = Dialog.FileOpen("zip");
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return;

        QueueResourcePackImport(result.Path);
    }

    private void ImportResourcePackFolderFromDialog()
    {
        if (!_projectManager.HasProject)
        {
            OpenNewProjectPopup();
            return;
        }

        var result = Dialog.FolderPicker();
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return;

        QueueResourcePackImport(result.Path);
    }

    private void QueueResourcePackImport(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;

        if (_resourcePackImportActive)
        {
            ShowErrorToast("A resource pack import is already running.");
            return;
        }

        _resourcePackImportSourcePath = sourcePath;
        _resourcePackImportedPath = "";
        _resourcePackImportError = "";
        _resourcePackImportProgress = 0f;
        _resourcePackImportStatus = "Preparing import...";
        _resourcePackImportDetail = Path.GetFileName(sourcePath);
        _resourcePackImportFinished = false;
        _resourcePackImportActive = true;
        _resourcePackImportStage = ResourcePackImportStage.CopyPack;
        _openResourcePackImportPopup = true;
    }

    private static ProjectAssetType DetectAssetType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".glb" or ".gltf" or ".fbx" or ".obj" or ".dae" or ".3ds" or ".blend" or ".ply" or ".stl" or ".x3d" or ".mimodel" or ".miobject")
            return ProjectAssetType.Model;
        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".gif" or ".webp" or ".tiff")
            return ProjectAssetType.Image;
        if (ext is ".wav" or ".mp3" or ".ogg" or ".flac" or ".m4a")
            return ProjectAssetType.Sound;
        return ProjectAssetType.Other;
    }

    private void SaveProjectWithScene()
    {
        if (!_projectManager.HasProject)
        {
            OpenNewProjectPopup();
            return;
        }

        RequestSaveProject();
    }

    private void SaveProjectWithSceneInternal()
    {
        if (!_projectManager.HasProject)
        {
            OpenNewProjectPopup();
            return;
        }

        if (_mainViewport == null)
            return;

        try
        {
            ProjectSceneSerializer.WriteSceneToManifest(_projectManager.Manifest, _mainViewport, _timeline, _propertiesPanel);
            _projectManager.SaveManifest();
            CaptureCurrentSceneAsSavedState();
            RefreshProjectThumbnail();
            RefreshWindowTitle();
            ShowSuccessToast($"Saved {_projectManager.Manifest.ProjectName}");
        }
        catch (Exception ex)
        {
            ShowErrorToast($"Save failed: {ex.Message}");
        }
    }

    private void LoadProjectScene()
    {
        if (_mainViewport == null || _spawnMenu == null || !_projectManager.HasProject)
            return;

        ReloadMinecraftDataForCurrentProject();
        ProjectSceneSerializer.LoadSceneFromManifest(_projectManager.Manifest, _mainViewport, _spawnMenu, _timeline, _propertiesPanel);
        ResetUndoRedoHistory();
    }

    private void ReloadMinecraftDataForCurrentProject()
    {
        BlockRegistry.Initialize();
        TerrainAtlas.Initialize(GL);
        ItemsAtlas.Initialize(GL);
        _spawnMenu?.RefreshExternalAssetOptions();
    }

    private ProjectManifest CreateSceneSnapshotManifest()
    {
        var manifestSnapshot = new ProjectManifest
        {
            ProjectName = _projectManager.Manifest.ProjectName,
            CreatedUtc = _projectManager.Manifest.CreatedUtc,
            LastSavedUtc = _projectManager.Manifest.LastSavedUtc,
            Assets = new List<ProjectAssetEntry>(_projectManager.Manifest.Assets)
        };

        if (_mainViewport != null)
            ProjectSceneSerializer.WriteSceneToManifest(manifestSnapshot, _mainViewport, _timeline, _propertiesPanel);

        // Preserve work camera state in snapshots so undo/redo restores the correct camera position.
        // Fly-camera-only changes are filtered out in UpdateUndoRedoTracking when they don't affect
        // the scene fingerprint, not by clearing the camera state.
        if (_projectManager.Manifest.WorkCamera != null)
            manifestSnapshot.WorkCamera = new ProjectWorkCameraState
            {
                Target = new ProjectVec3 { X = _projectManager.Manifest.WorkCamera.Target.X, Y = _projectManager.Manifest.WorkCamera.Target.Y, Z = _projectManager.Manifest.WorkCamera.Target.Z },
                Yaw = _projectManager.Manifest.WorkCamera.Yaw,
                Pitch = _projectManager.Manifest.WorkCamera.Pitch,
                Distance = _projectManager.Manifest.WorkCamera.Distance
            };

        return manifestSnapshot;
    }

    private static string ComputeSnapshotFingerprint(string snapshotJson)
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson));
        return Convert.ToHexString(hashBytes);
    }

    private string CaptureSceneSnapshotJson(out string fingerprint)
    {
        fingerprint = "";

        if (!_projectManager.HasProject || _mainViewport == null)
            return "";

        ProjectManifest manifestSnapshot = CreateSceneSnapshotManifest();
        string snapshotJson = JsonSerializer.Serialize(manifestSnapshot, AppJsonContext.Default.ProjectManifest);
        if (string.IsNullOrEmpty(snapshotJson))
            return "";

        fingerprint = ComputeSnapshotFingerprint(snapshotJson);
        return snapshotJson;
    }

    private void ResetUndoRedoHistory()
    {
        _undoSceneSnapshots.Clear();
        _redoSceneSnapshots.Clear();
        _pendingHistorySnapshotJson = "";
        _pendingHistoryFingerprint = "";
        _pendingHistoryChangedAtSeconds = 0;

        _lastHistorySnapshotJson = CaptureSceneSnapshotJson(out _lastHistoryFingerprint);
    }

    private void PushUndoSnapshot(string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return;

        if (_undoSceneSnapshots.Count >= MaxUndoEntries)
            _undoSceneSnapshots.RemoveAt(0);

        _undoSceneSnapshots.Add(snapshotJson);
    }

    private void PushRedoSnapshot(string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return;

        if (_redoSceneSnapshots.Count >= MaxUndoEntries)
            _redoSceneSnapshots.RemoveAt(0);

        _redoSceneSnapshots.Add(snapshotJson);
    }

    private bool ApplySceneSnapshot(string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson) || _mainViewport == null || _spawnMenu == null)
            return false;

        ProjectManifest? snapshotManifest = JsonSerializer.Deserialize(snapshotJson, AppJsonContext.Default.ProjectManifest);
        if (snapshotManifest == null)
            return false;

        _suppressHistoryTracking = true;
        try
        {
            ProjectSceneSerializer.LoadSceneFromManifest(snapshotManifest, _mainViewport, _spawnMenu, _timeline, _propertiesPanel);
        }
        finally
        {
            _suppressHistoryTracking = false;
        }

        _lastHistorySnapshotJson = snapshotJson;
        _lastHistoryFingerprint = ComputeSnapshotFingerprint(snapshotJson);
        _pendingHistorySnapshotJson = "";
        _pendingHistoryFingerprint = "";
        _pendingHistoryChangedAtSeconds = 0;
        _nextDirtyCheckAtSeconds = 0;
        UpdateDirtyStateFromScene();
        RefreshWindowTitle();
        return true;
    }

    private void UpdateUndoRedoTracking()
    {
        if (_suppressHistoryTracking || !_projectManager.HasProject || _mainViewport == null || _spawnMenu == null)
            return;

        string currentSnapshotJson = CaptureSceneSnapshotJson(out string currentFingerprint);
        if (string.IsNullOrEmpty(currentSnapshotJson))
            return;

        if (string.IsNullOrEmpty(_lastHistoryFingerprint))
        {
            _lastHistoryFingerprint = currentFingerprint;
            _lastHistorySnapshotJson = currentSnapshotJson;
            return;
        }

        if (string.Equals(currentFingerprint, _lastHistoryFingerprint, StringComparison.Ordinal))
        {
            _pendingHistorySnapshotJson = "";
            _pendingHistoryFingerprint = "";
            _pendingHistoryChangedAtSeconds = 0;
            return;
        }

        if (!string.Equals(currentFingerprint, _pendingHistoryFingerprint, StringComparison.Ordinal))
        {
            _pendingHistoryFingerprint = currentFingerprint;
            _pendingHistorySnapshotJson = currentSnapshotJson;
            _pendingHistoryChangedAtSeconds = GetNowSeconds();
            return;
        }

        if (GetNowSeconds() - _pendingHistoryChangedAtSeconds < UndoCommitDelaySeconds)
            return;

        PushUndoSnapshot(_lastHistorySnapshotJson);
        _redoSceneSnapshots.Clear();

        _lastHistoryFingerprint = _pendingHistoryFingerprint;
        _lastHistorySnapshotJson = _pendingHistorySnapshotJson;
        _pendingHistoryFingerprint = "";
        _pendingHistorySnapshotJson = "";
        _pendingHistoryChangedAtSeconds = 0;
    }

    private void PerformUndo()
    {
        if (_undoSceneSnapshots.Count == 0)
            return;

        string currentSnapshotJson = CaptureSceneSnapshotJson(out _);
        if (string.IsNullOrWhiteSpace(currentSnapshotJson))
            return;

        string targetSnapshot = _undoSceneSnapshots[^1];
        _undoSceneSnapshots.RemoveAt(_undoSceneSnapshots.Count - 1);
        PushRedoSnapshot(currentSnapshotJson);

        if (!ApplySceneSnapshot(targetSnapshot))
        {
            string redoSnapshot = _redoSceneSnapshots[^1];
            _redoSceneSnapshots.RemoveAt(_redoSceneSnapshots.Count - 1);
            _undoSceneSnapshots.Add(targetSnapshot);
            ApplySceneSnapshot(redoSnapshot);
        }
    }

    private void PerformRedo()
    {
        if (_redoSceneSnapshots.Count == 0)
            return;

        string currentSnapshotJson = CaptureSceneSnapshotJson(out _);
        if (string.IsNullOrWhiteSpace(currentSnapshotJson))
            return;

        string targetSnapshot = _redoSceneSnapshots[^1];
        _redoSceneSnapshots.RemoveAt(_redoSceneSnapshots.Count - 1);
        PushUndoSnapshot(currentSnapshotJson);

        if (!ApplySceneSnapshot(targetSnapshot))
        {
            string undoSnapshot = _undoSceneSnapshots[^1];
            _undoSceneSnapshots.RemoveAt(_undoSceneSnapshots.Count - 1);
            _redoSceneSnapshots.Add(targetSnapshot);
            ApplySceneSnapshot(undoSnapshot);
        }
    }

    private void OpenNewProjectPopup()
    {
        _projectDialogMode = ProjectDialogMode.NewProject;
        _newProjectNameBuffer = "Untitled Project";
        _openProjectDialogPopup = true;
    }

    private void OpenSaveAsPopup()
    {
        if (!_projectManager.HasProject)
        {
            OpenNewProjectPopup();
            return;
        }

        _projectDialogMode = ProjectDialogMode.SaveAs;
        _newProjectNameBuffer = string.IsNullOrWhiteSpace(_projectManager.Manifest.ProjectName)
            ? "Untitled Project"
            : _projectManager.Manifest.ProjectName;
        _openProjectDialogPopup = true;
    }

    private string GetProjectDialogTitle()
    {
        return _projectDialogMode == ProjectDialogMode.SaveAs ? "Save Project As" : "New Project";
    }

    private bool ExecuteProjectDialogAction(string name)
    {
        try
        {
            if (_projectDialogMode == ProjectDialogMode.SaveAs)
            {
                if (_mainViewport == null)
                    return false;

                ProjectSceneSerializer.WriteSceneToManifest(_projectManager.Manifest, _mainViewport, _timeline, _propertiesPanel);
                _projectManager.SaveProjectAs(name);
                CaptureCurrentSceneAsSavedState();
                RefreshProjectThumbnail();
                RefreshWindowTitle();
                ShowSuccessToast($"Saved copy as {name}");
                _showProjectHome = false;
                return true;
            }

            _projectManager.CreateNewProject(name);
            _showProjectHome = false;

            if (_mainViewport != null && _spawnMenu != null)
                ProjectSceneSerializer.LoadSceneFromManifest(_projectManager.Manifest, _mainViewport, _spawnMenu, _timeline, _propertiesPanel);

            SaveProjectWithSceneInternal();
            RefreshWindowTitle();
            return true;
        }
        catch (Exception ex)
        {
            ShowErrorToast($"{GetProjectDialogTitle()} failed: {ex.Message}");
            return false;
        }
    }

    private void ShowSuccessToast(string message)
    {
        ShowToast(message, ToastKind.Success, 2.4);
    }

    private void ShowErrorToast(string message)
    {
        ShowToast(message, ToastKind.Error, 4.0);
    }

    private void ShowToast(string message, ToastKind kind, double durationSeconds)
    {
        _toastMessage = message;
        _toastKind = kind;
        _toastExpiresAtSeconds = GetNowSeconds() + durationSeconds;
    }

    private void RenderToast()
    {
        if (_showSavingToast)
        {
            RenderToastWindow("Saving...", ToastKind.Success, 1f);
            return;
        }

        if (string.IsNullOrWhiteSpace(_toastMessage))
            return;

        double now = GetNowSeconds();
        if (now >= _toastExpiresAtSeconds)
        {
            _toastMessage = "";
            return;
        }

        double remaining = _toastExpiresAtSeconds - now;
        float alpha = 1f;
        const float fadeWindowSeconds = 0.45f;
        if (remaining < fadeWindowSeconds)
            alpha = Math.Clamp((float)(remaining / fadeWindowSeconds), 0f, 1f);

        RenderToastWindow(_toastMessage, _toastKind, alpha);
    }

    private static double GetNowSeconds()
    {
        return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }

    private void RenderToastWindow(string message, ToastKind kind, float alpha)
    {
        ImGuiViewportPtr mainViewport = ImGui.GetMainViewport();
        Vector2 size = new Vector2(280f, 0f);
        Vector2 pos = new Vector2(mainViewport.WorkPos.X + mainViewport.WorkSize.X - size.X - 20f, mainViewport.WorkPos.Y + 20f);

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowViewport(mainViewport.ID);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f, 12f));

        Vector4 bgColor;
        Vector4 borderColor;
        Vector4 textColor;
        if (kind == ToastKind.Error)
        {
            bgColor = new Vector4(0.24f, 0.10f, 0.10f, 0.96f * alpha);
            borderColor = new Vector4(0.76f, 0.28f, 0.28f, 1f * alpha);
            textColor = new Vector4(1.00f, 0.82f, 0.82f, alpha);
        }
        else
        {
            bgColor = new Vector4(0.10f, 0.16f, 0.12f, 0.96f * alpha);
            borderColor = new Vector4(0.28f, 0.55f, 0.34f, 1f * alpha);
            textColor = new Vector4(0.72f, 0.96f, 0.78f, alpha);
        }

        ImGui.PushStyleColor(ImGuiCol.WindowBg, bgColor);
        ImGui.PushStyleColor(ImGuiCol.Border, borderColor);

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoInputs;

        ImGui.Begin("##SaveToast", flags);
        ImGui.TextColored(textColor, message);
        ImGui.End();

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }

    private void RefreshProjectThumbnail()
    {
        if (_mainViewport == null || !_projectManager.HasProject)
            return;

        string thumbnailPath = Path.Combine(_projectManager.ProjectFolder, "thumbnail.png");
        if (_mainViewport.SaveThumbnail(thumbnailPath))
        {
            _projectManager.UpdateRecentProjectThumbnail(_projectManager.ProjectFilePath, thumbnailPath);
            InvalidateThumbnailTexture(thumbnailPath);
        }
    }

    private uint GetThumbnailTexture(string thumbnailPath)
    {
        if (GL == null || string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
            return 0;

        if (_thumbnailTextures.TryGetValue(thumbnailPath, out uint cached))
            return cached;

        try
        {
            using var stream = File.OpenRead(thumbnailPath);
            ImageResult img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            GL.GenTextures(1, out uint texId);
            GL.BindTexture(GLEnum.Texture2D, texId);
            unsafe
            {
                fixed (byte* p = img.Data)
                {
                    GL.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, (uint)img.Width, (uint)img.Height, 0, PixelFormat.Rgba, GLEnum.UnsignedByte, p);
                }
            }

            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(GLEnum.Texture2D, 0);

            _thumbnailTextures[thumbnailPath] = texId;
            return texId;
        }
        catch
        {
            return 0;
        }
    }

    private void InvalidateThumbnailTexture(string thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath))
            return;

        if (_thumbnailTextures.TryGetValue(thumbnailPath, out uint texId))
        {
            GL.DeleteTextures(1, in texId);
            _thumbnailTextures.Remove(thumbnailPath);
        }
    }

    private void LoadHomeSplashes()
    {
        _homeSplashPool.Clear();

        try
        {
            if (!File.Exists(SplashTextPath))
                return;

            foreach (string raw in File.ReadAllLines(SplashTextPath))
            {
                string line = raw.Trim();
                if (!string.IsNullOrWhiteSpace(line))
                    _homeSplashPool.Add(line);
            }
        }
        catch
        {
            // Keep fallback splash text if loading fails.
        }
    }

    private void LoadHomeSplashCredit()
    {
        _homeSplashCreditText = "(unassigned)";

        try
        {
            if (!File.Exists(SplashCreditPath))
                return;

            string creditText = File.ReadAllText(SplashCreditPath).Trim();
            if (!string.IsNullOrWhiteSpace(creditText))
                _homeSplashCreditText = creditText;
        }
        catch
        {
            // Keep fallback credit text if loading fails.
        }
    }

    private void PickRandomHomeSplash()
    {
        if (_homeSplashPool.Count == 0)
            LoadHomeSplashes();

        string selected = _homeSplashPool.Count > 0
            ? _homeSplashPool[Rnd.Next(_homeSplashPool.Count)]
            : "Splash Screen Placeholder";

        _homeSplashSegments.Clear();
        _homeSplashSegments.AddRange(ParseSplashSegments(selected));

        var plain = new StringBuilder();
        foreach (SplashTextSegment segment in _homeSplashSegments)
            plain.Append(segment.Text);

        _homeSplashPlainText = plain.Length > 0 ? plain.ToString() : "Splash Screen Placeholder";
    }

    private static List<SplashTextSegment> ParseSplashSegments(string source)
    {
        var result = new List<SplashTextSegment>();
        if (string.IsNullOrEmpty(source))
        {
            result.Add(new SplashTextSegment("", false));
            return result;
        }

        var buffer = new StringBuilder();
        bool strike = false;

        void Flush()
        {
            if (buffer.Length == 0)
                return;

            result.Add(new SplashTextSegment(buffer.ToString(), strike));
            buffer.Clear();
        }

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '~')
            {
                int runLength = 1;
                while (i + runLength < source.Length && source[i + runLength] == '~')
                    runLength++;

                Flush();
                strike = !strike;
                i += runLength - 1;
                continue;
            }

            buffer.Append(source[i]);
        }

        Flush();

        if (result.Count == 0)
            result.Add(new SplashTextSegment(source, false));

        return result;
    }

    private void DrawHomeSplashText(ImDrawListPtr drawList, Vector2 start, uint textColor)
    {
        if (_homeSplashSegments.Count == 0)
        {
            drawList.AddText(start, textColor, _homeSplashPlainText);
            return;
        }

        Vector2 cursor = start;
        foreach (SplashTextSegment segment in _homeSplashSegments)
        {
            if (string.IsNullOrEmpty(segment.Text))
                continue;

            drawList.AddText(cursor, textColor, segment.Text);

            Vector2 size = ImGui.CalcTextSize(segment.Text);
            if (segment.Strikethrough)
            {
                float y = cursor.Y + size.Y * 0.52f;
                drawList.AddLine(
                    new Vector2(cursor.X, y),
                    new Vector2(cursor.X + size.X, y),
                    textColor,
                    1.6f);
            }

            cursor.X += size.X;
        }
    }

    private unsafe void SetupDefaultDockSpace(uint dockspaceId, Vector2 size)
    {
        ImGuiP.DockBuilderRemoveNode(dockspaceId);
        ImGuiP.DockBuilderAddNode(dockspaceId, ImGuiDockNodeFlags.None);
        ImGuiP.DockBuilderSetNodeSize(dockspaceId, size);

        uint rightId = 0;
        uint leftId = ImGuiP.DockBuilderSplitNode(dockspaceId, ImGuiDir.Left, 0.70f, null, &rightId);

        uint timelineDockId = 0;
        uint viewportDockId = ImGuiP.DockBuilderSplitNode(leftId, ImGuiDir.Up, 0.75f, null, &timelineDockId);

        uint propertiesDockId = 0;
        uint sceneTreeDockId = ImGuiP.DockBuilderSplitNode(rightId, ImGuiDir.Up, 0.30f, null, &propertiesDockId);

        ImGuiP.DockBuilderDockWindow(ViewportDockId, viewportDockId);
        ImGuiP.DockBuilderDockWindow(TimelineDockId, timelineDockId);
        ImGuiP.DockBuilderDockWindow(ContentBrowserDockId, timelineDockId);
        ImGuiP.DockBuilderDockWindow(SceneTreeDockId, sceneTreeDockId);
        ImGuiP.DockBuilderDockWindow(PropertiesDockId, propertiesDockId);

        ImGuiP.DockBuilderFinish(dockspaceId);
    }

    private void RequestApplicationExit()
    {
        CheckAndHandleUnsavedChanges();
    }

    public bool CanWindowClose()
    {
        // If we've already determined the window can close, allow it
        if (_allowWindowClose)
            return true;

        // If there's no project or no unsaved changes, allow close
        if (!_projectManager.HasProject || !_projectManager.IsDirty)
            return true;

        // If we're not already handling the close dialog, start handling it
        if (!_handleCloseWithUnsavedChanges)
        {
            _handleCloseWithUnsavedChanges = true;
            _showUnsavedChangesDialog = true;
        }

        // Don't close the window yet - we're showing the dialog
        return false;
    }

    private unsafe void CheckAndHandleUnsavedChanges()
    {
        if (!_projectManager.HasProject || !_projectManager.IsDirty)
        {
            // No unsaved changes, save preferences and proceed with close
            _preferencesPanel?.SavePreferences();
            Glfw.SetWindowShouldClose(WindowHandle, true);
            return;
        }

        _handleCloseWithUnsavedChanges = true;
        _showUnsavedChangesDialog = true;
    }

    private unsafe void RenderUnsavedChangesDialog()
    {
        if (!_showUnsavedChangesDialog)
            return;

        if (!ImGui.IsPopupOpen("Unsaved Changes"))
            ImGui.OpenPopup("Unsaved Changes");

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        Vector2 center = new Vector2(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Unsaved Changes", ref _showUnsavedChangesDialog, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped($"The project \"{_projectManager.Manifest.ProjectName}\" has unsaved changes.");
            ImGui.TextWrapped("What would you like to do?");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            bool shouldSave = false;
            bool shouldExit = false;

            float buttonWidth = 150f;
            float totalButtonWidth = (buttonWidth * 3) + (ImGui.GetStyle().ItemSpacing.X * 2);
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float offset = (availableWidth - totalButtonWidth) / 2;

            if (offset > 0)
                ImGui.Indent(offset);

            if (ImGui.Button("Save", new Vector2(buttonWidth, 0)))
            {
                shouldSave = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("Exit without Saving", new Vector2(buttonWidth, 0)))
            {
                shouldExit = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                ImGui.CloseCurrentPopup();
                _showUnsavedChangesDialog = false;
                _handleCloseWithUnsavedChanges = false;
                _allowWindowClose = false;
                Glfw.SetWindowShouldClose(WindowHandle, false);
            }

            if (offset > 0)
                ImGui.Unindent(offset);

            if (shouldSave)
            {
                SaveProjectWithSceneInternal();
                ImGui.CloseCurrentPopup();
                _showUnsavedChangesDialog = false;
                _handleCloseWithUnsavedChanges = false;
                _preferencesPanel?.SavePreferences();
                Glfw.SetWindowShouldClose(WindowHandle, true);
            }
            else if (shouldExit)
            {
                ImGui.CloseCurrentPopup();
                _showUnsavedChangesDialog = false;
                _handleCloseWithUnsavedChanges = false;
                _allowWindowClose = true;
                _preferencesPanel?.SavePreferences();
                Glfw.SetWindowShouldClose(WindowHandle, true);
            }

            ImGui.EndPopup();
        }
    }
}
