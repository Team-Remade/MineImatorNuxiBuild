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
                        changed |= ImGui.Combo(Localization.Label("text.modifier.axis", "Axis"), ref axis, axes, axes.Length);
                        RegisterPropertyContext($"{prefix}.axis");
                        changed |= ImGui.DragFloat(Localization.Label("text.modifier.distance", "Distance Between Copies"), ref distance, 0.1f);
                        RegisterPropertyContext($"{prefix}.distance");
                        changed |= ImGui.DragFloat3(Localization.Label("text.modifier.rotation", "Rotation Per Copy (XYZ)"), ref rotation, 0.5f);
                        RegisterPropertyContext($"{prefix}.rotation.x", $"{prefix}.rotation.y", $"{prefix}.rotation.z");
                        changed |= ImGui.DragFloat3(Localization.Label("text.modifier.scale", "Scale Multiplier Per Copy (XYZ)"), ref scale, 0.01f);
                        RegisterPropertyContext($"{prefix}.scale.x", $"{prefix}.scale.y", $"{prefix}.scale.z");
                        string pathId = modifier.PathObjectId;
                        var selected = paths.FirstOrDefault(path => path.ObjectId == pathId);
                        string preview = pathId.Length == 0 ? Localization.T("text.path.none") :
                            selected?.GetDisplayName() ?? Localization.DisplayText("Missing object");
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
                        if (pathId.Length != 0 && (selected == null || ModifierEvaluation.IsWithin(selected, owner) || selected.Samples.Count < 2))
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
}
