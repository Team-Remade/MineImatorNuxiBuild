using System.Numerics;
using System.Linq;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class PropertiesPanel : UiPanel
{
    // ── Project tab state ─────────────────────────────────────────────────────

    public Node Floor;

    public readonly float[] BackgroundColor = [0.5764706f, 0.5764706f, 1f, 1f];

    public bool UseSky;
    public bool UseAdvancedSky;

    private string _projectName = "Untitled Project";
    private int _resolutionWidth = 1920;
    private int _resolutionHeight = 1080;
    private int _framerate = 30;

    public int GetResolutionWidth()  => _resolutionWidth;
    public int GetResolutionHeight() => _resolutionHeight;
    public int GetFramerate()        => _framerate;

    public int    TextureAnimationFps  = 20;
    public string BackgroundImagePath  = "No image selected";
    public bool   StretchBackground    = true;
    public bool   FloorVisible         = true;
    public string FloorTextureAtlas    = "block";
    public string FloorTileKey         = "grass_block_top";

    // ── Object tab state ──────────────────────────────────────────────────────

    private SceneObject _currentObject;

    // Scale link toggle
    private bool _linkScale = true;

    // ── Public wiring ─────────────────────────────────────────────────────────

    /// <summary>Set from MainWindow after both panels are initialised.</summary>
    public Timeline? Timeline { get; set; }
    public Viewport? Viewport { get; set; }

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
        TextureAnimationFps = Math.Clamp(settings.TextureAnimationFps, 1, 240);
        UseSky = settings.UseSky;
        UseAdvancedSky = settings.UseAdvancedSky;
        StretchBackground = settings.StretchBackground;
        BackgroundImagePath = string.IsNullOrWhiteSpace(settings.BackgroundImagePath)
            ? "No image selected"
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

        ApplyFloorSettingsToViewport();
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
        manifest.Settings.TextureAnimationFps = Math.Clamp(TextureAnimationFps, 1, 240);
        manifest.Settings.UseSky = UseSky;
        manifest.Settings.UseAdvancedSky = UseAdvancedSky;
        manifest.Settings.StretchBackground = StretchBackground;
        manifest.Settings.BackgroundImagePath = string.IsNullOrWhiteSpace(BackgroundImagePath)
            ? "No image selected"
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

        Timeline?.SetFrameRate(_framerate);
    }

    private static string NormalizeFloorAtlas(string atlas)
    {
        return string.Equals(atlas, "item", StringComparison.OrdinalIgnoreCase) ? "item" : "block";
    }

    private IEnumerable<string> GetFloorAtlasKeys()
    {
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
    
    // ── Selection callback ────────────────────────────────────────────────────

    private void OnSelectionChanged()
    {
        var sel = SelectionManager.Instance?.SelectedObjects;
        _currentObject = (sel != null && sel.Count > 0) ? sel[0] : null;
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
                // Background Image (display only; file picking not yet implemented)
                {
                    ImGui.Text("Background Image:");
                    ImGui.SameLine();
                    string backgroundImg = Path.GetFileName(BackgroundImagePath);
                    ImGui.Text(backgroundImg);
                    // TODO: open NativeFileDialog to pick a background texture file
                    if (ImGui.Button("Browse##backgroundBrowse"))
                    {
                        // TODO: implement background image file picker
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Clear##backgroundClear") && BackgroundImagePath != "No image selected")
                    {
                        BackgroundImagePath = "No image selected";
                        WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
                        ProjectManager.Instance.SetDirty(true);
                    }
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

            bool castShadow = _currentObject.CastShadow;
            if (ImGui.Checkbox("Cast Shadows", ref castShadow))
                _currentObject.CastShadow = castShadow;
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
            if (ImGui.DragFloat("X##posX", ref posX, 0.1f))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            if (ImGui.DragFloat("Y##posY", ref posY, 0.1f))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            if (ImGui.DragFloat("Z##posZ", ref posZ, 0.1f))
            {
                rawPos.x = posX / 16f;
                rawPos.y = posY / 16f;
                rawPos.z = posZ / 16f;
                ApplyPosition(rawPos);
            }
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##posReset"))
                ApplyPosition(vec3.Zero);
        }

        // ── Rotation ──────────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Rotation (degrees)"))
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
            if (ImGui.DragFloat("X##rotX", ref rotX, 0.5f))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            if (ImGui.DragFloat("Y##rotY", ref rotY, 0.5f))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            if (ImGui.DragFloat("Z##rotZ", ref rotZ, 0.5f))
            {
                rawRot = new vec3(rotX * (MathF.PI / 180f), rotY * (MathF.PI / 180f), rotZ * (MathF.PI / 180f));
                ApplyRotation(rawRot);
            }
            ImGui.PopItemWidth();

            if (ImGui.Button("Reset##rotReset"))
                ApplyRotation(vec3.Zero);
        }

        // ── Scale ─────────────────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Scale"))
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
        bool isLight = _currentObject is LightSceneObject;
        if (isLight) ImGui.BeginDisabled();
        if (ImGui.CollapsingHeader("Material") && !isLight)
        {
            // Ensure MaterialSettings exists when we need to write
            EnsureMaterialSettings();
            var mat = _currentObject.MaterialSettings;

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
        if (isLight) ImGui.EndDisabled();

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
                }

                // Range
                {
                    float range = light.LightRange;
                    if (ImGui.DragFloat("Range##lightRange", ref range, 0.1f, 0.01f, 500f))
                    {
                        light.LightRange = range;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.range");
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
                }

                // Specular
                {
                    float specular = light.LightSpecular;
                    if (ImGui.SliderFloat("Specular##lightSpecular", ref specular, 0f, 1f))
                    {
                        light.LightSpecular = specular;
                        Timeline?.RecordAutoKeyframe(_currentObject, "light.specular");
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
}