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
    public Vector3 SubsurfaceRadius;
    public float Subsurface;
    public Vector3 SubsurfaceColor;
    public float SubsurfaceHighlight;
    public float SubsurfaceHighlightStrength;
    public int IncludeInFog;
    public int _pad1;
    public int _pad2;

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
        SubsurfaceRadius = new Vector3(0.42f, 0.24f, 0.14f),
        Subsurface = 0f,
        SubsurfaceColor = Vector3.One,
        SubsurfaceHighlight = 0f,
        SubsurfaceHighlightStrength = 0f,
        IncludeInFog = 1,
    };
}

/// <summary>
/// C# mirror of the <c>SceneEnvironment</c> std140 block (set = 1, binding = 4)
/// declared in <c>simple.frag</c>: subsurface-scattering quality/multiplier
/// globals and distance/height fog. These used to be static fields on the old
/// GL <c>Mesh</c> class (<c>Mesh.FogEnabled</c>, <c>Mesh.SssRadius</c>, etc.);
/// now they're uploaded once per frame like <see cref="SceneDataUniforms"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceneEnvironmentUniforms
{
    public Vector3 CameraPosition;
    public float _pad0;
    public int FogEnabled;
    public int HeightFogEnabled;
    public int SSSEnabled;
    public int SSSBlurSamples;
    public Vector3 FogColor;
    public float FogDistance;
    public Vector3 HeightFogColor;
    public float FogFadeSize;
    public float FogHeight;
    public float HeightFogSize;
    public float HeightFogOffset;
    public float SSSStrength;
    public float SSSDesaturation;
    public float SSSColorThreshold;
    public float SSSHighlightSize;
    public float SSSGlobalHighlightStrength;
    public float SSSHighlightSharpness;
    public float SSSHighlightDesaturation;
    public float SSSHighlightColorThreshold;
    public float SSSAbsorption;
    public Vector3 SSSGlobalRadius;
    public float _pad1;

    public static SceneEnvironmentUniforms Default => new()
    {
        CameraPosition = Vector3.Zero,
        FogEnabled = 0,
        HeightFogEnabled = 0,
        SSSEnabled = 0,
        SSSBlurSamples = 8,
        FogColor = new Vector3(0.5764706f, 0.5764706f, 1f),
        FogDistance = 10000f,
        HeightFogColor = new Vector3(0.5764706f, 0.5764706f, 1f),
        FogFadeSize = 2000f,
        FogHeight = 1250f,
        HeightFogSize = 4000f,
        HeightFogOffset = -3850f,
        SSSStrength = 1f,
        SSSDesaturation = 0f,
        SSSColorThreshold = 0f,
        SSSHighlightSize = 1f,
        SSSGlobalHighlightStrength = 1f,
        SSSHighlightSharpness = 2f,
        SSSHighlightDesaturation = 0f,
        SSSHighlightColorThreshold = 0f,
        SSSAbsorption = 0.35f,
        SSSGlobalRadius = new Vector3(0.42f, 0.24f, 0.14f),
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
    public int IsSkinned;

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
        IsSkinned = 0,
    };
}

/// <summary>
/// C# mirror of the <c>PointShadowDepthUniforms</c> std140 block declared in
/// <c>assets/shaders/point_shadow_depth.vert</c>/<c>point_shadow_depth.frag</c>,
/// used when rendering a mesh into one face of a <see cref="VeldridPointShadowMap"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PointShadowDepthUniforms
{
    public Matrix4x4 LightViewProj;
    public Matrix4x4 Model;
    public Vector2 TexOffset;
    public float TexScaleV;
    public float Alpha;
    public Vector2 TexUvOffset;
    public Vector2 TexUvRepeat;
    public Vector2 TexUvMirror;
    public int UseTexture;
    public int IsSkinned;
    public Vector3 LightPos;
    public float FarPlane;

    public static PointShadowDepthUniforms Default => new()
    {
        LightViewProj = Matrix4x4.Identity,
        Model = Matrix4x4.Identity,
        TexOffset = Vector2.Zero,
        TexScaleV = 1f,
        Alpha = 1f,
        TexUvOffset = Vector2.Zero,
        TexUvRepeat = Vector2.One,
        TexUvMirror = Vector2.Zero,
        UseTexture = 0,
        IsSkinned = 0,
        LightPos = Vector3.Zero,
        FarPlane = 25f,
    };
}

/// <summary>
/// One point light's data for <see cref="PointLightUniforms.Set"/>. ShadowIndex
/// selects which of up to 8 <see cref="VeldridPointShadowMap"/> cubemaps this
/// light samples (-1 = unshadowed). SpotCosOuter/SpotCosInner of 0 disable the
/// spot cone entirely (full sphere, i.e. an ordinary omni point light) -
/// matches the old renderer's convention.
/// </summary>
public readonly record struct PointLightEntry(
    Vector3 Position,
    float Range,
    Vector3 Color,
    float Energy,
    Vector3 Direction = default,
    float FarPlane = 25f,
    float SpotCosOuter = 0f,
    float SpotCosInner = 0f,
    int ShadowIndex = -1);

/// <summary>
/// C# counterpart of the <c>PointLightData</c> std140 block (set = 1, binding = 1)
/// declared in <c>simple.frag</c>.
///
/// Deliberately NOT a single <c>[StructLayout]</c> struct blitted in one
/// <c>UpdateBuffer&lt;T&gt;</c> call: C# has no ergonomic way to embed a true
/// fixed-size <c>Vector4[32]</c> array inline in a struct that also round-trips
/// correctly through Veldrid's generic (<c>Unsafe.SizeOf&lt;T&gt;</c>-based) buffer
/// update path. Instead, <see cref="WriteTo"/> writes the header and each array
/// directly into the target buffer at the byte offsets matching the GLSL block's
/// std140 layout (count+padding: 16 bytes, then four 32×16-byte vec4 arrays).
/// </summary>
public sealed class PointLightUniforms
{
    public const int MaxPointLights = 32;

    /// <summary>Total buffer size in bytes: 16 (count+padding) + 4 * 32*16 (PosRange/ColorEnergy/DirFarPlane/SpotShadow).</summary>
    public const uint SizeInBytes = 16 + 4 * MaxPointLights * 16u;

    public int Count { get; private set; }
    private readonly Vector4[] _posRange = new Vector4[MaxPointLights];
    private readonly Vector4[] _colorEnergy = new Vector4[MaxPointLights];
    private readonly Vector4[] _dirFarPlane = new Vector4[MaxPointLights];
    private readonly Vector4[] _spotShadow = new Vector4[MaxPointLights];

    public static readonly PointLightUniforms Empty = new();

    /// <summary>
    /// Repopulates this instance from up to <see cref="MaxPointLights"/> lights.
    /// Extra lights beyond the cap are silently dropped (matches the old
    /// renderer's fixed-size array behavior). Reuses the same backing arrays
    /// every call to avoid per-frame allocations.
    /// </summary>
    public void Set(IReadOnlyList<PointLightEntry> lights)
    {
        Count = Math.Min(lights.Count, MaxPointLights);
        for (int i = 0; i < Count; i++)
        {
            PointLightEntry light = lights[i];
            _posRange[i] = new Vector4(light.Position, light.Range);
            _colorEnergy[i] = new Vector4(light.Color, light.Energy);
            _dirFarPlane[i] = new Vector4(light.Direction, light.FarPlane);
            _spotShadow[i] = new Vector4(light.SpotCosOuter, light.SpotCosInner, light.ShadowIndex, 0f);
        }
        for (int i = Count; i < MaxPointLights; i++)
        {
            _posRange[i] = Vector4.Zero;
            _colorEnergy[i] = Vector4.Zero;
            _dirFarPlane[i] = Vector4.Zero;
            _spotShadow[i] = new Vector4(0, 0, -1, 0);
        }
    }

    /// <summary>Convenience overload for simple omni (non-spot, unshadowed) lights.</summary>
    public void Set(IReadOnlyList<(Vector3 Position, float Range, Vector3 Color, float Energy)> lights)
    {
        var entries = new PointLightEntry[lights.Count];
        for (int i = 0; i < lights.Count; i++)
        {
            var (position, range, color, energy) = lights[i];
            entries[i] = new PointLightEntry(position, range, color, energy);
        }
        Set(entries);
    }

    /// <summary>Writes this light set into <paramref name="buffer"/>, which must be
    /// at least <see cref="SizeInBytes"/> bytes (see <see cref="VeldridBitmapRenderSurface.PointLightBuffer"/>).</summary>
    public void WriteTo(Veldrid.GraphicsDevice device, Veldrid.DeviceBuffer buffer)
    {
        const uint arrayBytes = MaxPointLights * 16u;
        device.UpdateBuffer(buffer, 0, new[] { Count, 0, 0, 0 });
        device.UpdateBuffer(buffer, 16, _posRange);
        device.UpdateBuffer(buffer, 16 + arrayBytes, _colorEnergy);
        device.UpdateBuffer(buffer, 16 + arrayBytes * 2, _dirFarPlane);
        device.UpdateBuffer(buffer, 16 + arrayBytes * 3, _spotShadow);
    }
}
