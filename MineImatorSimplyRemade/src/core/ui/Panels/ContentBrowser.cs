using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using NativeFileDialogSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class ContentBrowser : UiPanel
{
    public SpawnMenu? SpawnMenu { get; set; }

    private int _selectedAssetIndex = -1;
    private string _search = "";

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

        ImGui.Separator();

        var assets = projectManager.GetProjectAssets();
        ImGui.BeginChild("##assetList", new Vector2(0, -34), ImGuiChildFlags.Borders);

        int visibleIndex = 0;
        for (int i = 0; i < assets.Count; i++)
        {
            var asset = assets[i];
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

            if (ImGui.IsItemHovered())
            {
                string fullPath = projectManager.GetAssetFullPath(asset);
                ImGui.SetTooltip(fullPath);
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _selectedAssetIndex = i;
                SpawnSelectedModel();
            }

            visibleIndex++;
        }

        if (visibleIndex == 0)
            ImGui.TextDisabled("No assets to display.");

        ImGui.EndChild();

        bool canSpawn = CanSpawnSelectedModel();
        if (!canSpawn) ImGui.BeginDisabled();
        if (ImGui.Button("Spawn Selected Model", new Vector2(-1, 28)))
            SpawnSelectedModel();
        if (!canSpawn) ImGui.EndDisabled();

        ImGui.End();
    }

    private void RenderToolbar()
    {
        if (ImGui.Button("Import Model"))
            ImportAsset(ProjectAssetType.Model, "glb,gltf,fbx,obj,dae,3ds,blend,ply,stl,x3d,mimodel,miobject");

        ImGui.SameLine();
        if (ImGui.Button("Import Image"))
            ImportAsset(ProjectAssetType.Image, "png,jpg,jpeg,bmp,tga,gif,webp,tiff");

        ImGui.SameLine();
        if (ImGui.Button("Import Sound"))
            ImportAsset(ProjectAssetType.Sound, "wav,mp3,ogg,flac,m4a");
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

    private bool CanSpawnSelectedModel()
    {
        var projectManager = ProjectManager.Instance;
        var assets = projectManager.GetProjectAssets();

        if (_selectedAssetIndex < 0 || _selectedAssetIndex >= assets.Count)
            return false;

        var selected = assets[_selectedAssetIndex];
        if (selected.AssetType != ProjectAssetType.Model)
            return false;

        string fullPath = projectManager.GetAssetFullPath(selected);
        return File.Exists(fullPath) && SpawnMenu != null;
    }

    private void SpawnSelectedModel()
    {
        if (!CanSpawnSelectedModel())
            return;

        var projectManager = ProjectManager.Instance;
        var selected = projectManager.GetProjectAssets()[_selectedAssetIndex];
        string fullPath = projectManager.GetAssetFullPath(selected);

        SpawnMenu?.SpawnCustomModelFromPath(fullPath);
    }
}
