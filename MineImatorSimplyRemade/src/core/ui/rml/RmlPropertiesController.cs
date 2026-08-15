using System.Globalization;
using System.Net;
using System.Text;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Selection-aware retained-mode object properties editor.</summary>
public sealed class RmlPropertiesController : IDisposable
{
    private readonly Element _root;
    private readonly Timeline? _timeline;
    private readonly PropertiesPanel? _operations;
    private SceneObject? _object;

    public RmlPropertiesController(Element root, Timeline? timeline, PropertiesPanel? operations = null)
    {
        _root = root;
        _timeline = timeline;
        _operations = operations;
        SelectionManager.Instance.SelectionChanged += OnSelectionChanged;
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        _object = SelectionManager.Instance.SelectedObjects.FirstOrDefault();
        Build();
    }

    public void Update()
    {
        SceneObject? selected = SelectionManager.Instance.SelectedObjects.FirstOrDefault();
        if (!ReferenceEquals(selected, _object)) OnSelectionChanged();
    }

    private void Build()
    {
        if (_object == null)
        {
            _root.SetInnerRml("<div style='padding:10px;color:#90939e'>Select an object to edit its properties.</div>");
            return;
        }
        SceneObject obj = _object;
        var html = new StringBuilder("""
          <style>
            #prop-scroll{position:absolute;inset:0;overflow:auto;padding:7px;}.prop-section{margin-bottom:8px;border:1px #3e4049;background:#26272d;}
            .prop-heading{padding:6px 8px;background:#30313a;color:#d9b24e;font-weight:bold;}.prop-row{display:flex;flex-direction:row;align-items:center;min-height:30px;padding:3px 7px;}
            .prop-label{width:82px;color:#aeb0b9;}.prop-row input{flex:1;min-width:45px;height:24px;margin-left:3px;background:#18191e;color:#e1e2e6;border:1px #484a54;padding:3px;}
            .prop-row button{flex:1;background:#31333b;border:1px #4a4d58;}.axis{width:13px;margin-left:4px;color:#8f94a2;}
          </style><div id="prop-scroll">
          """);
        html.Append("<div class='prop-section'><div class='prop-heading'>Object</div>");
        TextRow(html, "Name", "prop-name", obj.GetDisplayName());
        ButtonRow(html, "Visible", "prop-visible", obj.ObjectVisible ? "On" : "Off");
        html.Append("</div><div class='prop-section'><div class='prop-heading'>Transform</div>");
        VectorRow(html, "Position", "position", PropertiesPanel.GetEditablePosition(obj));
        VectorRow(html, "Rotation", "rotation", PropertiesPanel.GetEditableRotation(obj));
        VectorRow(html, "Scale", "scale", PropertiesPanel.GetEditableScale(obj));
        html.Append("</div><div class='prop-section'><div class='prop-heading'>Rendering</div>");
        ButtonRow(html, "Cast shadow", "prop-shadow", obj.CastShadow ? "On" : "Off");
        ButtonRow(html, "In fog", "prop-fog", obj.IncludeInFog ? "On" : "Off");
        ButtonRow(html, "Ambient occl.", "prop-ao", obj.IncludeInAmbientOcclusion ? "On" : "Off");
        MaterialSettings material = obj.MaterialSettings ?? new MaterialSettings();
        NumberRow(html, "Metallic", "prop-metallic", material.Metallic);
        NumberRow(html, "Roughness", "prop-roughness", material.Roughness);
        NumberRow(html, "Transparency", "prop-transparency", material.Transparency);
        ButtonRow(html, "Emission", "prop-emission", material.EmissionEnabled ? "On" : "Off");
        ButtonRow(html, "Auto emission", "prop-auto-emission", material.AutoEmission ? "On" : "Off");
        NumberRow(html, "Emission power", "prop-emission-energy", material.EmissionEnergy);
        Vector4Row(html, "Albedo RGBA", "material-albedo", material.AlbedoColor);
        Vector4Row(html, "Blend RGBA", "material-blend", material.BlendColor);
        Vector4Row(html, "Mix RGBA", "material-mix", material.MixColor);
        Vector4Row(html, "Emission RGBA", "material-emission", material.EmissionColor);
        ButtonRow(html, "Double sided", "prop-double-sided", material.DoubleSided ? "On" : "Off");
        Vector2Row(html, "UV offset", "material-offset", material.TextureOffset);
        Vector2Row(html, "UV repeat", "material-repeat", material.TextureRepeat);
        ButtonRow(html, "Mirror H", "prop-mirror-h", material.TextureMirror.x ? "On" : "Off");
        ButtonRow(html, "Mirror V", "prop-mirror-v", material.TextureMirror.y ? "On" : "Off");
        bool supportsItemImage = string.Equals(obj.SpawnCategory, "Items", StringComparison.Ordinal);
        if (supportsItemImage)
        {
            ItemAtlasSource atlasSource = GetItemAtlasSource(obj);
            ButtonRow(html, "Item atlas", "prop-item-atlas-cycle", atlasSource switch
            {
                ItemAtlasSource.BlockAtlas => "Block Atlas",
                ItemAtlasSource.LocalAtlas => "Local Atlas",
                _ => "Item Atlas"
            });

            string currentItemKey = GetCurrentItemKey(obj, atlasSource);
            ButtonRow(html, "Item image", "prop-item-image-cycle", string.IsNullOrWhiteSpace(currentItemKey) ? "(none)" : currentItemKey);
            html.Append("<div class='prop-row'><button id='prop-item-custom-load'>Load custom image...</button></div>");

            if (atlasSource == ItemAtlasSource.LocalAtlas && obj.TemporaryItemSheetColumns > 0 && obj.TemporaryItemSheetRows > 0)
            {
                NumberRow(html, "Slot column", "prop-item-slot-column", obj.TemporaryItemSheetColumnIndex);
                NumberRow(html, "Slot row", "prop-item-slot-row", obj.TemporaryItemSheetRowIndex);
            }
        }
        bool supportsResourcePack = string.Equals(obj.SpawnCategory, "Blocks", StringComparison.Ordinal) ||
                                    string.Equals(obj.SpawnCategory, "Scenery", StringComparison.Ordinal);
        if (supportsResourcePack)
        {
            string currentPackId = MinecraftDataLoader.NormalizeResourcePackId(obj.ResourcePackId);
            string packLabel = string.IsNullOrWhiteSpace(currentPackId) ? "Default" : currentPackId;
            ButtonRow(html, "Resource pack", "prop-resource-pack-cycle", packLabel);
            html.Append("<div class='prop-row'><button id='prop-resource-pack-default'>Use default pack</button></div>");
        }
        html.Append("<div class='prop-row'><button id='prop-material-reset'>Reset material</button></div></div>");
        AppendInheritance(html, obj);
        AppendSpecialized(html, obj);
        html.Append("</div>");
        _root.SetInnerRml(html.ToString());

        BindText("prop-name", value => { if (!string.IsNullOrWhiteSpace(value)) obj.Name = value.Trim(); });
        Bind("prop-visible", () => obj.ObjectVisible = !obj.ObjectVisible);
        Bind("prop-shadow", () => obj.CastShadow = !obj.CastShadow);
        Bind("prop-fog", () => obj.IncludeInFog = !obj.IncludeInFog);
        Bind("prop-ao", () => obj.IncludeInAmbientOcclusion = !obj.IncludeInAmbientOcclusion);
        BindVector("position", PropertiesPanel.GetEditablePosition(obj), value => PropertiesPanel.SetEditablePosition(obj, value));
        BindVector("rotation", PropertiesPanel.GetEditableRotation(obj), value => PropertiesPanel.SetEditableRotation(obj, value));
        BindVector("scale", PropertiesPanel.GetEditableScale(obj), value => PropertiesPanel.SetEditableScale(obj, value));
        BindNumber("prop-metallic", value => EditMaterial(m => m.Metallic = Math.Clamp(value, 0, 1)));
        BindNumber("prop-roughness", value => EditMaterial(m => m.Roughness = Math.Clamp(value, 0, 1)));
        BindNumber("prop-transparency", value => EditMaterial(m => m.Transparency = Math.Clamp(value, 0, 1)));
        Bind("prop-emission", () => EditMaterial(m => m.EmissionEnabled = !m.EmissionEnabled));
        Bind("prop-auto-emission", () =>
        {
            EditMaterial(m => m.AutoEmission = !m.AutoEmission);
            _operations?.RefreshAutoEmissionMeshes(obj);
        });
        BindNumber("prop-emission-energy", value => EditMaterial(m => m.EmissionEnergy = Math.Clamp(value, 0, 10)));
        BindVector4("material-albedo", material.AlbedoColor, value => EditMaterial(m => m.AlbedoColor = value));
        BindVector4("material-blend", material.BlendColor, value => EditMaterial(m => m.BlendColor = value));
        BindVector4("material-mix", material.MixColor, value => EditMaterial(m => m.MixColor = value));
        BindVector4("material-emission", material.EmissionColor, value => EditMaterial(m => m.EmissionColor = value));
        Bind("prop-double-sided", () => EditMaterial(m => m.DoubleSided = !m.DoubleSided));
        BindVector2("material-offset", material.TextureOffset, value => EditMaterial(m => m.TextureOffset = value));
        BindVector2("material-repeat", material.TextureRepeat, value => EditMaterial(m => m.TextureRepeat = new vec2(Math.Max(0.0001f, value.x), Math.Max(0.0001f, value.y))));
        Bind("prop-mirror-h", () => EditMaterial(m => m.TextureMirror = new bvec2(!m.TextureMirror.x, m.TextureMirror.y)));
        Bind("prop-mirror-v", () => EditMaterial(m => m.TextureMirror = new bvec2(m.TextureMirror.x, !m.TextureMirror.y)));
        if (supportsItemImage)
        {
            Bind("prop-item-atlas-cycle", () => CycleItemAtlas(obj));
            Bind("prop-item-image-cycle", () => CycleItemImage(obj));
            Bind("prop-item-custom-load", () => _operations?.ImportCustomItemImageAndApply(obj));

            if (GetItemAtlasSource(obj) == ItemAtlasSource.LocalAtlas && obj.TemporaryItemSheetColumns > 0 && obj.TemporaryItemSheetRows > 0)
            {
                BindNumber("prop-item-slot-column", value =>
                {
                    int column = Math.Clamp((int)value, 0, obj.TemporaryItemSheetColumns - 1);
                    int row = Math.Clamp(obj.TemporaryItemSheetRowIndex, 0, obj.TemporaryItemSheetRows - 1);
                    if (_operations?.ApplyTemporaryItemSheetSlot(obj, column, row) == true)
                    {
                        _timeline?.RecordAutoKeyframe(obj, "item.slot");
                        _timeline?.RecordAutoKeyframe(obj, "item.custom_slot");
                    }
                });
                BindNumber("prop-item-slot-row", value =>
                {
                    int column = Math.Clamp(obj.TemporaryItemSheetColumnIndex, 0, obj.TemporaryItemSheetColumns - 1);
                    int row = Math.Clamp((int)value, 0, obj.TemporaryItemSheetRows - 1);
                    if (_operations?.ApplyTemporaryItemSheetSlot(obj, column, row) == true)
                    {
                        _timeline?.RecordAutoKeyframe(obj, "item.slot");
                        _timeline?.RecordAutoKeyframe(obj, "item.custom_slot");
                    }
                });
            }
        }
        if (supportsResourcePack)
        {
            Bind("prop-resource-pack-cycle", () => CycleResourcePack(obj));
            Bind("prop-resource-pack-default", () => _operations?.ApplyResourcePack(obj, ""));
        }
        Bind("prop-material-reset", () =>
        {
            obj.MaterialSettings = new MaterialSettings();
            obj.SetExplicitMaterialSettings();
            obj.PropagateMaterialSettingsToChildren();
            _operations?.RefreshAutoEmissionMeshes(obj);
        });
        BindInheritance(obj);
        BindSpecialized(obj);
    }

    private static void AppendInheritance(StringBuilder html, SceneObject obj)
    {
        html.Append("<div class='prop-section'><div class='prop-heading'>Inheritance &amp; Geometry</div>");
        ButtonRow(html, "Position", "prop-inherit-position", obj.InheritPosition ? "Inherited" : "Local");
        ButtonRow(html, "Rotation", "prop-inherit-rotation", obj.InheritRotation ? "Inherited" : "Local");
        ButtonRow(html, "Scale", "prop-inherit-scale", obj.InheritScale ? "Inherited" : "Local");
        ButtonRow(html, "Pivot", "prop-inherit-pivot", obj.InheritPivotOffset ? "Inherited" : "Local");
        ButtonRow(html, "Visibility", "prop-inherit-visibility", obj.InheritVisibility ? "Inherited" : "Local");
        ButtonRow(html, "Visible", "prop-visible", obj.ObjectVisible ? "On" : "Off");
        if (obj is not CameraSceneObject and not LightSceneObject)
            ButtonRow(html, "Cast shadows", "prop-cast-shadow", obj.CastShadow ? "On" : "Off");
        VectorRow(html, "Pivot offset", "pivot", obj.PivotOffset);
        ButtonRow(html, "Invert faces", "prop-invert", obj.InvertFaces ? "On" : "Off");
        ButtonRow(html, "Blur texture", "prop-blur", obj.BlurTexture ? "On" : "Off");
        ButtonRow(html, "Mipmaps", "prop-mipmaps", obj.TextureMipmaps ? "On" : "Off");
        ButtonRow(html, "Ambient occlusion", "prop-include-ao", obj.IncludeInAmbientOcclusion ? "On" : "Off");
        ButtonRow(html, "Fog", "prop-include-fog", obj.IncludeInFog ? "On" : "Off");
        ButtonRow(html, "High quality", "prop-render-hq", obj.RenderInHighQuality ? "On" : "Off");
        ButtonRow(html, "Low quality", "prop-render-lq", obj.RenderInLowQuality ? "On" : "Off");
        NumberRow(html, "Depth offset", "prop-depth", obj.RenderDepthOffset);
        html.Append("</div>");
    }

    private static void AppendSpecialized(StringBuilder html, SceneObject obj)
    {
        if (string.Equals(obj.SpawnCategory, "Blocks", StringComparison.Ordinal))
        {
            html.Append("<div class='prop-section'><div class='prop-heading'>Block Tiling</div>");
            NumberRow(html, "X", "prop-tile-x", obj.GetEffectiveTileX());
            NumberRow(html, "Y", "prop-tile-y", obj.GetEffectiveTileY());
            NumberRow(html, "Z", "prop-tile-z", obj.GetEffectiveTileZ());
            html.Append("<div class='prop-row'><button id='prop-tile-reset'>Reset tiling</button></div></div>");
        }
        if (obj is MiBoneSceneObject bone && bone.BendParameters is { } bend &&
            (bend.AxisX || bend.AxisY || bend.AxisZ))
        {
            vec3 angle = bone.GetEditableBendAngle();
            html.Append("<div class='prop-section'><div class='prop-heading'>Bone Bend</div>");
            if (bend.AxisX) NumberRow(html, "X degrees", "prop-bend-x", angle.x);
            if (bend.AxisY) NumberRow(html, "Y degrees", "prop-bend-y", angle.y);
            if (bend.AxisZ) NumberRow(html, "Z degrees", "prop-bend-z", angle.z);
            html.Append("<div class='prop-row'><button id='prop-bend-reset'>Reset bend</button></div></div>");
        }
        if (obj is CharacterSceneObject character && character.BoneObjects.Values.Any(bone => bone is MiBoneSceneObject))
        {
            html.Append("<div class='prop-section'><div class='prop-heading'>Character</div>");
            ButtonRow(html, "Bend style", "prop-character-bends", character.ModelBendStyle == BendStyle.Blocky ? "Sharp" : "Smooth");
            html.Append("</div>");
        }
        if (obj is CameraSceneObject camera)
        {
            html.Append("<div class='prop-section'><div class='prop-heading'>Camera</div>");
            ButtonRow(html, "Render camera", "prop-camera-active", camera.Active ? "Active" : "Inactive");
            NumberRow(html, "Field of view", "prop-camera-fov", camera.Fov);
            NumberRow(html, "Near clip", "prop-camera-near", camera.Near);
            NumberRow(html, "Far clip", "prop-camera-far", camera.Far);
            html.Append("</div>");
            html.Append("<div class='prop-section'><div class='prop-heading'>Camera Effects</div>");
            html.Append("<div class='prop-row'><button id='prop-effect-add-shake'>Add camera shake</button><button id='prop-effect-add-grain'>Add film grain</button></div>");
            if (camera.Effects.Count == 0)
                html.Append("<div class='prop-row'><span style='color:#90939e'>No effects added.</span></div>");
            for (int i = 0; i < camera.Effects.Count; i++)
                AppendCameraEffect(html, camera.Effects[i], i);
            html.Append("</div>");
        }
        if (obj is LightSceneObject light)
        {
            html.Append("<div class='prop-section'><div class='prop-heading'>Light</div>");
            ButtonRow(html, "Type", "prop-light-type", light.Type == LightType.Point ? "Point" : "Spot");
            VectorRow(html, "Color", "light-color", new vec3(light.LightColor.r, light.LightColor.g, light.LightColor.b));
            NumberRow(html, "Energy", "prop-light-energy", light.LightEnergy);
            NumberRow(html, "Range", "prop-light-range", light.LightRange);
            NumberRow(html, "Indirect", "prop-light-indirect", light.LightIndirectEnergy);
            NumberRow(html, "Specular", "prop-light-specular", light.LightSpecular);
            ButtonRow(html, "Shadows", "prop-light-shadow", light.LightShadowEnabled ? "On" : "Off");
            if (light.Type == LightType.Spot) { NumberRow(html, "Spot angle", "prop-light-angle", light.LightSpotAngle); NumberRow(html, "Spot blend", "prop-light-blend", light.LightSpotBlend); }
            html.Append("<div class='prop-row'><button id='prop-light-reset'>Reset light</button></div>");
            html.Append("</div>");
        }
        if (obj is ParticleSpawnerSceneObject particle)
        {
            html.Append("<div class='prop-section'><div class='prop-heading'>Particles</div>");
            ButtonRow(html, "Source", "prop-particle-source", string.IsNullOrWhiteSpace(particle.ParticleLibraryEntryId) ? "(none)" : particle.ParticleLibraryDisplayName);
            html.Append("<div class='prop-row'><button id='prop-particle-source-clear'>Clear source</button></div>");
            ButtonRow(html, "Emitting", "prop-particle-emitting", particle.Emitting ? "On" : "Off");
            ButtonRow(html, "One shot", "prop-particle-oneshot", particle.OneShot ? "On" : "Off");
            ButtonRow(html, "Top level", "prop-particle-top-level", particle.TopLevelParticles ? "On" : "Off");
            NumberRow(html, "Amount", "prop-particle-amount", particle.Amount);
            NumberRow(html, "Spawn rate", "prop-particle-rate", particle.SpawnRate);
            NumberRow(html, "Lifetime min", "prop-particle-life-min", particle.LifetimeMin);
            NumberRow(html, "Lifetime max", "prop-particle-life-max", particle.LifetimeMax);
            NumberRow(html, "Sim speed", "prop-particle-speed", particle.SimulationSpeed);
            NumberRow(html, "Linear damping", "prop-particle-linear-damping", particle.LinearDamping);
            NumberRow(html, "Angular damping", "prop-particle-angular-damping", particle.AngularDamping);
            ButtonRow(html, "Shape", "prop-particle-shape", particle.EmissionShape == ParticleEmissionShape.Box ? "Box" : "Sphere");
            VectorRow(html, "Spawn extents", "particle-spawn-extents", particle.SpawnBoxExtents);
            ButtonRow(html, "Directional", "prop-particle-directional", particle.UseDirectionalEmission ? "On" : "Off");
            if (particle.UseDirectionalEmission)
            {
                VectorRow(html, "Direction", "particle-direction", particle.Direction);
                NumberRow(html, "Spread", "prop-particle-spread", particle.SpreadDegrees);
                NumberRow(html, "Speed min", "prop-particle-speed-min", particle.InitialSpeedMin);
                NumberRow(html, "Speed max", "prop-particle-speed-max", particle.InitialSpeedMax);
            }
            else
            {
                VectorRow(html, "Velocity min", "particle-velocity-min", particle.InitialVelocityMin);
                VectorRow(html, "Velocity max", "particle-velocity-max", particle.InitialVelocityMax);
            }
            VectorRow(html, "Gravity", "particle-gravity", particle.Gravity);
            VectorRow(html, "Rotation min", "particle-rotation-min", particle.InitialRotationMinDegrees);
            VectorRow(html, "Rotation max", "particle-rotation-max", particle.InitialRotationMaxDegrees);
            VectorRow(html, "Angular vel. min", "particle-angular-velocity-min", particle.AngularVelocityMinDegrees);
            VectorRow(html, "Angular vel. max", "particle-angular-velocity-max", particle.AngularVelocityMaxDegrees);
            NumberRow(html, "Start scale min", "prop-particle-start-scale-min", particle.StartScaleMin);
            NumberRow(html, "Start scale max", "prop-particle-start-scale-max", particle.StartScaleMax);
            NumberRow(html, "End scale min", "prop-particle-end-scale-min", particle.EndScaleMin);
            NumberRow(html, "End scale max", "prop-particle-end-scale-max", particle.EndScaleMax);
            html.Append("<div class='prop-row'><button id='prop-particle-restart'>Restart particles</button></div></div>");
        }
        if (obj.ObjectType is "Plane" or "Cube" or "Sphere")
        {
            html.Append("<div class='prop-section'><div class='prop-heading'>Primitive</div>");
            if (obj.ObjectType == "Plane")
            {
                PlaneOrientation orientation = obj.Visuals.OfType<PlaneMesh>().FirstOrDefault()?.Orientation ?? PlaneOrientation.XY;
                ButtonRow(html, "Orientation", "prop-plane-orientation", orientation == PlaneOrientation.XY ? "XY" : "XZ");
                ButtonRow(html, "Face camera", "prop-plane-face", obj.PrimitivePlaneFaceCamera ? "On" : "Off");
            }
            if (obj.ObjectType == "Cube") ButtonRow(html, "Cube mapped", "prop-cube-map", obj.PrimitiveCubeMapped ? "On" : "Off");
            if (obj.ObjectType == "Sphere") { ButtonRow(html, "Smooth", "prop-sphere-smooth", obj.PrimitiveSphereSmooth ? "On" : "Off"); NumberRow(html, "Segments", "prop-sphere-segments", obj.PrimitiveSphereSegments); NumberRow(html, "Rings", "prop-sphere-rings", obj.PrimitiveSphereRings); }
            html.Append("</div>");
        }
        AppendShapeKeys(html, obj);
        if (obj.SpawnCategory.Equals("Primitives", StringComparison.OrdinalIgnoreCase) &&
            obj.ObjectType.Equals("Text Mesh", StringComparison.OrdinalIgnoreCase))
        {
            html.Append("<div class='prop-section'><div class='prop-heading'>Text Mesh</div>");
            TextRow(html, "Base text", "prop-text-base", obj.TextMeshBaseString);
            TextRow(html, "Override", "prop-text-override", obj.TextMeshStringOverride);
            TextRow(html, "Font", "prop-text-font", obj.TextMeshFontPath);
            ButtonRow(html, "Horizontal", "prop-text-horizontal", HorizontalAlignmentName(obj.TextMeshHorizontalAlignment));
            ButtonRow(html, "Vertical", "prop-text-vertical", VerticalAlignmentName(obj.TextMeshVerticalAlignment));
            ButtonRow(html, "Antialiasing", "prop-text-antialias", obj.TextMeshAntialiasing ? "On" : "Off");
            NumberRow(html, "Font size", "prop-text-size", obj.TextMeshFontSize);
            ButtonRow(html, "Outline", "prop-text-outline", obj.TextMeshOutlineEnabled ? "On" : "Off");
            if (obj.TextMeshOutlineEnabled)
            {
                Vector4Row(html, "Outline RGBA", "text-outline-color", obj.TextMeshOutlineColor);
                NumberRow(html, "Thickness", "prop-text-outline-thickness", obj.TextMeshOutlineThickness);
            }
            ButtonRow(html, "Extruded", "prop-text-extruded", obj.TextMeshExtruded ? "On" : "Off");
            if (obj.TextMeshExtruded) NumberRow(html, "Depth", "prop-text-depth", obj.TextMeshExtrusionDepth);
            ButtonRow(html, "Face camera", "prop-text-face", obj.TextMeshFaceCamera ? "On" : "Off");
            html.Append("</div>");
        }
    }

    private void BindVector(string prefix, vec3 initial, Action<vec3> setter)
    {
        float[] values = [initial.x, initial.y, initial.z];
        for (int axis = 0; axis < 3; axis++)
        {
            int captured = axis;
            BindNumber($"prop-{prefix}-{axis}", value =>
            {
                values[captured] = value;
                setter(new vec3(values[0], values[1], values[2]));
                _timeline?.RecordAutoKeyframe(_object!, $"{prefix}.{(captured == 0 ? "x" : captured == 1 ? "y" : "z")}");
            });
        }
    }

    private void BindInheritance(SceneObject obj)
    {
        void ApplyToSubtree(Action<SceneObject> apply)
        {
            apply(obj);
            foreach (SceneObject child in obj.GetAllDescendants())
                apply(child);
        }

        Bind("prop-inherit-position", () => obj.InheritPosition = !obj.InheritPosition);
        Bind("prop-inherit-rotation", () => obj.InheritRotation = !obj.InheritRotation);
        Bind("prop-inherit-scale", () => obj.InheritScale = !obj.InheritScale);
        Bind("prop-inherit-pivot", () => obj.InheritPivotOffset = !obj.InheritPivotOffset);
        Bind("prop-inherit-visibility", () =>
        {
            bool inherited = !obj.InheritVisibility;
            obj.InheritVisibility = inherited;
            foreach (SceneObject child in obj.GetAllDescendants())
                child.InheritVisibility = inherited;
        });
        Bind("prop-visible", () =>
        {
            obj.SetObjectVisible(!obj.ObjectVisible);
            Record(obj, "visible");
        });
        Bind("prop-cast-shadow", () =>
        {
            bool enabled = !obj.CastShadow;
            ApplyToSubtree(node => node.CastShadow = enabled);
        });
        Bind("prop-invert", () =>
        {
            bool enabled = !obj.InvertFaces;
            ApplyToSubtree(node => node.InvertFaces = enabled);
        });
        Bind("prop-blur", () =>
        {
            bool enabled = !obj.BlurTexture;
            if (_operations != null)
            {
                ApplyToSubtree(node =>
                    _operations.ApplyTextureFiltering(node, enabled, node.TextureMipmaps));
            }
            else
            {
                ApplyToSubtree(node => node.BlurTexture = enabled);
            }
        });
        Bind("prop-mipmaps", () =>
        {
            bool enabled = !obj.TextureMipmaps;
            if (_operations != null)
            {
                ApplyToSubtree(node =>
                    _operations.ApplyTextureFiltering(node, node.BlurTexture, enabled));
            }
            else
            {
                ApplyToSubtree(node => node.TextureMipmaps = enabled);
            }
        });
        Bind("prop-include-ao", () =>
        {
            bool enabled = !obj.IncludeInAmbientOcclusion;
            ApplyToSubtree(node => node.IncludeInAmbientOcclusion = enabled);
        });
        Bind("prop-include-fog", () =>
        {
            bool enabled = !obj.IncludeInFog;
            ApplyToSubtree(node => node.IncludeInFog = enabled);
        });
        Bind("prop-render-hq", () =>
        {
            bool enabled = !obj.RenderInHighQuality;
            ApplyToSubtree(node => node.RenderInHighQuality = enabled);
        });
        Bind("prop-render-lq", () =>
        {
            bool enabled = !obj.RenderInLowQuality;
            ApplyToSubtree(node => node.RenderInLowQuality = enabled);
        });
        BindNumber("prop-depth", value =>
        {
            ApplyToSubtree(node => node.RenderDepthOffset = value);
        });
        BindVector("pivot", obj.PivotOffset, value => obj.PivotOffset = value);
    }

    private void BindSpecialized(SceneObject obj)
    {
        if (string.Equals(obj.SpawnCategory, "Blocks", StringComparison.Ordinal))
        {
            int[] tiles = [obj.GetEffectiveTileX(), obj.GetEffectiveTileY(), obj.GetEffectiveTileZ()];
            for (int axis = 0; axis < 3; axis++)
            {
                int index = axis;
                BindNumber($"prop-tile-{(axis == 0 ? "x" : axis == 1 ? "y" : "z")}", value =>
                {
                    tiles[index] = Math.Clamp((int)value, 1, SceneObject.MaxTilesPerAxis);
                    ApplyBlockTiling(obj, tiles[0], tiles[1], tiles[2]);
                });
            }
            Bind("prop-tile-reset", () => ApplyBlockTiling(obj, 1, 1, 1));
        }
        if (obj is MiBoneSceneObject bone && bone.BendParameters is { } bend)
        {
            BindBendAxis("prop-bend-x", bone, bend.AxisX, 0, bend.DirectionMin.x, bend.DirectionMax.x);
            BindBendAxis("prop-bend-y", bone, bend.AxisY, 1, bend.DirectionMin.y, bend.DirectionMax.y);
            BindBendAxis("prop-bend-z", bone, bend.AxisZ, 2, bend.DirectionMin.z, bend.DirectionMax.z);
            Bind("prop-bend-reset", () =>
            {
                bone.SetEditableBendAngle(vec3.Zero);
                Record(bone, "bend.x", "bend.y", "bend.z");
            });
        }
        if (obj is CharacterSceneObject character && character.BoneObjects.Values.Any(bone => bone is MiBoneSceneObject))
        {
            Bind("prop-character-bends", () =>
                character.ModelBendStyle = character.ModelBendStyle == BendStyle.Blocky
                    ? BendStyle.Realistic
                    : BendStyle.Blocky);
        }
        if (obj is CameraSceneObject camera)
        {
            Bind("prop-camera-active", () =>
            {
                camera.ToggleActive();
                _timeline?.RecordAutoKeyframe(obj, "camera.active");
            });
            BindAnimatedNumber("prop-camera-fov", obj, "camera.fov", value => camera.Fov = Math.Clamp(value, 1f, 179f));
            BindAnimatedNumber("prop-camera-near", obj, "camera.near", value => camera.Near = Math.Clamp(value, 0.001f, Math.Max(0.001f, camera.Far - 0.001f)));
            BindAnimatedNumber("prop-camera-far", obj, "camera.far", value => camera.Far = Math.Max(camera.Near + 0.001f, value));
            Bind("prop-effect-add-shake", () => camera.AddEffect(CameraEffectType.CameraShake));
            Bind("prop-effect-add-grain", () => camera.AddEffect(CameraEffectType.FilmGrain));
            for (int i = 0; i < camera.Effects.Count; i++)
                BindCameraEffect(camera, camera.Effects[i], i);
        }
        if (obj is LightSceneObject light)
        {
            Bind("prop-light-type", () =>
            {
                light.Type = light.Type == LightType.Point ? LightType.Spot : LightType.Point;
                _timeline?.RecordAutoKeyframe(obj, "light.type");
            });
            BindAnimatedVector("light-color", obj, "light.color", new vec3(light.LightColor.r, light.LightColor.g, light.LightColor.b),
                value => light.LightColor = new vec4(Math.Clamp(value.x, 0f, 1f), Math.Clamp(value.y, 0f, 1f), Math.Clamp(value.z, 0f, 1f), 1f), 0f, 1f);
            BindAnimatedNumber("prop-light-energy", obj, "light.energy", value => light.LightEnergy = Math.Clamp(value, 0f, 100f));
            BindAnimatedNumber("prop-light-range", obj, "light.range", value => light.LightRange = Math.Clamp(value, 0.01f, 500f));
            BindAnimatedNumber("prop-light-indirect", obj, "light.indirect_energy", value => light.LightIndirectEnergy = Math.Clamp(value, 0f, 16f));
            BindAnimatedNumber("prop-light-specular", obj, "light.specular", value => light.LightSpecular = Math.Clamp(value, 0f, 1f));
            Bind("prop-light-shadow", () => light.LightShadowEnabled = !light.LightShadowEnabled);
            BindAnimatedNumber("prop-light-angle", obj, "light.spot_angle", value =>
            {
                light.LightSpotAngle = Math.Clamp(value, 1f, 170f);
                light.LightSpotBlend = Math.Min(light.LightSpotBlend, light.LightSpotAngle * 0.5f);
            });
            BindAnimatedNumber("prop-light-blend", obj, "light.spot_blend", value =>
                light.LightSpotBlend = Math.Clamp(value, 0, Math.Max(0, light.LightSpotAngle * 0.5f)));
            Bind("prop-light-reset", () =>
            {
                light.Type = LightType.Point;
                light.LightEnergy = 1f;
                light.LightRange = 5f;
                light.LightIndirectEnergy = 1f;
                light.LightSpecular = 0.5f;
                light.LightShadowEnabled = true;
                light.LightColor = new vec4(1f, 1f, 1f, 1f);
                light.LightSpotAngle = 45f;
                light.LightSpotBlend = 5f;
                Record(obj, "light.type", "light.energy", "light.range", "light.indirect_energy", "light.specular", "light.color.r", "light.color.g", "light.color.b", "light.spot_angle", "light.spot_blend");
            });
        }
        if (obj is ParticleSpawnerSceneObject particle)
        {
            Bind("prop-particle-source", () => CycleParticleSource(particle));
            Bind("prop-particle-source-clear", () => particle.SetParticleSource("", ""));
            Bind("prop-particle-emitting", () => { particle.Emitting = !particle.Emitting; _timeline?.RecordAutoKeyframe(obj, "particle.emitting"); });
            Bind("prop-particle-oneshot", () => { particle.OneShot = !particle.OneShot; particle.ResetRuntime(); _timeline?.RecordAutoKeyframe(obj, "particle.one_shot"); });
            Bind("prop-particle-top-level", () => { particle.TopLevelParticles = !particle.TopLevelParticles; particle.ResetRuntime(); _timeline?.RecordAutoKeyframe(obj, "particle.top_level_particles"); });
            BindParticleNumber("prop-particle-amount", obj, particle, "particle.amount", value => particle.Amount = Math.Clamp((int)value, 1, 10000));
            BindParticleNumber("prop-particle-rate", obj, particle, "particle.spawn_rate", value => particle.SpawnRate = Math.Clamp(value, 0f, 10000f));
            BindParticleNumber("prop-particle-life-min", obj, particle, "particle.lifetime_min", value => particle.LifetimeMin = Math.Clamp(value, 0.01f, 120f));
            BindParticleNumber("prop-particle-life-max", obj, particle, "particle.lifetime_max", value => particle.LifetimeMax = Math.Clamp(value, 0.01f, 120f));
            BindParticleNumber("prop-particle-speed", obj, particle, "particle.simulation_speed", value => particle.SimulationSpeed = Math.Clamp(value, 0f, 32f));
            BindParticleNumber("prop-particle-linear-damping", obj, particle, "particle.linear_damping", value => particle.LinearDamping = Math.Clamp(value, 0f, 100f));
            BindParticleNumber("prop-particle-angular-damping", obj, particle, "particle.angular_damping", value => particle.AngularDamping = Math.Clamp(value, 0f, 100f));
            Bind("prop-particle-shape", () => { particle.EmissionShape = particle.EmissionShape == ParticleEmissionShape.Box ? ParticleEmissionShape.Sphere : ParticleEmissionShape.Box; particle.ResetRuntime(); _timeline?.RecordAutoKeyframe(obj, "particle.emission_shape"); });
            BindParticleVector("particle-spawn-extents", obj, particle, "particle.spawn_extents", particle.SpawnBoxExtents, value => particle.SpawnBoxExtents = new vec3(Math.Max(0, value.x), Math.Max(0, value.y), Math.Max(0, value.z)), 0, 1000);
            Bind("prop-particle-directional", () => { particle.UseDirectionalEmission = !particle.UseDirectionalEmission; particle.ResetRuntime(); _timeline?.RecordAutoKeyframe(obj, "particle.directional_emission"); });
            BindParticleVector("particle-direction", obj, particle, "particle.direction", particle.Direction, value => particle.Direction = value, -1, 1);
            BindParticleNumber("prop-particle-spread", obj, particle, "particle.spread", value => particle.SpreadDegrees = Math.Clamp(value, 0, 180));
            BindParticleNumber("prop-particle-speed-min", obj, particle, "particle.speed_min", value => particle.InitialSpeedMin = Math.Max(0, value));
            BindParticleNumber("prop-particle-speed-max", obj, particle, "particle.speed_max", value => particle.InitialSpeedMax = Math.Max(0, value));
            BindParticleVector("particle-velocity-min", obj, particle, "particle.velocity_min", particle.InitialVelocityMin, value => particle.InitialVelocityMin = value, -1000, 1000);
            BindParticleVector("particle-velocity-max", obj, particle, "particle.velocity_max", particle.InitialVelocityMax, value => particle.InitialVelocityMax = value, -1000, 1000);
            BindParticleVector("particle-gravity", obj, particle, "particle.gravity", particle.Gravity, value => particle.Gravity = value, -1000, 1000);
            BindParticleVector("particle-rotation-min", obj, particle, "particle.rotation_min", particle.InitialRotationMinDegrees, value => particle.InitialRotationMinDegrees = value, -3600, 3600);
            BindParticleVector("particle-rotation-max", obj, particle, "particle.rotation_max", particle.InitialRotationMaxDegrees, value => particle.InitialRotationMaxDegrees = value, -3600, 3600);
            BindParticleVector("particle-angular-velocity-min", obj, particle, "particle.angular_velocity_min", particle.AngularVelocityMinDegrees, value => particle.AngularVelocityMinDegrees = value, -3600, 3600);
            BindParticleVector("particle-angular-velocity-max", obj, particle, "particle.angular_velocity_max", particle.AngularVelocityMaxDegrees, value => particle.AngularVelocityMaxDegrees = value, -3600, 3600);
            BindParticleNumber("prop-particle-start-scale-min", obj, particle, "particle.start_scale_min", value => particle.StartScaleMin = Math.Max(0.001f, value));
            BindParticleNumber("prop-particle-start-scale-max", obj, particle, "particle.start_scale_max", value => particle.StartScaleMax = Math.Max(0.001f, value));
            BindParticleNumber("prop-particle-end-scale-min", obj, particle, "particle.end_scale_min", value => particle.EndScaleMin = Math.Max(0.001f, value));
            BindParticleNumber("prop-particle-end-scale-max", obj, particle, "particle.end_scale_max", value => particle.EndScaleMax = Math.Max(0.001f, value));
            Bind("prop-particle-restart", particle.ResetRuntime);
        }
        if (obj.SpawnCategory.Equals("Primitives", StringComparison.OrdinalIgnoreCase) &&
            obj.ObjectType.Equals("Text Mesh", StringComparison.OrdinalIgnoreCase))
        {
            BindTextMeshText("prop-text-base", obj, null, value => obj.TextMeshBaseString = value);
            BindTextMeshText("prop-text-override", obj, null, value => obj.TextMeshStringOverride = value);
            BindTextMeshText("prop-text-font", obj, "text.font", value => obj.TextMeshFontPath = value.Trim());
            BindTextMesh("prop-text-horizontal", obj, "text.horizontal_alignment", () => obj.TextMeshHorizontalAlignment = (obj.TextMeshHorizontalAlignment + 1) % 3);
            BindTextMesh("prop-text-vertical", obj, "text.vertical_alignment", () => obj.TextMeshVerticalAlignment = (obj.TextMeshVerticalAlignment + 1) % 3);
            BindTextMesh("prop-text-antialias", obj, "text.antialiasing", () => obj.TextMeshAntialiasing = !obj.TextMeshAntialiasing);
            BindTextMeshNumber("prop-text-size", obj, "text.font_size", value => obj.TextMeshFontSize = Math.Clamp(value, 1f, 512f));
            BindTextMesh("prop-text-outline", obj, "text.outline_enabled", () => obj.TextMeshOutlineEnabled = !obj.TextMeshOutlineEnabled);
            BindVector4("text-outline-color", obj.TextMeshOutlineColor, value =>
            {
                obj.TextMeshOutlineColor = value;
                Record(obj, "text.outline.r", "text.outline.g", "text.outline.b", "text.outline.a");
                RebuildTextMesh(obj);
            });
            BindTextMeshNumber("prop-text-outline-thickness", obj, "text.outline_thickness", value => obj.TextMeshOutlineThickness = Math.Clamp(value, 0f, 64f));
            BindTextMesh("prop-text-extruded", obj, null, () => obj.TextMeshExtruded = !obj.TextMeshExtruded);
            BindTextMeshNumber("prop-text-depth", obj, null, value => obj.TextMeshExtrusionDepth = Math.Clamp(value, 0.001f, 10f));
            Bind("prop-text-face", () => obj.TextMeshFaceCamera = !obj.TextMeshFaceCamera);
        }
        Bind("prop-plane-face", () => obj.PrimitivePlaneFaceCamera = !obj.PrimitivePlaneFaceCamera);
        Bind("prop-plane-orientation", () =>
        {
            PlaneMesh? plane = obj.Visuals.OfType<PlaneMesh>().FirstOrDefault();
            if (plane != null) plane.SetOrientation(plane.Orientation == PlaneOrientation.XY ? PlaneOrientation.XZ : PlaneOrientation.XY);
        });
        Bind("prop-cube-map", () => _operations?.ApplyCubeUvMapping(!obj.PrimitiveCubeMapped));
        Bind("prop-sphere-smooth", () => { obj.PrimitiveSphereSmooth = !obj.PrimitiveSphereSmooth; RebuildSphere(obj); });
        BindNumber("prop-sphere-segments", value => { obj.PrimitiveSphereSegments = Math.Clamp((int)value, 3, 256); RebuildSphere(obj); });
        BindNumber("prop-sphere-rings", value => { obj.PrimitiveSphereRings = Math.Clamp((int)value, 2, 128); RebuildSphere(obj); });
        BindShapeKeys(obj);
    }

    private static void RebuildSphere(SceneObject obj) => obj.Visuals.OfType<SphereMesh>().FirstOrDefault()?.SetGeometry(obj.PrimitiveSphereSegments, obj.PrimitiveSphereRings, obj.PrimitiveSphereSmooth);

    private static bool HasAnyShapeKeys(SceneObject obj)
    {
        foreach (var mesh in obj.GetMeshInstancesRecursively())
            if (mesh.HasShapeKeys) return true;
        return false;
    }

    private static void AppendShapeKeys(StringBuilder html, SceneObject obj)
    {
        if (!HasAnyShapeKeys(obj)) return;

        html.Append("<div class='prop-section'><div class='prop-heading'>Shape Keys</div>");
        html.Append("<div class='prop-row'><button id='prop-shapekey-reset-all'>Reset all shape keys</button></div>");

        var meshes = obj.GetMeshInstancesRecursively();
        int meshCount = meshes.Count;
        int meshIndex = 0;
        foreach (var mesh in meshes)
        {
            if (!mesh.HasShapeKeys)
            {
                meshIndex++;
                continue;
            }

            if (meshCount > 1)
                html.Append("<div class='prop-row'><span style='color:#90939e'>Mesh ").Append(meshIndex).Append("</span></div>");

            for (int i = 0; i < mesh.ShapeKeys.Count; i++)
            {
                var shapeKey = mesh.ShapeKeys[i];
                string keyId = $"prop-shapekey-{meshIndex}-{i}";
                string resetId = $"prop-shapekey-reset-{meshIndex}-{i}";
                html.Append("<div class='prop-row'><span class='prop-label'>")
                    .Append(E(shapeKey.Name))
                    .Append("</span><input id='")
                    .Append(keyId)
                    .Append("' value='")
                    .Append(shapeKey.Weight.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append("'/><button id='")
                    .Append(resetId)
                    .Append("'>Reset</button></div>");
            }

            meshIndex++;
        }

        html.Append("</div>");
    }

    private void BindShapeKeys(SceneObject obj)
    {
        if (!HasAnyShapeKeys(obj)) return;

        Bind("prop-shapekey-reset-all", () =>
        {
            foreach (var mesh in obj.GetMeshInstancesRecursively())
                mesh.ResetShapeKeys();
        });

        int meshIndex = 0;
        foreach (var mesh in obj.GetMeshInstancesRecursively())
        {
            if (!mesh.HasShapeKeys)
            {
                meshIndex++;
                continue;
            }

            for (int i = 0; i < mesh.ShapeKeys.Count; i++)
            {
                int shapeKeyIndex = i;
                int capturedMeshIndex = meshIndex;
                string path = $"shapekey.{capturedMeshIndex}.{shapeKeyIndex}";
                BindNumber($"prop-shapekey-{capturedMeshIndex}-{shapeKeyIndex}", value =>
                {
                    mesh.SetShapeKeyWeight(shapeKeyIndex, Math.Clamp(value, -1f, 1f));
                    _timeline?.RecordAutoKeyframe(obj, path);
                });
                Bind($"prop-shapekey-reset-{capturedMeshIndex}-{shapeKeyIndex}", () =>
                {
                    mesh.SetShapeKeyWeight(shapeKeyIndex, 0f);
                    _timeline?.RecordAutoKeyframe(obj, path);
                });
            }

            meshIndex++;
        }
    }

    private void ApplyBlockTiling(SceneObject obj, int x, int y, int z)
    {
        if (_operations != null)
        {
            _operations.ApplyBlockTiling(obj, x, y, z);
            return;
        }
        obj.TileX = Math.Clamp(x, 1, SceneObject.MaxTilesPerAxis);
        obj.TileY = Math.Clamp(y, 1, SceneObject.MaxTilesPerAxis);
        obj.TileZ = Math.Clamp(z, 1, SceneObject.MaxTilesPerAxis);
        Record(obj, "tile.x", "tile.y", "tile.z");
    }

    private static void AppendCameraEffect(StringBuilder html, CameraEffect effect, int index)
    {
        string prefix = $"prop-effect-{index}";
        html.Append("<div style='margin:5px;border-top:1px #484a54;padding-top:5px'>");
        html.Append("<div class='prop-row'><span class='prop-label'>")
            .Append(effect.Type == CameraEffectType.CameraShake ? "Camera Shake" : "Film Grain")
            .Append("</span><button id='").Append(prefix).Append("-remove'>Remove</button></div>");
        if (effect.Type == CameraEffectType.CameraShake)
        {
            ButtonRow(html, "Mode", $"{prefix}-mode", effect.Shake.Mode.ToString());
            NumberRow(html, "Trauma", $"{prefix}-trauma", effect.Shake.Trauma);
            VectorRow(html, "Strength", $"effect-{index}-strength", effect.Shake.Strength);
            VectorRow(html, "Speed", $"effect-{index}-speed", effect.Shake.Speed);
            VectorRow(html, "Offset", $"effect-{index}-offset", effect.Shake.Offset);
        }
        else
        {
            NumberRow(html, "Strength", $"{prefix}-strength", effect.FilmGrain.Strength);
            NumberRow(html, "Saturation", $"{prefix}-saturation", effect.FilmGrain.Saturation);
            NumberRow(html, "Size", $"{prefix}-size", effect.FilmGrain.Size);
        }
        html.Append("<div class='prop-row'><button id='").Append(prefix).Append("-reset'>Reset effect</button></div></div>");
    }

    private void BindCameraEffect(CameraSceneObject camera, CameraEffect effect, int index)
    {
        string id = $"prop-effect-{index}";
        Bind($"{id}-remove", () => camera.Effects.RemoveAt(index));
        if (effect.Type == CameraEffectType.CameraShake)
        {
            string path = $"camera.effect.{index}.shake";
            Bind($"{id}-mode", () =>
            {
                effect.Shake.Mode = (CameraShakeMode)(((int)effect.Shake.Mode + 1) % 3);
                _timeline?.RecordAutoKeyframe(camera, $"{path}.mode");
            });
            BindAnimatedNumber($"{id}-trauma", camera, $"{path}.trauma", value => effect.Shake.Trauma = Math.Clamp(value, 0f, 5f));
            BindAnimatedVector($"effect-{index}-strength", camera, $"{path}.strength", effect.Shake.Strength, value => effect.Shake.Strength = value, -100f, 100f);
            BindAnimatedVector($"effect-{index}-speed", camera, $"{path}.speed", effect.Shake.Speed, value => effect.Shake.Speed = value, -200f, 200f);
            BindAnimatedVector($"effect-{index}-offset", camera, $"{path}.offset", effect.Shake.Offset, value => effect.Shake.Offset = value, -1000f, 1000f);
            Bind($"{id}-reset", () =>
            {
                effect.Shake = new CameraShakeSettings();
                Record(camera, $"{path}.mode", $"{path}.trauma",
                    $"{path}.strength.x", $"{path}.strength.y", $"{path}.strength.z",
                    $"{path}.speed.x", $"{path}.speed.y", $"{path}.speed.z",
                    $"{path}.offset.x", $"{path}.offset.y", $"{path}.offset.z");
            });
        }
        else
        {
            string path = $"camera.effect.{index}.film_grain";
            BindAnimatedNumber($"{id}-strength", camera, $"{path}.strength", value => effect.FilmGrain.Strength = Math.Clamp(value, 0f, 1f));
            BindAnimatedNumber($"{id}-saturation", camera, $"{path}.saturation", value => effect.FilmGrain.Saturation = Math.Clamp(value, 0f, 1f));
            BindAnimatedNumber($"{id}-size", camera, $"{path}.size", value => effect.FilmGrain.Size = Math.Clamp(value, 0.25f, 8f));
            Bind($"{id}-reset", () =>
            {
                effect.FilmGrain = new FilmGrainSettings();
                Record(camera, $"{path}.strength", $"{path}.saturation", $"{path}.size");
            });
        }
    }

    private void BindAnimatedVector(string prefix, SceneObject obj, string path, vec3 initial, Action<vec3> setter, float minimum, float maximum)
    {
        float[] values = [initial.x, initial.y, initial.z];
        for (int axis = 0; axis < 3; axis++)
        {
            int index = axis;
            BindNumber($"prop-{prefix}-{axis}", value =>
            {
                values[index] = Math.Clamp(value, minimum, maximum);
                setter(new vec3(values[0], values[1], values[2]));
                _timeline?.RecordAutoKeyframe(obj, $"{path}.{(index == 0 ? "x" : index == 1 ? "y" : "z")}");
            });
        }
    }

    private void BindBendAxis(string id, MiBoneSceneObject bone, bool enabled, int axis, float minimum, float maximum)
    {
        if (!enabled) return;
        BindNumber(id, value =>
        {
            vec3 angle = bone.GetEditableBendAngle();
            value = Math.Clamp(value, minimum, maximum);
            if (axis == 0) angle.x = value;
            else if (axis == 1) angle.y = value;
            else angle.z = value;
            bone.SetEditableBendAngle(angle);
            _timeline?.RecordAutoKeyframe(bone, $"bend.{(axis == 0 ? "x" : axis == 1 ? "y" : "z")}");
        });
    }

    private void BindAnimatedNumber(string id, SceneObject obj, string path, Action<float> setter) => BindNumber(id, value => { setter(value); _timeline?.RecordAutoKeyframe(obj, path); });
    private void BindParticleNumber(string id, SceneObject obj, ParticleSpawnerSceneObject particle, string path, Action<float> setter) => BindNumber(id, value => { setter(value); particle.ResetRuntime(); _timeline?.RecordAutoKeyframe(obj, path); });

    private void BindParticleVector(string prefix, SceneObject obj, ParticleSpawnerSceneObject particle, string path, vec3 initial, Action<vec3> setter, float minimum, float maximum) =>
        BindAnimatedVector(prefix, obj, path, initial, value => { setter(value); particle.ResetRuntime(); }, minimum, maximum);

    private void CycleParticleSource(ParticleSpawnerSceneObject particle)
    {
        IReadOnlyList<(string Id, string Name, string Type)> sources = _operations?.GetParticleSourceOptions()
            ?? Array.Empty<(string, string, string)>();
        if (sources.Count == 0)
        {
            particle.SetParticleSource("", "");
            return;
        }

        int current = -1;
        for (int i = 0; i < sources.Count; i++)
            if (string.Equals(sources[i].Id, particle.ParticleLibraryEntryId, StringComparison.OrdinalIgnoreCase))
                current = i;
        int next = current + 1;
        if (next >= sources.Count)
            particle.SetParticleSource("", "");
        else
            particle.SetParticleSource(sources[next].Id, sources[next].Name);
    }

    private void CycleResourcePack(SceneObject obj)
    {
        IReadOnlyList<string> packs = _operations?.GetResourcePackOptions() ?? Array.Empty<string>();
        string currentPackId = MinecraftDataLoader.NormalizeResourcePackId(obj.ResourcePackId);
        int current = -1;
        for (int i = 0; i < packs.Count; i++)
        {
            if (string.Equals(packs[i], currentPackId, StringComparison.OrdinalIgnoreCase))
            {
                current = i;
                break;
            }
        }

        int next = current + 1;
        if (next >= packs.Count) _operations?.ApplyResourcePack(obj, "");
        else _operations?.ApplyResourcePack(obj, packs[next]);
    }

    private static string? ExtractItemTileKeyFromObjectType(string objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType))
            return null;

        int open = objectType.IndexOf('[');
        int close = objectType.LastIndexOf(']');
        if (open < 0 || close <= open)
            return null;

        return objectType[(open + 1)..close];
    }

    private ItemAtlasSource GetItemAtlasSource(SceneObject obj)
    {
        if (_operations != null)
            return _operations.GetItemAtlasSource(obj);

        if (string.Equals(obj.TextureType, "block", StringComparison.OrdinalIgnoreCase))
            return ItemAtlasSource.BlockAtlas;
        if (string.Equals(obj.TextureType, "local", StringComparison.OrdinalIgnoreCase) &&
            obj.TemporaryItemSheetColumns > 0 && obj.TemporaryItemSheetRows > 0)
            return ItemAtlasSource.LocalAtlas;
        return ItemAtlasSource.ItemAtlas;
    }

    private string GetCurrentItemKey(SceneObject obj, ItemAtlasSource atlasSource)
    {
        if (!string.IsNullOrWhiteSpace(obj.ItemTileKey))
            return obj.ItemTileKey;

        string? fromType = ExtractItemTileKeyFromObjectType(obj.ObjectType);
        if (!string.IsNullOrWhiteSpace(fromType))
            return fromType;

        if (atlasSource == ItemAtlasSource.LocalAtlas)
            return _operations?.GetLocalItemSheetOptions(obj).FirstOrDefault().Key ?? "";

        return _operations?.GetItemAtlasOptions(atlasSource).FirstOrDefault() ?? "";
    }

    private void CycleItemAtlas(SceneObject obj)
    {
        ItemAtlasSource current = GetItemAtlasSource(obj);
        bool hasLocal = obj.TemporaryItemSheetColumns > 0 && obj.TemporaryItemSheetRows > 0;
        ItemAtlasSource[] order = hasLocal
            ? [ItemAtlasSource.LocalAtlas, ItemAtlasSource.ItemAtlas, ItemAtlasSource.BlockAtlas]
            : [ItemAtlasSource.ItemAtlas, ItemAtlasSource.BlockAtlas];

        int currentIndex = Array.IndexOf(order, current);
        int nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % order.Length;
        ItemAtlasSource next = order[nextIndex];

        if (next == ItemAtlasSource.LocalAtlas)
        {
            var local = _operations?.GetLocalItemSheetOptions(obj);
            var first = local?.FirstOrDefault();
            if (first != null && _operations?.ApplyTemporaryItemSheetSlot(obj, first.Value.Column, first.Value.Row) == true)
            {
                _timeline?.RecordAutoKeyframe(obj, "item.slot");
                _timeline?.RecordAutoKeyframe(obj, "item.custom_slot");
            }
            return;
        }

        string? nextKey = _operations?.GetItemAtlasOptions(next).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(nextKey))
            _operations?.ApplyItemTexture(obj, next, nextKey);
    }

    private void CycleItemImage(SceneObject obj)
    {
        ItemAtlasSource atlasSource = GetItemAtlasSource(obj);
        string currentKey = GetCurrentItemKey(obj, atlasSource);

        if (atlasSource == ItemAtlasSource.LocalAtlas)
        {
            var local = _operations?.GetLocalItemSheetOptions(obj);
            if (local == null || local.Count == 0)
                return;

            int current = -1;
            for (int i = 0; i < local.Count; i++)
            {
                if (string.Equals(local[i].Key, currentKey, StringComparison.Ordinal))
                {
                    current = i;
                    break;
                }
            }

            int next = (current + 1 + local.Count) % local.Count;
            if (_operations?.ApplyTemporaryItemSheetSlot(obj, local[next].Column, local[next].Row) == true)
            {
                _timeline?.RecordAutoKeyframe(obj, "item.slot");
                _timeline?.RecordAutoKeyframe(obj, "item.custom_slot");
            }
            return;
        }

        IReadOnlyList<string> keys = _operations?.GetItemAtlasOptions(atlasSource) ?? Array.Empty<string>();
        if (keys.Count == 0)
            return;

        int currentIndex = -1;
        for (int i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], currentKey, StringComparison.Ordinal))
            {
                currentIndex = i;
                break;
            }
        }

        int nextIndex = (currentIndex + 1 + keys.Count) % keys.Count;
        _operations?.ApplyItemTexture(obj, atlasSource, keys[nextIndex]);
    }

    private void EditMaterial(Action<MaterialSettings> edit)
    {
        if (_object == null) return;
        _object.SetExplicitMaterialSettings();
        MaterialSettings material = _object.MaterialSettings ?? new MaterialSettings();
        edit(material);
        _object.MaterialSettings = material;
        _object.PropagateMaterialSettingsToChildren();
    }

    private void BindTextMesh(string id, SceneObject obj, string? path, Action setter) =>
        Bind(id, () => { setter(); if (path != null) _timeline?.RecordAutoKeyframe(obj, path); RebuildTextMesh(obj); });

    private void BindTextMeshText(string id, SceneObject obj, string? path, Action<string> setter) =>
        BindText(id, value => { setter(value); if (path != null) _timeline?.RecordAutoKeyframe(obj, path); RebuildTextMesh(obj); });

    private void BindTextMeshNumber(string id, SceneObject obj, string? path, Action<float> setter) =>
        BindNumber(id, value => { setter(value); if (path != null) _timeline?.RecordAutoKeyframe(obj, path); RebuildTextMesh(obj); });

    private void RebuildTextMesh(SceneObject obj) => _operations?.RebuildTextMesh(obj);
    private void Record(SceneObject obj, params string[] paths)
    {
        foreach (string path in paths) _timeline?.RecordAutoKeyframe(obj, path);
    }

    private void Bind(string id, Action action) => _root.GetElementById(id)?.AddEventListener("click", _ => { action(); Changed(); Build(); });
    private void BindText(string id, Action<string> setter)
    {
        if (_root.GetElementById(id) is not ElementFormControlInput input) return;
        input.AddEventListener("change", _ => { setter(input.GetValue()); Changed(); Build(); });
    }
    private void BindNumber(string id, Action<float> setter) => BindText(id, value =>
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)) setter(number);
    });
    private static void Changed() => ProjectManager.Instance.SetDirty(true);
    private static void TextRow(StringBuilder h, string label, string id, string value) => h.Append("<div class='prop-row'><span class='prop-label'>").Append(E(label)).Append("</span><input id='").Append(id).Append("' value='").Append(E(value)).Append("'/></div>");
    private static void ButtonRow(StringBuilder h, string label, string id, string value) => h.Append("<div class='prop-row'><span class='prop-label'>").Append(E(label)).Append("</span><button id='").Append(id).Append("'>").Append(E(value)).Append("</button></div>");
    private static void NumberRow(StringBuilder h, string label, string id, float value) => TextRow(h, label, id, value.ToString("0.###", CultureInfo.InvariantCulture));
    private static void VectorRow(StringBuilder h, string label, string prefix, vec3 value)
    {
        h.Append("<div class='prop-row'><span class='prop-label'>").Append(E(label)).Append("</span>");
        float[] v = [value.x, value.y, value.z];
        for (int i = 0; i < 3; i++) h.Append("<span class='axis'>").Append(i == 0 ? "X" : i == 1 ? "Y" : "Z").Append("</span><input id='prop-").Append(prefix).Append('-').Append(i).Append("' value='").Append(v[i].ToString("0.###", CultureInfo.InvariantCulture)).Append("'/>");
        h.Append("</div>");
    }
    private void BindVector4(string prefix, vec4 initial, Action<vec4> setter)
    {
        float[] values = [initial.x, initial.y, initial.z, initial.w];
        for (int axis = 0; axis < 4; axis++)
        {
            int index = axis;
            BindNumber($"prop-{prefix}-{axis}", value =>
            {
                values[index] = Math.Clamp(value, 0f, 1f);
                setter(new vec4(values[0], values[1], values[2], values[3]));
            });
        }
    }
    private void BindVector2(string prefix, vec2 initial, Action<vec2> setter)
    {
        float[] values = [initial.x, initial.y];
        for (int axis = 0; axis < 2; axis++)
        {
            int index = axis;
            BindNumber($"prop-{prefix}-{axis}", value =>
            {
                values[index] = value;
                setter(new vec2(values[0], values[1]));
            });
        }
    }
    private static void Vector2Row(StringBuilder h, string label, string prefix, vec2 value)
    {
        h.Append("<div class='prop-row'><span class='prop-label'>").Append(E(label)).Append("</span>");
        float[] values = [value.x, value.y];
        for (int i = 0; i < 2; i++) h.Append("<span class='axis'>").Append(i == 0 ? "H" : "V").Append("</span><input id='prop-").Append(prefix).Append('-').Append(i).Append("' value='").Append(values[i].ToString("0.###", CultureInfo.InvariantCulture)).Append("'/>");
        h.Append("</div>");
    }
    private static void Vector4Row(StringBuilder h, string label, string prefix, vec4 value)
    {
        h.Append("<div class='prop-row'><span class='prop-label'>").Append(E(label)).Append("</span>");
        float[] values = [value.x, value.y, value.z, value.w];
        for (int i = 0; i < 4; i++) h.Append("<span class='axis'>").Append("RGBA"[i]).Append("</span><input id='prop-").Append(prefix).Append('-').Append(i).Append("' value='").Append(values[i].ToString("0.###", CultureInfo.InvariantCulture)).Append("'/>");
        h.Append("</div>");
    }
    private static string HorizontalAlignmentName(int value) => value switch { 0 => "Left", 2 => "Right", _ => "Center" };
    private static string VerticalAlignmentName(int value) => value switch { 0 => "Top", 2 => "Bottom", _ => "Center" };
    private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
    public void Dispose() => SelectionManager.Instance.SelectionChanged -= OnSelectionChanged;
}
