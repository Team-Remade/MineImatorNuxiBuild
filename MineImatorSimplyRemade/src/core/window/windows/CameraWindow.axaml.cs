using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MineImatorSimplyRemade.core.window;

namespace MineImatorSimplyRemade.core.window.windows;

/// <summary>
/// Standalone (undocked) camera preview window. Ported from the old GLFW window that
/// shared a GL context with the main window purely so its FBO/texture were visible
/// across contexts.
///
/// That whole "second native window sharing a GL context" problem goes away with the
/// CPU-blit render bridge (<see cref="VeldridBitmapRenderSurface"/>): the scene is
/// rendered once (wherever the owning Viewport lives) and handed to this window as a
/// plain <see cref="Bitmap"/> via <see cref="FrameProvider"/>, so there is nothing here
/// that needs its own GPU context at all.
///
/// MIGRATION NOTE: this window is intentionally decoupled from the (not yet ported)
/// <c>Viewport</c> panel - it only knows about a bitmap-producing delegate and a small
/// set of UI events, mirroring the "just displays whatever it's given" role the old
/// CameraWindow.Render() played after Viewport.RenderScenePublic() ran. Once Viewport
/// is ported, wire it up by setting <see cref="FrameProvider"/> to
/// <c>() => viewport.RenderSurface.ReadBack()</c> (or similar) and populating
/// <see cref="AvailableCameras"/> from the scene's camera list.
/// </summary>
public partial class CameraWindow : WindowBase
{
    private readonly DispatcherTimer _renderTimer;
    private bool _suppressSelectionEvent;

    /// <summary>Called ~60 times/second to fetch the latest rendered scene bitmap for
    /// this preview. Owner (whoever ported Viewport) is responsible for actually
    /// driving the Veldrid render + <c>VeldridBitmapRenderSurface.ReadBack()</c> call;
    /// this window just displays the result.</summary>
    public Func<Bitmap?>? FrameProvider { get; set; }

    /// <summary>Raised when the "Dock" button is pressed - the owner should hide this
    /// window and mark the camera viewport as docked/inline again (mirrors the old
    /// <c>camViewport.DockToInlineVisible()</c> + <c>Hide()</c> call in main.cs).</summary>
    public event Action? DockRequested;

    /// <summary>Raised when the Overlays toggle button changes state.</summary>
    public event Action<bool>? OverlaysChanged;

    /// <summary>Raised when the user picks a different camera from the dropdown, with
    /// the selected index into <see cref="AvailableCameras"/>.</summary>
    public event Action<int>? CameraSelected;

    /// <summary>F5 while this window is focused: toggle high-quality (shadowed) preview.</summary>
    public event Action? HighQualityPreviewToggleRequested;

    /// <summary>Ctrl+F6 while this window is focused: toggle shadow debug mode.</summary>
    public event Action? ShadowDebugToggleRequested;

    public CameraWindow()
    {
        InitializeComponent();

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0) };
        _renderTimer.Tick += (_, _) => PumpFrame();

        Opened += (_, _) => _renderTimer.Start();
        Closed += (_, _) => _renderTimer.Stop();

        // Ported from CameraWindow.HandleCameraWindowKeyboardShortcuts(): F5/F6 need to
        // be handled by whichever window currently has focus, not just the main window,
        // so the undocked preview window gets its own KeyDown handler for them.
        KeyDown += OnCameraWindowKeyDown;
    }

    private void OnCameraWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            HighQualityPreviewToggleRequested?.Invoke();
            e.Handled = true;
        }
        else if (e.Key == Key.F6 && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ShadowDebugToggleRequested?.Invoke();
            e.Handled = true;
        }
    }

    /// <summary>Sets the camera names shown in the dropdown without firing
    /// <see cref="CameraSelected"/>.</summary>
    public void SetAvailableCameras(IReadOnlyList<string> cameraNames, int selectedIndex)
    {
        _suppressSelectionEvent = true;
        try
        {
            CameraDropdown.ItemsSource = cameraNames;
            CameraDropdown.SelectedIndex = selectedIndex >= 0 && selectedIndex < cameraNames.Count ? selectedIndex : -1;
        }
        finally
        {
            _suppressSelectionEvent = false;
        }
    }

    public bool OverlaysEnabled
    {
        get => OverlaysToggle.IsChecked == true;
        set => OverlaysToggle.IsChecked = value;
    }

    private void PumpFrame()
    {
        Bitmap? frame = FrameProvider?.Invoke();
        if (frame != null)
            SceneImage.Source = frame;
    }

    private void OnDockClicked(object? sender, RoutedEventArgs e) => DockRequested?.Invoke();

    private void OnOverlaysToggled(object? sender, RoutedEventArgs e) =>
        OverlaysChanged?.Invoke(OverlaysToggle.IsChecked == true);

    private void OnCameraSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvent)
            return;

        CameraSelected?.Invoke(CameraDropdown.SelectedIndex);
    }
}
