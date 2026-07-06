using System.Reflection;
using System.Runtime.InteropServices;

namespace MineImatorSimplyRemade.core.startup;

public static class NativeLibraryBootstrap
{
    private static readonly object Sync = new();
    private static bool _initialized;

    private static readonly string[] WindowsNativeLibraries =
    [
        "cimgui.dll",
        "glfw3.dll",
        "ImGuiImplGLFW.dll",
        "ImGuiImpl.dll",
        "Assimp64.dll",
        "nfd.dll"
    ];

    public static void Initialize()
    {
        if (!OperatingSystem.IsWindows())
            return;

        lock (Sync)
        {
            if (_initialized)
                return;

            string extractionRoot = Path.Combine(Path.GetTempPath(), "MineImatorSimplyRemade", "native", Environment.ProcessId.ToString());
            Directory.CreateDirectory(extractionRoot);

            Assembly assembly = typeof(NativeLibraryBootstrap).Assembly;

            foreach (string libraryName in WindowsNativeLibraries)
            {
                ExtractAndLoadLibrary(assembly, libraryName, extractionRoot);
            }

            _initialized = true;
        }
    }

    private static void ExtractAndLoadLibrary(Assembly assembly, string libraryName, string extractionRoot)
    {
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(libraryName, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new InvalidOperationException($"Missing embedded native library resource: {libraryName}");

        string destinationPath = Path.Combine(extractionRoot, libraryName);

        using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);
        if (resourceStream == null)
            throw new InvalidOperationException($"Unable to open embedded native library resource: {libraryName}");

        using (FileStream fileStream = File.Create(destinationPath))
        {
            resourceStream.CopyTo(fileStream);
        }

        IntPtr handle = LoadLibrary(destinationPath);
        if (handle == IntPtr.Zero)
        {
            int errorCode = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException($"Failed to load native library '{libraryName}' from '{destinationPath}' (Win32 error {errorCode}).");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);
}