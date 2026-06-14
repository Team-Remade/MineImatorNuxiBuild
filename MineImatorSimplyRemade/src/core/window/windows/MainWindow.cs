using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using StbImageSharp;

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
        foreach (var panel in _panels)
        {
            panel.Gl = gl;
            
            if (panel is Viewport viewport)
            {
                viewport.InitFramebuffer(1, 1);
                //test
                viewport.SceneObjects.Add(new SceneObject());
                viewport.SceneObjects[0].AddMesh(new Mesh(gl));
            }
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