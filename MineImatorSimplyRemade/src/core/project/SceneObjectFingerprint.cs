using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.project;

/// <summary>
/// Change detector for SerializeNode's authored scene state; idle scans allocate
/// nothing after immutable string hashes have warmed up.
/// Paths are hashed verbatim; keyframes retain their current dictionary/list order.
/// This is not a persistent identifier or a replacement for the undo snapshot.
/// </summary>
internal static class SceneObjectFingerprint
{
    private static readonly ConditionalWeakTable<string, StringContentHash> StringHashes = new();

    private sealed class StringContentHash
    {
        public readonly ulong Value;

        public StringContentHash(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
            {
                hash = unchecked((hash ^ (byte)c) * 1099511628211UL);
                hash = unchecked((hash ^ (byte)(c >> 8)) * 1099511628211UL);
            }
            Value = hash;
        }
    }

    public static ulong Compute(IReadOnlyList<SceneObject> roots)
    {
        var hash = new Fnv1a();
        hash.Add(roots.Count);
        // Transient transforms are excluded, but legacy shape weights use all subtree meshes.
        for (int i = 0; i < roots.Count; i++)
        {
            bool hasShapeWeights = HasShapeWeights(roots[i]);
            hash.Add(hasShapeWeights);
            AddNode(ref hash, roots[i], hasShapeWeights);
        }
        return hash.Value;
    }

    private static void AddNode(ref Fnv1a hash, SceneObject obj, bool hasShapeWeights)
    {
        hash.Add(0x4e4f4445); // Node boundary, independent of names and hierarchy depth.
        hash.Add(obj.ObjectId);
        hash.Add(obj.LibrarySourceId);
        hash.Add(obj.Name);
        hash.Add(obj.ObjectType);
        hash.Add(obj.SpawnCategory);
        hash.Add(obj.BlockVariant);
        hash.Add(obj.TextureType);
        hash.Add(obj.ResourcePackId);
        hash.Add(obj.NormalResourcePackId);
        hash.Add(obj.EmissionResourcePackId);
        hash.Add(obj.SourceAssetPath);
        hash.Add(obj.AlbedoTexturePath);
        hash.Add(obj.NormalTexturePath);
        hash.Add(obj.EmissionTexturePath);
        hash.Add(obj.SurfaceTexturePath);
        hash.Add(obj.OcclusionTexturePath);
        hash.Add(obj.RoughnessTexturePath);
        hash.Add(obj.MetallicTexturePath);
        hash.Add(obj.HeightTexturePath);
        hash.Add(obj.CameraTextureObjectId);
        hash.Add(obj.TemporaryItemSheetPath);
        hash.Add(obj.TemporaryItemSheetCacheKey);
        hash.Add(obj.TemporaryItemSheetColumns);
        hash.Add(obj.TemporaryItemSheetRows);
        hash.Add(obj.TemporaryItemSheetColumnIndex);
        hash.Add(obj.TemporaryItemSheetRowIndex);
        hash.Add(obj.TextureOverridePath);
        hash.Add(obj.Position);
        hash.Add(obj.Rotation);
        hash.Add(obj.Scale);
        bool hasBend = obj is MiBoneSceneObject { BendParameters: not null };
        hash.Add(hasBend);
        if (obj is MiBoneSceneObject { BendParameters: { } bend })
        {
            hash.Add(bend.Angle);
            hash.Add(bend.InheritBend);
        }
        hash.Add(obj.PivotOffset);
        hash.Add(obj.InheritPosition);
        hash.Add(obj.InheritRotation);
        hash.Add(obj.InheritScale);
        hash.Add(obj.UsePropertyInheritance);
        hash.Add(obj.InheritAlpha);
        hash.Add(obj.InheritColor);
        hash.Add(obj.InheritTexture);
        hash.Add(obj.InheritSurface);
        hash.Add(obj.InheritSubsurface);
        hash.Add(obj.InheritGlowColor);
        hash.Add(obj.InheritSelect);
        hash.Add(obj.InheritPose);
        hash.Add(obj.ScaleResize);
        hash.Add(obj.LockBend);
        hash.Add(obj.InheritPivotOffset);
        hash.Add(obj.PivotOffsetInheritsModelOrigin);
        hash.Add(obj.InheritVisibility);
        hash.Add(obj.ObjectVisible);
        hash.Add(obj.IsHidden);
        hash.Add(obj.CastShadow);
        hash.Add(obj.InvertFaces);
        hash.Add(obj.BlurTexture);
        hash.Add(obj.TextureMipmaps);
        hash.Add(obj.IncludeInAmbientOcclusion);
        hash.Add(obj.IncludeInFog);
        hash.Add(obj.RenderInHighQuality);
        hash.Add(obj.RenderInLowQuality);
        hash.Add(obj.RenderDepthOffset);
        hash.Add(obj.IsSelectable);
        hash.Add(obj.IsLocked);
        hash.Add(obj.HideInSceneTree);
        hash.Add(obj.HasExplicitMaterialSettings);
        hash.Add(obj.GetEffectiveTileX());
        hash.Add(obj.GetEffectiveTileY());
        hash.Add(obj.GetEffectiveTileZ());

        if (obj.SpawnCategory == "Primitives" && obj.ObjectType == "Plane")
        {
            var orientation = PlaneOrientation.XY;
            for (int i = 0; i < obj.Visuals.Count; i++)
            {
                if (obj.Visuals[i] is not PlaneMesh plane) continue;
                orientation = plane.Orientation;
                break;
            }
            hash.Add(orientation);
            hash.Add(obj.PrimitivePlaneFaceCamera);
        }
        if (obj.SpawnCategory == "Primitives" && obj.ObjectType == "Cube")
            hash.Add(obj.PrimitiveCubeMapped);
        hash.Add(obj.PrimitiveSphereSmooth);
        hash.Add(obj.PrimitiveSphereSegments);
        hash.Add(obj.PrimitiveSphereRings);
        hash.Add(obj.PrimitiveRadialSegments);
        hash.Add(obj.PrimitiveRadialSmooth);
        hash.Add(obj.PrimitiveRadialClosed);
        hash.Add(obj.PrimitiveRadialMapped);
        hash.Add(obj.TextMeshFontPath);
        hash.Add(obj.TextMeshBaseString);
        hash.Add(obj.TextMeshStringOverride);
        hash.Add(obj.TextMeshExtruded);
        hash.Add(obj.TextMeshExtrusionDepth);
        hash.Add(obj.TextMeshFaceCamera);
        hash.Add(obj.TextMeshHorizontalAlignment);
        hash.Add(obj.TextMeshVerticalAlignment);
        hash.Add(obj.TextMeshAntialiasing);
        hash.Add(obj.TextMeshFontSize);
        hash.Add(obj.TextMeshOutlineEnabled);
        hash.Add(obj.TextMeshOutlineColor);
        hash.Add(obj.TextMeshOutlineThickness);

        if (obj is { HasExplicitMaterialSettings: true, MaterialSettings: { } material })
        {
            hash.Add(true);
            hash.Add(material.AlbedoColor);
            hash.Add(material.BlendColor);
            hash.Add(material.MixColor);
            hash.Add(material.Metallic);
            hash.Add(material.Roughness);
            hash.Add(material.PbrEnabled);
            hash.Add(material.NormalStrength);
            hash.Add(material.OcclusionStrength);
            hash.Add(material.HeightScale);
            hash.Add(material.NormalEnabled);
            hash.Add(material.Transparency);
            hash.Add(material.DoubleSided);
            hash.Add(material.TextureOffset);
            hash.Add(material.TextureRepeat);
            hash.Add(material.TextureMirror.x);
            hash.Add(material.TextureMirror.y);
            hash.Add(material.EmissionEnabled);
            hash.Add(material.EmissionColor);
            hash.Add(material.EmissionEnergy);
            hash.Add(material.Subsurface);
            hash.Add(material.SubsurfaceRadius);
            hash.Add(material.SubsurfaceColor);
            hash.Add(material.SubsurfaceHighlight);
            hash.Add(material.SubsurfaceHighlightStrength);
            hash.Add(material.EmissionIndirectOnly);
            hash.Add(material.AutoEmission);
        }
        else
            hash.Add(false);

        if (obj.SpawnCategory == "Items")
        {
            if (!string.IsNullOrWhiteSpace(obj.ItemTileKey))
                hash.Add(obj.ItemTileKey);
            else
            {
                var type = obj.ObjectType;
                int open = type?.IndexOf('[') ?? -1;
                int close = type?.LastIndexOf(']') ?? -1;
                hash.Add(open >= 0 && close > open
                    ? type.AsSpan(open + 1, close - open - 1)
                    : ReadOnlySpan<char>.Empty);
            }
            bool is3D = true;
            for (int i = 0; i < obj.Visuals.Count; i++)
            {
                if (obj.Visuals[i] is not ExtrudedItemMesh item) continue;
                is3D = item.Is3D;
                break;
            }
            hash.Add(is3D);
        }

        hash.Add(0x4b455953);
        int keyCount = 0;
        foreach (var pair in obj.Keyframes)
        {
            if (pair.Value == null || pair.Value.Count == 0) continue;
            hash.Add(true);
            hash.Add(pair.Key);
            hash.Add(pair.Value.Count);
            for (int i = 0; i < pair.Value.Count; i++)
            {
                var key = pair.Value[i];
                hash.Add(key.Frame);
                hash.AddValue(key.Value);
                hash.Add(key.InterpolationType);
            }
            keyCount++;
        }
        hash.Add(false);
        hash.Add(keyCount);

        if (hasShapeWeights)
            AddOwnShapeWeights(ref hash, obj);

        hash.Add(obj is ParticleSpawnerSceneObject);
        if (obj is ParticleSpawnerSceneObject particle)
        {
            hash.Add(particle.ParticleLibraryEntryId);
            hash.Add(particle.ParticleLibraryDisplayName);
            hash.Add(particle.Emitting);
            hash.Add(particle.OneShot);
            hash.Add(particle.Amount);
            hash.Add(particle.SpawnRate);
            hash.Add(particle.LifetimeMin);
            hash.Add(particle.LifetimeMax);
            hash.Add(particle.SimulationSpeed);
            hash.Add(particle.LinearDamping);
            hash.Add(particle.AngularDamping);
            hash.Add(particle.EmissionShape);
            hash.Add(particle.UseDirectionalEmission);
            hash.Add(particle.Direction);
            hash.Add(particle.SpreadDegrees);
            hash.Add(particle.InitialSpeedMin);
            hash.Add(particle.InitialSpeedMax);
            hash.Add(particle.SpawnBoxExtents);
            hash.Add(particle.InitialVelocityMin);
            hash.Add(particle.InitialVelocityMax);
            hash.Add(particle.Gravity);
            hash.Add(particle.InitialRotationMinDegrees);
            hash.Add(particle.InitialRotationMaxDegrees);
            hash.Add(particle.AngularVelocityMinDegrees);
            hash.Add(particle.AngularVelocityMaxDegrees);
            hash.Add(particle.StartScaleMin);
            hash.Add(particle.StartScaleMax);
            hash.Add(particle.EndScaleMin);
            hash.Add(particle.EndScaleMax);
            hash.Add(particle.TopLevelParticles);
        }

        hash.Add(obj is CameraSceneObject);
        if (obj is CameraSceneObject camera)
        {
            hash.Add(camera.Fov);
            hash.Add(camera.Near);
            hash.Add(camera.Far);
            hash.Add(camera.Active);
            hash.Add(camera.Effects.Count);
            for (int i = 0; i < camera.Effects.Count; i++)
            {
                var effect = camera.Effects[i];
                hash.Add(effect.Type);
                // SerializeCameraEffect persists all nested settings, not just the active type.
                var depth = effect.DepthOfField;
                hash.Add(depth.Depth);
                hash.Add(depth.FocusRange);
                hash.Add(depth.FadeSize);
                hash.Add(depth.BlurSize);
                hash.Add(depth.AnamorphicRatio);
                hash.Add(depth.EdgeHighlight);
                hash.Add(depth.HighlightThreshold);
                hash.Add(depth.HighlightStrength);
                hash.Add(depth.ColorFringe);
                hash.Add(depth.RedAngle);
                hash.Add(depth.GreenAngle);
                hash.Add(depth.BlueAngle);
                hash.Add(depth.RedOffset);
                hash.Add(depth.GreenOffset);
                hash.Add(depth.BlueOffset);
                hash.Add(effect.Shake.Mode);
                hash.Add(effect.Shake.Trauma);
                hash.Add(effect.Shake.Strength);
                hash.Add(effect.Shake.Speed);
                hash.Add(effect.Shake.Offset);
                hash.Add(effect.FilmGrain.Strength);
                hash.Add(effect.FilmGrain.Saturation);
                hash.Add(effect.FilmGrain.Size);
            }
        }

        hash.Add(obj is LightSceneObject);
        if (obj is LightSceneObject light)
        {
            hash.Add(light.LightColor);
            hash.Add(light.LightEnergy);
            hash.Add(light.LightRange);
            hash.Add(light.LightIndirectEnergy);
            hash.Add(light.LightSpecular);
            hash.Add(light.LightShadowEnabled);
            hash.Add(light.Type);
            hash.Add(light.LightSpotAngle);
            hash.Add(light.LightSpotBlend);
        }

        hash.Add(0x4348494c);
        int childCount = 0;
        var children = obj.Children;
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.IsRuntimeTransient)
            {
                if (hasShapeWeights)
                    AddTransientShapeWeights(ref hash, child);
                continue;
            }
            AddNode(ref hash, child, hasShapeWeights);
            childCount++;
        }
        hash.Add(0x454e444e);
        hash.Add(childCount);
    }

    private static bool HasShapeWeights(SceneObject obj)
    {
        foreach (var mesh in obj.Visuals)
            foreach (var key in mesh.ShapeKeys)
                if (key.Weight != 0f) return true;
        var children = obj.Children;
        for (int i = 0; i < children.Count; i++)
            if (HasShapeWeights(children[i])) return true;
        return false;
    }

    private static void AddOwnShapeWeights(ref Fnv1a hash, SceneObject obj)
    {
        hash.Add(0x53484150);
        hash.Add(obj.Visuals.Count);
        for (int m = 0; m < obj.Visuals.Count; m++)
            foreach (var key in obj.Visuals[m].ShapeKeys)
            {
                if (key.Weight == 0f) continue;
                hash.Add(true);
                hash.Add(m);
                hash.Add(key.Name);
                hash.Add(key.Weight);
            }
        hash.Add(false);
    }

    private static void AddTransientShapeWeights(ref Fnv1a hash, SceneObject obj)
    {
        hash.Add(0x5452414e);
        AddOwnShapeWeights(ref hash, obj);
        var children = obj.Children;
        for (int i = 0; i < children.Count; i++)
            AddTransientShapeWeights(ref hash, children[i]);
        hash.Add(0x454e4454);
    }

    private struct Fnv1a
    {
        public ulong Value { get; private set; }

        public Fnv1a() => Value = 14695981039346656037UL;

        private void AddByte(byte value) => Value = unchecked((Value ^ value) * 1099511628211UL);

        public void Add(bool value) => AddByte(value ? (byte)1 : (byte)0);

        public void Add(int value)
        {
            uint bits = unchecked((uint)value);
            ulong hash = Value;
            hash = unchecked((hash ^ (byte)bits) * 1099511628211UL);
            hash = unchecked((hash ^ (byte)(bits >> 8)) * 1099511628211UL);
            hash = unchecked((hash ^ (byte)(bits >> 16)) * 1099511628211UL);
            Value = unchecked((hash ^ (byte)(bits >> 24)) * 1099511628211UL);
        }

        public void Add(float value) => Add(BitConverter.SingleToInt32Bits(value));
        public void Add(vec2 value) { Add(value.x); Add(value.y); }
        public void Add(vec3 value) { Add(value.x); Add(value.y); Add(value.z); }
        public void Add(vec4 value) { Add(value.x); Add(value.y); Add(value.z); Add(value.w); }
        public void Add(PlaneOrientation value) => Add((int)value);
        public void Add(ParticleEmissionShape value) => Add((int)value);
        public void Add(CameraEffectType value) => Add((int)value);
        public void Add(CameraShakeMode value) => Add((int)value);
        public void Add(LightType value) => Add((int)value);

        public void Add(string? value)
        {
            if (value == null) { Add(-1); return; }
            if (value.Length >= 32)
            {
                // Strings are immutable. Weak ownership avoids retaining old scenes/text
                // while a content hash (not identity) preserves equal-string semantics.
                Add(value.Length);
                Add(unchecked((long)StringHashes.GetValue(value, static text => new StringContentHash(text)).Value));
                return;
            }
            Add(value.AsSpan());
        }

        public void Add(ReadOnlySpan<char> value)
        {
            Add(value.Length);
            ulong hash = Value;
            foreach (char c in value)
            {
                hash = unchecked((hash ^ (byte)c) * 1099511628211UL);
                hash = unchecked((hash ^ (byte)(c >> 8)) * 1099511628211UL);
            }
            Value = hash;
        }

        private void Add(long value)
        {
            Add(unchecked((int)value));
            Add(unchecked((int)(value >> 32)));
        }

        // ObjectKeyframe.Value is scalar today; vectors are also supported without boxing.
        // Fail loudly for unsupported types rather than silently missing authored edits.
        public void AddValue(object? value)
        {
            switch (value)
            {
                case null: Add(0); break;
                case bool b: Add(1); Add(b); break;
                case string s: Add(2); Add(s); break;
                case float f: Add(3); Add(f); break;
                case double d: Add(4); Add(BitConverter.DoubleToInt64Bits(d)); break;
                case int n: Add(5); Add(n); break;
                case long n: Add(6); Add(n); break;
                case uint n: Add(7); Add(unchecked((int)n)); break;
                case ulong n: Add(8); Add(unchecked((long)n)); break;
                case short n: Add(9); Add((int)n); break;
                case ushort n: Add(10); Add((int)n); break;
                case byte n: Add(11); Add((int)n); break;
                case sbyte n: Add(12); Add((int)n); break;
                case decimal n:
                    Add(13);
                    Span<int> bits = stackalloc int[4];
                    decimal.GetBits(n, bits);
                    for (int i = 0; i < bits.Length; i++) Add(bits[i]);
                    break;
                case vec2 v: Add(14); Add(v); break;
                case vec3 v: Add(15); Add(v); break;
                case vec4 v: Add(16); Add(v); break;
                case JsonElement json: Add(17); AddJson(json); break;
                default: throw new NotSupportedException("Unsupported scene keyframe value type.");
            }
        }

        private void AddJson(JsonElement value)
        {
            Add((int)value.ValueKind);
            if (value.ValueKind == JsonValueKind.Undefined) return;
            // Preserve exact numbers and structured values without GetRawText/GetString
            // allocations. Equivalent JSON with different formatting can trigger a change.
            var bytes = JsonMarshal.GetRawUtf8Value(value);
            Add(bytes.Length);
            ulong hash = Value;
            foreach (byte b in bytes) hash = unchecked((hash ^ b) * 1099511628211UL);
            Value = hash;
        }
    }
}
