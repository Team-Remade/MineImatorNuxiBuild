using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using NativeFileDialogSharp;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class ContentBrowser : UiPanel
{
    public SpawnMenu? SpawnMenu { get; set; }
    public Timeline?  Timeline  { get; set; }
    public Action? ImportResourcePackRequested { get; set; }
    public Action? ImportResourcePackFolderRequested { get; set; }

    private int _selectedAssetIndex = -1;
    private string _search = "";
    private int _selectedFolderIndex;
    private ProjectAssetEntry? _pendingRemoval;
    private readonly List<int> _visibleAssetIndices = [];
    private readonly Dictionary<string, ThumbnailEntry> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _pendingThumbnailPaths = new();
    private readonly HashSet<string> _queuedThumbnailPaths = new(StringComparer.OrdinalIgnoreCase);
    private string _thumbnailProjectFolder = "";

    private const float FolderPaneWidth = 150f;
    private const float TileWidth = 128f;
    private const float TileHeight = 144f;
    private const float ThumbnailSize = 96f;
    private const int ThumbnailPixelSize = 96;

    private sealed class ThumbnailEntry
    {
        public uint TextureId;
        public bool LoadFailed;
    }

    public override unsafe void Render()
    {
        ImGui.Begin("Content Browser");

        var projectManager = ProjectManager.Instance;
        if (!projectManager.HasProject)
        {
            ImGui.TextDisabled("No project is currently loaded.");
            ImGui.End();
            return;
        }

        ResetThumbnailCacheIfProjectChanged(projectManager.ProjectFolder);
        if (Gl != null)
            ContentBrowserIcons.Initialize(Gl);
        ProcessOnePendingThumbnail();

        ImGui.TextUnformatted(projectManager.Manifest.ProjectName);
        ImGui.SameLine();
        ImGui.TextDisabled("Content Drawer");

        RenderToolbar();
        ImGui.Separator();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##assetSearch", "Search assets...", ref _search, 128);
        ImGui.Separator();

        var assets = projectManager.GetProjectAssets();
        BuildVisibleAssetIndexList(assets);

        ImGui.BeginChild("##assetFolders", new Vector2(FolderPaneWidth, -34f), ImGuiChildFlags.Borders);
        RenderFolderNavigation(assets);
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##assetGrid", new Vector2(0, -34f), ImGuiChildFlags.Borders);
        RenderAssetGrid(assets, projectManager);
        ImGui.EndChild();

        RenderRemovalPopup(projectManager);

        bool canSpawn = CanSpawnSelectedAsset();
        bool canAddAudio = CanAddSelectedAssetToTimeline();
        if (!canSpawn && !canAddAudio) ImGui.BeginDisabled();

        if (canAddAudio)
        {
            if (ImGui.Button("Add Selected Sound to Timeline", new Vector2(-1, 28)))
                AddSelectedAssetToTimeline();
            if (canSpawn)
            {
                if (ImGui.Button("Spawn Selected Asset", new Vector2(-1, 28)))
                    SpawnSelectedAsset();
            }
        }
        else
        {
            if (ImGui.Button("Spawn Selected Asset", new Vector2(-1, 28)))
                SpawnSelectedAsset();
        }
        if (!canSpawn && !canAddAudio) ImGui.EndDisabled();

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

    private void BuildVisibleAssetIndexList(IReadOnlyList<ProjectAssetEntry> assets)
    {
        _visibleAssetIndices.Clear();
        for (int i = 0; i < assets.Count; i++)
        {
            var asset = assets[i];
            if (!MatchesSelectedFolder(asset.AssetType))
                continue;
            if (!string.IsNullOrWhiteSpace(_search) &&
                !asset.DisplayName.Contains(_search, StringComparison.OrdinalIgnoreCase))
                continue;

            _visibleAssetIndices.Add(i);
        }
    }

    private unsafe void RenderFolderNavigation(IReadOnlyList<ProjectAssetEntry> assets)
    {
        int models = 0;
        int images = 0;
        int sounds = 0;
        int other = 0;
        foreach (var asset in assets)
        {
            switch (asset.AssetType)
            {
                case ProjectAssetType.Model: models++; break;
                case ProjectAssetType.Image: images++; break;
                case ProjectAssetType.Sound: sounds++; break;
                default: other++; break;
            }
        }

        ImGui.TextDisabled("FOLDERS");
        RenderFolderItem("All Assets", assets.Count, 0);
        RenderFolderItem("Models", models, 1);
        RenderFolderItem("Images", images, 2);
        RenderFolderItem("Sounds", sounds, 3);
        RenderFolderItem("Other", other, 4);
    }

    private unsafe void RenderFolderItem(string name, int count, int folderIndex)
    {
        if (ContentBrowserIcons.Folder != 0)
        {
            ImGui.Image(new ImTextureRef(texId: ContentBrowserIcons.Folder), new Vector2(16f, 16f));
            ImGui.SameLine();
        }

        if (ImGui.Selectable($"{name}  ({count})##folder{folderIndex}", _selectedFolderIndex == folderIndex))
            _selectedFolderIndex = folderIndex;
    }

    private void RenderAssetGrid(IReadOnlyList<ProjectAssetEntry> assets, ProjectManager projectManager)
    {
        if (_visibleAssetIndices.Count == 0)
        {
            ImGui.TextDisabled("No assets match this folder and search.");
            return;
        }

        Vector2 gridOrigin = ImGui.GetCursorPos();
        float availableWidth = Math.Max(TileWidth, ImGui.GetContentRegionAvail().X);
        int columns = Math.Max(1, (int)(availableWidth / TileWidth));
        int rowCount = (_visibleAssetIndices.Count + columns - 1) / columns;
        int firstRow = Math.Clamp((int)(ImGui.GetScrollY() / TileHeight), 0, rowCount - 1);
        int visibleRows = Math.Max(1, (int)MathF.Ceiling(ImGui.GetWindowHeight() / TileHeight) + 1);
        int lastRow = Math.Min(rowCount, firstRow + visibleRows);

        for (int row = firstRow; row < lastRow; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int visibleIndex = row * columns + column;
                if (visibleIndex >= _visibleAssetIndices.Count)
                    break;

                int assetIndex = _visibleAssetIndices[visibleIndex];
                ImGui.SetCursorPos(new Vector2(
                    gridOrigin.X + column * TileWidth,
                    gridOrigin.Y + row * TileHeight));
                RenderAssetTile(assets[assetIndex], assetIndex, projectManager);
            }
        }

        ImGui.SetCursorPos(new Vector2(gridOrigin.X, gridOrigin.Y + rowCount * TileHeight));
        ImGui.Dummy(new Vector2(1f, 1f));
    }

    private unsafe void RenderAssetTile(ProjectAssetEntry asset, int assetIndex, ProjectManager projectManager)
    {
        Vector2 tilePosition = ImGui.GetCursorPos();
        Vector2 screenPosition = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();
        bool selected = _selectedAssetIndex == assetIndex;
        Vector2 tileSize = new(TileWidth - 6f, TileHeight - 6f);
        uint background = ImGui.ColorConvertFloat4ToU32(style.Colors[(int)(selected
            ? ImGuiCol.HeaderActive
            : ImGuiCol.FrameBg)]);
        drawList.AddRectFilled(screenPosition, screenPosition + tileSize, background, style.FrameRounding);
        drawList.AddRect(screenPosition, screenPosition + tileSize,
            ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.Border]), style.FrameRounding);

        string fullPath = projectManager.GetAssetFullPath(asset);
        uint thumbnailTexture = asset.AssetType == ProjectAssetType.Image
            ? GetOrQueueThumbnail(fullPath)
            : 0;

        ImGui.SetCursorPos(tilePosition + new Vector2(14f, 8f));
        if (thumbnailTexture != 0)
        {
            ImGui.Image(new ImTextureRef(texId: thumbnailTexture), new Vector2(ThumbnailSize, ThumbnailSize));
        }
        else
        {
            Vector2 iconPosition = ImGui.GetCursorScreenPos();
            drawList.AddRectFilled(iconPosition, iconPosition + new Vector2(ThumbnailSize, ThumbnailSize),
                ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.FrameBgHovered]), style.FrameRounding);
            uint icon = GetAssetTypeIcon(asset.AssetType);
            if (icon != 0)
            {
                ImGui.SetCursorPos(tilePosition + new Vector2(38f, 30f));
                ImGui.Image(new ImTextureRef(texId: icon), new Vector2(48f, 48f));
            }
            else
            {
                ImGui.SetCursorPos(tilePosition + new Vector2(14f, 43f));
                ImGui.TextUnformatted(GetAssetTypeLabel(asset.AssetType));
            }
        }

        ImGui.SetCursorPos(tilePosition + new Vector2(8f, 108f));
        ImGui.PushTextWrapPos(screenPosition.X + TileWidth - 10f);
        ImGui.TextUnformatted(AbbreviateAssetName(asset.DisplayName));
        ImGui.PopTextWrapPos();

        ImGui.SetCursorPos(tilePosition);
        ImGui.InvisibleButton($"##asset{assetIndex}", tileSize);
        bool hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            _selectedAssetIndex = assetIndex;
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            _selectedAssetIndex = assetIndex;
            SpawnSelectedAsset();
        }

        if (ImGui.BeginPopupContextItem($"##assetContext{assetIndex}"))
        {
            if (asset.AssetType == ProjectAssetType.Sound && Timeline != null)
            {
                if (ImGui.MenuItem("Add to Timeline as Audio Track"))
                {
                    Timeline.AddAudioTrackFromAsset(asset);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.Separator();
            }
            if (ImGui.MenuItem("Remove asset..."))
                _pendingRemoval = asset;
            ImGui.EndPopup();
        }

        if (hovered)
            ImGui.SetTooltip(fullPath);
    }

    private bool MatchesSelectedFolder(ProjectAssetType assetType)
    {
        return _selectedFolderIndex switch
        {
            0 => true,
            1 => assetType == ProjectAssetType.Model,
            2 => assetType == ProjectAssetType.Image,
            3 => assetType == ProjectAssetType.Sound,
            _ => assetType is not ProjectAssetType.Model and not ProjectAssetType.Image and not ProjectAssetType.Sound
        };
    }

    private static string GetAssetTypeLabel(ProjectAssetType assetType)
    {
        return assetType switch
        {
            ProjectAssetType.Model => "MODEL",
            ProjectAssetType.Image => "IMAGE",
            ProjectAssetType.Sound => "SOUND",
            _ => "ASSET"
        };
    }

    private static uint GetAssetTypeIcon(ProjectAssetType assetType)
    {
        return assetType switch
        {
            ProjectAssetType.Model => ContentBrowserIcons.Model,
            ProjectAssetType.Image => ContentBrowserIcons.Image,
            ProjectAssetType.Sound => ContentBrowserIcons.Sound,
            _ => ContentBrowserIcons.File
        };
    }

    private static string AbbreviateAssetName(string name)
    {
        const int maxLength = 34;
        return name.Length <= maxLength ? name : name[..(maxLength - 3)] + "...";
    }

    private uint GetOrQueueThumbnail(string fullPath)
    {
        if (_thumbnailCache.TryGetValue(fullPath, out var cached))
            return cached.TextureId;

        _thumbnailCache[fullPath] = new ThumbnailEntry();
        if (_queuedThumbnailPaths.Add(fullPath))
            _pendingThumbnailPaths.Enqueue(fullPath);
        return 0;
    }

    private void ProcessOnePendingThumbnail()
    {
        if (Gl == null || _pendingThumbnailPaths.Count == 0)
            return;

        string fullPath = _pendingThumbnailPaths.Dequeue();
        _queuedThumbnailPaths.Remove(fullPath);
        if (!_thumbnailCache.TryGetValue(fullPath, out var entry) || entry.LoadFailed || entry.TextureId != 0)
            return;

        if (!File.Exists(fullPath))
        {
            entry.LoadFailed = true;
            return;
        }

        try
        {
            entry.TextureId = CreateThumbnailTexture(fullPath);
            entry.LoadFailed = entry.TextureId == 0;
        }
        catch (Exception ex)
        {
            entry.LoadFailed = true;
            Console.Error.WriteLine($"Thumbnail load error '{fullPath}': {ex.Message}");
        }
    }

    private unsafe uint CreateThumbnailTexture(string fullPath)
    {
        if (Gl == null)
            return 0;

        byte[] sourceBytes = File.ReadAllBytes(fullPath);
        ImageResult image = ImageResult.FromMemory(sourceBytes, ColorComponents.RedGreenBlueAlpha);
        float scale = Math.Min(
            (float)ThumbnailPixelSize / image.Width,
            (float)ThumbnailPixelSize / image.Height);
        int width = Math.Max(1, (int)MathF.Round(image.Width * scale));
        int height = Math.Max(1, (int)MathF.Round(image.Height * scale));
        byte[] thumbnailPixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(image.Height - 1, (int)(y / scale));
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(image.Width - 1, (int)(x / scale));
                int sourceOffset = (sourceY * image.Width + sourceX) * 4;
                int destinationOffset = (y * width + x) * 4;
                thumbnailPixels[destinationOffset] = image.Data[sourceOffset];
                thumbnailPixels[destinationOffset + 1] = image.Data[sourceOffset + 1];
                thumbnailPixels[destinationOffset + 2] = image.Data[sourceOffset + 2];
                thumbnailPixels[destinationOffset + 3] = image.Data[sourceOffset + 3];
            }
        }

        uint texture = Gl.GenTexture();
        Gl.BindTexture(GLEnum.Texture2D, texture);
        fixed (byte* pixels = thumbnailPixels)
            Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        Gl.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        Gl.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        Gl.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        Gl.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        Gl.BindTexture(GLEnum.Texture2D, 0);
        return texture;
    }

    private void ResetThumbnailCacheIfProjectChanged(string projectFolder)
    {
        if (string.Equals(_thumbnailProjectFolder, projectFolder, StringComparison.OrdinalIgnoreCase))
            return;

        if (Gl != null)
        {
            foreach (var entry in _thumbnailCache.Values)
            {
                if (entry.TextureId != 0)
                    Gl.DeleteTexture(entry.TextureId);
            }
        }

        _thumbnailProjectFolder = projectFolder;
        _thumbnailCache.Clear();
        _pendingThumbnailPaths.Clear();
        _queuedThumbnailPaths.Clear();
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
            string confirmMessage = _pendingRemoval.StoredInProject
                ? $"Remove '{_pendingRemoval.DisplayName}' from the project and delete it from disk?"
                : $"Remove '{_pendingRemoval.DisplayName}' from the project? This is a built-in data asset, so its file will not be deleted from disk.";
            ImGui.TextWrapped(confirmMessage);
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

    private bool CanAddSelectedAssetToTimeline()
    {
        if (Timeline == null) return false;
        var assets = ProjectManager.Instance.GetProjectAssets();
        if (_selectedAssetIndex < 0 || _selectedAssetIndex >= assets.Count) return false;
        var selected = assets[_selectedAssetIndex];
        return selected.AssetType == ProjectAssetType.Sound
            && File.Exists(ProjectManager.Instance.GetAssetFullPath(selected));
    }

    private void AddSelectedAssetToTimeline()
    {
        if (!CanAddSelectedAssetToTimeline()) return;
        var assets = ProjectManager.Instance.GetProjectAssets();
        Timeline?.AddAudioTrackFromAsset(assets[_selectedAssetIndex]);
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
            SpawnMenu?.SpawnSchematicFromPathInteractive(fullPath);
            return;
        }

        SpawnMenu?.SpawnCustomModelFromPath(fullPath);
    }
}
