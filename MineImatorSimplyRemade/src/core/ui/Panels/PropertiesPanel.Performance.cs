using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private void RenderPerformanceSettings()
    {
        ImGui.PushID("ProjectPerformance");
        bool changed = ImGui.DragInt(Localization.Label("text.panel.properties.max_array_instances", "Maximum Array Instances"),
            ref MaxArrayInstances, 1f, 1, SceneModifier.MaxInstances, "%d", ImGuiSliderFlags.AlwaysClamp);
        RegisterProjectPropertyContext(nameof(MaxArrayInstances), 1, SceneModifier.MaxInstances);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Localization.T("text.panel.properties.max_array_instances_help"));

        changed |= ImGui.DragInt(Localization.Label("text.panel.properties.max_particles_per_spawner", "Maximum Particles per Spawner"),
            ref MaxParticlesPerSpawner, 10f, 1, ParticlePresetSimulation.SafetyLimit, "%d", ImGuiSliderFlags.AlwaysClamp);
        RegisterProjectPropertyContext(nameof(MaxParticlesPerSpawner), 1, ParticlePresetSimulation.SafetyLimit);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Localization.T("text.panel.properties.max_particles_per_spawner_help"));

        ImGui.Separator();
        changed |= ImGui.Checkbox(Localization.Label("text.panel.properties.shadows", "Shadows"), ref ShadowsEnabled);
        RegisterProjectPropertyContext(nameof(ShadowsEnabled));
        ImGui.BeginDisabled(!ShadowsEnabled);
        changed |= ShadowBufferCombo("Sun lamps", ref SunShadowBufferSize);
        RegisterProjectPropertyContext(nameof(SunShadowBufferSize));
        changed |= ShadowBufferCombo("Spot lights", ref SpotShadowBufferSize);
        RegisterProjectPropertyContext(nameof(SpotShadowBufferSize));
        changed |= ShadowBufferCombo("Point lights", ref PointShadowBufferSize);
        RegisterProjectPropertyContext(nameof(PointShadowBufferSize));
        ImGui.EndDisabled();

        changed |= ImGui.Checkbox(Localization.Label("text.panel.properties.ambient_occlusion", "Ambient Occlusion"), ref AmbientOcclusionEnabled);
        RegisterProjectPropertyContext(nameof(AmbientOcclusionEnabled));
        ImGui.BeginDisabled(!AmbientOcclusionEnabled);
        changed |= ImGui.SliderInt(Localization.Label("text.panel.properties.samples", "Samples##ao"),
            ref AmbientOcclusionSampleCount, 1, 128, "%d", ImGuiSliderFlags.AlwaysClamp);
        RegisterProjectPropertyContext(nameof(AmbientOcclusionSampleCount), 1, 128);
        ImGui.EndDisabled();
        changed |= ImGui.Checkbox(Localization.Label("text.panel.properties.indirect_lighting", "Indirect Lighting"), ref IndirectLightingEnabled);
        RegisterProjectPropertyContext(nameof(IndirectLightingEnabled));
        ImGui.TextWrapped(Localization.T("text.panel.properties.performance_help"));
        ImGui.PopID();

        if (!changed) return;
        WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
        ProjectManager.Instance.SetDirty(true);
    }
}
