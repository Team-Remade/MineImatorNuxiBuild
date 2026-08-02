using System;
using System.Collections.Generic;
using GlmSharp;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl;

/// <summary>
/// Manages a single shared GL uniform buffer object (UBO) for scene-constant
/// lighting data.  Uploaded once per frame by the Viewport and shared by all
/// meshes, eliminating ~12 per-mesh <c>glUniform</c> calls.
///
/// All colour/light values are pre-baked on the CPU side (strength multipliers
/// baked in) before upload, so the shader reads them directly as-is — matching
/// the original per-mesh uniform behaviour exactly.
///
/// std140 layout (size = 320 bytes):
///   mat4  uView                  [  0.. 63]
///   mat4  uProj                  [ 64..127]
///   mat4  uLightSpaceMatrix      [128..191]
///   vec3  uAmbient               [192..203]
///   float _pad0                  [204..207]
///   vec3  uLightDir              [208..219]
///   float _pad1                  [220..223]
///   vec3  uLightColor            [224..235]
///   float _pad2                  [236..239]
///   vec3  uSunFillLightDir       [240..251]
///   float _pad3                  [252..255]
///   vec3  uSunFillLightColor     [256..267]
///   float _pad4                  [268..271]
///   vec3  uMoonFillLightDir      [272..283]
///   float _pad5                  [284..287]
///   vec3  uMoonFillLightColor    [288..299]
///   float _pad6                  [300..303]
///   int   uShadowDebugMode       [304..307]
///   int   uMainLightCastsShadows [308..311]
///   int   uSunFillLightCastsShadows [312..315]
///   int   uMoonFillLightCastsShadows [316..319]
/// </summary>
public sealed class SceneUniformBuffer : IDisposable
{
    private readonly GL _gl;
    private uint _ubo;
    private bool _initialized;
    private const uint BindingPoint = 0;

    private readonly byte[] _data = new byte[192];
    private int _lastHash;
    private readonly HashSet<uint> _linkedPrograms = new();

    public SceneUniformBuffer(GL gl) { _gl = gl; }

    public void EnsureInitialized(uint shaderProgram)
    {
        if (_initialized)
        {
            if (!_linkedPrograms.Contains(shaderProgram))
                BindToShader(shaderProgram);
            return;
        }

        unsafe
        {
            _gl.GenBuffers(1, out _ubo);
            _gl.BindBuffer(GLEnum.UniformBuffer, _ubo);
            _gl.BufferData(GLEnum.UniformBuffer, (uint)_data.Length, (void*)0, GLEnum.DynamicDraw);
            // Upload the latest cached scene data immediately so the first draw
            // does not sample uninitialized/zeroed UBO contents.
            fixed (byte* p = _data)
                _gl.BufferSubData(GLEnum.UniformBuffer, 0, (uint)_data.Length, p);
            _gl.BindBuffer(GLEnum.UniformBuffer, 0);
        }

        _lastHash = HashData();

        BindToShader(shaderProgram);
        _initialized = true;
    }

    public void BindToShader(uint shaderProgram)
    {
        if (_ubo == 0) return;
        uint idx = _gl.GetUniformBlockIndex(shaderProgram, "SceneData");
        if (idx == uint.MaxValue) return;
        _gl.UniformBlockBinding(shaderProgram, idx, BindingPoint);
        _gl.BindBufferBase(GLEnum.UniformBuffer, BindingPoint, _ubo);
        _linkedPrograms.Add(shaderProgram);
    }

    /// <summary>
    /// Upload all scene-constant lighting data.  Values should already be
    /// pre-baked (strength multipliers applied) — matching what the shader
    /// expects as-is.
    /// </summary>
    public unsafe void Upload(
        mat4 lightSpaceMatrix,
        vec3 ambient,               // already * ambientStrength
        vec3 lightDir,
        vec3 lightColor,            // already * lightIntensity
        vec3 sunFillLightDir,
        vec3 sunFillLightColor,     // already * sunFillStrength
        vec3 moonFillLightDir,
        vec3 moonFillLightColor,    // already * moonFillStrength
        int   shadowDebugMode,
        bool  mainLightCastsShadows,
        bool  sunFillLightCastsShadows,
        bool  moonFillLightCastsShadows)
    {
        WriteMat4(0,   lightSpaceMatrix);
        WriteVec3(64,  ambient);
        WriteVec3(80,  lightDir);
        WriteVec3(96,  lightColor);
        WriteVec3(112, sunFillLightDir);
        WriteVec3(128, sunFillLightColor);
        WriteVec3(144, moonFillLightDir);
        WriteVec3(160, moonFillLightColor);
        WriteInt(176, shadowDebugMode);
        WriteInt(180, mainLightCastsShadows ? 1 : 0);
        WriteInt(184, sunFillLightCastsShadows ? 1 : 0);
        WriteInt(188, moonFillLightCastsShadows ? 1 : 0);

        int hash = HashData();
        if (hash == _lastHash) return;
        _lastHash = hash;

        fixed (byte* p = _data)
        {
            _gl.BindBuffer(GLEnum.UniformBuffer, _ubo);
            _gl.BufferSubData(GLEnum.UniformBuffer, 0, (uint)_data.Length, p);
            _gl.BindBuffer(GLEnum.UniformBuffer, 0);
        }
    }

    private void WriteFloat(int o, float v) { System.Buffer.BlockCopy(BitConverter.GetBytes(v), 0, _data, o, 4); }
    private void WriteInt(int o, int v)     { System.Buffer.BlockCopy(BitConverter.GetBytes(v), 0, _data, o, 4); }
    private void WriteVec3(int o, vec3 v)   { WriteFloat(o, v.x); WriteFloat(o + 4, v.y); WriteFloat(o + 8, v.z); }

    private unsafe void WriteMat4(int o, mat4 m)
    {
        float[] f =
        {
            m.m00, m.m01, m.m02, m.m03,
            m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23,
            m.m30, m.m31, m.m32, m.m33,
        };
        System.Buffer.BlockCopy(f, 0, _data, o, 64);
    }

    private int HashData()
    {
        int h = -2128831035;
        foreach (byte b in _data) { h ^= b; h *= 16777619; }
        return h;
    }

    public void Dispose()
    {
        if (_ubo != 0) _gl.DeleteBuffers(1, _ubo);
    }
}
