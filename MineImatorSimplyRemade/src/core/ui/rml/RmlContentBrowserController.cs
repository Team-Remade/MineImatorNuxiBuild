using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using NativeFileDialogSharp;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Retained-mode controller for the editor's project asset browser.</summary>
public sealed class RmlContentBrowserController
{
    private readonly Element _root;
    private readonly ProjectManager _projects = ProjectManager.Instance;
    private readonly Dictionary<string, ProjectAssetEntry> _assetElements = new();
    private string? _selectedKey;
    private string _lastSignature = string.Empty;

    public SpawnMenu? SpawnMenu { get; set; }
    public Timeline? Timeline { get; set; }
    public Action? ImportResourcePackRequested { get; set; }
    public Action? ImportResourcePackFolderRequested { get; set; }

    public RmlContentBrowserController(Element root)
    {
        _root = root;
        Refresh(force: true);
    }

    public void Update() => Refresh(force: false);

    private void Refresh(bool force)
    {
        IReadOnlyList<ProjectAssetEntry> assets = _projects.HasProject
            ? _projects.GetProjectAssets()
            : Array.Empty<ProjectAssetEntry>();
        string signature = _projects.HasProject
            ? $"{_projects.Manifest.ProjectName}|{string.Join('|', assets.Select(AssetKey))}|{_selectedKey}"
            : "no-project";
        if (!force && signature == _lastSignature) return;
        _lastSignature = signature;
        _assetElements.Clear();

        if (!_projects.HasProject)
        {
            _root.SetInnerRml("<div class='empty'>No project is currently loaded.</div>");
            return;
        }

        var html = new System.Text.StringBuilder();
        html.Append("<div id='asset-tools'><button id='asset-import-model'>Import Model</button>")
            .Append("<button id='asset-import-image'>Import Image</button><button id='asset-import-sound'>Import Sound</button>")
            .Append("<button id='asset-import-pack'>Resource Pack</button></div><div id='asset-list'>");

        for (int i = 0; i < assets.Count; i++)
        {
            ProjectAssetEntry asset = assets[i];
            string id = $"asset-{i}";
            string key = AssetKey(asset);
            _assetElements[id] = asset;
            html.Append("<button id='").Append(id).Append("' class='asset")
                .Append(key == _selectedKey ? " selected" : string.Empty).Append("'><span class='asset-kind'>")
                .Append(Escape(asset.AssetType.ToString())).Append("</span>")
                .Append(Escape(asset.DisplayName)).Append(asset.StoredInProject ? "" : " <small>(built-in)</small>")
                .Append("</button>");
        }
        if (assets.Count == 0) html.Append("<div class='empty'>No assets to display.</div>");
        html.Append("</div><div id='asset-actions'><button id='asset-primary'>")
            .Append(SelectedAsset()?.AssetType == ProjectAssetType.Sound ? "Add Selected Sound to Timeline" : "Spawn Selected Asset")
            .Append("</button><button id='asset-remove'>Remove</button></div>");
        _root.SetInnerRml(html.ToString());

        Bind("asset-import-model", () => Import(ProjectAssetType.Model, "glb,gltf,fbx,obj,dae,3ds,blend,ply,stl,x3d,mimodel,miobject"));
        Bind("asset-import-image", () => Import(ProjectAssetType.Image, "png,jpg,jpeg,bmp,tga,gif,webp,tiff"));
        Bind("asset-import-sound", () => Import(ProjectAssetType.Sound, "wav,mp3,ogg,flac,m4a"));
        Bind("asset-import-pack", () => ImportResourcePackRequested?.Invoke());
        Bind("asset-primary", ActivateSelected);
        Bind("asset-remove", RemoveSelected);
        foreach ((string id, ProjectAssetEntry asset) in _assetElements)
            Bind(id, () => Select(asset));
    }

    private void Select(ProjectAssetEntry asset)
    {
        _selectedKey = AssetKey(asset);
        Refresh(force: true);
    }

    private ProjectAssetEntry? SelectedAsset() => _selectedKey == null
        ? null
        : _projects.GetProjectAssets().FirstOrDefault(asset => AssetKey(asset) == _selectedKey);

    private void ActivateSelected()
    {
        ProjectAssetEntry? asset = SelectedAsset();
        if (asset == null) return;
        string path = _projects.GetAssetFullPath(asset);
        if (!File.Exists(path)) return;
        if (asset.AssetType == ProjectAssetType.Sound)
        {
            Timeline?.AddAudioTrackFromAsset(asset);
            return;
        }
        if (asset.AssetType != ProjectAssetType.Model) return;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".schematic" or ".schem") SpawnMenu?.SpawnSchematicFromPathInteractive(path);
        else SpawnMenu?.SpawnCustomModelFromPath(path);
    }

    private void RemoveSelected()
    {
        ProjectAssetEntry? asset = SelectedAsset();
        if (asset == null) return;
        _projects.RemoveAsset(asset);
        _selectedKey = null;
        Refresh(force: true);
    }

    private void Import(ProjectAssetType type, string filter)
    {
        var result = Dialog.FileOpen(filter);
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path)) return;
        ProjectAssetEntry asset = _projects.AddAsset(result.Path, type);
        _selectedKey = AssetKey(asset);
        Refresh(force: true);
    }

    private void Bind(string id, Action action) => _root.GetElementById(id)?.AddEventListener("click", _ => action());
    private static string AssetKey(ProjectAssetEntry asset) => $"{asset.RelativePath}|{asset.AssetType}";
    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
