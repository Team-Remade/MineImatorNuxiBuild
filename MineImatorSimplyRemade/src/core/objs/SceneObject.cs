using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.diagnostics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.material.materials;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemadeNuxi.core.objs;

// ── MaterialSettings ──────────────────────────────────────────────────────────

public enum MaterialAlphaMode
{
    Blend,
    Clip
}

/// <summary>
/// Per-object material overrides that can be inherited down the scene hierarchy.
/// Mirrors the Godot MaterialSettings class from ExampleSceneObject.
/// </summary>
public class MaterialSettings
{
    public vec4 AlbedoColor = new vec4(1f, 1f, 1f, 1f);
    public vec4 BlendColor = new vec4(1f, 1f, 1f, 1f);
    public vec4 MixColor = new vec4(0f, 0f, 0f, 0f);
    public float Metallic = 0f;
    public float Roughness = 0.5f;
    public bool PbrEnabled = false;
    public float NormalStrength = 1f;
    public float OcclusionStrength = 1f;
    public float HeightScale = 0f;
    public bool NormalEnabled = false;

    /// <summary>
    /// OpenGL texture handle for the normal map (0 = no normal map).
    /// Propagated down the scene hierarchy when <see cref="SceneObject.MaterialSettings"/>
    /// is updated, and applied to all <see cref="StandardMaterial"/> surfaces.
    /// </summary>
    public uint NormalTexture = 0;

    /// <summary>Alpha-transparency amount (0 = fully opaque, 1 = fully transparent).</summary>
    public float Transparency = 0f;
    public MaterialAlphaMode AlphaMode = MaterialAlphaMode.Blend;
    public float AlphaClipThreshold = 0.5f;
    public bool EmissionEnabled = false;
    public vec4 EmissionColor = new vec4(0f, 0f, 0f, 1f);
    public float EmissionEnergy = 1f;
    public float Subsurface = 0f;
    public vec3 SubsurfaceRadius = vec3.Ones;
    public vec4 SubsurfaceColor = new vec4(1f, 1f, 1f, 1f);
    public float SubsurfaceHighlight = 0.35f;
    public float SubsurfaceHighlightStrength = 0.6f;
    public bool EmissionIndirectOnly = false;
    public bool AutoEmission = true;

    /// <summary>
    /// When true, both faces of meshes are rendered (back-face culling disabled).
    /// </summary>
    public bool DoubleSided = false;

    /// <summary>
    /// Per-axis UV offset applied after repeat/mirroring.
    /// </summary>
    public vec2 TextureOffset = vec2.Zero;

    /// <summary>
    /// Per-axis UV repeat multiplier.
    /// </summary>
    public vec2 TextureRepeat = new vec2(1f, 1f);

    /// <summary>
    /// Per-axis UV mirroring toggle.
    /// </summary>
    public bvec2 TextureMirror = new bvec2(false, false);

    /// <summary>
    /// Creates a copy of these material settings.
    /// </summary>
    public MaterialSettings Clone()
    {
        return new MaterialSettings
        {
            AlbedoColor = this.AlbedoColor,
            BlendColor = this.BlendColor,
            MixColor = this.MixColor,
            Metallic = this.Metallic,
            Roughness = this.Roughness,
            PbrEnabled = this.PbrEnabled,
            NormalStrength = this.NormalStrength,
            OcclusionStrength = this.OcclusionStrength,
            HeightScale = this.HeightScale,
            NormalEnabled = this.NormalEnabled,
            NormalTexture = this.NormalTexture,
            Transparency = this.Transparency,
            AlphaMode = this.AlphaMode,
            AlphaClipThreshold = this.AlphaClipThreshold,
            EmissionEnabled = this.EmissionEnabled,
            EmissionColor = this.EmissionColor,
            EmissionEnergy = this.EmissionEnergy,
            Subsurface = this.Subsurface,
            SubsurfaceRadius = this.SubsurfaceRadius,
            SubsurfaceColor = this.SubsurfaceColor,
            SubsurfaceHighlight = this.SubsurfaceHighlight,
            SubsurfaceHighlightStrength = this.SubsurfaceHighlightStrength,
            EmissionIndirectOnly = this.EmissionIndirectOnly,
            AutoEmission = this.AutoEmission,
            DoubleSided = this.DoubleSided,
            TextureOffset = this.TextureOffset,
            TextureRepeat = this.TextureRepeat,
            TextureMirror = this.TextureMirror
        };
    }
}

// ── SceneObject ───────────────────────────────────────────────────────────────

public class SceneObject
{
    [ThreadStatic] private static long _renderEvaluation;
    private static long _nextRenderEvaluation;
    private long _worldEvaluation;
    private long _visibilityEvaluation;
    private mat4 _evaluatedWorldMatrix;
    private mat4 _evaluatedWorldMatrixNoPivot;
    private bool _evaluatedVisibility;

    // Public transform fields can change without notifications, so reuse is bounded
    // to explicit renderer read phases, never an entire editor frame or idle period.
    internal static EvaluationScope BeginRenderEvaluation()
    {
        long previous = _renderEvaluation;
        _renderEvaluation = Interlocked.Increment(ref _nextRenderEvaluation);
        return new EvaluationScope(previous);
    }

    internal static EvaluationScope SuspendRenderEvaluation()
    {
        long previous = _renderEvaluation;
        _renderEvaluation = 0;
        return new EvaluationScope(previous);
    }

    internal static void InvalidateRenderEvaluation()
    {
        if (_renderEvaluation != 0)
            _renderEvaluation = Interlocked.Increment(ref _nextRenderEvaluation);
    }

    internal readonly struct EvaluationScope : IDisposable
    {
        private readonly long _previous;
        private readonly int _thread;

        internal EvaluationScope(long previous)
        {
            _previous = previous;
            _thread = Environment.CurrentManagedThreadId;
        }

        public void Dispose()
        {
            if (_thread != Environment.CurrentManagedThreadId) return;
            // A nested phase may have edited the scene or replaced cached values.
            _renderEvaluation = _previous == 0 ? 0 : Interlocked.Increment(ref _nextRenderEvaluation);
        }
    }

    // ── Basic identity ────────────────────────────────────────────────────────

    public string ObjectType = "Object";
    public string Name;
    public string ObjectId;
    public string LibrarySourceId = "";

    public string SpawnCategory = "";
    public string BlockVariant = "";
    public string TextureType = "item";
    public string ItemTileKey = "";
    // Canonical semicolon-separated pack IDs, highest priority first; empty uses defaults.
    // A legacy single pack ID is already a valid stack.
    public string ResourcePackId = "";
    public string TemporaryItemSheetPath = "";
    public string TemporaryItemSheetCacheKey = "";
    public int TemporaryItemSheetColumns = 0;
    public int TemporaryItemSheetRows = 0;
    public int TemporaryItemSheetColumnIndex = 0;
    public int TemporaryItemSheetRowIndex = 0;

    /// <summary>
    /// Hard cap on the tile count along any single axis.  1000 per axis keeps
    /// the mesh update budget bounded; total vertices scale as
    /// <c>TileX * TileY * TileZ * (block face vertex count)</c>.
    /// </summary>
    public const int MaxTilesPerAxis = 1000;

    /// <summary>
    /// Per-axis block repetition.  Values &lt;= 0 are treated as 1 (no tiling).
    /// Values &gt; <see cref="MaxTilesPerAxis"/> are clamped to that limit
    /// during <see cref="GetEffectiveTileX/Y/Z"/>.  Tiling only applies to
    /// objects in the <c>Blocks</c> spawn category.
    /// </summary>
    public int TileX = 1;
    public int TileY = 1;
    public int TileZ = 1;

    /// <summary>
    /// Returns <see cref="TileX"/> clamped to <c>[1, MaxTilesPerAxis]</c>.
    /// </summary>
    public int GetEffectiveTileX() => ClampTile(TileX);

    /// <summary>
    /// Returns <see cref="TileY"/> clamped to <c>[1, MaxTilesPerAxis]</c>.
    /// </summary>
    public int GetEffectiveTileY() => ClampTile(TileY);

    /// <summary>
    /// Returns <see cref="TileZ"/> clamped to <c>[1, MaxTilesPerAxis]</c>.
    /// </summary>
    public int GetEffectiveTileZ() => ClampTile(TileZ);

    /// <summary>
    /// True when the object is in the <c>Blocks</c> category and any tile
    /// axis is &gt; 1.
    /// </summary>
    public bool HasTiling =>
        string.Equals(SpawnCategory, "Blocks", StringComparison.Ordinal) &&
        (GetEffectiveTileX() > 1 || GetEffectiveTileY() > 1 || GetEffectiveTileZ() > 1);

    private static int ClampTile(int value)
    {
        if (value < 1) return 1;
        if (value > MaxTilesPerAxis) return MaxTilesPerAxis;
        return value;
    }

    /// <summary>
    /// Absolute path to the source asset file used to create this object.
    /// Empty for built-in objects (primitives, lights, etc.).
    /// </summary>
    public string SourceAssetPath = "";

    /// <summary>
    /// Path to the albedo texture file for this object (used for primitives and custom objects).
    /// When set, the texture is loaded and applied to all meshes on scene load.
    /// Empty means no custom albedo texture.
    /// </summary>
    public string AlbedoTexturePath = "";
    public string NormalTexturePath = "";
    public string EmissionTexturePath = "";
    /// <summary>Packed linear AO, roughness, metallic and height texture.</summary>
    public string SurfaceTexturePath = "";
    // Source metadata: null = packed source unknown, empty = explicitly absent.
    public string? OcclusionTexturePath;
    public string? RoughnessTexturePath;
    public string? MetallicTexturePath;
    public string? HeightTexturePath;
    public string NormalResourcePackId = "";
    public string EmissionResourcePackId = "";

    /// <summary>Object id of the scene camera used as this object's live albedo texture.</summary>
    public string CameraTextureObjectId = "";

    /// <summary>Runtime camera feed shared with inheriting meshes, including meshless model roots.</summary>
    public uint CameraTextureId { get; set; }

    /// <summary>
    /// Primitive-cube UV mode. False = each face fills the whole texture;
    /// true = use a 3x2 cubemap unwrap layout.
    /// </summary>
    public bool PrimitiveCubeMapped = false;

    public bool PrimitiveSphereSmooth = true;
    public int PrimitiveSphereSegments = 32;
    public int PrimitiveSphereRings = 16;
    public int PrimitiveRadialSegments = 32;
    public bool PrimitiveRadialSmooth = true;
    public bool PrimitiveRadialClosed = true;
    public bool PrimitiveRadialMapped = false;

    /// <summary>
    /// Primitive-plane option. When true, the plane rotates at render-time so it faces the active camera.
    /// </summary>
    public bool PrimitivePlaneFaceCamera = false;

    public string TextMeshFontPath = "minecraftia";
    public string TextMeshBaseString = "Text";
    public string TextMeshStringOverride = "";
    public bool TextMeshExtruded = false;
    public float TextMeshExtrusionDepth = 0.08f;
    public bool TextMeshFaceCamera = false;
    public int TextMeshHorizontalAlignment = 1;
    public int TextMeshVerticalAlignment = 1;
    public bool TextMeshAntialiasing = true;
    public float TextMeshFontSize = 64f;
    public bool TextMeshOutlineEnabled = false;
    public vec4 TextMeshOutlineColor = new vec4(0f, 0f, 0f, 1f);
    public float TextMeshOutlineThickness = 2f;

    public string GetEffectiveTextMeshString() =>
        string.IsNullOrEmpty(TextMeshStringOverride) ? TextMeshBaseString : TextMeshStringOverride;

    /// <summary>
    /// Path to the character texture-variant image selected in the spawn menu
    /// at import time (e.g. a "skin" PNG chosen from a character's
    /// <c>textures.nux</c> manifest), for objects created from
    /// <see cref="SourceAssetPath"/>. Empty means the model's own
    /// default/embedded texture should be used. Re-applied whenever the model
    /// is re-imported from <see cref="SourceAssetPath"/> (e.g. on project
    /// load), since the underlying loaders always start from the source
    /// file's own default texture.
    /// </summary>
    public string TextureOverridePath = "";

    // ── Visual ────────────────────────────────────────────────────────────────

    /// <summary>
    /// All <see cref="Mesh"/> instances attached to this object.
    /// Multiple meshes are supported (e.g. a character body made from several
    /// sub-meshes, or a block with separate overlay geometry).
    /// Use <see cref="AddMesh"/> / <see cref="RemoveMesh"/> to modify the list.
    /// </summary>
    public List<Mesh> Visuals { get; } = [];

    /// <summary>Attaches a mesh to this object's visual list.</summary>
    public void AddMesh(Mesh mesh)
    {
        if (mesh != null && !Visuals.Contains(mesh))
            Visuals.Add(mesh);
    }

    /// <summary>Detaches a mesh from this object's visual list.</summary>
    public void RemoveMesh(Mesh mesh)
    {
        Visuals.Remove(mesh);
        if (_inheritedTextures.Remove(mesh, out var previous) && mesh.TextureId == previous.Applied)
            mesh.TextureId = previous.Local;
    }

    // ── Transform – local cache ───────────────────────────────────────────────

    public vec3 Position;
    public vec3 Rotation;
    public vec3 Scale = vec3.Ones;

    /// <summary>
    /// The local position set by the user (before inheritance is applied).
    /// Use <see cref="SetLocalPosition"/> to keep the cache in sync.
    /// </summary>
    private vec3 _localPosition = vec3.Zero;
    public vec3 LocalPosition => _localPosition;

    /// <summary>
    /// The local rotation set by the user (before inheritance is applied).
    /// Use <see cref="SetLocalRotation"/> to keep the cache in sync.
    /// </summary>
    private vec3 _localRotation = vec3.Zero;
    public vec3 LocalRotation => _localRotation;
    private mat4? _localRotationMatrixOverride;

    /// <summary>
    /// The local scale set by the user (before inheritance is applied).
    /// Use <see cref="SetLocalScale"/> to keep the cache in sync.
    /// </summary>
    private vec3 _localScale = vec3.Ones;
    public vec3 LocalScale => _localScale;

    /// <summary>Sets the local position and keeps the cache in sync.</summary>
    public void SetLocalPosition(vec3 pos)
    {
        InvalidateRenderEvaluation();
        _localPosition = pos;
        Position = pos;
    }

    /// <summary>Sets the local rotation and keeps the cache in sync.</summary>
    public void SetLocalRotation(vec3 rot)
    {
        InvalidateRenderEvaluation();
        _localRotation = rot;
        Rotation = rot;
        _localRotationMatrixOverride = null;
    }

    /// <summary>
    /// Sets a local rotation using an exact authored matrix while retaining an
    /// equivalent Euler value for editors and animation. The exact matrix is
    /// used until the rotation is subsequently edited through SetLocalRotation.
    /// </summary>
    public void SetLocalRotationMatrix(mat4 matrix, vec3 equivalentEuler)
    {
        InvalidateRenderEvaluation();
        _localRotation = equivalentEuler;
        Rotation = equivalentEuler;
        _localRotationMatrixOverride = matrix;
    }

    /// <summary>Sets the local scale and keeps the cache in sync.</summary>
    public void SetLocalScale(vec3 scale)
    {
        InvalidateRenderEvaluation();
        _localScale = scale;
        Scale = scale;
    }

    // ── Bone target transform (used by BoneSceneObject) ───────────────────────

    public vec3 TargetPosition;
    public vec3 TargetRotation;

    // ── Bend ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Model-import lock_bend metadata for internal parts. Kept separate from
    /// the editor attachment setting and retained for older project files.
    /// </summary>
    public float LockBend = 1f;

    private bool _lockToBentHalf = true;

    /// <summary>Attach to the parent's bent-half origin, independently of imported part metadata.</summary>
    public bool LockToBentHalf
    {
        get => _lockToBentHalf;
        set
        {
            if (_lockToBentHalf == value) return;
            _lockToBentHalf = value;
            InvalidateRenderEvaluation();
        }
    }

    // ── Inheritance flags ─────────────────────────────────────────────────────

    /// <summary>When true, this object inherits the parent's position (default: true).</summary>
    public bool InheritPosition = true;

    /// <summary>When true, this object inherits the parent's rotation (default: true).</summary>
    public bool InheritRotation = true;

    /// <summary>When true, this object inherits the parent's scale (default: true).</summary>
    public bool InheritScale = true;

    public bool InheritAlpha = false;
    public bool InheritColor = false;
    public bool InheritTexture = false;
    public bool InheritSurface = true;
    public bool InheritSubsurface = true;
    public bool InheritGlowColor = true;
    public bool InheritSelect = false;

    private bool _inheritPose;
    public bool InheritPose
    {
        get => _inheritPose;
        set
        {
            if (_inheritPose == value) return;
            _inheritPose = value;
            InvalidateRenderEvaluation();
        }
    }

    private bool _usePropertyInheritance;

    /// <summary>
    /// Resolves Mine-imator material channels independently. False preserves
    /// whole-material sharing in native projects created before these flags existed.
    /// </summary>
    public bool UsePropertyInheritance
    {
        get => _usePropertyInheritance;
        set
        {
            // Legacy inherited references are not authored local overrides.
            if (value && !_usePropertyInheritance && !_hasExplicitMaterialSettings &&
                Parent != null && ReferenceEquals(_materialSettings, Parent._materialSettings))
                _materialSettings = null;
            _usePropertyInheritance = value;
        }
    }

    /// <summary>Rebuild inherited scale along the resolved local axes instead of shearing the child.</summary>
    public bool ScaleResize = true;

    // ── Pivot offset ──────────────────────────────────────────────────────────

    private vec3 _pivotOffset = new vec3(0, 0.5f, 0);

    /// <summary>
    /// Offset applied to the visual position so the object rotates/scales
    /// around a custom pivot point.  Changing this updates the visual immediately.
    /// </summary>
    public vec3 PivotOffset
    {
        get => _pivotOffset;
        set
        {
            _pivotOffset = value;
            UpdateVisualPosition();
            UpdateChildrenPivotOffsets();
        }
    }

    /// <summary>The editor's pivot inheritance setting; checked uses the parent's pivot frame.</summary>
    public bool InheritPivot
    {
        get => !InheritPivotOffset;
        set
        {
            PivotOffsetInheritsModelOrigin = false;
            InheritPivotOffset = !value;
        }
    }

    /// <summary>Preserves model-origin attachment for imported and older serialized inheritance flags.</summary>
    public bool PivotOffsetInheritsModelOrigin = true;

    private bool _inheritPivotOffset = false;

    /// <summary>
    /// Selects the parent's offset frame instead of its logical pivot frame.
    /// The offset direction is controlled by <see cref="PivotOffsetInheritsModelOrigin"/>.
    /// </summary>
    public bool InheritPivotOffset
    {
        get => _inheritPivotOffset;
        set
        {
            _inheritPivotOffset = value;
            UpdateVisualPosition();
            UpdateChildrenPivotOffsets();
        }
    }

    // ── Visibility ────────────────────────────────────────────────────────────

    public bool ObjectVisible = true;

    /// <summary>Editor hiding is independent of authored/keyframed visibility.</summary>
    public bool IsHidden { get; set; } = false;

    // Top-level runtime particles still follow their emitter's hidden state.
    internal SceneObject? RuntimeHiddenParent { get; set; }

    /// <summary>
    /// When true this object renders backfaces instead of front faces.
    /// Descendants inherit this through <see cref="GetEffectiveInvertFaces"/>.
    /// </summary>
    public bool InvertFaces = false;

    private bool _inheritVisibility = true;

    /// <summary>
    /// When true this object's effective visibility considers the parent's visibility.
    /// When false only <see cref="ObjectVisible"/> is used (default: true).
    /// </summary>
    public bool InheritVisibility
    {
        get => _inheritVisibility;
        set
        {
            _inheritVisibility = value;
            ApplyEffectiveVisibility();
        }
    }

    // ── Shadow casting ────────────────────────────────────────────────────────

    /// <summary>
    /// Controls whether this object casts shadows into both the directional
    /// (sun) shadow map and the point/spot-light shadow cubemaps. Read
    /// directly by <c>Viewport.RenderShadowCasters</c> and
    /// <c>Viewport.RenderPointShadowCasters</c> each frame — objects with this
    /// set to <c>false</c> still render normally but are skipped from every
    /// shadow pass.
    /// </summary>
    public bool CastShadow
    {
        get;
        set
        {
            field = value;
            ApplyCastShadow();
        }
    } = true;

    /// <summary>
    /// Reserved for future per-mesh shadow-state caching. Shadow casting is
    /// currently applied by the renderer reading <see cref="CastShadow"/>
    /// directly each frame, so no push-down is needed here.
    /// </summary>
    public void ApplyCastShadow()
    {
    }

    // ── Per-object render toggles ──────────────────────────────────────────

    /// <summary>
    /// Enables linear texture filtering for this object's textured meshes.
    /// Disabled by default for crisp nearest-neighbour sampling.
    /// </summary>
    public bool BlurTexture = false;

    /// <summary>
    /// Enables mipmap filtering for this object's textured meshes.
    /// Disabled by default.
    /// </summary>
    public bool TextureMipmaps = false;

    /// <summary>
    /// Controls whether this object contributes to and receives ambient occlusion.
    /// </summary>
    public bool IncludeInAmbientOcclusion = true;

    /// <summary>
    /// Controls whether this object is affected by scene fog.
    /// </summary>
    public bool IncludeInFog = true;

    /// <summary>
    /// Controls whether this object renders while the viewport is in rendered mode.
    /// </summary>
    public bool RenderInHighQuality = true;

    /// <summary>
    /// Controls whether this object renders while the viewport is in unrendered modes.
    /// </summary>
    public bool RenderInLowQuality = true;

    /// <summary>
    /// Per-object render depth offset added to each mesh sort depth.
    /// Positive values render later, negative values render earlier.
    /// </summary>
    public float RenderDepthOffset = 0f;

    // ── MaterialSettings ──────────────────────────────────────────────────────

    private MaterialSettings _materialSettings;

    /// <summary>
    /// Material settings applied to this object's meshes.  Setting this propagates
    /// the change to all child <see cref="SceneObject"/>s that have not set their
    /// own explicit material settings.
    /// </summary>
    public MaterialSettings MaterialSettings
    {
        get => _materialSettings;
        set
        {
            if (_materialSettings != value)
            {
                _materialSettings = value;
                OnMaterialSettingsChanged();
            }
        }
    }

    /// <summary>
    /// True when this object has explicitly set its own <see cref="MaterialSettings"/>
    /// rather than inheriting from a parent.  Children with explicit settings are
    /// skipped during propagation.
    /// </summary>
    protected bool _hasExplicitMaterialSettings = false;

    /// <summary>
    /// Marks this object's MaterialSettings as explicitly set (not inherited).
    /// If the current settings were inherited from a parent, they are cloned first
    /// so subsequent edits do not affect the parent's material.
    /// </summary>
    public void SetExplicitMaterialSettings()
    {
        if (_materialSettings == null)
        {
            _materialSettings = new MaterialSettings();
        }
        else if (!_hasExplicitMaterialSettings)
        {
            _materialSettings = _materialSettings.Clone();
        }
        _hasExplicitMaterialSettings = true;
    }

    /// <summary>
    /// True when this object owns explicit material overrides instead of inheriting.
    /// </summary>
    public bool HasExplicitMaterialSettings => _hasExplicitMaterialSettings;

    internal void RestoreInheritedMaterialSettings()
    {
        _hasExplicitMaterialSettings = false;
        _materialSettings = UsePropertyInheritance ? null : Parent?._materialSettings;
        ApplyMaterialSettingsToMeshes();
        PropagateMaterialSettingsToChildren();
    }

    /// <summary>
    /// Copies <paramref name="other"/>'s current material state (a cloned
    /// <see cref="MaterialSettings"/> plus whether it is explicit or inherited)
    /// directly onto this object and re-applies it to this object's own meshes.
    /// Used by duplication so a copy starts out looking identical to the
    /// object it was copied from instead of losing its material back to the
    /// un-set (null) default, which previously got silently overwritten by the
    /// next material edit made through the Properties panel.
    /// </summary>
    public void CopyMaterialSettingsFrom(SceneObject other)
    {
        if (other == null) return;
        _materialSettings = other._materialSettings?.Clone();
        _hasExplicitMaterialSettings = other._hasExplicitMaterialSettings;
        ApplyMaterialSettingsToMeshes();
    }

    private void OnMaterialSettingsChanged()
    {
        ApplyMaterialSettingsToMeshes();
        PropagateMaterialSettingsToChildren();
    }

    /// <summary>
    /// Applies the current <see cref="MaterialSettings"/> to this object's own meshes,
    /// then propagates to all descendant <see cref="SceneObject"/>s that do not have
    /// explicit settings of their own.
    /// </summary>
    public void PropagateMaterialSettingsToChildren()
    {
        if (UsePropertyInheritance)
        {
            RefreshInheritedMaterials();
            return;
        }
        if (_materialSettings == null) return;

        // Always apply to self first so the object that was just edited updates too.
        ApplyMaterialSettingsToMeshes();

        foreach (var child in GetChildrenObjects())
        {
            if (child.UsePropertyInheritance)
            {
                child.RefreshInheritedMaterials();
            }
            else if (!child._hasExplicitMaterialSettings)
            {
                child._materialSettings = _materialSettings;
                child.ApplyMaterialSettingsToMeshes();
                child.PropagateMaterialSettingsToChildren();
            }
        }
    }

    /// <summary>
    /// Returns a resolved copy without modifying authored settings. Each channel
    /// stops at the first ancestor whose corresponding inheritance flag is false.
    /// </summary>
    public MaterialSettings GetEffectiveMaterialSettings()
    {
        var resolved = _materialSettings?.Clone() ?? new MaterialSettings
        {
            Roughness = 1f,
            EmissionColor = new vec4(1f),
            EmissionEnergy = 0f,
            SubsurfaceRadius = vec3.Ones
        };
        if (!UsePropertyInheritance) return resolved;

        bool alpha = true, color = true, surface = true, subsurface = true, glow = true;
        resolved.EmissionEnergy = resolved.EmissionEnabled ? resolved.EmissionEnergy : 0f;
        for (SceneObject current = this; current.Parent != null; current = current.Parent)
        {
            alpha &= current.InheritAlpha;
            color &= current.InheritColor;
            surface &= current.InheritSurface;
            subsurface &= current.InheritSubsurface;
            glow &= current.InheritGlowColor;
            if (!(alpha || color || surface || subsurface || glow)) break;

            var parent = current.Parent._materialSettings;
            if (parent == null) continue;
            if (alpha)
                resolved.AlbedoColor.w *= parent.AlbedoColor.w * (1f - parent.Transparency);
            if (color)
            {
                resolved.AlbedoColor.x *= parent.AlbedoColor.x;
                resolved.AlbedoColor.y *= parent.AlbedoColor.y;
                resolved.AlbedoColor.z *= parent.AlbedoColor.z;
                resolved.BlendColor.x *= parent.BlendColor.x;
                resolved.BlendColor.y *= parent.BlendColor.y;
                resolved.BlendColor.z *= parent.BlendColor.z;
                resolved.MixColor = new vec4(
                    Math.Clamp(resolved.MixColor.x + parent.MixColor.x, 0f, 1f),
                    Math.Clamp(resolved.MixColor.y + parent.MixColor.y, 0f, 1f),
                    Math.Clamp(resolved.MixColor.z + parent.MixColor.z, 0f, 1f),
                    Math.Clamp(resolved.MixColor.w + parent.MixColor.w, 0f, 1f));
            }
            if (surface)
            {
                resolved.PbrEnabled |= parent.PbrEnabled;
                resolved.NormalStrength *= parent.NormalStrength;
                resolved.OcclusionStrength *= parent.OcclusionStrength;
                resolved.HeightScale = Math.Max(resolved.HeightScale, parent.HeightScale);
                resolved.Metallic = Math.Clamp(resolved.Metallic + parent.Metallic, 0f, 1f);
                resolved.Roughness = Math.Clamp(resolved.Roughness * parent.Roughness, 0f, 1f);
                resolved.EmissionEnergy += parent.EmissionEnabled ? parent.EmissionEnergy : 0f;
                resolved.EmissionEnabled |= parent.EmissionEnabled;
            }
            if (subsurface)
            {
                resolved.Subsurface += parent.Subsurface;
                resolved.SubsurfaceRadius = new vec3(
                    Math.Clamp(resolved.SubsurfaceRadius.x * parent.SubsurfaceRadius.x, 0f, 1f),
                    Math.Clamp(resolved.SubsurfaceRadius.y * parent.SubsurfaceRadius.y, 0f, 1f),
                    Math.Clamp(resolved.SubsurfaceRadius.z * parent.SubsurfaceRadius.z, 0f, 1f));
                resolved.SubsurfaceColor.x *= parent.SubsurfaceColor.x;
                resolved.SubsurfaceColor.y *= parent.SubsurfaceColor.y;
                resolved.SubsurfaceColor.z *= parent.SubsurfaceColor.z;
            }
            if (glow)
            {
                resolved.EmissionColor.x *= parent.EmissionColor.x;
                resolved.EmissionColor.y *= parent.EmissionColor.y;
                resolved.EmissionColor.z *= parent.EmissionColor.z;
            }
        }
        return resolved;
    }

    /// <summary>
    /// Reapply after animation and live camera texture updates, before any render
    /// passes. Legacy objects are traversed but retain their existing push updates.
    /// </summary>
    public void RefreshInheritedMaterials()
    {
        if (this is MiBoneSceneObject poseBone) poseBone.RefreshInheritedPoseBend();
        if (UsePropertyInheritance || _inheritedTextures.Count > 0) ApplyMaterialSettingsToMeshes();
        else ApplyAuxiliaryTextures();
        foreach (var child in _children)
            child.RefreshInheritedMaterials();
    }

    /// <summary>
    /// Finds the nearest explicit albedo override along the texture inheritance
    /// chain. Embedded textures and filtered model skin variants are not timeline overrides.
    /// </summary>
    public SceneObject? GetEffectiveTextureSource()
    {
        for (SceneObject current = this; ; current = current.Parent)
        {
            if (!string.IsNullOrEmpty(current.AlbedoTexturePath) ||
                !string.IsNullOrEmpty(current.CameraTextureObjectId))
                return current;
            if (!current.InheritTexture || current.Parent == null) return null;
        }
    }

    public string GetEffectiveAuxiliaryTexturePath(bool normal)
    {
        var source = GetAuxiliaryTextureSource(normal);
        return source == null ? "" : normal ? source.NormalTexturePath : source.EmissionTexturePath;
    }

    public string GetEffectiveSurfaceTexturePath() => GetEffectiveSurfaceTextureSource()?.SurfaceTexturePath ?? "";

    public SceneObject? GetEffectiveSurfaceTextureSource()
    {
        for (SceneObject current = this; ; current = current.Parent)
        {
            if (!string.IsNullOrEmpty(current.SurfaceTexturePath)) return current;
            if (!UsePropertyInheritance || !current.InheritTexture || current.Parent == null) return null;
        }
    }

    public string GetEffectiveAuxiliaryResourcePackId(bool normal)
    {
        var source = GetAuxiliaryTextureSource(normal);
        return source == null ? "" : normal ? source.NormalResourcePackId : source.EmissionResourcePackId;
    }

    private SceneObject? GetAuxiliaryTextureSource(bool normal)
    {
        for (SceneObject current = this; ; current = current.Parent)
        {
            if (!string.IsNullOrEmpty(normal ? current.NormalTexturePath : current.EmissionTexturePath) ||
                !string.IsNullOrEmpty(normal ? current.NormalResourcePackId : current.EmissionResourcePackId))
                return current;
            if (!UsePropertyInheritance || !current.InheritTexture || current.Parent == null) return null;
        }
    }

    /// <summary>Loads all material slots with the same orientation and padding as this object's diffuse texture.</summary>
    public uint LoadTexture(string path, bool reload = false)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        path = ProjectManager.Instance?.ResolveAssetPath(path) ?? path;
        bool modelTexture = this is CharacterSceneObject or MiBoneSceneObject;
        bool minecraftTexture = SpawnCategory is "Blocks" or "Items" or "Scenery";
        // Assimp already flips UVs to match top-first image rows.
        bool importedTexture = SpawnCategory == "Custom Models";
        return MineImatorLoader.Instance.LoadTextureFromFile(path,
            makeSquare: modelTexture, flipVertically: !modelTexture && !minecraftTexture && !importedTexture, reload: reload);
    }

    private void ApplyAuxiliaryTextures()
    {
        var normalSource = GetAuxiliaryTextureSource(true);
        var emissionSource = GetAuxiliaryTextureSource(false);
        uint normalTexture = normalSource?.LoadTexture(normalSource.NormalTexturePath) ?? 0;
        uint emissionTexture = emissionSource?.LoadTexture(emissionSource.EmissionTexturePath) ?? 0;
        uint surfaceTexture = LoadTexture(GetEffectiveSurfaceTexturePath());
        foreach (var mesh in Visuals)
        {
            if (mesh.PickOnly) continue;
            for (int i = 0; i < mesh.GetSurfaceCount(); i++)
                if (mesh.SurfaceGetMaterial(i) is StandardMaterial material)
                    material.SurfaceTexture = surfaceTexture;
            mesh.NormalTexture = normalSource != null && !string.IsNullOrEmpty(normalSource.NormalResourcePackId)
                ? ResourcePackMaterialTextures.GetTexture(normalSource.NormalResourcePackId,
                    mesh.MinecraftTextureKey, mesh.MinecraftItemTexture, true, mesh.TextureId)
                : normalTexture;
            mesh.NormalEnabled = mesh.NormalTexture != 0 && (normalSource?._materialSettings?.NormalEnabled ?? true);
            mesh.EmissionTexture = emissionSource != null && !string.IsNullOrEmpty(emissionSource.EmissionResourcePackId)
                ? ResourcePackMaterialTextures.GetTexture(emissionSource.EmissionResourcePackId,
                    mesh.MinecraftTextureKey, mesh.MinecraftItemTexture, false, mesh.TextureId)
                : emissionTexture;
        }
    }

    private readonly Dictionary<Mesh, (uint Local, uint Applied)> _inheritedTextures = new();

    private void ApplyInheritedTexture()
    {
        var source = UsePropertyInheritance ? GetEffectiveTextureSource() : null;
        uint? inherited = source != null && source != this
            ? source.Visuals.FirstOrDefault(mesh => !mesh.PickOnly)?.TextureId
            : null;
        if (source != null && !string.IsNullOrEmpty(source.CameraTextureObjectId) && source.CameraTextureId != 0)
            inherited = source.CameraTextureId;
        if (source != null && !inherited.HasValue &&
            string.IsNullOrEmpty(source.CameraTextureObjectId))
        {
            string path = !string.IsNullOrEmpty(source.AlbedoTexturePath)
                ? source.AlbedoTexturePath : source.TextureOverridePath;
            uint loaded = source.LoadTexture(path);
            if (loaded != 0) inherited = loaded;
        }
        foreach (var mesh in Visuals)
        {
            if (_inheritedTextures.TryGetValue(mesh, out var previous))
            {
                // Preserve a new texture assigned by animation, the UI, or a camera.
                if (mesh.TextureId == previous.Applied) mesh.TextureId = previous.Local;
                _inheritedTextures.Remove(mesh);
            }
            if (inherited.HasValue && !mesh.PickOnly)
            {
                _inheritedTextures[mesh] = (mesh.TextureId, inherited.Value);
                mesh.TextureId = inherited.Value;
            }
        }
    }

    /// <summary>
    /// Applies resolved settings to meshes without changing the local material.
    /// </summary>
    public void ApplyMaterialSettingsToMeshes()
    {
        ApplyInheritedTexture();
        ApplyAuxiliaryTextures();
        var settings = UsePropertyInheritance ? GetEffectiveMaterialSettings() : _materialSettings;
        if (settings == null) return;

        foreach (var mesh in Visuals)
        {
            mesh.Albedo = new vec3(
                settings.AlbedoColor.x,
                settings.AlbedoColor.y,
                settings.AlbedoColor.z) * mesh.BaseTint;
            mesh.BlendColor = UsePropertyInheritance
                ? mesh.BaseBlendColor * settings.BlendColor : settings.BlendColor;
            mesh.MixColor = UsePropertyInheritance
                ? new vec4(
                    Math.Clamp(mesh.BaseMixColor.x + settings.MixColor.x, 0f, 1f),
                    Math.Clamp(mesh.BaseMixColor.y + settings.MixColor.y, 0f, 1f),
                    Math.Clamp(mesh.BaseMixColor.z + settings.MixColor.z, 0f, 1f),
                    Math.Clamp(mesh.BaseMixColor.w + settings.MixColor.w, 0f, 1f))
                : settings.MixColor;
            mesh.DoubleSided = settings.DoubleSided;
            bool mappedAtlas = false;
            if (mesh is CubeMesh cube)
            {
                cube.SetMappedMirrors(settings.TextureMirror);
                mappedAtlas = cube.Mapped;
            }
            else if (mesh is RadialMesh radial)
            {
                radial.SetMappedMirrors(settings.TextureMirror);
                mappedAtlas = radial.Mapped;
            }
            // Mapped primitives transform UVs within each atlas cell, not across the atlas.
            mesh.TextureOffset = mappedAtlas ? new vec2(0f) : settings.TextureOffset;
            mesh.TextureRepeat = mappedAtlas ? new vec2(1f) : settings.TextureRepeat;
            mesh.TextureMirror = mappedAtlas ? new bvec2(false) : settings.TextureMirror;
            // Combine AlbedoColor.a with (1 - Transparency) so both routes
            // can control opacity: 0 Transparency = fully opaque.
            mesh.Alpha = mesh.BaseAlpha * settings.AlbedoColor.w * (1f - settings.Transparency);
            mesh.AlphaMode = settings.AlphaMode;
            mesh.AlphaClipThreshold = settings.AlphaClipThreshold;
            bool useAutoEmission = settings.AutoEmission && mesh.AutoEmissionLevel > 0;
            if (useAutoEmission)
            {
                mesh.EmissionEnabled = true;
                mesh.EmissionColor = vec3.Ones;
                mesh.EmissionEnergy = Math.Clamp(mesh.AutoEmissionLevel / 7.5f, 0f, 10f);
            }
            else
            {
                mesh.EmissionEnabled = settings.EmissionEnabled;
                mesh.EmissionColor = new vec3(
                    settings.EmissionColor.x,
                    settings.EmissionColor.y,
                    settings.EmissionColor.z);
                mesh.EmissionEnergy = settings.EmissionEnergy;
            }
            mesh.Subsurface = settings.Subsurface;
            mesh.SubsurfaceRadius = settings.SubsurfaceRadius;
            mesh.SubsurfaceColor = new vec3(
                settings.SubsurfaceColor.x,
                settings.SubsurfaceColor.y,
                settings.SubsurfaceColor.z);
            mesh.SubsurfaceHighlight = settings.SubsurfaceHighlight;
            mesh.SubsurfaceHighlightStrength = settings.SubsurfaceHighlightStrength;
            mesh.EmissionIndirectOnly = settings.EmissionIndirectOnly;
            for (int i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                if (mesh.SurfaceGetMaterial(i) is not StandardMaterial material) continue;
                material.Metallic = settings.Metallic;
                material.Roughness = settings.Roughness;
                material.PbrEnabled = settings.PbrEnabled;
                material.NormalStrength = settings.NormalStrength;
                material.OcclusionStrength = settings.OcclusionStrength;
                material.HeightScale = settings.HeightScale;
            }
        }
    }

    // ── Keyframes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-property keyframe lists.
    /// Key format: "propertyPath" (e.g. "visible", "position.x", "rotation.y").
    /// </summary>
    public Dictionary<string, List<ObjectKeyframe>> Keyframes = new();

    // ── Selection ────────────────────────────────────────────────────────────

    public bool IsSelectable = true;
    public bool IsLocked = false;
    public bool IsSelected;

    /// <summary>Sets the viewport lock on this object and every descendant.</summary>
    public void SetLockedRecursive(bool locked)
    {
        IsLocked = locked;
        foreach (var child in Children)
            child.SetLockedRecursive(locked);
    }

    /// <summary>Resolves a viewport hit without restricting scene-tree selection.</summary>
    public SceneObject? GetViewportSelectionTarget()
    {
        SceneObject? candidate = this;
        while (candidate != null && (candidate.IsLocked || !candidate.IsSelectable))
            candidate = candidate.Parent;
        return candidate;
    }

    /// <summary>
    /// When true this node is omitted from the scene-tree panel.
    /// Used for internal helper nodes (e.g. mesh-display children of bones)
    /// that should not be directly manipulated by the user.
    /// </summary>
    public bool HideInSceneTree = false;

    /// <summary>
    /// Marks this node as runtime-only transient state.
    /// Transient nodes are rendered normally but should be excluded from
    /// scene serialization and editor duplication workflows.
    /// </summary>
    public bool IsRuntimeTransient = false;

    /// <summary>
    /// Sets the selection state and applies or removes the selection material overlay.
    /// </summary>
    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        ApplySelectionMaterial(selected);
    }

    /// <summary>
    /// Reserved for future per-mesh material overlay work.
    /// The visible highlight effect is handled by the <c>RenderHighlightPass</c>
    /// in <c>Viewport</c> using an inflated back-face shell shader.
    /// </summary>
    public void ApplySelectionMaterial(bool selected) { }

    // ── Colour picking ───────────────────────────────────────────────────────

    /// <summary>Unique integer pick ID (1-based; 0 means "nothing").</summary>
    public int PickColorId { get; private set; }

    /// <summary>
    /// RGB colour encoding of <see cref="PickColorId"/>, in 0–1 range.
    /// Supports ~16 million unique objects.
    /// </summary>
    public vec3 PickColor { get; private set; }

    /// <summary>
    /// Assigns <see cref="ObjectId"/> and the pick-colour pair from
    /// <see cref="SelectionManager"/>.  Call once after construction.
    /// </summary>
    public void AssignObjectId()
    {
        var (uuid, pickColorId) = SelectionManager.Instance.GetNextObjectId();
        ObjectId    = uuid;
        PickColorId = pickColorId;
        GeneratePickColor();
    }

    private void GeneratePickColor()
    {
        // Bit-shift encoding: R=bits 0-7, G=bits 8-15, B=bits 16-23.
        // Decoded in AppViewport as: id = R | (G << 8) | (B << 16).
        // Supports up to 16,777,215 unique objects.
        PickColor = new vec3(
            ((PickColorId >>  0) & 0xFF) / 255f,
            ((PickColorId >>  8) & 0xFF) / 255f,
            ((PickColorId >> 16) & 0xFF) / 255f);
    }

    // ── Hierarchy ────────────────────────────────────────────────────────────

    private readonly List<SceneObject> _children = new();

    /// <summary>The parent SceneObject, or null if at the scene root.</summary>
    public SceneObject Parent { get; private set; }

    public IReadOnlyList<SceneObject> Children => _children;

    /// <summary>Adds a child and sets its <see cref="Parent"/>.</summary>
    public void AddChild(SceneObject child)
    {
        if (child == null || child == this) return;
        child.Parent?.RemoveChild(child);
        child.Parent = this;
        _children.Add(child);
        InvalidateRenderEvaluation();
    }

    /// <summary>Removes a child and clears its <see cref="Parent"/>.</summary>
    public void RemoveChild(SceneObject child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
            InvalidateRenderEvaluation();
        }
    }

    /// <summary>
    /// Re-parents this object under <paramref name="newParent"/> with cycle detection.
    /// Returns true on success.
    /// Passing null detaches the object from its current parent.
    /// </summary>
    public bool SetParent(SceneObject newParent)
    {
        if (newParent == this)
        {
            Console.Error.WriteLine("SceneObject.SetParent: cannot set self as parent");
            return false;
        }

        if (newParent == Parent) return false;

        // Cycle check: make sure newParent is not a descendant of this object.
        if (newParent != null)
        {
            var current = newParent;
            while (current != null)
            {
                if (current == this)
                {
                    Console.Error.WriteLine("SceneObject.SetParent: would create cyclic relationship");
                    return false;
                }
                current = current.Parent;
            }
        }

        Parent?.RemoveChild(this);
        newParent?.AddChild(this);

        // Re-evaluate pivot and visibility relative to the new parent.
        UpdateVisualPosition();
        ApplyEffectiveVisibility();

        return true;
    }

    /// <summary>Returns all direct <see cref="SceneObject"/> children.</summary>
    public SceneObject[] GetChildrenObjects()
    {
        return _children.ToArray();
    }

    /// <summary>
    /// Returns this object and all descendant <see cref="SceneObject"/>s
    /// in depth-first order (excluding this object itself).
    /// </summary>
    public SceneObject[] GetAllDescendants()
    {
        var result = new List<SceneObject>();
        CollectDescendants(result);
        return result.ToArray();
    }

    private void CollectDescendants(List<SceneObject> list)
    {
        foreach (var child in _children)
        {
            list.Add(child);
            child.CollectDescendants(list);
        }
    }

    /// <summary>
    /// Returns true if <paramref name="ancestor"/> appears somewhere up this
    /// object's parent chain.
    /// </summary>
    public bool IsDescendantOf(SceneObject ancestor)
    {
        var current = Parent;
        while (current != null)
        {
            if (current == ancestor) return true;
            current = current.Parent;
        }
        return false;
    }

    // ── Display ───────────────────────────────────────────────────────────────

    /// <summary>Returns the display name for UI use.</summary>
    public string GetDisplayName()
    {
        if (!string.IsNullOrEmpty(Name)) return Name;
        if (!string.IsNullOrEmpty(ObjectType)) return ObjectType;
        return "Object";
    }

    /// <summary>Returns the icon key for this object type (used by scene-tree UI).</summary>
    public virtual string GetObjectIcon()
    {
        return "Object";
    }

    // ── Visibility helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Gets the effective (resolved) visibility of this object, considering parent
    /// visibility and the <see cref="InheritVisibility"/> setting.
    /// </summary>
    public bool GetEffectiveVisibility()
    {
        FrameProfiler.Shared.Count(ProfileCounter.VisibilityQueries);
        long evaluation = _renderEvaluation;
        if (evaluation != 0 && _visibilityEvaluation == evaluation)
        {
            FrameProfiler.Shared.Count(ProfileCounter.VisibilityCacheHits);
            return _evaluatedVisibility;
        }

        using var timing = FrameProfiler.Shared.Measure(ProfileStage.VisibilityEvaluation);
        bool visible = ObjectVisible && (!_inheritVisibility || Parent == null || Parent.GetEffectiveVisibility());
        if (evaluation != 0)
        {
            _evaluatedVisibility = visible;
            _visibilityEvaluation = evaluation;
        }
        return visible;
    }

    /// <summary>Applies editor hiding to this object and its descendants.</summary>
    public bool SetHiddenRecursive(bool hidden)
    {
        bool changed = IsHidden != hidden;
        IsHidden = hidden;
        foreach (var child in Children)
            changed |= child.SetHiddenRecursive(hidden);
        return changed;
    }

    /// <summary>Runtime particles follow their emitter; ordinary children have independent hide flags.</summary>
    public bool IsHiddenForRendering() => IsHidden || RuntimeHiddenParent?.IsHiddenForRendering() == true;

    /// <summary>
    /// Gets whether this object should render backfaces, including parent inheritance.
    /// </summary>
    public bool GetEffectiveInvertFaces()
    {
        if (InvertFaces)
            return true;

        return Parent != null && Parent.GetEffectiveInvertFaces();
    }

    /// <summary>
    /// Sets <see cref="ObjectVisible"/> and immediately re-applies effective visibility.
    /// </summary>
    public void SetObjectVisible(bool visible)
    {
        ObjectVisible = visible;
        ApplyEffectiveVisibility();
    }

    /// <summary>Flips <see cref="ObjectVisible"/> and re-applies effective visibility.</summary>
    public void ToggleObjectVisibility()
    {
        SetObjectVisible(!ObjectVisible);
    }

    /// <summary>
    /// Pushes the effective visibility to the Visual and recursively updates children.
    /// </summary>
    private void ApplyEffectiveVisibility()
    {
        InvalidateRenderEvaluation();
        // No Godot .Visible on Visual in MonoGame; callers/renderer should
        // query GetEffectiveVisibility() when deciding whether to draw this object.
        UpdateChildrenVisibility();
    }

    private void UpdateChildrenVisibility()
    {
        foreach (var child in _children)
            child.ApplyEffectiveVisibility();
    }

    // ── World transform ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds the local TRS matrix for this object from its current
    /// <see cref="Position"/>, <see cref="Rotation"/> (Euler XYZ, radians),
    /// <see cref="Scale"/>, and <see cref="PivotOffset"/>.
    ///
    /// The mesh visually sits at <c>Position + PivotOffset</c>, and rotation/scale
    /// always happen around <c>Position</c> (the scene object's own origin), never
    /// the mesh geometry's center.
    /// Transform order (column-vector, applied right-to-left):
    ///   ModelBase * T(pos) * S * R * T(pivot)
    ///   1. T(pivot)  — displace mesh geometry to the pivot offset
    ///   2. R         — rotate around Position (local origin)
    ///   3. S         — scale along the timeline axes
    ///   4. T(pos)    — translate to world position
    /// </summary>
    public mat4 GetLocalMatrix()
    {
        GetTimelineTransform(out mat4 modelTransform, out vec3 position, out mat4 rotation);
        return modelTransform * mat4.Translate(position) * mat4.Scale(Scale) * rotation
            * mat4.Translate(_pivotOffset);
    }

    protected mat4 GetLocalRotationMatrix()
    {
        mat4 rx = mat4.RotateX(Rotation.x);
        mat4 ry = mat4.RotateY(Rotation.y);
        mat4 rz = mat4.RotateZ(Rotation.z);
        return _localRotationMatrixOverride ?? (rz * ry * rx);
    }

    protected virtual void GetTimelineTransform(out mat4 modelTransform, out vec3 position, out mat4 rotation)
    {
        modelTransform = mat4.Identity;
        position = Position;
        rotation = GetLocalRotationMatrix();
    }

    /// <summary>
    /// Returns the world-space TRS matrix for this object by recursively
    /// multiplying up the parent chain, respecting inheritance flags.
    /// </summary>
    public mat4 GetWorldMatrix()
    {
        return GetEvaluatedWorldMatrix(includePivot: true);
    }

    private mat4 GetEvaluatedWorldMatrix(bool includePivot)
    {
        FrameProfiler.Shared.Count(ProfileCounter.WorldMatrixQueries);
        long evaluation = _renderEvaluation;
        if (evaluation != 0 && _worldEvaluation == evaluation)
        {
            FrameProfiler.Shared.Count(ProfileCounter.WorldMatrixCacheHits);
            return includePivot ? _evaluatedWorldMatrix : _evaluatedWorldMatrixNoPivot;
        }

        using var timing = FrameProfiler.Shared.Measure(ProfileStage.TransformEvaluation);
        mat4 logicalWorld = ComputeWorldMatrixNoPivot();
        mat4 world = logicalWorld * mat4.Translate(_pivotOffset);
        if (evaluation != 0)
        {
            _evaluatedWorldMatrix = world;
            _evaluatedWorldMatrixNoPivot = logicalWorld;
            _worldEvaluation = evaluation;
        }
        return includePivot ? world : logicalWorld;
    }

    private mat4 ComputeWorldMatrixNoPivot()
    {
        GetTimelineTransform(out mat4 modelTransform, out vec3 position, out mat4 rotation);
        // GameMaker matrix_build rotates before scaling. The resize override
        // below deliberately rebuilds its basis in the opposite order.
        mat4 local = mat4.Translate(position) * mat4.Scale(Scale) * rotation;

        mat4 parentWorld = GetBendAdjustedParentMatrix() * modelTransform;
        mat4 world = parentWorld * local;

        // Mine-imator applies inheritance overrides after composing the full
        // transform. Removing rotation/scale must not change child placement.
        mat4 basis = world;
        if (ScaleResize || !InheritScale || this is ParticleSpawnerSceneObject)
        {
            vec3 parentScale = GetBasisScale(parentWorld);
            if (parentScale.x > 0f && parentScale.y > 0f && parentScale.z > 0f)
            {
                parentWorld.m00 /= parentScale.x; parentWorld.m01 /= parentScale.x; parentWorld.m02 /= parentScale.x;
                parentWorld.m10 /= parentScale.y; parentWorld.m11 /= parentScale.y; parentWorld.m12 /= parentScale.y;
                parentWorld.m20 /= parentScale.z; parentWorld.m21 /= parentScale.z; parentWorld.m22 /= parentScale.z;
            }
            if (!InheritRotation)
                parentWorld = mat4.Scale(GetBasisScale(parentWorld));
            vec3 scale = Scale;
            if (InheritScale)
            {
                for (SceneObject current = this; current.InheritScale && current.Parent != null; current = current.Parent)
                    scale *= current.Parent.Scale;
            }
            basis = parentWorld * rotation * mat4.Scale(scale);
        }
        else if (!InheritRotation)
        {
            basis = mat4.Scale(GetBasisScale(world)) * rotation;
        }

        world.m00 = basis.m00; world.m01 = basis.m01; world.m02 = basis.m02;
        world.m10 = basis.m10; world.m11 = basis.m11; world.m12 = basis.m12;
        world.m20 = basis.m20; world.m21 = basis.m21; world.m22 = basis.m22;
        if (this is MiBoneSceneObject poseBone)
            world *= poseBone.GetInheritedPoseTransform();
        if (!InheritPosition)
        {
            world.m30 = position.x;
            world.m31 = position.y;
            world.m32 = position.z;
        }
        return world;
    }

    private static vec3 GetBasisScale(mat4 matrix) => new(
        new vec3(matrix.m00, matrix.m01, matrix.m02).Length,
        new vec3(matrix.m10, matrix.m11, matrix.m12).Length,
        new vec3(matrix.m20, matrix.m21, matrix.m22).Length);

    /// <summary>
    /// Returns the logical world-space rotation/gizmo anchor before this object's
    /// visual pivot displacement, using the same model and inheritance stages as rendering.
    /// </summary>
    public vec3 GetWorldPosition()
    {
        mat4 world = GetWorldMatrixNoPivot();
        return new vec3(world.m30, world.m31, world.m32);
    }

    /// <summary>
    /// Returns the parent frame selected by <see cref="InheritPivotOffset"/>,
    /// including bend attachment. Position uses this entire frame when inherited;
    /// rotation and scale inheritance affect the child's basis independently.
    /// </summary>
    public mat4 GetParentWorldTransform()
    {
        return GetBendAdjustedParentMatrix();
    }

    /// <summary>
    /// Returns the world-space matrix for this object without the pivot offset
    /// baked in — i.e. the raw TRS using only Position, Rotation, and Scale.
    /// Useful when the caller needs the un-pivoted world position.
    /// </summary>
    public mat4 GetWorldMatrixNoPivot()
    {
        return GetEvaluatedWorldMatrix(includePivot: false);
    }

    private mat4 GetBendAdjustedParentMatrix()
    {
        if (Parent == null)
            return mat4.Identity;

        mat4 parentWorld = Parent.GetWorldMatrixNoPivot();
        if (InheritPivotOffset)
        {
            vec3 offset = Parent.PivotOffset;
            if (Parent.Visuals.OfType<ExtrudedItemMesh>().FirstOrDefault() is { } item)
                offset -= item.CenterOffset;
            // The editor's unchecked state removes the negative render displacement.
            // Imported flags instead attach directly to the original model-origin frame.
            parentWorld *= mat4.Translate(PivotOffsetInheritsModelOrigin ? offset : -offset);
        }

        // Internal model parts retain their authored attachment and rebased positions.
        // Editor objects instead choose the bent-origin frame with their own flag.
        bool followBentHalf = this is MiBoneSceneObject ? LockBend > 0f : LockToBentHalf;
        if (followBentHalf && Parent is MiBoneSceneObject bendAncestor)
        {
            parentWorld *= bendAncestor.GetBentHalfTransform(vec3.Zero);
        }

        return parentWorld;
    }

    // ── Pivot helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Invalidates the evaluated transform after pivot or inheritance changes.
    /// </summary>
    public void UpdateVisualPosition()
    {
        InvalidateRenderEvaluation();
    }

    private void UpdateChildrenPivotOffsets()
    {
        foreach (var child in _children)
        {
            child.UpdateVisualPosition();
            child.UpdateChildrenPivotOffsets();
        }
    }

    // ── Mesh helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all <see cref="Mesh"/> instances attached to this object and every
    /// descendant <see cref="SceneObject"/> in the hierarchy (depth-first).
    /// </summary>
    public List<Mesh> GetMeshInstancesRecursively()
    {
        using var timing = FrameProfiler.Shared.Measure(ProfileStage.MeshFlattening);
        FrameProfiler.Shared.Count(ProfileCounter.MeshFlattenCalls);
        var result = new List<Mesh>();
        Collect(this, result);
        return result;

        static void Collect(SceneObject obj, List<Mesh> meshes)
        {
            meshes.AddRange(obj.Visuals);
            foreach (var child in obj._children)
                Collect(child, meshes);
        }
    }

    /// <summary>
    /// Convenience alias for <see cref="AddMesh"/>.
    /// Adds a mesh to this object's visual list.
    /// </summary>
    public void AddVisualInstance(Mesh mesh) => AddMesh(mesh);
}

// ── ObjectKeyframe ────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single keyframe stored on a <see cref="SceneObject"/>.
/// Mirrors the Godot ObjectKeyframe class from ExampleSceneObject.
/// </summary>
public class ObjectKeyframe
{
    public int Frame { get; set; }
    public object Value { get; set; }
    public string InterpolationType { get; set; } = "linear";
}
