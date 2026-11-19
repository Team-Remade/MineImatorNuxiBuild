using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private void RenderFluidProperties()
    {
        if (_currentObject is not FluidSceneObject fluid ||
            !ImGui.CollapsingHeader("Fluid Simulation", ImGuiTreeNodeFlags.DefaultOpen)) return;

        UiText.Wrapped("Deterministic position-based fluid. Seeking backward rebuilds the simulation from frame zero.");
        FluidSettings settings = fluid.Settings;
        bool changed = false;

        int count = settings.ParticleCount;
        changed |= ImGui.SliderInt("Particle Count", ref count, 1, FluidSettings.MaxParticles);
        int seed = settings.Seed;
        changed |= ImGui.InputInt("Seed", ref seed);
        float smoothing = settings.SmoothingRadius;
        changed |= ImGui.DragFloat("Interaction Radius", ref smoothing, 0.005f, 0.02f, 2f);
        float radius = settings.ParticleRadius;
        changed |= ImGui.DragFloat("Particle Radius", ref radius, 0.001f, 0.005f, 0.5f);
        float gravity = settings.Gravity;
        changed |= ImGui.DragFloat("Gravity", ref gravity, 0.05f, -100f, 100f);
        float viscosity = settings.Viscosity;
        changed |= ImGui.SliderFloat("Viscosity", ref viscosity, 0f, 1f);
        float pressure = settings.Pressure;
        changed |= ImGui.SliderFloat("Pressure", ref pressure, 0f, 2f);
        float bounce = settings.Bounce;
        changed |= ImGui.SliderFloat("Bounce", ref bounce, 0f, 1f);
        bool meshCollisions = settings.MeshCollisions;
        changed |= ImGui.Checkbox("Collide With Scene Meshes", ref meshCollisions);
        float friction = settings.CollisionFriction;
        if (meshCollisions)
            changed |= ImGui.SliderFloat("Collision Friction", ref friction, 0f, 1f);
        bool showBounds = settings.ShowBounds;
        changed |= ImGui.Checkbox("Show Simulation Bounds", ref showBounds);
        int iterations = settings.Iterations;
        changed |= ImGui.SliderInt("Solver Iterations", ref iterations, 1, 12);
        float speed = settings.SimulationSpeed;
        changed |= ImGui.SliderFloat("Simulation Speed", ref speed, 0f, 8f);
        System.Numerics.Vector3 bounds = new(settings.Bounds.x, settings.Bounds.y, settings.Bounds.z);
        changed |= ImGui.DragFloat3("Bounds", ref bounds, 0.01f, 0.1f, 50f);
        System.Numerics.Vector3 velocity = new(settings.InitialVelocity.x, settings.InitialVelocity.y, settings.InitialVelocity.z);
        changed |= ImGui.DragFloat3("Initial Velocity", ref velocity, 0.01f, -100f, 100f);
        int renderMode = (int)settings.RenderMode;
        changed |= ImGui.Combo("Renderer", ref renderMode, ["Particles", "Fluid Surface"], 2);
        int resolution = settings.SurfaceResolution;
        float surfaceRadius = settings.SurfaceRadius;
        float threshold = settings.SurfaceThreshold;
        if (renderMode == (int)FluidRenderMode.Surface)
        {
            changed |= ImGui.SliderInt("Surface Resolution", ref resolution, 8, 32);
            changed |= ImGui.DragFloat("Surface Radius", ref surfaceRadius, 0.005f, 0.02f, 2f);
            changed |= ImGui.SliderFloat("Surface Threshold", ref threshold, 0.01f, 0.99f);
        }
        bool interaction = settings.RigidBodyInteraction;
        float density = settings.Density, linearDrag = settings.LinearDrag, angularDrag = settings.AngularDrag;
        ImGui.SeparatorText(Localization.T("text.rigid_body.fluid_interaction"));
        changed |= ImGui.Checkbox(Localization.T("text.rigid_body.fluid_enabled"), ref interaction);
        if (interaction)
        {
            changed |= ImGui.DragFloat(Localization.T("text.rigid_body.fluid_density"), ref density, 10f, 0f, 100000f, "%.0f",
                ImGuiSliderFlags.AlwaysClamp);
            changed |= ImGui.DragFloat(Localization.T("text.rigid_body.fluid_linear_drag"), ref linearDrag, 0.05f, 0f, 10f, "%.2f",
                ImGuiSliderFlags.AlwaysClamp);
            changed |= ImGui.DragFloat(Localization.T("text.rigid_body.fluid_angular_drag"), ref angularDrag, 0.05f, 0f, 10f, "%.2f",
                ImGuiSliderFlags.AlwaysClamp);
            UiText.Wrapped(Localization.T("text.rigid_body.fluid_help"));
        }

        if (changed)
        {
            fluid.Settings = settings with
            {
                ParticleCount = count,
                Seed = Math.Clamp(seed, 0, 1000000),
                SmoothingRadius = smoothing,
                ParticleRadius = radius,
                Gravity = gravity,
                Viscosity = viscosity,
                Pressure = pressure,
                Bounce = bounce,
                MeshCollisions = meshCollisions,
                CollisionFriction = friction,
                ShowBounds = showBounds,
                Iterations = iterations,
                SimulationSpeed = speed,
                Bounds = new vec3(bounds.X, bounds.Y, bounds.Z),
                InitialVelocity = new vec3(velocity.X, velocity.Y, velocity.Z),
                RenderMode = (FluidRenderMode)Math.Clamp(renderMode, 0, 1),
                SurfaceResolution = resolution,
                SurfaceRadius = surfaceRadius,
                SurfaceThreshold = threshold,
                RigidBodyInteraction = interaction,
                Density = density,
                LinearDrag = linearDrag,
                AngularDrag = angularDrag
            };
            ProjectManager.Instance.SetDirty(true);
        }
        if (fluid.Bake is { } bake)
            UiText.Wrapped(fluid.HasValidBake(Timeline?.Framerate ?? 30f)
                ? Localization.Format("text.rigid_body.fluid_baked", bake.Frames.Length - 1)
                : Localization.T("text.rigid_body.fluid_stale"));
        ImGui.TextDisabled($"Active particles: {fluid.Particles.Count}");
        if (ImGui.Button("Restart Fluid"))
            fluid.ResetRuntime();
    }
}
