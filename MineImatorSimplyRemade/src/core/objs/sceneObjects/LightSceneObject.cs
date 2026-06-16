using GlmSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public class LightSceneObject : SceneObject
{
    public vec4 LightColor;
    public float LightEnergy;
    public float LightRange;
    public float LightIndirectEnergy;
    public float LightSpecular;
    public bool LightShadowEnabled;
}