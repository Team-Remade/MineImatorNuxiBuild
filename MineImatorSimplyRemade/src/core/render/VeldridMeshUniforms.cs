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
/// C# mirror of the shared per-frame <c>SceneData</c> std140 block. Only the
/// fields actually read by the reduced simple.frag (this migration pass) are
/// meaningful right now (uAmbient, uLightDir, uLightColor); the rest exist so
/// the struct's total size/layout matches the GLSL block exactly, ready for the
/// "lighting uniforms" follow-up pass to start using them without a layout change.
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
