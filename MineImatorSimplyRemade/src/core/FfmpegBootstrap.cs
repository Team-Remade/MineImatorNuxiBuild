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
        return !HasFfmpegBinary(FfmpegBinaryDirectory) || !HasFfprobeBinary(FfmpegBinaryDirectory);
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
        return Path.Combine(FfmpegBinaryDirectory, fileName);
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

            Directory.CreateDirectory(FfmpegBinaryDirectory);
            Directory.CreateDirectory(Path.Combine(FfmpegBinaryDirectory, "temp"));

            ConfigureGlobalFfmpegOptions(FfmpegBinaryDirectory);

            if (RequiresFirstTimeDownload())
            {
                statusCallback?.Invoke("Downloading FFmpeg binaries...");
                Console.WriteLine($"[FFmpeg] Downloading binaries to '{FfmpegBinaryDirectory}'...");

                var options = new FFOptions
                {
                    BinaryFolder = FfmpegBinaryDirectory,
                    WorkingDirectory = FfmpegBinaryDirectory,
                    TemporaryFilesFolder = Path.Combine(FfmpegBinaryDirectory, "temp")
                };

                FFMpegDownloader
                    .DownloadBinaries(FFMpegVersions.LatestAvailable,
                                      FFMpegBinaries.FFMpeg | FFMpegBinaries.FFProbe,
                                      options,
                                      null)
                    .GetAwaiter()
                    .GetResult();

                statusCallback?.Invoke("Finalizing FFmpeg setup...");
            }

            ConfigureGlobalFfmpegOptions(FfmpegBinaryDirectory);
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

    private static bool HasFfprobeBinary(string directory)
    {
        string fileName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
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
        string sourceFfmpegPath = Path.Combine(sourceDirectory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        string sourceFfprobePath = Path.Combine(sourceDirectory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");

        if (!File.Exists(sourceFfmpegPath) || !File.Exists(sourceFfprobePath))
        {
            detail = "Selected folder does not include both ffmpeg and ffprobe binaries. Falling back to download.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(FfmpegBinaryDirectory);
            Directory.CreateDirectory(Path.Combine(FfmpegBinaryDirectory, "temp"));

            string targetFfmpegPath = Path.Combine(FfmpegBinaryDirectory, Path.GetFileName(sourceFfmpegPath));
            string targetFfprobePath = Path.Combine(FfmpegBinaryDirectory, Path.GetFileName(sourceFfprobePath));

            File.Copy(sourceFfmpegPath, targetFfmpegPath, overwrite: true);
            File.Copy(sourceFfprobePath, targetFfprobePath, overwrite: true);

            ConfigureGlobalFfmpegOptions(FfmpegBinaryDirectory);
            detail = "Using pre-existing FFmpeg binaries from selected folder.";
            return true;
        }
        catch (Exception ex)
        {
            detail = $"Failed to copy existing FFmpeg binaries: {ex.Message}. Falling back to download.";
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
