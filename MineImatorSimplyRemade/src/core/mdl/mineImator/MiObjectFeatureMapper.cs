using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

public static class MiObjectFeatureMapper
{
    public static bool IsEnvironment(MiTimeline timeline) =>
        timeline.Type?.ToLowerInvariant() is "environment" or "background";

    public static SceneObject? Create(MiTimeline timeline, MiTemplate? template, MiObject source, GL? gl)
    {
        string type = timeline.Type?.ToLowerInvariant() ?? template?.Type?.ToLowerInvariant() ?? "";
        SceneObject? obj = null;
        if (type == "path")
        {
            JsonElement settings = timeline.Path ?? default;
            string shape = Text(Property(settings, "shape"), "none");
            var path = new PathSceneObject
            {
                Settings = new PathSettings
                {
                    Closed = Flag(Property(settings, "closed"), true),
                    Smooth = Flag(Property(settings, "smooth"), true),
                    Detail = (int)Number(Property(settings, "detail"), 6),
                    Shape = shape switch
                    {
                        "none" => PathShape.None, "flat" => PathShape.Flat,
                        "round" or "tube" => PathShape.Tube,
                        _ => throw new InvalidDataException($"Unknown Mine-imator path shape '{shape}'.")
                    },
                    Radius = Number(Property(settings, "shape_radius"), 8) / 16f,
                    Sides = (int)Number(Property(settings, "shape_detail"), 12),
                    Invert = Flag(Property(settings, "shape_invert"), false),
                    SmoothSegments = Flag(Property(settings, "shape_smooth_segments"), true),
                    SmoothRing = Flag(Property(settings, "shape_smooth_ring"), true),
                    MappedTexture = Flag(Property(settings, "shape_tex_mapped"), false),
                    TextureLength = Number(Property(settings, "shape_tex_length"), 16) / 16f
                }
            };
            if (gl != null) path.AddMesh(new Mesh(gl));
            obj = path;
        }
        else if (type is "path_point" or "pathpoint")
        {
            obj = new PathPointSceneObject();
            if (gl != null) obj.AddMesh(new SphereMesh(gl, 0.12f, 8, 6) { PickOnly = true });
        }
        else if (type == "particle_spawner" && template?.Particles is { } particles)
        {
            var spawner = new ParticleSpawnerSceneObject { AttractorForce = 1f };
            var settings = JsonNode.Parse(particles.GetRawText())!.AsObject();
            var references = (settings["types"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(t => ParticlePreset.Text(t, "temp_type") == "templateobj")
                .Select(t => ParticlePreset.Text(t, "temp")).ToHashSet(StringComparer.Ordinal);
            var templates = new JsonArray();
            foreach (var entry in (source.Templates ?? []).Where(t => references.Contains(t.Id)))
                templates.Add(JsonNode.Parse(JsonSerializer.Serialize(entry, AppJsonContext.Default.MiTemplate)));
            spawner.SetPreset(new JsonObject
            {
                ["particles"] = settings,
                ["templates"] = templates
            });
            obj = spawner;
        }
        else if (type == "folder")
            obj = new SceneObject { ObjectType = "Folder", PivotOffset = vec3.Zero };

        if (obj != null)
            obj.Name = !string.IsNullOrWhiteSpace(timeline.Name) ? timeline.Name : template?.Name ?? obj.ObjectType;
        return obj;
    }

    public static void Apply(SceneObject obj, MiTimeline timeline, IReadOnlyDictionary<string, SceneObject> ids,
        Action<string>? warning = null)
    {
        var frames = Frames(timeline);
        MiKeyframe? first = timeline.Animated == false ? timeline.CurrentValues : frames.FirstOrDefault().Key;
        if (obj is PathPointSceneObject point)
        {
            Scalar("PATH_POINT_ANGLE", "path_point.angle", 0f, value => point.PathPointAngle = value);
            Scalar("PATH_POINT_SCALE", "path_point.scale", 1f, value => point.PathPointScale = value);
        }
        else if (obj is not PathSceneObject)
        {
            Reference("PATH_OBJ", "path_follow.target", id => obj.PathFollowObjectId = id, target => target is PathSceneObject);
            Scalar("PATH_OFFSET", "path_follow.offset", 0f, value => obj.PathFollowOffset = value, 1f / 16f);
            if (obj.FollowPathModifier == null && obj.Keyframes.ContainsKey("path_follow.target"))
                obj.Modifiers.Insert(0, new SceneModifier { Type = SceneModifierType.FollowPath });
        }
        if (obj is ParticleSpawnerSceneObject particle)
        {
            Scalar("SPAWN", "particle.emitting", 1f, value => particle.Emitting = value != 0, discrete: true);
            Scalar("FREEZE", "particle.freeze", 0f, value => particle.Freeze = value != 0, discrete: true);
            Scalar("FORCE", "particle.attractor_force", 1f, value => particle.AttractorForce = value);
            Reference("ATTRACTOR", "particle.attractor_target", id => particle.AttractorObjectId = id, _ => true);
            if (Flag(Value(timeline, first, "CUSTOM_SEED"), false))
                particle.Seed = (int)Number(Value(timeline, first, "SEED"), 0);
            foreach (string channel in new[] { "CLEAR", "FORCE_DIRECTIONAL", "FORCE_VORTEX" })
                if (Has(timeline, frames, channel))
                    Report($"MIObject particle '{timeline.Name}': '{channel}' is not supported by the particle engine.", warning);
            if (frames.Select(f => Number(Value(timeline, f.Key, "SEED"), particle.Seed)).Distinct().Count() > 1)
                Report($"MIObject particle '{timeline.Name}': animated seeds are not supported; using the first seed.", warning);
        }

        void Scalar(string source, string target, float fallback, Action<float> set, float multiplier = 1f, bool discrete = false)
        {
            if (!Has(timeline, frames, source)) return;
            float Read(MiKeyframe? key) => (discrete ? (Flag(Value(timeline, key, source), fallback != 0) ? 1f : 0f) :
                Number(Value(timeline, key, source), fallback)) * multiplier;
            set(Read(first));
            Track(target, key => Read(key), discrete);
        }

        void Reference(string source, string target, Action<string> set, Func<SceneObject, bool> valid)
        {
            if (!Has(timeline, frames, source)) return;
            string Read(MiKeyframe? key)
            {
                string id = Text(Value(timeline, key, source), "");
                if (id is "" or "null" or "-1") return "";
                if (ids.TryGetValue(id, out var found) && valid(found)) return found.ObjectId;
                Report($"MIObject timeline '{timeline.Name}': unresolved {source} reference '{id}'.", warning);
                return "";
            }
            set(Read(first));
            Track(target, key => Read(key), true);
        }

        void Track(string target, Func<MiKeyframe, object> read, bool discrete)
        {
            if (frames.Count == 0) return;
            var keys = frames.Select(f => new ObjectKeyframe
            {
                Frame = f.Frame, Value = read(f.Key),
                InterpolationType = discrete ? "instant" : Transition(timeline, f.Key)
            }).ToList();
            if (keys[0].Frame > 0)
                keys.Insert(0, new ObjectKeyframe { Frame = 0, Value = keys[0].Value, InterpolationType = "instant" });
            obj.Keyframes[target] = keys;
        }
    }

    public static Dictionary<string, List<ProjectBackgroundKeyframeEntry>> BackgroundTracks(MiObject source,
        Action<string>? warning = null)
    {
        var result = new Dictionary<string, List<ProjectBackgroundKeyframeEntry>>();
        var defaults = new PropertiesPanel();
        var mappings = new (string Source, string Target, float Multiplier)[]
        {
            ("ENV_SKY_TIME", "SkyTime", -1f / 15f),
            ("ENV_SKY_MOON_PHASE", "MoonPhase", 1),
            ("ENV_SKY_SUN_SCALE", "SunSize", 16),
            ("ENV_SKY_MOON_SCALE", "MoonSize", 16),
            ("ENV_SKY_ROTATION", "SunAngle.1", 1),
            ("ENV_SKY_ROTATION", "MoonAngle.1", 1),
            ("ENV_TWILIGHT", "Twilight", 1),
            ("ENV_GROUND_SHOW", "FloorVisible", 1),
            ("ENV_IMAGE_ROTATION", "BackgroundRotationDegrees", 1),
            ("ENV_SUNLIGHT_STRENGTH", "SunFillLightStrength", 1),
            ("ENV_FOG_SHOW", "FogEnabled", 1),
            ("ENV_FOG_SKY", "SkyFog", 1),
            ("ENV_FOG_CUSTOM_COLOR", "CustomFogColor", 1),
            ("ENV_FOG_CUSTOM_OBJECT_COLOR", "CustomObjectFogColor", 1),
            ("ENV_FOG_DISTANCE", "FogDistance", 1),
            ("ENV_FOG_SIZE", "FogFadeSize", 1),
            ("ENV_FOG_HEIGHT", "FogHeight", 1),
            ("ENV_SKY_COLOR", "SkyHorizonDay", 1),
            ("ENV_SKY_COLOR", "SkyZenithDay", 1),
            ("ENV_SKY_COLOR", "BackgroundColor", 1),
            ("ENV_NIGHT_SKY_COLOR", "SkyHorizonNight", 1),
            ("ENV_NIGHT_SKY_COLOR", "SkyZenithNight", 1),
            ("ENV_SKY_CLOUDS_COLOR", "CloudColor", 1),
            ("ENV_NIGHT_SKY_CLOUDS_COLOR", "NightCloudColor", 1),
            ("ENV_NIGHT_SKY_STARS_COLOR", "StarColor", 1),
            ("ENV_SUNLIGHT_COLOR", "SunFillLightColor", 1),
            ("ENV_AMBIENT_COLOR", "AmbientLightColor", 1),
            ("ENV_NIGHT_COLOR", "NightAmbientLightColor", 1),
            ("ENV_FOG_COLOR", "FogColor", 1),
            ("ENV_FOG_OBJECT_COLOR", "ObjectFogColor", 1)
        };
        foreach (var timeline in (source.Timelines ?? []).Where(IsEnvironment))
        {
            var frames = Frames(timeline);
            var poses = frames.Count > 0 ? frames :
                new List<(int Frame, MiKeyframe Key)> { (0, timeline.CurrentValues ?? new MiKeyframe()) };
            bool celestial = Has(timeline, poses, "ENV_SKY_TIME") || Has(timeline, poses, "ENV_SKY_ROTATION");
            if (celestial)
            {
                // Mine-imator starts its orbit at noon; the native sky starts on
                // the horizon and advances in the opposite direction.
                Add("SunAngle.0", _ => 90f, false);
                Add("MoonAngle.0", _ => 270f, false);
                Add("SunAngle.2", _ => 0f, false);
                Add("MoonAngle.2", _ => 0f, false);
            }
            foreach (var mapping in mappings)
            {
                if (!Has(timeline, poses, mapping.Source) &&
                    !(celestial && mapping.Source is "ENV_SKY_TIME" or "ENV_SKY_ROTATION")) continue;
                string[] parts = mapping.Target.Split('.');
                object fallback = typeof(PropertiesPanel).GetField(parts[0])!.GetValue(defaults)!;
                if (fallback is float[] color && parts.Length == 1)
                {
                    for (int component = 0; component < 3; component++)
                    {
                        int index = component;
                        Add(mapping.Target + "." + component, key =>
                        {
                            JsonElement value = Value(timeline, key, mapping.Source);
                            return value.ValueKind == JsonValueKind.Undefined ? color[index] :
                                ParticlePreset.Color(Text(value, ""))[index];
                        }, false);
                    }
                }
                else
                {
                    if (fallback is float[] vector) fallback = vector[int.Parse(parts[1], CultureInfo.InvariantCulture)];
                    Add(mapping.Target, key =>
                    {
                        var value = Value(timeline, key, mapping.Source);
                        if (fallback is bool flag) return Flag(value, flag);
                        if (mapping.Source == "ENV_SKY_TIME") return Number(value, 45f) * mapping.Multiplier;
                        if (mapping.Source == "ENV_SKY_ROTATION") return Number(value, -45f) + 180f;
                        float number = Number(value, Convert.ToSingle(fallback, CultureInfo.InvariantCulture) / mapping.Multiplier) * mapping.Multiplier;
                        return fallback is int ? (object)(int)number : number;
                    }, fallback is bool or int);
                }
            }
            var supported = mappings.Select(m => m.Source).ToHashSet(StringComparer.Ordinal);
            foreach (string channel in (timeline.DefaultValues?.Values.Keys ?? Enumerable.Empty<string>())
                         .Concat(poses.SelectMany(p => p.Key.ExtraValues.Keys)).Distinct())
                if (channel.StartsWith("ENV_", StringComparison.Ordinal) && !supported.Contains(channel))
                    Report($"MIObject background '{timeline.Name}': '{channel}' has no internal mapping.", warning);

            void Add(string target, Func<MiKeyframe, object> read, bool discrete)
            {
                var keys = poses.Select(p => new ProjectBackgroundKeyframeEntry
                {
                    Frame = p.Frame, Value = Convert.ToString(read(p.Key), CultureInfo.InvariantCulture) ?? "",
                    Discrete = discrete, InterpolationType = Transition(timeline, p.Key)
                }).ToList();
                if (keys[0].Frame > 0)
                    keys.Insert(0, new ProjectBackgroundKeyframeEntry
                    {
                        Frame = 0, Value = keys[0].Value, Discrete = discrete, InterpolationType = "instant"
                    });
                result[target] = keys;
            }
        }
        return result;
    }

    public static string Transition(MiTimeline timeline, MiKeyframe key)
    {
        string transition = key.Transition ?? Text(Value(timeline, null, "TRANSITION"), "linear");
        return transition != "bezier" ? transition : string.Create(CultureInfo.InvariantCulture,
            $"bezier:{key.EaseInX ?? Number(Value(timeline, null, "EASE_IN_X"), 1)},{key.EaseInY ?? Number(Value(timeline, null, "EASE_IN_Y"), 0)},{key.EaseOutX ?? Number(Value(timeline, null, "EASE_OUT_X"), 0)},{key.EaseOutY ?? Number(Value(timeline, null, "EASE_OUT_Y"), 1)}");
    }

    internal static void Report(string message, Action<string>? warning)
    {
        Console.Error.WriteLine(message);
        warning?.Invoke(message);
    }

    private static List<(int Frame, MiKeyframe Key)> Frames(MiTimeline timeline) =>
        timeline.Animated == false ? [] : (timeline.Keyframes ?? [])
            .Select(p => (Frame: int.TryParse(p.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame) ? frame : -1, Key: p.Value))
            .Where(p => p.Frame >= 0 && p.Key != null).OrderBy(p => p.Frame).ToList();

    private static bool Has(MiTimeline timeline, List<(int Frame, MiKeyframe Key)> frames, string channel) =>
        timeline.DefaultValues?.Values.ContainsKey(channel) == true ||
        timeline.CurrentValues?.ExtraValues.ContainsKey(channel) == true || frames.Any(f => f.Key.ExtraValues.ContainsKey(channel));

    private static JsonElement Value(MiTimeline timeline, MiKeyframe? key, string channel) =>
        key?.ExtraValues.TryGetValue(channel, out var value) == true ? value :
        timeline.DefaultValues?.Values.TryGetValue(channel, out value) == true ? value : default;

    private static JsonElement Property(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) ? value : default;

    private static float Number(JsonElement value, float fallback)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return fallback;
        if (float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && float.IsFinite(number))
            return number;
        throw new InvalidDataException($"Expected a finite Mine-imator number, got '{value}'.");
    }

    private static bool Flag(JsonElement value, bool fallback) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? fallback :
        value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : Number(value, 0) != 0;

    private static string Text(JsonElement value, string fallback) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? fallback :
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback :
        value.ValueKind == JsonValueKind.Object ? Text(Property(value, "id"), fallback) :
        throw new InvalidDataException($"Expected Mine-imator text or an object reference, got '{value}'.");
}
