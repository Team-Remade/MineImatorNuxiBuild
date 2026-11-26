using GlmSharp;
using MineImatorSimplyRemade.core.diagnostics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemadeNuxi.core.objs;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

public sealed partial class VeldridSceneRenderer
{
    private const int NativePointLightCount = 32;
    private VeldridFullscreenPass? _nativeScenePass;
    private DeviceBuffer? _nativeSceneDataBuffer;
    private Texture? _nativeEmptyCube;
    private TextureView? _nativeEmptyCubeView;
    private readonly float[] _nativePointLightPositions = new float[NativePointLightCount * 3];
    private readonly float[] _nativePointLightColors = new float[NativePointLightCount * 3];
    private readonly float[] _nativePointLightRanges = new float[NativePointLightCount];
    private readonly float[] _nativePointLightEnergies = new float[NativePointLightCount];
    private readonly float[] _nativePointLightIndirectEnergies = new float[NativePointLightCount];
    private readonly float[] _nativePointLightSpeculars = new float[NativePointLightCount];
    private readonly int[] _nativePointLightRealisticFalloffs = new int[NativePointLightCount];
    private readonly int[] _nativePointLightShadowIndices = CreateEmptyShadowIndices();
    private readonly float[] _nativePointLightDirections = new float[NativePointLightCount * 3];
    private readonly float[] _nativePointLightOuterAngles = new float[NativePointLightCount];
    private readonly float[] _nativePointLightInnerAngles = new float[NativePointLightCount];

    private static int[] CreateEmptyShadowIndices()
    {
        var indices = new int[NativePointLightCount];
        Array.Fill(indices, -1);
        return indices;
    }

    private void InitializeNativeScenePass()
    {
        _nativeScenePass = new VeldridFullscreenPass(_device, "simple.frag", "simple.vert",
            VeldridVertex.LayoutDescription, disableShaderGraphs: true, remapClipZ: true);
        try
        {
            _nativeSceneDataBuffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<SceneUniformData>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _nativeEmptyCube = _device.ResourceFactory.CreateTexture(new TextureDescription(
                1, 1, 1, 1, 6, PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled | TextureUsage.Cubemap, TextureType.Texture2D));
            _nativeEmptyCubeView = _device.ResourceFactory.CreateTextureView(_nativeEmptyCube);
        }
        catch
        {
            _nativeEmptyCubeView?.Dispose();
            _nativeEmptyCube?.Dispose();
            _nativeSceneDataBuffer?.Dispose();
            _nativeScenePass.Dispose();
            _nativeScenePass = null;
            throw;
        }
    }

    private void UpdateNativeSceneData()
    {
        static vec3 ClampColor(vec3 color) => new(
            Math.Clamp(color.x, 0f, 1f), Math.Clamp(color.y, 0f, 1f), Math.Clamp(color.z, 0f, 1f));
        var data = new SceneUniformData
        {
            LightSpaceMatrix = Mesh.ShadowLightSpaceMatrix,
            Ambient = ClampColor(Mesh.GlobalAmbientColor) * Math.Clamp(Mesh.GlobalAmbientStrength, 0f, 5f),
            LightDir = new vec3(1f, 1f, 1f).Normalized,
            LightColor = ClampColor(Mesh.GlobalFillLightColor) * Math.Clamp(Mesh.GlobalFillLightStrength, 0f, 5f),
            SunFillLightDir = Mesh.GlobalSunFillLightDirection,
            SunFillLightColor = ClampColor(Mesh.GlobalSunFillLightColor) * Math.Clamp(Mesh.GlobalSunFillLightStrength, 0f, 5f),
            MoonFillLightDir = Mesh.GlobalMoonFillLightDirection,
            MoonFillLightColor = ClampColor(Mesh.GlobalMoonFillLightColor) * Math.Clamp(Mesh.GlobalMoonFillLightStrength, 0f, 5f),
            ShadowDebugMode = Mesh.ShadowDebugMode,
            MainLightCastsShadows = Mesh.MainFillLightCastsShadows ? 1 : 0,
            SunFillLightCastsShadows = Mesh.SunFillLightCastsShadows ? 1 : 0,
            MoonFillLightCastsShadows = Mesh.MoonFillLightCastsShadows ? 1 : 0
        };
        _device.UpdateBuffer(_nativeSceneDataBuffer!, 0, ref data);
    }

    private void DrawNativeScene(Mesh mesh, mat4 model, bool forceUnshaded)
    {
        bool cacheGeometry = CanCacheGeometry(mesh);
        VeldridMesh gpuMesh;
        if (cacheGeometry)
        {
            _seenStaticGeometry.Add(mesh);
            if (!_staticGeometry.TryGetValue(mesh, out var cached) || cached.Version != mesh.GeometryVersion)
            {
                cached.Mesh?.Dispose();
                gpuMesh = CreateGpuMesh(mesh, BuildVertices(mesh));
                _staticGeometry[mesh] = (mesh.GeometryVersion, gpuMesh);
            }
            else gpuMesh = cached.Mesh;
        }
        else
        {
            gpuMesh = CreateGpuMesh(mesh, BuildVertices(mesh));
            _frameResources.Add(gpuMesh);
        }

        bool hasUvs = mesh.TexCoords.Count == mesh.Vertices.Count;
        var texture = hasUvs ? ImportTexture(mesh.TextureId) : _white;
        var alphaMask = hasUvs ? ImportTexture(mesh.AlphaMaskTextureId) : _white;
        var normalTexture = hasUvs && mesh.NormalEnabled && mesh.NormalTexture != 0
            ? ImportTexture(mesh.NormalTexture) : _white;
        var surfaceTexture = hasUvs && mesh.PbrEnabled && mesh.SurfaceTexture != 0
            ? ImportTexture(mesh.SurfaceTexture) : _white;
        var emissionTexture = hasUvs && mesh.EmissionTexture != 0 ? ImportTexture(mesh.EmissionTexture) : _white;
        var glintTexture = hasUvs && mesh.GlintTexture != 0 ? ImportTexture(mesh.GlintTexture) : _white;
        float scaleV = 1f, offsetV = 0f;
        if (!string.IsNullOrEmpty(mesh.AnimationKey) &&
            TerrainAtlas.AnimatedTextures.TryGetValue(mesh.AnimationKey, out var animation) &&
            animation.Frames.Length > 0 && animation.TotalFrames > 0 && animation.FrameTime > 0)
        {
            double animationTime = Mesh.AnimatedTextureTimelineTimeSeconds is { } timelineTime
                ? timelineTime * Mesh.AnimatedTextureSpeedScale
                : _animationTime;
            scaleV = 1f / animation.TotalFrames;
            offsetV = Mesh.GetAnimatedTextureFrameIndex(animation, animationTime) * scaleV;
        }

        var pass = _nativeScenePass!;
        pass.ClearUniforms();
        pass.SetUniformBlock("SceneData", _nativeSceneDataBuffer!);
        pass.Set("uMVP", _viewProjection * model);
        pass.Set("uViewProj", _viewProjection);
        pass.Set("uModel", model);
        pass.Set("uTexOffset", new vec2(0, offsetV));
        pass.Set("uTexScaleV", scaleV);
        pass.Set("uTexUvOffset", mesh.TextureOffset);
        pass.Set("uTexUvRepeat", mesh.TextureRepeat);
        pass.Set("uTexUvMirror", new vec2(mesh.TextureMirror.x ? 1 : 0, mesh.TextureMirror.y ? 1 : 0));
        pass.Set("uIsSkinned", false);
        pass.Set("uUseInstancing", false);
        pass.Set("uBendEnabled", false);
        pass.Set("uBendHasSourceSegments", false);
        pass.Set("uWindEnabled", mesh.WindActive);
        if (mesh.WindActive)
        {
            pass.Set("uWindKind", mesh.WindKind);
            pass.Set("uWindParameters", Mesh.Wind.Parameters);
            pass.Set("uWindDirection", Mesh.Wind.Heading);
            pass.Set("uWindModel", model);
            pass.Set("uWindInverseModel", MathF.Abs(model.Determinant) > 1e-12f ? model.Inverse : mat4.Identity);
        }
        pass.Set("uAlbedo", mesh.RenderAlbedo);
        pass.Set("uBlendColor", mesh.BlendColor);
        pass.Set("uMixColor", mesh.MixColor);
        pass.Set("uMultiplyHsv", mesh.ColorAdjustments.MultiplyHsv);
        pass.Set("uAddRgb", mesh.ColorAdjustments.AddRgb);
        pass.Set("uAddHsv", mesh.ColorAdjustments.AddHsv);
        pass.Set("uSubtractRgb", mesh.ColorAdjustments.SubtractRgb);
        pass.Set("uSubtractHsv", mesh.ColorAdjustments.SubtractHsv);
        pass.Set("uAlpha", mesh.Alpha);
        pass.Set("uPremultiplyBlend", mesh.BlendMode is MaterialBlendMode.Multiply or MaterialBlendMode.Screen);
        pass.Set("uAlphaClipEnabled", mesh.AlphaMode is MaterialAlphaMode.Clip or MaterialAlphaMode.Hashed);
        pass.Set("uAlphaClipThreshold", mesh.AlphaClipThreshold);
        pass.Set("uAlphaHashed", mesh.AlphaMode == MaterialAlphaMode.Hashed);
        pass.Set("uBendSharp", false);
        pass.Set("uEmissionEnabled", mesh.EmissionEnabled);
        pass.Set("uEmissionColor", mesh.EmissionColor);
        pass.Set("uEmissionEnergy", mesh.EmissionEnergy);
        pass.Set("uEmissionIndirectOnly", mesh.EmissionIndirectOnly);
        pass.Set("uMetallic", mesh.Metallic);
        pass.Set("uRoughness", mesh.Roughness);
        pass.Set("uPbrEnabled", mesh.PbrEnabled);
        pass.Set("uNormalStrength", mesh.NormalStrength);
        pass.Set("uOcclusionStrength", mesh.OcclusionStrength);
        pass.Set("uHeightScale", mesh.HeightScale);
        pass.Set("uSSS", mesh.Subsurface);
        pass.Set("uSSSRadius", mesh.SubsurfaceRadius);
        pass.Set("uSSSColor", mesh.SubsurfaceColor);
        pass.Set("uSSSHighlight", mesh.SubsurfaceHighlight);
        pass.Set("uSSSHighlightStrength", mesh.SubsurfaceHighlightStrength);
        pass.Set("uSSSEnabled", Mesh.SssEnabled);
        pass.Set("uSSSBlurSamples", Mesh.SssBlurSamples);
        pass.Set("uSSSStrength", Mesh.SssStrength);
        pass.Set("uSSSDesaturation", Mesh.SssDesaturation);
        pass.Set("uSSSColorThreshold", Mesh.SssColorThreshold);
        pass.Set("uSSSGlobalRadius", Mesh.SssRadius);
        pass.Set("uSSSHighlightSize", Mesh.SssHighlightSize);
        pass.Set("uSSSGlobalHighlightStrength", Mesh.SssHighlightStrength);
        pass.Set("uSSSHighlightSharpness", Mesh.SssHighlightSharpness);
        pass.Set("uSSSHighlightDesaturation", Mesh.SssHighlightDesaturation);
        pass.Set("uSSSHighlightColorThreshold", Mesh.SssHighlightColorThreshold);
        pass.Set("uSSSAbsorption", Mesh.SssAbsorption);
        pass.Set("uUseShadowMap", false);
        pass.Set("uShadowBlurStrength", Mesh.ShadowBlurStrength);
        pass.Set("uIsUnlit", forceUnshaded || mesh.Unlit);
        pass.Set("uEndPortalSurface", mesh.EndPortalSurface);
        pass.Set("uEffectTime", Mesh.EffectTime);
        pass.Set("uUseEnchantmentGlint", mesh.GlintMode != MaterialGlintMode.None);
        pass.Set("uGlintScale", mesh.GlintScale);
        pass.Set("uGlintSpeed", mesh.GlintSpeed);
        pass.Set("uGlintStrength", mesh.GlintStrength);
        pass.Set("uCameraPosition", _cameraPosition);
        pass.Set("uFogColor", Mesh.FogColor);
        pass.Set("uHeightFogColor", Mesh.HeightFogColor);
        pass.Set("uFogEnabled", Mesh.FogEnabled && mesh.IncludeInFog);
        pass.Set("uHeightFogEnabled", Mesh.HeightFogEnabled);
        pass.Set("uFogDistance", Mesh.FogDistance);
        pass.Set("uFogFadeSize", Mesh.FogFadeSize);
        pass.Set("uFogHeight", Mesh.FogHeight);
        pass.Set("uHeightFogSize", Mesh.HeightFogSize);
        pass.Set("uHeightFogOffset", Mesh.HeightFogOffset);
        pass.Set("uUseTexture", mesh.TextureId != 0 && hasUvs);
        pass.Set("uUseNormalTexture", mesh.NormalEnabled && mesh.NormalTexture != 0 && hasUvs);
        pass.Set("uUseEmissionTexture", mesh.EmissionTexture != 0 && hasUvs);
        pass.Set("uUseSurfaceTexture", mesh.PbrEnabled && mesh.SurfaceTexture != 0 && hasUvs);
        pass.Set("uUseAlphaMask", mesh.AlphaMaskTextureId != 0 && hasUvs);
        pass.Set("uIsTextAlphaMask", mesh.IsTextAlphaMask);
        pass.Set("uTextOutlineColor", mesh.TextMaskOutlineColor);

        const int pointCount = NativePointLightCount;
        int lightCount = forceUnshaded || mesh.Unlit ? 0 : Math.Min(Mesh.PointLights.Count, pointCount);
        for (int i = 0; i < lightCount; i++)
        {
            var light = Mesh.PointLights[i];
            _nativePointLightPositions[i * 3] = light.pos.x; _nativePointLightPositions[i * 3 + 1] = light.pos.y; _nativePointLightPositions[i * 3 + 2] = light.pos.z;
            _nativePointLightColors[i * 3] = light.color.x; _nativePointLightColors[i * 3 + 1] = light.color.y; _nativePointLightColors[i * 3 + 2] = light.color.z;
            _nativePointLightRanges[i] = light.range; _nativePointLightEnergies[i] = light.energy;
            _nativePointLightIndirectEnergies[i] = light.indirectEnergy; _nativePointLightSpeculars[i] = light.specular;
            _nativePointLightRealisticFalloffs[i] = light.realisticFalloff ? 1 : 0;
            _nativePointLightDirections[i * 3] = light.direction.x; _nativePointLightDirections[i * 3 + 1] = light.direction.y; _nativePointLightDirections[i * 3 + 2] = light.direction.z;
            _nativePointLightOuterAngles[i] = light.spotCosOuterAngle; _nativePointLightInnerAngles[i] = light.spotCosInnerAngle;
        }
        pass.Set("uPointLightCount", lightCount);
        pass.SetFloats("uPointLightPos", _nativePointLightPositions, "vec3");
        pass.SetFloats("uPointLightColor", _nativePointLightColors, "vec3");
        pass.SetFloats("uPointLightRange", _nativePointLightRanges, "float");
        pass.SetFloats("uPointLightEnergy", _nativePointLightEnergies, "float");
        pass.SetFloats("uPointLightIndirectEnergy", _nativePointLightIndirectEnergies, "float");
        pass.SetFloats("uPointLightSpecular", _nativePointLightSpeculars, "float");
        pass.SetInts("uPointLightRealisticFalloff", _nativePointLightRealisticFalloffs);
        pass.SetInts("uPointLightShadowIndex", _nativePointLightShadowIndices);
        pass.SetFloats("uPointLightDir", _nativePointLightDirections, "vec3");
        pass.SetFloats("uPointLightSpotCosOuter", _nativePointLightOuterAngles, "float");
        pass.SetFloats("uPointLightSpotCosInner", _nativePointLightInnerAngles, "float");
        pass.Set("uIndirectWorldEnabled", Mesh.IndirectWorldEnabled && !(forceUnshaded || mesh.Unlit));
        pass.Set("uIndirectWorldStrength", Mesh.IndirectWorldStrength);
        pass.Set("uShaderGraphSceneColorAvailable", false);
        pass.Set("uFogVolumeRangePass", false);
        pass.Set("uFogVolumeMarch", false);
        pass.Set("uFogInvertFaces", false);
        pass.Set("uFogOrigin", vec2.Zero);
        pass.Set("uFogInverseViewProj", mat4.Identity);

        pass.SetTexture("uTexture", texture.TextureView, mesh.BlurTexture ? _linearSampler : _pointSampler);
        pass.SetTexture("uNormalTexture", normalTexture.TextureView, _linearSampler);
        pass.SetTexture("uEmissionTexture", emissionTexture.TextureView, _linearSampler);
        pass.SetTexture("uSurfaceTexture", surfaceTexture.TextureView, _linearSampler);
        pass.SetTexture("uAlphaMask", alphaMask.TextureView, mesh.BlurTexture ? _linearSampler : _pointSampler);
        pass.SetTexture("uGlintTexture", glintTexture.TextureView, _linearSampler);
        pass.SetTexture("uShadowMap", _white.TextureView, _pointSampler);
        pass.SetTexture("uFogRange", _white.TextureView, _pointSampler);
        pass.SetTexture("uFogSceneDepth", _white.TextureView, _pointSampler);
        for (int i = 0; i < 8; i++)
            pass.SetTexture("uPointShadowMaps", i, _nativeEmptyCubeView!, _linearSampler);

        FaceCullMode cull = mesh.DoubleSided ? FaceCullMode.None : mesh.CullFrontFaces ? FaceCullMode.Front : FaceCullMode.Back;
        bool alphaClip = mesh.AlphaMode is MaterialAlphaMode.Clip or MaterialAlphaMode.Hashed;
        var blend = alphaClip || !mesh.RequiresTranslucentPass
            ? VeldridFullscreenPass.BlendMode.Opaque
            : mesh.BlendMode switch
            {
                MaterialBlendMode.Add => VeldridFullscreenPass.BlendMode.AdditiveAlpha,
                MaterialBlendMode.Subtract => VeldridFullscreenPass.BlendMode.Subtract,
                MaterialBlendMode.Multiply => VeldridFullscreenPass.BlendMode.Multiply,
                MaterialBlendMode.Screen => VeldridFullscreenPass.BlendMode.Screen,
                _ => VeldridFullscreenPass.BlendMode.Alpha
            };
        pass.Draw(_commands, _target!.Framebuffer, blend, gpuMesh.VertexBuffer, gpuMesh.VertexCount,
            depthTest: !mesh.DepthTestDisabled,
            depthWrite: !mesh.DepthTestDisabled && !mesh.DepthWriteDisabled && !mesh.RequiresTranslucentPass,
            cull: cull, fill: _wireframe ? PolygonFillMode.Wireframe : PolygonFillMode.Solid,
            writeAuxiliaryAttachments: !mesh.RequiresTranslucentPass, issueDraw: false);
        if (gpuMesh.IndexBuffer != null)
        {
            _commands.SetIndexBuffer(gpuMesh.IndexBuffer, IndexFormat.UInt32);
            _commands.DrawIndexed(gpuMesh.IndexCount, 1, 0, 0, 0);
        }
        else
            _commands.Draw(gpuMesh.VertexCount, 1, 0, 0);
        FrameProfiler.Shared.Count(ProfileCounter.PointLightsUploaded, lightCount);
        _hasDraws = true;
    }

    private void DisposeNativeScenePass()
    {
        _nativeScenePass?.Dispose();
        _nativeScenePass = null;
        _nativeEmptyCubeView?.Dispose();
        _nativeEmptyCubeView = null;
        _nativeEmptyCube?.Dispose();
        _nativeEmptyCube = null;
        _nativeSceneDataBuffer?.Dispose();
        _nativeSceneDataBuffer = null;
    }
}