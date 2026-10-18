using System;
using MineImatorSimplyRemade.core;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

internal static class ContentBrowserIcons
{
    public static uint Folder { get; private set; }
    public static uint Model { get; private set; }
    public static uint Image { get; private set; }
    public static uint Sound { get; private set; }
    public static uint File { get; private set; }

    private const string Prefix = "MineImatorSimplyRemade.assets.img.content.";

    public static void Initialize(GL gl, int iconSize = 32)
    {
        if (Folder != 0)
            return;

        Folder = Load(gl, Prefix + "mdi--folder.svg", iconSize);
        Model = Load(gl, Prefix + "mdi--cube-outline.svg", iconSize);
        Image = Load(gl, Prefix + "mdi--image.svg", iconSize);
        Sound = Load(gl, Prefix + "mdi--music-note.svg", iconSize);
        File = Load(gl, Prefix + "mdi--file.svg", iconSize);
    }

    private static unsafe uint Load(GL gl, string resourceName, int size)
    {
        try
        {
            SvgLoader.SvgImage image = SvgLoader.LoadEmbedded(resourceName, size);
            uint texture = gl.GenTexture();
            gl.BindTexture(GLEnum.Texture2D, texture);
            fixed (byte* pixels = image.Data)
            {
                gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8,
                    (uint)image.Width, (uint)image.Height, 0,
                    PixelFormat.Rgba, GLEnum.UnsignedByte, pixels);
            }

            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            gl.BindTexture(GLEnum.Texture2D, 0);
            return texture;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load content-browser icon '{resourceName}': {ex.Message}");
            return 0;
        }
    }
}
