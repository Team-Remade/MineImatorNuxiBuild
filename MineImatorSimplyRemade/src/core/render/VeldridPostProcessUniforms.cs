using System.Numerics;
using System.Runtime.InteropServices;

namespace MineImatorSimplyRemade.core.render;

/// <summary>C# mirror of <c>ambient_occlusion.frag</c>'s <c>AOUniforms</c> block.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct AmbientOcclusionUniforms
{
    public Vector2 TexelSize;
    public float Near;
    public float Far;
    public float Radius;
    public float Strength;
    public Vector2 _pad0;
    public Vector3 Color;
    public float Ratio;
    public float RatioBalance;
    public int SampleCount;
    public int OutputMode;
    public int _pad1;

    public static AmbientOcclusionUniforms Default => new()
    {
        Near = 0.1f,
        Far = 1000f,
        Radius = 6f,
        Strength = 1f,
        Color = Vector3.Zero,
        Ratio = 0.5f,
        RatioBalance = 0.5f,
        SampleCount = 16,
        OutputMode = 0,
    };
}

/// <summary>C# mirror of <c>indirect_lighting.frag</c>'s <c>IndirectUniforms</c> block.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct IndirectLightingUniforms
{
    public Vector2 TexelSize;
    public float Near;
    public float Far;
    public float Precision;
    public float RayStep;
    public int SampleCount;
    public int _pad0;
    public int _pad1;

    public static IndirectLightingUniforms Default => new()
    {
        Near = 0.1f,
        Far = 1000f,
        Precision = 0.5f,
        RayStep = 8f,
        SampleCount = 16,
    };
}

/// <summary>C# mirror of <c>indirect_denoise.frag</c>'s <c>DenoiseUniforms</c> block.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct IndirectDenoiseUniforms
{
    public Vector2 TexelSize;
    public float DenoiseStrength;
    public float Near;
    public float Far;
    public float _pad0;
    public float _pad1;
    public float _pad2;

    public static IndirectDenoiseUniforms Default => new()
    {
        DenoiseStrength = 40f,
        Near = 0.1f,
        Far = 1000f,
    };
}
