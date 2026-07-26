using System.Runtime.InteropServices;

namespace MineImatorSimplyRemade.core.log;

/// <summary>
/// Shows a blocking, OS-native "something went wrong" dialog without shelling out to any
/// external process/command - each platform is handled by calling directly into its own
/// native OS library (P/Invoke), the same mechanism used elsewhere in this project
/// (see <see cref="startup.NativeLibraryBootstrap"/>).
/// </summary>
public static class NativeMessageBox
{
    public static void Show(string title, string message)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                ShowWindows(title, message);
            }
            else if (OperatingSystem.IsMacOS())
            {
                ShowMacOs(title, message);
            }
            else
            {
                // No native dialog toolkit is wired up for this platform yet. The crash
                // report and log file written to disk still contain the full details.
                Logger.Error($"[NativeMessageBox] No native dialog available on this platform for: {title}: {message}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[NativeMessageBox] Failed to display native dialog: {ex.Message}");
        }
    }

    // ── Windows: user32.dll MessageBoxW ─────────────────────────────────────

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_SETFOREGROUND = 0x00010000;
    private const uint MB_TOPMOST = 0x00040000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static void ShowWindows(string title, string message)
    {
        MessageBoxW(IntPtr.Zero, message, title, MB_OK | MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST);
    }

    // ── macOS: CoreFoundation CFUserNotificationDisplayAlert ────────────────
    // Displays a standalone alert dialog without needing a running NSApplication/AppKit
    // event loop, which makes it usable from a crash handler on any thread.

    private const string CoreFoundationFramework =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint KCFStringEncodingUtf8 = 0x08000100;
    private const uint KCFUserNotificationStopAlertLevel = 0;

    [DllImport(CoreFoundationFramework, CharSet = CharSet.Ansi)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

    [DllImport(CoreFoundationFramework)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundationFramework)]
    private static extern int CFUserNotificationDisplayAlert(
        double timeout,
        uint flags,
        IntPtr iconUrl,
        IntPtr soundUrl,
        IntPtr localizationUrl,
        IntPtr alertHeader,
        IntPtr alertMessage,
        IntPtr defaultButtonTitle,
        IntPtr alternateButtonTitle,
        IntPtr otherButtonTitle,
        out uint responseFlags);

    private static void ShowMacOs(string title, string message)
    {
        IntPtr headerRef = IntPtr.Zero;
        IntPtr messageRef = IntPtr.Zero;
        IntPtr okButtonRef = IntPtr.Zero;

        try
        {
            headerRef = CFStringCreateWithCString(IntPtr.Zero, title, KCFStringEncodingUtf8);
            messageRef = CFStringCreateWithCString(IntPtr.Zero, message, KCFStringEncodingUtf8);
            okButtonRef = CFStringCreateWithCString(IntPtr.Zero, "OK", KCFStringEncodingUtf8);

            CFUserNotificationDisplayAlert(
                0, // no timeout - waits for the user to press OK
                KCFUserNotificationStopAlertLevel,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                headerRef,
                messageRef,
                okButtonRef,
                IntPtr.Zero,
                IntPtr.Zero,
                out _);
        }
        finally
        {
            if (headerRef != IntPtr.Zero) CFRelease(headerRef);
            if (messageRef != IntPtr.Zero) CFRelease(messageRef);
            if (okButtonRef != IntPtr.Zero) CFRelease(okButtonRef);
        }
    }
}
