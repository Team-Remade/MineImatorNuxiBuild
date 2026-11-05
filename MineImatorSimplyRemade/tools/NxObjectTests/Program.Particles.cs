using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static string ParticleDataRoot()
    {
        for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "MineImatorSimplyRemade", "data");
            if (Directory.Exists(Path.Combine(path, "Particles"))) return path;
        }
        throw new DirectoryNotFoundException("Run the particle tests from the repository.");
    }

    private static ParticlePreset BasicParticlePreset() => new(JsonNode.Parse("""
        {
          "particles": {
            "spawn_constant": false, "spawn_amount": 1,
            "spawn_region_use": false, "bounding_box_type": "none",
            "destroy_at_time": false, "destroy_at_animation_finish": false,
            "types": [{
              "name": "test", "temp_type": "template", "sprite_template": "generic",
              "spawn_rate": 1, "sprite_template_still_frame": true,
              "scale": 1, "alpha": 1, "color": "#FFFFFF"
            }]
          },
          "templates": [], "resources": [], "timelines": []
        }
        """)!.AsObject());

    private static ParticlePresetSimulation Simulate(ParticlePreset preset, float time, vec3? origin = null)
    {
        var simulation = new ParticlePresetSimulation(preset, 123);
        simulation.Advance(time, true, origin ?? vec3.Zero, mat4.Identity);
        return simulation;
    }

    private static void ParticleBundled(Fixture f)
    {
        string data = ParticleDataRoot();
        string[] files = ParticlePreset.EnumerateFiles(Path.Combine(data, "Particles")).ToArray();
        Equal(15, files.Length, "Bundled preset count");
        foreach (string file in files)
        {
            ParticlePreset preset = ParticlePreset.Load(file);
            var simulation = new ParticlePresetSimulation(preset, 42);
            bool spawned = false;
            for (int step = 0; step < 180; step++)
            {
                simulation.Advance(1f / 60f, true, new vec3(0, 20, 0), mat4.Identity);
                spawned |= simulation.Particles.Count > 0;
                Check(simulation.Particles.Count <= ParticlePresetSimulation.SafetyLimit, "Particle count safety bound");
                foreach (var p in simulation.Particles)
                    Check(float.IsFinite(p.Position.LengthSqr) && float.IsFinite(p.Scale) &&
                        float.IsFinite(p.Alpha) && float.IsFinite(p.Rotation.LengthSqr), $"Invalid state: {file}");
            }
            Check(spawned, $"Preset emitted nothing: {file}");
            foreach (JsonObject type in preset.Types.Where(type => ParticlePreset.Text(type, "temp_type") == "template"))
            {
                string sprite = ParticlePreset.Text(type, "sprite_template");
                int frames = ParticlePreset.Sprite(sprite).Frames;
                for (int frame = 0; frame < frames; frame++)
                {
                    string name = frames == 1 ? sprite : $"{sprite}_{frame}";
                    Check(File.Exists(Path.Combine(data, "minecraft", "versions", "26.3", "textures", "particle", name + ".png")),
                        $"Missing bundled sprite: {name}");
                }
            }
        }
    }

    private static void ParticleEmission(Fixture f)
    {
        ParticlePreset preset = BasicParticlePreset();
        preset.Settings["spawn_constant"] = true;
        preset.Settings["spawn_amount"] = 600f;
        Equal(10, Simulate(preset, 1).Particles.Count, "Continuous amount is per minute");
        var chunks = new ParticlePresetSimulation(preset, 123);
        for (int step = 0; step < 120; step++) chunks.Advance(1f / 120f, true, vec3.Zero, mat4.Identity);
        Equal(10, chunks.Particles.Count, "Continuous rate is independent of chunk size");
        preset.Settings["spawn_constant"] = false;
        preset.Settings["spawn_amount"] = 10f;
        preset.Types[0]["spawn_rate"] = 0.3f;
        var other = (JsonObject)preset.Types[0].DeepClone();
        other["spawn_rate"] = 0.7f;
        ((JsonArray)preset.Settings["types"]!).Add(other);
        preset = new ParticlePreset(preset.Document);
        var burst = Simulate(preset, 1);
        Equal(3, burst.Particles.Count(p => p.TypeIndex == 0), "First weighted burst");
        Equal(7, burst.Particles.Count(p => p.TypeIndex == 1), "Second weighted burst");
        burst.Advance(1, true, vec3.Zero, mat4.Identity);
        Equal(10, burst.Particles.Count, "One-shot fires only once");
        Equal(10, Simulate(preset, 1).Particles.Count, "New simulation can replay a burst");
        preset.Settings["destroy_at_amount"] = true;
        preset.Settings["destroy_at_amount_val"] = 4f;
        Equal(4, Simulate(preset, 1).Particles.Count, "Oldest particles are removed at amount limit");
        preset.Settings["spawn_amount"] = 1000000f;
        preset.Settings["destroy_at_amount"] = false;
        Equal(ParticlePresetSimulation.SafetyLimit, Simulate(preset, 1f / 60f).Particles.Count, "Hard safety limit");
    }

    private static void ParticleSpawnMenu(Fixture f)
    {
        string data = ParticleDataRoot();
        string[] files = ParticlePreset.EnumerateFiles(Path.Combine(data, "Particles")).ToArray();
        PropertyInfo version = typeof(BlockRegistry).GetProperty("VersionRoot")!;
        string original = (string)version.GetValue(null)!;
        try
        {
            version.SetValue(null, Path.Combine(data, "minecraft", "versions", "26.3"));
            var viewport = new Viewport();
            var spawn = new SpawnMenu { Viewport = viewport };
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SpawnMenu).GetField("_particlePresetFiles", fields)!.SetValue(spawn, files.ToList());
            typeof(SpawnMenu).GetField("_selectedCategory", fields)!.SetValue(spawn, "Particle Spawners");
            var options = typeof(SpawnMenu).GetMethod("GetFilteredObjects", fields)!;
            Equal(16, ((List<string>)options.Invoke(spawn, null)!).Count, "Blank emitter plus all presets in menu");
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                typeof(SpawnMenu).GetField("_searchQuery", fields)!.SetValue(spawn, name);
                var filtered = (List<string>)options.Invoke(spawn, null)!;
                int selected = filtered.FindIndex(option => option.EndsWith(name, StringComparison.Ordinal));
                Check(selected >= 0, "Preset must be searchable by name");
                typeof(SpawnMenu).GetField("_selectedObjectIndex", fields)!.SetValue(spawn, selected);
                Equal(file, (string)typeof(SpawnMenu).GetProperty("SelectedParticlePresetPath", fields)!.GetValue(spawn)!,
                    "Filtered menu selection resolves the correct file");
                ParticleSpawnerSceneObject emitter = spawn.SpawnParticlePresetObject(name, file)!;
                Check(emitter.Preset != null && emitter.ParticleLibraryEntryId == "", "Preset source does not need library objects");
                emitter.Position = new vec3(0, 20, 0);
                emitter.SimulateToTime(1.05f, viewport, spawn);
                foreach (var obj in viewport.SceneObjects.Where(obj => obj.IsRuntimeTransient))
                    Check(obj is ParticleSpriteSceneObject || obj.SpawnCategory == "Blocks", "Preset visual source");
                emitter.ResetRuntime();
                viewport.SceneObjects.Remove(emitter);
                Equal(0, viewport.SceneObjects.Count, "Preset removal cleans its runtime roots");
            }
        }
        finally { version.SetValue(null, original); }
    }

    private static void ParticleParameters(Fixture f)
    {
        ParticlePreset preset = BasicParticlePreset();
        JsonObject type = preset.Types[0];
        type["spd_extend"] = true;
        type["spd"] = new JsonArray(16f, 32f, -16f);
        type["spd_add"] = new JsonArray(1f, 0f, 0f);
        type["spd_mul"] = new JsonArray(1f, 1f, 1f);
        type["rot_spd_extend"] = true;
        type["rot_spd"] = new JsonArray(30f, 60f, 90f);
        type["rot_spd_add"] = new JsonArray(1f, 0f, 0f);
        type["rot_spd_mul"] = new JsonArray(1f, 1f, 1f);
        type["scale_add"] = -0.25f;
        type["alpha_add"] = -0.5f;
        type["sprite_angle"] = 15f;
        type["sprite_angle_add"] = 30f;
        type["color"] = "#000000";
        type["color_mix_enabled"] = true;
        type["color_mix"] = "#FFFFFF";
        type["color_mix_time"] = 2f;
        var p = Simulate(preset, 1).Particles.Single();
        Near(2f, p.Position.y, "Y velocity and 16 pixel unit conversion");
        Near(-1f, p.Position.z, "Z velocity");
        Near(4.75f, p.Velocity.x, "Per-frame acceleration conversion");
        Near(90, p.AngularVelocity.x, "Angular acceleration");
        Near(0.75f, p.Scale, "Scale evolution");
        Near(0.5f, p.Alpha, "Alpha evolution");
        Near(45, p.SpriteAngle, "Sprite spin");
        Near(0.5f, p.Color.x, "Color mixing time");
        type["scale_israndom"] = true;
        type["scale_random_min"] = 2f;
        type["scale_random_max"] = 3f;
        type["scale_add"] = 0f;
        type["color_israndom"] = true;
        type["color_random_start"] = "#FF0000";
        type["color_random_end"] = "#0000FF";
        p = Simulate(preset, 1f / 60f).Particles.Single();
        Check(p.Scale >= 2 && p.Scale <= 3, "Random scale range");
        Near(1, p.StartColor.x + p.StartColor.z, "Color random interpolation");
        Near(0, p.StartColor.y, "Color random channel");
        type["angle_extend"] = true;
        type["angle"] = new JsonArray(180f, 0f, 0f);
        type["angle_speed"] = 16f;
        p = Simulate(preset, 1f / 60f).Particles.Single();
        Check(p.Direction.y < -0.99f, "Launch direction uses the preset's Y-up coordinates");
        type["orbit"] = true;
        var orbit = new ParticlePresetSimulation(preset, 123) { AttractorPosition = new vec3(10, 0, 0), AttractorForce = 2 };
        orbit.Advance(1f / 60f, true, vec3.Zero, mat4.Identity);
        Check(orbit.Particles[0].Velocity.x > p.Velocity.x, "Attractor force affects particle motion");
    }

    private static void ParticleCollisions(Fixture f)
    {
        ParticlePreset preset = BasicParticlePreset();
        preset.Settings["spawn_amount"] = 20f;
        preset.Settings["spawn_region_use"] = true;
        preset.Settings["spawn_region_type"] = "sphere";
        preset.Settings["spawn_region_sphere_radius"] = 16f;
        Check(Simulate(preset, 1f / 60f).Particles.All(p => p.Position.Length <= 1), "Sphere radius in pixels");
        preset.Settings["spawn_region_type"] = "box";
        preset.Settings["spawn_region_box_size"] = new JsonArray(32f, 16f, 0f);
        Check(Simulate(preset, 1f / 60f).Particles.All(p =>
            Math.Abs(p.Position.x) <= 1 && Math.Abs(p.Position.y) <= 0.5f && p.Position.z == 0), "Box dimensions");
        preset.Settings["spawn_region_use"] = false;
        preset.Settings["spawn_amount"] = 1f;
        preset.Settings["bounding_box_type"] = "ground";
        preset.Settings["bounding_box_ground_z"] = 0f;
        preset.Types[0]["spd_extend"] = true;
        preset.Types[0]["spd"] = new JsonArray(16f, -16f, 0f);
        preset.Types[0]["bounce"] = true;
        preset.Types[0]["bounce_factor"] = 0.5f;
        var p = Simulate(preset, 1f / 60f).Particles.Single();
        Near(0, p.Position.y, "Ground contact");
        Near(0.5f, p.Velocity.y, "Ground restitution");
        Near(1, p.Velocity.x, "Bounce preserves tangential velocity");
        preset.Settings["destroy_at_bounding_box"] = 1f;
        Equal(0, Simulate(preset, 1f / 60f).Particles.Count, "Destroy on contact");
        preset.Settings["bounding_box_type"] = "custom";
        preset.Settings["bounding_box_custom_start"] = new JsonArray(-1f, -1f, -1f);
        preset.Settings["bounding_box_custom_end"] = new JsonArray(1f, 1f, 1f);
        Equal(0, Simulate(preset, 1).Particles.Count, "Custom bounds destruction");
        preset.Settings["bounding_box_type"] = "spawn";
        preset.Settings["spawn_region_use"] = true;
        preset.Settings["spawn_region_type"] = "sphere";
        preset.Settings["spawn_region_sphere_radius"] = 0f;
        Equal(0, Simulate(preset, 1f / 60f).Particles.Count, "Spawn sphere bounds destruction");
    }

    private static void ParticleAnimation(Fixture f)
    {
        ParticlePreset preset = BasicParticlePreset();
        JsonObject type = preset.Types[0];
        type["sprite_template_still_frame"] = false;
        type["sprite_animation_speed"] = 7f;
        Near(4, Simulate(preset, 0.5f).Particles.Single().Frame, "Sprite frame progression");
        type["sprite_template_reverse"] = true;
        Equal(5, Simulate(preset, 0.25f).Particles.Single().Frame, "Reversed frames");
        type["sprite_template_reverse"] = false;
        type["sprite_animation_onend"] = 1f;
        Equal(0, Simulate(preset, 1).Particles.Single().Frame, "Looping frames");
        type["sprite_animation_onend"] = 2f;
        Equal(7, Simulate(preset, 1).Particles.Single().Frame, "Ping-pong endpoint");
        Equal(0, Simulate(preset, 2).Particles.Single().Frame, "Ping-pong return");
        type["sprite_animation_onend"] = 0f;
        preset.Settings["destroy_at_animation_finish"] = true;
        Equal(0, Simulate(preset, 1).Particles.Count, "Animation finish destruction");
        preset.Settings["destroy_at_animation_finish"] = false;
        preset.Settings["destroy_at_time"] = true;
        preset.Settings["destroy_at_time_seconds"] = 0.5f;
        Equal(0, Simulate(preset, 0.5f).Particles.Count, "Lifetime destruction");
        type["sprite_template"] = "flame";
        preset.Settings["destroy_at_time"] = false;
        preset.Settings["destroy_at_animation_finish"] = true;
        Equal(1, Simulate(preset, 2).Particles.Count, "Single-frame templates have no animation end");
    }

    private static void ParticleRuntime(Fixture f)
    {
        var emitter = new ParticleSpawnerSceneObject { Seed = 43 };
        emitter.SetPreset(ParticlePreset.Load(Path.Combine(ParticleDataRoot(), "Particles", "Effects", "Explosion.miparticles")).Document);
        var viewport = new Viewport();
        var spawn = new SpawnMenu { Viewport = viewport };
        viewport.SceneObjects.Add(emitter);
        emitter.Position = new vec3(0, 20, 0);
        emitter.SimulateToTime(0.1f, viewport, spawn);
        Check(emitter.ActiveParticleCount > 0, "Runtime creates preset particles without a library entry");
        var first = viewport.SceneObjects.Where(obj => obj.IsRuntimeTransient).Select(obj => obj.Position).ToArray();
        Check(first.Length > 0, "Preset visuals registered");
        Check(viewport.SceneObjects.Where(obj => obj.IsRuntimeTransient).All(obj => !obj.IsSelectable && obj.HideInSceneTree),
            "Transient particles are hidden from editing");
        emitter.ObjectVisible = false;
        Check(viewport.SceneObjects.Where(obj => obj.IsRuntimeTransient).All(obj => !obj.GetEffectiveVisibility()),
            "Top-level particles inherit emitter visibility");
        emitter.ObjectVisible = true;
        emitter.Freeze = true;
        emitter.Step(0.1f, viewport, spawn);
        Check(first.SequenceEqual(viewport.SceneObjects.Where(obj => obj.IsRuntimeTransient).Select(obj => obj.Position)),
            "Freeze holds existing particles");
        emitter.Freeze = false;
        emitter.SimulateToTime(0, viewport, spawn);
        Equal(0, emitter.ActiveParticleCount, "Backward seek clears runtime");
        emitter.SimulateToTime(0.1f, viewport, spawn);
        Check(first.SequenceEqual(viewport.SceneObjects.Where(obj => obj.IsRuntimeTransient).Select(obj => obj.Position)),
            "Backward seek reproduces positions");
        emitter.ResetRuntime();
        Equal(1, viewport.SceneObjects.Count, "Restart cleans all transient roots");
        emitter.Step(0.1f, viewport, spawn);
        Check(emitter.ActiveParticleCount > 0 && emitter.Emitting, "Burst restart preserves authored emitting state");
        emitter.SetExplicitMaterialSettings();
        emitter.MaterialSettings.EmissionEnabled = true;
        emitter.MaterialSettings.EmissionEnergy = 3;
        emitter.MaterialSettings.AlbedoColor = new vec4(0.5f, 0.75f, 1, 0.25f);
        emitter.Step(1f / 60f, viewport, spawn);
        Check(viewport.SceneObjects.Where(obj => obj.IsRuntimeTransient).All(obj =>
            obj.MaterialSettings.EmissionEnabled && obj.MaterialSettings.EmissionEnergy == 3 &&
            obj.MaterialSettings.AlbedoColor.w <= 0.25f), "Emitter material reaches runtime particles");
        emitter.ResetRuntime();
    }

    private static void ParticlePersistence(Fixture f)
    {
        var emitter = new ParticleSpawnerSceneObject { Name = "preset", Seed = 92, Freeze = true, AttractorForce = 2 };
        emitter.SetPreset(ParticlePreset.Load(Path.Combine(ParticleDataRoot(), "Particles", "Effects", "Fountain of cakes.miparticles")).Document);
        var parent = Node("parent");
        var target = Node("attractor");
        parent.AddChild(emitter);
        parent.AddChild(target);
        emitter.AttractorObjectId = target.ObjectId;
        var (manifest, _) = f.Roundtrip(parent);
        ProjectSceneObjectEntry entry = manifest.Root.Children[0];
        Check(JsonNode.DeepEquals(emitter.Preset, entry.ParticlePreset), "Source-generated preset archive roundtrip");
        Equal(target.ObjectId, entry.ParticleAttractorObjectId, "Internal attractor reference persisted");
        var viewport = new Viewport();
        var spawn = new SpawnMenu { Viewport = viewport };
        var imported = ProjectSceneSerializer.SpawnObjectFromEntry(manifest.Root, viewport, spawn)!;
        var restored = (ParticleSpawnerSceneObject)imported.Children[0];
        Equal(imported.Children[1].ObjectId, restored.AttractorObjectId, "Imported attractor reference remapped");
        Check(JsonNode.DeepEquals(emitter.Preset, restored.Preset), "Runtime import settings");
        Equal(92, restored.Seed, "Seed persisted");
        Check(restored.Freeze, "Freeze persisted");
        var libraryClone = typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", BindingFlags.Static | BindingFlags.NonPublic)!;
        var clonedEntry = (ProjectSceneObjectEntry)libraryClone.Invoke(null, [entry])!;
        Check(JsonNode.DeepEquals(entry.ParticlePreset, clonedEntry.ParticlePreset), "Library clone settings");
        var duplicate = typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var clone = (ParticleSpawnerSceneObject)duplicate.Invoke(new SceneTree(), [emitter, new Dictionary<string, string>()])!;
        Check(JsonNode.DeepEquals(emitter.Preset, clone.Preset), "Scene duplicate settings");
        Check(!ReferenceEquals(emitter.Preset, clone.Preset), "Duplicate must own its preset");
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { emitter }])!;
        ProjectSceneObjectEntry snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(emitter);
        ulong initial = Hash();
        emitter.Preset!["particles"]!["spawn_amount"] = 13f;
        Check(initial != Hash(), "Dirty fingerprint includes preset parameters");
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [emitter, snapshot, null]);
        Equal(initial, Hash(), "Undo restores preset and common state");
        Check(!ReferenceEquals(snapshot.ParticlePreset, emitter.Preset), "Undo snapshot must stay independent");
        clonedEntry.ParticlePreset!["particles"]!["spawn_amount"] = 4f;
        Check(!JsonNode.DeepEquals(clonedEntry.ParticlePreset, entry.ParticlePreset), "Library clone must own its preset");
    }

    private static void ParticleTimeline(Fixture f)
    {
        var timeline = new Timeline();
        var emitter = new ParticleSpawnerSceneObject();
        emitter.SetPreset(BasicParticlePreset().Document);
        var get = typeof(Timeline).GetMethod("GetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var discovery = typeof(Timeline).GetMethod("GetKeyframePropertyPaths", BindingFlags.Static | BindingFlags.NonPublic)!;
        var paths = ((IEnumerable<string>)discovery.Invoke(null, [emitter])!).ToHashSet();
        Check(paths.Contains("particle.freeze") && paths.Contains("particle.attractor_force"), "Preset channels discovered");
        Check(!paths.Contains("particle.spawn_rate"), "Legacy-only parameters must not appear on presets");
        set.Invoke(timeline, [emitter, "particle.freeze", 1f]);
        Equal(1f, Convert.ToSingle(get.Invoke(timeline, [emitter, "particle.freeze"])), "Freeze keyframe");
        set.Invoke(timeline, [emitter, "particle.attractor_force", 4f]);
        Equal(4f, Convert.ToSingle(get.Invoke(timeline, [emitter, "particle.attractor_force"])), "Force keyframe");
        emitter.SetPreset(null);
        Check(((IEnumerable<string>)discovery.Invoke(null, [emitter])!).Contains("particle.spawn_rate"), "Legacy channels unchanged");
    }

    private static void ParticleInvalid(Fixture f)
    {
        foreach (string edit in new[] { "sprite", "source", "region", "vector", "color" })
        {
            ParticlePreset preset = BasicParticlePreset();
            switch (edit)
            {
                case "sprite": preset.Types[0]["sprite_template"] = "unknown"; break;
                case "source": preset.Types[0]["temp_type"] = "sheet"; break;
                case "region": preset.Settings["spawn_region_type"] = "path"; break;
                case "vector": preset.Types[0]["spd"] = new JsonArray(1f, 2f); break;
                case "color": preset.Types[0]["color"] = "invalid"; break;
            }
            Reject(() => new ParticlePreset(preset.Document), $"Accepted invalid {edit}");
        }
        Reject(() => new ParticlePreset(new JsonObject()), "Accepted missing particle types");
    }

    private static unsafe void ParticleGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization failed");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Particle graphics test", null, null);
        PropertyInfo versionRoot = typeof(BlockRegistry).GetProperty("VersionRoot")!;
        string originalRoot = (string)versionRoot.GetValue(null)!;
        try
        {
            Check(window != null, "OpenGL context creation failed");
            glfw.MakeContextCurrent(window);
            using var gl = Silk.NET.OpenGL.GL.GetApi(glfw.GetProcAddress);
            MineImatorLoader.Instance.Initialize(gl);
            versionRoot.SetValue(null, Path.Combine(ParticleDataRoot(), "minecraft", "versions", "26.3"));
            foreach (string block in new[] { "cake", "grass_block" })
            {
                typeof(BlockRegistry).GetMethod("ParseBlockstate", BindingFlags.Static | BindingFlags.NonPublic,
                    [typeof(string), typeof(string)])!
                    .Invoke(null, [block, Path.Combine(MinecraftDataLoader.GetVersionRoot(), "blockstates", block + ".json")]);
                var variant = BlockRegistry.GetVariants(block).First();
                var model = BlockRegistry.ResolveModel(variant.ModelPath, "", block)!;
                foreach (string reference in model.Textures.Keys)
                {
                    string? key = BlockRegistry.ResolveTextureKey(model, "#" + reference);
                    if (key == null || TerrainAtlas.Textures.ContainsKey(key)) continue;
                    string path = Path.Combine(MinecraftDataLoader.GetVersionRoot(), "textures", "block", key + ".png");
                    if (File.Exists(path)) TerrainAtlas.Textures[key] = MineImatorLoader.Instance.LoadTextureFromFile(path);
                }
            }
            using var ubo = new SceneUniformBuffer(gl);
            Mesh.SceneUBO = ubo;
            ubo.Upload(mat4.Identity, vec3.Ones, vec3.UnitY, vec3.Zero, vec3.UnitY, vec3.Zero,
                vec3.UnitY, vec3.Zero, 0, false, false, false);
            using var preview = new PreviewRenderer(gl, 160) { Distance = 1.5f, AutoRotateSpeed = 0 };
            var viewport = new Viewport();
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            preview.Render([], "particles", 0);
            byte[] baseline = ReadPixels();
            var emitter = spawn.SpawnParticlePresetObject("Smoke",
                Path.Combine(ParticleDataRoot(), "Particles", "Effects", "Smoke.miparticles"))!;
            var document = BasicParticlePreset().Document;
            document["particles"]!["types"]![0]!["sprite_template"] = "generic";
            emitter.SetPreset(document);
            emitter.Step(1f / 60f, viewport, spawn);
            var sprite = viewport.SceneObjects.OfType<ParticleSpriteSceneObject>().Single();
            sprite.ApplyMaterialSettingsToMeshes();
            Mesh mesh = sprite.Visuals.Single();
            Check(mesh.TextureId != 0, "Particle texture uploaded");
            byte[] Render()
            {
                preview.Render(sprite.Visuals, "particles", 0, centerMeshes: true);
                return ReadPixels();
            }
            byte[] first = Render();
            int changed = Enumerable.Range(0, first.Length / 4).Count(index =>
                first[index * 4] != baseline[index * 4] || first[index * 4 + 1] != baseline[index * 4 + 1] ||
                first[index * 4 + 2] != baseline[index * 4 + 2]);
            Check(changed > 32, $"Particle must draw visible textured pixels: {changed}");
            typeof(SpawnMenu).GetMethod("SetPresetParticleFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(spawn, [sprite, 7]);
            sprite.ApplyMaterialSettingsToMeshes();
            Check(!first.SequenceEqual(Render()), "Sprite animation changes rendered pixels");
            sprite.MaterialSettings!.AlbedoColor.w = 0;
            sprite.ApplyMaterialSettingsToMeshes();
            Check(baseline.SequenceEqual(Render()), "Zero-alpha particle renders no pixels");
            var matrix = typeof(Viewport).GetMethod("GetRenderableWorldMatrix", BindingFlags.Static | BindingFlags.NonPublic)!;
            mat4 face = (mat4)matrix.Invoke(null, [sprite, new vec3(0, 0, 5)])!;
            sprite.RollRadians = MathF.PI / 2;
            mat4 rolled = (mat4)matrix.Invoke(null, [sprite, new vec3(0, 0, 5)])!;
            Check(face != rolled, "Sprite roll changes billboard orientation");
            mat4 side = (mat4)matrix.Invoke(null, [sprite, new vec3(5, 0, 0)])!;
            Check(side != rolled, "Billboards must face the active camera");
            emitter.ResetRuntime();
            foreach (Mesh pick in emitter.Visuals) pick.Dispose();
            viewport.SceneObjects.Remove(emitter);
            foreach (string name in new[] { "Fountain of cakes", "Grass explosion" })
            {
                var blockEmitter = spawn.SpawnParticlePresetObject(name,
                    Path.Combine(ParticleDataRoot(), "Particles", "Effects", name + ".miparticles"))!;
                blockEmitter.Position = new vec3(0, 20, 0);
                blockEmitter.SimulateToTime(0.5f, viewport, spawn);
                SceneObject block = viewport.SceneObjects.First(obj => obj.IsRuntimeTransient && obj.SpawnCategory == "Blocks");
                Check(block.Visuals.Any(visual => visual.Vertices.Count > 0 && visual.TextureId != 0),
                    $"{name} must build real textured block meshes");
                Near(-0.5f, block.PivotOffset.y, "Block particles rotate around their center");
                preview.Render(block.Visuals, "blocks", 0, centerMeshes: true);
                Check(!baseline.SequenceEqual(ReadPixels()), $"{name} must render actual pixels");
                blockEmitter.ResetRuntime();
                foreach (Mesh pick in blockEmitter.Visuals) pick.Dispose();
                viewport.SceneObjects.Remove(blockEmitter);
            }
            Equal(Silk.NET.OpenGL.GLEnum.NoError, gl.GetError(), "Particle OpenGL errors");
            Console.WriteLine($"PASS Particle graphics: {changed} visible pixels");

            byte[] ReadPixels()
            {
                byte[] pixels = new byte[160 * 160 * 4];
                gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, preview.ColorTexture);
                fixed (byte* pointer = pixels)
                    gl.GetTexImage(Silk.NET.OpenGL.GLEnum.Texture2D, 0, Silk.NET.OpenGL.GLEnum.Rgba,
                        Silk.NET.OpenGL.GLEnum.UnsignedByte, pointer);
                gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, 0);
                return pixels;
            }
        }
        finally
        {
            versionRoot.SetValue(null, originalRoot);
            Mesh.SceneUBO = null;
            if (window != null) glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
