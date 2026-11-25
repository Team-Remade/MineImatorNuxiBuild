using System.Runtime.InteropServices;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.material.materials;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl;

// Geometry instancing for the main scene pass. Consecutive draws of meshes with identical geometry and
// identical shading state are submitted as one instanced draw that only differs by model matrix.
public partial class Mesh
{
    /// <summary>Turns scene-pass instancing off, e.g. to compare against per-mesh draws.</summary>
    public static bool InstancingEnabled = true;

    /// <summary>
    /// Lets the viewport move identical meshes next to each other in draw order. Off by default: moving a
    /// mesh across others can change the result of partially transparent parts that write depth.
    /// </summary>
    public static bool InstancingReorder = false;

    /// <summary>Identity of this mesh's shared geometry, or null when it cannot be instanced.</summary>
    internal object? InstanceGeometryKey => IsInstanceable ? GetGeometryShare() : null;

    private const int InstanceFloats = 16;
    private const uint InstanceAttribute = 5;

    private static List<mat4>? _pendingInstances;
    private static uint _instanceVbo;
    private static GL? _instanceVboGl;

    /// <summary>
    /// Model matrices for the next <see cref="Render"/> call. The first entry is the mesh being rendered;
    /// the call then draws every entry in order with one instanced draw. Set to null afterwards.
    /// </summary>
    internal static void SetPendingInstances(List<mat4>? models) =>
        _pendingInstances = models is { Count: > 1 } ? models : null;

    // ── Exact geometry sharing ────────────────────────────────────────────────

    private sealed class GeometryShare(float[] data, uint[]? indices)
    {
        internal readonly float[] Data = data;
        internal readonly uint[]? Indices = indices;
    }

    private static readonly Dictionary<ulong, List<WeakReference<GeometryShare>>> GeometryShares = new();
    private GeometryShare? _geometryShare;

    private GeometryShare? GetGeometryShare()
    {
        if (_geometryShare != null) return _geometryShare;
        if (_baseVertexData == null) return null;
        var indices = Indices is { Length: > 0 } ? Indices : null;
        ulong hash = HashGeometry(_baseVertexData, indices);
        lock (GeometryShares)
        {
            if (!GeometryShares.TryGetValue(hash, out var candidates))
                GeometryShares[hash] = candidates = new List<WeakReference<GeometryShare>>();
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                if (!candidates[i].TryGetTarget(out var share))
                {
                    candidates.RemoveAt(i);
                    continue;
                }
                if (share.Data.AsSpan().SequenceEqual(_baseVertexData) &&
                    (share.Indices == null ? indices == null : indices != null && share.Indices.AsSpan().SequenceEqual(indices)))
                    return _geometryShare = share;
            }
            var created = new GeometryShare(_baseVertexData, indices == null ? null : (uint[])indices.Clone());
            candidates.Add(new WeakReference<GeometryShare>(created));
            return _geometryShare = created;
        }
    }

    private static ulong HashGeometry(float[] data, uint[]? indices)
    {
        const ulong prime = 0x100000001B3UL;
        ulong hash = 0xCBF29CE484222325UL ^ (ulong)data.Length;
        foreach (uint word in MemoryMarshal.Cast<float, uint>(data))
            hash = (hash ^ word) * prime;
        hash = (hash ^ (ulong)(indices?.Length ?? -1)) * prime;
        if (indices != null)
            foreach (uint index in indices)
                hash = (hash ^ index) * prime;
        return hash;
    }

    // ── Eligibility and equality ──────────────────────────────────────────────

    /// <summary>
    /// True when this mesh's draw depends only on its model matrix, so it may share an instanced draw.
    /// Skinned, actively bent, shape-keyed, cloth, wind, animated-texture, glint, shader-graph and overlay
    /// meshes keep per-mesh draws because their GPU inputs are per mesh.
    /// </summary>
    internal bool IsInstanceable =>
        InstancingEnabled && _vao != 0 && _shader != null && _baseVertexData != null && Vertices.Count > 0 &&
        ShapeKeys.Count == 0 && !HasClothGeometry && !IsSkinned && (GpuBend == null || GpuBend.Angle == vec3.Zero) &&
        !WindActive &&
        string.IsNullOrEmpty(AnimationKey) && CustomShader is not { Enabled: true } &&
        !DepthTestDisabled && !PickOnly && !EndPortalSurface && !IsTextAlphaMask &&
        GlintMode == MaterialGlintMode.None;

    /// <summary>
    /// True when drawing <paramref name="other"/> right after this mesh, with the same viewport-supplied
    /// state, would produce the same GL state except for the model matrix.
    /// </summary>
    internal bool CanInstanceWith(Mesh other)
    {
        if (ReferenceEquals(this, other)) return IsInstanceable;
        if (Vertices.Count != other.Vertices.Count || Indices?.Length != other.Indices?.Length) return false;
        if (!IsInstanceable || !other.IsInstanceable) return false;
        if (_shader!.ShaderProgram != other._shader!.ShaderProgram) return false;
        if (!InstanceStateEquals(other)) return false;
        var share = GetGeometryShare();
        return share != null && ReferenceEquals(share, other.GetGeometryShare());
    }

    /// <summary>
    /// Compares every mesh and material setting that reaches the GPU in <see cref="Render"/>, other than
    /// geometry (compared through <see cref="GetGeometryShare"/>) and the model matrix. When a setting is
    /// added to <see cref="Mesh"/> or <see cref="StandardMaterial"/> it must be added here; the
    /// <c>--instancing</c> regression check fails for any setting this method does not notice.
    /// </summary>
    private bool InstanceStateEquals(Mesh other) =>
        Unlit == other.Unlit && IncludeInFog == other.IncludeInFog &&
        BlurTexture == other.BlurTexture && TextureMipmaps == other.TextureMipmaps &&
        MaterialStateEquals(other);

    /// <summary>
    /// <see cref="InstanceStateEquals"/> without the four settings that the viewport overrides for the
    /// duration of one draw call (<see cref="Unlit"/>, <see cref="IncludeInFog"/>, <see cref="BlurTexture"/>
    /// and <see cref="TextureMipmaps"/>). The render path compares those against the values it actually used.
    /// </summary>
    private bool MaterialStateEquals(Mesh other)
    {
        // Wind, bend and custom-shader state only matter when active, which IsInstanceable rules out,
        // except that CustomShader is compared by reference in case a disabled graph is re-enabled.
        if (AnimationKey != other.AnimationKey || EndPortalSurface != other.EndPortalSurface ||
            MinecraftTextureKey != other.MinecraftTextureKey || MinecraftItemTexture != other.MinecraftItemTexture ||
            AffectedByWind != other.AffectedByWind || TerrainAffectedByWind != other.TerrainAffectedByWind ||
            TerrainWindKind != other.TerrainWindKind ||
            DepthTestDisabled != other.DepthTestDisabled || DepthWriteDisabled != other.DepthWriteDisabled ||
            IsTranslucent != other.IsTranslucent || PickOnly != other.PickOnly ||
            AlphaMaskTextureId != other.AlphaMaskTextureId || IsTextAlphaMask != other.IsTextAlphaMask ||
            TextMaskOutlineColor != other.TextMaskOutlineColor || CullFrontFaces != other.CullFrontFaces ||
            BaseTint != other.BaseTint || BaseDoubleSided != other.BaseDoubleSided || BiomeTint != other.BiomeTint ||
            BaseAlpha != other.BaseAlpha || BaseBlendColor != other.BaseBlendColor || BaseMixColor != other.BaseMixColor ||
            !ReferenceEquals(CustomShader, other.CustomShader))
            return false;

        var a = DefaultMaterial;
        var b = other.DefaultMaterial;
        if (ReferenceEquals(a, b)) return true;
        return a.AlbedoColor == b.AlbedoColor && a.BlendColor == b.BlendColor && a.MixColor == b.MixColor &&
               ColorAdjustmentsEqual(a.ColorAdjustments, b.ColorAdjustments) &&
               a.AlbedoTexture == b.AlbedoTexture && a.Metallic == b.Metallic && a.Roughness == b.Roughness &&
               a.PbrEnabled == b.PbrEnabled && a.SurfaceTexture == b.SurfaceTexture &&
               a.NormalStrength == b.NormalStrength && a.OcclusionStrength == b.OcclusionStrength &&
               a.HeightScale == b.HeightScale && a.NormalEnabled == b.NormalEnabled &&
               a.NormalTexture == b.NormalTexture && a.Transparency == b.Transparency &&
               a.AlphaMode == b.AlphaMode && a.BlendMode == b.BlendMode &&
               a.AlphaClipThreshold == b.AlphaClipThreshold && a.EmissionEnabled == b.EmissionEnabled &&
               a.EmissionTexture == b.EmissionTexture && a.Emission == b.Emission &&
               a.EmissionEnergyMultiplier == b.EmissionEnergyMultiplier && a.Subsurface == b.Subsurface &&
               a.SubsurfaceRadius == b.SubsurfaceRadius && a.SubsurfaceColor == b.SubsurfaceColor &&
               a.SubsurfaceHighlight == b.SubsurfaceHighlight &&
               a.SubsurfaceHighlightStrength == b.SubsurfaceHighlightStrength &&
               a.EmissionIndirectOnly == b.EmissionIndirectOnly && a.GlintMode == b.GlintMode &&
               a.GlintTexture == b.GlintTexture && a.GlintScale == b.GlintScale && a.GlintSpeed == b.GlintSpeed &&
               a.GlintStrength == b.GlintStrength && a.AutoEmissionLevel == b.AutoEmissionLevel &&
               a.DoubleSided == b.DoubleSided && a.TextureOffset == b.TextureOffset &&
               a.TextureRepeat == b.TextureRepeat && a.TextureMirror.x == b.TextureMirror.x &&
               a.TextureMirror.y == b.TextureMirror.y;
    }

    private static bool ColorAdjustmentsEqual(MaterialColorAdjustments a, MaterialColorAdjustments b) =>
        a.MultiplyHsv == b.MultiplyHsv && a.AddRgb == b.AddRgb && a.AddHsv == b.AddHsv &&
        a.SubtractRgb == b.SubtractRgb && a.SubtractHsv == b.SubtractHsv;

    // ── Instanced draw ────────────────────────────────────────────────────────

    private unsafe void DrawInstanced(List<mat4> models)
    {
        int count = models.Count;
        if (_instanceVboGl != _gl || _instanceVbo == 0)
        {
            _instanceVbo = _gl.GenBuffer();
            _instanceVboGl = _gl;
        }

        _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVbo);
        fixed (mat4* data = CollectionsMarshal.AsSpan(models))
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(count * InstanceFloats * sizeof(float)), data, GLEnum.StreamDraw);
        for (uint column = 0; column < 4; column++)
        {
            uint location = InstanceAttribute + column;
            _gl.EnableVertexAttribArray(location);
            _gl.VertexAttribPointer(location, 4, GLEnum.Float, false, InstanceFloats * sizeof(float),
                (void*)(column * 4 * sizeof(float)));
            _gl.VertexAttribDivisor(location, 1);
        }

        if (Indices != null && _ebo != 0)
            _gl.DrawElementsInstanced(GLEnum.Triangles, (uint)Indices.Length, GLEnum.UnsignedInt, (void*)0, (uint)count);
        else
            _gl.DrawArraysInstanced(GLEnum.Triangles, 0, (uint)Vertices.Count, (uint)count);

        for (uint column = 0; column < 4; column++)
        {
            uint location = InstanceAttribute + column;
            _gl.VertexAttribDivisor(location, 0);
            _gl.DisableVertexAttribArray(location);
        }
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
    }
}
