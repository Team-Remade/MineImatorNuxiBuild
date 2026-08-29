using System.Numerics;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Directional shadow map: a depth-only render target holding the scene as seen
/// from the main light's point of view, sampled back in <c>simple.frag</c> via
/// PCF (see <c>calculateShadow</c>) to darken surfaces the light can't reach.
///
/// MIGRATION STATUS (subsystem pass 3/N - "shadow passes: directional map"):
/// this ports the directional shadow map only. Point-light shadow cubemaps
/// (8x, one per shadow-casting point light) and spot-light cones are a
/// separate follow-up pass - <c>PointLightData</c> in simple.frag currently has
/// no shadow-index field at all, so point lights always render fully lit.
///
/// Scene-bounds fitting (the old renderer's <c>CollectShadowBounds</c>, which
/// grew the orthographic frustum to exactly cover all shadow-casting geometry)
/// is also not ported - <see cref="ComputeLightSpaceMatrix"/> takes an explicit
/// extent/near/far instead. Callers (eventually Viewport) should compute a
/// reasonable extent from the scene's actual bounding box once that's ported.
/// </summary>
public sealed class VeldridShadowMap : IDisposable
{
    private readonly GraphicsDevice _device;

    public Texture DepthTexture { get; private set; } = null!;
    public TextureView DepthTextureView { get; private set; } = null!;
    public Sampler DepthSampler { get; private set; } = null!;
    public Framebuffer Framebuffer { get; private set; } = null!;

    public uint Size { get; private set; }

    public VeldridShadowMap(GraphicsDevice device, uint size = 2048)
    {
        _device = device;
        DepthSampler = device.ResourceFactory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear, null, 0, 0, 0, 0, SamplerBorderColor.OpaqueWhite));
        Resize(size);
    }

    public void Resize(uint size)
    {
        size = Math.Max(1, size);
        if (size == Size && Framebuffer != null)
            return;

        Size = size;

        DepthTextureView?.Dispose();
        Framebuffer?.Dispose();
        DepthTexture?.Dispose();

        DepthTexture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1,
            PixelFormat.R32_Float,
            TextureUsage.DepthStencil | TextureUsage.Sampled));

        Framebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(DepthTexture, Array.Empty<Texture>()));
        DepthTextureView = _device.ResourceFactory.CreateTextureView(DepthTexture);
    }

    /// <summary>
    /// Convenience wrapper mirroring <c>VeldridBitmapRenderSurface.RenderFrame</c>:
    /// begins a command list, binds+clears this shadow map's framebuffer (depth
    /// only, no color), lets <paramref name="recordDrawCalls"/> record
    /// <c>VeldridMesh.RenderDepthOnly(...)</c> calls for every shadow caster, then submits.
    /// </summary>
    public void RenderShadowPass(Action<CommandList> recordDrawCalls)
    {
        using CommandList commandList = _device.ResourceFactory.CreateCommandList();
        commandList.Begin();
        commandList.SetFramebuffer(Framebuffer);
        commandList.ClearDepthStencil(1f);

        recordDrawCalls(commandList);

        commandList.End();
        _device.SubmitCommands(commandList);
        _device.WaitForIdle();
    }

    /// <summary>
    /// Builds a light-space view*projection matrix for an orthographic
    /// directional light, looking at <paramref name="sceneCenter"/> from along
    /// <paramref name="lightDir"/> (the direction the light travels, i.e.
    /// surfaces are lit from <c>-lightDir</c>), covering a
    /// <paramref name="extent"/>-sized square area.
    /// </summary>
    public static Matrix4x4 ComputeLightSpaceMatrix(Vector3 lightDir, Vector3 sceneCenter, float extent, float near = 0.1f, float far = 500f)
    {
        Vector3 direction = lightDir.LengthSquared() > 1e-6f ? Vector3.Normalize(lightDir) : Vector3.UnitY;
        Vector3 eye = sceneCenter - direction * (far * 0.5f);
        Vector3 up = MathF.Abs(direction.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, sceneCenter, up);
        Matrix4x4 proj = Matrix4x4.CreateOrthographic(extent, extent, near, far);
        return view * proj;
    }

    public void Dispose()
    {
        DepthTextureView?.Dispose();
        Framebuffer?.Dispose();
        DepthTexture?.Dispose();
        DepthSampler?.Dispose();
    }
}
