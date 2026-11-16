using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using TimelinePanel = MineImatorSimplyRemade.core.ui.Panels.Timeline;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private SceneObject? _modifierErrorOwner;
    private string? _modifierError;

    private void DrawModifiers(SceneObject owner)
    {
        if (owner is PathSceneObject or PathPointSceneObject ||
            !ImGui.CollapsingHeader(Localization.Label("text.modifier.title", "Modifiers"))) return;
        ImGui.PushID("modifiers");
        float buttonSize = ImGui.GetFrameHeight();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - buttonSize));
        if (ImGui.Button("+##add", new Vector2(buttonSize))) ImGui.OpenPopup("##addMenu");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Localization.T("text.modifier.add"));
        if (ImGui.BeginPopup("##addMenu"))
        {
            if (ImGui.MenuItem(Localization.Label("text.shake.title", "Shake")))
            {
                owner.Modifiers.Add(new SceneModifier { Type = SceneModifierType.Shake });
                Timeline?.RefreshFromScene();
                ProjectManager.Instance.SetDirty(true);
            }
            if (owner is not CameraSceneObject and not LightSceneObject &&
                ImGui.MenuItem(Localization.Label("text.modifier.array", "Array")))
            {
                owner.Modifiers.Add(new SceneModifier());
                Timeline?.RefreshFromScene();
                ProjectManager.Instance.SetDirty(true);
            }
            ImGui.BeginDisabled(owner.FollowPathModifier != null);
            if (ImGui.MenuItem(Localization.Label("text.path.follow", "Follow Path")))
            {
                owner.Modifiers.Insert(0, new SceneModifier { Type = SceneModifierType.FollowPath });
                Timeline?.RefreshFromScene();
                ProjectManager.Instance.SetDirty(true);
            }
            ImGui.EndDisabled();
            if (owner.Modifiers.All(m => m.Type != SceneModifierType.Cloth) &&
                ImGui.MenuItem(Localization.Label("text.cloth.title", "Cloth")))
            {
                if (string.IsNullOrEmpty(owner.ObjectId)) owner.AssignObjectId();
                owner.Modifiers.Add(new SceneModifier { Type = SceneModifierType.Cloth });
                Timeline?.RefreshFromScene();
                ProjectManager.Instance.SetDirty(true);
            }
            ImGui.EndPopup();
        }
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f * UiTheme.Scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f) * UiTheme.Scale);
        bool visible = ImGui.BeginChild("##list", Vector2.Zero,
            ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AlwaysUseWindowPadding);
        int remove = -1, move = -1, destination = -1;
        if (visible && owner.Modifiers.Count == 0) UiText.Disabled(Localization.T("text.modifier.empty"));
        if (visible)
        {
            var paths = Viewport == null ? [] : EnumeratePaths(Viewport.SceneObjects).ToArray();
            for (int i = 0; i < owner.Modifiers.Count; i++)
            {
                var modifier = owner.Modifiers[i];
                string prefix = $"modifier.{modifier.Id}";
                ImGui.PushID(i);
                if (i > 0) ImGui.Separator();
                string label = modifier.Type switch
                {
                    SceneModifierType.FollowPath => Localization.Label("text.path.follow", "Follow Path"),
                    SceneModifierType.Cloth => Localization.Label("text.cloth.title", "Cloth"),
                    SceneModifierType.Shake => Localization.Label("text.shake.title", "Shake"),
                    _ => Localization.Label("text.modifier.array", "Array")
                };
                if (ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton(Localization.DisplayLabel("Remove"))) remove = i;
                    ImGui.BeginDisabled(i == 0);
                    if (ImGui.SmallButton(Localization.Label("text.modifier.up", "Move Up"))) { move = i; destination = i - 1; }
                    ImGui.EndDisabled();
                    ImGui.SameLine();
                    ImGui.BeginDisabled(i == owner.Modifiers.Count - 1);
                    if (ImGui.SmallButton(Localization.Label("text.modifier.down", "Move Down"))) { move = i; destination = i + 1; }
                    ImGui.EndDisabled();
                    bool enabled = modifier.Enabled;
                    if (modifier.Type == SceneModifierType.FollowPath)
                    {
                        if (ImGui.Checkbox(Localization.DisplayLabel("Enabled"), ref enabled))
                        {
                            owner.Modifiers[i] = modifier with { Enabled = enabled };
                            owner.SetEvaluatedPathFrame(null);
                            Timeline?.RecordAutoKeyframe(owner, $"{prefix}.enabled");
                            ProjectManager.Instance.SetDirty(true);
                        }
                        RegisterPropertyContext($"{prefix}.enabled");
                        DrawFollowPathModifier(owner);
                    }
                    else if (modifier.Type == SceneModifierType.Cloth)
                        DrawClothModifier(owner, i);
                    else if (modifier.Type == SceneModifierType.Shake)
                        DrawShakeModifier(owner, i);
                    else
                    {
                        int count = modifier.Count, axis = (int)modifier.Axis;
                        float distance = modifier.Distance * 16f;
                        Vector3 rotation = new(modifier.RotationX, modifier.RotationY, modifier.RotationZ);
                        Vector3 scale = new(modifier.ScaleX, modifier.ScaleY, modifier.ScaleZ);
                        bool changed = ImGui.Checkbox(Localization.DisplayLabel("Enabled"), ref enabled);
                        RegisterPropertyContext($"{prefix}.enabled");
                        changed |= ImGui.DragInt(Localization.Label("text.modifier.count", "Count (Including Original)"), ref count, 1f, 1, SceneModifier.MaxInstances);
                        RegisterPropertyContext($"{prefix}.count");
                        string[] axes = ["X", "Y", "Z"];
                        bool radialEnabled = modifier.RadialEnabled;
                        int radialAxis = (int)modifier.RadialAxis;
                        float radialRadius = modifier.RadialRadius * 16f;
                        if (ImGui.TreeNodeEx(Localization.Label("text.modifier.radial", "Radial"), ImGuiTreeNodeFlags.DefaultOpen))
                        {
                            changed |= ImGui.Checkbox(Localization.Label("text.modifier.radial_enabled", "Enable Radial"), ref radialEnabled);
                            RegisterPropertyContext($"{prefix}.radial.enabled");
                            ImGui.BeginDisabled(!radialEnabled);
                            changed |= ImGui.Combo(Localization.Label("text.modifier.radial_axis", "Radial Axis"), ref radialAxis, axes, axes.Length);
                            RegisterPropertyContext($"{prefix}.radial.axis");
                            changed |= ImGui.DragFloat(Localization.Label("text.modifier.radial_radius", "Radius"), ref radialRadius, 0.1f, 0f, float.MaxValue);
                            RegisterPropertyContext($"{prefix}.radial.radius");
                            ImGui.EndDisabled();
                            UiText.Wrapped(Localization.T("text.modifier.radial_help"));
                            ImGui.TreePop();
                        }
                        ImGui.BeginDisabled(radialEnabled);
                        changed |= ImGui.Combo(Localization.Label("text.modifier.axis", "Axis"), ref axis, axes, axes.Length);
                        RegisterPropertyContext($"{prefix}.axis");
                        changed |= ImGui.DragFloat(Localization.Label("text.modifier.distance", "Distance Between Copies"), ref distance, 0.1f);
                        RegisterPropertyContext($"{prefix}.distance");
                        ImGui.EndDisabled();
                        changed |= ImGui.DragFloat3(Localization.Label("text.modifier.rotation", "Rotation Per Copy (XYZ)"), ref rotation, 0.5f);
                        RegisterPropertyContext($"{prefix}.rotation.x", $"{prefix}.rotation.y", $"{prefix}.rotation.z");
                        changed |= ImGui.DragFloat3(Localization.Label("text.modifier.scale", "Scale Multiplier Per Copy (XYZ)"), ref scale, 0.01f);
                        RegisterPropertyContext($"{prefix}.scale.x", $"{prefix}.scale.y", $"{prefix}.scale.z");
                        string pathId = modifier.PathObjectId;
                        var selected = paths.FirstOrDefault(path => path.ObjectId == pathId);
                        string preview = pathId.Length == 0 ? Localization.T("text.path.none") :
                            selected?.GetDisplayName() ?? Localization.DisplayText("Missing object");
                        ImGui.BeginDisabled(radialEnabled);
                        if (ImGui.BeginCombo(Localization.Label("text.path.target", "Path Object"), preview))
                        {
                            if (ImGui.Selectable(Localization.T("text.path.none"), pathId.Length == 0)) { pathId = ""; changed = true; }
                            foreach (var path in paths)
                            {
                                ImGui.BeginDisabled(ModifierEvaluation.IsWithin(path, owner));
                                if (ImGui.Selectable($"{path.GetDisplayName()}##{path.ObjectId}", path == selected))
                                { pathId = path.ObjectId; changed = true; }
                                ImGui.EndDisabled();
                            }
                            ImGui.EndCombo();
                        }
                        RegisterPropertyContext($"{prefix}.target");
                        ImGui.EndDisabled();
                        if (changed)
                        {
                            try
                            {
                                owner.Modifiers[i] = (modifier with
                                {
                                    Enabled = enabled,
                                    Count = Math.Clamp(count, 1, SceneModifier.MaxInstances),
                                    Axis = (ArrayAxis)axis,
                                    Distance = distance / 16f,
                                    RadialEnabled = radialEnabled,
                                    RadialAxis = (ArrayAxis)radialAxis,
                                    RadialRadius = radialRadius / 16f,
                                    RotationX = rotation.X,
                                    RotationY = rotation.Y,
                                    RotationZ = rotation.Z,
                                    ScaleX = scale.X,
                                    ScaleY = scale.Y,
                                    ScaleZ = scale.Z,
                                    PathObjectId = pathId
                                }).Validated();
                                var updated = owner.Modifiers[i];
                                foreach (var channel in TimelinePanel.ArrayChannels)
                                    if (!Equals(TimelinePanel.GetModifierValue(modifier, channel.Channel),
                                            TimelinePanel.GetModifierValue(updated, channel.Channel)))
                                        Timeline?.RecordAutoKeyframe(owner, $"{prefix}.{channel.Channel}");
                                _modifierError = null;
                                ProjectManager.Instance.SetDirty(true);
                            }
                            catch (InvalidDataException ex)
                            {
                                _modifierErrorOwner = owner;
                                _modifierError = ex.Message;
                                Console.Error.WriteLine($"Array modifier edit rejected: {ex.Message}");
                            }
                        }
                        selected = paths.FirstOrDefault(path => path.ObjectId == pathId);
                        if (!radialEnabled && pathId.Length != 0 && (selected == null || ModifierEvaluation.IsWithin(selected, owner) || selected.Samples.Count < 2))
                            UiText.Wrapped(Localization.T("text.modifier.path_unavailable"));
                        UiText.Wrapped(Localization.T("text.modifier.help"));
                    }
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }

        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        bool combineChildren = owner.CombineChildren;
        ImGui.BeginDisabled(!combineChildren && owner.Children.Count == 0);
        if (ImGui.Checkbox(Localization.Label("text.modifier.combine_children", "Combine Children"), ref combineChildren))
        {
            owner.CombineChildren = combineChildren;
            MineImatorSimplyRemadeNuxi.core.SelectionManager.Instance?.ClearSelection();
            MineImatorSimplyRemadeNuxi.core.SelectionManager.Instance?.SelectObject(owner);
            ProjectManager.Instance.SetDirty(true);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.Tooltip(Localization.T("text.modifier.combine_children_help"));
        if (owner.GetCombinedGeometryOwner() != owner)
            UiText.Wrapped(Localization.T("text.modifier.combined_source_help"));
        if (_modifierErrorOwner == owner && _modifierError != null) UiText.Wrapped(_modifierError);
        if (remove >= 0)
        {
            string prefix = $"modifier.{owner.Modifiers[remove].Id}.";
            foreach (string path in owner.Keyframes.Keys.Where(path => path.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                owner.Keyframes.Remove(path);
            if (owner.Modifiers[remove].Type == SceneModifierType.FollowPath)
            {
                owner.Keyframes.Remove("path_follow.target");
                owner.Keyframes.Remove("path_follow.offset");
                owner.SetEvaluatedPathFrame(null);
            }
            owner.Modifiers.RemoveAt(remove);
        }
        else if (move >= 0)
        {
            var modifier = owner.Modifiers[move];
            owner.Modifiers.RemoveAt(move);
            owner.Modifiers.Insert(destination, modifier);
        }
        if (remove >= 0 || move >= 0)
        {
            _modifierError = null;
            Timeline?.RefreshFromScene();
            ProjectManager.Instance.SetDirty(true);
        }
        ImGui.PopID();
    }

    private void DrawShakeModifier(SceneObject owner, int index)
    {
        var modifier = owner.Modifiers[index];
        string prefix = $"modifier.{modifier.Id}";
        bool enabled = modifier.Enabled;
        var settings = modifier.Shake;
        bool position = settings.PositionEnabled, rotation = settings.RotationEnabled, bend = settings.BendEnabled;
        bool automatic = settings.AutomaticOffset;
        float positionPower = settings.PositionPower, rotationPower = settings.RotationPower, bendPower = settings.BendPower;
        float intensity = settings.Intensity * 100f, speed = settings.Speed * 100f;
        float offset = settings.Offset, influence = settings.KeyframeInfluence * 100f;
        bool changed = ImGui.Checkbox(Localization.DisplayLabel("Enabled"), ref enabled);
        RegisterPropertyContext($"{prefix}.enabled");
        changed |= ImGui.Checkbox(Localization.Label("text.shake.position", "Shake Position"), ref position);
        RegisterPropertyContext($"{prefix}.shake.position.enabled");
        changed |= ImGui.Checkbox(Localization.Label("text.shake.rotation", "Shake Rotation"), ref rotation);
        RegisterPropertyContext($"{prefix}.shake.rotation.enabled");
        bool hasBend = owner is MiBoneSceneObject { BendParameters: not null };
        if (hasBend)
        {
            changed |= ImGui.Checkbox(Localization.Label("text.shake.bend", "Shake Bend"), ref bend);
            RegisterPropertyContext($"{prefix}.shake.bend.enabled");
        }
        changed |= ImGui.DragFloat(Localization.Label("text.shake.position_power", "Position Power"), ref positionPower, 0.01f, 0f, 5f, "%.2fx");
        RegisterPropertyContext($"{prefix}.shake.position_power");
        changed |= ImGui.DragFloat(Localization.Label("text.shake.rotation_power", "Rotation Power"), ref rotationPower, 0.01f, 0f, 5f, "%.2fx");
        RegisterPropertyContext($"{prefix}.shake.rotation_power");
        if (hasBend)
        {
            changed |= ImGui.DragFloat(Localization.Label("text.shake.bend_power", "Bend Power"), ref bendPower, 0.01f, 0f, 5f, "%.2fx");
            RegisterPropertyContext($"{prefix}.shake.bend_power");
        }
        changed |= ImGui.DragFloat(Localization.Label("text.shake.intensity", "Intensity"), ref intensity, 0.1f, 0f, float.MaxValue, "%.1f%%");
        RegisterPropertyContext($"{prefix}.shake.intensity");
        changed |= ImGui.DragFloat(Localization.Label("text.shake.speed", "Speed"), ref speed, 0.1f, 0f, float.MaxValue, "%.1f%%");
        RegisterPropertyContext($"{prefix}.shake.speed");
        changed |= ImGui.DragFloat(Localization.Label("text.shake.offset", "Offset"), ref offset, 0.1f);
        RegisterPropertyContext($"{prefix}.shake.offset");
        changed |= ImGui.Checkbox(Localization.Label("text.shake.automatic", "Automatic Offset"), ref automatic);
        RegisterPropertyContext($"{prefix}.shake.automatic.enabled");
        changed |= ImGui.DragFloat(Localization.Label("text.shake.influence", "Keyframe Influence"), ref influence, 1f, -100f, 100f, "%.1f%%");
        RegisterPropertyContext($"{prefix}.shake.influence");
        if (changed)
        {
            try
            {
                var updated = (modifier with
                {
                    Enabled = enabled,
                    Shake = settings with
                    {
                        PositionEnabled = position,
                        RotationEnabled = rotation,
                        BendEnabled = bend,
                        PositionPower = positionPower,
                        RotationPower = rotationPower,
                        BendPower = bendPower,
                        Intensity = intensity / 100f,
                        Speed = speed / 100f,
                        Offset = offset,
                        AutomaticOffset = automatic,
                        KeyframeInfluence = influence / 100f
                    }
                }).Validated();
                owner.Modifiers[index] = updated;
                foreach (var channel in TimelinePanel.ShakeChannels)
                    if (!Equals(TimelinePanel.GetModifierValue(modifier, channel.Channel),
                            TimelinePanel.GetModifierValue(updated, channel.Channel)))
                        Timeline?.RecordAutoKeyframe(owner, $"{prefix}.{channel.Channel}");
                _modifierError = null;
                ProjectManager.Instance.SetDirty(true);
            }
            catch (InvalidDataException ex)
            {
                _modifierErrorOwner = owner;
                _modifierError = ex.Message;
                Console.Error.WriteLine($"Shake modifier edit rejected: {ex.Message}");
            }
        }
        UiText.Wrapped(Localization.T("text.shake.help"));
    }
}
