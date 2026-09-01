using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Dock;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemade.core.window;

namespace MineImatorSimplyRemade.core.window.windows;

/// <summary>
/// Main application window.
///
/// MIGRATION STATUS: this is a partial port. What's ported and working: window shell,
/// menu bar wiring (delegates to the same handler methods the old ImGui version used),
/// app icon, About dialog, window title / unsaved-changes close confirmation, and the
/// toast notification mechanism. What's NOT ported yet (tracked as separate todo items,
/// since each is itself a substantial panel): the dockspace + all dockable panels
/// (Viewport, Scene Tree, Properties, Timeline, Content Browser, Spawn Menu), the
/// project home screen, the Render Output / Update / Resource Pack Import dialogs, and
/// undo/redo scene-snapshot tracking (all of which depend on the not-yet-ported
/// Viewport/Timeline/PropertiesPanel panels for their data).
/// </summary>
public partial class MainWindow : WindowBase
{
    private readonly ProjectManager _projectManager = ProjectManager.Instance;

    /// <summary>App-wide preferences model, persisted to disk. Edited through the
    /// ported <see cref="PreferencesView"/> dialog (see <see cref="OpenPreferencesDialog"/>).</summary>
    private readonly PreferencesPanel _preferences = new();

    private readonly string _appTitle;
    private readonly string _aboutVersion;
    private string _lastAppliedWindowTitle = "";

    /// <summary>The dockspace layout's factory - exposes each panel's dock tool
    /// (e.g. <see cref="AppDockFactory.ViewportTool"/>) so a panel's porting pass can swap
    /// in its real content without rebuilding the layout.</summary>
    public AppDockFactory DockFactory { get; private set; } = null!;

    private bool _allowWindowClose;
    private bool _closeRequestedWhileDirty;

    private readonly DispatcherTimer _titleRefreshTimer;

    public MainWindow()
    {
        InitializeComponent();

        _appTitle = "Mine Imator Nuxi";
        _aboutVersion = ResolveAppVersion();
        Title = _appTitle;

        _preferences.LoadPreferences();

        WireMenubar();
        WireDockLayout();
        SetWindowIconFromEmbedded("icons.Icon");

        KeyDown += OnMainWindowKeyDown;
        Closing += OnMainWindowClosing;

        // Ported from RefreshWindowTitle()'s per-frame call in the old RenderUi() -
        // Avalonia has no per-frame hook, so a low-frequency timer takes its place.
        _titleRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _titleRefreshTimer.Tick += (_, _) => RefreshWindowTitle();
        _titleRefreshTimer.Start();

        RefreshWindowTitle();
    }

    private void WireMenubar()
    {
        MenubarControl.NewProjectRequested = OpenNewProjectPopup;
        MenubarControl.OpenProjectRequested = OpenProjectFromDialog;
        MenubarControl.OpenRecentRequested = () => { /* TODO(migration): project home screen */ };
        MenubarControl.SaveProjectRequested = SaveProjectWithScene;
        MenubarControl.SaveProjectAsRequested = OpenSaveAsPopup;
        MenubarControl.UndoRequested = PerformUndo;
        MenubarControl.RedoRequested = PerformRedo;
        MenubarControl.DuplicateRequested = () => DockFactory?.SceneTreeModel.DuplicateSelectedObjects();
        MenubarControl.DeleteRequested = () => DockFactory?.SceneTreeModel.DeleteSelectedObjects();
        MenubarControl.ImportAssetRequested = () => { /* TODO(migration): asset import dialog */ };
        MenubarControl.ImportResourcePackRequested = () => { /* TODO(migration): resource pack import */ };
        MenubarControl.ImportResourcePackFolderRequested = () => { /* TODO(migration): resource pack import */ };
        MenubarControl.ResetLayoutRequested = ResetDockLayout;
        MenubarControl.ResetWorkCameraRequested = () => { /* TODO(migration): Viewport */ };
        MenubarControl.HomeScreenRequested = () => { /* TODO(migration): project home screen */ };
        MenubarControl.AboutRequested = OpenAboutDialog;
        MenubarControl.CheckForUpdatesRequested = () => { /* TODO(migration): update dialog */ };
        MenubarControl.ReportBugsRequested = OpenIssuesLink;
        MenubarControl.VisitForumsRequested = OpenForumsLink;
        MenubarControl.SupportUsRequested = OpenDonateLink;
        MenubarControl.RenderRequested = _ => { /* TODO(migration): render output dialog */ };
        MenubarControl.PreferencesRequested = OpenPreferencesDialog;
        MenubarControl.ExitRequested = () => Close();
    }

    // ── Dockspace layout ─────────────────────────────────────────────────────
    // Ported from SetupDefaultDockSpace()/RequestDockSpaceRebuild() (see
    // AppDockFactory's doc comment for the exact old ImGuiP.DockBuilder* split
    // this reproduces). Unlike the old version, there's no "only rebuild if no
    // imgui.ini exists yet" check - Dock.Avalonia's own layout persistence
    // (if/when wired up) would be a separate, explicit save/load pair rather
    // than an implicit ini file, so every launch currently starts from this
    // default layout.

    private void WireDockLayout()
    {
        DockFactory = new AppDockFactory();
        IRootDock layout = DockFactory.CreateLayout();
        DockFactory.InitLayout(layout);

        MainDockControl.Factory = DockFactory;
        MainDockControl.Layout = layout;

        // Wire the Scene Tree model to the global selection state now that the
        // layout (and thus the model) exists. Safe to call repeatedly on a
        // layout reset - Initialize() only subscribes when SelectionManager is
        // present.
        DockFactory.SceneTreeModel.Initialize();
    }

    private void ResetDockLayout() => WireDockLayout();

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

        Title = title;
        _lastAppliedWindowTitle = title;
    }

    // ── Toast notifications ─────────────────────────────────────────────────
    // Ported from RenderToast(): a short-lived success/error banner. Previously
    // drawn every frame from a fields-based timer check; now a DispatcherTimer
    // just hides the (already-visible) banner after a delay.

    public void ShowSuccessToast(string message) => ShowToast(message, Brushes.LightGreen);
    public void ShowErrorToast(string message) => ShowToast(message, Brushes.IndianRed);

    private void ShowToast(string message, IBrush color)
    {
        ToastText.Text = message;
        ToastText.Foreground = color;
        ToastBorder.IsVisible = true;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            ToastBorder.IsVisible = false;
            timer.Stop();
        };
        timer.Start();
    }

    // ── About dialog ─────────────────────────────────────────────────────────

    private async void OpenAboutDialog()
    {
        var dialog = new Window
        {
            Title = "About Mine Imator Nuxi Build",
            Width = 760,
            Height = 460,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var closeButton = new Button { Content = "Close", Width = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        closeButton.Click += (_, _) => dialog.Close();

        var donateButton = new Button { Content = "Donate (Ko-fi)", Width = 150 };
        donateButton.Click += (_, _) => OpenDonateLink();

        var discordButton = new Button { Content = "Join Discord", Width = 150 };
        discordButton.Click += (_, _) => OpenDiscordLink();

        var creditsScroll = new ScrollViewer
        {
            Height = 300,
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text =
                    "Mine Imator: David Andrei\n" +
                    "Mine Imator Development: David, Nimi, Marvin, Mbanders\n" +
                    "Mine Imator Beta Testing: 9redwoods, AnxiousCynic, Hozq, Jossamations, Rollo, SoundsDotZip, UpgradedMoon, _Mine_, Randi(11x)Stress, Alpha Toostrr, Cade [CaZaKoJa], Jnick, KeepOnChucking, SKIBBZ, Swooplezz, Vash, Nirwandra, Azaron\n" +
                    "Mine Imator Branding: Voxy\n\n" +
                    "Nuxi Project Management: frosty boi, AshFX\n" +
                    "Nuxi Development: frosty boi, Zandar, & Github Contributors\n" +
                    "Nuxi Beta Testing: AshFX, Pikan, Evelyn, Lolin\n" +
                    "Nuxi Branding: AshFX"
            }
        };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Mine Imator Nuxi Build", FontSize = 18, FontWeight = FontWeight.Bold },
                new TextBlock { Text = $"Version {_aboutVersion}", Foreground = Brushes.Gray },
                new Separator(),
                new TextBlock { Text = "Credits" },
                creditsScroll,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children = { donateButton, discordButton, closeButton }
                }
            }
        };

        await dialog.ShowDialog(this);
    }

    // ── Preferences dialog ─────────────────────────────────────────────────────
    // Ported from PreferencesPanel.Render(): the old ImGui version drew the
    // settings as a dockable/floating window. Avalonia gets a proper modal dialog
    // hosting the ported PreferencesView, bound to the shared _preferences model.

    private async void OpenPreferencesDialog()
    {
        var view = new PreferencesView();
        view.Bind(_preferences);

        var dialog = new Window
        {
            Title = "Preferences",
            Width = 520,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = view,
        };

        await dialog.ShowDialog(this);
    }

    // ── External links ───────────────────────────────────────────────────────

    private static void OpenLink(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch failures and keep the editor responsive.
        }
    }

    private static void OpenDonateLink() => OpenLink("https://ko-fi.com/forestw");
    private static void OpenDiscordLink() => OpenLink("https://discord.gg/eswvppFuAD");
    private static void OpenForumsLink() => OpenLink("https://www.mineimatorforums.com/");
    private static void OpenIssuesLink() => OpenLink("https://github.com/Team-Remade/MineImatorNuxiBuild/issues");

    // ── Project actions (kept intact from the old MainWindow - these are
    //    NativeFileDialogSharp/ProjectManager driven and have no ImGui dependency) ──

    private void OpenNewProjectPopup()
    {
        // TODO(migration): port the new-project name-entry modal.
    }

    private void OpenSaveAsPopup()
    {
        // TODO(migration): port the save-as name-entry modal.
    }

    private void OpenProjectFromDialog()
    {
        var result = NativeFileDialogSharp.Dialog.FileOpen("nxProj");
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return;

        if (_projectManager.LoadProject(result.Path))
            RefreshWindowTitle();
    }

    private void SaveProjectWithScene()
    {
        // TODO(migration): scene snapshot capture depends on the not-yet-ported
        // Viewport/Timeline; wire this up once they're ported.
    }

    private void PerformUndo()
    {
        // TODO(migration): undo/redo scene-snapshot stack depends on Viewport.
    }

    private void PerformRedo()
    {
        // TODO(migration): undo/redo scene-snapshot stack depends on Viewport.
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────────
    // Ported from HandleKeyboardShortcuts(): the old version polled
    // ImGui.IsKeyPressed(...) every frame; Avalonia is event-driven, so this is a
    // single KeyDown handler instead. F5/F7/F8/Delete/Space shortcuts that depend
    // on Viewport/Timeline are stubbed until those panels are ported.

    private void OnMainWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (!ctrl)
            return;

        switch (e.Key)
        {
            case Key.N:
                OpenNewProjectPopup();
                e.Handled = true;
                break;
            case Key.O:
                OpenProjectFromDialog();
                e.Handled = true;
                break;
            case Key.Z:
                if (shift) PerformRedo(); else PerformUndo();
                e.Handled = true;
                break;
            case Key.Y:
                PerformRedo();
                e.Handled = true;
                break;
            case Key.S:
                if (shift) OpenSaveAsPopup(); else SaveProjectWithScene();
                e.Handled = true;
                break;
        }
    }

    // ── Close confirmation ───────────────────────────────────────────────────
    // Ported from CanWindowClose()/the unsaved-changes modal that used to gate the
    // main.cs GLFW loop's exit condition. Avalonia has a real Closing event with a
    // cancellable args object, which is a much better fit than polling a bool every
    // frame.

    private async void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowWindowClose || !_projectManager.HasProject || !_projectManager.IsDirty)
            return;

        e.Cancel = true;

        if (_closeRequestedWhileDirty)
            return; // a confirmation dialog is already open

        _closeRequestedWhileDirty = true;
        try
        {
            bool discard = await ShowUnsavedChangesDialog();
            if (discard)
            {
                _allowWindowClose = true;
                Close();
            }
        }
        finally
        {
            _closeRequestedWhileDirty = false;
        }
    }

    private async System.Threading.Tasks.Task<bool> ShowUnsavedChangesDialog()
    {
        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 420,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        bool discard = false;

        var saveButton = new Button { Content = "Save", Width = 100 };
        var discardButton = new Button { Content = "Discard", Width = 100 };
        var cancelButton = new Button { Content = "Cancel", Width = 100 };

        saveButton.Click += (_, _) =>
        {
            SaveProjectWithScene();
            discard = true;
            dialog.Close();
        };
        discardButton.Click += (_, _) =>
        {
            discard = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            discard = false;
            dialog.Close();
        };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "This project has unsaved changes. Save before closing?", TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { saveButton, discardButton, cancelButton }
                }
            }
        };

        await dialog.ShowDialog(this);
        return discard;
    }
}
