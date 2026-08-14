using System.Globalization;
using System.Net;
using System.Text;
using GlmSharp;
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
        VectorRow(html, "Position", "position", obj.Position);
        VectorRow(html, "Rotation", "rotation", obj.Rotation);
        VectorRow(html, "Scale", "scale", obj.Scale);
        html.Append("</div><div class='prop-section'><div class='prop-heading'>Rendering</div>");
        ButtonRow(html, "Cast shadow", "prop-shadow", obj.CastShadow ? "On" : "Off");
        ButtonRow(html, "In fog", "prop-fog", obj.IncludeInFog ? "On" : "Off");
        ButtonRow(html, "Ambient occl.", "prop-ao", obj.IncludeInAmbientOcclusion ? "On" : "Off");
        MaterialSettings material = obj.MaterialSettings ?? new MaterialSettings();
        NumberRow(html, "Metallic", "prop-metallic", material.Metallic);
        NumberRow(html, "Roughness", "prop-roughness", material.Roughness);
        NumberRow(html, "Transparency", "prop-transparency", material.Transparency);
        html.Append("</div>");
        AppendInheritance(html, obj);
        AppendSpecialized(html, obj);
        html.Append("</div>");
        _root.SetInnerRml(html.ToString());

        BindText("prop-name", value => { if (!string.IsNullOrWhiteSpace(value)) obj.Name = value.Trim(); });
        Bind("prop-visible", () => obj.ObjectVisible = !obj.ObjectVisible);
        Bind("prop-shadow", () => obj.CastShadow = !obj.CastShadow);
        Bind("prop-fog", () => obj.IncludeInFog = !obj.IncludeInFog);
        Bind("prop-ao", () => obj.IncludeInAmbientOcclusion = !obj.IncludeInAmbientOcclusion);
        BindVector("position", obj.Position, value => obj.SetLocalPosition(value));
        BindVector("rotation", obj.Rotation, value => obj.SetLocalRotation(value));
        BindVector("scale", obj.Scale, value => obj.SetLocalScale(value));
        BindNumber("prop-metallic", value => EditMaterial(m => m.Metallic = Math.Clamp(value, 0, 1)));
        BindNumber("prop-roughness", value => EditMaterial(m => m.Roughness = Math.Clamp(value, 0, 1)));
        BindNumber("prop-transparency", value => EditMaterial(m => m.Transparency = Math.Clamp(value, 0, 1)));
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
        VectorRow(html, "Pivot offset", "pivot", obj.PivotOffset);
        ButtonRow(html, "Invert faces", "prop-invert", obj.InvertFaces ? "On" : "Off");
        ButtonRow(html, "Blur texture", "prop-blur", obj.BlurTexture ? "On" : "Off");
        ButtonRow(html, "Mipmaps", "prop-mipmaps", obj.TextureMipmaps ? "On" : "Off");
        NumberRow(html, "Depth offset", "prop-depth", obj.RenderDepthOffset);
        html.Append("</div>");
    }

    private static void AppendSpecialized(StringBuilder html, SceneObject obj)
    {
        if (obj is CameraSceneObject camera)
        {
            html.Append("<div class='prop-section'><div class='prop-heading'>Camera</div>");
            ButtonRow(html, "Render camera", "prop-camera-active", camera.Active ? "Active" : "Inactive");
            NumberRow(html, "Field of view", "prop-camera-fov", camera.Fov);
            NumberRow(html, "Near clip", "prop-camera-near", camera.Near);
            NumberRow(html, "Far clip", "prop-camera-far", camera.Far);
            html.Append("</div>");
        }
        if (obj is LightSceneObject light)
        {
            html.Append("<div class='prop-section'><div class='prop-heading'>Light</div>");
            ButtonRow(html, "Type", "prop-light-type", light.Type == LightType.Point ? "Point" : "Spot");
            NumberRow(html, "Energy", "prop-light-energy", light.LightEnergy);
            NumberRow(html, "Range", "prop-light-range", light.LightRange);
            NumberRow(html, "Indirect", "prop-light-indirect", light.LightIndirectEnergy);
            NumberRow(html, "Specular", "prop-light-specular", light.LightSpecular);
            ButtonRow(html, "Shadows", "prop-light-shadow", light.LightShadowEnabled ? "On" : "Off");
            if (light.Type == LightType.Spot) { NumberRow(html, "Spot angle", "prop-light-angle", light.LightSpotAngle); NumberRow(html, "Spot blend", "prop-light-blend", light.LightSpotBlend); }
            html.Append("</div>");
        }
        if (obj is ParticleSpawnerSceneObject particle)
        {
            html.Append("<div class='prop-section'><div class='prop-heading'>Particles</div>");
            TextRow(html, "Source", "prop-particle-source", particle.ParticleLibraryDisplayName);
            ButtonRow(html, "Emitting", "prop-particle-emitting", particle.Emitting ? "On" : "Off");
            ButtonRow(html, "One shot", "prop-particle-oneshot", particle.OneShot ? "On" : "Off");
            NumberRow(html, "Amount", "prop-particle-amount", particle.Amount);
            NumberRow(html, "Spawn rate", "prop-particle-rate", particle.SpawnRate);
            NumberRow(html, "Lifetime min", "prop-particle-life-min", particle.LifetimeMin);
            NumberRow(html, "Lifetime max", "prop-particle-life-max", particle.LifetimeMax);
            NumberRow(html, "Sim speed", "prop-particle-speed", particle.SimulationSpeed);
            VectorRow(html, "Gravity", "particle-gravity", particle.Gravity);
            html.Append("<div class='prop-row'><button id='prop-particle-restart'>Restart particles</button></div></div>");
        }
        if (obj.ObjectType is "Plane" or "Cube" or "Sphere")
        {
            html.Append("<div class='prop-section'><div class='prop-heading'>Primitive</div>");
            if (obj.ObjectType == "Plane") ButtonRow(html, "Face camera", "prop-plane-face", obj.PrimitivePlaneFaceCamera ? "On" : "Off");
            if (obj.ObjectType == "Cube") ButtonRow(html, "Cube mapped", "prop-cube-map", obj.PrimitiveCubeMapped ? "On" : "Off");
            if (obj.ObjectType == "Sphere") { ButtonRow(html, "Smooth", "prop-sphere-smooth", obj.PrimitiveSphereSmooth ? "On" : "Off"); NumberRow(html, "Segments", "prop-sphere-segments", obj.PrimitiveSphereSegments); NumberRow(html, "Rings", "prop-sphere-rings", obj.PrimitiveSphereRings); }
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
        Bind("prop-inherit-position", () => obj.InheritPosition = !obj.InheritPosition);
        Bind("prop-inherit-rotation", () => obj.InheritRotation = !obj.InheritRotation);
        Bind("prop-inherit-scale", () => obj.InheritScale = !obj.InheritScale);
        Bind("prop-inherit-pivot", () => obj.InheritPivotOffset = !obj.InheritPivotOffset);
        Bind("prop-invert", () => obj.InvertFaces = !obj.InvertFaces);
        Bind("prop-blur", () => obj.BlurTexture = !obj.BlurTexture);
        Bind("prop-mipmaps", () => obj.TextureMipmaps = !obj.TextureMipmaps);
        BindNumber("prop-depth", value => obj.RenderDepthOffset = value);
        BindVector("pivot", obj.PivotOffset, value => obj.PivotOffset = value);
    }

    private void BindSpecialized(SceneObject obj)
    {
        if (obj is CameraSceneObject camera)
        {
            Bind("prop-camera-active", () => camera.ToggleActive());
            BindNumber("prop-camera-fov", value => camera.Fov = Math.Clamp(value, 1f, 179f));
            BindNumber("prop-camera-near", value => camera.Near = Math.Clamp(value, 0.001f, Math.Max(0.001f, camera.Far - 0.001f)));
            BindNumber("prop-camera-far", value => camera.Far = Math.Max(camera.Near + 0.001f, value));
        }
        if (obj is LightSceneObject light)
        {
            Bind("prop-light-type", () => light.Type = light.Type == LightType.Point ? LightType.Spot : LightType.Point);
            BindAnimatedNumber("prop-light-energy", obj, "light.energy", value => light.LightEnergy = Math.Max(0, value));
            BindAnimatedNumber("prop-light-range", obj, "light.range", value => light.LightRange = Math.Max(0.01f, value));
            BindAnimatedNumber("prop-light-indirect", obj, "light.indirect_energy", value => light.LightIndirectEnergy = Math.Max(0, value));
            BindAnimatedNumber("prop-light-specular", obj, "light.specular", value => light.LightSpecular = Math.Max(0, value));
            Bind("prop-light-shadow", () => light.LightShadowEnabled = !light.LightShadowEnabled);
            BindAnimatedNumber("prop-light-angle", obj, "light.spot_angle", value => light.LightSpotAngle = Math.Clamp(value, 0.1f, 180f));
            BindAnimatedNumber("prop-light-blend", obj, "light.spot_blend", value => light.LightSpotBlend = Math.Clamp(value, 0, light.LightSpotAngle));
        }
        if (obj is ParticleSpawnerSceneObject particle)
        {
            Bind("prop-particle-emitting", () => { particle.Emitting = !particle.Emitting; particle.ResetRuntime(); _timeline?.RecordAutoKeyframe(obj, "particle.emitting"); });
            Bind("prop-particle-oneshot", () => { particle.OneShot = !particle.OneShot; particle.ResetRuntime(); _timeline?.RecordAutoKeyframe(obj, "particle.one_shot"); });
            BindParticleNumber("prop-particle-amount", obj, particle, "particle.amount", value => particle.Amount = Math.Clamp((int)value, 1, 10000));
            BindParticleNumber("prop-particle-rate", obj, particle, "particle.spawn_rate", value => particle.SpawnRate = Math.Max(0, value));
            BindParticleNumber("prop-particle-life-min", obj, particle, "particle.lifetime_min", value => particle.LifetimeMin = Math.Max(0.01f, value));
            BindParticleNumber("prop-particle-life-max", obj, particle, "particle.lifetime_max", value => particle.LifetimeMax = Math.Max(0.01f, value));
            BindParticleNumber("prop-particle-speed", obj, particle, "particle.simulation_speed", value => particle.SimulationSpeed = Math.Max(0, value));
            BindVector("particle-gravity", particle.Gravity, value => { particle.Gravity = value; particle.ResetRuntime(); });
            Bind("prop-particle-restart", particle.ResetRuntime);
        }
        Bind("prop-plane-face", () => obj.PrimitivePlaneFaceCamera = !obj.PrimitivePlaneFaceCamera);
        Bind("prop-cube-map", () => _operations?.ApplyCubeUvMapping(!obj.PrimitiveCubeMapped));
        Bind("prop-sphere-smooth", () => { obj.PrimitiveSphereSmooth = !obj.PrimitiveSphereSmooth; RebuildSphere(obj); });
        BindNumber("prop-sphere-segments", value => { obj.PrimitiveSphereSegments = Math.Clamp((int)value, 3, 256); RebuildSphere(obj); });
        BindNumber("prop-sphere-rings", value => { obj.PrimitiveSphereRings = Math.Clamp((int)value, 2, 128); RebuildSphere(obj); });
    }

    private static void RebuildSphere(SceneObject obj) => obj.Visuals.OfType<SphereMesh>().FirstOrDefault()?.SetGeometry(obj.PrimitiveSphereSegments, obj.PrimitiveSphereRings, obj.PrimitiveSphereSmooth);

    private void BindAnimatedNumber(string id, SceneObject obj, string path, Action<float> setter) => BindNumber(id, value => { setter(value); _timeline?.RecordAutoKeyframe(obj, path); });
    private void BindParticleNumber(string id, SceneObject obj, ParticleSpawnerSceneObject particle, string path, Action<float> setter) => BindNumber(id, value => { setter(value); particle.ResetRuntime(); _timeline?.RecordAutoKeyframe(obj, path); });

    private void EditMaterial(Action<MaterialSettings> edit)
    {
        if (_object == null) return;
        _object.SetExplicitMaterialSettings();
        MaterialSettings material = _object.MaterialSettings ?? new MaterialSettings();
        edit(material);
        _object.MaterialSettings = material;
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
    private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
    public void Dispose() => SelectionManager.Instance.SelectionChanged -= OnSelectionChanged;
}
