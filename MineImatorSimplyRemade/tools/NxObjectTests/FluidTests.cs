using GlmSharp;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;
using System.Reflection;

internal static partial class Program
{
    private static unsafe void FluidGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization failed");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile,
            Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(96, 96, "Fluid renderer smoke test", null, null);
        try
        {
            Check(window != null, "OpenGL context creation failed");
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            using var ubo = new SceneUniformBuffer(gl);
            Mesh.SceneUBO = ubo;
            ubo.Upload(mat4.Identity, new vec3(0.1f), vec3.UnitY, vec3.Zero, vec3.UnitY, vec3.Zero,
                vec3.UnitY, vec3.Zero, 0, false, false, false);
            using var preview = new PreviewRenderer(gl, 192) { Distance = 8f, AutoRotateSpeed = 0f };
            var viewport = new Viewport();
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };

            foreach (FluidRenderMode mode in Enum.GetValues<FluidRenderMode>())
            {
                var fluid = spawn.SpawnFluidObject($"graphics {mode}", new FluidSettings
                {
                    ParticleCount = 48,
                    RenderMode = mode,
                    SurfaceResolution = 16,
                    InitialVelocity = new vec3(0.4f, 1f, 0f)
                })!;
                try
                {
                    preview.Render([], "fluid baseline", 0);
                    byte[] baseline = ReadPixels();
                    fluid.EvaluateGeometry(0f);
                    byte[] first = Render();
                    int changedPixels = 0;
                    for (int i = 0; i < first.Length; i += 4)
                        if (first[i] != baseline[i] || first[i + 1] != baseline[i + 1] ||
                            first[i + 2] != baseline[i + 2])
                            changedPixels++;
                    Check(changedPixels > 32, $"{mode} renderer produced {changedPixels} visible pixels");
                    Check(fluid.Visuals[0].Vertices.Count > 0, $"{mode} geometry must not be empty");
                    fluid.EvaluateGeometry(0.8f);
                    byte[] animated = Render();
                    Check(!animated.SequenceEqual(first), $"{mode} geometry must animate");
                    fluid.EvaluateGeometry(0f);
                    Check(first.SequenceEqual(Render()), $"{mode} renderer must reproduce after backward seek");
                    Equal(GLEnum.NoError, gl.GetError(), $"{mode} OpenGL error state");
                }
                finally
                {
                    foreach (var mesh in fluid.Visuals) mesh.Dispose();
                    viewport.SceneObjects.Remove(fluid);
                }
            }

            byte[] Render()
            {
                var fluid = viewport.SceneObjects.OfType<FluidSceneObject>().Single();
                preview.Render(fluid.Visuals, "fluid", 0, centerMeshes: true);
                return ReadPixels();
            }
            byte[] ReadPixels()
            {
                byte[] pixels = new byte[192 * 192 * 4];
                gl.BindTexture(GLEnum.Texture2D, preview.ColorTexture);
                fixed (byte* pointer = pixels)
                    gl.GetTexImage(GLEnum.Texture2D, 0, Silk.NET.OpenGL.GLEnum.Rgba,
                        GLEnum.UnsignedByte, pointer);
                gl.BindTexture(GLEnum.Texture2D, 0);
                return pixels;
            }
        }
        finally
        {
            Mesh.SceneUBO = null;
            if (window != null) glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    private static void FluidDeterminism(Fixture f)
    {
        var settings = new FluidSettings
        {
            ParticleCount = 64,
            Gravity = 8f,
            InitialVelocity = new vec3(1.5f, 2f, -0.75f),
            Iterations = 3
        };
        var first = new FluidSimulation(settings);
        first.SimulateToTime(1.25f);
        var target = first.Particles.ToArray();
        var alternate = new FluidSimulation(settings);
        alternate.SimulateToTime(3f);
        alternate.SimulateToTime(1.25f);
        for (int i = 0; i < target.Length; i++)
            Equal(target[i], alternate.Particles[i], $"Deterministic replay particle {i}");
        foreach (FluidParticle particle in target)
        {
            Check(float.IsFinite(particle.Position.LengthSqr), "Finite position");
            Check(float.IsFinite(particle.Velocity.LengthSqr), "Finite velocity");
            Check(particle.Position.x >= -settings.Bounds.x - 1e-5f &&
                  particle.Position.x <= settings.Bounds.x + 1e-5f &&
                  particle.Position.y >= -settings.Bounds.y - 1e-5f &&
                  particle.Position.y <= settings.Bounds.y + 1e-5f &&
                  particle.Position.z >= -settings.Bounds.z - 1e-5f &&
                  particle.Position.z <= settings.Bounds.z + 1e-5f, "Particles must remain inside the collision domain");
        }
        var frozen = new FluidSimulation(settings with { SimulationSpeed = 0f });
        var rest = frozen.Particles.ToArray();
        frozen.SimulateToTime(2f);
        Check(rest.SequenceEqual(frozen.Particles), "Zero simulation speed must freeze particles");
        Check(target.Any(p => p.Position.y > -settings.Bounds.y + settings.ParticleRadius), "Gravity simulation must evolve");

        var collisions = new FluidSettings
        {
            ParticleCount = 1,
            Bounds = new vec3(0.5f),
            ParticleRadius = 0.065f,
            SmoothingRadius = 0.2f,
            Pressure = 0f,
            MeshCollisions = true,
            Bounce = 0f
        };
        ClothTriangle[] floor =
        [
            new(new vec3(-1f, -0.48f, -1f), new vec3(1f, -0.48f, -1f), new vec3(1f, -0.48f, 1f)),
            new(new vec3(-1f, -0.48f, -1f), new vec3(1f, -0.48f, 1f), new vec3(-1f, -0.48f, 1f))
        ];
        var colliding = new FluidSimulation(collisions);
        colliding.SimulateToTime(0.5f, floor);
        var noCollision = new FluidSimulation(collisions with { MeshCollisions = false });
        noCollision.SimulateToTime(0.5f, floor);
        Check(colliding.Particles[0].Position.y > noCollision.Particles[0].Position.y,
            $"Scene mesh collision must keep particles above a floor: {colliding.Particles[0].Position.y} vs {noCollision.Particles[0].Position.y}");
        Check(colliding.Particles[0].Position.y >= -0.4151f,
            "Particle radius must be respected against scene triangles");
    }

    private static void FluidLimits(Fixture f)
    {
        FluidSettings valid = new();
        Reject(() => (valid with { ParticleCount = 0 }).Validated(), "Zero particle count must fail");
        Reject(() => (valid with { ParticleCount = FluidSettings.MaxParticles + 1 }).Validated(), "Particle cap must fail");
        Reject(() => (valid with { SurfaceResolution = 33 }).Validated(), "Surface resolution cap must fail");
        Reject(() => (valid with { Bounds = new vec3(1f, float.NaN, 1f) }).Validated(), "Nonfinite bounds must fail");
        Reject(() => (valid with { InitialVelocity = new vec3(float.PositiveInfinity, 0, 0) }).Validated(),
            "Nonfinite initial velocity must fail");
        Reject(() => (valid with { Pressure = -0.1f }).Validated(), "Negative pressure must fail");
        Reject(() => (valid with { CollisionFriction = 1.1f }).Validated(),
            "Collision friction must stay normalized");
        Equal(FluidSettings.MaxParticles,
            (valid with { ParticleCount = FluidSettings.MaxParticles }).Validated().ParticleCount,
            "Maximum supported particle count");
    }

    private static void FluidPersistence(Fixture f)
    {
        var settings = new FluidSettings
        {
            ParticleCount = 144,
            Seed = 42,
            Bounds = new vec3(3f, 2f, 1f),
            Pressure = 1.1f,
            RenderMode = FluidRenderMode.Particles,
            MeshCollisions = true,
            CollisionFriction = 0.4f,
            ShowBounds = false,
            SurfaceResolution = 24,
            SurfaceRadius = 0.5f,
            SurfaceThreshold = 0.22f
        };
        var fluid = new FluidSceneObject { Name = "Water test", Settings = settings };
        var (manifest, _) = f.Roundtrip(fluid);
        Equal(settings, manifest.Root.Fluid!.ToRuntime(), "Source-generated fluid settings roundtrip");
        var viewport = new Viewport();
        var spawn = new SpawnMenu { Viewport = viewport };
        var imported = ProjectSceneSerializer.SpawnObjectFromEntry(manifest.Root, viewport, spawn) as FluidSceneObject;
        Check(imported != null, "Import must preserve fluid runtime type");
        Equal(settings, imported!.Settings, "Imported fluid settings");
        var duplicateMethod = typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var clone = (FluidSceneObject)duplicateMethod.Invoke(new SceneTree(), [fluid, new Dictionary<string, string>()])!;
        Equal(settings, clone.Settings, "Duplicate fluid settings");

        var fingerprint = typeof(ProjectSceneSerializer).Assembly
            .GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { fluid }])!;
        ulong before = Hash();
        fluid.Settings = settings with { SurfaceThreshold = 0.3f };
        Check(before != Hash(), "Fluid renderer settings participate in dirty detection");
    }
}
