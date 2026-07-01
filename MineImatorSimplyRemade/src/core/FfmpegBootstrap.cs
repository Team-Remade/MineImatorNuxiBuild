using FFMpegCore;
using FFMpegCore.Extensions.Downloader;
using FFMpegCore.Extensions.Downloader.Enums;

namespace MineImatorSimplyRemade.core;

public static class FfmpegBootstrap
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static bool _ffmpegReady;
    public static string FfmpegBinaryDirectory => Path.Combine(main.ApplicationLocalDirectoryPath, "ffmpeg");

    public static bool RequiresFirstTimeDownload()
    {
        return !HasFfmpegBinary(FfmpegBinaryDirectory) || !HasFfprobeBinary(FfmpegBinaryDirectory);
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
}
