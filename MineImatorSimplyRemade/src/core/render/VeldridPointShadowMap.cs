using System.Numerics;
using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// One point light's cube shadow map: 6 faces, each storing the normalized
/// light-to-fragment distance (not a real depth buffer - see the migration
/// note in <c>point_shadow_depth.frag</c> for why this uses a plain R32_Float
/// color cube texture instead of depth-format cube attachments).
///
/// MIGRATION STATUS (subsystem pass 3b/N - "point-light shadow cubemaps"):
/// caller-owned, like <see cref="VeldridShadowMap"/> - create one per
/// shadow-casting point light (up to <see cref="PointLightUniforms.MaxPointLights"/>,
/// though in practice only a handful of lights should cast shadows at once for
/// performance, matching the old renderer's <c>MaxPointShadowLights</c> cap of 8).
/// </summary>
public sealed class VeldridPointShadowMap : IDisposable
{
    private readonly GraphicsDevice _device;

    public Texture ColorCubeTexture { get; private set; } = null!;
    public TextureView CubeTextureView { get; private set; } = null!;
    public Sampler CubeSampler { get; private set; }
    public Framebuffer[] FaceFramebuffers { get; private set; } = new Framebuffer[6];

    private Texture _sharedDepthTarget = null!;
    public uint Size { get; private set; }

    /// <summary>Farthest distance this light's shadow can represent - matches the
    /// value baked into <c>point_shadow_depth.frag</c>'s stored distance and must
    /// be passed back into <c>PointLightUniforms</c>/the sampling side consistently.</summary>
    public float FarPlane { get; set; } = 50f;

    // Standard 6-direction cube face basis (+X, -X, +Y, -Y, +Z, -Z), matching
    // the conventional cubemap face order used by both GL and Veldrid.
    private static readonly (Vector3 Forward, Vector3 Up)[] FaceBases =
    [
        (Vector3.UnitX, -Vector3.UnitY),
        (-Vector3.UnitX, -Vector3.UnitY),
        (Vector3.UnitY, Vector3.UnitZ),
        (-Vector3.UnitY, -Vector3.UnitZ),
        (Vector3.UnitZ, -Vector3.UnitY),
        (-Vector3.UnitZ, -Vector3.UnitY),
    ];

    public VeldridPointShadowMap(GraphicsDevice device, uint size = 512)
    {
        _device = device;
        CubeSampler = device.ResourceFactory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear, null, 0, 0, 0, 0, SamplerBorderColor.OpaqueWhite));
        Resize(size);
    }

    public void Resize(uint size)
    {
        size = Math.Max(1, size);
        if (size == Size && ColorCubeTexture != null)
            return;

        Size = size;

        foreach (Framebuffer? fb in FaceFramebuffers)
            fb?.Dispose();
        CubeTextureView?.Dispose();
        ColorCubeTexture?.Dispose();
        _sharedDepthTarget?.Dispose();

        // Cubemap textures in Veldrid are represented as a 2D texture with the
        // Cubemap usage flag and exactly 6 array layers (one per face) - NOT
        // ArrayLayers=1 the way a "single" non-array resource normally would be.
        ColorCubeTexture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 6, PixelFormat.R32_Float,
            TextureUsage.RenderTarget | TextureUsage.Sampled | TextureUsage.Cubemap));

        _sharedDepthTarget = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.D24_UNorm_S8_UInt, TextureUsage.DepthStencil));

        for (int face = 0; face < 6; face++)
        {
            FaceFramebuffers[face] = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(
                new FramebufferAttachmentDescription(_sharedDepthTarget, 0),
                new[] { new FramebufferAttachmentDescription(ColorCubeTexture, (uint)face) }));
        }

        CubeTextureView = _device.ResourceFactory.CreateTextureView(ColorCubeTexture);
    }

    /// <summary>View*projection matrix for one of the 6 cube faces, looking out
    /// from <paramref name="lightPos"/> with a 90-degree FOV (required so the 6
    /// faces exactly tile the full sphere around the light).</summary>
    public Matrix4x4 GetFaceViewProjection(int face, Vector3 lightPos, float nearPlane = 0.05f)
    {
        var (forward, up) = FaceBases[face];
        Matrix4x4 view = Matrix4x4.CreateLookAt(lightPos, lightPos + forward, up);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, nearPlane, FarPlane);
        return view * proj;
    }

    /// <summary>Renders one face's depth-caster pass. Call 6 times (face 0..5) per frame.</summary>
    public void RenderFace(int face, Action<CommandList> recordDrawCalls)
    {
        using CommandList commandList = _device.ResourceFactory.CreateCommandList();
        commandList.Begin();
        commandList.SetFramebuffer(FaceFramebuffers[face]);
        commandList.ClearColorTarget(0, new RgbaFloat(1f, 0f, 0f, 1f)); // 1.0 = "infinitely far" default
        commandList.ClearDepthStencil(1f);

        recordDrawCalls(commandList);

        commandList.End();
        _device.SubmitCommands(commandList);
        _device.WaitForIdle();
    }

    public void Dispose()
    {
        foreach (Framebuffer? fb in FaceFramebuffers)
            fb?.Dispose();
        CubeTextureView?.Dispose();
        ColorCubeTexture?.Dispose();
        _sharedDepthTarget?.Dispose();
        CubeSampler?.Dispose();
    }
}
