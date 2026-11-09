using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private string? _particlePresetError;

    private void RenderParticlePresetProperties(ParticleSpawnerSceneObject spawner)
    {
        if (!ImGui.CollapsingHeader(Localization.Label("text.panel.properties.particles", "Particles"),
                ImGuiTreeNodeFlags.DefaultOpen)) return;
        UiText.Wrapped(Localization.T("text.particles.units"));
        bool emitting = spawner.Emitting;
        bool freeze = spawner.Freeze;
        bool changed = false;
        if (ImGui.Checkbox(Localization.Label("text.common.emitting", "Emitting"), ref emitting))
        {
            spawner.Emitting = emitting;
            Timeline?.RecordAutoKeyframe(spawner, "particle.emitting");
            changed = true;
        }
        RegisterPropertyContext("particle.emitting");
        if (ImGui.Checkbox(Localization.Label("text.particles.freeze", "Freeze"), ref freeze))
        {
            spawner.Freeze = freeze;
            Timeline?.RecordAutoKeyframe(spawner, "particle.freeze");
            changed = true;
        }
        RegisterPropertyContext("particle.freeze");
        float speed = spawner.SimulationSpeed;
        if (ImGui.DragFloat(Localization.Label("text.common.simulation_speed", "Simulation Speed"),
                ref speed, 0.01f, 0, 100))
        {
            spawner.SimulationSpeed = speed;
            Timeline?.RecordAutoKeyframe(spawner, "particle.simulation_speed");
            changed = true;
        }
        RegisterPropertyContext("particle.simulation_speed");
        int seed = spawner.Seed;
        if (ImGui.InputInt(Localization.Label("text.effects.seed", "Seed"), ref seed))
        {
            spawner.Seed = seed;
            spawner.ResetRuntime();
            changed = true;
        }
        RegisterPropertyContext("particle.custom.seed", () => spawner.Seed, value =>
        {
            spawner.Seed = (int)value;
            spawner.ResetRuntime();
        }, new ParticleSpawnerSceneObject().Seed);
        SceneObject[] targets = Viewport == null ? [] : Walk(Viewport.SceneObjects)
            .Where(obj => !obj.IsRuntimeTransient && obj != spawner).ToArray();
        string targetName = targets.FirstOrDefault(obj => obj.ObjectId == spawner.AttractorObjectId)?.Name ??
            Localization.T("text.panel.properties.none");
        bool attractorOpen = ImGui.BeginCombo(Localization.Label("text.particles.attractor", "Attractor"), targetName);
        RegisterPropertyContext("particle.attractor_target");
        if (attractorOpen)
        {
            if (ImGui.Selectable(Localization.Label("text.panel.properties.none", "(none)"),
                    string.IsNullOrEmpty(spawner.AttractorObjectId)))
            {
                spawner.AttractorObjectId = "";
                Timeline?.RecordAutoKeyframe(spawner, "particle.attractor_target");
                changed = true;
            }
            RegisterPropertyContext("particle.attractor_force");
            foreach (SceneObject target in targets)
                if (ImGui.Selectable(target.Name + "##particleAttractor_" + target.ObjectId,
                        target.ObjectId == spawner.AttractorObjectId))
                {
                    spawner.AttractorObjectId = target.ObjectId;
                    Timeline?.RecordAutoKeyframe(spawner, "particle.attractor_target");
                    changed = true;
                }
            ImGui.EndCombo();
        }
        float force = spawner.AttractorForce;
        if (ImGui.DragFloat(Localization.Label("text.particles.attractor_force", "Attractor Force"),
                ref force, 0.01f, 0, 10000))
        {
            spawner.AttractorForce = force;
            Timeline?.RecordAutoKeyframe(spawner, "particle.attractor_force");
            changed = true;
        }

        JsonObject candidate = (JsonObject)spawner.Preset!.DeepClone();
        JsonObject settings = (JsonObject)candidate["particles"]!;
        bool parametersChanged = EditParticleParameters(settings);
        if (parametersChanged)
        {
            try
            {
                spawner.SetPreset(candidate);
                _particlePresetError = null;
                changed = true;
            }
            catch (InvalidDataException ex)
            {
                _particlePresetError = ex.Message;
                Console.Error.WriteLine($"Particle parameter edit rejected: {ex.Message}");
            }
        }
        if (changed) ProjectManager.Instance.SetDirty(true);
        if (_particlePresetError != null) UiText.Wrapped(_particlePresetError);
        UiText.Disabled(Localization.Format("text.panel.properties.active_particles", spawner.ActiveParticleCount));
        if (ImGui.Button(Localization.Label("text.panel.properties.restart_particles", "Restart Particles")))
            spawner.ResetRuntime();

        static IEnumerable<SceneObject> Walk(IEnumerable<SceneObject> objects)
        {
            foreach (SceneObject obj in objects)
            {
                yield return obj;
                foreach (SceneObject child in Walk(obj.Children)) yield return child;
            }
        }
    }

    private static bool EditParticleParameters(JsonObject obj)
    {
        bool changed = false;
        foreach (var pair in obj.ToArray())
        {
            string key = pair.Key;
            if (key is "sprite_tex_image" or "sprite_frame_width" or "sprite_frame_height" or
                "sprite_frame_start" or "sprite_frame_end" or "spawn_region_path_radius" or "text")
                continue;
            string label = Localization.DisplayLabel(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                key.Replace('_', ' ')) + "##" + key);
            ImGui.PushID(key);
            if (pair.Value is JsonArray array)
            {
                if (ImGui.TreeNode(label))
                {
                    for (int index = 0; index < array.Count; index++)
                    {
                        ImGui.PushID(index);
                        if (array[index] is JsonObject type)
                        {
                            if (ImGui.TreeNode(ParticlePreset.Text(type, "name", $"Type {index + 1}") + "##type"))
                            {
                                changed |= EditParticleParameters(type);
                                ImGui.TreePop();
                            }
                        }
                        else if (key.EndsWith("_israndom", StringComparison.Ordinal))
                        {
                            bool flag = array[index]?.GetValue<float>() != 0;
                            if (ImGui.Checkbox("XYZ"[index].ToString(), ref flag))
                            {
                                array[index] = flag ? 1f : 0f;
                                changed = true;
                            }
                        }
                        else
                        {
                            float number = array[index]?.GetValue<float>() ?? 0;
                            if (ImGui.DragFloat("XYZ"[index].ToString(), ref number, 0.1f))
                            {
                                array[index] = number;
                                changed = true;
                            }
                        }
                        ImGui.PopID();
                    }
                    ImGui.TreePop();
                }
            }
            else if (pair.Value is JsonValue value)
            {
                if (value.TryGetValue<bool>(out bool flag))
                {
                    if (ImGui.Checkbox(label, ref flag)) { obj[key] = flag; changed = true; }
                }
                else if (key.EndsWith("_israndom", StringComparison.Ordinal))
                {
                    bool random = ParticlePreset.Flag(obj, key);
                    if (ImGui.Checkbox(label, ref random)) { obj[key] = random; changed = true; }
                }
                else if (value.TryGetValue<float>(out float number))
                {
                    if (ImGui.DragFloat(label, ref number, 0.05f)) { obj[key] = number; changed = true; }
                }
                else if (value.TryGetValue<string>(out string? text))
                {
                    string[]? options = key switch
                    {
                        "sprite_template" => ["generic", "big_smoke", "effect", "explosion", "spark", "splash",
                            "flame", "firefly", "flash", "glow"],
                        "spawn_region_type" => ["sphere", "cube", "box"],
                        "bounding_box_type" => ["none", "ground", "spawn", "custom"],
                        _ => null
                    };
                    bool readOnly = key is "id" or "temp" or "temp_type" or "sprite_tex" or "sprite_template_tex";
                    if (options != null && ImGui.BeginCombo(label, text))
                    {
                        foreach (string option in options)
                            if (ImGui.Selectable(option, text == option)) { obj[key] = option; changed = true; }
                        ImGui.EndCombo();
                    }
                    else if (options == null && text.StartsWith('#'))
                    {
                        vec3 rgb = ParticlePreset.Color(text);
                        Vector3 color = new(rgb.x, rgb.y, rgb.z);
                        if (ImGui.ColorEdit3(label, ref color))
                        {
                            obj[key] = $"#{(int)(color.X * 255):X2}{(int)(color.Y * 255):X2}{(int)(color.Z * 255):X2}";
                            changed = true;
                        }
                    }
                    else if (options == null && !readOnly && ImGui.InputText(label, ref text, 1024))
                    { obj[key] = text; changed = true; }
                    else if (readOnly) UiText.Disabled(label.Split("##")[0] + ": " + text);
                }
            }
            ImGui.PopID();
        }
        return changed;
    }
}
