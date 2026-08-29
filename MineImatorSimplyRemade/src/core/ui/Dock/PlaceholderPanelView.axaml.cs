using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MineImatorSimplyRemade.core.ui.Dock;

/// <summary>
/// Stand-in content for a dockable panel that hasn't been ported to Avalonia
/// yet. Each panel's own porting pass replaces the corresponding
/// <see cref="AppDockFactory"/> tool's <c>Content</c> with the real ported
/// view - this just keeps the dockspace layout itself fully real/functional
/// (resizable, tabbable, closable) in the meantime.
/// </summary>
public partial class PlaceholderPanelView : UserControl
{
    public PlaceholderPanelView() : this("This panel")
    {
    }

    public PlaceholderPanelView(string panelName)
    {
        InitializeComponent();
        MessageText.Text = $"{panelName} not yet ported to Avalonia - see the migration plan.";
    }
}
