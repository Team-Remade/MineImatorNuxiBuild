using System.Numerics;
using System.Runtime.InteropServices;

namespace MineImatorSimplyRemade.core.render;

/// <summary>C# mirror of <c>glow.frag</c>'s <c>GlowUniforms</c> block. <see cref="Mode"/>:
/// 0 = threshold-extract, 1 = separable blur (<see cref="Direction"/> selects axis), 2 = additive composite.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GlowUniforms
{
    public Vector2 TexelSize;
    public float Strength;
    public float Size;
    public Vector2 Direction;
    public int Mode;
    public int _pad0;
}

/// <summary>C# mirror of <c>film_grain.frag</c>'s <c>FilmGrainUniforms</c> block.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FilmGrainUniforms
{
    public Vector2 Resolution;
    public float Frame;
    public float Strength;
    public float Saturation;
    public float Size;
    public Vector2 _pad0;
}

/// <summary>C# mirror of <c>pick.vert</c>'s <c>PickUniforms</c> block (shared by the
/// pick and silhouette passes - same vertex shader, different fragment output).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PickVertexUniforms
{
    public Matrix4x4 MVP;
    public Vector2 TexOffset;
    public float TexScaleV;
    public int IsSkinned;
    public Vector2 TexUvOffset;
    public Vector2 TexUvRepeat;
    public Vector2 TexUvMirror;
    public Vector2 _pad1;

    public static PickVertexUniforms Default => new()
    {
        MVP = Matrix4x4.Identity,
        TexScaleV = 1f,
        IsSkinned = 0,
        TexUvRepeat = Vector2.One,
    };
}

/// <summary>C# mirror of <c>pick.frag</c>/<c>silhouette.frag</c>'s shared <c>PickMaterial</c> block.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PickMaterialUniforms
{
    public Vector3 PickColor;
    public float Alpha;
    public Vector4 BlendColor;
    public int UseTexture;
    public int UseAlphaMask;
    public int ForceOpaque;
    public int _pad0;

    public static PickMaterialUniforms Default => new()
    {
        PickColor = Vector3.Zero,
        Alpha = 1f,
        BlendColor = Vector4.One,
    };
}

/// <summary>C# mirror of <c>edge.frag</c>'s <c>EdgeUniforms</c> block.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct EdgeUniforms
{
    public Vector2 TexelSize;
    public Vector4 EdgeColor;
    public float Threshold;
    public Vector3 _pad0;

    public static EdgeUniforms Default => new()
    {
        EdgeColor = new Vector4(1f, 0.65f, 0f, 1f),
        Threshold = 0.4f,
    };
}

/// <summary>C# mirror of <c>gizmo.vert</c>/<c>gizmo.frag</c>'s shared <c>GizmoUniforms</c> block.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GizmoUniforms
{
    public Matrix4x4 MVP;
    public Vector4 Color;

    public static GizmoUniforms Default => new() { MVP = Matrix4x4.Identity, Color = Vector4.One };
}

/// <summary>C# mirror of <c>billboard.vert</c>/<c>billboard.frag</c>'s shared <c>BillboardUniforms</c> block.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BillboardUniforms
{
    public Matrix4x4 View;
    public Matrix4x4 Proj;
    public Vector3 WorldPos;
    public float Size;
    public Vector4 Tint;

    public static BillboardUniforms Default => new()
    {
        View = Matrix4x4.Identity,
        Proj = Matrix4x4.Identity,
        Size = 1f,
        Tint = Vector4.One,
    };
}

/// <summary>C# mirror of <c>lightring.vert</c>/<c>lightring.frag</c>'s shared <c>LightRingUniforms</c> block.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct LightRingUniforms
{
    public Matrix4x4 View;
    public Matrix4x4 Proj;
    public Vector3 WorldPos;
    public float Range;
    public Vector4 Color;

    public static LightRingUniforms Default => new()
    {
        View = Matrix4x4.Identity,
        Proj = Matrix4x4.Identity,
        Range = 1f,
        Color = Vector4.One,
    };
}
