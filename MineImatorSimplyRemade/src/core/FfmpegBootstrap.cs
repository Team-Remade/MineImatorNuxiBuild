using FFMpegCore;
using FFMpegCore.Extensions.Downloader;
using FFMpegCore.Extensions.Downloader.Enums;
using NativeFileDialogSharp;
using System.Text.Json;
using MineImatorSimplyRemade;

namespace MineImatorSimplyRemade.core;

public static class FfmpegBootstrap
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static bool _ffmpegReady;
    public static string FfmpegBinaryDirectory => Path.Combine(main.ApplicationLocalDirectoryPath, "ffmpeg");
    private static string BootstrapStateFilePath => Path.Combine(main.ApplicationLocalDirectoryPath, "ffmpeg-bootstrap-state.json");

    public static bool RequiresFirstTimeDownload()
    {
        string ffmpegPath = GetFfmpegExecutablePath();
        return !File.Exists(ffmpegPath);
    }

    public static bool TryImportExistingInstallOnFirstLaunch(Action<string>? statusCallback = null)
    {
        if (!RequiresFirstTimeDownload())
            return true;

        var bootstrapState = LoadBootstrapState();
        if (bootstrapState.FirstLaunchBinaryPromptShown)
            return false;

        bootstrapState.FirstLaunchBinaryPromptShown = true;
        SaveBootstrapState(bootstrapState);

        statusCallback?.Invoke("Choose an existing ffmpeg binary (optional)...");

        string filter = OperatingSystem.IsWindows() ? "exe" : string.Empty;
        var result = Dialog.FileOpen(filter);
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
        {
            statusCallback?.Invoke("No existing ffmpeg binary selected.");
            return false;
        }

        if (!TryImportExistingInstallFromExecutable(result.Path, out string detail))
        {
            statusCallback?.Invoke(detail);
            return false;
        }

        statusCallback?.Invoke(detail);
        return true;
    }

    public static string GetFfmpegExecutablePath()
    {
        string fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        // 1. Check user application data directory
        string localAppDataFfmpeg = Path.Combine(FfmpegBinaryDirectory, fileName);
        if (File.Exists(localAppDataFfmpeg))
            return localAppDataFfmpeg;

        // 2. Check local application / bundled directories
        string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
        string appFfmpeg = Path.Combine(appBaseDir, fileName);
        if (File.Exists(appFfmpeg))
            return appFfmpeg;

        string appSubFfmpeg = Path.Combine(appBaseDir, "ffmpeg", fileName);
        if (File.Exists(appSubFfmpeg))
            return appSubFfmpeg;

        string toolsFfmpeg = Path.Combine(appBaseDir, "tools", fileName);
        if (File.Exists(toolsFfmpeg))
            return toolsFfmpeg;

        // 3. Check system PATH
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
                catch
                {
                    // Ignore invalid PATH entries
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
            string binaryDir = File.Exists(executablePath)
                ? (Path.GetDirectoryName(executablePath) ?? FfmpegBinaryDirectory)
                : FfmpegBinaryDirectory;

            Directory.CreateDirectory(binaryDir);
            Directory.CreateDirectory(Path.Combine(binaryDir, "temp"));

            ConfigureGlobalFfmpegOptions(binaryDir);

            if (RequiresFirstTimeDownload())
            {
                statusCallback?.Invoke("Downloading minimal FFmpeg binary...");
                Console.WriteLine($"Downloading binary to '{FfmpegBinaryDirectory}'...");

                var options = new FFOptions
                {
                    BinaryFolder = FfmpegBinaryDirectory,
                    WorkingDirectory = FfmpegBinaryDirectory,
                    TemporaryFilesFolder = Path.Combine(FfmpegBinaryDirectory, "temp")
                };

                FFMpegDownloader
                    .DownloadBinaries(FFMpegVersions.LatestAvailable,
                                      FFMpegBinaries.FFMpeg,
                                      options,
                                      null)
                    .GetAwaiter()
                    .GetResult();

                statusCallback?.Invoke("Finalizing FFmpeg setup...");
            }

            string finalExecutable = GetFfmpegExecutablePath();
            string finalDir = File.Exists(finalExecutable)
                ? (Path.GetDirectoryName(finalExecutable) ?? FfmpegBinaryDirectory)
                : FfmpegBinaryDirectory;

            ConfigureGlobalFfmpegOptions(finalDir);
            _ffmpegReady = true;
            statusCallback?.Invoke("FFmpeg is ready.");
        }
        finally
        {
            InitLock.Release();
        }
    }

    private static void ConfigureGlobalFfmpegOptions(string binaryDirectory)
    {
        string tempDir = Path.Combine(binaryDirectory, "temp");
        Directory.CreateDirectory(tempDir);

        GlobalFFOptions.Configure(options =>
        {
            options.BinaryFolder = binaryDirectory;
            options.WorkingDirectory = binaryDirectory;
            options.TemporaryFilesFolder = tempDir;
        });
    }

    private static bool HasFfmpegBinary(string directory)
    {
        string fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return File.Exists(Path.Combine(directory, fileName));
    }

    private static bool TryImportExistingInstallFromExecutable(string selectedExecutablePath, out string detail)
    {
        detail = "";

        string fullSelectedPath;
        try
        {
            fullSelectedPath = Path.GetFullPath(selectedExecutablePath);
        }
        catch (Exception)
        {
            detail = "Selected ffmpeg path is invalid. Falling back to download.";
            return false;
        }

        if (!File.Exists(fullSelectedPath))
        {
            detail = "Selected ffmpeg binary was not found. Falling back to download.";
            return false;
        }

        string sourceDirectory = Path.GetDirectoryName(fullSelectedPath) ?? string.Empty;
        string sourceFfmpegPath = File.Exists(fullSelectedPath) && Path.GetFileName(fullSelectedPath).StartsWith("ffmpeg", StringComparison.OrdinalIgnoreCase)
            ? fullSelectedPath
            : Path.Combine(sourceDirectory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        string sourceFfprobePath = Path.Combine(sourceDirectory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");

        if (!File.Exists(sourceFfmpegPath))
        {
            detail = "Selected location does not include an ffmpeg binary. Falling back to download.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(FfmpegBinaryDirectory);
            Directory.CreateDirectory(Path.Combine(FfmpegBinaryDirectory, "temp"));

            string targetFfmpegPath = Path.Combine(FfmpegBinaryDirectory, Path.GetFileName(sourceFfmpegPath));
            File.Copy(sourceFfmpegPath, targetFfmpegPath, overwrite: true);

            if (File.Exists(sourceFfprobePath))
            {
                string targetFfprobePath = Path.Combine(FfmpegBinaryDirectory, Path.GetFileName(sourceFfprobePath));
                File.Copy(sourceFfprobePath, targetFfprobePath, overwrite: true);
            }

            ConfigureGlobalFfmpegOptions(FfmpegBinaryDirectory);
            detail = "Using pre-existing FFmpeg binary from selected location.";
            return true;
        }
        catch (Exception ex)
        {
            detail = $"Failed to copy existing FFmpeg binary: {ex.Message}. Falling back to download.";
            return false;
        }
    }

    private static FfmpegBootstrapState LoadBootstrapState()
    {
        try
        {
            if (!File.Exists(BootstrapStateFilePath))
                return new FfmpegBootstrapState();

            string json = File.ReadAllText(BootstrapStateFilePath);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.FfmpegBootstrapState)
                   ?? new FfmpegBootstrapState();
        }
        catch
        {
            return new FfmpegBootstrapState();
        }
    }

    private static void SaveBootstrapState(FfmpegBootstrapState state)
    {
        try
        {
            Directory.CreateDirectory(main.ApplicationLocalDirectoryPath);

            string json = JsonSerializer.Serialize(state, AppJsonContext.Default.FfmpegBootstrapState);
            File.WriteAllText(BootstrapStateFilePath, json);
        }
        catch
        {
            // Startup can continue even if state persistence fails.
        }
    }

    public sealed class FfmpegBootstrapState
    {
        public bool FirstLaunchBinaryPromptShown { get; set; }
    }
}
