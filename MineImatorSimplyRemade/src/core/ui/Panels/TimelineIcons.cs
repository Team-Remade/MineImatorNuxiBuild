using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MineImatorSimplyRemade.core;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// Loads the timeline transport-button SVG icons as Avalonia bitmaps.
///
/// MIGRATION NOTE: these icons are pure 2D UI chrome for the (not yet ported)
/// Avalonia Timeline panel - unlike block/item textures, they never need to
/// touch the Veldrid 3D pipeline at all, so this now decodes straight to an
/// Avalonia <see cref="Bitmap"/> for use in an &lt;Image&gt;/IconButton control,
/// instead of uploading to a GL/Veldrid texture the way the old renderer did.
/// </summary>
public static class TimelineIcons
{
    public static Bitmap? Play { get; private set; }
    public static Bitmap? Pause { get; private set; }
    public static Bitmap? Stop { get; private set; }
    public static Bitmap? StepBack { get; private set; }
    public static Bitmap? StepForward { get; private set; }
    public static Bitmap? JumpStart { get; private set; }
    public static Bitmap? JumpEnd { get; private set; }
    public static Bitmap? AutoKey { get; private set; }
    public static Bitmap? Loop { get; private set; }
    public static Bitmap? Ghost { get; private set; }

    public static bool IsLoaded { get; private set; }

    private const string Prefix = "MineImatorSimplyRemade.assets.img.button.";

    public static void Initialize(int iconSize = 20)
    {
        if (IsLoaded) return;

        Play = Load(Prefix + "mdi--play.svg", iconSize);
        Pause = Load(Prefix + "material-symbols--pause.svg", iconSize);
        Stop = Load(Prefix + "material-symbols--stop.svg", iconSize);
        StepBack = Load(Prefix + "mdi--step-backward.svg", iconSize);
        StepForward = Load(Prefix + "mdi--step-forward.svg", iconSize);
        JumpStart = Load(Prefix + "vaadin--step-backward.svg", iconSize);
        JumpEnd = Load(Prefix + "vaadin--step-forward.svg", iconSize);
        AutoKey = Load(Prefix + "bi--dot.svg", iconSize);
        Loop = Load(Prefix + "ic--outline-loop.svg", iconSize);
        Ghost = Load(Prefix + "icon-park-solid--ghost.svg", iconSize);

        IsLoaded = true;
    }

    private static Bitmap? Load(string resourceName, int size)
    {
        SvgLoader.SvgImage img;
        try { img = SvgLoader.LoadEmbedded(resourceName, size); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load {resourceName}: {ex.Message}");
            return null;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(img.Width, img.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using (ILockedFramebuffer fb = bitmap.Lock())
        {
            System.Runtime.InteropServices.Marshal.Copy(img.Data, 0, fb.Address, img.Data.Length);
        }

        return bitmap;
    }
}
