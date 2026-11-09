using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private bool DrawWatermarkSettings()
    {
        if (!ImGui.TreeNode(Localization.Label("text.panel.properties.watermark", "Watermark")))
            return false;

        bool changed = ImGui.Checkbox(Localization.Label("text.panel.properties.enabled", "Enabled##watermark"), ref WatermarkEnabled);
        RegisterProjectPropertyContext(nameof(WatermarkEnabled));
        if (WatermarkEnabled)
        {
            string label = string.IsNullOrEmpty(WatermarkImagePath)
                ? Localization.T("text.panel.properties.none")
                : TextureSheetPicker.Label(WatermarkImagePath);
            bool open = ImGui.BeginCombo(Localization.Label("text.panel.properties.watermark_image", "Image##watermark"), label);
            RegisterProjectPropertyContext(nameof(WatermarkImagePath));
            if (open)
            {
                if (ImGui.Selectable(Localization.T("text.panel.properties.none"), string.IsNullOrEmpty(WatermarkImagePath)))
                    AssignWatermarkImage("");
                foreach (var asset in GetBackgroundImageAssets())
                {
                    string path = ProjectManager.Instance.GetAssetFullPath(asset);
                    if (ImGui.Selectable(asset.DisplayName + "##" + path,
                        ProjectManager.Instance.AssetMatchesPath(asset, ProjectManager.Instance.ResolveAssetPath(WatermarkImagePath))))
                        ApplySheetSelection(path, AssignWatermarkImage);
                }
                ImGui.EndCombo();
            }
            if (ImGui.Button(Localization.Label("text.panel.properties.load_new_texture", "Load new texture..."), new Vector2(-1, 0)))
                RequestTextureImport(AssignWatermarkImage);
            DrawTextureSheetPicker("WatermarkSheet", WatermarkImagePath, AssignWatermarkImage);
            if (!string.IsNullOrEmpty(_pbrImportError))
                UiText.Wrapped(_pbrImportError);

            string[] horizontal =
            [
                Localization.T("text.panel.properties.watermark_left"),
                Localization.T("text.panel.properties.watermark_center"),
                Localization.T("text.panel.properties.watermark_right")
            ];
            string[] vertical =
            [
                Localization.T("text.panel.properties.watermark_top"),
                Localization.T("text.panel.properties.watermark_center"),
                Localization.T("text.panel.properties.watermark_bottom")
            ];
            changed |= ImGui.Combo(Localization.Label("text.panel.properties.watermark_horizontal", "Horizontal Alignment##watermark"),
                ref WatermarkHorizontalAlignment, horizontal, horizontal.Length);
            RegisterProjectPropertyContext(nameof(WatermarkHorizontalAlignment), 0, 2);
            changed |= ImGui.Combo(Localization.Label("text.panel.properties.watermark_vertical", "Vertical Alignment##watermark"),
                ref WatermarkVerticalAlignment, vertical, vertical.Length);
            RegisterProjectPropertyContext(nameof(WatermarkVerticalAlignment), 0, 2);
            changed |= InputFloatEditor(Localization.Label("text.panel.properties.watermark_padding", "Padding (px)##watermark"),
                ref WatermarkPadding, 1f, 0f, 100000f, "%.1f");
            RegisterProjectPropertyContext(nameof(WatermarkPadding), 0, 100000);
            changed |= PercentageSlider(Localization.Label("text.panel.properties.watermark_scale", "Scale##watermark"),
                ref WatermarkScale, 1f, 2000f);
            RegisterProjectPropertyContext(nameof(WatermarkScale), 0.01, 20);
            changed |= PercentageSlider(Localization.Label("text.panel.properties.watermark_opacity", "Opacity##watermark"),
                ref WatermarkOpacity, 0f, 100f);
            RegisterProjectPropertyContext(nameof(WatermarkOpacity), 0, 1);
        }
        ImGui.TreePop();
        return changed;
    }

    private void AssignWatermarkImage(string path)
    {
        WatermarkImagePath = ProjectManager.Instance.ToAssetReference(path);
        _pbrImportError = "";
        WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
        ProjectManager.Instance.SetDirty(true);
    }
}
