namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public sealed class ParticleSpriteSceneObject : SceneObject
{
    public string SpriteTemplate = "generic";
    public int CurrentFrame = -1;
    public float RollRadians;

    public ParticleSpriteSceneObject()
    {
        ObjectType = "Plane";
        SpawnCategory = "Primitives";
        PrimitivePlaneFaceCamera = true;
        CastShadow = false;
    }
}
