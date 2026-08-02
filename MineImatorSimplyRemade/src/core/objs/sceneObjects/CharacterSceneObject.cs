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
    /// Defaults to sharp/blocky; ProjectDefault resolves through
    /// <see cref="MineImatorLoader.ProjectBendStyle"/> when assigned.
    /// </summary>
    private BendStyle _modelBendStyle = BendStyle.Blocky;

    public BendStyle ModelBendStyle
    {
        get => _modelBendStyle;
        set => SetModelBendStyle(value);
    }

    /// <summary>
    /// Changes the bend style for this imported Mine-imator model and rebuilds
    /// its meshes. Imported models default to Modelbench's sharp/blocky style.
    /// </summary>
    public void SetModelBendStyle(BendStyle style)
    {
        if (style == BendStyle.ProjectDefault)
            style = MineImatorLoader.ProjectBendStyle;
        if (_modelBendStyle == style)
            return;

        BendStyle oldStyle = _modelBendStyle;
        _modelBendStyle = style;

        var mineImatorBones = BoneObjects.Values.OfType<MiBoneSceneObject>().ToArray();
        foreach (var bone in mineImatorBones)
            bone.SetModelBendStyle(oldStyle, style);
        foreach (var bone in mineImatorBones)
            bone.RegenerateMeshes(propagateInheritedBends: false);
    }

    /// <summary>
    /// Dictionary mapping bone name → BoneSceneObject (or MiBoneSceneObject for .mimodel).
    /// Populated by <see cref="MineImatorLoader.CreateCharacterFromModel"/> or the Assimp loader.
    /// </summary>
    public Dictionary<string, BoneSceneObject> BoneObjects { get; } = new();

    public override string GetObjectIcon() => "Character";
}
