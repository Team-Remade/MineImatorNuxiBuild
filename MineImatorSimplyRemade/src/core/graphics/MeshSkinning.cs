using GlmSharp;
using MineImatorSimplyRemade.core.diagnostics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.graphics;

internal static class MeshSkinning
{
    internal static Dictionary<string, BoneSceneObject> BuildBoneDictionary(SceneObject root)
    {
        using var profileScope = FrameProfiler.Shared.Measure(ProfileStage.BoneLookup);
        var dict = new Dictionary<string, BoneSceneObject>(StringComparer.OrdinalIgnoreCase);
        CollectBones(root, dict);
        return dict;
    }

    private static void CollectBones(SceneObject obj, Dictionary<string, BoneSceneObject> dict)
    {
        if (obj is BoneSceneObject bone && !string.IsNullOrEmpty(bone.BoneName))
            dict.TryAdd(bone.BoneName, bone);
        foreach (var child in obj.Children) CollectBones(child, dict);
    }

    internal static void UpdateBoneMatrices(Mesh mesh, mat4 meshWorld, Dictionary<string, BoneSceneObject> boneDict)
    {
        using var profileScope = FrameProfiler.Shared.Measure(ProfileStage.BoneMatrices);
        if (mesh.BoneNames.Count == 0 || mesh.BoneInverseBindMatrices.Count == 0) return;
        mat4 meshWorldInverse = meshWorld.Inverse;
        // Reuse the mesh's matrix list; every consumer reads it immediately and never retains it.
        var matrices = mesh.BoneMatrices ??= new List<mat4>(mesh.BoneNames.Count);
        matrices.Clear();
        for (int i = 0; i < mesh.BoneNames.Count; i++)
            matrices.Add(boneDict.TryGetValue(mesh.BoneNames[i], out var bone)
                ? meshWorldInverse * bone.GetWorldMatrix() * mesh.BoneInverseBindMatrices[i]
                : mat4.Identity);
    }
}
