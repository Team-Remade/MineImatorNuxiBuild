using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private void RenderBiomeSettings()
    {
        bool changed = false;
        string label = Biome switch
        {
            "default" => Localization.T("text.panel.properties.biome_default"),
            "custom" => Localization.T("text.panel.properties.biome_custom"),
            _ => BiomeEnvironment.Presets.First(p => p.Id == Biome).Name
        };
        if (ImGui.BeginCombo(Localization.Label("text.panel.properties.biome", "Biome"), label))
        {
            if (ImGui.Selectable(Localization.T("text.panel.properties.biome_default"), Biome == "default"))
            {
                Biome = "default";
                changed = true;
            }
            foreach (var preset in BiomeEnvironment.Presets)
                if (ImGui.Selectable(Localization.DisplayText(preset.Name), Biome == preset.Id))
                {
                    Biome = preset.Id;
                    changed = true;
                }
            if (ImGui.Selectable(Localization.T("text.panel.properties.biome_custom"), Biome == "custom"))
            {
                Biome = "custom";
                changed = true;
            }
            ImGui.EndCombo();
        }
        if (Biome == "custom")
        {
            foreach (var kind in Enum.GetValues<BiomeTintKind>())
            {
                if (kind == BiomeTintKind.None) continue;
                vec3 tint = CustomBiomeColors.GetTint(kind);
                var color = new Vector3(tint.x, tint.y, tint.z);
                string key = kind.ToString().ToLowerInvariant();
                if (ImGui.ColorEdit3(Localization.Label($"text.panel.properties.biome_{key}", kind == BiomeTintKind.DarkOak ? "Dark oak" : kind.ToString()), ref color))
                {
                    CustomBiomeColors.SetColor(kind, BiomeColors.Pack(new vec3(color.X, color.Y, color.Z)));
                    changed = true;
                }
            }
        }
        if (!changed) return;
        BiomeEnvironment.Apply(Biome, CustomBiomeColors);
        WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
        ProjectManager.Instance.SetDirty(true);
    }
}
