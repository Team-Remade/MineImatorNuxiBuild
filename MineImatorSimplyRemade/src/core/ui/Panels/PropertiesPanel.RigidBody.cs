using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private static readonly string[] RigidBodyTypeLabels = ["text.rigid_body.type_active", "text.rigid_body.type_passive"];
    private static readonly string[] RigidBodyShapeLabels =
    [
        "text.rigid_body.shape_box", "text.rigid_body.shape_sphere", "text.rigid_body.shape_capsule",
        "text.rigid_body.shape_cylinder", "text.rigid_body.shape_convex_hull", "text.rigid_body.shape_mesh"
    ];

    private void DrawRigidBodyModifier(SceneObject owner, int index)
    {
        var modifier = owner.Modifiers[index];
        var settings = modifier.RigidBody;
        string prefix = $"modifier.{modifier.Id}";
        bool baking = Viewport?.IsPhysicsBaking == true;
        ImGui.BeginDisabled(baking);
        bool enabled = modifier.Enabled;
        bool changed = ImGui.Checkbox(Localization.DisplayLabel("Enabled"), ref enabled);
        RegisterPropertyContext($"{prefix}.enabled");

        int type = (int)settings.Type, shape = (int)settings.Shape;
        bool animated = settings.Animated, includeChildren = settings.IncludeChildren;
        float mass = settings.Mass, friction = settings.Friction, bounciness = settings.Bounciness,
            margin = settings.Margin * 16f, linearDamping = settings.LinearDamping, angularDamping = settings.AngularDamping;
        bool deactivation = settings.Deactivation, continuous = settings.ContinuousCollision;
        int collections = settings.CollisionCollections;

        string[] typeLabels = RigidBodyTypeLabels.Select(Localization.T).ToArray();
        changed |= ImGui.Combo(Localization.T("text.rigid_body.type"), ref type, typeLabels, typeLabels.Length);
        if (ImGui.TreeNodeEx(Localization.T("text.rigid_body.settings"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (type == (int)RigidBodyType.Active)
                changed |= ImGui.DragFloat(Localization.T("text.rigid_body.mass"), ref mass, 0.05f, 0.001f, 1000000f, "%.3f",
                    ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic);
            changed |= ImGui.Checkbox(Localization.T("text.rigid_body.animated"), ref animated);
            RegisterPropertyContext($"{prefix}.rigid_body.animated");
            if (ImGui.IsItemHovered()) UiText.Tooltip(Localization.T("text.rigid_body.animated_help"));
            ImGui.TreePop();
        }
        if (ImGui.TreeNodeEx(Localization.T("text.rigid_body.collisions"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            string[] shapeLabels = RigidBodyShapeLabels.Select(Localization.T).ToArray();
            changed |= ImGui.Combo(Localization.T("text.rigid_body.shape"), ref shape, shapeLabels, shapeLabels.Length);
            changed |= ImGui.Checkbox(Localization.T("text.rigid_body.include_children"), ref includeChildren);
            changed |= ImGui.SliderFloat(Localization.T("text.rigid_body.friction"), ref friction, 0f, 1f);
            changed |= ImGui.SliderFloat(Localization.T("text.rigid_body.bounciness"), ref bounciness, 0f, 1f);
            changed |= ImGui.DragFloat(Localization.T("text.rigid_body.margin"), ref margin, 0.01f, 0f, 16f, "%.2f",
                ImGuiSliderFlags.AlwaysClamp);
            ImGui.TextUnformatted(Localization.T("text.rigid_body.collections"));
            if (ImGui.IsItemHovered()) UiText.Tooltip(Localization.T("text.rigid_body.collections_help"));
            for (int bit = 0; bit < RigidBodySettings.CollectionCount; bit++)
            {
                if (bit % 8 != 0) ImGui.SameLine();
                bool member = (collections & (1 << bit)) != 0;
                if (member) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
                if (ImGui.Button($"{bit + 1}##collection", new Vector2(ImGui.GetFrameHeight() * 1.2f, 0)))
                {
                    int toggled = collections ^ (1 << bit);
                    // A body must stay in at least one collection.
                    if (toggled != 0) { collections = toggled; changed = true; }
                }
                if (member) ImGui.PopStyleColor();
            }
            ImGui.TreePop();
        }
        if (type == (int)RigidBodyType.Active && ImGui.TreeNodeEx(Localization.T("text.rigid_body.dynamics"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= ImGui.SliderFloat(Localization.T("text.rigid_body.linear_damping"), ref linearDamping, 0f, 1f);
            changed |= ImGui.SliderFloat(Localization.T("text.rigid_body.angular_damping"), ref angularDamping, 0f, 1f);
            changed |= ImGui.Checkbox(Localization.T("text.rigid_body.deactivation"), ref deactivation);
            changed |= ImGui.Checkbox(Localization.T("text.rigid_body.continuous"), ref continuous);
            if (ImGui.IsItemHovered()) UiText.Tooltip(Localization.T("text.rigid_body.continuous_help"));
            ImGui.TreePop();
        }
        ImGui.EndDisabled();

        if (changed)
        {
            var updated = settings with
            {
                Type = (RigidBodyType)Math.Clamp(type, 0, 1),
                Shape = (RigidBodyShape)Math.Clamp(shape, 0, RigidBodyShapeLabels.Length - 1),
                Animated = animated, IncludeChildren = includeChildren, Mass = mass, Friction = friction,
                Bounciness = bounciness, Margin = margin / 16f, LinearDamping = linearDamping,
                AngularDamping = angularDamping, Deactivation = deactivation, ContinuousCollision = continuous,
                CollisionCollections = collections
            };
            // Active bodies cannot use Mesh shapes in Jolt; switch to the closest supported shape instead of failing.
            if (updated.Type == RigidBodyType.Active && updated.Shape == RigidBodyShape.Mesh)
                updated = updated with { Shape = RigidBodyShape.ConvexHull };
            try
            {
                owner.Modifiers[index] = (modifier with { Enabled = enabled, RigidBody = updated }).Validated();
                if (enabled != modifier.Enabled) Timeline?.RecordAutoKeyframe(owner, $"{prefix}.enabled");
                if (animated != settings.Animated) Timeline?.RecordAutoKeyframe(owner, $"{prefix}.rigid_body.animated");
                _modifierError = null;
                ProjectManager.Instance.SetDirty(true);
            }
            catch (InvalidDataException ex)
            {
                _modifierErrorOwner = owner;
                _modifierError = ex.Message;
                Console.Error.WriteLine($"Rigid body modifier edit rejected: {ex.Message}");
            }
        }

        modifier = owner.Modifiers[index];
        if (MineImatorSimplyRemade.core.ui.Panels.Viewport.RigidBodyUnsupportedReason(owner) is { } unsupported) UiText.Wrapped(unsupported);
        if (modifier.RigidBodyBake is { } bake)
        {
            if (RigidBodyEvaluation.IsStale(modifier, Timeline?.Framerate ?? 30f))
                UiText.Wrapped(Localization.T("text.rigid_body.stale"));
            else
                ImGui.TextUnformatted(Localization.Format("text.rigid_body.baked_range", bake.StartFrame,
                    bake.StartFrame + bake.Frames.Length - 1));
        }
        else if (modifier.RigidBody.Type == RigidBodyType.Passive)
            UiText.Disabled(Localization.T("text.rigid_body.passive_help"));
        DrawRigidBodyWorld();
    }

    /// <summary>Scene-wide Blender-style Rigid Body World settings and bake controls.</summary>
    private void DrawRigidBodyWorld()
    {
        if (!ImGui.TreeNodeEx(Localization.T("text.rigid_body.world"), ImGuiTreeNodeFlags.DefaultOpen)) return;
        ImGui.PushID("rigidBodyWorld");
        var world = RigidBodyEvaluation.World;
        var viewport = Viewport?.MainViewport ?? Viewport;
        bool baking = viewport?.IsPhysicsBaking == true;
        ImGui.BeginDisabled(baking);
        Vector3 gravity = new(world.GravityX, world.GravityY, world.GravityZ);
        float speed = world.Speed;
        int substeps = world.Substeps, iterations = world.SolverIterations, start = world.StartFrame;
        bool changed = ImGui.DragFloat3(Localization.T("text.rigid_body.gravity"), ref gravity, 0.1f, -1000f, 1000f, "%.2f",
            ImGuiSliderFlags.AlwaysClamp);
        changed |= ImGui.DragFloat(Localization.T("text.rigid_body.speed"), ref speed, 0.01f, 0f, 100f, "%.2f",
            ImGuiSliderFlags.AlwaysClamp);
        changed |= ImGui.SliderInt(Localization.T("text.rigid_body.substeps"), ref substeps, 1, 100);
        changed |= ImGui.SliderInt(Localization.T("text.rigid_body.iterations"), ref iterations, 1, 100);
        changed |= ImGui.DragInt(Localization.T("text.rigid_body.start_frame"), ref start, 1f, 0,
            Math.Max(0, Timeline?.MaxFrames ?? 0), "%d", ImGuiSliderFlags.AlwaysClamp);
        if (changed)
        {
            var updated = world with
            {
                GravityX = gravity.X, GravityY = gravity.Y, GravityZ = gravity.Z, Speed = speed,
                Substeps = substeps, SolverIterations = iterations, StartFrame = Math.Max(0, start)
            };
            try
            {
                updated.Validate();
                ProjectManager.Instance.Manifest.Settings.RigidBodyWorld = updated;
                ProjectManager.Instance.SetDirty(true);
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine($"Rigid body world edit rejected: {ex.Message}");
            }
        }
        ImGui.EndDisabled();
        UiText.Wrapped(Localization.T("text.rigid_body.bake_help"));
        if (viewport != null)
        {
            if (viewport.RigidBodyError is { } error) UiText.Wrapped(error);
            ImGui.BeginDisabled(baking);
            if (ImGui.Button(Localization.T("text.rigid_body.bake"))) viewport.BakeRigidBodies();
            ImGui.SameLine();
            if (ImGui.Button(Localization.T("text.rigid_body.clear_bake"))) viewport.ClearRigidBodyBake();
            ImGui.EndDisabled();
            if (viewport.IsRigidBodyBaking)
            {
                ImGui.ProgressBar(viewport.RigidBodyBakeTotalFrames == 0 ? 0f
                    : viewport.RigidBodyBakeCompletedFrames / (float)viewport.RigidBodyBakeTotalFrames);
                if (ImGui.Button(Localization.T("text.wiggle.cancel"))) viewport.CancelRigidBodyBake();
            }
        }
        ImGui.PopID();
        ImGui.TreePop();
    }
}
