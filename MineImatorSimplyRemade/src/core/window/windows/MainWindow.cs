using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using StbImageSharp;
using MineImatorSimplyRemade;

namespace MineImatorSimplyRemade.core.window.windows;

public class MainWindow : Window
{
    public static Random Rnd = new Random();
    
    private static readonly string
        LocalPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    
    private static readonly string ApplicationLocalDirectory = "SimplyRemadeNuxi";
    private static readonly string ImGuiIniPath = "imgui.ini";
    
    public const string ViewportDockId = "Viewport";
    public const string SceneTreeDockId = "Scene Tree";
    public const string PropertiesDockId = "Properties";
    public const string TimelineDockId = "Timeline";
    
    private bool _dockSpaceInitialized = false;

    private SpawnMenu? _spawnMenu;
    
    private Menubar menubar;

    private UiPanel[] _panels =
    {
        new Viewport(),
        new Timeline(),
        new SceneTree(),
        new PropertiesPanel()
    };

    public MainWindow(int width, int height, string title, Glfw glfw, GL gl = null) : base(width, height, title, glfw, gl)
    {
        menubar = new Menubar();

        ImageResult icon;
        var rng = Rnd.Next(1, 1000);

        if (rng == 777)
        {
            icon = LoadEmbeddedImage("chegg");
        }
        else if (rng < 500)
        {
            icon = LoadEmbeddedImage("Icon");
        }
        else
        {
            icon = LoadEmbeddedImage("tamari");
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

        Viewport?        viewport        = null;
        SceneTree?       sceneTree       = null;
        PropertiesPanel? propertiesPanel = null;

        foreach (var panel in _panels)
        {
            panel.Gl = gl;

            switch (panel)
            {
                case Viewport vp:
                    viewport = vp;
                    vp.InitFramebuffer(1, 1);

                    // Initialise the textured ground plane after atlases are loaded.
                    vp.InitGroundPlane();

                    // Default test object: a unit cube at the origin.
                    var testObj = new SceneObject { Name = "Cube" };
                    testObj.AssignObjectId();
                    testObj.AddMesh(new CubeMesh(gl));
                    vp.SceneObjects.Add(testObj);
                    break;

                case SceneTree st:
                    sceneTree = st;
                    break;

                case PropertiesPanel pp:
                    propertiesPanel = pp;
                    break;
            }
        }

        // Wire cross-references after all panels are set up.
        if (sceneTree != null && viewport != null)
            sceneTree.Viewport = viewport;

        sceneTree?.Initialize();
        propertiesPanel?.Initialize();

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
        }
    }

    protected override void RenderUi()
    {
        menubar.Render();
        
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
        ImGuiP.DockBuilderDockWindow(SceneTreeDockId, sceneTreeDockId);
        ImGuiP.DockBuilderDockWindow(PropertiesDockId, propertiesDockId);

        ImGuiP.DockBuilderFinish(dockspaceId);
    }
}