using System.Numerics;
using System.Linq;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.material.materials;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using NativeFileDialogSharp;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class PropertiesPanel : UiPanel
{
    private const string NoImageSelected = "No image selected";
    private const string BackgroundModeStretch = "stretch";
    private const string BackgroundModeFit = "fit";
    private const string BackgroundModeOriginal = "original";

    // ── Project tab state ─────────────────────────────────────────────────────

    public Node Floor;

    public readonly float[] BackgroundColor = [0.5764706f, 0.5764706f, 1f, 1f];

    public bool UseSky;
    public bool UseAdvancedSky;
    public readonly float[] AmbientLightColor = [1f, 1f, 1f];
    public float AmbientLightStrength = 0.35f;
    public readonly float[] FillLightColor = [0.85f, 0.85f, 0.85f];
    public float FillLightStrength = 1f;
    public bool FillLightCastsShadows = true;

    private string _projectName = "Untitled Project";
    private int _resolutionWidth = 1920;
    private int _resolutionHeight = 1080;
    private int _framerate = 30;
    private string _renderMode = "image";
    private string _renderImageFormat = "png";
    private string _renderVideoFormat = "mp4";
    private int _renderVideoBitrateKbps = 12000;
    private string _renderResolutionPreset = "1080P";

    public int GetResolutionWidth()  => _resolutionWidth;
    public int GetResolutionHeight() => _resolutionHeight;
    public int GetFramerate()        => _framerate;
    public string GetRenderMode() => _renderMode;
    public string GetRenderImageFormat() => _renderImageFormat;
    public string GetRenderVideoFormat() => _renderVideoFormat;
    public int GetRenderVideoBitrateKbps() => _renderVideoBitrateKbps;
    public string GetRenderResolutionPreset() => _renderResolutionPreset;

    public void SetRenderDimensionsAndFramerate(int width, int height, int framerate)
    {
        _resolutionWidth = Math.Max(1, width);
        _resolutionHeight = Math.Max(1, height);
        _framerate = Math.Clamp(framerate, 1, 120);

        if (ProjectManager.Instance.HasProject)
            WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
    }

    public void SetRenderExportSettings(string mode, string imageFormat, string videoFormat, int videoBitrateKbps, string resolutionPreset)
    {
        _renderMode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
        _renderImageFormat = string.IsNullOrWhiteSpace(imageFormat) ? "png" : imageFormat.Trim().ToLowerInvariant();
        _renderVideoFormat = string.IsNullOrWhiteSpace(videoFormat) ? "mp4" : videoFormat.Trim().ToLowerInvariant();
        _renderVideoBitrateKbps = Math.Clamp(videoBitrateKbps, 500, 200000);
        _renderResolutionPreset = string.IsNullOrWhiteSpace(resolutionPreset) ? "Custom" : resolutionPreset.Trim();

        if (ProjectManager.Instance.HasProject)
            WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
    }

    public int    TextureAnimationFps  = 20;
    public string BackgroundImagePath  = NoImageSelected;
    public string BackgroundRenderMode = BackgroundModeStretch;
    public float  BackgroundScale      = 1f;
    public float  BackgroundRotationDegrees;
    public readonly float[] BackgroundOffset = [0f, 0f];
    public bool   StretchBackground    = true;
    public bool   FloorVisible         = true;
    public string FloorTextureAtlas    = "block";
    public string FloorTileKey         = "grass_block_top";

    // ── Object tab state ──────────────────────────────────────────────────────

    private SceneObject _currentObject;

    // Scale link toggle
    private bool _linkScale = true;

    // ── Right-click keyframe context menu ──────────────────────────────────
    
    private bool              _openPropContextMenu = false;
    private string?           _ctxPropertyPath;
    private System.Numerics.Vector2 _ctxMenuPos;

    // ── Public wiring ─────────────────────────────────────────────────────────

    /// <summary>Set from MainWindow after both panels are initialised.</summary>
    public Timeline? Timeline { get; set; }
    public Viewport? Viewport { get; set; }
    public SpawnMenu? SpawnMenu { get; set; }

    /// <summary>
    /// Subscribe to SelectionManager events.  Call once from App.Initialize()
    /// after SelectionManager.Initialize() has been called.
    /// </summary>
    public void Initialize()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.SelectionChanged += OnSelectionChanged;

        LoadProjectSettingsFromManifest(ProjectManager.Instance.Manifest);
    }

    public void LoadProjectSettingsFromManifest(ProjectManifest manifest)
    {
        if (manifest == null)
            return;

        _projectName = string.IsNullOrWhiteSpace(manifest.ProjectName) ? "Untitled Project" : manifest.ProjectName;

        ProjectRenderSettings settings = manifest.Settings ?? new ProjectRenderSettings();
        _resolutionWidth = Math.Max(1, settings.ResolutionWidth);
        _resolutionHeight = Math.Max(1, settings.ResolutionHeight);
        _framerate = Math.Clamp(settings.Framerate, 1, 120);
        _renderMode = string.Equals(settings.RenderMode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
        _renderImageFormat = string.IsNullOrWhiteSpace(settings.RenderImageFormat) ? "png" : settings.RenderImageFormat.Trim().ToLowerInvariant();
        _renderVideoFormat = string.IsNullOrWhiteSpace(settings.RenderVideoFormat) ? "mp4" : settings.RenderVideoFormat.Trim().ToLowerInvariant();
        _renderVideoBitrateKbps = Math.Clamp(settings.RenderVideoBitrateKbps, 500, 200000);
        _renderResolutionPreset = string.IsNullOrWhiteSpace(settings.RenderResolutionPreset) ? "Custom" : settings.RenderResolutionPreset.Trim();
        TextureAnimationFps = Math.Clamp(settings.TextureAnimationFps, 1, 240);
        UseSky = settings.UseSky;
        UseAdvancedSky = settings.UseAdvancedSky;
        BackgroundRenderMode = NormalizeBackgroundRenderMode(settings.BackgroundRenderMode);
        StretchBackground = settings.StretchBackground;
        if (string.IsNullOrWhiteSpace(settings.BackgroundRenderMode))
            BackgroundRenderMode = StretchBackground ? BackgroundModeStretch : BackgroundModeOriginal;

        BackgroundScale = Math.Clamp(settings.BackgroundScale, 0.01f, 20f);
        BackgroundRotationDegrees = Math.Clamp(settings.BackgroundRotationDegrees, -360f, 360f);
        BackgroundOffset[0] = settings.BackgroundOffsetX;
        BackgroundOffset[1] = settings.BackgroundOffsetY;
        BackgroundImagePath = string.IsNullOrWhiteSpace(settings.BackgroundImagePath)
            ? NoImageSelected
            : settings.BackgroundImagePath;
        FloorVisible = settings.FloorVisible;
        FloorTextureAtlas = NormalizeFloorAtlas(settings.FloorTextureAtlas);
        FloorTileKey = string.IsNullOrWhiteSpace(settings.FloorTileKey)
            ? "grass_block_top"
            : settings.FloorTileKey;

        ProjectVec4 bg = settings.BackgroundColor ?? new ProjectVec4 { X = 0.5764706f, Y = 0.5764706f, Z = 1f, W = 1f };
        BackgroundColor[0] = bg.X;
        BackgroundColor[1] = bg.Y;
        BackgroundColor[2] = bg.Z;
        BackgroundColor[3] = bg.W;

        ProjectVec3 ambient = settings.AmbientLightColor ?? new ProjectVec3 { X = 1f, Y = 1f, Z = 1f };
        AmbientLightColor[0] = ambient.X;
        AmbientLightColor[1] = ambient.Y;
        AmbientLightColor[2] = ambient.Z;
        AmbientLightStrength = settings.AmbientLightStrength;
        
        ProjectVec3 fillLight = settings.FillLightColor ?? new ProjectVec3 { X = 0.85f, Y = 0.85f, Z = 0.85f };
        FillLightColor[0] = fillLight.X;
        FillLightColor[1] = fillLight.Y;
        FillLightColor[2] = fillLight.Z;
        FillLightStrength = settings.FillLightStrength;
        FillLightCastsShadows = settings.FillLightCastsShadows;

        ApplyFloorSettingsToViewport();
        ApplyBackgroundSettingsToViewport();
        ApplyAmbientSettingsToRenderer();
        Timeline?.SetFrameRate(_framerate);
    }

    public void WriteProjectSettingsToManifest(ProjectManifest manifest)
    {
        if (manifest == null)
            return;

        string normalizedName = string.IsNullOrWhiteSpace(_projectName) ? "Untitled Project" : _projectName.Trim();
        _projectName = normalizedName;
        manifest.ProjectName = normalizedName;

        manifest.Settings ??= new ProjectRenderSettings();
        manifest.Settings.ResolutionWidth = Math.Max(1, _resolutionWidth);
        manifest.Settings.ResolutionHeight = Math.Max(1, _resolutionHeight);
        manifest.Settings.Framerate = Math.Clamp(_framerate, 1, 120);
        manifest.Settings.RenderMode = string.Equals(_renderMode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
        manifest.Settings.RenderImageFormat = string.IsNullOrWhiteSpace(_renderImageFormat) ? "png" : _renderImageFormat.Trim().ToLowerInvariant();
        manifest.Settings.RenderVideoFormat = string.IsNullOrWhiteSpace(_renderVideoFormat) ? "mp4" : _renderVideoFormat.Trim().ToLowerInvariant();
        manifest.Settings.RenderVideoBitrateKbps = Math.Clamp(_renderVideoBitrateKbps, 500, 200000);
        manifest.Settings.RenderResolutionPreset = string.IsNullOrWhiteSpace(_renderResolutionPreset) ? "Custom" : _renderResolutionPreset.Trim();
        manifest.Settings.TextureAnimationFps = Math.Clamp(TextureAnimationFps, 1, 240);
        manifest.Settings.UseSky = UseSky;
        manifest.Settings.UseAdvancedSky = UseAdvancedSky;
        BackgroundRenderMode = NormalizeBackgroundRenderMode(BackgroundRenderMode);
        BackgroundScale = Math.Clamp(BackgroundScale, 0.01f, 20f);
        BackgroundRotationDegrees = Math.Clamp(BackgroundRotationDegrees, -360f, 360f);

        manifest.Settings.BackgroundRenderMode = BackgroundRenderMode;
        manifest.Settings.StretchBackground = string.Equals(BackgroundRenderMode, BackgroundModeStretch, StringComparison.OrdinalIgnoreCase);
        manifest.Settings.BackgroundScale = BackgroundScale;
        manifest.Settings.BackgroundRotationDegrees = BackgroundRotationDegrees;
        manifest.Settings.BackgroundOffsetX = BackgroundOffset[0];
        manifest.Settings.BackgroundOffsetY = BackgroundOffset[1];
        manifest.Settings.BackgroundImagePath = string.IsNullOrWhiteSpace(BackgroundImagePath)
            ? NoImageSelected
            : BackgroundImagePath;
        manifest.Settings.FloorVisible = FloorVisible;
        manifest.Settings.FloorTextureAtlas = NormalizeFloorAtlas(FloorTextureAtlas);
        manifest.Settings.FloorTileKey = string.IsNullOrWhiteSpace(FloorTileKey)
            ? "grass_block_top"
            : FloorTileKey;

        manifest.Settings.BackgroundColor = new ProjectVec4
        {
            X = BackgroundColor[0],
            Y = BackgroundColor[1],
            Z = BackgroundColor[2],
            W = BackgroundColor[3]
        };
        manifest.Settings.AmbientLightColor = new ProjectVec3
        {
            X = AmbientLightColor[0],
            Y = AmbientLightColor[1],
            Z = AmbientLightColor[2]
        };
        manifest.Settings.AmbientLightStrength = AmbientLightStrength;
        manifest.Settings.FillLightColor = new ProjectVec3
        {
            X = FillLightColor[0],
            Y = FillLightColor[1],
            Z = FillLightColor[2]
        };
        manifest.Settings.FillLightStrength = FillLightStrength;
        manifest.Settings.FillLightCastsShadows = FillLightCastsShadows;
    }

    private void ApplyAmbientSettingsToRenderer()
    {
        AmbientLightColor[0] = Math.Clamp(AmbientLightColor[0], 0f, 1f);
        AmbientLightColor[1] = Math.Clamp(AmbientLightColor[1], 0f, 1f);
        AmbientLightColor[2] = Math.Clamp(AmbientLightColor[2], 0f, 1f);
        AmbientLightStrength = Math.Clamp(AmbientLightStrength, 0f, 5f);

        Mesh.GlobalAmbientColor = new vec3(
            AmbientLightColor[0],
            AmbientLightColor[1],
            AmbientLightColor[2]);
        Mesh.GlobalAmbientStrength = AmbientLightStrength;
        
        FillLightColor[0] = Math.Clamp(FillLightColor[0], 0f, 1f);
        FillLightColor[1] = Math.Clamp(FillLightColor[1], 0f, 1f);
        FillLightColor[2] = Math.Clamp(FillLightColor[2], 0f, 1f);
        FillLightStrength = Math.Clamp(FillLightStrength, 0f, 5f);

        Mesh.GlobalFillLightColor = new vec3(
            FillLightColor[0],
            FillLightColor[1],
            FillLightColor[2]);
        Mesh.GlobalFillLightStrength = FillLightStrength;
        Mesh.DirectionalShadowEnabled = FillLightCastsShadows;
    }

    private static string NormalizeFloorAtlas(string atlas)
    {
        return string.Equals(atlas, "item", StringComparison.OrdinalIgnoreCase) ? "item" : "block";
    }

    private static string NormalizeBackgroundRenderMode(string mode)
    {
        if (string.Equals(mode, BackgroundModeFit, StringComparison.OrdinalIgnoreCase))
            return BackgroundModeFit;
        if (string.Equals(mode, BackgroundModeOriginal, StringComparison.OrdinalIgnoreCase))
            return BackgroundModeOriginal;
        return BackgroundModeStretch;
    }

    private static string? ExtractItemTileKeyFromObjectType(string objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType))
            return null;

        int open = objectType.IndexOf('[');
        int close = objectType.LastIndexOf(']');
        if (open < 0 || close <= open)
            return null;

        return objectType[(open + 1)..close];
    }

    private static IEnumerable<string> GetItemAtlasKeys(ItemAtlasSource atlasSource)
    {
        if (atlasSource == ItemAtlasSource.ItemAtlas)
            ItemsAtlas.EnsureProjectCustomTexturesLoaded();

        var atlas = atlasSource == ItemAtlasSource.BlockAtlas ? TerrainAtlas.Textures : ItemsAtlas.Textures;
        return atlas.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetFloorAtlasKeys()
    {
        if (NormalizeFloorAtlas(FloorTextureAtlas) == "item")
            ItemsAtlas.EnsureProjectCustomTexturesLoaded();

        var atlas = NormalizeFloorAtlas(FloorTextureAtlas) == "item" ? ItemsAtlas.Textures : TerrainAtlas.Textures;
        return atlas.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyFloorSettingsToViewport()
    {
        if (Viewport == null)
            return;

        FloorTextureAtlas = NormalizeFloorAtlas(FloorTextureAtlas);
        Viewport.SetGroundPlaneVisible(FloorVisible);

        if (!Viewport.SetGroundPlaneTexture(FloorTextureAtlas, FloorTileKey))
        {
            string? fallback = GetFloorAtlasKeys().FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                FloorTileKey = fallback;
                Viewport.SetGroundPlaneTexture(FloorTextureAtlas, FloorTileKey);
            }
        }
    }

    private void ApplyBackgroundSettingsToViewport()
    {
        Viewport?.SetBackgroundImage(
            BackgroundImagePath,
            BackgroundRenderMode,
            BackgroundScale,
            BackgroundRotationDegrees,
            new Vector2(BackgroundOffset[0], BackgroundOffset[1]));
    }

    private IReadOnlyList<ProjectAssetEntry> GetBackgroundImageAssets()
    {
        return ProjectManager.Instance
            .GetProjectAssets()
            .Where(asset => asset.AssetType == ProjectAssetType.Image)
            .ToList();
    }

    private static string GetBackgroundImageLabel(string backgroundImagePath)
    {
        return string.IsNullOrWhiteSpace(backgroundImagePath) ||
               string.Equals(backgroundImagePath, NoImageSelected, StringComparison.OrdinalIgnoreCase)
            ? NoImageSelected
            : Path.GetFileName(backgroundImagePath);
    }

    private bool ImportBackgroundImageFromDialog()
    {
        if (!ProjectManager.Instance.HasProject)
            return false;

        var result = Dialog.FileOpen("png,jpg,jpeg,bmp,tga,gif,webp,tiff");
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return false;

        ProjectAssetEntry entry = ProjectManager.Instance.AddAsset(result.Path, ProjectAssetType.Image);
        string importedPath = entry.StoredInProject && !string.IsNullOrWhiteSpace(entry.RelativePath)
            ? entry.RelativePath
            : entry.SourcePath;

        BackgroundImagePath = string.IsNullOrWhiteSpace(importedPath) ? NoImageSelected : importedPath;
        return true;
    }
    
    // ── Selection callback ────────────────────────────────────────────────────

    private void OnSelectionChanged()
    {
        var sel = SelectionManager.Instance?.SelectedObjects;
        _currentObject = (sel != null && sel.Count > 0) ? sel[0] : null;
        
        // If object has a stored albedo texture path but hasn't loaded the texture yet, load it now
        if (_currentObject != null && 
            !string.IsNullOrEmpty(_currentObject.AlbedoTexturePath) &&
            string.Equals(_currentObject.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase))
        {
            // Check if any mesh already has the texture loaded
            bool hasTexture = false;
            foreach (var mesh in _currentObject.Visuals)
            {
                if (mesh.TextureId != 0)
                {
                    hasTexture = true;
                    break;
                }
            }
            
            // If not loaded, load it from the stored path
            if (!hasTexture)
            {
                string fullPath = Path.Combine(ProjectManager.Instance.ProjectFolder, _currentObject.AlbedoTexturePath);
                if (File.Exists(fullPath))
                {
                    OnLoadAlbedoTextureForObject(fullPath);
                }
            }
        }
    }
    
    public override void Render()
    {
        if (ImGui.Begin("Properties"))
        {
            if (ImGui.BeginTabBar("PropertiesTabs"))
            {
                RenderProjectTab();
                RenderObjectTab();
                ImGui.EndTabBar();
            }
            
            // Deferred context menu popup
            if (_openPropContextMenu)
            {
                _openPropContextMenu = false;
                ImGui.OpenPopup("##prop_keyframe_ctx");
            }
            RenderPropertyContextMenu();
        }
        ImGui.End();
    }
    
    private void RenderProjectTab()
    {
        if (!ImGui.BeginTabItem("Project")) return;

        ImGui.Text("Project Properties");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Project Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var projectManager = ProjectManager.Instance;

            ImGui.Text("Project Name:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##ProjectName", ref _projectName, 256))
            {
                WriteProjectSettingsToManifest(projectManager.Manifest);
                projectManager.SetDirty(true);
            }

            ImGui.Spacing();
            ImGui.Text("Resolution:");
            ImGui.SetNextItemWidth(80);
            bool resolutionChanged = ImGui.InputInt("##ResWidth", ref _resolutionWidth, 0, 0, ImGuiInputTextFlags.None);
            ImGui.SameLine();
            ImGui.Text(" x ");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            resolutionChanged |= ImGui.InputInt("##ResHeight", ref _resolutionHeight, 0, 0, ImGuiInputTextFlags.None);
            ImGui.Text("Presets:");
            if (ImGui.Button("720p"))  { _resolutionWidth = 1280; _resolutionHeight = 720; resolutionChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("1080p")) { _resolutionWidth = 1920; _resolutionHeight = 1080; resolutionChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("1440p")) { _resolutionWidth = 2560; _resolutionHeight = 1440; resolutionChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("4K"))    { _resolutionWidth = 3840; _resolutionHeight = 2160; resolutionChanged = true; }
            if (resolutionChanged)
            {
                _resolutionWidth = Math.Max(1, _resolutionWidth);
                _resolutionHeight = Math.Max(1, _resolutionHeight);
                WriteProjectSettingsToManifest(projectManager.Manifest);
                projectManager.SetDirty(true);
            }

            ImGui.Spacing();
            ImGui.Text("Framerate:");
            ImGui.SetNextItemWidth(80);
            bool frameRateChanged = ImGui.InputInt("##Framerate", ref _framerate, 0, 0, ImGuiInputTextFlags.None);
            ImGui.SameLine();
            ImGui.Text(" fps");
            ImGui.Text("Presets:");
            if (ImGui.Button("24"))  { _framerate = 24; frameRateChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("30"))  { _framerate = 30; frameRateChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("60"))  { _framerate = 60; frameRateChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("120")) { _framerate = 120; frameRateChanged = true; }
            if (frameRateChanged)
            {
                _framerate = Math.Clamp(_framerate, 1, 120);
                WriteProjectSettingsToManifest(projectManager.Manifest);
                projectManager.SetDirty(true);
            }

            ImGui.Spacing();
            ImGui.Text("Texture Animation Speed:");
            ImGui.SetNextItemWidth(80);
            bool textureFpsChanged = ImGui.InputInt("##TexAnimSpeed", ref TextureAnimationFps, 0, 0, ImGuiInputTextFlags.None);
            ImGui.SameLine();
            ImGui.Text(" fps");
            ImGui.Text("Presets:");
            if (ImGui.Button("10##tex")) { TextureAnimationFps = 10; textureFpsChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("20##tex")) { TextureAnimationFps = 20; textureFpsChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("30##tex")) { TextureAnimationFps = 30; textureFpsChanged = true; }
            ImGui.SameLine();
            if (ImGui.Button("60##tex")) { TextureAnimationFps = 60; textureFpsChanged = true; }
            if (textureFpsChanged)
            {
                TextureAnimationFps = Math.Clamp(TextureAnimationFps, 1, 240);
                WriteProjectSettingsToManifest(projectManager.Manifest);
                projectManager.SetDirty(true);
            }
        }

        if (ImGui.CollapsingHeader("Background Settings"))
        {
            unsafe
            {
                ImGui.Text("Background Color:");
                ImGui.SetNextItemWidth(-1);
                fixed (byte* label = "##BackgroundColor"u8)
                fixed (float* bgColorPtr = BackgroundColor)
                {
                    if (ImGui.ColorEdit4(label, bgColorPtr, ImGuiColorEditFlags.None))
                    {
                        WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                        ProjectManager.Instance.SetDirty(true);
                    }
                }

                ImGui.Spacing();
                ImGui.Text("Presets:");
                var presets = new (string name, float r, float g, float b, float a)[]
                {
                    ("Dawn",    1f,          0.7f,        0.5f,  1f),
                    ("Morning", 0.6f,        0.8f,        1f,    1f),
                    ("Day",     0.5764706f,  0.5764706f,  1f,    1f),
                    ("Sunset",  1f,          0.5f,        0.3f,  1f),
                    ("Dusk",    0.3f,        0.4f,        0.7f,  1f),
                    ("Night",   0.05f,       0.05f,       0.15f, 1f)
                };
                for (int i = 0; i < presets.Length; i++)
                {
                    if (i > 0) ImGui.SameLine();
                    if (ImGui.Button(presets[i].name))
                    {
                        BackgroundColor[0] = presets[i].r;
                        BackgroundColor[1] = presets[i].g;
                        BackgroundColor[2] = presets[i].b;
                        BackgroundColor[3] = presets[i].a;
                        WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                        ProjectManager.Instance.SetDirty(true);
                    }
                }
                ImGui.Spacing();

                bool floorVisible = FloorVisible;
                if (ImGui.Checkbox("Show Floor", ref floorVisible))
                {
                    FloorVisible = floorVisible;
                    ApplyFloorSettingsToViewport();
                    WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                    ProjectManager.Instance.SetDirty(true);
                }

                bool floorChanged = false;
                string floorAtlasLabel = NormalizeFloorAtlas(FloorTextureAtlas) == "item" ? "Item Atlas" : "Block Atlas";
                if (ImGui.BeginCombo("Floor Atlas", floorAtlasLabel))
                {
                    bool useBlock = NormalizeFloorAtlas(FloorTextureAtlas) == "block";
                    if (ImGui.Selectable("Block Atlas", useBlock))
                    {
                        FloorTextureAtlas = "block";
                        floorChanged = true;
                    }

                    bool useItem = NormalizeFloorAtlas(FloorTextureAtlas) == "item";
                    if (ImGui.Selectable("Item Atlas", useItem))
                    {
                        FloorTextureAtlas = "item";
                        floorChanged = true;
                    }

                    ImGui.EndCombo();
                }

                ImGui.Text("Floor Tile:");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##FloorTile", FloorTileKey))
                {
                    foreach (string key in GetFloorAtlasKeys())
                    {
                        bool selected = string.Equals(key, FloorTileKey, StringComparison.Ordinal);
                        if (ImGui.Selectable(key, selected))
                        {
                            FloorTileKey = key;
                            floorChanged = true;
                        }
                    }
                    ImGui.EndCombo();
                }

                if (floorChanged)
                {
                    ApplyFloorSettingsToViewport();
                    WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                    ProjectManager.Instance.SetDirty(true);
                }

                ImGui.Spacing();
                bool backgroundChanged = false;
                
                var imageAssets = GetBackgroundImageAssets();
                string selectedImageLabel = GetBackgroundImageLabel(BackgroundImagePath);
                ImGui.Text("Background Image:");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##BackgroundImage", selectedImageLabel))
                {
                    bool noneSelected = string.Equals(BackgroundImagePath, NoImageSelected, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(NoImageSelected, noneSelected))
                    {
                        BackgroundImagePath = NoImageSelected;
                        backgroundChanged = true;
                    }

                    foreach (var asset in imageAssets)
                    {
                        string candidatePath = !string.IsNullOrWhiteSpace(asset.RelativePath)
                            ? asset.RelativePath
                            : asset.SourcePath;
                        if (string.IsNullOrWhiteSpace(candidatePath))
                            continue;

                        bool selected = string.Equals(candidatePath, BackgroundImagePath, StringComparison.OrdinalIgnoreCase);
                        string optionLabel = string.IsNullOrWhiteSpace(asset.DisplayName)
                            ? Path.GetFileName(candidatePath)
                            : asset.DisplayName;

                        if (ImGui.Selectable(optionLabel + "##" + candidatePath, selected))
                        {
                            BackgroundImagePath = candidatePath;
                            backgroundChanged = true;
                        }
                    }

                    ImGui.EndCombo();
                }

                if (ImGui.Button("Import##backgroundImport") && ImportBackgroundImageFromDialog())
                    backgroundChanged = true;

                ImGui.SameLine();
                if (ImGui.Button("Clear##backgroundClear") && !string.Equals(BackgroundImagePath, NoImageSelected, StringComparison.OrdinalIgnoreCase))
                {
                    BackgroundImagePath = NoImageSelected;
                    backgroundChanged = true;
                }

                if (backgroundChanged)
                {
                    ApplyBackgroundSettingsToViewport();
                    WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                    ProjectManager.Instance.SetDirty(true);
                }

                string modeLabel = BackgroundRenderMode switch
                {
                    BackgroundModeFit => "Fit",
                    BackgroundModeOriginal => "Original",
                    _ => "Stretch"
                };

                if (ImGui.BeginCombo("Background Mode", modeLabel))
                {
                    bool isStretch = string.Equals(BackgroundRenderMode, BackgroundModeStretch, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable("Stretch", isStretch))
                    {
                        BackgroundRenderMode = BackgroundModeStretch;
                        StretchBackground = true;
                        backgroundChanged = true;
                    }

                    bool isFit = string.Equals(BackgroundRenderMode, BackgroundModeFit, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable("Fit", isFit))
                    {
                        BackgroundRenderMode = BackgroundModeFit;
                        StretchBackground = false;
                        backgroundChanged = true;
                    }

                    bool isOriginal = string.Equals(BackgroundRenderMode, BackgroundModeOriginal, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable("Original", isOriginal))
                    {
                        BackgroundRenderMode = BackgroundModeOriginal;
                        StretchBackground = false;
                        backgroundChanged = true;
                    }

                    ImGui.EndCombo();
                }

                float backgroundScale = BackgroundScale;
                if (ImGui.DragFloat("Background Scale", ref backgroundScale, 0.01f, 0.01f, 20f))
                {
                    BackgroundScale = Math.Clamp(backgroundScale, 0.01f, 20f);
                    backgroundChanged = true;
                }

                float backgroundRotation = BackgroundRotationDegrees;
                if (ImGui.DragFloat("Background Rotation", ref backgroundRotation, 0.25f, -360f, 360f))
                {
                    BackgroundRotationDegrees = Math.Clamp(backgroundRotation, -360f, 360f);
                    backgroundChanged = true;
                }

                float offsetX = BackgroundOffset[0];
                if (ImGui.DragFloat("Background Offset X", ref offsetX, 0.005f, -3f, 3f))
                {
                    BackgroundOffset[0] = offsetX;
                    backgroundChanged = true;
                }

                float offsetY = BackgroundOffset[1];
                if (ImGui.DragFloat("Background Offset Y", ref offsetY, 0.005f, -3f, 3f))
                {
                    BackgroundOffset[1] = offsetY;
                    backgroundChanged = true;
                }

                if (ImGui.Button("Reset Transform##backgroundResetTransform"))
                {
                    BackgroundScale = 1f;
                    BackgroundRotationDegrees = 0f;
                    BackgroundOffset[0] = 0f;
                    BackgroundOffset[1] = 0f;
                    backgroundChanged = true;
                }

                ImGui.Spacing();
                ImGui.Text("Ambient Light:");
                bool ambientChanged = false;
                ImGui.SetNextItemWidth(-1);
                fixed (byte* ambientLabel = "##AmbientLightColor"u8)
                fixed (float* ambientColorPtr = AmbientLightColor)
                {
                    if (ImGui.ColorEdit3(ambientLabel, ambientColorPtr, ImGuiColorEditFlags.None))
                        ambientChanged = true;
                }

                float ambientStrength = AmbientLightStrength;
                ImGui.SetNextItemWidth(120f);
                if (ImGui.DragFloat("Ambient Strength", ref ambientStrength, 0.01f, 0f, 5f))
                {
                    AmbientLightStrength = ambientStrength;
                    ambientChanged = true;
                }

                ImGui.Spacing();
                ImGui.Text("Fill Light:");
                ImGui.SetNextItemWidth(-1);
                fixed (byte* fillLabel = "##FillLightColor"u8)
                fixed (float* fillColorPtr = FillLightColor)
                {
                    if (ImGui.ColorEdit3(fillLabel, fillColorPtr, ImGuiColorEditFlags.None))
                        ambientChanged = true;
                }

                float fillStrength = FillLightStrength;
                ImGui.SetNextItemWidth(120f);
                if (ImGui.DragFloat("Fill Strength", ref fillStrength, 0.01f, 0f, 5f))
                {
                    FillLightStrength = fillStrength;
                    ambientChanged = true;
                }

                bool fillLightCastsShadows = FillLightCastsShadows;
                if (ImGui.Checkbox("Fill Light Casts Shadows", ref fillLightCastsShadows))
                {
                    FillLightCastsShadows = fillLightCastsShadows;
                    ambientChanged = true;
                }

                if (ambientChanged)
                {
                    ApplyAmbientSettingsToRenderer();
                    WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                    ProjectManager.Instance.SetDirty(true);
                }
            }
        }

        ImGui.EndTabItem();
    }
    
    private void RenderObjectTab()
    {
        if (!ImGui.BeginTabItem("Object")) return;

        // ── Object header ─────────────────────────────────────────────────────
        if (_currentObject == null)
        {
            float windowWidth = ImGui.GetWindowWidth();
            string noSelText = "No object selected";
            float textWidth  = ImGui.CalcTextSize(noSelText).X;
            ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
            ImGui.TextDisabled(noSelText);
            ImGui.EndTabItem();
            return;
        }

        // Centered bold name label
        {
            float windowWidth = ImGui.GetWindowWidth();
            string displayName = _currentObject.GetDisplayName();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0.6f, 1f));
            float textWidth = ImGui.CalcTextSize(displayName).X;
            ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
            ImGui.Text(displayName);
            ImGui.PopStyleColor();
        }

        ImGui.Separator();

        // ── Visibility & shadows ──────────────────────────────────────────────
        {
            bool vis = _currentObject.ObjectVisible;
            if (ImGui.Checkbox("Visible", ref vis))
            {
                _currentObject.SetObjectVisible(vis);
                Timeline?.RecordAutoKeyframe(_currentObject, "visible");
            }

            bool inheritVis = _currentObject.InheritVisibility;
            if (ImGui.Checkbox("Inherit Visibility", ref inheritVis))
                _currentObject.InheritVisibility = inheritVis;

            // Hide Cast Shadows for cameras and point lights
            if (!(_currentObject is CameraSceneObject) && !(_currentObject is LightSceneObject))
            {
                bool castShadow = _currentObject.CastShadow;
                if (ImGui.Checkbox("Cast Shadows", ref castShadow))
                    _currentObject.CastShadow = castShadow;
            }
        }

        ImGui.Spacing();

        // ── Position ──────────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Position"))
        {
            bool inheritPos = _currentObject.InheritPosition;
            if (ImGui.Checkbox("Inherit Position##pos", ref inheritPos))
                _currentObject.InheritPosition = inheritPos;

            // MiBoneSceneObjects expose an offset from their model base pose (always zero at load).
            // Plain BoneSceneObjects (GLB) use TargetPosition as an offset from rest pose.
            vec3 rawPos = (_currentObject is MiBoneSceneObject miPos)
                ? miPos.OffsetPosition
                : (_currentObject is BoneSceneObject bonePos)
                    ? bonePos.TargetPosition
                    : _currentObject.LocalPosition;

            float posX = rawPos.x * 16f;
            float posY = rawPos.y * 16f;
            float posZ = rawPos.z * 16f;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            
            // Position X
            if (ImGui.DragFloat("X##posX", ref posX, 0.1f))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "position.x";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Position Y
            if (ImGui.DragFloat("Y##posY", ref posY, 0.1f))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "position.y";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Position Z
            if (ImGui.DragFloat("Z##posZ", ref posZ, 0.1f))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "position.z";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##posReset"))
                ApplyPosition(vec3.Zero);
        }

        // ── Rotation ──────────────────────────────────────────────────────────
        // Point lights cannot have rotation (they're omni-directional)
        bool canRotate = !(_currentObject is LightSceneObject);
        if (canRotate && ImGui.CollapsingHeader("Rotation (degrees)"))
        {
            bool inheritRot = _currentObject.InheritRotation;
            if (ImGui.Checkbox("Inherit Rotation##rot", ref inheritRot))
                _currentObject.InheritRotation = inheritRot;

            vec3 rawRot = (_currentObject is MiBoneSceneObject miRot)
                ? miRot.OffsetRotation
                : (_currentObject is BoneSceneObject boneRot)
                    ? boneRot.TargetRotation
                    : _currentObject.LocalRotation;

            float rotX = rawRot.x * (180f / MathF.PI);
            float rotY = rawRot.y * (180f / MathF.PI);
            float rotZ = rawRot.z * (180f / MathF.PI);

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            
            // Rotation X
            if (ImGui.DragFloat("X##rotX", ref rotX, 0.5f))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "rotation.x";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Rotation Y
            if (ImGui.DragFloat("Y##rotY", ref rotY, 0.5f))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "rotation.y";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Rotation Z
            if (ImGui.DragFloat("Z##rotZ", ref rotZ, 0.5f))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "rotation.z";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##rotReset"))
                ApplyRotation(vec3.Zero);
        }

        // ── Scale ─────────────────────────────────────────────────────────────
        // Cameras and point lights cannot have their scale changed
        bool canScale = !(_currentObject is CameraSceneObject) && !(_currentObject is LightSceneObject);
        if (canScale && ImGui.CollapsingHeader("Scale"))
        {
            bool inheritScale = _currentObject.InheritScale;
            if (ImGui.Checkbox("Inherit Scale##scale", ref inheritScale))
                _currentObject.InheritScale = inheritScale;

            ImGui.Checkbox("Link Scale", ref _linkScale);

            vec3 curScale = (_currentObject is MiBoneSceneObject miScale)
                ? miScale.OffsetScale
                : _currentObject.LocalScale;
            float scaleX = curScale.x;
            float scaleY = curScale.y;
            float scaleZ = curScale.z;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            
            // Scale X
            if (ImGui.DragFloat("X##scaleX", ref scaleX, 0.01f, 0.001f, float.MaxValue))
            {
                scaleX = MathF.Max(scaleX, 0.001f);
                if (_linkScale)
                {
                    float delta = scaleX - curScale.x;
                    scaleY = MathF.Max(curScale.y + delta, 0.001f);
                    scaleZ = MathF.Max(curScale.z + delta, 0.001f);
                }
                ApplyScale(new vec3(scaleX, scaleY, scaleZ));
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "scale.x";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Scale Y
            if (ImGui.DragFloat("Y##scaleY", ref scaleY, 0.01f, 0.001f, float.MaxValue))
            {
                scaleY = MathF.Max(scaleY, 0.001f);
                if (_linkScale)
                {
                    float delta = scaleY - curScale.y;
                    scaleX = MathF.Max(curScale.x + delta, 0.001f);
                    scaleZ = MathF.Max(curScale.z + delta, 0.001f);
                }
                ApplyScale(new vec3(scaleX, scaleY, scaleZ));
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "scale.y";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            
            // Scale Z
            if (ImGui.DragFloat("Z##scaleZ", ref scaleZ, 0.01f, 0.001f, float.MaxValue))
            {
                scaleZ = MathF.Max(scaleZ, 0.001f);
                if (_linkScale)
                {
                    float delta = scaleZ - curScale.z;
                    scaleX = MathF.Max(curScale.x + delta, 0.001f);
                    scaleY = MathF.Max(curScale.y + delta, 0.001f);
                }
                ApplyScale(new vec3(scaleX, scaleY, scaleZ));
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _ctxPropertyPath = "scale.z";
                _ctxMenuPos = ImGui.GetMousePos();
                _openPropContextMenu = true;
            }
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##scaleReset"))
                ApplyScale(vec3.Ones);
        }

        // ── Pivot Offset ──────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Pivot Offset"))
        {
            bool inheritPivot = _currentObject.InheritPivotOffset;
            if (ImGui.Checkbox("Inherit Pivot##pivot", ref inheritPivot))
                _currentObject.InheritPivotOffset = inheritPivot;

            vec3 pivot = _currentObject.PivotOffset;
            float pivX = pivot.x * 16f;
            float pivY = pivot.y * 16f;
            float pivZ = pivot.z * 16f;

            ImGui.PushItemWidth(-ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemInnerSpacing.X * 2);
            if (ImGui.DragFloat("X##pivX", ref pivX, 0.1f))
                _currentObject.PivotOffset = new vec3(pivX / 16f, pivY / 16f, pivZ / 16f);

            if (ImGui.DragFloat("Y##pivY", ref pivY, 0.1f))
                _currentObject.PivotOffset = new vec3(pivX / 16f, pivY / 16f, pivZ / 16f);

            if (ImGui.DragFloat("Z##pivZ", ref pivZ, 0.1f))
                _currentObject.PivotOffset = new vec3(pivX / 16f, pivY / 16f, pivZ / 16f);
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##pivReset"))
                _currentObject.PivotOffset = vec3.Zero;
        }

        // ── Material ──────────────────────────────────────────────────────────
        bool isLight = _currentObject is LightSceneObject || _currentObject is CameraSceneObject;
        if (!isLight && ImGui.CollapsingHeader("Material"))
        {
            // Ensure MaterialSettings exists when we need to write
            EnsureMaterialSettings();
            var mat = _currentObject.MaterialSettings;

            bool supportsResourcePack = string.Equals(_currentObject.SpawnCategory, "Blocks", StringComparison.Ordinal) ||
                                        string.Equals(_currentObject.SpawnCategory, "Scenery", StringComparison.Ordinal);
            bool supportsItemImage = string.Equals(_currentObject.SpawnCategory, "Items", StringComparison.Ordinal);

            if (supportsItemImage)
            {
                var atlasSource = string.Equals(_currentObject.TextureType, "block", StringComparison.OrdinalIgnoreCase)
                    ? ItemAtlasSource.BlockAtlas
                    : ItemAtlasSource.ItemAtlas;

                string currentKey = ExtractItemTileKeyFromObjectType(_currentObject.ObjectType)
                                    ?? GetItemAtlasKeys(atlasSource).FirstOrDefault()
                                    ?? "";

                string atlasLabel = atlasSource == ItemAtlasSource.BlockAtlas ? "Block Atlas" : "Item Atlas";
                if (ImGui.BeginCombo("Item Atlas", atlasLabel))
                {
                    bool useItem = atlasSource == ItemAtlasSource.ItemAtlas;
                    if (ImGui.Selectable("Item Atlas", useItem))
                    {
                        atlasSource = ItemAtlasSource.ItemAtlas;
                        currentKey = GetItemAtlasKeys(atlasSource).FirstOrDefault() ?? currentKey;
                    }

                    bool useBlock = atlasSource == ItemAtlasSource.BlockAtlas;
                    if (ImGui.Selectable("Block Atlas", useBlock))
                    {
                        atlasSource = ItemAtlasSource.BlockAtlas;
                        currentKey = GetItemAtlasKeys(atlasSource).FirstOrDefault() ?? currentKey;
                    }

                    ImGui.EndCombo();
                }

                ImGui.Text("Item Image:");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##ItemImageKey", string.IsNullOrWhiteSpace(currentKey) ? "(none)" : currentKey))
                {
                    foreach (string key in GetItemAtlasKeys(atlasSource))
                    {
                        bool selected = string.Equals(key, currentKey, StringComparison.Ordinal);
                        if (ImGui.Selectable(key, selected))
                        {
                            if (SpawnMenu != null && SpawnMenu.ApplyItemTextureToSpawnedObject(_currentObject, atlasSource, key))
                            {
                                ProjectManager.Instance.SetDirty(true);
                                currentKey = key;
                            }
                        }
                        if (selected)
                            ImGui.SetItemDefaultFocus();
                    }

                    ImGui.EndCombo();
                }

                if (ImGui.Button("Load custom image...##ItemMaterialCustom", new Vector2(-1, 0)))
                {
                    string? customKey = SpawnMenu?.ImportCustomItemImageFromDialogForProperties();
                    if (!string.IsNullOrWhiteSpace(customKey) &&
                        SpawnMenu != null &&
                        SpawnMenu.ApplyItemTextureToSpawnedObject(_currentObject, ItemAtlasSource.ItemAtlas, customKey))
                    {
                        ProjectManager.Instance.SetDirty(true);
                    }
                }

                ImGui.Spacing();
            }

            if (supportsResourcePack)
            {
                string currentPackId = MinecraftDataLoader.NormalizeResourcePackId(_currentObject.ResourcePackId);
                var packIds = MinecraftDataLoader.GetAvailableResourcePackIds().ToList();

                int selectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(currentPackId))
                {
                    int found = packIds.FindIndex(id => string.Equals(id, currentPackId, StringComparison.OrdinalIgnoreCase));
                    if (found >= 0)
                        selectedIndex = found + 1;
                }

                string selectedLabel = selectedIndex == 0 ? "Default" : packIds[selectedIndex - 1];
                if (ImGui.BeginCombo("Resource Pack", selectedLabel))
                {
                    bool isDefaultSelected = selectedIndex == 0;
                    if (ImGui.Selectable("Default", isDefaultSelected))
                    {
                        if (SpawnMenu != null && SpawnMenu.ApplyResourcePackToSpawnedObject(_currentObject, ""))
                        {
                            _currentObject.ResourcePackId = "";
                            ProjectManager.Instance.SetDirty(true);
                        }
                    }
                    if (isDefaultSelected)
                        ImGui.SetItemDefaultFocus();

                    for (int i = 0; i < packIds.Count; i++)
                    {
                        string id = packIds[i];
                        bool isSelected = selectedIndex == (i + 1);
                        if (ImGui.Selectable(id, isSelected))
                        {
                            if (SpawnMenu != null && SpawnMenu.ApplyResourcePackToSpawnedObject(_currentObject, id))
                            {
                                _currentObject.ResourcePackId = id;
                                ProjectManager.Instance.SetDirty(true);
                            }
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }

                    ImGui.EndCombo();
                }

                ImGui.Spacing();
            }

            // Albedo texture (for primitives and custom objects)
            if (string.Equals(_currentObject.SpawnCategory, "Primitives", StringComparison.OrdinalIgnoreCase))
            {
                ImGui.Text("Albedo Texture:");
                
                // Show current texture if any mesh has one
                uint currentTextureId = 0;
                foreach (var mesh in _currentObject.Visuals)
                {
                    if (mesh.TextureId != 0)
                    {
                        currentTextureId = mesh.TextureId;
                        break;
                    }
                }

                if (currentTextureId != 0)
                {
                    ImGui.TextDisabled("(texture loaded)");
                }
                else
                {
                    ImGui.TextDisabled("(none)");
                }

                if (ImGui.Button("Load texture...##AlbedoTexture", new Vector2(-1, 0)))
                {
                    var result = Dialog.FileOpen("png,jpg,jpeg,bmp,tga,gif,webp,tiff");
                    if (result.IsOk && !string.IsNullOrWhiteSpace(result.Path) && File.Exists(result.Path))
                    {
                        OnLoadAlbedoTextureForObject(result.Path);
                        ProjectManager.Instance.SetDirty(true);
                    }
                }

                if (currentTextureId != 0 && ImGui.Button("Clear texture##AlbedoTextureClear", new Vector2(-1, 0)))
                {
                    foreach (var mesh in _currentObject.Visuals)
                    {
                        if (mesh.TextureId != 0)
                        {
                            Gl?.DeleteTexture(mesh.TextureId);
                            mesh.TextureId = 0;
                        }
                    }
                    ProjectManager.Instance.SetDirty(true);
                }

                ImGui.Spacing();
            }

            // Alpha – skip for BoneSceneObject
            if (_currentObject is not BoneSceneObject)
            {
                float alpha = mat?.AlbedoColor.a ?? 1f;
                ImGui.SetNextItemWidth(-60f);
                if (ImGui.SliderFloat("Alpha", ref alpha, 0f, 1f))
                {
                    EnsureMaterialSettings();
                    var c = _currentObject.MaterialSettings.AlbedoColor;
                    _currentObject.MaterialSettings.AlbedoColor = new vec4(c.r, c.g, c.b, alpha);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                    Timeline?.RecordAutoKeyframe(_currentObject, "material.alpha");
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _ctxPropertyPath = "material.alpha";
                    _ctxMenuPos = ImGui.GetMousePos();
                    _openPropContextMenu = true;
                }
                ImGui.SameLine();
                ImGui.Text(alpha.ToString("F2"));
            }

            // Albedo color
            {
                vec4 ac = mat?.AlbedoColor ?? new vec4(1f, 1f, 1f, 1f);
                var vec4 = new Vector4(ac.r, ac.g, ac.b, ac.a);
                if (ImGui.ColorEdit4("Albedo", ref vec4))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.AlbedoColor = new vec4(vec4.X, vec4.Y, vec4.Z, vec4.W);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Metallic
            {
                float metallic = mat?.Metallic ?? 0f;
                if (ImGui.SliderFloat("Metallic", ref metallic, 0f, 1f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.Metallic = metallic;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Roughness
            {
                float roughness = mat?.Roughness ?? 0.5f;
                if (ImGui.SliderFloat("Roughness", ref roughness, 0f, 1f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.Roughness = roughness;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Emission enabled
            {
                bool emissionEnabled = mat?.EmissionEnabled ?? false;
                if (ImGui.Checkbox("Emission", ref emissionEnabled))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.EmissionEnabled = emissionEnabled;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Emission color (no alpha)
            {
                vec4 ec = mat?.EmissionColor ?? new vec4(0f, 0f, 0f, 1f);
                var vec3 = new Vector3(ec.r, ec.g, ec.b);
                if (ImGui.ColorEdit3("Emission Color", ref vec3))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.EmissionColor = new vec4(vec3.X, vec3.Y, vec3.Z, 1f);
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Emission energy
            {
                float emEnergy = mat?.EmissionEnergy ?? 1f;
                if (ImGui.SliderFloat("Emission Energy", ref emEnergy, 0f, 10f))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.EmissionEnergy = emEnergy;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Normal map (display only; file picking not yet implemented)
            {
                string normalName = (mat?.NormalTexture != 0) ? "(texture)" : "None";
                ImGui.Text("Normal: " + normalName);
                // TODO: open NativeFileDialog to pick a normal-map texture file
                if (ImGui.Button("Browse##normalBrowse"))
                {
                    // TODO: implement normal map file picker
                }
                ImGui.SameLine();
                if (ImGui.Button("Clear##normalClear") && mat != null)
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.NormalTexture = 0;
                    _currentObject.MaterialSettings.NormalEnabled  = false;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            // Double Sided
            {
                bool doubleSided = mat?.DoubleSided ?? false;
                if (ImGui.Checkbox("Double Sided", ref doubleSided))
                {
                    EnsureMaterialSettings();
                    _currentObject.MaterialSettings.DoubleSided = doubleSided;
                    _currentObject.SetExplicitMaterialSettings();
                    _currentObject.PropagateMaterialSettingsToChildren();
                }
            }

            ImGui.Spacing();

            // Reset material
            if (ImGui.Button("Reset Material"))
            {
                EnsureMaterialSettings();
                var m = _currentObject.MaterialSettings;
                m.AlbedoColor     = new vec4(1f, 1f, 1f, 1f);
                m.Metallic        = 0f;
                m.Roughness       = 0.5f;
                m.EmissionEnabled = false;
                m.EmissionEnergy  = 1f;
                m.NormalTexture   = 0;
                m.NormalEnabled   = false;
                m.DoubleSided     = false;
                _currentObject.SetExplicitMaterialSettings();
                _currentObject.PropagateMaterialSettingsToChildren();
            }
        }

        // ── Light (shown only for LightSceneObject) ───────────────────────────
        if (_currentObject is LightSceneObject light)
        {
            if (ImGui.CollapsingHeader("Light"))
            {
                // Color
                {
                    var lc   = light.LightColor;
                    var vec3 = new Vector3(lc.r, lc.g, lc.b);
                    if (ImGui.ColorEdit3("Color##lightColor", ref vec3))
                    {
                        light.LightColor = new vec4(vec3.X, vec3.Y, vec3.Z, 1);
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.color.r");
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.color.g");
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.color.b");
                    }
                }

                // Energy
                {
                    float energy = light.LightEnergy;
                    if (ImGui.DragFloat("Energy##lightEnergy", ref energy, 0.05f, 0f, 100f))
                    {
                        light.LightEnergy = energy;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.energy");
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _ctxPropertyPath = "light.energy";
                        _ctxMenuPos = ImGui.GetMousePos();
                        _openPropContextMenu = true;
                    }
                }

                // Range
                {
                    float range = light.LightRange;
                    if (ImGui.DragFloat("Range##lightRange", ref range, 0.1f, 0.01f, 500f))
                    {
                        light.LightRange = range;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.range");
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _ctxPropertyPath = "light.range";
                        _ctxMenuPos = ImGui.GetMousePos();
                        _openPropContextMenu = true;
                    }
                }

                // Indirect Energy
                {
                    float indirect = light.LightIndirectEnergy;
                    if (ImGui.DragFloat("Indirect Energy##lightIndirect", ref indirect, 0.05f, 0f, 16f))
                    {
                        light.LightIndirectEnergy = indirect;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.indirect_energy");
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _ctxPropertyPath = "light.indirect_energy";
                        _ctxMenuPos = ImGui.GetMousePos();
                        _openPropContextMenu = true;
                    }
                }

                // Specular
                {
                    float specular = light.LightSpecular;
                    if (ImGui.SliderFloat("Specular##lightSpecular", ref specular, 0f, 1f))
                    {
                        light.LightSpecular = specular;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.specular");
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _ctxPropertyPath = "light.specular";
                        _ctxMenuPos = ImGui.GetMousePos();
                        _openPropContextMenu = true;
                    }
                }

                // Cast Shadows
                {
                    bool shadow = light.LightShadowEnabled;
                    if (ImGui.Checkbox("Cast Shadows##lightShadow", ref shadow))
                        light.LightShadowEnabled = shadow;
                }

                ImGui.Spacing();

                // Reset light
                if (ImGui.Button("Reset Light"))
                {
                    light.LightEnergy         = 1f;
                    light.LightRange          = 5f;
                    light.LightIndirectEnergy = 1f;
                    light.LightSpecular       = 0.5f;
                    light.LightShadowEnabled  = true;
                    light.LightColor          = new vec4(1f, 1f, 1f, 1f);
                }
            }
        }

        // ── Bend section (not yet implemented) ────────────────────────────────
        // TODO: implement Bend section once BoneSceneObject.BendParameters is available

        ImGui.EndTabItem();
    }
    
    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplyPosition(vec3 pos)
    {
        // MiBoneSceneObjects: pos is an offset from the base pose.
        // Plain BoneSceneObjects (GLB): pos is an offset from the rest pose.
        if (_currentObject is MiBoneSceneObject miPos)
            miPos.OffsetPosition = pos;
        else if (_currentObject is BoneSceneObject bone)
            bone.TargetPosition = pos;
        else
            _currentObject.SetLocalPosition(pos);
        Timeline?.RecordAutoKeyframe(_currentObject, "position.x");
        Timeline?.RecordAutoKeyframe(_currentObject, "position.y");
        Timeline?.RecordAutoKeyframe(_currentObject, "position.z");
    }

    private void ApplyRotation(vec3 rot)
    {
        if (_currentObject is MiBoneSceneObject miRot)
            miRot.OffsetRotation = rot;
        else if (_currentObject is BoneSceneObject bone)
            bone.TargetRotation = rot;
        else
            _currentObject.SetLocalRotation(rot);
        Timeline?.RecordAutoKeyframe(_currentObject, "rotation.x");
        Timeline?.RecordAutoKeyframe(_currentObject, "rotation.y");
        Timeline?.RecordAutoKeyframe(_currentObject, "rotation.z");
    }

    private void ApplyScale(vec3 scale)
    {
        if (_currentObject is MiBoneSceneObject miScale)
            miScale.OffsetScale = scale;
        else
            _currentObject.SetLocalScale(scale);
        Timeline?.RecordAutoKeyframe(_currentObject, "scale.x");
        Timeline?.RecordAutoKeyframe(_currentObject, "scale.y");
        Timeline?.RecordAutoKeyframe(_currentObject, "scale.z");
    }

    /// <summary>
    /// Ensures <see cref="SceneObject.MaterialSettings"/> is non-null before writing.
    /// </summary>
    private void EnsureMaterialSettings()
    {
        if (_currentObject == null) return;
        if (_currentObject.MaterialSettings == null)
            _currentObject.MaterialSettings = new MaterialSettings();
    }

    /// <summary>
    /// Loads a texture from file and applies it as the albedo texture to all meshes in the current object.
    /// Supports PNG, JPG, BMP, TGA, GIF, WebP, and TIFF formats with RGBA color components.
    /// </summary>
    private unsafe void OnLoadAlbedoTextureForObject(string filePath)
    {
        if (_currentObject == null || Gl == null || !File.Exists(filePath))
            return;

        try
        {
            // Flip image vertically on load for OpenGL Y-axis convention
            StbImage.stbi_set_flip_vertically_on_load(1);
            
            var bytes = File.ReadAllBytes(filePath);
            ImageResult img = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            
            // Reset to default behavior
            StbImage.stbi_set_flip_vertically_on_load(0);

            uint tex = Gl.GenTexture();
            Gl.BindTexture(GLEnum.Texture2D, tex);

            fixed (byte* p = img.Data)
                Gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba,
                    (uint)img.Width, (uint)img.Height,
                    0, GLEnum.Rgba, GLEnum.UnsignedByte, p);

            // Use linear filtering for better image quality
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            // Allow wrapping for tileable textures
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.Repeat);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.Repeat);
            Gl.BindTexture(GLEnum.Texture2D, 0);

            // Apply to all meshes
            foreach (var mesh in _currentObject.Visuals)
            {
                // Delete old texture if exists
                if (mesh.TextureId != 0)
                    Gl.DeleteTexture(mesh.TextureId);
                
                mesh.TextureId = tex;
                
                // Configure material for proper rendering
                if (mesh.GetSurfaceCount() > 0)
                {
                    var material = mesh.SurfaceGetMaterial(0);
                    if (material is StandardMaterial stdMat)
                    {
                        stdMat.AlbedoColor = new vec4(1f, 1f, 1f, 1f); // White for full color pass-through
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PropertiesPanel] Failed to load albedo texture '{filePath}': {ex.Message}");
        }
    }

    // ── Property context menu ─────────────────────────────────────────────────

    private void RenderPropertyContextMenu()
    {
        if (!ImGui.BeginPopup("##prop_keyframe_ctx")) return;
        if (_currentObject == null || _ctxPropertyPath == null) { ImGui.EndPopup(); return; }

        ImGui.TextDisabled("Keyframe");
        ImGui.Separator();

        if (ImGui.MenuItem("Add Keyframe at Current Frame"))
        {
            Timeline?.AddKeyframeForProperty(_currentObject, _ctxPropertyPath, Timeline?.CurrentFrame ?? 0);
        }

        ImGui.EndPopup();
    }
}