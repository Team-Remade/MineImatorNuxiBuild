using MineImatorSimplyRemade.core.ui.Panels;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.window.windows;

public class MainWindow : Window
{
    private Menubar menubar;
    
    public MainWindow(int width, int height, string title, Glfw glfw, GL gl = null) : base(width, height, title, glfw, gl)
    {
        menubar = new Menubar();
    }

    protected override void RenderUi()
    {
        menubar.Render();
    }
}