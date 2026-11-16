using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private void DrawWiggle(SceneObject obj)
    {
        if (!ImGui.CollapsingHeader(Localization.T("text.wiggle.title"))) return;
        ImGui.PushID("wiggle");
        var viewport = Viewport?.MainViewport ?? Viewport;
        bool baking = viewport?.IsWiggleBaking == true || viewport?.IsClothBaking == true;
        ImGui.BeginDisabled(baking);
        var scene = ProjectManager.Instance.Manifest.Settings.Wiggle;
        if (ImGui.TreeNodeEx(Localization.T("text.wiggle.scene"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool enabled = scene.Enabled, loop = scene.Loop;
            int quality = scene.Quality, preroll = scene.Preroll;
            float gravity = scene.Gravity;
            bool changed = ImGui.Checkbox(Localization.T("text.wiggle.enable_scene"), ref enabled);
            changed |= ImGui.SliderInt(Localization.T("text.wiggle.quality"), ref quality, 1, 32);
            changed |= ImGui.SliderInt(Localization.T("text.wiggle.preroll"), ref preroll, 0, 600);
            changed |= ImGui.DragFloat(Localization.T("text.wiggle.scene_gravity"), ref gravity, 0.1f, -1000, 1000,
                "%.2f", ImGuiSliderFlags.AlwaysClamp);
            changed |= ImGui.Checkbox(Localization.T("text.wiggle.loop"), ref loop);
            if (changed)
            {
                ProjectManager.Instance.Manifest.Settings.Wiggle = scene with
                { Enabled = enabled, Quality = quality, Preroll = preroll, Gravity = gravity, Loop = loop };
                Changed();
            }
            ImGui.TreePop();
        }
        SceneObject rig = obj is BoneSceneObject boneRig ? WiggleSimulation.RigFor(boneRig) : obj;
        if (obj is BoneSceneObject || WiggleSimulation.Objects([obj]).OfType<BoneSceneObject>().Any())
        {
            if (ImGui.TreeNodeEx(Localization.T("text.wiggle.armature") + $" ({rig.Name})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                bool enabled = rig.WiggleRig.Enabled, mute = rig.WiggleRig.Mute, freeze = rig.WiggleRig.Freeze;
                bool changed = ImGui.Checkbox(Localization.T("text.wiggle.enable_armature"), ref enabled);
                changed |= ImGui.Checkbox(Localization.T("text.wiggle.mute"), ref mute);
                changed |= ImGui.Checkbox(Localization.T("text.wiggle.freeze"), ref freeze);
                if (changed) { rig.WiggleRig = rig.WiggleRig with { Enabled = enabled, Mute = mute, Freeze = freeze }; Changed(reset: false); }
                ImGui.TreePop();
            }
        }
        if (obj is BoneSceneObject bone)
        {
            ImGui.PushID("bone");
            if (ImGui.TreeNodeEx(Localization.T("text.wiggle.bone"), ImGuiTreeNodeFlags.DefaultOpen))
            {
                var w = bone.Wiggle;
                bool enabled = w.Enabled, mute = w.Mute, freeze = w.Freeze;
                bool connected = w.Connected, chain = w.Chain;
                bool changed = ImGui.Checkbox(Localization.T("text.wiggle.enable_bone"), ref enabled);
                changed |= ImGui.Checkbox(Localization.T("text.wiggle.mute"), ref mute);
                changed |= ImGui.Checkbox(Localization.T("text.wiggle.freeze"), ref freeze);
                ImGui.BeginDisabled(bone.Parent is not BoneSceneObject);
                changed |= ImGui.Checkbox(Localization.T("text.wiggle.connected"), ref connected);
                ImGui.EndDisabled();
                if (connected) UiText.Wrapped(Localization.T("text.wiggle.connected_help"));
                ImGui.BeginDisabled(!connected);
                changed |= ImGui.Checkbox(Localization.T("text.wiggle.chain"), ref chain);
                ImGui.EndDisabled();
                if (!bone.EndOffset.HasValue)
                {
                    Vector3 tail = new(w.TailX * 16, w.TailY * 16, w.TailZ * 16);
                    if (ImGui.DragFloat3(Localization.T("text.wiggle.tail_offset"), ref tail, 0.1f, -160000, 160000,
                        "%.2f", ImGuiSliderFlags.AlwaysClamp))
                    {
                        w = w with { TailX = tail.X / 16, TailY = tail.Y / 16, TailZ = tail.Z / 16 };
                        changed = true;
                    }
                }
                else UiText.Disabled(Localization.T("text.wiggle.imported_tail"));
                var headSettings = DrawWigglePoint("text.wiggle.head", w.Head,
                    connected && bone.Parent is BoneSceneObject);
                var tailSettings = DrawWigglePoint("text.wiggle.tail", w.Tail, false);
                var bendSettings = w.Bend;
                if (bone is MiBoneSceneObject { BendParameters: not null, EndOffset: not null })
                {
                    bendSettings = DrawWigglePoint("text.wiggle.bend", w.Bend, false, bend: true);
                    UiText.Wrapped(Localization.T("text.wiggle.bend_help"));
                }
                changed |= headSettings != w.Head || tailSettings != w.Tail || bendSettings != w.Bend;
                bool collisions = w.Collisions, meshCollisions = w.MeshCollisions;
                changed |= ImGui.Checkbox(Localization.T("text.wiggle.collisions"), ref collisions);
                changed |= ImGui.Checkbox(Localization.T("text.wiggle.mesh_collisions"), ref meshCollisions);
                float radius = w.Radius * 16, friction = w.Friction, bounce = w.Bounce;
                if (collisions || meshCollisions)
                {
                    changed |= ImGui.DragFloat(Localization.T("text.wiggle.radius"), ref radius, 0.01f, 0.016f, 16000,
                        "%.3f", ImGuiSliderFlags.AlwaysClamp);
                    changed |= ImGui.SliderFloat(Localization.T("text.wiggle.friction"), ref friction, 0, 1);
                    changed |= ImGui.SliderFloat(Localization.T("text.wiggle.bounce"), ref bounce, 0, 1);
                    UiText.Wrapped(Localization.T("text.wiggle.collision_help"));
                }
                if (changed)
                {
                    bool controlsOnly = w.Enabled == enabled && w.Connected == connected && w.Chain == chain &&
                        w.Head == headSettings && w.Tail == tailSettings && w.Collisions == collisions &&
                        w.Bend == bendSettings && w.MeshCollisions == meshCollisions &&
                        w.Radius == radius / 16 && w.Friction == friction && w.Bounce == bounce && w == bone.Wiggle;
                    bone.Wiggle = w with
                    {
                        Enabled = enabled,
                        Mute = mute,
                        Freeze = freeze,
                        Connected = connected,
                        Chain = chain,
                        Head = headSettings,
                        Tail = tailSettings,
                        Bend = bendSettings,
                        Collisions = collisions,
                        MeshCollisions = meshCollisions,
                        Radius = radius / 16,
                        Friction = friction,
                        Bounce = bounce
                    };
                    Changed(reset: !controlsOnly);
                }
                if (ImGui.Button(Localization.T("text.wiggle.copy_selected")))
                {
                    foreach (var selected in SelectionManager.Instance.SelectedObjects.OfType<BoneSceneObject>())
                        selected.Wiggle = selected is MiBoneSceneObject { BendParameters: not null, EndOffset: not null }
                            ? bone.Wiggle : bone.Wiggle with { Bend = bone.Wiggle.Bend with { Enabled = false } };
                    Changed();
                }
                ImGui.SameLine();
                if (ImGui.Button(Localization.T("text.wiggle.defaults")))
                {
                    bone.Wiggle = new();
                    Changed();
                }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        if (ImGui.TreeNode(Localization.T("text.wiggle.collider")))
        {
            var c = obj.WiggleCollider;
            bool enabled = c.Enabled;
            float radius = c.Radius * 16;
            Vector3 offset = new(c.OffsetX * 16, c.OffsetY * 16, c.OffsetZ * 16);
            bool changed = ImGui.Checkbox(Localization.T("text.wiggle.enable_collider"), ref enabled);
            changed |= ImGui.DragFloat(Localization.T("text.wiggle.radius"), ref radius, 0.1f, 0.016f, 160000,
                "%.2f", ImGuiSliderFlags.AlwaysClamp);
            changed |= ImGui.DragFloat3(Localization.T("text.wiggle.collider_offset"), ref offset, 0.1f, -160000, 160000,
                "%.2f", ImGuiSliderFlags.AlwaysClamp);
            UiText.Wrapped(Localization.T("text.wiggle.collider_help"));
            if (changed)
            {
                obj.WiggleCollider = c with
                { Enabled = enabled, Radius = radius / 16, OffsetX = offset.X / 16, OffsetY = offset.Y / 16, OffsetZ = offset.Z / 16 };
                Changed();
            }
            ImGui.TreePop();
        }
        ImGui.EndDisabled();
        if (viewport != null)
        {
            ImGui.Separator();
            UiText.Wrapped(Localization.T("text.wiggle.preview_help"));
            if (obj is BoneSceneObject { WiggleBake: { } bake })
                ImGui.TextUnformatted(Localization.Format("text.wiggle.baked_range", bake.Frames.Length - 1));
            if (viewport.WiggleError is { } error) UiText.Wrapped(error);
            ImGui.BeginDisabled(baking);
            if (ImGui.Button(Localization.T("text.wiggle.bake"))) viewport.BakeWiggle();
            ImGui.SameLine();
            if (ImGui.Button(Localization.T("text.wiggle.clear_bake"))) viewport.ClearWiggleBake();
            if (ImGui.Button(Localization.T("text.wiggle.reset"))) viewport.ResetWiggle();
            ImGui.EndDisabled();
            if (viewport.IsWiggleBaking)
            {
                ImGui.ProgressBar(viewport.WiggleBakeCompletedFrames / (float)viewport.WiggleBakeTotalFrames);
                if (ImGui.Button(Localization.T("text.wiggle.cancel"))) viewport.CancelWiggleBake();
            }
        }
        ImGui.PopID();

        void Changed(bool reset = true)
        {
            if (reset) viewport?.ResetWiggle();
            ProjectManager.Instance.SetDirty(true);
        }
    }

    private static WigglePointSettings DrawWigglePoint(string label, WigglePointSettings p, bool connected, bool bend = false)
    {
        ImGui.PushID(label);
        if (ImGui.TreeNodeEx(Localization.T(label), ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.BeginDisabled(connected);
            bool enabled = p.Enabled;
            bool changed = ImGui.Checkbox(Localization.DisplayLabel("Enabled"), ref enabled);
            ImGui.BeginDisabled(!enabled);
            float mass = p.Mass, stiffness = p.Stiffness, damping = p.Damping, gravity = p.Gravity, wind = p.Wind, stretch = p.Stretch;
            changed |= ImGui.DragFloat(Localization.T("text.wiggle.mass"), ref mass, 0.01f, 0.01f, 1000, "%.2f", ImGuiSliderFlags.AlwaysClamp);
            changed |= ImGui.DragFloat(Localization.T("text.wiggle.stiffness"), ref stiffness, 1, 0, 10000, "%.1f", ImGuiSliderFlags.AlwaysClamp);
            changed |= ImGui.DragFloat(Localization.T("text.wiggle.damping"), ref damping, 0.1f, 0, 1000, "%.2f", ImGuiSliderFlags.AlwaysClamp);
            changed |= ImGui.DragFloat(Localization.T("text.wiggle.gravity"), ref gravity, 0.1f, -100, 100, "%.2f", ImGuiSliderFlags.AlwaysClamp);
            changed |= ImGui.DragFloat(Localization.T("text.wiggle.wind"), ref wind, 0.1f, -100, 100, "%.2f", ImGuiSliderFlags.AlwaysClamp);
            if (!bend) changed |= ImGui.SliderFloat(Localization.T("text.wiggle.stretch"), ref stretch, 0, 1);
            ImGui.EndDisabled();
            ImGui.EndDisabled();
            if (changed) p = p with
            { Enabled = enabled, Mass = mass, Stiffness = stiffness, Damping = damping, Gravity = gravity, Wind = wind, Stretch = stretch };
            ImGui.TreePop();
        }
        ImGui.PopID();
        return p;
    }

    internal void SampleWiggleWind(int frame, Action sample)
    {
        var saved = (WindEnabled, WindSpeed, WindStrength, WindDirection, WindDirectionalSpeed, WindDirectionalStrength);
        try
        {
            ApplyBackgroundAnimationFiltered(frame, path => path.StartsWith("Wind", StringComparison.Ordinal));
            sample();
        }
        finally
        {
            (WindEnabled, WindSpeed, WindStrength, WindDirection, WindDirectionalSpeed, WindDirectionalStrength) = saved;
        }
    }
}
