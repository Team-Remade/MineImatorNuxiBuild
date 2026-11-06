using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static MiObject MiDocument(string json) =>
        JsonSerializer.Deserialize(json, AppJsonContext.Default.MiObject)!;

    private static object MiInterpolate(Timeline timeline, SceneObject obj, string path, int frame) =>
        typeof(Timeline).GetMethod("InterpolateKeyframes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(timeline, [obj.Keyframes[path].Select(k => new TimelineKeyframe
            {
                Frame = k.Frame, Value = k.Value, InterpolationType = k.InterpolationType
            }).ToList(), path, frame, true])!;

    private const string MiPathDocument = """
        {
          "templates": [],
          "timelines": [
            {"id":"follower","type":"folder","name":"Follower",
             "default_values":{"PATH_OBJ":"curve","PATH_OFFSET":16},
             "keyframes":{"5":{"PATH_OFFSET":32,"TRANSITION":"easeinquad"},"15":{}}},
            {"id":"curve","type":"path","name":"Curve",
             "path":{"closed":false,"smooth":false,"shape":"round","shape_radius":8,
                     "shape_detail":8,"shape_tex_length":32,"shape_tex_mapped":true},
             "keyframes":{}},
            {"id":"start","type":"path_point","name":"Start","parent":"curve",
             "default_values":{"PATH_POINT_ANGLE":30,"PATH_POINT_SCALE":2},"keyframes":{"0":{},"10":{"PATH_POINT_ANGLE":90}}},
            {"id":"end","type":"pathpoint","name":"End","parent":"curve",
             "default_values":{"POS_Y":160},"keyframes":{}}
          ]
        }
        """;

    private static void MiImportPaths(Fixture f)
    {
        string file = f.File("paths.miobject", MiPathDocument);
        var loader = new MineImatorLoader();
        var source = loader.LoadMiObject(file);
        var root = loader.CreateSceneFromMiObject(source);
        var path = root.Children.OfType<PathSceneObject>().Single();
        var follower = root.Children.Single(o => o.Name == "Follower");
        Equal(PathShape.Tube, path.Settings.Shape, "Typed path shape");
        Near(0.5f, path.Settings.Radius, "Path radius in block units");
        Near(2f, path.Settings.TextureLength, "Path texture length in block units");
        Equal(8, path.Settings.Sides, "Path ring detail");
        Near(10f, path.Length, "Imported control-point length");
        Equal(2, path.Children.Count, "No generated default points");
        Check(path.Children.All(p => p is PathPointSceneObject), "Typed path points including legacy alias");
        Near(30f, ((PathPointSceneObject)path.Children[0]).PathPointAngle, "Default point angle");
        Near(2f, ((PathPointSceneObject)path.Children[0]).PathPointScale, "Default point scale");
        Equal(path.ObjectId, follower.PathFollowObjectId, "Forward reference resolves");
        Equal(path.ObjectId, (string)follower.Keyframes["path_follow.target"][0].Value, "Reference track remapped");
        Near(2f, follower.PathFollowOffset, "First pose before first key");
        Equal(0, follower.Keyframes["path_follow.offset"][0].Frame, "Held start pose");
        Near(1f, Convert.ToSingle(follower.Keyframes["path_follow.offset"][^1].Value), "Omitted offset returns to default");
        Equal("easeinquad", follower.Keyframes["path_follow.offset"][1].InterpolationType, "Easing preserved");
        var timeline = new Timeline();
        timeline.LoadKeyframesForAllObjects(new[] { root }.Concat(root.GetAllDescendants()));
        timeline.SetPropertyValue(follower, "path_follow.offset", MiInterpolate(timeline, follower, "path_follow.offset", 10));
        Near(1.75f, follower.PathFollowOffset, "Quadratic interpolation");
        var second = loader.CreateSceneFromMiObject(source);
        var secondPath = second.Children.OfType<PathSceneObject>().Single();
        Check(secondPath.ObjectId != path.ObjectId, "Repeated imports have independent IDs");
        Equal(secondPath.ObjectId, second.Children.Single(o => o.Name == "Follower").PathFollowObjectId, "Second import reference");
        var remapped = new Dictionary<string, string> { [path.ObjectId] = "replacement" };
        ProjectSceneSerializer.RemapObjectReferences(follower, remapped);
        Equal("replacement", follower.PathFollowObjectId, "Persistent reference remapping");
        Equal("replacement", (string)follower.Keyframes["path_follow.target"][0].Value, "Persistent track remapping");
    }

    private static void MiImportStatic(Fixture f)
    {
        var source = MiDocument("""
            {"templates":[],"timelines":[
              {"id":"point","type":"path_point","animated":false,
               "default_values":{"POS_X":16,"PATH_POINT_SCALE":1},
               "current_values":{"POS_X":48,"PATH_POINT_SCALE":3,"PATH_POINT_ANGLE":45},
               "keyframes":{"0":{"POS_X":160,"PATH_POINT_SCALE":9}}}
            ]}
            """);
        var point = (PathPointSceneObject)new MineImatorLoader().CreateSceneFromMiObject(source).Children.Single();
        Near(3f, point.Position.x, "Static current transform overrides retained keys");
        Near(3f, point.PathPointScale, "Static current scale");
        Near(45f, point.PathPointAngle, "Static current angle");
        Equal(0, point.Keyframes.Count, "Static objects do not acquire animated tracks");
        source = MiDocument("""{"timelines":[{"id":"bad","type":"path","path":{"shape_radius":0}}]}""");
        Reject(() => new MineImatorLoader().CreateSceneFromMiObject(source), "Invalid imported radius must be surfaced");
    }

    private static void MiImportBackground(Fixture f)
    {
        var source = MiDocument("""
            {"templates":[],"timelines":[
              {"id":"background","type":"background","name":"Environment",
               "default_values":{"ENV_SKY_COLOR":"#FF0000","ENV_FOG_DISTANCE":100,"ENV_GROUND_SHOW":true,"ENV_SKY_TIME":45},
               "keyframes":{"5":{"ENV_SKY_COLOR":"#0000FF","ENV_FOG_DISTANCE":200,"ENV_GROUND_SHOW":false,"TRANSITION":"easeinquad"},
                            "15":{"ENV_SKY_TIME":90}}}
            ]}
            """);
        Equal(0, new MineImatorLoader().CreateSceneFromMiObject(source).Children.Count, "Environment is not a placeholder");
        var tracks = MiObjectFeatureMapper.BackgroundTracks(source);
        Equal(3, tracks["FogDistance"].Count, "Held first background pose");
        Equal("200", tracks["FogDistance"][0].Value, "First background value held before frame 5");
        Equal("100", tracks["FogDistance"][^1].Value, "Sparse background key restores default, not previous pose");
        Equal("easeinquad", tracks["FogDistance"][1].InterpolationType, "Background easing");
        Check(tracks["FloorVisible"].All(k => k.Discrete), "Background toggles discrete");
        Equal("0", tracks["SkyHorizonDay.0"][0].Value, "RGB red component");
        Equal("1", tracks["SkyHorizonDay.2"][0].Value, "RGB blue component");
        Near(-6f, float.Parse(tracks["SkyTime"][^1].Value, CultureInfo.InvariantCulture), "Degree sky time converts to the native orbit");
        var saved = ProjectManager.Instance.Manifest.Settings;
        try
        {
            ProjectManager.Instance.Manifest.Settings = new();
            var panel = new PropertiesPanel { Timeline = new Timeline() };
            panel.ImportMineImatorBackground(source);
            Near(200f, panel.FogDistance, "Import immediately applies background values");
            Check(!panel.FloorVisible, "Import applies toggle");
            Near(90f, panel.SunAngle[0], "Native noon orbit anchor");
            Near(270f, panel.MoonAngle[0], "Moon stays opposite the sun");
            Near(135f, panel.SunAngle[1], "Default source azimuth converts to native coordinates");
            Near(-3f, panel.SkyTime, "Omitted source time uses default 45 degrees");
            var direction = (vec3)typeof(Viewport).GetMethod("DirectionFromEuler", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [new[] { panel.SunAngle[0] + panel.SkyTime * 15f, panel.SunAngle[1], panel.SunAngle[2] }])!;
            Near(MathF.Sqrt(0.5f), direction.y, "Source daytime remains above the native horizon");
            typeof(PropertiesPanel).GetMethod("ApplyBackgroundAnimation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(panel, [10]);
            Near(175f, panel.FogDistance, "Background uses source quadratic curve");
            Near(0.25f, panel.SkyHorizonDay[0], "Background color uses source curve");
            Check(!panel.FloorVisible, "Toggle holds until next key");
            var settings = ProjectManager.Instance.Manifest.Settings;
            var json = JsonSerializer.Serialize(ProjectManager.Instance.Manifest, AppJsonContext.Default.ProjectManifest);
            var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectManifest)!;
            Equal("easeinquad", restored.Settings.BackgroundKeyframes["FogDistance"][1].InterpolationType, "Easing survives project JSON");
            Equal(settings.BackgroundKeyframes.Count, restored.Settings.BackgroundKeyframes.Count, "Background tracks persist");
            var old = JsonSerializer.Deserialize("""{"Frame":0,"Value":"1"}""", AppJsonContext.Default.ProjectBackgroundKeyframeEntry)!;
            Equal("linear", old.InterpolationType, "Old projects retain linear interpolation");
        }
        finally
        {
            ProjectManager.Instance.Manifest.Settings = saved;
        }
    }

    private static void MiImportParticles(Fixture f)
    {
        foreach (string file in ParticlePreset.EnumerateFiles(Path.Combine(ParticleDataRoot(), "Particles")))
        {
            var preset = ParticlePreset.Load(file);
            var document = new JsonObject
            {
                ["templates"] = new JsonArray(new JsonObject
                {
                    ["id"] = "spawner", ["type"] = "particle_spawner",
                    ["particles"] = preset.Settings.DeepClone()
                }),
                ["timelines"] = JsonNode.Parse("""
                    [{"id":"emitter","type":"particle_spawner","temp":"spawner","name":"Emitter",
                      "default_values":{"CUSTOM_SEED":true,"SEED":42,"SPAWN":true,"FREEZE":false,"ATTRACTOR":"target","FORCE":2},
                      "keyframes":{"0":{},"10":{"SPAWN":false,"FREEZE":true,"ATTRACTOR":null}}},
                     {"id":"target","type":"folder","name":"Target"}]
                    """)
            };
            foreach (var template in (preset.Document["templates"] as JsonArray ?? []).OfType<JsonObject>())
                document["templates"]!.AsArray().Add(template.DeepClone());
            var source = MiDocument(document.ToJsonString());
            var warnings = new List<string>();
            var root = new MineImatorLoader().CreateSceneFromMiObject(source, warning: warnings.Add);
            var spawner = root.Children.OfType<ParticleSpawnerSceneObject>().Single();
            Check(spawner.Preset != null, $"Embedded preset: {file}");
            Check(JsonNode.DeepEquals(preset.Settings, spawner.Preset!["particles"]), "Original particle ranges preserved");
            Equal(42, spawner.Seed, "Imported deterministic seed");
            Equal(root.Children.Single(o => o.Name == "Target").ObjectId, spawner.AttractorObjectId, "Attractor reference remapped");
            Near(2f, spawner.AttractorForce, "Attractor strength");
            Equal(0, warnings.Count, "Supported preset imports without warnings");
            var timeline = new Timeline();
            timeline.LoadKeyframesForAllObjects(root.GetAllDescendants());
            Equal("", (string)MiInterpolate(timeline, spawner, "particle.attractor_target", 10), "Explicit null clears reference");
            timeline.SetPropertyValue(spawner, "particle.freeze", MiInterpolate(timeline, spawner, "particle.freeze", 10));
            Check(spawner.Freeze, "Particle freeze track wired");
            var ids = new Dictionary<string, string> { [spawner.AttractorObjectId] = "remapped-attractor" };
            ProjectSceneSerializer.RemapObjectReferences(spawner, ids);
            Equal("remapped-attractor", spawner.AttractorObjectId, "Attractor remaps for clones/imports");
            Equal("remapped-attractor", (string)spawner.Keyframes["particle.attractor_target"][0].Value, "Attractor track remaps");
        }
    }

    private static void MiImportWarnings(Fixture f)
    {
        var source = MiDocument("""
            {"templates":[{"id":"unsupported","type":"particle_spawner",
                           "particles":{"spawn_region_type":"path","types":[{"temp_type":"sheet"}]}}],
             "timelines":[{"id":"bad","type":"particle_spawner","temp":"unsupported","name":"Unsupported emitter"},
                          {"id":"good","type":"folder","name":"Retained"},
                          {"id":"missing","type":"folder","default_values":{"PATH_OBJ":"not-exported"}}]}
            """);
        var warnings = new List<string>();
        var root = new MineImatorLoader().CreateSceneFromMiObject(source, warning: warnings.Add);
        Equal(3, root.Children.Count, "Unsupported particles do not discard other objects");
        Equal("Placeholder", root.Children.Single(o => o.Name == "Unsupported emitter").ObjectType, "Unsupported emitter retained");
        Check(warnings.Any(w => w.Contains("Path-based particle")) && warnings.Any(w => w.Contains("not-exported")),
            "Unsupported features and unresolved references are reported");
    }
}
