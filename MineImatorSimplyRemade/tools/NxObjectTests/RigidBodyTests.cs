using GlmSharp;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private const float RigidFrame = 1f / 30f;

    private static (vec3[] Points, int[] Triangles) RigidBox(vec3 center, vec3 half)
    {
        var points = new vec3[8];
        for (int i = 0; i < 8; i++)
            points[i] = center + new vec3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y,
                (i & 4) == 0 ? -half.z : half.z);
        int[] triangles =
        [
            0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4,
            2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5
        ];
        return (points, triangles);
    }

    private static RigidBodySource RigidSource(string id, RigidBodySettings settings, vec3 center, vec3 half)
    {
        var (points, triangles) = RigidBox(center, half);
        return new RigidBodySource(id, settings, mat4.Translate(center), points, triangles);
    }

    private static RigidBodySource RigidFloor(string id = "floor", float top = 0f, int collections = 1) =>
        RigidSource(id, new RigidBodySettings { Type = RigidBodyType.Passive, Shape = RigidBodyShape.Box, CollisionCollections = collections },
            new vec3(0, top - 0.5f, 0), new vec3(5, 0.5f, 5));

    private static vec3 RigidPosition(mat4 world) => new(world.m30, world.m31, world.m32);

    private static void StepRigid(RigidBodySimulation simulation, int frames, Func<int, RigidBodyTarget[]> targets,
        IReadOnlyList<RigidBodyFluid>? fluids = null, int firstFrame = 1)
    {
        for (int frame = firstFrame; frame < firstFrame + frames; frame++)
            simulation.Step(RigidFrame, targets(frame), fluids ?? [], (frame - 1) * RigidFrame);
    }

    private static RigidBodyTarget[] DynamicTargets(RigidBodySimulation simulation) =>
        Enumerable.Range(0, simulation.BodyCount).Select(i => new RigidBodyTarget(simulation.GetWorld(i), true)).ToArray();

    private static void RigidBodyFallingAndResting(Fixture f)
    {
        mat4[] Run()
        {
            using var simulation = new RigidBodySimulation(
                [RigidFloor(), RigidSource("cube", new() { Shape = RigidBodyShape.Box }, new vec3(0, 3, 0), new vec3(0.5f))],
                new RigidBodyWorldSettings());
            var frames = new List<mat4>();
            for (int frame = 1; frame <= 90; frame++)
            {
                simulation.Step(RigidFrame, DynamicTargets(simulation), [], 0);
                frames.Add(simulation.GetWorld(1));
            }
            Near(0f, RigidPosition(simulation.GetWorld(0)).y + 0.5f, "Static passive floor never moves");
            Check(simulation.GetLinearVelocity(1).Length < 0.05f, "Cube comes to rest on the floor");
            return frames.ToArray();
        }
        var first = Run();
        Check(RigidPosition(first[10]).y < 3f - 0.2f, "Gravity accelerates the active body");
        Check(MathF.Abs(RigidPosition(first[^1]).y - 0.5f) < 0.03f,
            $"Cube rests with its base on the floor: {RigidPosition(first[^1]).y}");
        Check(first.SequenceEqual(Run()), "Identical inputs bake identical frames");

        using var noGravity = new RigidBodySimulation(
            [RigidSource("cube", new() { Shape = RigidBodyShape.Box }, new vec3(0, 3, 0), new vec3(0.5f))],
            new RigidBodyWorldSettings { GravityY = 0f });
        StepRigid(noGravity, 30, _ => DynamicTargets(noGravity));
        Near(new vec3(0, 3, 0), RigidPosition(noGravity.GetWorld(0)), "Zero world gravity leaves bodies in place");

        using var slow = new RigidBodySimulation(
            [RigidSource("cube", new() { Shape = RigidBodyShape.Box }, new vec3(0, 3, 0), new vec3(0.5f))],
            new RigidBodyWorldSettings { Speed = 0f });
        StepRigid(slow, 30, _ => DynamicTargets(slow));
        Near(new vec3(0, 3, 0), RigidPosition(slow.GetWorld(0)), "Zero world speed freezes the simulation");
    }

    private static void RigidBodyShapesAndTransforms(Fixture f)
    {
        foreach (var shape in new[] { RigidBodyShape.Box, RigidBodyShape.Sphere, RigidBodyShape.Capsule,
                     RigidBodyShape.Cylinder, RigidBodyShape.ConvexHull })
        {
            using var simulation = new RigidBodySimulation(
                [RigidFloor(), RigidSource("body", new() { Shape = shape }, new vec3(0, 2, 0), new vec3(0.5f))],
                new RigidBodyWorldSettings());
            StepRigid(simulation, 90, _ => DynamicTargets(simulation));
            float y = RigidPosition(simulation.GetWorld(1)).y;
            Check(y > 0.3f && y < 0.8f, $"{shape} body lands on the floor: {y}");
        }
        using (var meshFloor = new RigidBodySimulation(
                   [RigidSource("floor", new() { Type = RigidBodyType.Passive, Shape = RigidBodyShape.Mesh },
                        new vec3(0, -0.5f, 0), new vec3(5, 0.5f, 5)),
                    RigidSource("body", new() { Shape = RigidBodyShape.ConvexHull }, new vec3(0, 2, 0), new vec3(0.5f))],
                   new RigidBodyWorldSettings()))
        {
            StepRigid(meshFloor, 90, _ => DynamicTargets(meshFloor));
            Check(MathF.Abs(RigidPosition(meshFloor.GetWorld(1)).y - 0.5f) < 0.06f, "Passive mesh shape supports active bodies");
        }
        Reject(() => new RigidBodySettings { Shape = RigidBodyShape.Mesh }.Validate(), "Active mesh shapes are rejected");
        Reject(() => new RigidBodySettings { CollisionCollections = 0 }.Validate(), "Bodies need a collision collection");
        Reject(() => new RigidBodySettings { Mass = 0f }.Validate(), "Mass must be positive");
        Reject(() => new RigidBodyWorldSettings { Substeps = 0 }.Validate(), "Substeps must be positive");

        // Rotated, scaled and flat sources keep the authored object transform as the simulation reference.
        mat4 world = mat4.Translate(new vec3(1, 2, 3)) * mat4.RotateY(0.7f) * mat4.Scale(new vec3(2, 1, 0.5f));
        var (local, triangles) = RigidBox(vec3.Zero, new vec3(0.5f));
        var points = local.Select(p => (world * new vec4(p, 1)).xyz).ToArray();
        using (var still = new RigidBodySimulation(
                   [new RigidBodySource("rotated", new() { Shape = RigidBodyShape.ConvexHull }, world, points, triangles)],
                   new RigidBodyWorldSettings { GravityY = 0f }))
        {
            mat4 result = still.GetWorld(0);
            for (int i = 0; i < 16; i++) Near(world[i], result[i], $"Initial simulated transform reproduces the object, element {i}");
        }
        var flat = RigidBox(new vec3(0, 1, 0), new vec3(1, 0, 1));
        using var plane = new RigidBodySimulation(
            [RigidFloor(), new RigidBodySource("plane", new(), mat4.Translate(new vec3(0, 1, 0)), flat.Points, flat.Triangles)],
            new RigidBodyWorldSettings());
        StepRigid(plane, 60, _ => DynamicTargets(plane));
        Check(RigidPosition(plane.GetWorld(1)).y is > -0.01f and < 0.1f, "Flat convex hull sources fall back to a thin box");
    }

    private static void RigidBodyCollectionsAndKinematics(Fixture f)
    {
        using (var collections = new RigidBodySimulation(
                   [RigidFloor(collections: 1),
                    RigidSource("other", new() { Shape = RigidBodyShape.Box, CollisionCollections = 2 }, new vec3(0, 2, 0), new vec3(0.5f)),
                    RigidSource("shared", new() { Shape = RigidBodyShape.Box, CollisionCollections = 3 }, new vec3(3, 2, 0), new vec3(0.5f))],
                   new RigidBodyWorldSettings()))
        {
            StepRigid(collections, 60, _ => DynamicTargets(collections));
            Check(RigidPosition(collections.GetWorld(1)).y < -1f, "Bodies without a shared collection pass through");
            Check(MathF.Abs(RigidPosition(collections.GetWorld(2)).y - 0.5f) < 0.03f, "Any shared collection bit collides");
        }

        // An animated active body follows its targets, then keyframed handoff keeps its momentum.
        using var handoff = new RigidBodySimulation(
            [RigidSource("thrown", new() { Shape = RigidBodyShape.Box, Animated = true }, new vec3(0, 5, 0), new vec3(0.25f))],
            new RigidBodyWorldSettings { GravityY = 0f });
        for (int frame = 1; frame <= 10; frame++)
        {
            mat4 target = mat4.Translate(new vec3(0.1f * frame, 5, 0)) * mat4.RotateY(0.05f * frame);
            handoff.Step(RigidFrame, [new RigidBodyTarget(target, false)], [], 0);
            for (int i = 0; i < 16; i++) Near(target[i], handoff.GetWorld(0)[i], $"Kinematic body follows animation frame {frame}");
        }
        Check(!handoff.IsDynamic(0), "Animated bodies stay kinematic");
        var stale = handoff.GetWorld(0);
        handoff.Step(RigidFrame, [new RigidBodyTarget(stale, true)], [], 0);
        Check(handoff.IsDynamic(0), "Animated off hands the body to dynamics");
        Check(MathF.Abs(handoff.GetLinearVelocity(0).x - 3f) < 0.05f,
            $"Dynamic handoff keeps the animated velocity: {handoff.GetLinearVelocity(0)}");
        Check(RigidPosition(handoff.GetWorld(0)).x > 1.05f, "Handed-off body continues moving");

        using var passive = new RigidBodySimulation(
            [RigidSource("pusher", new() { Type = RigidBodyType.Passive, Animated = true, Shape = RigidBodyShape.Box },
                 new vec3(-2, 0.5f, 0), new vec3(0.5f)),
             RigidFloor("floor", 0f),
             RigidSource("pushed", new() { Shape = RigidBodyShape.Box }, new vec3(0, 0.5f, 0), new vec3(0.5f))],
            new RigidBodyWorldSettings());
        for (int frame = 1; frame <= 45; frame++)
            passive.Step(RigidFrame, [new RigidBodyTarget(mat4.Translate(new vec3(-2 + frame * 0.06f, 0.5f, 0)), false),
                new RigidBodyTarget(mat4.Identity, false), new RigidBodyTarget(passive.GetWorld(2), true)], [], 0);
        Check(RigidPosition(passive.GetWorld(2)).x > 0.5f, "Animated passive bodies push active bodies");
    }

    private static RigidBodyFluid RigidPool(string id, FluidSettings? settings = null)
    {
        settings ??= new FluidSettings
        {
            ParticleCount = 1024, Bounds = new vec3(1f), SmoothingRadius = 0.2f, ParticleRadius = 0.05f,
            Pressure = 0.85f, Iterations = 4, Viscosity = 0.1f
        };
        return new RigidBodyFluid { ObjectId = id, Simulation = new FluidSimulation(settings), Settings = settings };
    }

    private static float PoolSurface(RigidBodyFluid fluid)
    {
        var heights = fluid.Simulation.Particles.Select(p => p.Position.y).OrderBy(y => y).ToArray();
        return heights[(int)((heights.Length - 1) * 0.85f)] + fluid.Settings.ParticleRadius;
    }

    private static void RigidBodyBuoyancy(Fixture f)
    {
        vec3 half = new(0.2f);
        float volume = 8 * half.x * half.y * half.z;
        var pool = RigidPool("water");
        // Let the water settle with only the floor in the world so the surface is level.
        using (var settle = new RigidBodySimulation([RigidFloor("floor", -1f)], new RigidBodyWorldSettings()))
            StepRigid(settle, 45, _ => [new RigidBodyTarget(mat4.Identity, false)], [pool]);
        float surface = PoolSurface(pool);
        Check(surface is > -0.8f and < -0.2f, $"Settled pool has a measurable surface: {surface}");

        (vec3 Position, float Speed, int Inside) Drop(float density, float fluidDensity)
        {
            var water = RigidPool("water", pool.Settings with { Density = fluidDensity });
            using (var settle = new RigidBodySimulation([RigidFloor("floor", -1f)], new RigidBodyWorldSettings()))
                StepRigid(settle, 45, _ => [new RigidBodyTarget(mat4.Identity, false)], [water]);
            using var simulation = new RigidBodySimulation(
                [RigidFloor("floor", -1f), RigidSource("body", new() { Shape = RigidBodyShape.Box, Mass = density * volume },
                    new vec3(0, surface + 0.5f, 0), half)],
                new RigidBodyWorldSettings());
            StepRigid(simulation, 150, frame => [new RigidBodyTarget(mat4.Identity, false), new RigidBodyTarget(simulation.GetWorld(1), true)],
                [water], 46);
            vec3 position = RigidPosition(simulation.GetWorld(1));
            float shrink = water.Settings.ParticleRadius * 0.5f;
            int inside = water.Simulation.Particles.Count(p =>
                MathF.Abs(p.Position.x - position.x) < half.x - shrink && MathF.Abs(p.Position.y - position.y) < half.y - shrink &&
                MathF.Abs(p.Position.z - position.z) < half.z - shrink);
            return (position, simulation.GetLinearVelocity(1).Length, inside);
        }

        var light = Drop(300f, 1000f);
        var heavy = Drop(3000f, 1000f);
        var dry = Drop(300f, 0f);
        Console.WriteLine($"  surface {surface:0.000}; light {light.Position.y:0.000} ({light.Speed:0.000} m/s); heavy {heavy.Position.y:0.000}; no buoyancy {dry.Position.y:0.000}");
        Check(heavy.Position.y < -0.7f, $"Dense bodies sink to the pool floor: {heavy.Position.y}");
        Check(dry.Position.y < -0.7f, $"Zero fluid density removes buoyancy: {dry.Position.y}");
        Check(light.Position.y > surface - half.y && light.Position.y < surface + half.y,
            $"Light bodies float at the surface ({surface}): {light.Position.y}");
        Check(light.Speed < 0.3f, $"Floating body settles instead of oscillating: {light.Speed}");
        Equal(0, light.Inside, "Particles are pushed out of the floating body");
        Equal(0, heavy.Inside, "Particles are pushed out of the sunken body");
        Check(MathF.Abs(light.Position.x) < 0.4f && MathF.Abs(light.Position.z) < 0.4f, "Floating body stays in the pool");
    }

    private static SceneObject RigidSceneBox(string name, vec3 position, vec3 half, RigidBodySettings? settings)
    {
        var obj = new SceneObject { Name = name, PivotOffset = vec3.Zero };
        obj.AssignObjectId();
        obj.SetLocalPosition(position);
        var (points, triangles) = RigidBox(vec3.Zero, half);
        var mesh = new MineImatorSimplyRemade.core.mdl.Mesh(null!);
        mesh.Vertices.AddRange(points);
        mesh.Normals.AddRange(points.Select(p => p.Normalized));
        mesh.Indices = triangles.Select(i => (uint)i).ToArray();
        obj.Visuals.Add(mesh);
        if (settings != null) obj.Modifiers.Add(new SceneModifier { Type = SceneModifierType.RigidBody, RigidBody = settings });
        return obj;
    }

    private static void WithRigidTimeline(Action<MineImatorSimplyRemade.core.ui.Panels.Viewport,
        MineImatorSimplyRemade.core.ui.Panels.Timeline, Action<int>> run, int maxFrames)
    {
        var timelineType = typeof(MineImatorSimplyRemade.core.ui.Panels.Timeline);
        var instance = timelineType.GetField("<Instance>k__BackingField",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        object? previous = instance.GetValue(null);
        var world = MineImatorSimplyRemade.core.project.ProjectManager.Instance.Manifest.Settings.RigidBodyWorld;
        try
        {
            var viewport = new MineImatorSimplyRemade.core.ui.Panels.Viewport();
            var timeline = new MineImatorSimplyRemade.core.ui.Panels.Timeline { Viewport = viewport };
            instance.SetValue(null, timeline);
            timelineType.GetField("_maxFrames", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(timeline, maxFrames);
            void At(int frame)
            {
                timelineType.GetField("_currentFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(timeline, frame);
                timelineType.GetMethod("ApplyKeyframesAtCurrentFrame",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(timeline, null);
                InverseKinematicsSolver.Evaluate(viewport.SceneObjects, frame, timeline.Framerate);
            }
            run(viewport, timeline, At);
        }
        finally
        {
            instance.SetValue(null, previous);
            MineImatorSimplyRemade.core.project.ProjectManager.Instance.Manifest.Settings.RigidBodyWorld = world;
        }
    }

    private static void BakeRigidAndWait(MineImatorSimplyRemade.core.ui.Panels.Viewport viewport)
    {
        Check(viewport.BakeRigidBodies(), "Rigid body bake starts: " + viewport.RigidBodyError);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        int completed = 0;
        while (viewport.IsRigidBodyBaking)
        {
            viewport.AdvanceRigidBodyBake();
            Check(viewport.RigidBodyBakeCompletedFrames >= completed, "Rigid body bake progress is monotonic");
            completed = viewport.RigidBodyBakeCompletedFrames;
            if (timeout.Elapsed > TimeSpan.FromMinutes(3)) throw new TimeoutException("Rigid body bake did not finish.");
            Thread.Sleep(1);
        }
        Check(viewport.RigidBodyError == null, "Rigid body bake succeeds: " + viewport.RigidBodyError);
        Equal(viewport.RigidBodyBakeTotalFrames, viewport.RigidBodyBakeCompletedFrames, "Every frame is baked");
    }

    private static void RigidBodyViewportBakeAndPlayback(Fixture f)
    {
        WithRigidTimeline((viewport, timeline, At) =>
        {
            var floor = RigidSceneBox("Floor", new vec3(0, -0.5f, 0), new vec3(5, 0.5f, 5),
                new() { Type = RigidBodyType.Passive, Shape = RigidBodyShape.Box });
            var cube = RigidSceneBox("Cube", new vec3(0, 3, 0), new vec3(0.5f), new() { Shape = RigidBodyShape.Box });
            var rider = RigidSceneBox("Rider", new vec3(0, 1, 0), new vec3(0.1f), null);
            cube.AddChild(rider);
            viewport.SceneObjects.Add(floor);
            viewport.SceneObjects.Add(cube);
            At(60);
            Near(3f, cube.GetWorldPosition().y, "Unbaked bodies keep their authored pose");

            BakeRigidAndWait(viewport);
            var bake = cube.Modifiers[0].RigidBodyBake;
            Check(bake != null && floor.Modifiers[0].RigidBodyBake == null, "Only active bodies store baked transforms");
            Equal(timeline.MaxFrames + 1, bake!.Frames.Length, "Bake covers frame zero through the timeline end");
            At(0);
            Near(3f, cube.GetWorldPosition().y, "Frame zero is the initial state");
            At(15);
            Check(cube.GetWorldPosition().y < 2.5f, "Baked playback falls under gravity");
            At(60);
            float rest = cube.GetWorldPosition().y;
            Check(MathF.Abs(rest - 0.5f) < 0.05f, $"Baked cube rests on the passive floor: {rest}");
            Near(rest + 1f, rider.GetWorldPosition().y, "Children follow their simulated parent");
            Near(new vec3(0, 3, 0), cube.Position, "Simulation never edits the authored position");
            At(500);
            Near(rest, cube.GetWorldPosition().y, "Frames after the bake hold the last result");

            cube.Modifiers[0] = cube.Modifiers[0] with { RigidBody = cube.Modifiers[0].RigidBody with { Mass = 2f } };
            Check(RigidBodyEvaluation.IsStale(cube.Modifiers[0], timeline.Framerate), "Setting edits mark the bake stale");
            At(60);
            Near(3f, cube.GetWorldPosition().y, "Stale bakes show the authored pose instead of outdated motion");
            cube.Modifiers[0] = cube.Modifiers[0] with { RigidBody = cube.Modifiers[0].RigidBody with { Mass = 1f } };
            At(60);
            Near(rest, cube.GetWorldPosition().y, "Restored settings reuse the saved bake");

            var (manifest, _) = f.Roundtrip(cube);
            var saved = manifest.Root.Modifiers!.Single(m => m.Type == SceneModifierType.RigidBody);
            Check(saved.RigidBodyBake != null && saved.RigidBodyBake.Frames.Length == bake.Frames.Length &&
                  saved.RigidBodyBake.Frames[^1].SequenceEqual(bake.Frames[^1]) && saved.RigidBody == cube.Modifiers[0].RigidBody,
                "Rigid body settings and bake survive archive roundtrips");

            viewport.ClearRigidBodyBake();
            At(60);
            Check(cube.Modifiers[0].RigidBodyBake == null, "Free bake removes saved frames");
            Near(3f, cube.GetWorldPosition().y, "Freed bodies return to the authored pose");

            MineImatorSimplyRemade.core.project.ProjectManager.Instance.Manifest.Settings.RigidBodyWorld =
                new RigidBodyWorldSettings { StartFrame = 20 };
            BakeRigidAndWait(viewport);
            Equal(20, cube.Modifiers[0].RigidBodyBake!.StartFrame, "Bakes record the world start frame");
            At(10);
            Near(3f, cube.GetWorldPosition().y, "Bodies follow animation before the simulation start frame");
            At(20);
            Near(3f, cube.GetWorldPosition().y, "The start frame is the initial state");
            At(40);
            Check(cube.GetWorldPosition().y < 2.9f, "Bodies simulate after the start frame");
        }, 60);
    }

    private static void RigidBodyAnimatedHandoff(Fixture f)
    {
        WithRigidTimeline((viewport, timeline, At) =>
        {
            MineImatorSimplyRemade.core.project.ProjectManager.Instance.Manifest.Settings.RigidBodyWorld =
                new RigidBodyWorldSettings { GravityY = 0f };
            var thrown = RigidSceneBox("Thrown", new vec3(0, 2, 0), new vec3(0.25f), new() { Shape = RigidBodyShape.Box, Animated = true });
            viewport.SceneObjects.Add(thrown);
            string channel = $"modifier.{thrown.Modifiers[0].Id}.rigid_body.animated";
            timeline.AddKeyframeForProperty(thrown, "position.x", 0);
            timeline.AddKeyframeForProperty(thrown, channel, 0);
            thrown.SetLocalPosition(new vec3(3, 2, 0));
            timeline.AddKeyframeForProperty(thrown, "position.x", 30);
            thrown.Modifiers[0] = thrown.Modifiers[0] with { RigidBody = thrown.Modifiers[0].RigidBody with { Animated = false } };
            timeline.AddKeyframeForProperty(thrown, channel, 20);
            At(0);
            Check(thrown.Modifiers[0].RigidBody.Animated, "Animated keyframes evaluate on the timeline");
            BakeRigidAndWait(viewport);
            At(10);
            float animatedX = thrown.GetWorldPosition().x;
            At(19);
            float handoffX = thrown.GetWorldPosition().x;
            At(30);
            float simulatedX = thrown.GetWorldPosition().x;
            At(40);
            float laterX = thrown.GetWorldPosition().x;
            Check(animatedX > 0.3f && handoffX > animatedX, "Animated frames follow keyframes");
            Check(laterX - simulatedX > 0.5f, $"Dynamic handoff keeps the animated momentum: {simulatedX} -> {laterX}");
            Check(laterX > 3.2f, "Released bodies continue past the last animated position");
        }, 40);
    }

    private static void RigidBodyFluidBake(Fixture f)
    {
        WithRigidTimeline((viewport, timeline, At) =>
        {
            var fluidSettings = new FluidSettings
            {
                ParticleCount = 512, Bounds = new vec3(0.8f, 0.6f, 0.8f), SmoothingRadius = 0.2f, ParticleRadius = 0.05f,
                Iterations = 3, ShowBounds = false
            };
            var water = new FluidSceneObject { Name = "Water", Settings = fluidSettings };
            water.AssignObjectId();
            vec3 half = new(0.15f);
            var floater = RigidSceneBox("Floater", new vec3(0, 0.3f, 0), half,
                new() { Shape = RigidBodyShape.Box, Mass = 300f * 8 * half.x * half.y * half.z });
            var floor = RigidSceneBox("Pool floor", new vec3(0, -1.1f, 0), new vec3(2, 0.5f, 2),
                new() { Type = RigidBodyType.Passive, Shape = RigidBodyShape.Box });
            viewport.SceneObjects.Add(water);
            viewport.SceneObjects.Add(floor);
            viewport.SceneObjects.Add(floater);
            BakeRigidAndWait(viewport);
            Check(water.Bake != null && water.HasValidBake(timeline.Framerate), "Interacting fluids receive a coupled bake");
            Equal(timeline.MaxFrames + 1, water.Bake!.Frames.Length, "Fluid bakes cover every timeline frame");
            var particles = water.Bake.GetParticles(timeline.MaxFrames);
            var heights = particles.Select(p => p.Position.y).OrderBy(y => y).ToArray();
            float surface = 2f * heights[heights.Length / 2] - (heights[0] - fluidSettings.ParticleRadius);
            At(timeline.MaxFrames);
            vec3 position = floater.GetWorldPosition();
            Check(MathF.Abs(position.y - surface) < half.y * 1.5f, $"Body floats on the baked water surface {surface}: {position.y}");
            Check(particles.All(p => MathF.Abs(p.Position.x - position.x) >= half.x - 0.02f ||
                MathF.Abs(p.Position.y - position.y) >= half.y - 0.02f || MathF.Abs(p.Position.z - position.z) >= half.z - 0.02f),
                "Baked water is displaced by the floating body");

            water.Settings = fluidSettings with { Density = 0f };
            Check(!water.HasValidBake(timeline.Framerate), "Fluid setting edits mark the fluid bake stale");
            water.Settings = fluidSettings;
            var (manifest, _) = f.Roundtrip(water);
            var restored = manifest.Root.FluidBake!.ToRuntime();
            Check(restored.Settings == water.Bake.Settings && restored.Frames.Length == water.Bake.Frames.Length &&
                  restored.Frames[^1].SequenceEqual(water.Bake.Frames[^1]), "Fluid bakes survive archive roundtrips");

            var context = (System.Text.Json.Serialization.JsonSerializerContext)typeof(MineImatorSimplyRemade.core.project.ProjectManager)
                .Assembly.GetType("MineImatorSimplyRemade.AppJsonContext")!.GetProperty("Default")!.GetValue(null)!;
            var manifestType = typeof(MineImatorSimplyRemade.core.project.ProjectManifest);
            var json = System.Text.Json.JsonSerializer.Serialize(MineImatorSimplyRemade.core.project.ProjectManager.Instance.Manifest,
                manifestType, context);
            var reloaded = (MineImatorSimplyRemade.core.project.ProjectManifest)System.Text.Json.JsonSerializer.Deserialize(json, manifestType, context)!;
            Equal(MineImatorSimplyRemade.core.project.ProjectManager.Instance.Manifest.Settings.RigidBodyWorld,
                reloaded.Settings.RigidBodyWorld, "Rigid body world settings roundtrip through the project manifest");

            viewport.ClearRigidBodyBake();
            Check(water.Bake == null, "Free bake also clears coupled fluid bakes");
        }, 75);
    }
}
