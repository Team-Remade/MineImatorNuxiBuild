using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.ui.Panels;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.window.windows;

public class MainWindow : Window
{
    private Menubar menubar;
    
    // test
    public Mesh triangleMesh;
    
    public MainWindow(int width, int height, string title, Glfw glfw, GL gl = null) : base(width, height, title, glfw, gl)
    {
        menubar = new Menubar();
    }

    protected override void Draw()
    {
        if (triangleMesh != null)
        {
            triangleMesh.Render();
        }
    }

    protected override void RenderUi()
    {
        menubar.Render();
    }
}