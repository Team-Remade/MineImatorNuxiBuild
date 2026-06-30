using System;
using MineImatorSimplyRemade.core;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// Loads the timeline transport-button SVG icons as OpenGL textures.
/// Call <see cref="Initialize"/> once the GL context is available.
/// </summary>
public static class TimelineIcons
{
    public static uint Play       { get; private set; }
    public static uint Pause      { get; private set; }
    public static uint Stop       { get; private set; }
    public static uint StepBack   { get; private set; }
    public static uint StepForward{ get; private set; }
    public static uint JumpStart  { get; private set; }
    public static uint JumpEnd    { get; private set; }
    public static uint AutoKey    { get; private set; }

    public static bool IsLoaded   { get; private set; }

    private const string Prefix = "MineImatorSimplyRemade.assets.img.button.";

    public static unsafe void Initialize(GL gl, int iconSize = 20)
    {
        if (IsLoaded) return;

        Play        = Load(gl, Prefix + "mdi--play.svg",              iconSize);
        Pause       = Load(gl, Prefix + "material-symbols--pause.svg",iconSize);
        Stop        = Load(gl, Prefix + "material-symbols--stop.svg", iconSize);
        StepBack    = Load(gl, Prefix + "mdi--step-backward.svg",     iconSize);
        StepForward = Load(gl, Prefix + "mdi--step-forward.svg",      iconSize);
        JumpStart   = Load(gl, Prefix + "vaadin--step-backward.svg",  iconSize);
        JumpEnd     = Load(gl, Prefix + "vaadin--step-forward.svg",   iconSize);
        AutoKey     = Load(gl, Prefix + "bi--dot.svg",                iconSize);

        IsLoaded = true;
    }

    private static unsafe uint Load(GL gl, string resourceName, int size)
    {
        SvgLoader.SvgImage img;
        try   { img = SvgLoader.LoadEmbedded(resourceName, size); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TimelineIcons] Failed to load {resourceName}: {ex.Message}");
            return 0;
        }

        // OpenGL expects row 0 at the bottom; flip the rows.
        int rowBytes = img.Width * 4;
        var flipped  = new byte[img.Data.Length];
        for (int row = 0; row < img.Height; row++)
        {
            int srcRow = img.Height - 1 - row;
            System.Buffer.BlockCopy(img.Data, srcRow * rowBytes, flipped, row * rowBytes, rowBytes);
        }

        uint tex = gl.GenTexture();
        gl.BindTexture(GLEnum.Texture2D, tex);

        fixed (byte* p = flipped)
        {
            gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8,
                (uint)img.Width, (uint)img.Height, 0,
                PixelFormat.Rgba, GLEnum.UnsignedByte, p);
        }

        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS,     (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT,     (int)TextureWrapMode.ClampToEdge);
        gl.BindTexture(GLEnum.Texture2D, 0);

        return tex;
    }
}
