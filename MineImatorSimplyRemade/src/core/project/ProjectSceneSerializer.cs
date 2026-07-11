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
    public static void WriteSceneToManifest(ProjectManifest manifest, Viewport viewport, Timeline? timeline = null, PropertiesPanel? propertiesPanel = null)
    {
        propertiesPanel?.WriteProjectSettingsToManifest(manifest);

        manifest.WorkCamera = new ProjectWorkCameraState
        {
            Target = ToProjectVec3(viewport.Camera.Target),
            Yaw = viewport.Camera.Yaw,
            Pitch = viewport.Camera.Pitch,
            Distance = viewport.Camera.Distance
        };

        // Persist which camera the preview viewport is currently using (0 = work camera)
        manifest.ActivePreviewCameraIndex = viewport.PreviewViewport?.SelectedCameraIndex ?? 0;

        manifest.SceneObjects = viewport.SceneObjects
            .Select(SerializeNode)
            .ToList();

        // Save currently selected object names so selection can be restored on undo/redo
        manifest.SelectedObjectNames = SelectionManager.Instance?.SelectedObjects
            .Select(obj => GetObjectPath(obj))
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList() ?? new();

        if (timeline != null)
            manifest.Timeline = timeline.ExportProjectState();
    }

    public static void LoadSceneFromManifest(ProjectManifest manifest, Viewport viewport, SpawnMenu spawnMenu, Timeline? timeline = null, PropertiesPanel? propertiesPanel = null)
    {
        propertiesPanel?.LoadProjectSettingsFromManifest(manifest);

        ApplyWorkCameraState(manifest.WorkCamera, viewport.Camera);

        ClearScene(viewport);

        foreach (var root in manifest.SceneObjects)
            RestoreNode(root, viewport, spawnMenu, parent: null);

        // Restore selection after scene is loaded
        SelectionManager.Instance?.ClearSelection();
        if (manifest.SelectedObjectNames != null && manifest.SelectedObjectNames.Count > 0 && SelectionManager.Instance != null)
        {
            foreach (var objectPath in manifest.SelectedObjectNames)
            {
                var obj = FindObjectByPath(viewport.SceneObjects, objectPath);
                if (obj != null)
                    SelectionManager.Instance.SelectObject(obj);
            }
        }

        timeline?.ImportProjectState(manifest.Timeline);

        // Keep timeline FPS aligned with project settings after timeline state is restored.
        if (propertiesPanel != null)
            timeline?.SetFrameRate(propertiesPanel.GetFramerate());

        // Restore preview viewport selected camera index (after scene objects restored so spawned cameras exist)
        if (viewport.PreviewViewport != null)
            viewport.PreviewViewport.SelectedCameraIndex = manifest.ActivePreviewCameraIndex;
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
            ResourcePackId = obj.ResourcePackId,
            SourceAssetPath = obj.SourceAssetPath,
            AlbedoTexturePath = obj.AlbedoTexturePath,
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
            HideInSceneTree = obj.HideInSceneTree,
            HasMaterialOverrides = obj.HasExplicitMaterialSettings,
            Keyframes = SerializeKeyframes(obj)
        };

        if (obj.HasExplicitMaterialSettings && obj.MaterialSettings != null)
        {
            entry.AlbedoColor = ToProjectVec4(obj.MaterialSettings.AlbedoColor);
            entry.Metallic = obj.MaterialSettings.Metallic;
            entry.Roughness = obj.MaterialSettings.Roughness;
            entry.Transparency = obj.MaterialSettings.Transparency;
            entry.DoubleSided = obj.MaterialSettings.DoubleSided;
            entry.EmissionEnabled = obj.MaterialSettings.EmissionEnabled;
            entry.EmissionColor = ToProjectVec4(obj.MaterialSettings.EmissionColor);
            entry.EmissionEnergy = obj.MaterialSettings.EmissionEnergy;
        }

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

        ApplyEntryToObject(obj, entry);

        if (parent != null)
        {
            viewport.SceneObjects.Remove(obj);
            parent.AddChild(obj);
        }

        RestoreChildren(entry, obj, viewport, spawnMenu);

        return obj;
    }

    private static void RestoreChildren(ProjectSceneObjectEntry entry, SceneObject obj, Viewport viewport, SpawnMenu spawnMenu)
    {
        var usedChildren = new HashSet<SceneObject>();

        foreach (var childEntry in entry.Children)
        {
            SceneObject? existingChild = FindMatchingChild(obj, childEntry, usedChildren);
            if (existingChild != null)
            {
                usedChildren.Add(existingChild);
                ApplyEntryToObject(existingChild, childEntry);
                RestoreChildren(childEntry, existingChild, viewport, spawnMenu);
                continue;
            }

            RestoreNode(childEntry, viewport, spawnMenu, obj);
        }
    }

    private static SceneObject? FindMatchingChild(SceneObject parent, ProjectSceneObjectEntry entry, HashSet<SceneObject> usedChildren)
    {
        foreach (var child in parent.Children)
        {
            if (usedChildren.Contains(child))
                continue;

            if (!string.IsNullOrWhiteSpace(entry.Name) && string.Equals(child.Name, entry.Name, StringComparison.Ordinal))
                return child;

            if (!string.IsNullOrWhiteSpace(entry.ObjectType) &&
                string.Equals(child.ObjectType, entry.ObjectType, StringComparison.Ordinal) &&
                string.Equals(child.SpawnCategory, entry.SpawnCategory, StringComparison.Ordinal))
                return child;
        }

        return null;
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
            return spawnMenu.SpawnBlockObject(entry.ObjectType, variant, entry.ResourcePackId);
        }

        if (entry.SpawnCategory == "Camera")
            return spawnMenu.SpawnCameraObject(entry.Name);

        if (entry.SpawnCategory == "Light")
            return spawnMenu.SpawnLightObject(entry.Name);

        if (entry.SpawnCategory == "Primitives")
            return spawnMenu.SpawnPrimitiveObject(entry.ObjectType, entry.Name);

        if (entry.SpawnCategory == "Scenery")
        {
            if (!string.IsNullOrWhiteSpace(entry.SourceAssetPath) && File.Exists(entry.SourceAssetPath))
                return spawnMenu.SpawnSchematicFromPath(entry.SourceAssetPath, entry.ResourcePackId);
            return null;
        }

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

    private static void ApplyEntryToObject(SceneObject obj, ProjectSceneObjectEntry entry)
    {
        obj.Name = entry.Name;
        obj.ObjectType = entry.ObjectType;
        obj.SpawnCategory = entry.SpawnCategory;
        obj.BlockVariant = entry.BlockVariant;
        obj.TextureType = entry.TextureType;
        obj.ResourcePackId = entry.ResourcePackId;
        obj.SourceAssetPath = entry.SourceAssetPath;
        
        // Restore albedo texture path (actual texture loading happens after scene is loaded)
        if (!string.IsNullOrEmpty(entry.AlbedoTexturePath))
        {
            obj.AlbedoTexturePath = entry.AlbedoTexturePath;
        }

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

        if (entry.HasMaterialOverrides)
        {
            var material = obj.MaterialSettings ?? new MaterialSettings();
            material.AlbedoColor = ToVec4(entry.AlbedoColor);
            material.Metallic = entry.Metallic;
            material.Roughness = entry.Roughness;
            material.Transparency = entry.Transparency;
            material.DoubleSided = entry.DoubleSided;
            material.EmissionEnabled = entry.EmissionEnabled;
            material.EmissionColor = ToVec4(entry.EmissionColor);
            material.EmissionEnergy = entry.EmissionEnergy;
            obj.MaterialSettings = material;
            obj.SetExplicitMaterialSettings();
            obj.PropagateMaterialSettingsToChildren();
        }

        obj.Keyframes = DeserializeKeyframes(entry.Keyframes);

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

    private static Dictionary<string, List<ProjectKeyframeEntry>> SerializeKeyframes(SceneObject obj)
    {
        var result = new Dictionary<string, List<ProjectKeyframeEntry>>();

        foreach (var pair in obj.Keyframes)
        {
            if (pair.Value == null || pair.Value.Count == 0)
                continue;

            result[pair.Key] = pair.Value
                .Select(kf => new ProjectKeyframeEntry
                {
                    Frame = kf.Frame,
                    Value = Convert.ToSingle(kf.Value),
                    InterpolationType = kf.InterpolationType
                })
                .OrderBy(kf => kf.Frame)
                .ToList();
        }

        return result;
    }

    private static Dictionary<string, List<ObjectKeyframe>> DeserializeKeyframes(Dictionary<string, List<ProjectKeyframeEntry>> source)
    {
        var result = new Dictionary<string, List<ObjectKeyframe>>();

        foreach (var pair in source)
        {
            if (pair.Value == null || pair.Value.Count == 0)
                continue;

            result[pair.Key] = pair.Value
                .Select(kf => new ObjectKeyframe
                {
                    Frame = kf.Frame,
                    Value = kf.Value,
                    InterpolationType = kf.InterpolationType
                })
                .OrderBy(kf => kf.Frame)
                .ToList();
        }

        return result;
    }

    private static void ApplyWorkCameraState(ProjectWorkCameraState? state, Camera camera)
    {
        if (state == null)
        {
            camera.ResetToDefaultPose();
            return;
        }

        camera.Target = ToVec3(state.Target);
        camera.Yaw = state.Yaw;
        camera.Pitch = Math.Clamp(state.Pitch, -MathF.PI / 2f + 0.01f, MathF.PI / 2f - 0.01f);
        camera.Distance = Math.Max(0.1f, state.Distance);
    }

    /// <summary>
    /// Gets a unique path for an object based on its hierarchy.
    /// Format: "RootName/ChildName/GrandchildName" etc.
    /// </summary>
    private static string GetObjectPath(SceneObject obj)
    {
        var parts = new List<string>();
        var current = obj;
        
        while (current != null)
        {
            parts.Insert(0, current.Name);
            current = current.Parent;
        }

        return string.Join("/", parts);
    }

    /// <summary>
    /// Finds an object by its hierarchical path in the scene.
    /// Path format: "RootName/ChildName/GrandchildName"
    /// </summary>
    private static SceneObject? FindObjectByPath(List<SceneObject> rootObjects, string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var parts = path.Split('/');
        if (parts.Length == 0)
            return null;

        // Find root object
        SceneObject? current = rootObjects.FirstOrDefault(obj => obj.Name == parts[0]);
        if (current == null)
            return null;

        // Find child objects through the hierarchy
        for (int i = 1; i < parts.Length; i++)
        {
            current = current.Children.FirstOrDefault(child => child.Name == parts[i]);
            if (current == null)
                return null;
        }

        return current;
    }
}
