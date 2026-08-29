using System.Numerics;
using System.Runtime.InteropServices;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// C# mirror of the <c>MeshUniforms</c> std140 block declared in
/// <c>assets/shaders/simple.vert</c>/<c>simple.frag</c>. Field order and padding
/// here MUST exactly match the GLSL block - std140 packs vec2/vec3/scalars on
/// 16-byte boundaries in ways that don't follow normal C# struct packing, hence
/// the explicit padding fields.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MeshUniforms
{
    public Matrix4x4 MVP;
    public Matrix4x4 Model;
    public Vector2 TexOffset;
    public float TexScaleV;
    public float _pad0;
    public Vector2 TexUvOffset;
    public Vector2 TexUvRepeat;
    public Vector2 TexUvMirror;
    public int IsSkinned;
    public int UseInstancing;
    public Vector2 _pad1;

    public static MeshUniforms Default => new()
    {
        MVP = Matrix4x4.Identity,
        Model = Matrix4x4.Identity,
        TexOffset = Vector2.Zero,
        TexScaleV = 1f,
        TexUvOffset = Vector2.Zero,
        TexUvRepeat = Vector2.One,
        TexUvMirror = Vector2.Zero,
        IsSkinned = 0,
        UseInstancing = 0,
    };
}

/// <summary>
/// C# mirror of the <c>MeshMaterial</c> std140 block declared in
/// <c>assets/shaders/simple.frag</c>. See <see cref="MeshUniforms"/> for the
/// padding-matching rule.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MeshMaterialUniforms
{
    public Vector3 Albedo;
    public float Alpha;
    public Vector4 BlendColor;
    public Vector4 MixColor;
    public int UseTexture;
    public int IsUnlit;
    public int EmissionEnabled;
    public float EmissionEnergy;
    public Vector3 EmissionColor;
    public float _pad0;

    public static MeshMaterialUniforms Default => new()
    {
        Albedo = Vector3.One,
        Alpha = 1f,
        BlendColor = Vector4.One,
        MixColor = new Vector4(0, 0, 0, 0),
        UseTexture = 0,
        IsUnlit = 0,
        EmissionEnabled = 0,
        EmissionEnergy = 0f,
        EmissionColor = Vector3.Zero,
    };
}

/// <summary>
/// C# mirror of the shared per-frame <c>SceneData</c> std140 block (set = 1,
/// binding = 0). Sun/moon fill light fields are read by the "lighting uniforms"
/// pass (simple.frag); shadow-related fields (LightSpaceMatrix, *CastsShadows)
/// exist so the struct's size/layout matches the GLSL block exactly, ready for
/// the "shadow passes" follow-up pass to start using them without a layout change.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceneDataUniforms
{
    public Matrix4x4 LightSpaceMatrix;
    public Vector3 Ambient;
    public float _pad0;
    public Vector3 LightDir;
    public float _pad1;
    public Vector3 LightColor;
    public float _pad2;
    public Vector3 SunFillLightDir;
    public float _pad3;
    public Vector3 SunFillLightColor;
    public float _pad4;
    public Vector3 MoonFillLightDir;
    public float _pad5;
    public Vector3 MoonFillLightColor;
    public float _pad6;
    public int ShadowDebugMode;
    public int MainLightCastsShadows;
    public int SunFillLightCastsShadows;
    public int MoonFillLightCastsShadows;
    public int UseShadowMap;
    public int _pad7;
    public int _pad8;
    public int _pad9;

    public static SceneDataUniforms Default => new()
    {
        LightSpaceMatrix = Matrix4x4.Identity,
        Ambient = new Vector3(0.35f, 0.35f, 0.35f),
        LightDir = Vector3.UnitY,
        LightColor = new Vector3(0.85f, 0.85f, 0.85f),
        SunFillLightDir = Vector3.UnitY,
        SunFillLightColor = new Vector3(1f, 0.9686f, 0.8941f),
        MoonFillLightDir = Vector3.UnitY,
        MoonFillLightColor = new Vector3(0.6f, 0.65f, 1f),
    };
}

/// <summary>
/// C# mirror of the <c>ShadowDepthUniforms</c> std140 block declared in
/// <c>assets/shaders/shadow_depth.vert</c>/<c>shadow_depth.frag</c>, used when
/// rendering a mesh into a <see cref="VeldridShadowMap"/> from a light's
/// point of view (depth-only, alpha-cutout tested against the mesh's texture).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ShadowDepthUniforms
{
    public Matrix4x4 MVP;
    public Vector2 TexOffset;
    public float TexScaleV;
    public float Alpha;
    public Vector2 TexUvOffset;
    public Vector2 TexUvRepeat;
    public Vector2 TexUvMirror;
    public int UseTexture;
    public int _pad0;

    public static ShadowDepthUniforms Default => new()
    {
        MVP = Matrix4x4.Identity,
        TexOffset = Vector2.Zero,
        TexScaleV = 1f,
        Alpha = 1f,
        TexUvOffset = Vector2.Zero,
        TexUvRepeat = Vector2.One,
        TexUvMirror = Vector2.Zero,
        UseTexture = 0,
    };
}

/// <summary>
/// C# counterpart of the <c>PointLightData</c> std140 block (set = 1, binding = 1)
/// declared in <c>simple.frag</c>. This is a deliberately simplified version of
/// the old <c>Mesh.PointLights</c> tuple list: only position, range, color, and
/// energy are carried - spot-cone direction/angles and per-light shadow-cubemap
/// index are added in the "shadow passes" follow-up pass, which is also where
/// they're actually consumed.
///
/// Deliberately NOT a single <c>[StructLayout]</c> struct blitted in one
/// <c>UpdateBuffer&lt;T&gt;</c> call: C# has no ergonomic way to embed a true
/// fixed-size <c>Vector4[32]</c> array inline in a struct that also round-trips
/// correctly through Veldrid's generic (<c>Unsafe.SizeOf&lt;T&gt;</c>-based) buffer
/// update path. Instead, <see cref="WriteTo"/> writes the header and each array
/// directly into the target buffer at the byte offsets matching the GLSL block's
/// std140 layout (count+padding: 16 bytes, then two 32×16-byte vec4 arrays).
/// </summary>
public sealed class PointLightUniforms
{
    public const int MaxPointLights = 32;

    /// <summary>Total buffer size in bytes required to hold this block: 16 (count+padding) + 32*16 (PosRange) + 32*16 (ColorEnergy).</summary>
    public const uint SizeInBytes = 16 + MaxPointLights * 16 + MaxPointLights * 16;

    public int Count { get; private set; }
    private readonly Vector4[] _posRange = new Vector4[MaxPointLights];
    private readonly Vector4[] _colorEnergy = new Vector4[MaxPointLights];

    public static readonly PointLightUniforms Empty = new();

    /// <summary>
    /// Repopulates this instance from up to <see cref="MaxPointLights"/> lights,
    /// each as (position, range, color, energy). Extra lights beyond the cap are
    /// silently dropped (matches the old renderer's fixed-size array behavior).
    /// Reuses the same backing arrays every call to avoid per-frame allocations.
    /// </summary>
    public void Set(IReadOnlyList<(Vector3 Position, float Range, Vector3 Color, float Energy)> lights)
    {
        Count = Math.Min(lights.Count, MaxPointLights);
        for (int i = 0; i < Count; i++)
        {
            var (position, range, color, energy) = lights[i];
            _posRange[i] = new Vector4(position, range);
            _colorEnergy[i] = new Vector4(color, energy);
        }
        for (int i = Count; i < MaxPointLights; i++)
        {
            _posRange[i] = Vector4.Zero;
            _colorEnergy[i] = Vector4.Zero;
        }
    }

    /// <summary>Writes this light set into <paramref name="buffer"/>, which must be
    /// at least <see cref="SizeInBytes"/> bytes (see <see cref="VeldridBitmapRenderSurface.PointLightBuffer"/>).</summary>
    public void WriteTo(Veldrid.GraphicsDevice device, Veldrid.DeviceBuffer buffer)
    {
        device.UpdateBuffer(buffer, 0, new[] { Count, 0, 0, 0 });
        device.UpdateBuffer(buffer, 16, _posRange);
        device.UpdateBuffer(buffer, 16 + MaxPointLights * 16u, _colorEnergy);
    }
}
