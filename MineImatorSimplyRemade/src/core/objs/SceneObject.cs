using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemadeNuxi.core.objs;

// ── MaterialSettings ──────────────────────────────────────────────────────────

/// <summary>
/// Per-object material overrides that can be inherited down the scene hierarchy.
/// Mirrors the Godot MaterialSettings class from ExampleSceneObject.
/// </summary>
public class MaterialSettings
{
    public vec4 AlbedoColor = new vec4(1f, 1f, 1f, 1f);
    public float Metallic = 0f;
    public float Roughness = 0.5f;
    public bool NormalEnabled = false;

    /// <summary>
    /// OpenGL texture handle for the normal map (0 = no normal map).
    /// Propagated down the scene hierarchy when <see cref="SceneObject.MaterialSettings"/>
    /// is updated, and applied to all <see cref="StandardMaterial"/> surfaces.
    /// </summary>
    public uint NormalTexture = 0;

    /// <summary>Alpha-transparency amount (0 = fully opaque, 1 = fully transparent).</summary>
    public float Transparency = 0f;
    public bool EmissionEnabled = false;
    public vec4 EmissionColor = new vec4(0f, 0f, 0f, 1f);
    public float EmissionEnergy = 1f;

    /// <summary>
    /// When true, both faces of meshes are rendered (back-face culling disabled).
    /// </summary>
    public bool DoubleSided = false;

    /// <summary>
    /// Creates a copy of these material settings.
    /// </summary>
    public MaterialSettings Clone()
    {
        return new MaterialSettings
        {
            AlbedoColor = this.AlbedoColor,
            Metallic = this.Metallic,
            Roughness = this.Roughness,
            NormalEnabled = this.NormalEnabled,
            NormalTexture = this.NormalTexture,
            Transparency = this.Transparency,
            EmissionEnabled = this.EmissionEnabled,
            EmissionColor = this.EmissionColor,
            EmissionEnergy = this.EmissionEnergy,
            DoubleSided = this.DoubleSided
        };
    }
}

// ── SceneObject ───────────────────────────────────────────────────────────────

public class SceneObject
{
    // ── Basic identity ────────────────────────────────────────────────────────

    public string ObjectType = "Object";
    public string Name;
    public string ObjectId;

    public string SpawnCategory = "";
    public string BlockVariant = "";
    public string TextureType = "item";
    public string ResourcePackId = "";

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

    // ── Visual ────────────────────────────────────────────────────────────────

    /// <summary>
    /// All <see cref="Mesh"/> instances attached to this object.
    /// Multiple meshes are supported (e.g. a character body made from several
    /// sub-meshes, or a block with separate overlay geometry).
    /// Use <see cref="AddMesh"/> / <see cref="RemoveMesh"/> to modify the list.
    /// </summary>
    public List<Mesh> Visuals { get; } = new();

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

    /// <summary>
    /// The local scale set by the user (before inheritance is applied).
    /// Use <see cref="SetLocalScale"/> to keep the cache in sync.
    /// </summary>
    private vec3 _localScale = vec3.Ones;
    public vec3 LocalScale => _localScale;

    /// <summary>Sets the local position and keeps the cache in sync.</summary>
    public void SetLocalPosition(vec3 pos)
    {
        _localPosition = pos;
        Position = pos;
    }

    /// <summary>Sets the local rotation and keeps the cache in sync.</summary>
    public void SetLocalRotation(vec3 rot)
    {
        _localRotation = rot;
        Rotation = rot;
    }

    /// <summary>Sets the local scale and keeps the cache in sync.</summary>
    public void SetLocalScale(vec3 scale)
    {
        _localScale = scale;
        Scale = scale;
    }

    // ── Bone target transform (used by BoneSceneObject) ───────────────────────

    public vec3 TargetPosition;
    public vec3 TargetRotation;

    // ── Bend ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// When &gt; 0 this object follows the parent bone's bent-half transform.
    /// Set by the importer for parts that should bend with their parent.
    /// </summary>
    public float LockBend = 1f;

    // ── Inheritance flags ─────────────────────────────────────────────────────

    /// <summary>When true, this object inherits the parent's position (default: true).</summary>
    public bool InheritPosition = true;

    /// <summary>When true, this object inherits the parent's rotation (default: true).</summary>
    public bool InheritRotation = true;

    /// <summary>When true, this object inherits the parent's scale (default: true).</summary>
    public bool InheritScale = true;

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

    private bool _inheritPivotOffset = false;

    /// <summary>
    /// When true this object accumulates the parent's pivot offset into its own
    /// visual position.  When false the parent's pivot offset is ignored (default).
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

    private bool _castShadow = true;

    /// <summary>
    /// Controls whether this object casts shadows.  Changing this propagates to
    /// all <see cref="Mesh"/> instances in the Visual hierarchy.
    /// </summary>
    public bool CastShadow
    {
        get => _castShadow;
        set
        {
            _castShadow = value;
            ApplyCastShadow();
        }
    }

    /// <summary>
    /// Propagates the current <see cref="CastShadow"/> value to all meshes.
    /// No-op when <see cref="Visual"/> is null or returns no meshes.
    /// </summary>
    public void ApplyCastShadow()
    {
        // Shadow casting is handled by the renderer; nothing to push here until
        // the rendering pipeline tracks per-mesh shadow state.
    }

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
        if (_materialSettings == null) return;

        // Always apply to self first so the object that was just edited updates too.
        ApplyMaterialSettingsToMeshes();

        foreach (var child in GetChildrenObjects())
        {
            if (!child._hasExplicitMaterialSettings)
            {
                child._materialSettings = _materialSettings;
                child.ApplyMaterialSettingsToMeshes();
                child.PropagateMaterialSettingsToChildren();
            }
        }
    }

    /// <summary>
    /// Applies the current <see cref="MaterialSettings"/> to this object's own meshes.
    /// </summary>
    public void ApplyMaterialSettingsToMeshes()
    {
        if (_materialSettings == null) return;

        foreach (var mesh in Visuals)
        {
            mesh.Albedo = new vec3(
                _materialSettings.AlbedoColor.x,
                _materialSettings.AlbedoColor.y,
                _materialSettings.AlbedoColor.z);
            mesh.DoubleSided = _materialSettings.DoubleSided;
            // Combine AlbedoColor.a with (1 - Transparency) so both routes
            // can control opacity: 0 Transparency = fully opaque.
            mesh.Alpha = _materialSettings.AlbedoColor.w * (1f - _materialSettings.Transparency);
            mesh.EmissionEnabled = _materialSettings.EmissionEnabled;
            mesh.EmissionColor = new vec3(
                _materialSettings.EmissionColor.x,
                _materialSettings.EmissionColor.y,
                _materialSettings.EmissionColor.z);
            mesh.EmissionEnergy = _materialSettings.EmissionEnergy;
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
    public bool IsSelected;

    /// <summary>
    /// When true this node is omitted from the scene-tree panel.
    /// Used for internal helper nodes (e.g. mesh-display children of bones)
    /// that should not be directly manipulated by the user.
    /// </summary>
    public bool HideInSceneTree = false;

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
    }

    /// <summary>Removes a child and clears its <see cref="Parent"/>.</summary>
    public void RemoveChild(SceneObject child)
    {
        if (_children.Remove(child))
            child.Parent = null;
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
        if (!_inheritVisibility)
            return ObjectVisible;

        if (Parent != null)
            return ObjectVisible && Parent.GetEffectiveVisibility();

        return ObjectVisible;
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
    ///   T(pos) * R * S * T(pivot)
    ///   1. T(pivot)  — displace mesh geometry to the pivot offset
    ///   2. S         — scale
    ///   3. R         — rotate around Position (local origin)
    ///   4. T(pos)    — translate to world position
    /// </summary>
    public mat4 GetLocalMatrix()
    {
        vec3 pivot = GetAccumulatedPivotOffset();
        mat4 t      = mat4.Translate(Position);
        mat4 tPivot = mat4.Translate(pivot);
        mat4 rx = mat4.RotateX(Rotation.x);
        mat4 ry = mat4.RotateY(Rotation.y);
        mat4 rz = mat4.RotateZ(Rotation.z);
        mat4 s  = mat4.Scale(Scale);
        return t * rz * ry * rx * s * tPivot;
    }

    /// <summary>
    /// Returns the world-space TRS matrix for this object by recursively
    /// multiplying up the parent chain, respecting inheritance flags.
    /// </summary>
    public mat4 GetWorldMatrix()
    {
        mat4 local = GetLocalMatrix();

        if (Parent == null)
            return local;

        mat4 parentWorld = GetBendAdjustedParentMatrix();

        // Selectively strip parent contributions that are not inherited.
        // For full inheritance just multiply; partial inheritance is handled
        // by rebuilding the parent matrix with only the inherited components.
        if (InheritPosition && InheritRotation && InheritScale && InheritPivotOffset)
            return parentWorld * local;

        // Decompose parent matrix into T, R, S and recombine with only the
        // parts this child wants to inherit.
        // GlmSharp is row-major: translation lives in Row3 (m30, m31, m32).
        vec3 parentPos = new vec3(parentWorld.m30, parentWorld.m31, parentWorld.m32);
        // Extract scale lengths from the upper-left 3×3 rows.
        vec3 row0 = new vec3(parentWorld.m00, parentWorld.m01, parentWorld.m02);
        vec3 row1 = new vec3(parentWorld.m10, parentWorld.m11, parentWorld.m12);
        vec3 row2 = new vec3(parentWorld.m20, parentWorld.m21, parentWorld.m22);
        vec3 parentScale = new vec3(row0.Length, row1.Length, row2.Length);

        // When InheritPivotOffset is false, cancel the parent's pivot displacement
        // from the decomposed translation so the child is not shifted by it.
        if (!InheritPivotOffset)
            parentPos += Parent._pivotOffset;

        // Normalised rotation rows
        mat4 parentRot = mat4.Identity;
        if (parentScale.x != 0) { parentRot.m00 = row0.x / parentScale.x; parentRot.m01 = row0.y / parentScale.x; parentRot.m02 = row0.z / parentScale.x; }
        if (parentScale.y != 0) { parentRot.m10 = row1.x / parentScale.y; parentRot.m11 = row1.y / parentScale.y; parentRot.m12 = row1.z / parentScale.y; }
        if (parentScale.z != 0) { parentRot.m20 = row2.x / parentScale.z; parentRot.m21 = row2.y / parentScale.z; parentRot.m22 = row2.z / parentScale.z; }

        mat4 inherited = mat4.Identity;
        if (InheritPosition) inherited = mat4.Translate(parentPos) * inherited;
        if (InheritRotation) inherited = inherited * parentRot;
        if (InheritScale)    inherited = inherited * mat4.Scale(parentScale);

        return inherited * local;
    }

    /// <summary>
    /// Returns the world-space coordinates of this object's <see cref="Position"/> point
    /// (i.e. the rotation/gizmo anchor), without any pivot-offset displacement.
    /// For a root object this equals <see cref="Position"/>; for a child it is
    /// <see cref="Position"/> transformed by the parent's world matrix.
    /// </summary>
    public vec3 GetWorldPosition()
    {
        if (Parent == null)
            return Position;

        mat4 parentTransform = GetParentWorldTransform();
        // Transform Position by the parent matrix (GlmSharp column-vector: mat4 * vec4).
        vec4 worldPos = parentTransform * new vec4(Position, 1f);
        return new vec3(worldPos.x, worldPos.y, worldPos.z);
    }

    /// <summary>
    /// Returns the effective parent world matrix as seen by this child, accounting
    /// for <see cref="InheritPivotOffset"/> by cancelling the parent's pivot
    /// displacement when the flag is false.
    /// </summary>
    public mat4 GetParentWorldTransform()
    {
        if (Parent == null)
            return mat4.Identity;

        mat4 parentWorld = GetBendAdjustedParentMatrix();

        if (InheritPivotOffset)
            return parentWorld;

        // Decompose and reconstruct the parent matrix without its pivot offset
        // so the gizmo/logical position anchor is not displaced by the parent's pivot.
        vec3 parentPos = new vec3(parentWorld.m30, parentWorld.m31, parentWorld.m32);
        vec3 row0 = new vec3(parentWorld.m00, parentWorld.m01, parentWorld.m02);
        vec3 row1 = new vec3(parentWorld.m10, parentWorld.m11, parentWorld.m12);
        vec3 row2 = new vec3(parentWorld.m20, parentWorld.m21, parentWorld.m22);
        vec3 parentScale = new vec3(row0.Length, row1.Length, row2.Length);

        // Cancel the parent's pivot displacement.
        parentPos += Parent._pivotOffset;

        mat4 parentRot = mat4.Identity;
        if (parentScale.x != 0) { parentRot.m00 = row0.x / parentScale.x; parentRot.m01 = row0.y / parentScale.x; parentRot.m02 = row0.z / parentScale.x; }
        if (parentScale.y != 0) { parentRot.m10 = row1.x / parentScale.y; parentRot.m11 = row1.y / parentScale.y; parentRot.m12 = row1.z / parentScale.y; }
        if (parentScale.z != 0) { parentRot.m20 = row2.x / parentScale.z; parentRot.m21 = row2.y / parentScale.z; parentRot.m22 = row2.z / parentScale.z; }

        mat4 result = mat4.Identity;
        if (InheritScale)    result = result * mat4.Scale(parentScale);
        if (InheritRotation) result = result * parentRot;
        if (InheritPosition) result = mat4.Translate(parentPos) * result;
        return result;
    }

    /// <summary>
    /// Returns the world-space matrix for this object without the pivot offset
    /// baked in — i.e. the raw TRS using only Position, Rotation, and Scale.
    /// Useful when the caller needs the un-pivoted world position.
    /// </summary>
    public mat4 GetWorldMatrixNoPivot()
    {
        mat4 t = mat4.Translate(Position);
        mat4 rx = mat4.RotateX(Rotation.x);
        mat4 ry = mat4.RotateY(Rotation.y);
        mat4 rz = mat4.RotateZ(Rotation.z);
        mat4 s = mat4.Scale(Scale);
        mat4 localNoPivot = t * rz * ry * rx * s;

        if (Parent == null)
            return localNoPivot;

        return Parent.GetWorldMatrix() * localNoPivot;
    }

    private mat4 GetBendAdjustedParentMatrix()
    {
        if (Parent == null)
            return mat4.Identity;

        mat4 parentWorld = Parent.GetWorldMatrix();

        if (LockBend > 0f && Parent is MiBoneSceneObject bendAncestor)
        {
            vec3 pivotOffset = -GetAccumulatedPivotOffset();
            parentWorld *= bendAncestor.GetBentHalfTransform(pivotOffset);
        }

        return parentWorld;
    }

    // ── Pivot helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the accumulated pivot offset from this object and its parent chain,
    /// respecting each object's <see cref="InheritPivotOffset"/> flag.
    /// </summary>
    public vec3 GetAccumulatedPivotOffset()
    {
        var accumulated = _pivotOffset;

        if (_inheritPivotOffset && Parent != null)
            accumulated += Parent.GetAccumulatedPivotOffset();

        return accumulated;
    }

    /// <summary>
    /// Recalculates the visual position offset from the accumulated pivot.
    /// Call after pivot or inheritance changes.
    /// </summary>
    public void UpdateVisualPosition()
    {
        // In MonoGame the visual offset is tracked separately from the scene node
        // position.  Callers/renderers should offset by -GetAccumulatedPivotOffset()
        // when computing the final world matrix for the visual mesh.
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
        var result = new List<Mesh>(Visuals);
        foreach (var child in _children)
            result.AddRange(child.GetMeshInstancesRecursively());
        return result;
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
