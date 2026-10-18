using System.Reflection;
using System.Runtime.InteropServices;

namespace MineImatorSimplyRemade.core.startup;

public static class NativeLibraryBootstrap
{
    private static readonly Lock Sync = new();
    private static bool _initialized;

    public static void Initialize()
    {
        if (!OperatingSystem.IsWindows())
            return;

        lock (Sync)
        {
            if (_initialized)
                return;

            Assembly assembly = typeof(NativeLibraryBootstrap).Assembly;
            string[] manifestResourceNames = assembly.GetManifestResourceNames();

            var dllResources = manifestResourceNames
                .Where(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Only extract and load embedded DLLs if any are embedded (e.g. on publish).
            if (dllResources.Length == 0)
            {
                _initialized = true;
                return;
            }

            TryCleanOldExtractions();

            string extractionRoot = Path.Combine(Path.GetTempPath(), "MineImatorSimplyRemade", "native", Environment.ProcessId.ToString());
            Directory.CreateDirectory(extractionRoot);

            // Step 1: Extract all embedded DLLs to disk
            var extractedFiles = new List<string>(dllResources.Length);
            foreach (string resourceName in dllResources)
            {
                string libraryName = GetLibraryFileName(assembly, resourceName);
                string destinationPath = Path.Combine(extractionRoot, libraryName);

                using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                    throw new InvalidOperationException($"Unable to open embedded native library resource: {resourceName}");

                using (FileStream fileStream = File.Create(destinationPath))
                {
                    resourceStream.CopyTo(fileStream);
                }

                extractedFiles.Add(destinationPath);
            }

            // Step 2: Set DLL search directory so interdependent DLLs can resolve each other
            SetDllDirectory(extractionRoot);

            // Step 3: Load all extracted libraries into the process via NativeLibrary.Load / LoadLibrary
            var loadedHandles = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
            foreach (string destinationPath in extractedFiles)
            {
                string libraryName = Path.GetFileName(destinationPath);
                IntPtr handle = IntPtr.Zero;
                try
                {
                    handle = NativeLibrary.Load(destinationPath);
                }
                catch
                {
                    handle = LoadLibrary(destinationPath);
                }

                if (handle == IntPtr.Zero)
                {
                    int errorCode = Marshal.GetLastPInvokeError();
                    throw new InvalidOperationException($"Failed to load native library '{libraryName}' from '{destinationPath}' (Win32 error {errorCode}).");
                }

                loadedHandles[libraryName] = handle;
                string nameWithoutExt = Path.GetFileNameWithoutExtension(destinationPath);
                loadedHandles[nameWithoutExt] = handle;
            }

            // Step 4: Register DllImportResolver on relevant assemblies so Silk.NET and other P/Invokes find the extracted DLLs
            IntPtr DllImportResolver(string libraryName, Assembly asm, DllImportSearchPath? searchPath)
            {
                if (loadedHandles.TryGetValue(libraryName, out IntPtr handle))
                    return handle;
                if (loadedHandles.TryGetValue(Path.GetFileName(libraryName), out handle))
                    return handle;
                if (loadedHandles.TryGetValue(Path.GetFileNameWithoutExtension(libraryName), out handle))
                    return handle;

                string directPath = Path.Combine(extractionRoot, libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? libraryName : libraryName + ".dll");
                if (File.Exists(directPath) && NativeLibrary.TryLoad(directPath, out handle))
                    return handle;

                return IntPtr.Zero;
            }

            NativeLibrary.SetDllImportResolver(assembly, DllImportResolver);
            foreach (Assembly loadedAssembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    NativeLibrary.SetDllImportResolver(loadedAssembly, DllImportResolver);
                }
                catch
                {
                    // Already has resolver or not allowed
                }
            }

            _initialized = true;
        }
    }

    private static string GetLibraryFileName(Assembly assembly, string resourceName)
    {
        string? assemblyName = assembly.GetName().Name;
        if (!string.IsNullOrEmpty(assemblyName) && resourceName.StartsWith(assemblyName + ".", StringComparison.OrdinalIgnoreCase))
        {
            return resourceName.Substring(assemblyName.Length + 1);
        }
        return resourceName;
    }

    private static void TryCleanOldExtractions()
    {
        try
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "MineImatorSimplyRemade", "native");
            if (!Directory.Exists(baseDir))
                return;

            string currentPid = Environment.ProcessId.ToString();
            foreach (string dir in Directory.GetDirectories(baseDir))
            {
                string dirName = Path.GetFileName(dir);
                if (dirName != currentPid)
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch
                    {
                        // Ignore directories in use by active processes
                    }
                }
            }
        }
        catch
        {
            // Non-critical, ignore
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);
}