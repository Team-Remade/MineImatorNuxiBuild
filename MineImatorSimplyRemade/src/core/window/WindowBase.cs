using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.window;

/// <summary>
/// Shared base for every top-level application window. Replaces the old GLFW-backed
/// <c>Window</c> class: there is no more manual GL-context/ImGui-context lifecycle
/// here because Avalonia owns window creation, the render loop, and input dispatch.
/// What's left are the small helpers every window used (icon loading, embedded image
/// loading) re-implemented on top of Avalonia APIs.
///
/// Windows that need to display a live-rendered scene (Viewport, CameraWindow, render
/// previews) should own a <see cref="VeldridBitmapRenderSurface"/> and push its
/// <c>ReadBack()</c> bitmap into an &lt;Image&gt; control themselves rather than
/// relying on anything in this base class - unlike the old GLFW windows, each Avalonia
/// window is no longer itself "a GL context that gets cleared and swapped every frame".
/// </summary>
public abstract class WindowBase : Window
{
    /// <summary>Loads an embedded PNG resource from <c>assets/img/{resourceName}.png</c>.</summary>
    public static ImageResult LoadEmbeddedImageResult(string resourceName)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        using Stream? stream = assembly.GetManifestResourceStream($"MineImatorSimplyRemade.assets.img.{resourceName}.png");
        if (stream == null)
            throw new FileNotFoundException($"Embedded resource {resourceName} not found");

        return ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
    }

    /// <summary>Loads an embedded PNG resource as an Avalonia bitmap, e.g. for use as
    /// window icon source or in an &lt;Image&gt; control.</summary>
    public static Bitmap LoadEmbeddedBitmap(string resourceName)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        using Stream? stream = assembly.GetManifestResourceStream($"MineImatorSimplyRemade.assets.img.{resourceName}.png");
        if (stream == null)
            throw new FileNotFoundException($"Embedded resource {resourceName} not found");

        return new Bitmap(stream);
    }

    /// <summary>Sets this window's OS icon from an embedded PNG resource.</summary>
    protected void SetWindowIconFromEmbedded(string resourceName)
    {
        Icon = new WindowIcon(LoadEmbeddedBitmap(resourceName));
    }
}
