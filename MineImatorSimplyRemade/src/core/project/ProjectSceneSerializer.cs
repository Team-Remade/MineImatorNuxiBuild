using GlmSharp;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.project;

public static class ProjectSceneSerializer
{
    public static ProjectSceneObjectEntry SerializeObjectForLibrary(SceneObject obj)
    {
        return SerializeNode(obj);
    }

    public static SceneObject? SpawnObjectFromEntry(ProjectSceneObjectEntry entry, Viewport viewport, SpawnMenu spawnMenu, SceneObject? parent = null)
    {
        if (entry == null || viewport == null || spawnMenu == null)
            return null;

        return RestoreNode(entry, viewport, spawnMenu, parent);
    }

    public static void WriteSceneToManifest(ProjectManifest manifest, Viewport viewport, Timeline? timeline = null, PropertiesPanel? propertiesPanel = null)
    {
        propertiesPanel?.WriteProjectSettingsToManifest(manifest);

        SyncObjectLibraryFromScene(manifest, viewport.SceneObjects);

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

        if (timeline != null)
            manifest.AudioTracks = timeline.AudioTracks
                .Select(t => t.ManifestEntry)
                .ToList();
    }

    public static void LoadSceneFromManifest(ProjectManifest manifest, Viewport viewport, SpawnMenu spawnMenu, Timeline? timeline = null, PropertiesPanel? propertiesPanel = null)
    {
        propertiesPanel?.LoadProjectSettingsFromManifest(manifest);

        ApplyWorkCameraState(manifest.WorkCamera, viewport.Camera);

        ClearScene(viewport);

        foreach (var root in manifest.SceneObjects)
            RestoreNode(root, viewport, spawnMenu, parent: null);

        // Restore albedo textures that were saved with a path but not yet loaded onto the GPU
        propertiesPanel?.LoadPendingAlbedoTextures(viewport.SceneObjects);

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

        if (timeline != null && manifest.AudioTracks != null)
            timeline.LoadAudioTracksFromManifest(manifest.AudioTracks);

        // Keep timeline FPS aligned with project settings after timeline state is restored.
        if (propertiesPanel != null)
            timeline?.SetFrameRate(propertiesPanel.GetFramerate());

        // Restore preview viewport selected camera index (after scene objects restored so spawned cameras exist)
        if (viewport.PreviewViewport != null)
            viewport.PreviewViewport.SelectedCameraIndex = manifest.ActivePreviewCameraIndex;

        // Re-apply active-camera flags now that the full scene exists so
        // SetActiveExclusive can demote every other camera in a single pass.
        var activeCam = FindActiveCameraFromEntries(manifest.SceneObjects, viewport.SceneObjects);
        if (activeCam != null)
            CameraSceneObject.SetActiveExclusive(activeCam);
    }

    private static CameraSceneObject? FindActiveCameraFromEntries(
        IEnumerable<ProjectSceneObjectEntry> entries,
        IEnumerable<SceneObject> loadedObjects)
    {
        // Walk entries in parallel to the loaded hierarchy and return the
        // CameraSceneObject that corresponds to an entry with CameraActive=true.
        using var entryEnum = entries.GetEnumerator();
        using var objEnum   = loadedObjects.GetEnumerator();
        while (entryEnum.MoveNext() && objEnum.MoveNext())
        {
            var match = FindActiveCameraRecursive(entryEnum.Current, objEnum.Current);
            if (match != null) return match;
        }
        return null;
    }

    private static CameraSceneObject? FindActiveCameraRecursive(
        ProjectSceneObjectEntry entry, SceneObject obj)
    {
        if (entry.CameraActive && obj is CameraSceneObject cam)
            return cam;

        using var childEntryEnum = entry.Children.GetEnumerator();
        using var childObjEnum   = obj.Children.GetEnumerator();
        while (childEntryEnum.MoveNext() && childObjEnum.MoveNext())
        {
            var match = FindActiveCameraRecursive(childEntryEnum.Current, childObjEnum.Current);
            if (match != null) return match;
        }
        return null;
    }

    private static ProjectSceneObjectEntry SerializeNode(SceneObject obj)
    {
        var pm = ProjectManager.Instance;
        var entry = new ProjectSceneObjectEntry
        {
            LibrarySourceId = obj.LibrarySourceId,
            Name = obj.Name,
            ObjectType = obj.ObjectType,
            SpawnCategory = obj.SpawnCategory,
            BlockVariant = obj.BlockVariant,
            TextureType = obj.TextureType,
            ResourcePackId = obj.ResourcePackId,
            SourceAssetPath = pm.ToProjectRelativePath(obj.SourceAssetPath),
            AlbedoTexturePath = pm.ToProjectRelativePath(obj.AlbedoTexturePath),
            TemporaryItemSheetPath = pm.ToProjectRelativePath(obj.TemporaryItemSheetPath),
            TemporaryItemSheetCacheKey = obj.TemporaryItemSheetCacheKey,
            TemporaryItemSheetColumns = obj.TemporaryItemSheetColumns,
            TemporaryItemSheetRows = obj.TemporaryItemSheetRows,
            TemporaryItemSheetColumnIndex = obj.TemporaryItemSheetColumnIndex,
            TemporaryItemSheetRowIndex = obj.TemporaryItemSheetRowIndex,
            TextureOverridePath = pm.ToProjectRelativePath(obj.TextureOverridePath),
            Position = ToProjectVec3(obj.Position),
            Rotation = ToProjectVec3(obj.Rotation),
            Scale = ToProjectVec3(obj.Scale),
            BendAngle = obj is MiBoneSceneObject bendBone && bendBone.BendParameters is { } bend
                ? ToProjectVec3(bend.Angle)
                : null,
            PivotOffset = ToProjectVec3(obj.PivotOffset),
            InheritPosition = obj.InheritPosition,
            InheritRotation = obj.InheritRotation,
            InheritScale = obj.InheritScale,
            InheritPivotOffset = obj.InheritPivotOffset,
            InheritVisibility = obj.InheritVisibility,
            ObjectVisible = obj.ObjectVisible,
            InvertFaces = obj.InvertFaces,
            BlurTexture = obj.BlurTexture,
            TextureMipmaps = obj.TextureMipmaps,
            IncludeInAmbientOcclusion = obj.IncludeInAmbientOcclusion,
            IncludeInFog = obj.IncludeInFog,
            RenderInHighQuality = obj.RenderInHighQuality,
            RenderInLowQuality = obj.RenderInLowQuality,
            RenderDepthOffset = obj.RenderDepthOffset,
            IsSelectable = obj.IsSelectable,
            HideInSceneTree = obj.HideInSceneTree,
            HasMaterialOverrides = obj.HasExplicitMaterialSettings,
            TileX = obj.GetEffectiveTileX(),
            TileY = obj.GetEffectiveTileY(),
            TileZ = obj.GetEffectiveTileZ(),
            PrimitivePlaneOrientation = obj.SpawnCategory == "Primitives" && obj.ObjectType == "Plane"
                ? obj.Visuals.OfType<PlaneMesh>().FirstOrDefault()?.Orientation ?? PlaneOrientation.XY
                : PlaneOrientation.XY,
            PrimitivePlaneFaceCamera = obj.SpawnCategory == "Primitives" && obj.ObjectType == "Plane"
                ? obj.PrimitivePlaneFaceCamera
                : false,
            PrimitiveCubeMapped = obj.SpawnCategory == "Primitives" && obj.ObjectType == "Cube"
                ? obj.PrimitiveCubeMapped
                : false,
            Keyframes = SerializeKeyframes(obj),
            ShapeKeyWeights = SerializeShapeKeyWeights(obj)
        };

        if (obj is { HasExplicitMaterialSettings: true, MaterialSettings: not null })
        {
            entry.AlbedoColor = ToProjectVec4(obj.MaterialSettings.AlbedoColor);
            entry.BlendColor = ToProjectVec4(obj.MaterialSettings.BlendColor);
            entry.MixColor = ToProjectVec4(obj.MaterialSettings.MixColor);
            entry.Metallic = obj.MaterialSettings.Metallic;
            entry.Roughness = obj.MaterialSettings.Roughness;
            entry.Transparency = obj.MaterialSettings.Transparency;
            entry.DoubleSided = obj.MaterialSettings.DoubleSided;
            entry.TextureOffsetH = obj.MaterialSettings.TextureOffset.x;
            entry.TextureOffsetV = obj.MaterialSettings.TextureOffset.y;
            entry.TextureRepeatH = obj.MaterialSettings.TextureRepeat.x;
            entry.TextureRepeatV = obj.MaterialSettings.TextureRepeat.y;
            entry.TextureMirrorH = obj.MaterialSettings.TextureMirror.x;
            entry.TextureMirrorV = obj.MaterialSettings.TextureMirror.y;
            entry.EmissionEnabled = obj.MaterialSettings.EmissionEnabled;
            entry.EmissionColor = ToProjectVec4(obj.MaterialSettings.EmissionColor);
            entry.EmissionEnergy = obj.MaterialSettings.EmissionEnergy;
            entry.Subsurface = obj.MaterialSettings.Subsurface;
            entry.SubsurfaceRadiusR = obj.MaterialSettings.SubsurfaceRadius.x;
            entry.SubsurfaceRadiusG = obj.MaterialSettings.SubsurfaceRadius.y;
            entry.SubsurfaceRadiusB = obj.MaterialSettings.SubsurfaceRadius.z;
            entry.SubsurfaceColor = ToProjectVec4(obj.MaterialSettings.SubsurfaceColor);
            entry.SubsurfaceHighlight = obj.MaterialSettings.SubsurfaceHighlight;
            entry.SubsurfaceHighlightStrength = obj.MaterialSettings.SubsurfaceHighlightStrength;
            entry.EmissionIndirectOnly = obj.MaterialSettings.EmissionIndirectOnly;
            entry.AutoEmission = obj.MaterialSettings.AutoEmission;
        }

        if (obj.SpawnCategory == "Items")
        {
            entry.ItemTileKey = obj.ItemTileKey;
            if (string.IsNullOrWhiteSpace(entry.ItemTileKey))
                entry.ItemTileKey = ExtractItemTileKey(obj) ?? "";
            entry.ItemIs3D = obj.Visuals.OfType<ExtrudedItemMesh>().FirstOrDefault()?.Is3D ?? true;
        }

        if (obj is ParticleSpawnerSceneObject particleSpawner)
        {
            entry.ParticleLibraryEntryId = particleSpawner.ParticleLibraryEntryId;
            entry.ParticleLibraryDisplayName = particleSpawner.ParticleLibraryDisplayName;
            entry.ParticleEmitting = particleSpawner.Emitting;
            entry.ParticleOneShot = particleSpawner.OneShot;
            entry.ParticleAmount = particleSpawner.Amount;
            entry.ParticleSpawnRate = particleSpawner.SpawnRate;
            entry.ParticleLifetimeMin = particleSpawner.LifetimeMin;
            entry.ParticleLifetimeMax = particleSpawner.LifetimeMax;
            entry.ParticleSimulationSpeed = particleSpawner.SimulationSpeed;
            entry.ParticleLinearDamping = particleSpawner.LinearDamping;
            entry.ParticleAngularDamping = particleSpawner.AngularDamping;
            entry.ParticleEmissionShape = (int)particleSpawner.EmissionShape;
            entry.ParticleUseDirectionalEmission = particleSpawner.UseDirectionalEmission;
            entry.ParticleDirection = ToProjectVec3(particleSpawner.Direction);
            entry.ParticleSpreadDegrees = particleSpawner.SpreadDegrees;
            entry.ParticleInitialSpeedMin = particleSpawner.InitialSpeedMin;
            entry.ParticleInitialSpeedMax = particleSpawner.InitialSpeedMax;
            entry.ParticleSpawnBoxExtents = ToProjectVec3(particleSpawner.SpawnBoxExtents);
            entry.ParticleInitialVelocityMin = ToProjectVec3(particleSpawner.InitialVelocityMin);
            entry.ParticleInitialVelocityMax = ToProjectVec3(particleSpawner.InitialVelocityMax);
            entry.ParticleGravity = ToProjectVec3(particleSpawner.Gravity);
            entry.ParticleInitialRotationMinDegrees = ToProjectVec3(particleSpawner.InitialRotationMinDegrees);
            entry.ParticleInitialRotationMaxDegrees = ToProjectVec3(particleSpawner.InitialRotationMaxDegrees);
            entry.ParticleAngularVelocityMinDegrees = ToProjectVec3(particleSpawner.AngularVelocityMinDegrees);
            entry.ParticleAngularVelocityMaxDegrees = ToProjectVec3(particleSpawner.AngularVelocityMaxDegrees);
            entry.ParticleStartScaleMin = particleSpawner.StartScaleMin;
            entry.ParticleStartScaleMax = particleSpawner.StartScaleMax;
            entry.ParticleEndScaleMin = particleSpawner.EndScaleMin;
            entry.ParticleEndScaleMax = particleSpawner.EndScaleMax;
            entry.ParticleTopLevelParticles = particleSpawner.TopLevelParticles;
        }

        if (obj is CameraSceneObject camera)
        {
            entry.CameraFov = camera.Fov;
            entry.CameraNear = camera.Near;
            entry.CameraFar = camera.Far;
            entry.CameraActive = camera.Active;
            entry.CameraEffects = camera.Effects
                .Select(SerializeCameraEffect)
                .ToList();
        }

        if (obj is LightSceneObject light)
        {
            entry.LightColor = ToProjectVec4(light.LightColor);
            entry.LightEnergy = light.LightEnergy;
            entry.LightRange = light.LightRange;
            entry.LightIndirectEnergy = light.LightIndirectEnergy;
            entry.LightSpecular = light.LightSpecular;
            entry.LightShadowEnabled = light.LightShadowEnabled;
            entry.LightType = (int)light.Type;
            entry.LightSpotAngle = light.LightSpotAngle;
            entry.LightSpotBlend = light.LightSpotBlend;
        }

        foreach (var child in obj.Children.Where(static child => !child.IsRuntimeTransient))
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

        if (parent != null)
        {
            viewport.SceneObjects.Remove(obj);
            parent.AddChild(obj);
        }

        // Apply serialized state after parent linkage so inherited bend can
        // resolve against the correct parent during SetBendAngle regeneration.
        ApplyEntryToObject(obj, entry);

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

            if (entry.TextureType != "block")
                spawnMenu.EnsureTemporaryItemSheetTile(entry, tileKey);

            return spawnMenu.SpawnItemObject(tileKey, atlasSource, entry.ItemIs3D);
        }

        if (entry.SpawnCategory == "Blocks")
        {
            var variants = BlockRegistry.GetVariants(entry.ObjectType);
            var variant = variants.FirstOrDefault(v => v.VariantKey == entry.BlockVariant)
                          ?? variants.FirstOrDefault();
            if (variant == null) return null;
            return spawnMenu.SpawnBlockObject(entry.ObjectType, variant, entry.ResourcePackId,
                                              entry.TileX, entry.TileY, entry.TileZ);
        }

        if (entry.SpawnCategory == "Camera")
            return spawnMenu.SpawnCameraObject(entry.Name);

        if (entry.SpawnCategory == "Light")
            return spawnMenu.SpawnLightObject(entry.Name);

        if (entry.SpawnCategory == "Primitives")
            return spawnMenu.SpawnPrimitiveObject(entry.ObjectType, entry.Name, 0, "", entry.PrimitivePlaneOrientation, entry.PrimitiveCubeMapped);

        if (entry.SpawnCategory == "Particle Spawners")
            return spawnMenu.SpawnParticleSpawnerObject(entry.Name, entry.ParticleLibraryEntryId, entry.ParticleLibraryDisplayName);

        if (entry.SpawnCategory == "Scenery")
        {
            string resolved = ResolveSourcePath(entry);
            if (File.Exists(resolved))
                return spawnMenu.SpawnSchematicFromPath(resolved, entry.ResourcePackId);
            return null;
        }

        string resolvedPath = ResolveSourcePath(entry);
        if (File.Exists(resolvedPath))
        {
            string? textureOverride = string.IsNullOrWhiteSpace(entry.TextureOverridePath)
                ? null
                : ResolveTextureOverridePath(entry.TextureOverridePath);
            return spawnMenu.SpawnCustomModelFromPath(resolvedPath, textureOverride);
        }

        return null;
    }

    /// <summary>
    /// Resolves <see cref="ProjectSceneObjectEntry.SourceAssetPath"/> through the
    /// project's asset list so that assets copied into the project folder can be
    /// found regardless of whether the path is stored as absolute (legacy saves)
    /// or project-relative (current saves).
    /// </summary>
    private static string ResolveSourcePath(ProjectSceneObjectEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.SourceAssetPath))
            return entry.SourceAssetPath;

        var pm = ProjectManager.Instance;

        // New saves store paths relative to ProjectFolder — try combining first.
        if (!Path.IsPathRooted(entry.SourceAssetPath) && pm.HasProject)
        {
            string combined = Path.GetFullPath(Path.Combine(pm.ProjectFolder, entry.SourceAssetPath));
            if (File.Exists(combined))
                return combined;
        }

        // Legacy saves store absolute paths — try direct existence check.
        if (File.Exists(entry.SourceAssetPath))
            return entry.SourceAssetPath;

        if (!pm.HasProject)
            return entry.SourceAssetPath;

        // Still not found — try to match by filename in the project's asset list
        // (handles the case where the project was moved to a different machine).
        string fileName = Path.GetFileName(entry.SourceAssetPath);
        if (string.IsNullOrWhiteSpace(fileName))
            return entry.SourceAssetPath;

        var asset = pm.GetProjectAssets()
            .FirstOrDefault(a => string.Equals(a.DisplayName, fileName, StringComparison.OrdinalIgnoreCase));

        return asset != null ? pm.GetAssetFullPath(asset) : entry.SourceAssetPath;
    }

    /// <summary>
    /// Resolves a texture-override path that may be absolute (legacy) or
    /// project-relative (current saves) back to an absolute path.
    /// </summary>
    private static string ResolveTextureOverridePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (File.Exists(path))
            return path;

        var pm = ProjectManager.Instance;
        if (!pm.HasProject)
            return path;

        if (!Path.IsPathRooted(path))
        {
            string combined = Path.GetFullPath(Path.Combine(pm.ProjectFolder, path));
            if (File.Exists(combined))
                return combined;
        }

        return path;
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
        obj.LibrarySourceId = string.IsNullOrWhiteSpace(entry.LibrarySourceId)
            ? entry.LibraryEntryId
            : entry.LibrarySourceId;
        obj.Name = entry.Name;
        obj.ObjectType = entry.ObjectType;
        obj.SpawnCategory = entry.SpawnCategory;
        obj.BlockVariant = entry.BlockVariant;
        obj.TextureType = entry.TextureType;
        obj.ResourcePackId = entry.ResourcePackId;
        obj.SourceAssetPath = ResolveSourcePath(entry);
        obj.TextureOverridePath = ResolveTextureOverridePath(entry.TextureOverridePath);

        // Restore albedo texture path (actual texture loading happens after scene is loaded).
        // AlbedoTexturePath is intentionally kept project-relative because the loading code
        // does Path.Combine(ProjectFolder, obj.AlbedoTexturePath).
        if (!string.IsNullOrEmpty(entry.AlbedoTexturePath))
        {
            obj.AlbedoTexturePath = entry.AlbedoTexturePath;
        }

        obj.SetLocalPosition(ToVec3(entry.Position));
        obj.SetLocalRotation(ToVec3(entry.Rotation));
        obj.SetLocalScale(ToVec3(entry.Scale));
        if (obj is MiBoneSceneObject bendBone && entry.BendAngle != null)
            bendBone.SetBendAngle(ToVec3(entry.BendAngle));

        obj.PivotOffset = ToVec3(entry.PivotOffset);
        obj.InheritPosition = entry.InheritPosition;
        obj.InheritRotation = entry.InheritRotation;
        obj.InheritScale = entry.InheritScale;
        obj.InheritPivotOffset = entry.InheritPivotOffset;
        obj.InheritVisibility = entry.InheritVisibility;
        obj.ObjectVisible = entry.ObjectVisible;
        obj.InvertFaces = entry.InvertFaces;
        obj.BlurTexture = entry.BlurTexture;
        obj.TextureMipmaps = entry.TextureMipmaps;
        obj.IncludeInAmbientOcclusion = entry.IncludeInAmbientOcclusion;
        obj.IncludeInFog = entry.IncludeInFog;
        obj.RenderInHighQuality = entry.RenderInHighQuality;
        obj.RenderInLowQuality = entry.RenderInLowQuality;
        obj.RenderDepthOffset = entry.RenderDepthOffset;
        obj.IsSelectable = entry.IsSelectable;
        obj.HideInSceneTree = entry.HideInSceneTree;

        obj.TileX = entry.TileX;
        obj.TileY = entry.TileY;
        obj.TileZ = entry.TileZ;
        obj.ItemTileKey = entry.ItemTileKey;
        obj.TemporaryItemSheetPath = entry.TemporaryItemSheetPath;
        obj.TemporaryItemSheetCacheKey = entry.TemporaryItemSheetCacheKey;
        obj.TemporaryItemSheetColumns = entry.TemporaryItemSheetColumns;
        obj.TemporaryItemSheetRows = entry.TemporaryItemSheetRows;
        obj.TemporaryItemSheetColumnIndex = entry.TemporaryItemSheetColumnIndex;
        obj.TemporaryItemSheetRowIndex = entry.TemporaryItemSheetRowIndex;

        if (obj is ParticleSpawnerSceneObject particleSpawner)
        {
            particleSpawner.ParticleLibraryEntryId = entry.ParticleLibraryEntryId ?? "";
            particleSpawner.ParticleLibraryDisplayName = entry.ParticleLibraryDisplayName ?? "";
            particleSpawner.Emitting = entry.ParticleEmitting;
            particleSpawner.OneShot = entry.ParticleOneShot;
            particleSpawner.Amount = entry.ParticleAmount;
            particleSpawner.SpawnRate = entry.ParticleSpawnRate;
            particleSpawner.LifetimeMin = entry.ParticleLifetimeMin;
            particleSpawner.LifetimeMax = entry.ParticleLifetimeMax;
            particleSpawner.SimulationSpeed = entry.ParticleSimulationSpeed;
            particleSpawner.LinearDamping = entry.ParticleLinearDamping;
            particleSpawner.AngularDamping = entry.ParticleAngularDamping;
            particleSpawner.EmissionShape = Enum.IsDefined(typeof(ParticleEmissionShape), entry.ParticleEmissionShape)
                ? (ParticleEmissionShape)entry.ParticleEmissionShape
                : ParticleEmissionShape.Box;
            particleSpawner.UseDirectionalEmission = entry.ParticleUseDirectionalEmission;
            particleSpawner.Direction = ToVec3(entry.ParticleDirection);
            particleSpawner.SpreadDegrees = entry.ParticleSpreadDegrees;
            particleSpawner.InitialSpeedMin = entry.ParticleInitialSpeedMin;
            particleSpawner.InitialSpeedMax = entry.ParticleInitialSpeedMax;
            particleSpawner.SpawnBoxExtents = ToVec3(entry.ParticleSpawnBoxExtents);
            particleSpawner.InitialVelocityMin = ToVec3(entry.ParticleInitialVelocityMin);
            particleSpawner.InitialVelocityMax = ToVec3(entry.ParticleInitialVelocityMax);
            particleSpawner.Gravity = ToVec3(entry.ParticleGravity);
            particleSpawner.InitialRotationMinDegrees = ToVec3(entry.ParticleInitialRotationMinDegrees);
            particleSpawner.InitialRotationMaxDegrees = ToVec3(entry.ParticleInitialRotationMaxDegrees);
            particleSpawner.AngularVelocityMinDegrees = ToVec3(entry.ParticleAngularVelocityMinDegrees);
            particleSpawner.AngularVelocityMaxDegrees = ToVec3(entry.ParticleAngularVelocityMaxDegrees);
            particleSpawner.StartScaleMin = entry.ParticleStartScaleMin;
            particleSpawner.StartScaleMax = entry.ParticleStartScaleMax;
            particleSpawner.EndScaleMin = entry.ParticleEndScaleMin;
            particleSpawner.EndScaleMax = entry.ParticleEndScaleMax;
            particleSpawner.TopLevelParticles = entry.ParticleTopLevelParticles;
            particleSpawner.ResetRuntime();
        }

        if (obj.SpawnCategory == "Primitives" && obj.ObjectType == "Plane")
        {
            var planeMesh = obj.Visuals.OfType<PlaneMesh>().FirstOrDefault();
            if (planeMesh != null)
                planeMesh.SetOrientation(entry.PrimitivePlaneOrientation);
            obj.PrimitivePlaneFaceCamera = entry.PrimitivePlaneFaceCamera;
        }

        if (obj.SpawnCategory == "Primitives" && obj.ObjectType == "Cube")
        {
            obj.PrimitiveCubeMapped = entry.PrimitiveCubeMapped;
            var cubeMesh = obj.Visuals.OfType<CubeMesh>().FirstOrDefault();
            if (cubeMesh != null)
                cubeMesh.SetMapped(entry.PrimitiveCubeMapped);
        }

        if (entry.HasMaterialOverrides)
        {
            var material = obj.MaterialSettings ?? new MaterialSettings();
            material.AlbedoColor = ToVec4(entry.AlbedoColor);
            material.BlendColor = ToVec4(entry.BlendColor);
            material.MixColor = ToVec4(entry.MixColor);
            material.Metallic = entry.Metallic;
            material.Roughness = entry.Roughness;
            material.Transparency = entry.Transparency;
            material.DoubleSided = entry.DoubleSided;
            material.TextureOffset = new vec2(entry.TextureOffsetH, entry.TextureOffsetV);
            material.TextureRepeat = new vec2(Math.Max(0.0001f, entry.TextureRepeatH), Math.Max(0.0001f, entry.TextureRepeatV));
            material.TextureMirror = new bvec2(entry.TextureMirrorH, entry.TextureMirrorV);
            material.EmissionEnabled = entry.EmissionEnabled;
            material.EmissionColor = ToVec4(entry.EmissionColor);
            material.EmissionEnergy = entry.EmissionEnergy;
            material.Subsurface = entry.Subsurface;
            material.SubsurfaceRadius = new vec3(
                Math.Max(0.0001f, entry.SubsurfaceRadiusR),
                Math.Max(0.0001f, entry.SubsurfaceRadiusG),
                Math.Max(0.0001f, entry.SubsurfaceRadiusB));
            material.SubsurfaceColor = ToVec4(entry.SubsurfaceColor);
            material.SubsurfaceHighlight = Math.Clamp(entry.SubsurfaceHighlight, -0.95f, 0.95f);
            material.SubsurfaceHighlightStrength = Math.Max(0f, entry.SubsurfaceHighlightStrength);
            material.EmissionIndirectOnly = entry.EmissionIndirectOnly;
            material.AutoEmission = entry.AutoEmission;
            obj.MaterialSettings = material;
            obj.SetExplicitMaterialSettings();
            obj.PropagateMaterialSettingsToChildren();
        }

        obj.Keyframes = DeserializeKeyframes(entry.Keyframes);
        ApplyShapeKeyWeights(obj, entry.ShapeKeyWeights);

        if (obj is CameraSceneObject camera)
        {
            camera.Fov = entry.CameraFov;
            camera.Near = entry.CameraNear;
            camera.Far = entry.CameraFar;
            camera.Effects.Clear();
            if (entry.CameraEffects != null)
            {
                foreach (var effect in entry.CameraEffects)
                {
                    camera.Effects.Add(DeserializeCameraEffect(effect));
                }
            }
            // CameraActive is applied after the whole scene is loaded so that
            // SetActiveExclusive can also clear other cameras' Active flags.
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
            light.Type = (LightType)entry.LightType;
            light.LightSpotAngle = entry.LightSpotAngle;
            light.LightSpotBlend = entry.LightSpotBlend;
        }
    }

    private static void ClearScene(Viewport viewport)
    {
        foreach (var mesh in viewport.SceneObjects.ToList().SelectMany(obj => obj.GetMeshInstancesRecursively()))
        {
            mesh.Dispose();
        }

        viewport.SceneObjects.Clear();
    }

    private static string? ExtractItemTileKey(SceneObject obj)
    {
        return !string.IsNullOrWhiteSpace(obj.ObjectType) ? ExtractItemTileKey(obj.ObjectType) : null;
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

    private static ProjectCameraEffectEntry SerializeCameraEffect(CameraEffect effect)
    {
        return new ProjectCameraEffectEntry
        {
            Type = effect.Type,
            Shake = new ProjectCameraShakeSettings
            {
                Mode = effect.Shake.Mode,
                Trauma = effect.Shake.Trauma,
                Strength = ToProjectVec3(effect.Shake.Strength),
                Speed = ToProjectVec3(effect.Shake.Speed),
                Offset = ToProjectVec3(effect.Shake.Offset)
            }
        };
    }

    private static CameraEffect DeserializeCameraEffect(ProjectCameraEffectEntry effect)
    {
        return new CameraEffect
        {
            Type = effect.Type,
            Shake = new CameraShakeSettings
            {
                Mode = effect.Shake?.Mode ?? CameraShakeMode.Both,
                Trauma = effect.Shake?.Trauma ?? 1f,
                Strength = effect.Shake != null ? ToVec3(effect.Shake.Strength) : new vec3(0.03f, 0.03f, 0.03f),
                Speed = effect.Shake != null ? ToVec3(effect.Shake.Speed) : new vec3(3f, 3.5f, 2.5f),
                Offset = effect.Shake != null ? ToVec3(effect.Shake.Offset) : vec3.Zero
            }
        };
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

    /// <summary>
    /// Captures the current (non-keyframed) weight of every non-default shape
    /// key on <paramref name="obj"/>'s meshes. Shape key deltas/geometry are
    /// never serialized (meshes are re-imported from <see cref="SceneObject.SourceAssetPath"/>
    /// on load) so only the lightweight name→weight state is saved here.
    /// </summary>
    private static List<ProjectShapeKeyWeightEntry> SerializeShapeKeyWeights(SceneObject obj)
    {
        var result = new List<ProjectShapeKeyWeightEntry>();
        var meshes = obj.GetMeshInstancesRecursively();

        for (int m = 0; m < meshes.Count; m++)
        {
            if (!meshes[m].HasShapeKeys) continue;

            result.AddRange(from sk in meshes[m].ShapeKeys where sk.Weight != 0f select new ProjectShapeKeyWeightEntry { MeshIndex = m, Name = sk.Name, Weight = sk.Weight });
        }

        return result;
    }

    /// <summary>
    /// Restores saved shape key weights onto <paramref name="obj"/>'s freshly
    /// (re-)imported meshes. Matches primarily by shape key <c>Name</c> within
    /// the mesh at the saved <c>MeshIndex</c> (robust to morph-target reordering
    /// within a mesh); if that mesh no longer has a matching name (e.g. the
    /// source model's mesh order changed too), falls back to searching every
    /// mesh on the object for the first shape key with that name.
    /// </summary>
    private static void ApplyShapeKeyWeights(SceneObject obj, List<ProjectShapeKeyWeightEntry>? entries)
    {
        if (entries == null || entries.Count == 0) return;

        var meshes = obj.GetMeshInstancesRecursively();
        if (meshes.Count == 0) return;

        foreach (var saved in entries)
        {
            if (string.IsNullOrEmpty(saved.Name)) continue;

            Mesh? targetMesh = null;
            int keyIndex = -1;

            if (saved.MeshIndex >= 0 && saved.MeshIndex < meshes.Count)
            {
                int idx = meshes[saved.MeshIndex].ShapeKeys.FindIndex(sk => sk.Name == saved.Name);
                if (idx >= 0) { targetMesh = meshes[saved.MeshIndex]; keyIndex = idx; }
            }

            if (targetMesh == null)
            {
                foreach (var mesh in meshes)
                {
                    int idx = mesh.ShapeKeys.FindIndex(sk => sk.Name == saved.Name);
                    if (idx >= 0) { targetMesh = mesh; keyIndex = idx; break; }
                }
            }

            targetMesh?.SetShapeKeyWeight(keyIndex, saved.Weight);
        }
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

    private static void SyncObjectLibraryFromScene(ProjectManifest manifest, IReadOnlyList<SceneObject> sceneRoots)
    {
        manifest.ObjectLibrary ??= new List<ProjectSceneObjectEntry>();

        foreach (var root in sceneRoots)
        {
            EnsureLibrarySourceIdsRecursive(root);

            string sourceId = root.LibrarySourceId;
            if (ContainsLibraryEntryId(manifest.ObjectLibrary, sourceId))
                continue;

            var libraryEntry = SerializeNode(root);
            libraryEntry.LibraryEntryId = sourceId;
            libraryEntry.LibrarySourceId = sourceId;
            if (string.IsNullOrWhiteSpace(libraryEntry.Name))
                libraryEntry.Name = string.IsNullOrWhiteSpace(libraryEntry.ObjectType) ? "Object" : libraryEntry.ObjectType;

            manifest.ObjectLibrary.Add(libraryEntry);
        }
    }

    private static bool ContainsLibraryEntryId(IEnumerable<ProjectSceneObjectEntry> nodes, string libraryEntryId)
    {
        if (string.IsNullOrWhiteSpace(libraryEntryId))
            return false;

        foreach (var node in nodes)
        {
            if (string.Equals(node.LibraryEntryId, libraryEntryId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (ContainsLibraryEntryId(node.Children, libraryEntryId))
                return true;
        }

        return false;
    }

    private static void EnsureLibrarySourceIdsRecursive(SceneObject obj)
    {
        if (string.IsNullOrWhiteSpace(obj.LibrarySourceId))
            obj.LibrarySourceId = string.IsNullOrWhiteSpace(obj.ObjectId) ? Guid.NewGuid().ToString("N") : obj.ObjectId;

        foreach (var child in obj.Children)
            EnsureLibrarySourceIdsRecursive(child);
    }
}
