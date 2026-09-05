using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MineImatorSimplyRemade.core.project;

namespace MineImatorSimplyRemade.core.ui.Dock;

public partial class HomeScreenView : UserControl
{
    private readonly ProjectManager _projectManager = ProjectManager.Instance;

    public Action? NewProjectRequested { get; set; }
    public Action? LoadProjectRequested { get; set; }
    public Action<string>? OpenRecentRequested { get; set; }

    public HomeScreenView()
    {
        InitializeComponent();
        NewProjectButton.Click += (_, _) => NewProjectRequested?.Invoke();
        LoadProjectButton.Click += (_, _) => LoadProjectRequested?.Invoke();
        AttachedToVisualTree += (_, _) => Refresh();
    }

    public void Refresh()
    {
        CurrentProjectName.Text = _projectManager.HasProject ? _projectManager.Manifest.ProjectName : "No project currently open.";
        CurrentProjectPath.Text = _projectManager.HasProject ? _projectManager.ProjectFilePath : "";
        LoadSplash();
        BuildRecentProjects();
    }

    private void LoadSplash()
    {
        string splashRoot = Path.Combine(AppContext.BaseDirectory, "data", "splashes");
        string splashPath = Path.Combine(splashRoot, "splash.png");
        string textPath = Path.Combine(splashRoot, "splash.txt");
        string creditPath = Path.Combine(splashRoot, "credit.txt");
        SplashText.Text = ReadRandomSplash(textPath);
        SplashCredit.Text = File.Exists(creditPath) ? $"Splash art credits: {File.ReadAllText(creditPath).Trim()}" : "";
        SplashImage.Source = File.Exists(splashPath) ? new Bitmap(splashPath) : null;
    }

    private void BuildRecentProjects()
    {
        RecentProjectsPanel.Children.Clear();
        IReadOnlyList<RecentProjectEntry> recents = _projectManager.GetRecentProjects();
        if (recents.Count == 0)
        {
            RecentProjectsPanel.Children.Add(new TextBlock { Text = "No recent projects yet.", Foreground = Brushes.Gray, Margin = new Avalonia.Thickness(0, 8, 0, 0) });
            return;
        }

        foreach (RecentProjectEntry recent in recents)
            RecentProjectsPanel.Children.Add(CreateRecentCard(recent));
    }

    private Control CreateRecentCard(RecentProjectEntry recent)
    {
        bool exists = File.Exists(recent.ProjectFilePath);
        var thumbnail = new Image { Height = 96, Stretch = Stretch.UniformToFill };
        if (!string.IsNullOrWhiteSpace(recent.ThumbnailPath) && File.Exists(recent.ThumbnailPath))
            thumbnail.Source = new Bitmap(recent.ThumbnailPath);

        var openButton = new Button { Content = "Open", IsEnabled = exists, HorizontalAlignment = HorizontalAlignment.Stretch };
        openButton.Click += (_, _) => OpenRecentRequested?.Invoke(recent.ProjectFilePath);
        var removeButton = new Button { Content = "Remove", HorizontalAlignment = HorizontalAlignment.Stretch };
        removeButton.Click += (_, _) => { _projectManager.RemoveRecentProject(recent.ProjectFilePath); Refresh(); };

        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new Border { Height = 96, Background = new SolidColorBrush(Color.Parse("#303942")), CornerRadius = new Avalonia.CornerRadius(4), Child = thumbnail });
        content.Children.Add(new TextBlock { Text = recent.ProjectName, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        content.Children.Add(new TextBlock { Text = Path.GetFileName(recent.ProjectFilePath), Foreground = Brushes.Gray, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!exists) content.Children.Add(new TextBlock { Text = "Missing from disk", Foreground = Brushes.IndianRed, FontSize = 12 });
        content.Children.Add(openButton);
        content.Children.Add(removeButton);
        return new Border
        {
            Width = 220, Height = 250, Margin = new Avalonia.Thickness(0, 0, 14, 14), Padding = new Avalonia.Thickness(10),
            Background = new SolidColorBrush(Color.Parse("#20242A")), BorderBrush = new SolidColorBrush(Color.Parse("#39424C")),
            BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(4), Child = content
        };
    }

    private static string ReadRandomSplash(string path)
    {
        if (!File.Exists(path)) return "Splash Screen Placeholder";
        string[] lines = File.ReadAllLines(path).Select(line => line.Trim().Replace("~", "").Replace("*", "")).Where(line => line.Length > 0).ToArray();
        return lines.Length > 0 ? lines[Random.Shared.Next(lines.Length)] : "Splash Screen Placeholder";
    }
}