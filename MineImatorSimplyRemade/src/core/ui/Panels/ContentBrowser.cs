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

    private static float FolderPaneWidth => 150f * UiTheme.Scale;
    private static float TileWidth => 128f * UiTheme.Scale;
    private static float TileHeight => 144f * UiTheme.Scale;
    private static float ThumbnailSize => 96f * UiTheme.Scale;
    private const int ThumbnailPixelSize = 96;

    private sealed class ThumbnailEntry
    {
        public uint TextureId;
        public bool LoadFailed;
    }

    public override unsafe void Render()
    {
        ImGui.Begin(Localization.Label("text.common.content_browser", "Content Browser"));

        var projectManager = ProjectManager.Instance;
        if (!projectManager.HasProject)
        {
            UiText.Disabled(Localization.T("text.panel.content_browser.no_project_is_currently_loaded"));
            ImGui.End();
            return;
        }

        ResetThumbnailCacheIfProjectChanged(projectManager.ProjectFolder);
        if (Gl != null)
            ContentBrowserIcons.Initialize(Gl);
        ProcessOnePendingThumbnail();

        ImGui.TextUnformatted(projectManager.Manifest.ProjectName);
        ImGui.SameLine();
        UiText.Disabled(Localization.T("text.panel.content_browser.content_drawer"));

        RenderToolbar();
        ImGui.Separator();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##assetSearch", Localization.T("text.panel.content_browser.search_assets"), ref _search, 128);
        ImGui.Separator();

        var assets = projectManager.GetProjectAssets();
        BuildVisibleAssetIndexList(assets);

        ImGui.BeginChild("##assetFolders", new Vector2(FolderPaneWidth, -34f * UiTheme.Scale), ImGuiChildFlags.Borders);
        RenderFolderNavigation(assets);
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##assetGrid", new Vector2(0, -34f * UiTheme.Scale), ImGuiChildFlags.Borders);
        RenderAssetGrid(assets, projectManager);
        ImGui.EndChild();

        RenderRemovalPopup(projectManager);

        bool canSpawn = CanSpawnSelectedAsset();
        bool canAddAudio = CanAddSelectedAssetToTimeline();
        if (!canSpawn && !canAddAudio) ImGui.BeginDisabled();

        if (canAddAudio)
        {
            if (ImGui.Button(Localization.Label("text.panel.content_browser.add_selected_sound_to_timeline", "Add Selected Sound to Timeline"), new Vector2(-1, 28f * UiTheme.Scale)))
                AddSelectedAssetToTimeline();
            if (canSpawn)
            {
                if (ImGui.Button(Localization.Label("text.panel.content_browser.spawn_selected_asset", "Spawn Selected Asset"), new Vector2(-1, 28f * UiTheme.Scale)))
                    SpawnSelectedAsset();
            }
        }
        else
        {
            if (ImGui.Button(Localization.Label("text.panel.content_browser.spawn_selected_asset", "Spawn Selected Asset"), new Vector2(-1, 28f * UiTheme.Scale)))
                SpawnSelectedAsset();
        }
        if (!canSpawn && !canAddAudio) ImGui.EndDisabled();

        ImGui.End();
    }

    private void RenderToolbar()
    {
        if (ImGui.Button(Localization.Label("text.common.import_asset", "Import Asset"), new Vector2(-1, 0)))
            ImGui.OpenPopup("##importAssetPopup");

        if (ImGui.BeginPopup("##importAssetPopup"))
        {
            if (ImGui.MenuItem(Localization.Label("text.common.model", "Model")))
                ImportAsset(ProjectAssetType.Model, "glb,gltf,fbx,obj,dae,3ds,blend,ply,stl,x3d,mimodel,miobject");

            if (ImGui.MenuItem(Localization.Label("text.common.image", "Image")))
                ImportAsset(ProjectAssetType.Image, "png,jpg,jpeg,bmp,tga,gif,webp,tiff,hdr,exr");

            if (ImGui.MenuItem(Localization.Label("text.panel.content_browser.sound_2", "Sound")))
                ImportAsset(ProjectAssetType.Sound, "wav,mp3,ogg,flac,m4a");

            ImGui.Separator();

            if (ImGui.MenuItem(Localization.Label("text.panel.content_browser.resource_pack_zip", "Resource Pack (.zip)")))
                ImportResourcePackRequested?.Invoke();

            if (ImGui.MenuItem(Localization.Label("text.panel.content_browser.resource_pack_folder", "Resource Pack Folder")))
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

        UiText.Disabled(Localization.T("text.panel.content_browser.folders"));
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
            ImGui.Image(new ImTextureRef(texId: ContentBrowserIcons.Folder), new Vector2(16f, 16f) * UiTheme.Scale);
            ImGui.SameLine();
        }

        string label = Localization.Format("text.panel.content_browser.folder_with_count", Localization.DisplayText(name), count);
        if (ImGui.Selectable(Localization.ComposeLabel(label, $"{name}  ({count})##folder{folderIndex}"), _selectedFolderIndex == folderIndex))
            _selectedFolderIndex = folderIndex;
    }

    private void RenderAssetGrid(IReadOnlyList<ProjectAssetEntry> assets, ProjectManager projectManager)
    {
        if (_visibleAssetIndices.Count == 0)
        {
            UiText.Disabled(Localization.T("text.panel.content_browser.no_assets_match_this_folder_and_search"));
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
        ImGui.Dummy(new Vector2(1f, 1f) * UiTheme.Scale);
    }

    private unsafe void RenderAssetTile(ProjectAssetEntry asset, int assetIndex, ProjectManager projectManager)
    {
        float scale = UiTheme.Scale;
        Vector2 tilePosition = ImGui.GetCursorPos();
        Vector2 screenPosition = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();
        bool selected = _selectedAssetIndex == assetIndex;
        Vector2 tileSize = new(TileWidth - 6f * scale, TileHeight - 6f * scale);
        uint background = ImGui.ColorConvertFloat4ToU32(style.Colors[(int)(selected
            ? ImGuiCol.HeaderActive
            : ImGuiCol.FrameBg)]);
        drawList.AddRectFilled(screenPosition, screenPosition + tileSize, background, style.FrameRounding);
        drawList.AddRect(screenPosition, screenPosition + tileSize,
            ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.Border]), style.FrameRounding, ImDrawFlags.None, scale);

        string fullPath = projectManager.GetAssetFullPath(asset);
        uint thumbnailTexture = asset.AssetType == ProjectAssetType.Image
            ? GetOrQueueThumbnail(fullPath)
            : 0;

        ImGui.SetCursorPos(tilePosition + new Vector2(14f, 8f) * scale);
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
                ImGui.SetCursorPos(tilePosition + new Vector2(38f, 30f) * scale);
                ImGui.Image(new ImTextureRef(texId: icon), new Vector2(48f, 48f) * scale);
            }
            else
            {
                ImGui.SetCursorPos(tilePosition + new Vector2(14f, 43f) * scale);
                ImGui.TextUnformatted(Localization.DisplayText(GetAssetTypeLabel(asset.AssetType)));
            }
        }

        ImGui.SetCursorPos(tilePosition + new Vector2(8f, 108f) * scale);
        ImGui.PushTextWrapPos(tilePosition.X + TileWidth - 10f * scale);
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
                if (ImGui.MenuItem(Localization.Label("text.panel.content_browser.add_to_timeline_as_audio_track", "Add to Timeline as Audio Track")))
                {
                    Timeline.AddAudioTrackFromAsset(asset);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.Separator();
            }
            if (ImGui.MenuItem(Localization.Label("text.panel.content_browser.remove_asset", "Remove asset...")))
                _pendingRemoval = asset;
            ImGui.EndPopup();
        }

        if (hovered)
            UiText.Tooltip(fullPath);
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

        var (width, height, thumbnailPixels) = DecodeThumbnailPixels(fullPath);
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

    private static (int Width, int Height, byte[] Data) DecodeThumbnailPixels(string fullPath)
    {
        byte[] sourceBytes = File.ReadAllBytes(fullPath);
        int sourceWidth, sourceHeight;
        byte[] sourcePixels;
        bool flipRows = false;
        if (Path.GetExtension(fullPath).Equals(".exr", StringComparison.OrdinalIgnoreCase))
        {
            (sourceWidth, sourceHeight, sourcePixels) = ExrTextureDecoder.Decode(sourceBytes);
            // EXR bypasses stb, but must follow the same orientation as other thumbnails.
            flipRows = (StbImage.stbi__vertically_flip_on_load_set != 0
                ? StbImage.stbi__vertically_flip_on_load_local : StbImage.stbi__vertically_flip_on_load_global) != 0;
        }
        else
        {
            var image = ImageResult.FromMemory(sourceBytes, ColorComponents.RedGreenBlueAlpha);
            (sourceWidth, sourceHeight, sourcePixels) = (image.Width, image.Height, image.Data);
        }
        float scale = Math.Min(
            (float)ThumbnailPixelSize / sourceWidth,
            (float)ThumbnailPixelSize / sourceHeight);
        int width = Math.Max(1, (int)MathF.Round(sourceWidth * scale));
        int height = Math.Max(1, (int)MathF.Round(sourceHeight * scale));
        byte[] thumbnailPixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(sourceHeight - 1, (int)(y / scale));
            if (flipRows) sourceY = sourceHeight - 1 - sourceY;
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(sourceWidth - 1, (int)(x / scale));
                int sourceOffset = (sourceY * sourceWidth + sourceX) * 4;
                int destinationOffset = (y * width + x) * 4;
                thumbnailPixels[destinationOffset] = sourcePixels[sourceOffset];
                thumbnailPixels[destinationOffset + 1] = sourcePixels[sourceOffset + 1];
                thumbnailPixels[destinationOffset + 2] = sourcePixels[sourceOffset + 2];
                thumbnailPixels[destinationOffset + 3] = sourcePixels[sourceOffset + 3];
            }
        }
        return (width, height, thumbnailPixels);
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
                ? Localization.Format("text.panel.content_browser.remove_from_the_project_and_delete_it_from_disk", _pendingRemoval.DisplayName)
                : Localization.Format("text.panel.content_browser.remove_from_the_project_this_is_a_built_in_data_asset_so_its_file_will_not_be", _pendingRemoval.DisplayName);
            UiText.Wrapped(confirmMessage);
            ImGui.Separator();

            if (ImGui.Button(Localization.Label("text.common.remove", "Remove"), new Vector2(120f * UiTheme.Scale, 0)))
            {
                projectManager.RemoveAsset(_pendingRemoval);
                _selectedAssetIndex = -1;
                _pendingRemoval = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button(Localization.Label("text.common.cancel", "Cancel"), new Vector2(120f * UiTheme.Scale, 0)))
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
