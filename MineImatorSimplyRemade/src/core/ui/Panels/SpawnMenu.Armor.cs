using System.Numerics;
using System.Text.Json;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemadeNuxi.core.objs;
using NativeFileDialogSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class SpawnMenu
{
    private CharacterEntry? _armorEditorEntry;
    private ArmorPieceSelection[] _armorPieceSelections = [];
    private bool _armorEditorVisible;
    private bool _armorEditorRequested;
    private bool _armorPreviewDirty;
    private PreviewRenderer? _armorPreviewRenderer;
    private SceneObject? _armorPreviewRoot;
    private MiModel? _armorPreviewModel;
    private string? _armorEditorError;

    private void OpenArmorEditor(CharacterEntry entry)
    {
        _armorEditorEntry = entry;
        int variant = Math.Clamp(_selectedCharTextureIndex, 0, entry.TextureVariants.Count - 1);
        _armorPieceSelections = ArmorModelBuilder.Pieces
            .Select(_ => new ArmorPieceSelection(variant, _customCharTexturePath)).ToArray();
        _armorEditorVisible = _armorEditorRequested = _armorPreviewDirty = true;
        _armorEditorError = null;
    }

    private unsafe void RenderArmorEditor()
    {
        string title = Localization.Label("text.panel.spawn.armor_editor", "Armor Editor") + "###ArmorEditor";
        if (_armorEditorRequested)
        {
            ImGui.OpenPopup(title);
            _armorEditorRequested = false;
        }
        ImGui.SetNextWindowSize(new Vector2(700f, 450f) * UiTheme.Scale, ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(title, ref _armorEditorVisible)) return;

        float previewWidth = Math.Min(280f * UiTheme.Scale, ImGui.GetContentRegionAvail().X * 0.45f);
        float footer = ImGui.GetFrameHeightWithSpacing() * 2f;
        float height = Math.Max(100f, ImGui.GetContentRegionAvail().Y - footer);
        ImGui.BeginChild("##armorPreview", new Vector2(previewWidth, height), ImGuiChildFlags.Borders);
        UpdateArmorEditorPreview();
        if (_armorPreviewRenderer != null)
        {
            float size = Math.Min(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y);
            ImGui.Image(new ImTextureRef(texId: (ulong)_armorPreviewRenderer.ColorTexture),
                new Vector2(size), new Vector2(0, 1), new Vector2(1, 0));
            if (ImGui.IsItemHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var delta = ImGui.GetIO().MouseDelta;
                _armorPreviewRenderer.Orbit(delta.X * 0.01f, -delta.Y * 0.01f);
            }
        }
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("##armorOptions", new Vector2(0, height));
        if (_armorEditorEntry != null)
        {
            for (int slot = 0; slot < _armorPieceSelections.Length; slot++)
            {
                ImGui.PushID(slot);
                ImGui.TextUnformatted(Localization.T("text.panel.spawn.armor_" + ArmorModelBuilder.Pieces[slot]));
                var selection = _armorPieceSelections[slot];
                string[] options = new[] { Localization.T("text.common.none") }
                    .Concat(_armorEditorEntry.TextureVariants.Select(variant => variant.Name)).ToArray();
                int index = selection.VariantIndex + 1;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##variant", ref index, options, options.Length))
                {
                    selection = selection with { VariantIndex = index - 1 };
                    _armorPieceSelections[slot] = selection;
                    _armorPreviewDirty = true;
                }
                if (selection.VariantIndex >= 0 &&
                    _armorEditorEntry.TextureVariants[selection.VariantIndex].IsCustom)
                {
                    if (ImGui.Button(Localization.Label("text.panel.spawn.browse", "Browse...")))
                    {
                        var result = Dialog.FileOpen("png,jpg,jpeg,tga,bmp");
                        if (result.IsOk && !string.IsNullOrEmpty(result.Path))
                        {
                            _armorPieceSelections[slot] = selection with { CustomTexturePath = result.Path };
                            _armorPreviewDirty = true;
                        }
                    }
                    UiText.Wrapped(string.IsNullOrEmpty(selection.CustomTexturePath)
                        ? Localization.T("text.panel.spawn.no_file_chosen") : Path.GetFileName(selection.CustomTexturePath));
                }
                ImGui.Spacing();
                ImGui.PopID();
            }
        }
        if (_armorEditorError != null) UiText.Wrapped(_armorEditorError);
        if (_armorPieceSelections.All(selection => selection.VariantIndex < 0))
            UiText.Wrapped(Localization.T("text.panel.spawn.armor_select_piece"));
        ImGui.EndChild();
        ImGui.Separator();
        ImGui.BeginDisabled(_armorPreviewDirty || _armorPreviewModel == null ||
            _armorEditorError != null || _armorPieceSelections.All(selection => selection.VariantIndex < 0));
        if (ImGui.Button(Localization.Label("text.panel.spawn.spawn", "Spawn")))
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SimplyRemadeNuxi", "Armor");
                string path = ArmorModelBuilder.Save(_armorPreviewModel!, directory);
                ImGui.CloseCurrentPopup();
                _armorEditorVisible = false;
                SpawnWithCursor(() =>
                {
                    var model = SpawnCustomModelFromPath(path);
                    if (model != null)
                    {
                        model.SpawnCategory = "Equipment";
                        model.InheritPose = true;
                    }
                    return model;
                });
                _isOpen = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _armorEditorError = ex.Message;
                Console.Error.WriteLine($"Armor editor: {ex.Message}");
            }
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(Localization.Label("text.common.cancel", "Cancel")))
        {
            _armorEditorVisible = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
        if (!ImGui.IsPopupOpen(title)) _armorEditorVisible = false;
    }

    private void UpdateArmorEditorPreview()
    {
        if (Gl == null || _armorEditorEntry == null) return;
        if (_armorPreviewRenderer == null)
        {
            _armorPreviewRenderer = new PreviewRenderer(Gl) { Distance = 3.5f };
            _armorPreviewRenderer.Initialize();
        }
        if (_armorPreviewDirty)
        {
            if (_armorPreviewRoot != null) DisposeModelGeometry(_armorPreviewRoot);
            _armorPreviewRoot = null;
            _armorPreviewModel = null;
            _armorEditorError = null;
            _armorPreviewDirty = false;
            try
            {
                _armorPreviewModel = ArmorModelBuilder.Build(_armorEditorEntry, _armorPieceSelections);
                _armorPreviewRoot = MineImatorLoader.Instance.CreateCharacterFromModel(_armorPreviewModel)
                    ?? throw new InvalidDataException("Could not create the armor preview.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                _armorEditorError = ex.Message;
                Console.Error.WriteLine($"Armor editor preview: {ex.Message}");
            }
        }
        _armorPreviewRenderer.Render([], "armor-editor", Mesh.DeltaTime,
            sceneRoot: _armorPreviewRoot, centerMeshes: true, isolatedLighting: true);
    }

    private void DisposeArmorEditorPreview()
    {
        if (_armorPreviewRoot != null) DisposeModelGeometry(_armorPreviewRoot);
        _armorPreviewRoot = null;
        _armorPreviewModel = null;
        _armorPreviewRenderer?.Dispose();
        _armorPreviewRenderer = null;
    }
}
