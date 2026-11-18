using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemadeNuxi.core.objs;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class SpawnMenu
{
    private string _bannerEditorBlock = "";
    private CharacterEntry? _bannerEditorEntry;
    private string _bannerEditorResourcePack = "";
    private string _bannerBaseColor = "white";
    private List<BannerPatternLayer> _bannerLayers = [];
    private bool _bannerEditorVisible;
    private bool _bannerEditorRequested;
    private bool _bannerPreviewDirty;
    private PreviewRenderer? _bannerPreviewRenderer;
    private SceneObject? _bannerPreviewRoot;
    private string? _bannerEditorError;
    private int _bannerDraggedLayer = -1;

    private void OpenBannerPatternEditor(CharacterEntry entry)
    {
        _bannerEditorEntry = entry;
        _bannerEditorBlock = entry.Name;
        _bannerEditorResourcePack = _spawnResourcePackId;
        _bannerBaseColor = "white";
        _bannerLayers = [];
        _bannerEditorVisible = _bannerEditorRequested = _bannerPreviewDirty = true;
        _bannerEditorError = null;
        _bannerDraggedLayer = -1;
    }

    private unsafe void RenderBannerPatternEditor()
    {
        string title = Localization.Label("text.panel.spawn.banner_pattern_editor", "Banner Pattern Editor") +
            "###BannerPatternEditor";
        if (_bannerEditorRequested)
        {
            ImGui.OpenPopup(title);
            _bannerEditorRequested = false;
        }
        ImGui.SetNextWindowSize(new Vector2(760f, 610f) * UiTheme.Scale, ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(title, ref _bannerEditorVisible)) return;

        float previewWidth = Math.Min(290f * UiTheme.Scale, ImGui.GetContentRegionAvail().X * 0.45f);
        float footer = ImGui.GetFrameHeightWithSpacing() * 2f;
        float height = Math.Max(120f, ImGui.GetContentRegionAvail().Y - footer);
        ImGui.BeginChild("##bannerPatternPreview", new Vector2(previewWidth, height), ImGuiChildFlags.Borders);
        UpdateBannerPatternPreview();
        if (_bannerPreviewRenderer != null)
        {
            float width = Math.Max(1f, ImGui.GetContentRegionAvail().X);
            float renderHeight = Math.Max(1f, ImGui.GetContentRegionAvail().Y);
            _bannerPreviewRenderer.Resize((int)width, (int)renderHeight);
            ImGui.Image(new ImTextureRef(texId: (ulong)_bannerPreviewRenderer.ColorTexture),
                new Vector2(width, renderHeight), new Vector2(0, 1), new Vector2(1, 0));
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##bannerPatternOptions", new Vector2(0, height));
        if (ImGui.Button(Localization.Label("text.panel.spawn.banner_add_layer", "+ Add layer"), new Vector2(-1, 0)))
        {
            _bannerLayers.Add(new BannerPatternLayer
            {
                Pattern = BannerPatternSystem.Patterns[Random.Shared.Next(BannerPatternSystem.Patterns.Count)],
                Color = BannerPatternSystem.Colors[Random.Shared.Next(BannerPatternSystem.Colors.Count)]
            });
            _bannerPreviewDirty = true;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted(Localization.Label("text.panel.spawn.banner_base_color", "Base color"));
        int baseIndex = IndexOf(BannerPatternSystem.Colors, _bannerBaseColor);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##bannerBase", ref baseIndex, ColorLabels(), BannerPatternSystem.Colors.Count))
        {
            _bannerBaseColor = BannerPatternSystem.Colors[baseIndex];
            _bannerPreviewDirty = true;
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Localization.Label("text.panel.spawn.banner_layers", "Layers (top first)"));
        ImGui.BeginChild("##bannerLayerList", new Vector2(0, 0), ImGuiChildFlags.Borders);
        for (int displayIndex = _bannerLayers.Count - 1; displayIndex >= 0; displayIndex--)
        {
            var layer = _bannerLayers[displayIndex];
            ImGui.PushID(displayIndex);
            if (ImGui.Button("=", new Vector2(ImGui.GetFrameHeight(), 0)))
                _bannerDraggedLayer = displayIndex;
            if (ImGui.BeginDragDropSource())
            {
                byte dummy = 1;
                _bannerDraggedLayer = displayIndex;
                ImGui.SetDragDropPayload("BANNER_LAYER", &dummy, 1);
                ImGui.TextUnformatted(DisplayPattern(layer.Pattern) + " / " + DisplayColor(layer.Color));
                ImGui.EndDragDropSource();
            }
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("BANNER_LAYER");
                if (payload.Handle != null && _bannerDraggedLayer >= 0 && _bannerDraggedLayer != displayIndex)
                {
                    MoveBannerLayer(_bannerDraggedLayer, displayIndex);
                    _bannerDraggedLayer = -1;
                    _bannerPreviewDirty = true;
                }
                ImGui.EndDragDropTarget();
            }

            ImGui.SameLine();
            int colorIndex = IndexOf(BannerPatternSystem.Colors, layer.Color);
            ImGui.SetNextItemWidth(105f * UiTheme.Scale);
            if (ImGui.Combo("##layerColor", ref colorIndex, ColorLabels(), BannerPatternSystem.Colors.Count))
            {
                layer.Color = BannerPatternSystem.Colors[colorIndex];
                _bannerPreviewDirty = true;
            }
            ImGui.SameLine();
            int patternIndex = IndexOf(BannerPatternSystem.Patterns, layer.Pattern);
            ImGui.SetNextItemWidth(Math.Max(100f, ImGui.GetContentRegionAvail().X - 30f * UiTheme.Scale));
            if (ImGui.Combo("##layerPattern", ref patternIndex, PatternLabels(), BannerPatternSystem.Patterns.Count))
            {
                layer.Pattern = BannerPatternSystem.Patterns[patternIndex];
                _bannerPreviewDirty = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("X"))
            {
                _bannerLayers.RemoveAt(displayIndex);
                _bannerPreviewDirty = true;
            }
            ImGui.PopID();
        }
        if (_bannerLayers.Count == 0)
            UiText.Disabled(Localization.Label("text.panel.spawn.banner_no_layers",
                "Add a layer to begin a pattern."));
        ImGui.EndChild();
        if (_bannerEditorError != null) UiText.Wrapped(_bannerEditorError);
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.BeginDisabled(_bannerPreviewDirty || _bannerEditorError != null || _bannerPreviewRoot == null);
        if (ImGui.Button(Localization.Label("text.panel.spawn.spawn", "Spawn")))
        {
            string blockName = _bannerEditorBlock;
            var entry = _bannerEditorEntry;
            string baseColor = _bannerBaseColor;
            var layers = _bannerLayers.Select(layer => layer.Clone()).ToList();
            if (entry != null)
            {
                ImGui.CloseCurrentPopup();
                _bannerEditorVisible = false;
                SpawnWithCursor(() => CreateBannerPropObject(entry, baseColor, layers));
                _isOpen = false;
            }
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(Localization.Label("text.common.cancel", "Cancel")))
        {
            _bannerEditorVisible = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
        if (!ImGui.IsPopupOpen(title)) _bannerEditorVisible = false;
    }

    private void UpdateBannerPatternPreview()
    {
        if (Gl == null || string.IsNullOrEmpty(_bannerEditorBlock)) return;
        _bannerPreviewRenderer ??= new PreviewRenderer(Gl) { Distance = 2.8f, AutoRotateSpeed = 0f };
        if (_bannerPreviewRoot == null)
            _bannerPreviewRoot = BuildBannerModel(_bannerEditorEntry?.FilePath ?? _bannerEditorBlock);
        if (_bannerPreviewDirty && _bannerPreviewRoot != null)
        {
            _bannerEditorError = null;
            _bannerPreviewDirty = false;
            try
            {
                BannerPatternSystem.ApplyToScene(_bannerPreviewRoot, _bannerBaseColor, _bannerLayers);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
            {
                _bannerEditorError = ex.Message;
                Console.Error.WriteLine($"Banner pattern editor: {ex.Message}");
            }
        }
        _bannerPreviewRenderer.Render([], "banner-pattern-editor", 0,
            sceneRoot: _bannerPreviewRoot, centerMeshes: true, isolatedLighting: true);
    }

    private SceneObject? BuildBannerModel(string modelReference, bool applyPattern = false,
        string? baseColor = null, IReadOnlyList<BannerPatternLayer>? layers = null)
    {
        if (Gl == null) return null;
        string path = File.Exists(modelReference)
            ? modelReference
            : Path.Combine(MinecraftDataLoader.GetVersionRoot(), "props", "vanilla",
                modelReference, modelReference + ".mimodel");
        var loader = MineImatorLoader.Instance;
        loader.Initialize(Gl);
        var model = loader.LoadModel(path);
        var root = model == null ? null : loader.CreateCharacterFromModel(model);
        if (root == null) return null;
        root.SourceAssetPath = path;
        if (applyPattern)
            BannerPatternSystem.ApplyToScene(root,
                baseColor ?? "white",
                layers ?? []);
        return root;
    }

    private SceneObject? CreateBannerPropObject(CharacterEntry entry, string baseColor,
        IReadOnlyList<BannerPatternLayer> layers)
    {
        var root = BuildBannerModel(entry.FilePath, applyPattern: true, baseColor, layers);
        if (root == null) return null;
        int nextNumber = GetNextAvailableObjectNumber(entry.Name);
        root.Name = nextNumber > 1 ? $"{entry.Name}{nextNumber}" : entry.Name;
        root.ObjectType = entry.Name;
        root.SpawnCategory = "Props";
        root.ResourcePackId = MinecraftDataLoader.NormalizeResourcePackStack(_bannerEditorResourcePack);
        root.PivotOffset = GlmSharp.vec3.Zero;
        root.AssignObjectId();
        Viewport?.SceneObjects.Add(root);
        return root;
    }

    private void DisposeBannerPatternEditorPreview()
    {
        if (_bannerPreviewRoot != null) DisposeModelGeometry(_bannerPreviewRoot);
        _bannerPreviewRoot = null;
        _bannerPreviewRenderer?.Dispose();
        _bannerPreviewRenderer = null;
        _bannerDraggedLayer = -1;
    }

    private static string[] ColorLabels() => BannerPatternSystem.Colors.Select(DisplayColor).ToArray();
    private static string[] PatternLabels() => BannerPatternSystem.Patterns.Select(DisplayPattern).ToArray();
    private static string DisplayColor(string value) => Title(value);
    private static string DisplayPattern(string value) => Title(value);
    private static string Title(string value) => System.Globalization.CultureInfo.InvariantCulture.TextInfo
        .ToTitleCase(value.Replace('_', ' '));
    private static int IndexOf(IReadOnlyList<string> values, string selected)
    {
        for (int i = 0; i < values.Count; i++)
            if (values[i].Equals(selected, StringComparison.Ordinal)) return i;
        return 0;
    }

    private void MoveBannerLayer(int from, int to)
    {
        var layer = _bannerLayers[from];
        _bannerLayers.RemoveAt(from);
        _bannerLayers.Insert(Math.Clamp(to, 0, _bannerLayers.Count), layer);
    }
}
