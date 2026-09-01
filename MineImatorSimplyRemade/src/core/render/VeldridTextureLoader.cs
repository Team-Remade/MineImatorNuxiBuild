using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Shared RGBA texture upload helper, replacing the repeated
/// <c>GenTexture</c>/<c>BindTexture</c>/<c>TexImage2D</c>/<c>TexParameter</c>
/// GL call sequences that used to appear in every atlas/texture-loading file
/// (<c>TerrainAtlas</c>, <c>ItemsAtlas</c>, <c>CtmAtlas</c>, <c>TextMeshFactory</c>,
/// <c>TimelineIcons</c>, the model loaders, etc.).
/// </summary>
public static class VeldridTextureLoader
{
    /// <summary>
    /// Uploads raw RGBA8 pixel data to a new Veldrid <see cref="Texture"/> on
    /// the shared <see cref="VeldridContext.Device"/>.
    /// </summary>
    /// <param name="nearest">True for pixel-art style nearest-neighbor filtering
    /// (matches the old renderer's <c>GLEnum.Nearest</c> min/mag filter usage for
    /// block/item textures); false for linear filtering.</param>
    /// <param name="generateMipmaps">True to generate a full mip chain (matches
    /// the old renderer's <c>GenerateMipmap</c> calls for textures sampled at a
    /// distance); false for a single mip level (matches <c>TextureMipmaps=false</c>
    /// texture uses, e.g. UI icons or animated tiles where mip aliasing artifacts
    /// from partial-frame atlii would look wrong).</param>
    /// <param name="repeat">True to wrap/repeat (tileable textures); false to
    /// clamp to edge (UI icons, one-off images).</param>
    public static Texture UploadRgba(byte[] rgbaPixels, uint width, uint height, bool nearest = true,
        bool generateMipmaps = false, bool repeat = true, GraphicsDevice? device = null)
    {
        device ??= VeldridContext.Device;
        uint mipLevels = generateMipmaps ? MipLevelCount(width, height) : 1;

        Texture texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, mipLevels, 1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled | (generateMipmaps ? TextureUsage.GenerateMipmaps : 0)));

        device.UpdateTexture(texture, rgbaPixels, 0, 0, 0, width, height, 1, 0, 0);

        if (generateMipmaps && mipLevels > 1)
        {
            using CommandList commandList = device.ResourceFactory.CreateCommandList();
            commandList.Begin();
            commandList.GenerateMipmaps(texture);
            commandList.End();
            device.SubmitCommands(commandList);
            device.WaitForIdle();
        }

        return texture;
    }

    /// <summary>Builds a sampler matching the old GL min/mag/wrap parameters used
    /// alongside a texture uploaded by <see cref="UploadRgba"/>.</summary>
    public static Sampler CreateSampler(bool nearest, bool repeat, bool mipmaps, GraphicsDevice? device = null)
    {
        device ??= VeldridContext.Device;
        SamplerAddressMode address = repeat ? SamplerAddressMode.Wrap : SamplerAddressMode.Clamp;
        SamplerFilter filter = nearest
            ? SamplerFilter.MinPoint_MagPoint_MipPoint
            : mipmaps ? SamplerFilter.MinLinear_MagLinear_MipLinear : SamplerFilter.MinLinear_MagLinear_MipPoint;

        return device.ResourceFactory.CreateSampler(new SamplerDescription(
            address, address, address, filter, null, 0, 0, mipmaps ? uint.MaxValue : 0, 0, SamplerBorderColor.TransparentBlack));
    }

    private static uint MipLevelCount(uint width, uint height)
    {
        uint levels = 1;
        uint size = Math.Max(width, height);
        while (size > 1)
        {
            size /= 2;
            levels++;
        }
        return levels;
    }
}
