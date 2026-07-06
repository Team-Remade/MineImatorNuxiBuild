using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using NativeFileDialogSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class ContentBrowser : UiPanel
{
    public SpawnMenu? SpawnMenu { get; set; }
    public Action? ImportResourcePackRequested { get; set; }
    public Action? ImportResourcePackFolderRequested { get; set; }

    private int _selectedAssetIndex = -1;
    private string _search = "";
    private int _assetTypeFilterIndex = 0;
    private ProjectAssetEntry? _pendingRemoval;

    private static readonly ProjectAssetType[] FilterableAssetTypes =
    {
        ProjectAssetType.Unknown,
        ProjectAssetType.Model,
        ProjectAssetType.Image,
        ProjectAssetType.Sound,
        ProjectAssetType.Other
    };

    public override void Render()
    {
        ImGui.Begin("Content Browser");

        var projectManager = ProjectManager.Instance;
        if (!projectManager.HasProject)
        {
            ImGui.TextDisabled("No project is currently loaded.");
            ImGui.End();
            return;
        }

        ImGui.TextDisabled(projectManager.Manifest.ProjectName);
        ImGui.SameLine();
        ImGui.TextDisabled($"({projectManager.ProjectFolder})");

        RenderToolbar();
        ImGui.Separator();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##assetSearch", "Search assets...", ref _search, 128);

        ImGui.SameLine();
        RenderTypeFilter();

        ImGui.Separator();

        var assets = projectManager.GetProjectAssets();
        ImGui.BeginChild("##assetList", new Vector2(0, -34), ImGuiChildFlags.Borders);

        int visibleIndex = 0;
        for (int i = 0; i < assets.Count; i++)
        {
            var asset = assets[i];
            if (!IsAssetTypeVisible(asset.AssetType))
                continue;

            if (!string.IsNullOrWhiteSpace(_search) &&
                !asset.DisplayName.Contains(_search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string state = asset.StoredInProject ? "project" : "data";
            string row = $"[{asset.AssetType}] {asset.DisplayName} ({state})";
            bool selected = _selectedAssetIndex == i;

            if (ImGui.Selectable(row + "##asset" + i, selected))
                _selectedAssetIndex = i;

            if (ImGui.BeginPopupContextItem("##assetContext" + i))
            {
                if (ImGui.MenuItem("Remove asset..."))
                    _pendingRemoval = asset;
                ImGui.EndPopup();
            }

            if (ImGui.IsItemHovered())
            {
                string fullPath = projectManager.GetAssetFullPath(asset);
                ImGui.SetTooltip(fullPath);
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _selectedAssetIndex = i;
                SpawnSelectedAsset();
            }

            visibleIndex++;
        }

        if (visibleIndex == 0)
            ImGui.TextDisabled("No assets to display.");

        ImGui.EndChild();

        RenderRemovalPopup(projectManager);

        bool canSpawn = CanSpawnSelectedAsset();
        if (!canSpawn) ImGui.BeginDisabled();
        if (ImGui.Button("Spawn Selected Asset", new Vector2(-1, 28)))
            SpawnSelectedAsset();
        if (!canSpawn) ImGui.EndDisabled();

        ImGui.End();
    }

    private void RenderToolbar()
    {
        if (ImGui.Button("Import Asset", new Vector2(-1, 0)))
            ImGui.OpenPopup("##importAssetPopup");

        if (ImGui.BeginPopup("##importAssetPopup"))
        {
            if (ImGui.MenuItem("Model"))
                ImportAsset(ProjectAssetType.Model, "glb,gltf,fbx,obj,dae,3ds,blend,ply,stl,x3d,mimodel,miobject");

            if (ImGui.MenuItem("Image"))
                ImportAsset(ProjectAssetType.Image, "png,jpg,jpeg,bmp,tga,gif,webp,tiff");

            if (ImGui.MenuItem("Sound"))
                ImportAsset(ProjectAssetType.Sound, "wav,mp3,ogg,flac,m4a");

            ImGui.Separator();

            if (ImGui.MenuItem("Resource Pack (.zip)"))
                ImportResourcePackRequested?.Invoke();

            if (ImGui.MenuItem("Resource Pack Folder"))
                ImportResourcePackFolderRequested?.Invoke();

            ImGui.EndPopup();
        }
    }

    private void RenderTypeFilter()
    {
        string currentLabel = _assetTypeFilterIndex == 0
            ? "All types"
            : FilterableAssetTypes[_assetTypeFilterIndex - 1].ToString();

        ImGui.SetNextItemWidth(140);
        if (ImGui.BeginCombo("##assetTypeFilter", currentLabel))
        {
            if (ImGui.Selectable("All types", _assetTypeFilterIndex == 0))
                _assetTypeFilterIndex = 0;

            for (int i = 0; i < FilterableAssetTypes.Length; i++)
            {
                var assetType = FilterableAssetTypes[i];
                bool selected = _assetTypeFilterIndex == i + 1;
                if (ImGui.Selectable(assetType.ToString(), selected))
                    _assetTypeFilterIndex = i + 1;
            }

            ImGui.EndCombo();
        }
    }

    private bool IsAssetTypeVisible(ProjectAssetType assetType)
    {
        return _assetTypeFilterIndex == 0 ||
               FilterableAssetTypes[_assetTypeFilterIndex - 1] == assetType;
    }

    private void RenderRemovalPopup(ProjectManager projectManager)
    {
        if (_pendingRemoval != null)
            ImGui.OpenPopup("##removeAssetConfirm");

        bool popupOpen = true;
        if (!ImGui.BeginPopupModal("##removeAssetConfirm", ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (_pendingRemoval != null)
        {
            ImGui.TextWrapped($"Remove '{_pendingRemoval.DisplayName}' from the project and delete it from disk?");
            ImGui.Separator();

            if (ImGui.Button("Remove", new Vector2(120, 0)))
            {
                projectManager.RemoveAsset(_pendingRemoval);
                _selectedAssetIndex = -1;
                _pendingRemoval = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                _pendingRemoval = null;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndPopup();
    }

    private void ImportAsset(ProjectAssetType assetType, string filter)
    {
        var result = Dialog.FileOpen(filter);
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return;

        var entry = ProjectManager.Instance.AddAsset(result.Path, assetType);
        var assets = ProjectManager.Instance.GetProjectAssets();
        for (int i = 0; i < assets.Count; i++)
        {
            if (ReferenceEquals(assets[i], entry))
            {
                _selectedAssetIndex = i;
                break;
            }
        }
    }

    private bool CanSpawnSelectedAsset()
    {
        var projectManager = ProjectManager.Instance;
        var assets = projectManager.GetProjectAssets();

        if (_selectedAssetIndex < 0 || _selectedAssetIndex >= assets.Count)
            return false;

        var selected = assets[_selectedAssetIndex];
        string fullPath = projectManager.GetAssetFullPath(selected);
        if (!File.Exists(fullPath) || SpawnMenu == null)
            return false;

        if (selected.AssetType == ProjectAssetType.Model)
            return true;

        string ext = Path.GetExtension(fullPath).ToLowerInvariant();
        return ext is ".schematic" or ".schem";
    }

    private void SpawnSelectedAsset()
    {
        if (!CanSpawnSelectedAsset())
            return;

        var projectManager = ProjectManager.Instance;
        var selected = projectManager.GetProjectAssets()[_selectedAssetIndex];
        string fullPath = projectManager.GetAssetFullPath(selected);

        string ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext is ".schematic" or ".schem")
        {
            SpawnMenu?.SpawnSchematicFromPath(fullPath);
            return;
        }

        SpawnMenu?.SpawnCustomModelFromPath(fullPath);
    }
}
