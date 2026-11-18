using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.mdl;
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
            }).ToList(), path, frame])!;

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

    private static void MiImportShapes(Fixture f)
    {
        var source = MiDocument("""
            {"templates":[
              {"id":"icosphere-template","type":"icosphere",
               "shape":{"subdivision":3,"morph":-0.75,"smooth":false}},
              {"id":"torus-template","type":"torus",
               "shape":{"detail":20,"major_size":1.5,"minor_size":0.25,"smooth":false,
                        "tex_hrepeat":2,"tex_vmirror":true}}
             ],
             "timelines":[
              {"id":"ico","type":"icosphere","temp":"icosphere-template","name":"Faceted"},
              {"id":"ring","type":"torus","temp":"torus-template","name":"Ring",
               "glow":false,"glow_texture":true,"only_render_glow":false,
               "glint_mode":0,"glint_scale":1,"glint_speed":1,"glint_strength":1,"glint_tex":"default",
               "alpha_mode":2,"object_tag":"Main"}
             ]}
            """);
        var warnings = new List<string>();
        var root = new MineImatorLoader().CreateSceneFromMiObject(source, warning: warnings.Add);
        Equal(0, warnings.Count, "Simply Upscaled shape settings map without warnings");
        var ico = root.Children.Single(o => o.ObjectType == "Icosphere");
        Equal(3, ico.PrimitiveIcosphereSubdivision, "Icosphere subdivision imports");
        Near(-0.75f, ico.PrimitiveIcosphereMorph, "Icosphere morph imports");
        Check(!ico.PrimitiveSphereSmooth, "Icosphere shading imports");
        var torus = root.Children.Single(o => o.ObjectType == "Torus");
        Equal(20, torus.PrimitiveRadialSegments, "Torus detail imports");
        Near(1.5f, torus.PrimitiveTorusMajorRadius, "Torus major radius imports");
        Near(0.25f, torus.PrimitiveTorusMinorRadius, "Torus minor radius imports");
        Check(!torus.PrimitiveRadialSmooth, "Torus shading imports");
        Near(2f, torus.MaterialSettings.TextureRepeat.x, "Torus UV repeat imports");
        Check(torus.MaterialSettings.TextureMirror.y, "Torus UV mirror imports");

        var saved = ProjectSceneSerializer.SerializeObjectForLibrary(torus);
        Equal(20, saved.PrimitiveRadialSegments, "Torus detail serializes");
        Near(1.5f, saved.PrimitiveTorusMajorRadius, "Torus major radius serializes");
        var json = JsonSerializer.Serialize(saved, AppJsonContext.Default.ProjectSceneObjectEntry);
        var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectSceneObjectEntry)!;
        Near(0.25f, restored.PrimitiveTorusMinorRadius, "Torus minor radius survives object JSON");
        Near(-0.75f, ProjectSceneSerializer.SerializeObjectForLibrary(ico).PrimitiveIcosphereMorph,
            "Icosphere morph survives object serialization");

        var enabledDefaults = MiDocument("""
            {"timelines":[{"id":"glint","type":"folder","name":"Glint",
              "glint_mode":1,"glint_scale":1,"glint_tex":"default"}]}
            """);
        warnings.Clear();
        new MineImatorLoader().CreateSceneFromMiObject(enabledDefaults, warning: warnings.Add);
        Check(warnings.Any(w => w.Contains("glint_mode")) && warnings.Any(w => w.Contains("glint_scale")),
            "Enabled unsupported glint settings remain explicit");
    }

    private static void MiImportBackground(Fixture f)
    {
        const string backgroundJson = """
            {"templates":[],"timelines":[
              {"id":"background","type":"background","name":"Environment",
               "default_values":{"ENV_SKY_COLOR":"#FF0000","ENV_FOG_DISTANCE":100,"ENV_GROUND_SHOW":true,"ENV_SKY_TIME":45},
               "keyframes":{"5":{"ENV_SKY_COLOR":"#0000FF","ENV_FOG_DISTANCE":200,"ENV_GROUND_SHOW":false,"TRANSITION":"easeinquad"},
                            "15":{"ENV_SKY_TIME":90}}}
            ]}
            """;
        var source = MiDocument(backgroundJson);
        Equal(0, new MineImatorLoader().CreateSceneFromMiObject(source).Children.Count, "Environment is not a placeholder");
        var tracks = MiObjectFeatureMapper.BackgroundTracks(source);
        var bgSource = MiDocument(backgroundJson.Replace("ENV_", "BG_", StringComparison.Ordinal));
        var bgTracks = MiObjectFeatureMapper.BackgroundTracks(bgSource);
        Check(tracks.Keys.All(key => tracks[key].Select(k => k.Value).SequenceEqual(bgTracks[key].Select(k => k.Value))),
            "Source BG-prefixed channels match legacy ENV aliases");
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

    private static void MiImportNativeObjects(Fixture f)
    {
        var source = MiDocument("""
            {"templates":[{"id":"text-template","type":"text","text":{"3d":true,"face_camera":true}}],
             "timelines":[
              {"id":"lamp","type":"spot_light","name":"Lamp","shadows":false,
               "default_values":{"LIGHT_RANGE":160,"LIGHT_STRENGTH":2,"LIGHT_SPOT_RADIUS":60,"LIGHT_SPOT_SHARPNESS":0.75},
               "keyframes":{"5":{"LIGHT_COLOR":"#FF8000","LIGHT_SPECULAR_STRENGTH":0.4},"15":{"LIGHT_RANGE":320}}},
              {"id":"camera","type":"camera","name":"Camera",
               "default_values":{"CAM_FOV":60,"CAM_BLADE_AMOUNT":6,"CAM_CLIP_NEAR":2,
                   "CAM_LIGHT_MANAGEMENT":true,"CAM_TONEMAPPER":4,"CAM_EXPOSURE":2,"CAM_GAMMA":2.4,
                   "CAM_BLOOM":true,"CAM_BLOOM_INTENSITY":0.75,"CAM_BLOOM_BLEND_MODE":"screen",
                   "CAM_OUTLINE":true,"CAM_ROTATE_DISTANCE":48},
               "keyframes":{"5":{"RGB_MUL":"#FF0000","CAM_FOV":80,"ROT_Z":90},"15":{"CAM_BLOOM":false,"CAM_OUTLINE":false}}},
              {"id":"camera-child","type":"folder","parent":"camera","default_values":{"POS_X":16},"keyframes":{"0":{},"15":{}}},
              {"id":"text","type":"text","temp":"text-template","text":"Base caption",
               "default_values":{"TEXT_HALIGN":"left","TEXT_VALIGN":"top","TEXT_AA":true},
               "keyframes":{"0":{"TEXT":"First caption"},"10":{"TEXT":"Second caption"}}}
             ]}
            """);
        var warnings = new List<string>();
        var root = new MineImatorLoader().CreateSceneFromMiObject(source, warning: warnings.Add);
        Equal(0, warnings.Count, "Native object channels map without losses");
        var light = root.Children.OfType<LightSceneObject>().Single();
        Equal(LightType.Spot, light.Type, "Typed spot light");
        Check(!light.LightShadowEnabled, "Imported light shadow toggle");
        Near(10f, light.LightRange, "Light range in block units");
        Near(15f, light.LightSpotBlend, "Spot sharpness becomes angular falloff");
        Near(0.2f, light.LightSpecular, "Source specular one corresponds to native half");
        Near(128f / 255, light.LightColor.y, "Light RGB");
        var camera = root.Children.OfType<CameraSceneObject>().Single();
        Near(80f, camera.Fov, "First camera pose held");
        CameraDirection(vec3.UnitX);
        var cameraChild = camera.Children.Single();
        Near(-1, cameraChild.GetWorldMatrixNoPivot().m32, "Camera basis conversion preserves child placement");
        Near(0.125f, camera.Near, "Camera clipping in block units");
        Equal(6, camera.BladeCount, "Blade count is not converted to a boolean");
        Equal(7, camera.ColorManagementToneMapper, "AgX enum remapped by meaning");
        Near(1f, camera.ColorManagementExposure, "Exposure multiplier becomes stops");
        Check(camera.ColorManagementOverrideEnabled && camera.ColorManagementEnabled, "Camera-local color management");
        Near(0, camera.BlendColor.y, "Camera RGB multiplication affects output, not its pick mesh");
        var bloom = camera.Effects.Single(e => e.Type == CameraEffectType.Bloom);
        Near(75, bloom.Bloom.Intensity, "Bloom normalized strength becomes percentage");
        Equal(BloomBlendMode.Screen, bloom.Bloom.BlendMode, "Effect blend enums are not material blend enums");
        var outline = camera.Effects.Single(e => e.Type == CameraEffectType.Outline);
        Near(1, outline.Outline.ColorR, "Implicit upstream outline color is white");
        var text = root.Children.Single(o => o.ObjectType == "Text Mesh");
        Check(text.TextMeshExtruded && text.TextMeshFaceCamera, "Text geometry options");
        Equal("First caption", text.GetEffectiveTextMeshString(), "Initial caption");
        Equal(0, text.TextMeshHorizontalAlignment, "Text horizontal alignment");
        var timeline = new Timeline { Viewport = new Viewport() };
        timeline.Viewport.SceneObjects.Add(root);
        timeline.LoadKeyframesForAllObjects(root.GetAllDescendants());
        Seek(15);
        Near(20, light.LightRange, "Actual timeline applies native light tracks");
        Near(60, camera.Fov, "Sparse camera pose returns to source defaults");
        CameraDirection(vec3.UnitZ);
        Near(1, cameraChild.GetWorldMatrixNoPivot().m30, "Camera children retain their source basis after seeking");
        Check(!bloom.Enabled && !outline.Enabled, "Animated effect toggles reach render state");
        Equal("Second caption", text.GetEffectiveTextMeshString(), "Timeline changes text");
        Seek(0);
        Check(bloom.Enabled && outline.Enabled, "Backward seeking restores effects");
        Equal("First caption", text.GetEffectiveTextMeshString(), "Backward seeking restores text");
        Seek(15);
        Check(!CameraSceneObject.CloneEffect(bloom).Enabled, "Duplicating preserves disabled effects");
        var saved = ProjectSceneSerializer.SerializeObjectForLibrary(camera);
        var json = JsonSerializer.Serialize(saved, AppJsonContext.Default.ProjectSceneObjectEntry);
        var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectSceneObjectEntry)!;
        Check(restored.CameraEffects.All(e => !e.Enabled), "Disabled effects persist");
        Equal(2, restored.Keyframes["camera.effect.0.enabled"].Count(k => k.Frame > 0),
            "Effect toggle tracks persist");

        void Seek(int frame)
        {
            typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, frame);
            timeline.ApplyKeyframesAtCurrentFrame();
        }

        void CameraDirection(vec3 expected)
        {
            vec3 direction = (camera.GetWorldMatrixNoPivot() * new vec4(0, 0, -1, 0)).xyz.Normalized;
            Near(expected, direction, "Camera uses the source forward direction");
        }
    }

    private static void MiImportEffectConversions(Fixture f)
    {
        var source = MiDocument("""
            {"timelines":[{"id":"camera","type":"camera",
             "default_values":{"CAM_COLOR_CORRECTION":true,"CAM_CONTRAST":0.25,"CAM_COLOR_HUE":0.5,
               "CAM_HEAT_DISTORTION":true,"CAM_HEAT_DISTORTION_STRENGTH":2,
               "CAM_CA":true,"CAM_CA_BLUR_AMOUNT":0.05,
               "CAM_BLACK_LINES":true,"CAM_BLACK_LINES_SIZE":0.7,
               "CAM_SHAKE":true,"CAM_SHAKE_MODE":1,"CAM_SHAKE_STRENGTH_X":16,
               "CAM_SHAKE_STRENGTH_Y":48,"CAM_SHAKE_STRENGTH_Z":32,
               "CAM_SHAKE_SPEED_X":2,"CAM_SHAKE_SPEED_Y":4,"CAM_SHAKE_SPEED_Z":3,
               "CAM_BRIGHTNESS":0.1,"CAM_COLOR_BURN":"#800000","CAM_VHS":true},
             "keyframes":{"0":{},"10":{"CAM_SHAKE_MODE":0,"CAM_SHAKE_STRENGTH_X":90,
                 "CAM_SHAKE_STRENGTH_Z":180,"CAM_BLACK_LINES_SIZE":0.9,
                 "CAM_BLACK_LINES_ROTATION":60,"CAM_BLACK_LINES_OFFSET_Y":10}}}]}
            """);
        var warnings = new List<string>();
        var camera = (CameraSceneObject)new MineImatorLoader().CreateSceneFromMiObject(source, warning: warnings.Add).Children.Single();
        var correction = camera.Effects.Single(e => e.Type == CameraEffectType.ColorCorrection).ColorCorrection;
        Near(125, correction.Contrast, "Source contrast is offset around one");
        Near(50, correction.Hue, "Source hue is expressed in hundreds of degrees");
        Near(2, camera.Effects.Single(e => e.Type == CameraEffectType.HeatDistort).HeatDistort.Strength,
            "Heat strength is already a percentage in both renderers");
        var aberration = camera.Effects.Single(e => e.Type == CameraEffectType.ChromaticAberration).ChromaticAberration;
        Near(-3, aberration.RedOffset, "Source channel shrink becomes a signed native radial offset");
        Check(aberration.DistortRgb, "Source scaling is radial, not a constant horizontal shift");
        Near(0, aberration.BlurAmount, "Spectral blur is not replaced with unrelated spatial blur");
        var bars = camera.Effects.Single(e => e.Type == CameraEffectType.CinematicBars);
        Near(20, bars.CinematicBars.Size, "Source offscreen bars become visible per-edge thickness");
        var shake = camera.Effects.Single(e => e.Type == CameraEffectType.CameraShake);
        Equal(CameraShakeMode.Positional, shake.Shake.Mode, "Source positional mode");
        Near(new vec3(1, 2, 3), shake.Shake.Strength, "Positional shake units and axes");
        Near(new vec3(2, 3, 4), shake.Shake.Speed, "Shake noise axes follow strength axes");
        int shakeIndex = camera.Effects.IndexOf(shake), barsIndex = camera.Effects.IndexOf(bars);
        Near(MathF.PI, Convert.ToSingle(camera.Keyframes[$"camera.effect.{shakeIndex}.shake.strength.x"][^1].Value),
            "Source yaw strength becomes radians");
        Near(MathF.PI / 2, Convert.ToSingle(camera.Keyframes[$"camera.effect.{shakeIndex}.shake.strength.y"][^1].Value),
            "Source pitch strength becomes radians");
        Near(0, Convert.ToSingle(camera.Keyframes[$"camera.effect.{shakeIndex}.shake.strength.z"][^1].Value),
            "Unsupported roll is not silently turned into yaw");
        Near(5, Convert.ToSingle(camera.Keyframes[$"camera.effect.{barsIndex}.cinematic_bars.offset"][^1].Value),
            "Rotated source offset is projected onto the bar normal");
        Check(warnings.Any(w => w.Contains("native noise")) &&
              new[] { "CAM_BRIGHTNESS", "CAM_COLOR_BURN", "CAM_VHS" }.All(c => warnings.Any(w => w.Contains(c))),
            "Approximate and unmapped rendering features are reported explicitly");
    }

    private static void MiImportResources(Fixture f)
    {
        string normal = f.File("normal.png", "normal resource");
        f.File("lab-normal.png", "packed normal resource");
        var source = MiDocument("""
            {"resources":[{"id":"rgb","type":"texture","filename":"normal.png","material_format":1},
                          {"id":"lab","type":"texture","filename":"lab-normal.png","material_format":2}],
             "templates":[{"id":"cube","type":"cube","shape":{"tex_normal":"rgb"}},
                          {"id":"human","type":"character","model":{"name":"human","state":{"variant":"alex"}}}],
             "timelines":[{"id":"cube","type":"cube","temp":"cube",
                 "default_values":{"TEXTURE_NORMAL_OBJ":null},
                 "keyframes":{"0":{},"5":{"TEXTURE_NORMAL_OBJ":"lab"},"15":{}}},
               {"id":"human","type":"character","temp":"human"},
               {"id":"camera","type":"camera","default_values":{"CAM_LENS_DIRT":true,"TEXTURE_OBJ":"rgb"},
                "keyframes":{"0":{},"15":{"TEXTURE_OBJ":"lab"}}}]}
            """);
        source.DirectoryPath = f.Root;
        var warnings = new List<string>();
        var root = new MineImatorLoader().CreateSceneFromMiObject(source, warning: warnings.Add);
        var cube = root.Children.Single(o => o.ObjectType == "Cube");
        Equal(normal, cube.NormalTexturePath, "Null normal reference uses the template resource");
        Check(cube.MaterialSettings.PbrEnabled && cube.MaterialSettings.NormalEnabled, "Imported normal is active");
        Equal("", (string)cube.Keyframes["material.normal_texture"].Single(k => k.Frame == 5).Value,
            "LabPBR data is not misinterpreted as RGB normals");
        Equal(normal, (string)cube.Keyframes["material.normal_texture"][^1].Value, "Sparse normal keys return to template");
        Check(warnings.Any(w => w.Contains("material-format conversion")), "Unsupported normal packing is explicit");
        var character = root.Children.OfType<CharacterSceneObject>().Single();
        Check(character.BoneObjects.Count > 0 && character.SourceMineImatorModel is { } model &&
              model.FullPath.EndsWith("alex.mimodel", StringComparison.OrdinalIgnoreCase),
            "Builtin human model resolves to the requested bundled rig");
        var camera = root.Children.OfType<CameraSceneObject>().Single();
        Equal(normal, camera.Effects[0].LensDirt.TexturePath, "Camera texture supplies lens dirt image");
        var timeline = new Timeline { Viewport = new Viewport() };
        timeline.Viewport.SceneObjects.Add(root);
        timeline.LoadKeyframesForAllObjects(root.GetAllDescendants());
        typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, 15);
        timeline.ApplyKeyframesAtCurrentFrame();
        Equal(Path.Combine(f.Root, "lab-normal.png"), camera.Effects[0].LensDirt.TexturePath,
            "Lens dirt image follows the source camera texture track");
        var (cameraManifest, cameraDirectory) = f.Roundtrip(camera);
        object cameraValue = cameraManifest.Root.Keyframes["camera.effect.0.lens_dirt.texture"][^1].Value;
        string cameraTrack = cameraValue is JsonElement cameraElement ? cameraElement.GetString()! : (string)cameraValue;
        Equal("packed normal resource", File.ReadAllText(Resource(cameraDirectory, cameraTrack)),
            "Lens dirt image tracks are packaged portably");
        var (manifest, directory) = f.Roundtrip(cube);
        object packedValue = manifest.Root.Keyframes["material.normal_texture"][^1].Value;
        string packedTrack = packedValue is JsonElement element ? element.GetString()! : (string)packedValue;
        Equal("normal resource", File.ReadAllText(Resource(directory, packedTrack)), "Animated normals are packaged portably");
    }

    private static void MiImportScenery(Fixture f)
    {
        string schematic = f.NewPath(".schematic");
        string cache = schematic + ".meshcache";
        File.Copy(CacheFile(f, CachePayload(7)), cache);
        var source = MiDocument("""
            {"resources":[{"id":"terrain","type":"scenery","filename":"missing.schematic"}],
             "templates":[{"id":"scenery","type":"scenery","scenery":"terrain"}],
             "timelines":[{"id":"scene","type":"scenery","temp":"scenery","name":"Terrain",
                           "default_values":{"POS_X":32},"keyframes":{"0":{},"10":{"POS_X":48}}}]}
            """);
        source.Resources[0].Filename = Path.GetFileName(schematic);
        source.DirectoryPath = Path.GetDirectoryName(schematic)!;
        var tried = new List<string>();
        SceneObject? Load(string path)
        {
            tried.Add(path);
            if (MeshCacheData.IsCache(path)) CheckCacheQuad(MeshCacheData.Load(path));
            return new SceneObject { ObjectType = "Schematic", SpawnCategory = "Scenery" };
        }
        var loader = new MineImatorLoader();
        var warnings = new List<string>();
        var scene = loader.CreateSceneFromMiObject(source, warning: warnings.Add, sceneryFactory: Load).Children.Single();
        Equal(cache, tried.Single(), "Missing schematic imports its adjacent meshcache");
        Equal(cache, scene.SourceAssetPath, "Surviving source is what persistence copies");
        Near(2, scene.Position.x, "Scenery participates in normal timeline mapping");
        Equal("Terrain", scene.Name, "Imported scenery uses timeline name");
        Equal(0, warnings.Count, "Valid meshcache-only scenery does not report missing schematic");
        new MineImatorSimplyRemade.core.world.WorldVolume(
            new MineImatorSimplyRemade.core.world.WorldBounds(0, 0, 0, 1, 0, 2)).SaveSchematic(schematic);
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-1));
        tried.Clear();
        loader.CreateSceneFromMiObject(source, sceneryFactory: Load);
        Equal(cache, tried.Single(), "Object imports prefer adjacent cache even if schematic is newer");
        tried.Clear();
        warnings.Clear();
        var fallback = loader.CreateSceneFromMiObject(source, warning: warnings.Add, sceneryFactory: path =>
        {
            tried.Add(path);
            if (MeshCacheData.IsCache(path)) throw new InvalidDataException("Deliberately invalid cache");
            return new SceneObject { ObjectType = "Schematic", SpawnCategory = "Scenery" };
        }).Children.Single();
        Equal(schematic, fallback.SourceAssetPath, "Invalid cache falls back to existing schematic");
        Check(tried.SequenceEqual(new[] { cache, schematic }) && warnings.Any(w => w.Contains("Deliberately invalid")),
            "Fallback order and original error are explicit");
        var (manifest, directory) = f.Roundtrip(scene);
        CheckCacheQuad(MeshCacheData.Load(Resource(directory, manifest.Root.SourceAssetPath)));
    }

    private static void MiImportConstraintsAndModifiers(Fixture f)
    {
        var source = MiDocument("""
            {"templates":[{"id":"cube","type":"cube","shape":{}}],
             "timelines":[
              {"id":"copy","type":"cube","temp":"cube","lock":true,"wind":true,"blend_mode":"screen",
               "default_values":{"ROT_TARGET":"target","COPY_ROT_Y":false,"COPY_ROT_OFFSET_Z":90,
                 "LOOK_AT_TARGET":"target","LOOK_AT_BLEND":0.5,
                 "MODIFIER_SHAKE":true,"MODIFIER_SHAKE_POSITION":true,"MODIFIER_SHAKE_POSITION_POWER":0.4,
                 "MODIFIER_FRAMESKIP":true,"MODIFIER_FRAMESKIP_VALUE":4,
                 "HSB_MUL":"#808080","RGB_ADD":"#004000"},
               "keyframes":{"5":{},"15":{"ROT_TARGET":null,"MODIFIER_FRAMESKIP_VALUE":2}}},
              {"id":"target","type":"folder","name":"Target"}]}
            """);
        var warnings = new List<string>();
        var root = new MineImatorLoader().CreateSceneFromMiObject(source, warning: warnings.Add);
        Equal(0, warnings.Count, "Compatible constraints and modifiers map without warnings");
        var obj = root.Children.Single(o => o.ObjectType == "Cube");
        var target = root.Children.Single(o => o.Name == "Target");
        Check(!obj.IsSelectable && obj.AffectedByWind, "Static selection and wind flags");
        Equal(MaterialBlendMode.Screen, obj.MaterialSettings.BlendMode, "Object blend mode");
        var copy = obj.CopyConstraints.Single(c => c.Type == CopyConstraintType.Rotation);
        Equal(target.ObjectId, copy.TargetObjectId, "Copy target IDs remapped");
        Check(!copy.AxisZ, "Z-up constraint masks converted to Y-up");
        Near(MathF.PI / 2, copy.Offset.y, "Constraint offsets become radians and swap axes");
        Near(0.5f, obj.CopyConstraints.Single(c => c.Type == CopyConstraintType.LookAt).Blend, "Look At blend");
        var skip = obj.Modifiers.Single(m => m.Type == SceneModifierType.FrameSkip);
        Equal(4, skip.FrameSkipFrames, "Frame Skip value is an integer, not a toggle");
        var shake = obj.Modifiers.Single(m => m.Type == SceneModifierType.Shake);
        Check(shake.Shake.PositionEnabled && !shake.Shake.RotationEnabled, "Shake flags and implicit defaults");
        Near(0.4f, shake.Shake.PositionPower, "Shake settings");
        Near(128f / 255, obj.MaterialSettings.ColorAdjustments.MultiplyHsv.x, "HSV components preserved, not reinterpreted as RGB");
        var timeline = new Timeline();
        timeline.SetPropertyValue(obj, "copy_constraint.0.target", MiInterpolate(timeline, obj, "copy_constraint.0.target", 15));
        Equal("", copy.TargetObjectId, "Null key clears the copy reference");
        timeline.SetPropertyValue(obj, $"modifier.{skip.Id}.frame_skip.frames",
            MiInterpolate(timeline, obj, $"modifier.{skip.Id}.frame_skip.frames", 15));
        Equal(2, obj.Modifiers.Single(m => m.Type == SceneModifierType.FrameSkip).FrameSkipFrames, "Animated Frame Skip");
        var ids = new Dictionary<string, string> { [target.ObjectId] = "remapped-target" };
        ProjectSceneSerializer.RemapObjectReferences(obj, ids);
        Equal("remapped-target", obj.CopyConstraints[1].TargetObjectId, "Constraint references remain portable");
    }
}
