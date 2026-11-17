using System.Globalization;
using System.Text.Json;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

public static partial class MiObjectFeatureMapper
{
    internal static SceneObject? CreateScenery(MiTemplate template,
        IReadOnlyDictionary<string, MiResource> resources, string? directory,
        Func<string, SceneObject?>? factory, Action<string>? warning)
    {
        string reference = template.Scenery ?? "";
        if (resources.TryGetValue(reference, out var resource)) reference = resource.Filename;
        if (string.IsNullOrWhiteSpace(reference) || reference == "null")
        {
            Report($"MIObject scenery '{template.Name}' has no scenery resource.", warning);
            return null;
        }
        string schematic = Path.GetFullPath(Path.Combine(directory ?? "", reference));
        // Exported caches can be the only surviving copy of edited scenery.
        // Unlike ordinary schematic reloads, do not reject these by timestamp.
        var candidates = MeshCacheData.IsCache(schematic) ? new[] { schematic } :
            new[] { schematic + ".meshcache", Path.ChangeExtension(schematic, ".meshcache"), schematic };
        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            if (factory == null)
            {
                Report($"MIObject scenery '{template.Name}' requires a scenery mesh loader: '{path}'.", warning);
                return null;
            }
            try
            {
                if (factory(path) is { } result)
                {
                    result.SourceAssetPath = path;
                    return result;
                }
                Report($"MIObject scenery '{template.Name}' could not load '{path}'; trying the next source.", warning);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            {
                Report($"MIObject scenery '{template.Name}' could not load '{path}': {ex.Message}", warning);
            }
        }
        Report($"MIObject scenery '{template.Name}': no usable meshcache or schematic for '{schematic}'.", warning);
        return null;
    }

    internal static void ApplyNativeChannels(SceneObject obj, MiTimeline source, MiTemplate? template,
        IReadOnlyDictionary<string, MiResource> resources, string? directory,
        IReadOnlyDictionary<string, SceneObject> ids, Action<string>? warning)
    {
        var frames = Frames(source);
        MiKeyframe? first = source.Animated == false ? source.CurrentValues : frames.FirstOrDefault().Key;
        var mapped = new HashSet<string>(StringComparer.Ordinal)
        {
            "POS_X", "POS_Y", "POS_Z", "ROT_X", "ROT_Y", "ROT_Z", "SCA_X", "SCA_Y", "SCA_Z",
            "BEND_ANGLE_X", "BEND_ANGLE_Y", "BEND_ANGLE_Z", "ALPHA", "RGB_MUL", "MIX_COLOR", "MIX_PERCENT",
            "GLOW_COLOR", "EMISSIVE", "METALLIC", "ROUGHNESS", "SUBSURFACE", "SUBSURFACE_RADIUS_RED",
            "SUBSURFACE_RADIUS_GREEN", "SUBSURFACE_RADIUS_BLUE", "SUBSURFACE_COLOR", "VISIBLE", "HIDE",
            "TRANSITION", "EASE_IN_X", "EASE_IN_Y", "EASE_OUT_X", "EASE_OUT_Y", "TEXTURE_OBJ",
            "CUSTOM_ITEM_SLOT", "ITEM_SLOT", "IK_TARGET", "IK_TARGET_ANGLE", "IK_ANGLE_OFFSET", "IK_BLEND",
            "PATH_OBJ", "PATH_OFFSET", "PATH_POINT_ANGLE", "PATH_POINT_SCALE",
            "SPAWN", "FREEZE", "FORCE", "ATTRACTOR", "CUSTOM_SEED", "SEED", "CLEAR", "FORCE_DIRECTIONAL", "FORCE_VORTEX"
        };
        var timeline = new Timeline();
        if (template?.Item is { } item)
        {
            obj.ItemBounce = item.Bounce;
            obj.ItemSpin = item.Spin;
        }
        StaticFlag("lock", value => obj.IsSelectable = !value);
        StaticFlag("wind", value => obj.AffectedByWind = value);
        StaticFlag("wind_terrain", value => obj.TerrainAffectedByWind = value);
        if (source.ExtraValues.TryGetValue("blend_mode", out var blend))
        {
            string mode = Text(blend, "normal");
            if (mode is "normal" or "add" or "subtract" or "multiply" or "screen")
                obj.MaterialSettings.BlendMode = mode switch
                {
                    "add" => MaterialBlendMode.Add, "subtract" => MaterialBlendMode.Subtract,
                    "multiply" => MaterialBlendMode.Multiply, "screen" => MaterialBlendMode.Screen,
                    _ => MaterialBlendMode.Normal
                };
            else Report($"MIObject '{source.Name}': blend mode '{mode}' has no native mapping.", warning);
        }

        foreach (var (channel, target, fallback) in new[]
        {
            ("RGB_ADD", "add_rgb", "#000000"), ("RGB_SUB", "subtract_rgb", "#000000"),
            ("HSB_ADD", "add_hsv", "#000000"), ("HSB_SUB", "subtract_hsv", "#000000"),
            ("HSB_MUL", "multiply_hsv", "#FFFFFF")
        })
            Color(channel, (obj is CameraSceneObject ? "camera." : "material.") + target, fallback);
        Add("NORMAL_STRENGTH", "material.normal_strength", 1f);
        if (new[] { "METALLIC", "ROUGHNESS", "SUBSURFACE" }.Any(c => Has(source, frames, c)))
            obj.MaterialSettings.PbrEnabled = true;
        if (template != null)
        {
            JsonElement settings = template.Shape ?? template.Block ?? default;
            string normal = template.ModelTexNormal ?? Text(Property(settings, "tex_normal"), "");
            if (template.Item is { } normalItem)
                normal = Text(PropertyValue(normalItem.ExtraValues, "tex_normal"), normal);
            TemplateTexture(normal, "material.normal_texture");
            UnsupportedMaterial(template.ModelTexMaterial ?? Text(Property(settings, "tex_material"), ""));
            if (template.Item is { } texturedItem)
            {
                UnsupportedMaterial(Text(PropertyValue(texturedItem.ExtraValues, "tex_material"), ""));
            }
            Texture("TEXTURE_NORMAL_OBJ", "material.normal_texture", normal);
            if (Has(source, frames, "TEXTURE_MATERIAL_OBJ"))
                Report($"MIObject '{source.Name}': packed material texture channels require SEUS/LabPBR conversion and were not applied; scalar materials are retained.", warning);
            mapped.Add("TEXTURE_MATERIAL_OBJ");
        }

        if (obj is LightSceneObject light)
        {
            light.LightShadowEnabled = source.Shadows ?? true;
            Add("LIGHT_STRENGTH", "light.energy", 1, force: true);
            Add("LIGHT_RANGE", "light.range", 250, 1f / 16f, force: true);
            Add("LIGHT_SPECULAR_STRENGTH", "light.specular", 1, 0.5f, force: true);
            Color("LIGHT_COLOR", "light.color", "#FFFFFF");
            if (light.Type == LightType.Spot)
            {
                Add("LIGHT_SPOT_RADIUS", "light.spot_angle", 50, force: true);
                Map(["LIGHT_SPOT_RADIUS", "LIGHT_SPOT_SHARPNESS"], "light.spot_blend",
                    key => Read(key, "LIGHT_SPOT_RADIUS", 50) * (1 - Read(key, "LIGHT_SPOT_SHARPNESS", 0.5f)));
            }
        }
        if (obj is CameraSceneObject camera)
        {
            Add("CAM_FOV", "camera.fov", 45, force: true);
            Add("CAM_CLIP_NEAR", "camera.near", 1, 1f / 16f, force: true);
            Add("CAM_BLADE_AMOUNT", "camera.blade_count", 0, discrete: true);
            Add("CAM_BLADE_ANGLE", "camera.blade_angle", 0);
            Add("CAM_ROTATE", "camera.orbit_enabled", 0, discrete: true);
            Add("CAM_ROTATE_DISTANCE", "camera.orbit_distance", 100);
            Add("CAM_ROTATE_ANGLE_XY", "camera.orbit_rotation_xy", 0);
            Add("CAM_ROTATE_ANGLE_Z", "camera.orbit_rotation_z", 0);
            camera.ColorManagementOverrideEnabled = true;
            Map(["CAM_LIGHT_MANAGEMENT", "CAM_TONEMAPPER"], "camera.color_management",
                key => Read(key, "CAM_LIGHT_MANAGEMENT", 0) != 0 && Read(key, "CAM_TONEMAPPER", 0) != 0 ? 1f : 0f, true);
            Map(["CAM_TONEMAPPER"], "camera.tonemapper", key => (int)Read(key, "CAM_TONEMAPPER", 0) switch
            {
                0 or 1 => 0, 2 => 1, 3 => 8, 4 => 7,
                5 => ApproximateAces(),
                _ => throw new InvalidDataException("Unknown Mine-imator camera tone mapper.")
            }, true);
            Map(["CAM_EXPOSURE"], "camera.exposure", key =>
            {
                float exposure = Read(key, "CAM_EXPOSURE", 1);
                if (exposure <= 0) throw new InvalidDataException("Mine-imator camera exposure must be positive.");
                return MathF.Log2(exposure);
            });
            Add("CAM_GAMMA", "camera.gamma", 2.2f);
            CameraColor("RGB_MUL", "blend", "#FFFFFF", k => k?.MultiplyColor);
            CameraColor("MIX_COLOR", "mix", "#FFFFFF", k => k?.MixColor);
            Map(["MIX_PERCENT"], "camera.mix_amount", key => key?.MixPercent ??
                Number(Value(source, null, "MIX_PERCENT"), 0));
            CameraEffects(camera);
            camera.SyncCameraToTransform();
        }
        if (obj.ObjectType == "Text Mesh")
        {
            Map(["TEXT"], "text.string", key => Text(Value(source, key, "TEXT"), obj.TextMeshBaseString), true);
            Map(["TEXT_HALIGN"], "text.horizontal_alignment", key => Alignment(key, "TEXT_HALIGN"), true);
            Map(["TEXT_VALIGN"], "text.vertical_alignment", key => Alignment(key, "TEXT_VALIGN"), true);
            Add("TEXT_AA", "text.antialiasing", 0, discrete: true);
            Add("TEXT_OUTLINE", "text.outline_thickness", 0);
            Map(["TEXT_OUTLINE"], "text.outline_enabled", key => Read(key, "TEXT_OUTLINE", 0) > 0 ? 1f : 0f, true);
            Color("TEXT_OUTLINE_COLOR", "text.outline", "#FFFFFF");
            string font = Text(Property(template?.Text ?? default, "font"), "");
            TemplateTexture(font, "text.font");
            Texture("TEXT_FONT", "text.font", font);
        }

        Copy("POS_TARGET", "COPY_POS", CopyConstraintType.Position);
        Copy("ROT_TARGET", "COPY_ROT", CopyConstraintType.Rotation);
        Copy("LOOK_AT_TARGET", "LOOK_AT", CopyConstraintType.LookAt);
        Copy("SCALE_TARGET", "COPY_SCALE", CopyConstraintType.Scale);
        Modifier("MODIFIER_SHAKE", SceneModifierType.Shake,
        [
            ("MODIFIER_SHAKE_POSITION", "shake.position.enabled", 0, true),
            ("MODIFIER_SHAKE_ROTATION", "shake.rotation.enabled", 0, true),
            ("MODIFIER_SHAKE_BEND", "shake.bend.enabled", 0, true),
            ("MODIFIER_SHAKE_POSITION_POWER", "shake.position_power", 0.1f, false),
            ("MODIFIER_SHAKE_ROTATION_POWER", "shake.rotation_power", 1, false),
            ("MODIFIER_SHAKE_BEND_POWER", "shake.bend_power", 1, false),
            ("MODIFIER_SHAKE_INTENSITY", "shake.intensity", 1, false),
            ("MODIFIER_SHAKE_SPEED", "shake.speed", 1, false),
            ("MODIFIER_SHAKE_OFFSET", "shake.offset", 0, false),
            ("MODIFIER_SHAKE_OFFSET_AUTOMATIC", "shake.automatic.enabled", 1, true),
            ("MODIFIER_SHAKE_KEYFRAME_INFLUENCE", "shake.influence", 0, false)
        ]);
        Modifier("MODIFIER_FRAMESKIP", SceneModifierType.FrameSkip,
            [("MODIFIER_FRAMESKIP_VALUE", "frame_skip.frames", 1, true)]);

        foreach (string channel in (source.DefaultValues?.Values.Keys ?? Enumerable.Empty<string>())
                     .Concat(first?.ExtraValues.Keys ?? Enumerable.Empty<string>())
                     .Concat(frames.SelectMany(f => f.Key.ExtraValues.Keys)).Distinct())
            if (!mapped.Contains(channel))
                Report($"MIObject '{source.Name}': channel '{channel}' has no native mapping.", warning);
        foreach (var property in source.ExtraValues)
            if (property.Key is not ("text" or "lock" or "wind" or "wind_terrain" or
                "parent_tree_index" or "tree_extend" or "color_tag" or "parts" or "blend_mode") && Active(property.Value))
                Report($"MIObject '{source.Name}': setting '{property.Key}' has no native mapping.", warning);
        if (template != null)
        {
            foreach (var property in template.ExtraValues.Where(p => Active(p.Value)))
                Report($"MIObject '{source.Name}': template setting '{property.Key}' has no native mapping.", warning);
            if (template.ModelDefinition is { ValueKind: JsonValueKind.Object } definition &&
                Property(definition, "state") is { ValueKind: JsonValueKind.Object } state)
                foreach (var property in state.EnumerateObject())
                    if (!(property.Name == "age" && property.Value.ToString() is "baby" or "adult") &&
                        !(property.Name == "variant" && Text(Property(definition, "name"), "") == "human" &&
                          property.Value.ToString() is "alex" or "steve"))
                        Report($"MIObject '{source.Name}': builtin model state '{property.Name}={property.Value}' has no native mapping.", warning);
            NestedSettings(template.Shape, ["tex", "tex_normal", "tex_material", "tex_mapped", "tex_hoffset",
                "tex_voffset", "tex_hrepeat", "tex_vrepeat", "tex_hmirror", "tex_vmirror", "closed", "invert",
                "smooth", "detail", "face_camera"], "shape");
            NestedSettings(template.Text, ["font", "3d", "face_camera"], "text");
            NestedSettings(template.Block, template.Type == "scenery"
                ? ["tex_normal", "tex_material"]
                : ["name", "state", "tex", "tex_normal", "tex_material", "repeat_enable", "repeat"], "block");
            if (template.Item is { } templateItem)
                foreach (var property in templateItem.ExtraValues.Where(p => p.Key is not ("tex_normal" or "tex_material") && Active(p.Value)))
                    Report($"MIObject '{source.Name}': item setting '{property.Key}' has no native mapping.", warning);
        }
        obj.RefreshInheritedMaterials();

        void NestedSettings(JsonElement? settings, string[] supported, string name)
        {
            if (settings is not { ValueKind: JsonValueKind.Object } value) return;
            foreach (var property in value.EnumerateObject())
                if (!supported.Contains(property.Name) && Active(property.Value))
                    Report($"MIObject '{source.Name}': {name} setting '{property.Name}' has no native mapping.", warning);
        }

        void UnsupportedMaterial(string reference)
        {
            if (reference is not ("" or "null" or "none" or "default"))
                Report($"MIObject '{source.Name}': packed material texture '{reference}' requires SEUS/LabPBR conversion and was not applied.", warning);
        }

        void CameraColor(string channel, string target, string fallback, Func<MiKeyframe?, string?> get)
        {
            for (int component = 0; component < 3; component++)
            {
                int index = component;
                Map([channel], "camera." + target + "_" + "rgb"[component], key =>
                    ParticlePreset.Color(get(key) ?? Text(Value(source, null, channel), fallback))[index]);
            }
        }

        float Read(MiKeyframe? key, string channel, float fallback)
        {
            var value = Value(source, key, channel);
            return value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? (value.GetBoolean() ? 1f : 0f) : Number(value, fallback);
        }

        void StaticFlag(string name, Action<bool> set)
        {
            if (source.ExtraValues.TryGetValue(name, out var value)) set(Flag(value, false));
        }

        void Add(string channel, string target, float fallback, float multiplier = 1, bool discrete = false, bool force = false)
        {
            if (force) timeline.SetPropertyValue(obj, target, fallback * multiplier);
            Map([channel], target, key => Read(key, channel, fallback) * multiplier, discrete);
        }

        void Map(string[] channels, string target, Func<MiKeyframe?, object> read, bool discrete = false)
        {
            mapped.UnionWith(channels);
            if (!channels.Any(channel => Has(source, frames, channel))) return;
            timeline.SetPropertyValue(obj, target, read(first));
            if (frames.Count == 0) return;
            var keys = frames.Select(f => new ObjectKeyframe
            {
                Frame = f.Frame, Value = read(f.Key),
                InterpolationType = discrete ? "instant" : Transition(source, f.Key)
            }).ToList();
            if (keys[0].Frame > 0)
                keys.Insert(0, new ObjectKeyframe { Frame = 0, Value = keys[0].Value, InterpolationType = "instant" });
            obj.Keyframes[target] = keys;
        }

        void Color(string channel, string target, string fallback)
        {
            for (int component = 0; component < 3; component++)
            {
                int index = component;
                Map([channel], target + "." + "rgb"[component],
                    key => ParticlePreset.Color(Text(Value(source, key, channel), fallback))[index]);
            }
        }

        string ResourcePath(string reference, string target)
        {
            if (reference is "" or "null" or "none" or "default" or "-1") return "";
            if (resources.TryGetValue(reference, out var resource))
            {
                if (target == "material.normal_texture" && resource.MaterialFormat != 1)
                {
                    Report($"MIObject '{source.Name}': normal texture '{reference}' requires material-format conversion and was not applied.", warning);
                    return "";
                }
                reference = resource.Filename;
            }
            string path = Path.GetFullPath(Path.Combine(directory ?? "", reference));
            if (!File.Exists(path))
            {
                Report($"MIObject '{source.Name}': missing resource '{reference}'.", warning);
                return "";
            }
            return path;
        }

        void Texture(string channel, string target, string fallback = "") =>
            Map([channel], target, key =>
            {
                string reference = (channel == "TEXTURE_OBJ" ? key?.TextureObject : null) ??
                    Text(Value(source, key, channel), "");
                return ResourcePath(reference is "" or "null" or "default" or "-1" ? fallback : reference, target);
            }, true);

        void TemplateTexture(string reference, string target)
        {
            if (reference is "" or "null" or "none" or "default") return;
            if (!obj.Keyframes.ContainsKey(target) && !Has(source, frames,
                    target == "material.normal_texture" ? "TEXTURE_NORMAL_OBJ" : "TEXT_FONT"))
                timeline.SetPropertyValue(obj, target, ResourcePath(reference, target));
        }

        float Alignment(MiKeyframe? key, string channel) => Text(Value(source, key, channel), "center") switch
        {
            "left" or "top" => 0, "center" or "middle" => 1, "right" or "bottom" => 2,
            var invalid => throw new InvalidDataException($"Unknown Mine-imator text alignment '{invalid}'.")
        };

        float ApproximateAces()
        {
            Report($"MIObject '{source.Name}': ACES Approx uses the native ACES tone mapper.", warning);
            return 1;
        }

        void Copy(string reference, string prefix, CopyConstraintType type)
        {
            if (!Has(source, frames, reference)) return;
            int index = obj.CopyConstraints.Count;
            obj.CopyConstraints.Add(new CopyConstraint { Type = type });
            string path = $"copy_constraint.{index}.";
            Map([reference], path + "target", key =>
            {
                string id = Text(Value(source, key, reference), "");
                if (id is "" or "null" or "-1") return "";
                if (ids.TryGetValue(id, out var target)) return target.ObjectId;
                Report($"MIObject '{source.Name}': unresolved {reference} '{id}'.", warning);
                return "";
            }, true);
            Add(prefix + "_BLEND", path + "blend", 1);
            if (type == CopyConstraintType.Position)
            {
                Add("COPY_POS_CHILD", path + "rotate", 1, discrete: true, force: true);
                Add("COPY_POS_BEND", path + "bent_half", 1, discrete: true, force: true);
            }
            if (type == CopyConstraintType.Rotation)
                Add("COPY_ROT_BEND", path + "bent_half", 1, discrete: true, force: true);
            foreach (var (sourceAxis, targetAxis) in new[] { ("X", "x"), ("Y", "z"), ("Z", "y") })
            {
                if (type != CopyConstraintType.LookAt)
                    Add(prefix + "_" + sourceAxis, path + "axis." + targetAxis, 1, discrete: true);
                if (type != CopyConstraintType.Scale)
                    Add(prefix + "_OFFSET_" + sourceAxis, path + "offset." + targetAxis, 0,
                        type == CopyConstraintType.Position ? 1f / 16f : MathF.PI / 180f);
            }
            if (type == CopyConstraintType.Position)
                Report($"MIObject '{source.Name}': Copy Position uses native world-space constraint semantics; authored position is not added to its offset.", warning);
        }

        void Modifier(string toggle, SceneModifierType type,
            (string Source, string Target, float Default, bool Discrete)[] controls)
        {
            if (!new[] { toggle }.Concat(controls.Select(c => c.Source)).Any(c => Has(source, frames, c))) return;
            var modifier = new SceneModifier { Type = type, Enabled = false };
            obj.Modifiers.Add(modifier);
            string path = $"modifier.{modifier.Id}.";
            Add(toggle, path + "enabled", 0, discrete: true);
            foreach (var control in controls)
                Add(control.Source, path + control.Target, control.Default, discrete: control.Discrete, force: true);
        }

        void CameraEffects(CameraSceneObject camera)
        {
            Effect("CAM_SHAKE", CameraEffectType.CameraShake, "shake",
                [("CAM_SHAKE_MODE", "mode", 1, 1)]);
            Effect("CAM_DOF", CameraEffectType.DepthOfField, "depth_of_field",
            [
                ("CAM_DOF_DEPTH", "depth", 0, 1f / 16f),
                ("CAM_DOF_RANGE", "focus_range", 200, 1f / 16f),
                ("CAM_DOF_FADE_SIZE", "fade_size", 100, 1f / 16f),
                ("CAM_DOF_BLUR_SIZE", "blur_size", 0.01f, 100),
                ("CAM_DOF_THRESHOLD", "highlight_threshold", 0, 1),
                ("CAM_DOF_GAIN", "highlight_strength", 0, 1),
                ("CAM_DOF_FRINGE", "color_fringe", 0, 1),
                ("CAM_DOF_FRINGE_ANGLE_RED", "red_angle", 90, 1),
                ("CAM_DOF_FRINGE_ANGLE_GREEN", "green_angle", -135, 1),
                ("CAM_DOF_FRINGE_ANGLE_BLUE", "blue_angle", -45, 1),
                ("CAM_DOF_FRINGE_RED", "red_offset", 1, 1),
                ("CAM_DOF_FRINGE_GREEN", "green_offset", 1, 1),
                ("CAM_DOF_FRINGE_BLUE", "blue_offset", 1, 1)
            ]);
            Effect("CAM_BLOOM", CameraEffectType.Bloom, "bloom",
            [
                ("CAM_BLOOM_THRESHOLD", "threshold", 0.85f, 100),
                ("CAM_BLOOM_INTENSITY", "intensity", 0.4f, 100),
                ("CAM_BLOOM_RADIUS", "radius", 1, 100)
            ], "CAM_BLOOM_BLEND", "blend_color", "#FFFFFF", "CAM_BLOOM_BLEND_MODE");
            Effect("CAM_LENS_DIRT", CameraEffectType.LensDirt, "lens_dirt",
            [
                ("CAM_LENS_DIRT_BLOOM", "affected_by_bloom", 1, 1),
                ("CAM_LENS_DIRT_GLOW", "affected_by_glow", 1, 1),
                ("CAM_LENS_DIRT_RADIUS", "radius", 0.5f, 100),
                ("CAM_LENS_DIRT_INTENSITY", "intensity", 0.8f, 100),
                ("CAM_LENS_DIRT_POWER", "power", 1.5f, 100)
            ]);
            Effect("CAM_COLOR_CORRECTION", CameraEffectType.ColorCorrection, "color_correction",
            [
                ("CAM_SATURATION", "saturation", 1, 100), ("CAM_VIBRANCE", "vibrance", 0, 100),
                ("CAM_COLOR_HUE", "hue", 0, 100), ("CAM_COLOR_INVERT", "invert", 0, 1),
                ("CAM_COLOR_INVERT_INTENSITY", "invert_strength", 1, 100)
            ]);
            Effect("CAM_GRAIN", CameraEffectType.FilmGrain, "film_grain",
            [
                ("CAM_GRAIN_STRENGTH", "strength", 0.1f, 1),
                ("CAM_GRAIN_SATURATION", "saturation", 0.1f, 1), ("CAM_GRAIN_SIZE", "size", 1, 1)
            ]);
            Effect("CAM_VIGNETTE", CameraEffectType.Vignette, "vignette",
            [
                ("CAM_VIGNETTE_RADIUS", "radius", 1, 100),
                ("CAM_VIGNETTE_SOFTNESS", "softness", 0.5f, 100),
                ("CAM_VIGNETTE_STRENGTH", "strength", 1, 100)
            ], "CAM_VIGNETTE_COLOR", "color", "#000000");
            Effect("CAM_CA", CameraEffectType.ChromaticAberration, "chromatic_aberration",
            [
                ("CAM_CA_RED_OFFSET", "red_offset", 0.12f, -25),
                ("CAM_CA_GREEN_OFFSET", "green_offset", 0.08f, -25),
                ("CAM_CA_BLUE_OFFSET", "blue_offset", 0.04f, -25)
            ]);
            Effect("CAM_DISTORT", CameraEffectType.Distort, "distort",
            [
                ("CAM_DISTORT_REPEAT", "repeat_image", 0, 1),
                ("CAM_DISTORT_ZOOM_AMOUNT", "zoom_amount", 1, 100), ("CAM_DISTORT_AMOUNT", "distort_amount", 0.05f, 100)
            ]);
            Effect("CAM_BLACK_LINES", CameraEffectType.CinematicBars, "cinematic_bars",
                [("CAM_BLACK_LINES_ROTATION", "rotation", 0, 1)],
                "CAM_BLACK_LINES_COLOR", "color", "#000000");
            Effect("CAM_HEAT_DISTORTION", CameraEffectType.HeatDistort, "heat_distort",
            [
                ("CAM_HEAT_DISTORTION_STRENGTH", "strength", 1, 1),
                ("CAM_HEAT_DISTORTION_SPEED", "speed", 1, 100), ("CAM_HEAT_DISTORTION_SCALE", "scale", 1, 100)
            ]);
            Effect("CAM_OUTLINE", CameraEffectType.Outline, "outline",
            [
                ("CAM_OUTLINE_STRENGTH", "strength", 1, 100), ("CAM_OUTLINE_RADIUS", "radius", 4, 1),
                ("CAM_OUTLINE_DEPTH_THRESHOLD", "depth_threshold", 0.002f, 1),
                ("CAM_OUTLINE_DEPTH_THRESHOLD_FADE", "depth_fade", 0.005f, 1),
                ("CAM_OUTLINE_NORMAL", "normal_pass", 1, 1),
                ("CAM_OUTLINE_NORMAL_THRESHOLD", "normal_threshold", 0.1f, 1),
                ("CAM_OUTLINE_NORMAL_THRESHOLD_FADE", "normal_fade", 3, 1)
            ], "CAM_OUTLINE_COLOR", "color", "#FFFFFF", "CAM_OUTLINE_BLEND_MODE");

            void Effect(string toggle, CameraEffectType type, string group,
                (string Source, string Target, float Default, float Scale)[] controls,
                string? color = null, string? colorTarget = null, string colorDefault = "#FFFFFF", string? blendMode = null)
            {
                var channels = new[] { toggle }.Concat(controls.Select(c => c.Source))
                    .Concat(color == null ? [] : new[] { color }).Concat(blendMode == null ? [] : new[] { blendMode })
                    .Concat(type switch
                    {
                        CameraEffectType.ColorCorrection => ["CAM_CONTRAST"],
                        CameraEffectType.CinematicBars => ["CAM_BLACK_LINES_SIZE", "CAM_BLACK_LINES_OFFSET_Y"],
                        CameraEffectType.ChromaticAberration => ["CAM_CA_DISTORT_CHANNELS"],
                        CameraEffectType.CameraShake => new[] { "X", "Y", "Z" }.SelectMany(axis =>
                            new[] { "CAM_SHAKE_STRENGTH_" + axis, "CAM_SHAKE_SPEED_" + axis }).Append("CAM_SHAKE_OFFSET"),
                        _ => Enumerable.Empty<string>()
                    }).ToArray();
                mapped.UnionWith(channels);
                if (!channels.Any(c => Has(source, frames, c))) return;
                int index = camera.Effects.Count;
                camera.AddEffect(type);
                camera.Effects[index].Enabled = false;
                string path = $"camera.effect.{index}.";
                Add(toggle, path + "enabled", 0, discrete: true);
                foreach (var control in controls)
                    Add(control.Source, path + group + "." + control.Target, control.Default, control.Scale,
                        control.Target is "mode" or "color_fringe" or "normal_pass" or "invert" or "affected_by_bloom" or
                            "affected_by_glow" or "repeat_image" or "distort_rgb", force: true);
                if (type == CameraEffectType.ColorCorrection)
                    Map(["CAM_CONTRAST"], path + group + ".contrast", key => (Read(key, "CAM_CONTRAST", 0) + 1) * 100);
                if (type == CameraEffectType.LensDirt)
                    Texture("TEXTURE_OBJ", path + group + ".texture");
                if (type == CameraEffectType.ChromaticAberration)
                {
                    timeline.SetPropertyValue(obj, path + group + ".distort_rgb", 1f);
                    Report($"MIObject '{source.Name}': chromatic aberration maps radial channel scaling; source spectral blur and nonlinear distortion are not reproduced.", warning);
                }
                if (type == CameraEffectType.CinematicBars)
                {
                    timeline.SetPropertyValue(obj, path + group + ".size", 20f);
                    Map(["CAM_BLACK_LINES_SIZE"], path + group + ".size",
                        key => Math.Clamp((Read(key, "CAM_BLACK_LINES_SIZE", 0.7f) - 0.5f) * 100, 0, 50));
                    Map(["CAM_BLACK_LINES_OFFSET_Y", "CAM_BLACK_LINES_ROTATION"], path + group + ".offset",
                        key => Read(key, "CAM_BLACK_LINES_OFFSET_Y", 0) *
                            MathF.Cos(Read(key, "CAM_BLACK_LINES_ROTATION", 0) * MathF.PI / 180f));
                }
                if (type == CameraEffectType.CameraShake)
                {
                    Report($"MIObject '{source.Name}': camera shake uses native noise; rotational roll is not supported.", warning);
                    foreach (var (axis, positional, rotational) in new[] { ("x", "X", "Z"), ("y", "Z", "X"), ("z", "Y", "") })
                    {
                        timeline.SetPropertyValue(obj, path + group + ".strength." + axis, 1f / 16f);
                        timeline.SetPropertyValue(obj, path + group + ".speed." + axis, 1f);
                        string[] dependencies = ["CAM_SHAKE_MODE", "CAM_SHAKE_STRENGTH_" + positional,
                            "CAM_SHAKE_SPEED_" + positional, "CAM_SHAKE_STRENGTH_" + rotational, "CAM_SHAKE_SPEED_" + rotational];
                        Map(dependencies, path + group + ".strength." + axis, key =>
                            Read(key, "CAM_SHAKE_MODE", 1) != 0
                                ? Read(key, "CAM_SHAKE_STRENGTH_" + positional, 1) / 16f
                                : rotational == "" ? 0f : Read(key, "CAM_SHAKE_STRENGTH_" + rotational, 1) * MathF.PI / 180f);
                        Map(dependencies, path + group + ".speed." + axis, key =>
                            Read(key, "CAM_SHAKE_SPEED_" + (Read(key, "CAM_SHAKE_MODE", 1) != 0 ? positional : rotational), 1));
                        Add("CAM_SHAKE_OFFSET", path + group + ".offset." + axis, 0);
                    }
                }
                if (color != null)
                {
                    var initialColor = ParticlePreset.Color(colorDefault);
                    for (int component = 0; component < 3; component++)
                        timeline.SetPropertyValue(obj, path + group + "." + colorTarget + "." + "rgb"[component], initialColor[component]);
                    Color(color, path + group + "." + colorTarget, colorDefault);
                }
                if (blendMode != null)
                    Map([blendMode], path + group + ".mode", key => Text(Value(source, key, blendMode),
                        type == CameraEffectType.Bloom ? "add" : "normal") switch
                    {
                        "normal" => 0f, "add" => 1f, "screen" => 2f, "multiply" => 3f, "overlay" => 4f,
                        "subtract" => UnsupportedBlend(),
                        var invalid => throw new InvalidDataException($"Unknown camera blend mode '{invalid}'.")
                    }, true);

                float UnsupportedBlend()
                {
                    Report($"MIObject '{source.Name}': subtract blending for {type} has no native mapping; retaining its default blend mode.", warning);
                    return type == CameraEffectType.Bloom ? 1f : 0f;
                }
            }
        }
    }

    private static JsonElement PropertyValue(Dictionary<string, JsonElement> properties, string name) =>
        properties.TryGetValue(name, out var value) ? value : default;

    private static bool Active(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False => false,
        JsonValueKind.Number => value.GetDouble() != 0,
        JsonValueKind.String => value.GetString() is not (null or "" or "null" or "none"),
        _ => true
    };
}
