using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using NativeFileDialogSharp;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.window.windows;

public class MainWindow : Window
{
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

    public static Random Rnd = new Random();

    private static readonly string ImGuiIniPath = "imgui.ini";

    public const string ViewportDockId = "Viewport";
    public const string SceneTreeDockId = "Scene Tree";
    public const string PropertiesDockId = "Properties";
    public const string TimelineDockId = "Timeline";
    public const string ContentBrowserDockId = "Content Browser";

    private bool _dockSpaceInitialized;
    private SpawnMenu? _spawnMenu;
    private CameraViewport? _cameraViewport;
    private ContentBrowser? _contentBrowser;
    private Viewport? _mainViewport;
    private Timeline? _timeline;

    private readonly Menubar _menubar;
    private readonly ProjectManager _projectManager = ProjectManager.Instance;
    private readonly Dictionary<string, uint> _thumbnailTextures = new(StringComparer.OrdinalIgnoreCase);

    private bool _openProjectDialogPopup;
    private bool _showProjectHome = true;
    private string _newProjectNameBuffer = "Untitled Project";
    private ProjectDialogMode _projectDialogMode = ProjectDialogMode.NewProject;

    private string _toastMessage = "";
    private ToastKind _toastKind = ToastKind.Success;
    private double _toastExpiresAtSeconds;
    private bool _showSavingToast;

    private PendingSaveAction _pendingSaveAction;
    private bool _pendingSavePrimed;

    private readonly string _appTitle;
    private string _lastAppliedWindowTitle = "";
    private string _savedSceneFingerprint = "";
    private double _nextDirtyCheckAtSeconds;
    private const double DirtyCheckIntervalSeconds = 0.35;

    private readonly UiPanel[] _panels =
    [
        new Viewport(),
        new Timeline(),
        new ContentBrowser(),
        new SceneTree(),
        new PropertiesPanel()
    ];

    public MainWindow(int width, int height, string title, Glfw glfw, GL? gl = null) : base(width, height, title, glfw, gl!)
    {
        _appTitle = title;
        _menubar = new Menubar();
        _menubar.NewProjectRequested = OpenNewProjectPopup;
        _menubar.OpenProjectRequested = OpenProjectFromDialog;
        _menubar.OpenRecentRequested = () => _showProjectHome = true;
        _menubar.SaveProjectRequested = SaveProjectWithScene;
        _menubar.SaveProjectAsRequested = OpenSaveAsPopup;
        _menubar.ImportAssetRequested = ImportAssetFromDialog;
        _menubar.HomeScreenRequested = () => _showProjectHome = true;

        ImageResult icon;
        int rng = Rnd.Next(1, 1000);
        if (rng == 777)
            icon = LoadEmbeddedImage("icons.chegg");
        else if (rng < 500)
            icon = LoadEmbeddedImage("icons.Icon");
        else
            icon = LoadEmbeddedImage(Rnd.Next(0, 1) == 1 ? "icons.tamari" : "icons.prism");

        SetWindowIcon(icon);
        RefreshWindowTitle();
    }

    public CameraViewport? GetCameraViewport() => _cameraViewport;

    public override void SetGL(GL gl)
    {
        base.SetGL(gl);

        SelectionManager.Initialize();
        TerrainAtlas.Initialize(gl);
        ItemsAtlas.Initialize(gl);
        BlockRegistry.Initialize();
        CharacterRegistry.Initialize();
        MineImatorLoader.Instance.Initialize(gl);

        Viewport? viewport = null;
        SceneTree? sceneTree = null;
        PropertiesPanel? propertiesPanel = null;
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
                    break;
                case SceneTree st:
                    sceneTree = st;
                    break;
                case PropertiesPanel pp:
                    propertiesPanel = pp;
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
        if (timeline != null && viewport != null)
            timeline.Viewport = viewport;
        if (timeline != null && propertiesPanel != null)
            propertiesPanel.Timeline = timeline;
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

        if (viewport != null)
        {
            _spawnMenu = new SpawnMenu
            {
                Gl = gl,
                Viewport = viewport
            };
            viewport.SpawnMenu = _spawnMenu;
            if (_contentBrowser != null)
                _contentBrowser.SpawnMenu = _spawnMenu;
        }

        if (viewport != null)
        {
            _cameraViewport = new CameraViewport
            {
                Gl = gl,
                MainViewport = viewport
            };
            unsafe
            {
                _cameraViewport.GlfwApi = Glfw;
                _cameraViewport.GlfwWindow = windowHandle;
            }
            _cameraViewport.Init(320, 200);
            viewport.CameraViewport = _cameraViewport;
        }
    }

    protected override void RenderUi()
    {
        if (_mainViewport != null)
            _mainViewport.SuppressInlineCameraViewport = _showProjectHome;

        HandleKeyboardShortcuts();
        ProcessPendingSaveAction();
        UpdateDirtyStateFromScene();
        RefreshWindowTitle();

        _menubar.Render();
        RenderProjectDialogs();

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

        if (!_dockSpaceInitialized && !File.Exists(ImGuiIniPath))
        {
            SetupDefaultDockSpace(dockspaceId, mainViewport.WorkSize);
            _dockSpaceInitialized = true;
        }

        ImGui.End();

        foreach (UiPanel panel in _panels)
            panel.Render();

        _spawnMenu?.Render();
        RenderProjectHomeScreen();
        RenderToast();
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

        var manifestSnapshot = new ProjectManifest
        {
            ProjectName = _projectManager.Manifest.ProjectName,
            CreatedUtc = "",
            LastSavedUtc = "",
            Assets = new List<ProjectAssetEntry>(_projectManager.Manifest.Assets)
        };

        ProjectSceneSerializer.WriteSceneToManifest(manifestSnapshot, _mainViewport, _timeline);

        string snapshotJson = JsonSerializer.Serialize(manifestSnapshot, AppJsonContext.Default.ProjectManifest);

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson));
        return Convert.ToHexString(hashBytes);
    }

    private void RefreshWindowTitle()
    {
        string title;
        if (_projectManager.HasProject)
        {
            string state = _projectManager.IsDirty ? "Unsaved" : "Saved";
            title = $"{_appTitle} - {_projectManager.Manifest.ProjectName} [{state}]";
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
        if (!io.KeyCtrl || io.WantTextInput)
            return;

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

        drawList.AddRectFilled(splashMin, splashMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0.16f, 0.18f, 0.22f, 1f)), 18f);
        drawList.AddRect(splashMin, splashMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0.34f, 0.40f, 0.48f, 1f)), 18f, ImDrawFlags.RoundCornersAll, 2f);
        drawList.AddText(splashMin + new Vector2(24f, 24f), ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.95f, 0.98f, 1f)), "Splash Screen Placeholder");
        drawList.AddText(splashMin + new Vector2(24f, 56f), ImGui.ColorConvertFloat4ToU32(new Vector4(0.70f, 0.75f, 0.82f, 1f)), "Add title artwork here later.");

        ImGui.Dummy(splashSize);
        ImGui.TextDisabled("Splash art credits: (unassigned)");
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
            ProjectSceneSerializer.WriteSceneToManifest(_projectManager.Manifest, _mainViewport, _timeline);
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

        ProjectSceneSerializer.LoadSceneFromManifest(_projectManager.Manifest, _mainViewport, _spawnMenu, _timeline);
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

                ProjectSceneSerializer.WriteSceneToManifest(_projectManager.Manifest, _mainViewport, _timeline);
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
                ProjectSceneSerializer.LoadSceneFromManifest(_projectManager.Manifest, _mainViewport, _spawnMenu, _timeline);

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
}
