using GlmSharp;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.project;

public static class ProjectSceneSerializer
{
    public static void WriteSceneToManifest(ProjectManifest manifest, Viewport viewport)
    {
        manifest.SceneObjects = viewport.SceneObjects
            .Select(SerializeNode)
            .ToList();
    }

    public static void LoadSceneFromManifest(ProjectManifest manifest, Viewport viewport, SpawnMenu spawnMenu)
    {
        ClearScene(viewport);

        foreach (var root in manifest.SceneObjects)
            RestoreNode(root, viewport, spawnMenu, parent: null);

        SelectionManager.Instance?.ClearSelection();
    }

    private static ProjectSceneObjectEntry SerializeNode(SceneObject obj)
    {
        var entry = new ProjectSceneObjectEntry
        {
            Name = obj.Name,
            ObjectType = obj.ObjectType,
            SpawnCategory = obj.SpawnCategory,
            BlockVariant = obj.BlockVariant,
            TextureType = obj.TextureType,
            SourceAssetPath = obj.SourceAssetPath,
            Position = ToProjectVec3(obj.Position),
            Rotation = ToProjectVec3(obj.Rotation),
            Scale = ToProjectVec3(obj.Scale),
            PivotOffset = ToProjectVec3(obj.PivotOffset),
            InheritPosition = obj.InheritPosition,
            InheritRotation = obj.InheritRotation,
            InheritScale = obj.InheritScale,
            InheritPivotOffset = obj.InheritPivotOffset,
            InheritVisibility = obj.InheritVisibility,
            ObjectVisible = obj.ObjectVisible,
            IsSelectable = obj.IsSelectable,
            HideInSceneTree = obj.HideInSceneTree
        };

        if (obj.SpawnCategory == "Items")
        {
            entry.ItemTileKey = ExtractItemTileKey(obj) ?? "";
            entry.ItemIs3D = obj.Visuals.OfType<ExtrudedItemMesh>().FirstOrDefault()?.Is3D ?? true;
        }

        if (obj is CameraSceneObject camera)
        {
            entry.CameraFov = camera.Fov;
            entry.CameraNear = camera.Near;
            entry.CameraFar = camera.Far;
        }

        if (obj is LightSceneObject light)
        {
            entry.LightColor = ToProjectVec4(light.LightColor);
            entry.LightEnergy = light.LightEnergy;
            entry.LightRange = light.LightRange;
            entry.LightIndirectEnergy = light.LightIndirectEnergy;
            entry.LightSpecular = light.LightSpecular;
            entry.LightShadowEnabled = light.LightShadowEnabled;
        }

        foreach (var child in obj.Children)
            entry.Children.Add(SerializeNode(child));

        return entry;
    }

    private static SceneObject RestoreNode(
        ProjectSceneObjectEntry entry,
        Viewport viewport,
        SpawnMenu spawnMenu,
        SceneObject? parent)
    {
        SceneObject obj = CreateSpawnedObject(entry, spawnMenu) ?? CreateFallbackObject(entry, viewport);

        ApplyBaseProperties(obj, entry);

        if (obj is CameraSceneObject camera)
        {
            camera.Fov = entry.CameraFov;
            camera.Near = entry.CameraNear;
            camera.Far = entry.CameraFar;
            camera.SyncCameraToTransform();
        }

        if (obj is LightSceneObject light)
        {
            light.LightColor = ToVec4(entry.LightColor);
            light.LightEnergy = entry.LightEnergy;
            light.LightRange = entry.LightRange;
            light.LightIndirectEnergy = entry.LightIndirectEnergy;
            light.LightSpecular = entry.LightSpecular;
            light.LightShadowEnabled = entry.LightShadowEnabled;
        }

        if (parent != null)
        {
            viewport.SceneObjects.Remove(obj);
            parent.AddChild(obj);
        }

        foreach (var child in entry.Children)
            RestoreNode(child, viewport, spawnMenu, obj);

        return obj;
    }

    private static SceneObject? CreateSpawnedObject(ProjectSceneObjectEntry entry, SpawnMenu spawnMenu)
    {
        if (spawnMenu.Viewport == null)
            return null;

        if (entry.SpawnCategory == "Items")
        {
            string tileKey = string.IsNullOrWhiteSpace(entry.ItemTileKey)
                ? (ExtractItemTileKey(entry.ObjectType) ?? "")
                : entry.ItemTileKey;
            if (string.IsNullOrWhiteSpace(tileKey)) return null;

            ItemAtlasSource atlasSource = entry.TextureType == "block"
                ? ItemAtlasSource.BlockAtlas
                : ItemAtlasSource.ItemAtlas;

            return spawnMenu.SpawnItemObject(tileKey, atlasSource, entry.ItemIs3D);
        }

        if (entry.SpawnCategory == "Blocks")
        {
            var variants = BlockRegistry.GetVariants(entry.ObjectType);
            var variant = variants.FirstOrDefault(v => v.VariantKey == entry.BlockVariant)
                          ?? variants.FirstOrDefault();
            if (variant == null) return null;
            return spawnMenu.SpawnBlockObject(entry.ObjectType, variant);
        }

        if (entry.SpawnCategory == "Camera")
            return spawnMenu.SpawnCameraObject(entry.Name);

        if (entry.SpawnCategory == "Light")
            return spawnMenu.SpawnLightObject(entry.Name);

        if (entry.SpawnCategory == "Primitives")
            return spawnMenu.SpawnPrimitiveObject(entry.ObjectType, entry.Name);

        if (!string.IsNullOrWhiteSpace(entry.SourceAssetPath) && File.Exists(entry.SourceAssetPath))
            return spawnMenu.SpawnCustomModelFromPath(entry.SourceAssetPath);

        return null;
    }

    private static SceneObject CreateFallbackObject(ProjectSceneObjectEntry entry, Viewport viewport)
    {
        var obj = new SceneObject
        {
            Name = entry.Name,
            ObjectType = string.IsNullOrWhiteSpace(entry.ObjectType) ? "Object" : entry.ObjectType,
            SpawnCategory = entry.SpawnCategory
        };

        obj.AssignObjectId();
        viewport.SceneObjects.Add(obj);
        return obj;
    }

    private static void ApplyBaseProperties(SceneObject obj, ProjectSceneObjectEntry entry)
    {
        obj.Name = entry.Name;
        obj.ObjectType = entry.ObjectType;
        obj.SpawnCategory = entry.SpawnCategory;
        obj.BlockVariant = entry.BlockVariant;
        obj.TextureType = entry.TextureType;
        obj.SourceAssetPath = entry.SourceAssetPath;

        obj.SetLocalPosition(ToVec3(entry.Position));
        obj.SetLocalRotation(ToVec3(entry.Rotation));
        obj.SetLocalScale(ToVec3(entry.Scale));

        obj.PivotOffset = ToVec3(entry.PivotOffset);
        obj.InheritPosition = entry.InheritPosition;
        obj.InheritRotation = entry.InheritRotation;
        obj.InheritScale = entry.InheritScale;
        obj.InheritPivotOffset = entry.InheritPivotOffset;
        obj.InheritVisibility = entry.InheritVisibility;
        obj.ObjectVisible = entry.ObjectVisible;
        obj.IsSelectable = entry.IsSelectable;
        obj.HideInSceneTree = entry.HideInSceneTree;
    }

    private static void ClearScene(Viewport viewport)
    {
        foreach (var obj in viewport.SceneObjects.ToList())
        {
            foreach (var mesh in obj.GetMeshInstancesRecursively())
                mesh.Dispose();
        }

        viewport.SceneObjects.Clear();
    }

    private static string? ExtractItemTileKey(SceneObject obj)
    {
        if (!string.IsNullOrWhiteSpace(obj.ObjectType))
            return ExtractItemTileKey(obj.ObjectType);
        return null;
    }

    private static string? ExtractItemTileKey(string objectType)
    {
        int open = objectType.IndexOf('[');
        int close = objectType.LastIndexOf(']');
        if (open < 0 || close <= open) return null;
        return objectType[(open + 1)..close];
    }

    private static ProjectVec3 ToProjectVec3(vec3 value)
    {
        return new ProjectVec3 { X = value.x, Y = value.y, Z = value.z };
    }

    private static vec3 ToVec3(ProjectVec3 value)
    {
        return new vec3(value.X, value.Y, value.Z);
    }

    private static ProjectVec4 ToProjectVec4(vec4 value)
    {
        return new ProjectVec4 { X = value.x, Y = value.y, Z = value.z, W = value.w };
    }

    private static vec4 ToVec4(ProjectVec4 value)
    {
        return new vec4(value.X, value.Y, value.Z, value.W);
    }
}
