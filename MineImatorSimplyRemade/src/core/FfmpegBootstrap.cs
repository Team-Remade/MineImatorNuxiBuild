using System.IO.Compression;
using System.Runtime.InteropServices;

namespace MineImatorSimplyRemade.core;

public static class FfmpegBootstrap
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static bool _ffmpegReady;
    public static string FfmpegBinaryDirectory => Path.Combine(main.ApplicationLocalDirectoryPath, "ffmpeg");

    public static string GetFfmpegExecutablePath()
    {
        string fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string localAppDataFfmpeg = Path.Combine(FfmpegBinaryDirectory, fileName);
        if (File.Exists(localAppDataFfmpeg))
            return localAppDataFfmpeg;

        string appBaseDir = AppContext.BaseDirectory;
        foreach (string directory in new[] { appBaseDir, Path.Combine(appBaseDir, "ffmpeg"), Path.Combine(appBaseDir, "tools") })
        {
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir, fileName);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                    // Ignore invalid PATH entries.
                }
            }
        }

        return localAppDataFfmpeg;
    }

    public static void EnsureFfmpegInstalled(Action<string>? statusCallback = null)
    {
        if (_ffmpegReady)
            return;

        InitLock.Wait();
        try
        {
            if (_ffmpegReady)
                return;

            string executablePath = GetFfmpegExecutablePath();
            if (!File.Exists(executablePath))
            {
                statusCallback?.Invoke("Extracting bundled FFmpeg (no internet required)...");
                ExtractBundledFfmpeg();
                executablePath = GetFfmpegExecutablePath();
            }

            if (!File.Exists(executablePath))
                throw new FileNotFoundException("FFmpeg executable was not found after extracting the bundled tools.", executablePath);

            _ffmpegReady = true;
            statusCallback?.Invoke("FFmpeg is ready.");
        }
        finally
        {
            InitLock.Release();
        }
    }

    private static void ExtractBundledFfmpeg()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Bundled FFmpeg supports Windows x64. Install FFmpeg on PATH for this platform.");

        using Stream resource = typeof(FfmpegBootstrap).Assembly.GetManifestResourceStream("MineImatorSimplyRemade.ffmpeg.win-x64.zip")
            ?? throw new InvalidOperationException("The embedded FFmpeg archive is missing from this build.");
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        Directory.CreateDirectory(FfmpegBinaryDirectory);

        // Publish the executable last and atomically so interrupted or concurrent launches never see a partial binary.
        foreach (string fileName in new[] { "LICENSE.txt", "README.txt", "SOURCE.txt", "ffmpeg.exe" })
        {
            ZipArchiveEntry entry = archive.GetEntry(fileName)
                ?? throw new InvalidDataException($"The embedded FFmpeg archive is missing {fileName}.");
            string targetPath = Path.Combine(FfmpegBinaryDirectory, fileName);
            string temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (Stream source = entry.Open())
                using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    source.CopyTo(destination);

                try
                {
                    File.Move(temporaryPath, targetPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(targetPath))
                {
                    // Another process may have finished extracting the same file first.
                }
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
