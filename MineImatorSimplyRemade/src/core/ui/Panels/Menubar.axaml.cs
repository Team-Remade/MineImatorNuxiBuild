using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// Top application menu bar. Ported from the old ImGui immediate-mode
/// <c>ImGui.BeginMainMenuBar()</c> version to a retained-mode Avalonia
/// &lt;Menu&gt;. The public delegate surface (<see cref="NewProjectRequested"/>
/// etc.) is unchanged so <c>MainWindow</c>'s wiring of this panel keeps
/// working verbatim.
/// </summary>
public partial class Menubar : UserControl
{
    public enum RenderRequestKind
    {
        Image,
        Video
    }

    public Action? NewProjectRequested { get; set; }
    public Action? OpenProjectRequested { get; set; }
    public Action? OpenRecentRequested { get; set; }
    public Action? SaveProjectRequested { get; set; }
    public Action? SaveProjectAsRequested { get; set; }
    public Action? UndoRequested { get; set; }
    public Action? RedoRequested { get; set; }
    public Action? DuplicateRequested { get; set; }
    public Action? DeleteRequested { get; set; }
    public Action? SpawnObjectRequested { get; set; }
    public Action? ImportAssetRequested { get; set; }
    public Action? ImportResourcePackRequested { get; set; }
    public Action? ImportResourcePackFolderRequested { get; set; }
    public Action? ResetWorkCameraRequested { get; set; }
    public Action? ResetLayoutRequested { get; set; }
    public Action? HomeScreenRequested { get; set; }
    public Action? AboutRequested { get; set; }
    public Action? ReportBugsRequested { get; set; }
    public Action? VisitForumsRequested { get; set; }
    public Action? SupportUsRequested { get; set; }
    public Action<RenderRequestKind>? RenderRequested { get; set; }
    public Action? PreferencesRequested { get; set; }
    public Action? CheckForUpdatesRequested { get; set; }
    public Action? ExitRequested { get; set; }

    public Menubar()
    {
        InitializeComponent();
    }

    private void OnNewProject(object? sender, RoutedEventArgs e) => NewProjectRequested?.Invoke();
    private void OnOpenProject(object? sender, RoutedEventArgs e) => OpenProjectRequested?.Invoke();
    private void OnOpenRecent(object? sender, RoutedEventArgs e) => OpenRecentRequested?.Invoke();
    private void OnSaveProject(object? sender, RoutedEventArgs e) => SaveProjectRequested?.Invoke();
    private void OnSaveProjectAs(object? sender, RoutedEventArgs e) => SaveProjectAsRequested?.Invoke();
    private void OnImportAsset(object? sender, RoutedEventArgs e) => ImportAssetRequested?.Invoke();
    private void OnImportResourcePack(object? sender, RoutedEventArgs e) => ImportResourcePackRequested?.Invoke();
    private void OnImportResourcePackFolder(object? sender, RoutedEventArgs e) => ImportResourcePackFolderRequested?.Invoke();
    private void OnExit(object? sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    private void OnUndo(object? sender, RoutedEventArgs e) => UndoRequested?.Invoke();
    private void OnRedo(object? sender, RoutedEventArgs e) => RedoRequested?.Invoke();
    private void OnDuplicate(object? sender, RoutedEventArgs e) => DuplicateRequested?.Invoke();
    private void OnDelete(object? sender, RoutedEventArgs e) => DeleteRequested?.Invoke();
    private void OnSpawnObject(object? sender, RoutedEventArgs e) => SpawnObjectRequested?.Invoke();
    private void OnPreferences(object? sender, RoutedEventArgs e) => PreferencesRequested?.Invoke();

    private void OnRenderImage(object? sender, RoutedEventArgs e) => RenderRequested?.Invoke(RenderRequestKind.Image);
    private void OnRenderAnimation(object? sender, RoutedEventArgs e) => RenderRequested?.Invoke(RenderRequestKind.Video);

    private void OnResetLayout(object? sender, RoutedEventArgs e) => ResetLayoutRequested?.Invoke();
    private void OnResetWorkCamera(object? sender, RoutedEventArgs e) => ResetWorkCameraRequested?.Invoke();
    private void OnHomeScreen(object? sender, RoutedEventArgs e) => HomeScreenRequested?.Invoke();

    private void OnCheckForUpdates(object? sender, RoutedEventArgs e) => CheckForUpdatesRequested?.Invoke();
    private void OnAbout(object? sender, RoutedEventArgs e) => AboutRequested?.Invoke();
    private void OnReportBugs(object? sender, RoutedEventArgs e) => ReportBugsRequested?.Invoke();
    private void OnVisitForums(object? sender, RoutedEventArgs e) => VisitForumsRequested?.Invoke();
    private void OnSupportUs(object? sender, RoutedEventArgs e) => SupportUsRequested?.Invoke();
}
