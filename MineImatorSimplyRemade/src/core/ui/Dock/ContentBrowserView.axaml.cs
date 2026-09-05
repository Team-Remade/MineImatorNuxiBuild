using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using MineImatorSimplyRemade.core.project;
using NativeFileDialogSharp;

namespace MineImatorSimplyRemade.core.ui.Dock;

/// <summary>
/// Avalonia port of <c>core.ui.Panels.ContentBrowser</c> (the old ImGui panel).
/// Lists the current project's imported assets (from <see cref="ProjectManager"/>)
/// with search + type filtering, supports importing new assets via native file
/// dialogs, and removing assets with a confirmation dialog.
///
/// The old panel drove "Spawn selected asset" / "Add sound to timeline" directly
/// through its <c>SpawnMenu</c> and <c>Timeline</c> references. Those panels
/// aren't ported to Avalonia yet, so instead of hard-wiring them this view
/// exposes callback hooks (<see cref="SpawnAssetRequested"/>,
/// <see cref="AddSoundToTimelineRequested"/>, <see cref="ImportResourcePackRequested"/>,
/// <see cref="ImportResourcePackFolderRequested"/>) that whoever owns this view
/// can wire up once those panels land - matching how <see cref="AppDockFactory"/>
/// composes panels without them needing to know about each other.
/// </summary>
public partial class ContentBrowserView : UserControl
{
    /// <summary>Invoked when the user asks to spawn the selected model/schematic
    /// asset. Set by the host once the SpawnMenu is ported.</summary>
    public Action<ProjectAssetEntry>? SpawnAssetRequested { get; set; }

    /// <summary>Invoked when the user asks to add the selected sound asset to the
    /// timeline. Set by the host once the Timeline is ported.</summary>
    public Action<ProjectAssetEntry>? AddSoundToTimelineRequested { get; set; }

    /// <summary>Invoked when the user chooses "Resource Pack (.zip)" from Import.</summary>
    public Action? ImportResourcePackRequested { get; set; }

    /// <summary>Invoked when the user chooses "Resource Pack Folder" from Import.</summary>
    public Action? ImportResourcePackFolderRequested { get; set; }

    private static readonly ProjectAssetType[] FilterableAssetTypes =
    {
        ProjectAssetType.Unknown,
        ProjectAssetType.Model,
        ProjectAssetType.Image,
        ProjectAssetType.Sound,
        ProjectAssetType.Other
    };

    // Underlying asset entry for each currently-visible row, index-aligned with
    // AssetList.Items so the selected index maps back to the real asset.
    private readonly List<ProjectAssetEntry> _visibleAssets = new();

    public ContentBrowserView()
    {
        InitializeComponent();

        ImportModelItem.Click += (_, _) => ImportAsset(ProjectAssetType.Model, "glb,gltf,fbx,obj,dae,3ds,blend,ply,stl,x3d,mimodel,miobject");
        ImportImageItem.Click += (_, _) => ImportAsset(ProjectAssetType.Image, "png,jpg,jpeg,bmp,tga,gif,webp,tiff");
        ImportSoundItem.Click += (_, _) => ImportAsset(ProjectAssetType.Sound, "wav,mp3,ogg,flac,m4a");
        ImportResourcePackItem.Click += (_, _) => ImportResourcePackRequested?.Invoke();
        ImportResourcePackFolderItem.Click += (_, _) => ImportResourcePackFolderRequested?.Invoke();

        SearchBox.TextChanged += (_, _) => RefreshAssetList();
        TypeFilterCombo.SelectionChanged += (_, _) => RefreshAssetList();
        AssetList.SelectionChanged += (_, _) => UpdateActionButtons();
        AssetList.DoubleTapped += (_, _) => SpawnSelectedAsset();

        SpawnButton.Click += (_, _) => SpawnSelectedAsset();
        AddToTimelineButton.Click += (_, _) => AddSelectedAssetToTimeline();
        RemoveAssetItem.Click += (_, _) =>
        {
            if (SelectedAsset is { } asset)
                RemoveAssetInteractive(asset);
        };

        AttachedToVisualTree += (_, _) => Refresh();
    }

    /// <summary>Rebuilds the header and asset list from the current project state.</summary>
    public void Refresh()
    {
        var projectManager = ProjectManager.Instance;
        if (!projectManager.HasProject)
        {
            ProjectNameText.Text = "No project is currently loaded.";
            ProjectFolderText.Text = "";
            _visibleAssets.Clear();
            AssetList.ItemsSource = null;
            EmptyLabel.IsVisible = true;
            UpdateActionButtons();
            return;
        }

        ProjectNameText.Text = projectManager.Manifest.ProjectName;
        ProjectFolderText.Text = $"({projectManager.ProjectFolder})";
        RefreshAssetList();
    }

    private ProjectAssetType? SelectedTypeFilter =>
        TypeFilterCombo.SelectedIndex <= 0
            ? null
            : FilterableAssetTypes[TypeFilterCombo.SelectedIndex - 1];

    private void RefreshAssetList()
    {
        var projectManager = ProjectManager.Instance;
        var previouslySelected = SelectedAsset;

        _visibleAssets.Clear();
        var rows = new List<string>();

        if (projectManager.HasProject)
        {
            string search = SearchBox.Text?.Trim() ?? "";
            ProjectAssetType? typeFilter = SelectedTypeFilter;

            foreach (var asset in projectManager.GetProjectAssets())
            {
                if (typeFilter != null && asset.AssetType != typeFilter)
                    continue;
                if (!string.IsNullOrWhiteSpace(search) &&
                    !asset.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
                    continue;

                string state = asset.StoredInProject ? "project" : "data";
                rows.Add($"[{asset.AssetType}] {asset.DisplayName} ({state})");
                _visibleAssets.Add(asset);
            }
        }

        AssetList.ItemsSource = rows;
        EmptyLabel.IsVisible = rows.Count == 0;

        // Preserve selection across the rebuild when the asset still shows.
        if (previouslySelected != null)
        {
            int idx = _visibleAssets.IndexOf(previouslySelected);
            if (idx >= 0)
                AssetList.SelectedIndex = idx;
        }

        UpdateActionButtons();
    }

    private ProjectAssetEntry? SelectedAsset
    {
        get
        {
            int idx = AssetList.SelectedIndex;
            return idx >= 0 && idx < _visibleAssets.Count ? _visibleAssets[idx] : null;
        }
    }

    private void UpdateActionButtons()
    {
        SpawnButton.IsEnabled = CanSpawnSelectedAsset();
        bool canAddAudio = CanAddSelectedAssetToTimeline();
        AddToTimelineButton.IsVisible = canAddAudio;
        AddToTimelineButton.IsEnabled = canAddAudio;
    }

    private bool CanSpawnSelectedAsset()
    {
        var selected = SelectedAsset;
        if (selected == null || SpawnAssetRequested == null)
            return false;

        string fullPath = ProjectManager.Instance.GetAssetFullPath(selected);
        if (!File.Exists(fullPath))
            return false;

        if (selected.AssetType == ProjectAssetType.Model)
            return true;

        string ext = Path.GetExtension(fullPath).ToLowerInvariant();
        return ext is ".schematic" or ".schem";
    }

    private bool CanAddSelectedAssetToTimeline()
    {
        var selected = SelectedAsset;
        if (selected == null || AddSoundToTimelineRequested == null)
            return false;
        return selected.AssetType == ProjectAssetType.Sound
            && File.Exists(ProjectManager.Instance.GetAssetFullPath(selected));
    }

    private void SpawnSelectedAsset()
    {
        if (!CanSpawnSelectedAsset())
            return;
        SpawnAssetRequested?.Invoke(SelectedAsset!);
    }

    private void AddSelectedAssetToTimeline()
    {
        if (!CanAddSelectedAssetToTimeline())
            return;
        AddSoundToTimelineRequested?.Invoke(SelectedAsset!);
    }

    private void ImportAsset(ProjectAssetType assetType, string filter)
    {
        var result = Dialog.FileOpen(filter);
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return;

        var entry = ProjectManager.Instance.AddAsset(result.Path, assetType);
        RefreshAssetList();

        int idx = _visibleAssets.IndexOf(entry);
        if (idx >= 0)
            AssetList.SelectedIndex = idx;
    }

    /// <summary>Prompts to remove <paramref name="asset"/> (project asset =&gt;
    /// also deleted from disk, data asset =&gt; only unlinked) then rebuilds the
    /// list. Public so a future context-menu hookup can reuse it.</summary>
    public async void RemoveAssetInteractive(ProjectAssetEntry asset)
    {
        if (asset == null)
            return;

        string message = asset.StoredInProject
            ? $"Remove '{asset.DisplayName}' from the project and delete it from disk?"
            : $"Remove '{asset.DisplayName}' from the project? This is a built-in data asset, so its file will not be deleted from disk.";

        bool confirmed = await ConfirmAsync("Remove asset", message);
        if (!confirmed)
            return;

        ProjectManager.Instance.RemoveAsset(asset);
        RefreshAssetList();
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;

        var result = false;
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var removeButton = new Button { Content = "Remove", MinWidth = 100 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 100 };
        removeButton.Click += (_, _) => { result = true; dialog.Close(); };
        cancelButton.Click += (_, _) => { result = false; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { removeButton, cancelButton },
                },
            },
        };

        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return result;
    }
}
