using System;
using System.IO;
using System.Runtime.InteropServices;
using StbImageSharp;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

public class VeldridTexture : IDisposable
{
    public Texture DeviceTexture { get; }
    public TextureView TextureView { get; }
    public ResourceSet? ResourceSet { get; private set; }
    public uint Width => DeviceTexture.Width;
    public uint Height => DeviceTexture.Height;

    public VeldridTexture(GraphicsDevice gd, Texture texture)
    {
        DeviceTexture = texture;
        TextureView = gd.ResourceFactory.CreateTextureView(texture);
    }

    public static VeldridTexture CreateFromRgbaBytes(
        GraphicsDevice gd,
        byte[] rgbaBytes,
        uint width,
        uint height,
        bool mipmaps = false)
    {
        uint mipLevels = 1;
        if (mipmaps)
        {
            mipLevels = (uint)Math.Floor(Math.Log2(Math.Max(width, height))) + 1;
        }

        var texDesc = TextureDescription.Texture2D(
            width,
            height,
            mipLevels,
            1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled);

        var texture = gd.ResourceFactory.CreateTexture(texDesc);

        unsafe
        {
            fixed (byte* p = rgbaBytes)
            {
                gd.UpdateTexture(
                    texture,
                    (IntPtr)p,
                    (uint)rgbaBytes.Length,
                    0, 0, 0,
                    width, height, 1,
                    0, 0);
            }
        }

        return new VeldridTexture(gd, texture);
    }

    public static VeldridTexture LoadFromStream(GraphicsDevice gd, Stream stream, bool flipVertically = false)
    {
        StbImage.stbi_set_flip_vertically_on_load(flipVertically ? 1 : 0);
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return CreateFromRgbaBytes(gd, image.Data, (uint)image.Width, (uint)image.Height);
    }

    public static VeldridTexture LoadFromFile(GraphicsDevice gd, string filePath, bool flipVertically = false)
    {
        using var stream = File.OpenRead(filePath);
        return LoadFromStream(gd, stream, flipVertically);
    }

    public void CreateResourceSet(GraphicsDevice gd, ResourceLayout layout, Sampler sampler)
    {
        ResourceSet?.Dispose();
        ResourceSet = gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            layout,
            TextureView,
            sampler));
    }

    public static Sampler CreateLinearSampler(GraphicsDevice gd)
    {
        return gd.ResourceFactory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Wrap,
            SamplerAddressMode.Wrap,
            SamplerAddressMode.Wrap,
            SamplerFilter.MinLinear_MagLinear_MipLinear,
            null,
            0,
            0,
            16,
            0,
            SamplerBorderColor.TransparentBlack));
    }

    public static Sampler CreatePointSampler(GraphicsDevice gd)
    {
        return gd.ResourceFactory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinPoint_MagPoint_MipPoint,
            null,
            0,
            0,
            1,
            0,
            SamplerBorderColor.TransparentBlack));
    }

    public void Dispose()
    {
        ResourceSet?.Dispose();
        TextureView.Dispose();
        DeviceTexture.Dispose();
    }
}
