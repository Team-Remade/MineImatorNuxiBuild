using System;
using System.Runtime.InteropServices;
using GlmSharp;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

[StructLayout(LayoutKind.Sequential)]
public struct SceneUniformData
{
    public mat4 LightSpaceMatrix;      // 64 bytes
    public vec3 Ambient;               // 12 bytes
    public float Pad0;                 // 4 bytes
    public vec3 LightDir;              // 12 bytes
    public float Pad1;                 // 4 bytes
    public vec3 LightColor;            // 12 bytes
    public float Pad2;                 // 4 bytes
    public vec3 SunFillLightDir;       // 12 bytes
    public float Pad3;                 // 4 bytes
    public vec3 SunFillLightColor;     // 12 bytes
    public float Pad4;                 // 4 bytes
    public vec3 MoonFillLightDir;      // 12 bytes
    public float Pad5;                 // 4 bytes
    public vec3 MoonFillLightColor;    // 12 bytes
    public float Pad6;                 // 4 bytes
    public int ShadowDebugMode;        // 4 bytes
    public int MainLightCastsShadows;  // 4 bytes
    public int SunFillLightCastsShadows;  // 4 bytes
    public int MoonFillLightCastsShadows; // 4 bytes
}

[StructLayout(LayoutKind.Sequential)]
public struct ModelUniformData
{
    public mat4 MVP;                   // 64 bytes
    public mat4 Model;                 // 64 bytes
    public vec2 TexOffset;             // 8 bytes
    public float TexScaleV;            // 4 bytes
    public float Pad0;                 // 4 bytes
    public vec2 TexUvOffset;           // 8 bytes
    public vec2 TexUvRepeat;           // 8 bytes
    public vec2 TexUvMirror;           // 8 bytes
    public uint IsSkinned;             // 4 bytes (bool as uint)
    public uint UseInstancing;         // 4 bytes (bool as uint)
}

public class VeldridSceneUniformBuffer : IDisposable
{
    public DeviceBuffer DeviceBuffer { get; }
    public ResourceLayout ResourceLayout { get; }
    public ResourceSet ResourceSet { get; }

    public VeldridSceneUniformBuffer(GraphicsDevice gd)
    {
        uint bufferSize = (uint)Marshal.SizeOf<SceneUniformData>();
        DeviceBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription(
            bufferSize,
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        ResourceLayout = gd.ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SceneData", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

        ResourceSet = gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            ResourceLayout,
            DeviceBuffer));
    }

    public unsafe void Update(
        GraphicsDevice gd,
        mat4 lightSpaceMatrix,
        vec3 ambient,
        vec3 lightDir,
        vec3 lightColor,
        vec3 sunFillLightDir,
        vec3 sunFillLightColor,
        vec3 moonFillLightDir,
        vec3 moonFillLightColor,
        int shadowDebugMode,
        bool mainLightCastsShadows,
        bool sunFillLightCastsShadows,
        bool moonFillLightCastsShadows)
    {
        var data = new SceneUniformData
        {
            LightSpaceMatrix = lightSpaceMatrix,
            Ambient = ambient,
            LightDir = lightDir,
            LightColor = lightColor,
            SunFillLightDir = sunFillLightDir,
            SunFillLightColor = sunFillLightColor,
            MoonFillLightDir = moonFillLightDir,
            MoonFillLightColor = moonFillLightColor,
            ShadowDebugMode = shadowDebugMode,
            MainLightCastsShadows = mainLightCastsShadows ? 1 : 0,
            SunFillLightCastsShadows = sunFillLightCastsShadows ? 1 : 0,
            MoonFillLightCastsShadows = moonFillLightCastsShadows ? 1 : 0
        };

        gd.UpdateBuffer(DeviceBuffer, 0, ref data);
    }

    public void Dispose()
    {
        ResourceSet.Dispose();
        ResourceLayout.Dispose();
        DeviceBuffer.Dispose();
    }
}
