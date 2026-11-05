using GlmSharp;
using MineImatorSimplyRemade.core.mdl.meshes;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public sealed class ProceduralEffectSceneObject : SceneObject
{
    private ProceduralEffectSettings _settings = new();
    private ProceduralEffectSettings? _evaluatedSettings;
    private (long Tick, float Blend) _evaluatedTime;

    public ProceduralEffectSettings Settings
    {
        get => _settings;
        set => _settings = value.Validated();
    }

    public ProceduralEffectSceneObject()
    {
        ObjectType = "Procedural Effect";
        SpawnCategory = "Effects";
        PivotOffset = vec3.Zero;
        CastShadow = false;
        IncludeInAmbientOcclusion = false;
        MaterialSettings = new MaterialSettings
        {
            AlbedoColor = new vec4(0.65f, 0.8f, 1f, 1f),
            EmissionEnabled = true,
            EmissionColor = new vec4(0.4f, 0.65f, 1f, 1f),
            EmissionEnergy = 5f
        };
    }

    public void EvaluateGeometry(float seconds)
    {
        var time = ProceduralEffectGeometry.SampleTime(Settings, seconds);
        if (_evaluatedSettings == Settings && _evaluatedTime == time)
            return;
        if (Visuals.Count == 0)
            return;
        ProceduralEffectGeometry.Populate(Visuals[0], Settings, seconds);
        Visuals[0].Upload();
        _evaluatedSettings = Settings;
        _evaluatedTime = time;
    }
}
