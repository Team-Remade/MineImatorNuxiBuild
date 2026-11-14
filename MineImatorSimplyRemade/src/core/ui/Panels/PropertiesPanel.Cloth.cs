using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private string _clothGroupName = "Vertex group";

    private void DrawClothModifier(SceneObject owner, int index)
    {
        var modifier = owner.Modifiers[index];
        var settings = modifier.Cloth;
        bool enabled = modifier.Enabled;
        bool changed = ImGui.Checkbox(Localization.DisplayLabel("Enabled"), ref enabled);
        float gravity = settings.Gravity, damping = settings.Damping, stretch = settings.Stretch,
            bend = settings.Bend, thickness = settings.Thickness * 16;
        int substeps = settings.Substeps, iterations = settings.Iterations;
        bool collisions = settings.Collisions, self = settings.SelfCollision;
        changed |= ImGui.DragFloat(Localization.T("text.cloth.gravity"), ref gravity, 0.1f, -1000, 1000, "%.2f", ImGuiSliderFlags.AlwaysClamp);
        changed |= ImGui.SliderFloat(Localization.T("text.cloth.damping"), ref damping, 0, 1);
        changed |= ImGui.SliderFloat(Localization.T("text.cloth.stretch"), ref stretch, 0, 1);
        changed |= ImGui.SliderFloat(Localization.T("text.cloth.bend"), ref bend, 0, 1);
        changed |= ImGui.DragFloat(Localization.T("text.cloth.thickness"), ref thickness, 0.01f, 0.0016f, 16, "%.3f", ImGuiSliderFlags.AlwaysClamp);
        changed |= ImGui.SliderInt(Localization.T("text.cloth.substeps"), ref substeps, 1, 16);
        changed |= ImGui.SliderInt(Localization.T("text.cloth.iterations"), ref iterations, 1, 32);
        changed |= ImGui.Checkbox(Localization.T("text.cloth.collisions"), ref collisions);
        changed |= ImGui.Checkbox(Localization.T("text.cloth.self_collision"), ref self);
        if (changed)
        {
            settings = settings with
            {
                Gravity = gravity, Damping = damping, Stretch = stretch, Bend = bend, Thickness = thickness / 16,
                Substeps = substeps, Iterations = iterations, Collisions = collisions, SelfCollision = self
            };
            try
            {
                owner.Modifiers[index] = (modifier with { Enabled = enabled, Cloth = settings }).Validated();
                _modifierError = null;
                ProjectManager.Instance.SetDirty(true);
            }
            catch (InvalidDataException ex)
            {
                settings = modifier.Cloth;
                _modifierErrorOwner = owner;
                _modifierError = ex.Message;
                Console.Error.WriteLine($"Cloth modifier edit rejected: {ex.Message}");
            }
        }
        if (MineImatorSimplyRemade.core.ui.Panels.Viewport.ClothUnsupportedReason(owner) is { } unsupported) UiText.Wrapped(unsupported);
        if (Viewport?.ClothError is { } error) UiText.Wrapped(error);
        if (Viewport == null) return;
        UiText.Wrapped(Localization.T("text.cloth.bake_help"));
        if (owner.Modifiers[index].ClothBake is { } bake)
            ImGui.TextUnformatted(Localization.Format("text.cloth.baked_range", bake.Frames.Length - 1));
        ImGui.BeginDisabled(!enabled || Viewport.IsClothBaking);
        if (ImGui.Button(Localization.T("text.cloth.bake"))) Viewport.BakeCloth();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(Viewport.IsClothBaking);
        if (ImGui.Button(Localization.T("text.cloth.clear_bake"))) Viewport.ClearClothBake();
        ImGui.EndDisabled();
        bool editing = Viewport.ClothEditObject == owner;
        if (ImGui.Checkbox(Localization.T("text.cloth.edit_vertices"), ref editing))
            Viewport.SetClothEditObject(editing ? owner : null);
        if (editing)
        {
            UiText.Wrapped(Localization.T("text.cloth.selection_help"));
            ImGui.TextUnformatted($"{Localization.T("text.cloth.selected")}: {Viewport.ClothVertexSelection.Count}");
            if (ImGui.Button(Localization.T("text.cloth.select_all"))) Viewport.SelectAllClothVertices(owner);
            ImGui.SameLine();
            if (ImGui.Button(Localization.T("text.cloth.clear_selection"))) Viewport.ClothVertexSelection.Clear();
            ImGui.InputText(Localization.T("text.cloth.group_name"), ref _clothGroupName, 128);
            ImGui.BeginDisabled(Viewport.ClothVertexSelection.Count is 0 or > ClothSimulation.MaxVertices ||
                string.IsNullOrWhiteSpace(_clothGroupName) || settings.Groups.Length >= 256);
            if (ImGui.Button(Localization.T("text.cloth.create_group")))
                Save(settings.Groups.Append(new ClothVertexGroup
                { Name = _clothGroupName.Trim(), Vertices = Selection() }).ToArray());
            ImGui.EndDisabled();
        }
        var candidates = Viewport.EnumerateClothSceneObjects()
            .SelectMany(obj => obj.Modifiers.Where(m => m.Type == SceneModifierType.Cloth)
                .SelectMany(m => m.Cloth.Groups.Select(group => (obj, group)))).ToArray();
        for (int i = 0; i < settings.Groups.Length; i++)
        {
            var group = settings.Groups[i];
            ImGui.PushID(group.Id);
            if (ImGui.TreeNodeEx($"{group.Name} ({group.Vertices.Length})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                string name = group.Name;
                if (ImGui.InputText(Localization.T("text.cloth.group_name"), ref name, 128) && !string.IsNullOrWhiteSpace(name))
                    Replace(i, group with { Name = name });
                bool pinned = group.Pinned;
                if (ImGui.Checkbox(Localization.T("text.cloth.pin"), ref pinned)) Replace(i, group with { Pinned = pinned });
                if (ImGui.Button(Localization.T("text.cloth.select_group")))
                {
                    Viewport.SetClothEditObject(owner);
                    Viewport.ClothVertexSelection.Clear();
                    Viewport.ClothVertexSelection.UnionWith(group.Vertices);
                }
                ImGui.BeginDisabled(!editing || Viewport.ClothVertexSelection.Count == 0);
                if (ImGui.Button(Localization.T("text.cloth.replace_group"))) Replace(i, group with { Vertices = Selection() });
                ImGui.EndDisabled();
                var target = candidates.FirstOrDefault(c => c.obj.ObjectId == group.SeamObjectId && c.group.Id == group.SeamGroupId);
                string preview = group.SeamObjectId.Length == 0 ? Localization.T("text.path.none") :
                    target.obj == null ? Localization.DisplayText("Missing object") : $"{target.obj.Name}: {target.group.Name}";
                if (ImGui.BeginCombo(Localization.T("text.cloth.seam"), preview))
                {
                    if (ImGui.Selectable(Localization.T("text.path.none"), group.SeamObjectId.Length == 0))
                        Replace(i, group with { SeamObjectId = "", SeamGroupId = "" });
                    foreach (var candidate in candidates)
                    {
                        if (candidate.obj == owner && candidate.group.Id == group.Id) continue;
                        if (ImGui.Selectable($"{candidate.obj.Name}: {candidate.group.Name}##{candidate.obj.ObjectId}{candidate.group.Id}",
                            candidate == target))
                        {
                            if (string.IsNullOrEmpty(candidate.obj.ObjectId)) candidate.obj.AssignObjectId();
                            Replace(i, group with { SeamObjectId = candidate.obj.ObjectId, SeamGroupId = candidate.group.Id });
                        }
                    }
                    ImGui.EndCombo();
                }
                bool reverse = group.ReverseSeam;
                if (ImGui.Checkbox(Localization.T("text.cloth.reverse_seam"), ref reverse))
                    Replace(i, group with { ReverseSeam = reverse });
                UiText.Wrapped(Localization.T("text.cloth.seam_help"));
                bool remove = ImGui.SmallButton(Localization.T("text.cloth.remove_group"));
                ImGui.TreePop();
                if (remove)
                {
                    Save(settings.Groups.Where(g => g.Id != group.Id).ToArray());
                    ImGui.PopID();
                    break;
                }
            }
            ImGui.PopID();
        }
        UiText.Wrapped(Localization.T("text.cloth.help"));
        ClothVertex[] Selection() => Viewport.ClothVertexSelection.OrderBy(v => v.Mesh).ThenBy(v => v.Vertex).ToArray();
        void Save(ClothVertexGroup[] groups)
        {
            settings = settings with { Groups = groups };
            settings.Validate();
            owner.Modifiers[index] = owner.Modifiers[index] with { Cloth = settings };
            ProjectManager.Instance.SetDirty(true);
        }
        void Replace(int i, ClothVertexGroup group)
        {
            var groups = (ClothVertexGroup[])settings.Groups.Clone();
            groups[i] = group;
            Save(groups);
        }
    }
}
