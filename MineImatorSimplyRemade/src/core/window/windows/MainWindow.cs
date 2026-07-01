using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using NativeFileDialogSharp;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using StbImageSharp;
using MineImatorSimplyRemade;

namespace MineImatorSimplyRemade.core.window.windows;

public class MainWindow : Window
{
    public static Random Rnd = new Random();

    private static readonly string ImGuiIniPath = "imgui.ini";
    
    /// <summary>Returns the camera viewport panel for use by <c>main.cs</c>.</summary>
    public CameraViewport? GetCameraViewport() => _cameraViewport;

    public const string ViewportDockId = "Viewport";
    public const string SceneTreeDockId = "Scene Tree";
    public const string PropertiesDockId = "Properties";
    public const string TimelineDockId = "Timeline";
    public const string ContentBrowserDockId = "Content Browser";
    
    private bool _dockSpaceInitialized = false;

    private SpawnMenu?      _spawnMenu;
    private CameraViewport? _cameraViewport;
    private ContentBrowser? _contentBrowser;
    private Viewport? _mainViewport;

    private Menubar menubar;
    private readonly ProjectManager _projectManager = ProjectManager.Instance;

    private bool _openNewProjectPopup;
    private string _newProjectNameBuffer = "Untitled Project";

    private UiPanel[] _panels =
    {
        new Viewport(),
        new Timeline(),
        new ContentBrowser(),
        new SceneTree(),
        new PropertiesPanel()
    };

    public MainWindow(int width, int height, string title, Glfw glfw, GL gl = null) : base(width, height, title, glfw, gl)
    {
        menubar = new Menubar();
        menubar.NewProjectRequested = () => _openNewProjectPopup = true;
        menubar.OpenProjectRequested = OpenProjectFromDialog;
        menubar.SaveProjectRequested = SaveProjectWithScene;
        menubar.SaveProjectAsRequested = () => _openNewProjectPopup = true;
        menubar.ImportAssetRequested = ImportAssetFromDialog;

        ImageResult icon;
        var rng = Rnd.Next(1, 1000);

        if (rng == 777)
        {
            icon = LoadEmbeddedImage("icons.chegg");
        }
        else if (rng < 500)
        {
            icon = LoadEmbeddedImage("icons.Icon");
        }
        else
        {
            var ic = Rnd.Next(0, 1);
            icon = LoadEmbeddedImage(ic == 1 ? "icons.tamari" : "icons.prism");
        }
        
        SetWindowIcon(icon);
    }

    public override void SetGL(GL gl)
    {
        base.SetGL(gl);

        // Must be initialised before any SceneObject calls AssignObjectId().
        SelectionManager.Initialize();

        // Initialise texture atlases (requires an active GL context).
        TerrainAtlas.Initialize(gl);
        ItemsAtlas.Initialize(gl);

        // Initialise block registry (reads blockstates/models from the most recent .nux version).
        BlockRegistry.Initialize();

        // Scan data/**/characters/ folders and register character models.
        CharacterRegistry.Initialize();

        // Initialise the Mine Imator model loader with the GL context.
        MineImatorLoader.Instance.Initialize(gl);

        Viewport?        viewport        = null;
        SceneTree?       sceneTree       = null;
        PropertiesPanel? propertiesPanel = null;
        Timeline?        timeline        = null;

        foreach (var panel in _panels)
        {
            panel.Gl = gl;

            switch (panel)
            {
                case Viewport vp:
                    viewport = vp;
                    _mainViewport = vp;
                    vp.InitFramebuffer(1, 1);

                    // Initialise the textured ground plane after atlases are loaded.
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
                    break;
            }
        }

        // Wire cross-references after all panels are set up.
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

        // Wire GLFW references so the viewport can lock/unlock the cursor.
        if (viewport != null)
        {
            unsafe
            {
                viewport.GlfwApi    = Glfw;
                viewport.GlfwWindow = windowHandle;
            }
        }

        sceneTree?.Initialize();
        propertiesPanel?.Initialize();
        timeline?.Initialize();

        // ── Spawn menu ────────────────────────────────────────────────────────
        // Must be created after the Viewport has been initialised (so the GL
        // context is available for mesh creation) and after the SceneTree is
        // wired (so SceneTree.Refresh() works from within the spawn menu).
        if (viewport != null)
        {
            _spawnMenu = new SpawnMenu
            {
                Gl       = gl,
                Viewport = viewport
            };
            viewport.SpawnMenu = _spawnMenu;

            if (_contentBrowser != null)
                _contentBrowser.SpawnMenu = _spawnMenu;
        }

        // ── Camera viewport ───────────────────────────────────────────────────
        if (viewport != null)
        {
            _cameraViewport = new CameraViewport
            {
                Gl           = gl,
                MainViewport = viewport
            };

            unsafe
            {
                _cameraViewport.GlfwApi    = Glfw;
                _cameraViewport.GlfwWindow = windowHandle;
            }

            _cameraViewport.Init(320, 200);
            viewport.CameraViewport = _cameraViewport;
            // Pop/Dock wiring is done in main.cs where CameraWindow is owned.
        }
    }

    protected override void RenderUi()
    {
        menubar.Render();
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

        foreach (var panel in _panels)
        {
            panel.Render();
        }

        // Render the floating spawn menu (no-op when closed).
        _spawnMenu?.Render();
    }

    private void RenderProjectDialogs()
    {
        if (_openNewProjectPopup)
        {
            _openNewProjectPopup = false;
            ImGui.OpenPopup("New Project");
        }

        bool popupOpen = true;
        if (ImGui.BeginPopupModal("New Project", ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Project name");
            ImGui.SetNextItemWidth(320f);
            ImGui.InputText("##newProjectName", ref _newProjectNameBuffer, 128);

            if (ImGui.Button("Create", new Vector2(120, 0)))
            {
                string name = string.IsNullOrWhiteSpace(_newProjectNameBuffer)
                    ? "Untitled Project"
                    : _newProjectNameBuffer.Trim();

                _projectManager.CreateNewProject(name);

                if (_mainViewport != null && _spawnMenu != null)
                    ProjectSceneSerializer.LoadSceneFromManifest(_projectManager.Manifest, _mainViewport, _spawnMenu);

                SaveProjectWithScene();
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
            LoadProjectScene();
    }

    private void ImportAssetFromDialog()
    {
        if (!_projectManager.HasProject)
        {
            _openNewProjectPopup = true;
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
            _openNewProjectPopup = true;
            return;
        }

        if (_mainViewport == null)
            return;

        ProjectSceneSerializer.WriteSceneToManifest(_projectManager.Manifest, _mainViewport);
        _projectManager.SaveManifest();
    }

    private void LoadProjectScene()
    {
        if (_mainViewport == null || _spawnMenu == null || !_projectManager.HasProject)
            return;

        ProjectSceneSerializer.LoadSceneFromManifest(_projectManager.Manifest, _mainViewport, _spawnMenu);
    }
    
    private unsafe void SetupDefaultDockSpace(uint dockspaceId, Vector2 size)
    {
        ImGuiP.DockBuilderRemoveNode(dockspaceId);
        ImGuiP.DockBuilderAddNode(dockspaceId, ImGuiDockNodeFlags.None);
        ImGuiP.DockBuilderSetNodeSize(dockspaceId, size);

        // Split root into left (viewport+timeline) and right (scene tree+properties)
        uint rightId = 0;
        uint leftId = ImGuiP.DockBuilderSplitNode(dockspaceId, ImGuiDir.Left, 0.70f, null, &rightId);

        // Split left column: top = viewport, bottom = timeline
        uint timelineDockId = 0;
        uint viewportDockId = ImGuiP.DockBuilderSplitNode(leftId, ImGuiDir.Up, 0.75f, null, &timelineDockId);

        // Split right column: top = scene tree, bottom = properties
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