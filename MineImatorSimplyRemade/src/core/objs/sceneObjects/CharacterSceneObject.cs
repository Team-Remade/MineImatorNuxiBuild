using MineImatorSimplyRemade.core.mdl.mineImator;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>
/// A scene object that represents a character loaded from a GLB or Mine Imator model file.
///
/// For Mine Imator (.mimodel) characters:
///   <see cref="BoneObjects"/> maps bone names to <see cref="MiBoneSceneObject"/> instances.
///   <see cref="ModelBendStyle"/> controls per-model bend style.
///
/// For GLB/Assimp characters:
///   <see cref="BoneObjects"/> maps bone names to plain <see cref="BoneSceneObject"/> instances.
/// </summary>
public class CharacterSceneObject : SceneObject
{
    public CharacterSceneObject()
    {
        // Characters (including Mine Imator models) should have no pivot offset —
        // the model geometry is already positioned relative to its own origin.
        PivotOffset = GlmSharp.vec3.Zero;
    }

    /// <summary>The character display name (e.g. "Steve", or the .mimodel name).</summary>
    public string CharacterName = "";

    /// <summary>
    /// Bend style for Mine Imator character models.
    /// When set to ProjectDefault, uses <see cref="MineImatorLoader.ProjectBendStyle"/>.
    /// </summary>
    public BendStyle ModelBendStyle { get; set; } = BendStyle.ProjectDefault;

    /// <summary>
    /// Dictionary mapping bone name → BoneSceneObject (or MiBoneSceneObject for .mimodel).
    /// Populated by <see cref="MineImatorLoader.CreateCharacterFromModel"/> or the Assimp loader.
    /// </summary>
    public Dictionary<string, BoneSceneObject> BoneObjects { get; } = new();

    public override string GetObjectIcon() => "Character";
}
