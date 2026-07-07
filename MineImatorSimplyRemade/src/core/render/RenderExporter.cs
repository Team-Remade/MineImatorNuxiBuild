using System.Diagnostics;
using MineImatorSimplyRemade.core.ui.Panels;

namespace MineImatorSimplyRemade.core.render;

public sealed class RenderExporter
{
    private readonly CameraViewport _cameraViewport;

    public sealed class VideoExportSession : IDisposable
    {
        private readonly Process _process;
        private readonly BinaryWriter _writer;
        private readonly int _expectedFrameBytes;
        private bool _closed;

        public string OutputPath { get; }

        internal VideoExportSession(string outputPath, Process process, BinaryWriter writer, int expectedFrameBytes)
        {
            OutputPath = outputPath;
            _process = process;
            _writer = writer;
            _expectedFrameBytes = expectedFrameBytes;
        }

        public void AppendFrame(byte[] rgbFrame)
        {
            if (_closed)
                throw new InvalidOperationException("Cannot append frame to a closed video export session.");

            if (rgbFrame.Length != _expectedFrameBytes)
                throw new ArgumentException($"Unexpected frame size. Expected {_expectedFrameBytes} bytes, got {rgbFrame.Length}.", nameof(rgbFrame));

            _writer.Write(rgbFrame, 0, rgbFrame.Length);
        }

        public void Complete()
        {
            if (_closed)
                return;

            _closed = true;
            _writer.Dispose();

            string stderr = _process.StandardError.ReadToEnd();
            _process.WaitForExit();

            if (_process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg failed with code {_process.ExitCode}: {stderr}");
        }

        public void Cancel()
        {
            if (_closed)
                return;

            _closed = true;
            try
            {
                _writer.Dispose();
            }
            catch
            {
                // Best-effort cleanup.
            }

            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        public void Dispose()
        {
            if (!_closed)
                Cancel();

            _process.Dispose();
        }
    }

    public RenderExporter(CameraViewport cameraViewport)
    {
        _cameraViewport = cameraViewport;
    }

    public string ExportImage(int width, int height, string imageFormat, string outputPath, bool highQuality = false)
    {
        EnsureFfmpegReady();

        if (!_cameraViewport.CaptureCurrentViewRgb((uint)width, (uint)height, highQuality, out byte[] frame))
            throw new InvalidOperationException("Failed to capture render frame.");

        return ExportImageFromRgb(width, height, imageFormat, outputPath, frame);
    }

    public string ExportImageFromRgb(int width, int height, string imageFormat, string outputPath, byte[] rgbFrame)
    {
        EnsureFfmpegReady();

        string format = NormalizeImageFormat(imageFormat);
        string finalPath = EnsureOutputExtension(outputPath, format);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? ".");

        List<string> args =
        [
            "-y",
            "-f", "rawvideo",
            "-pixel_format", "rgb24",
            "-video_size", $"{width}x{height}",
            "-i", "-",
            "-frames:v", "1"
        ];

        if (string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(["-q:v", "2"]);
        }

        args.Add(finalPath);

        RunFfmpegWithRawFrames(args, writer => writer.Write(rgbFrame, 0, rgbFrame.Length));
        return finalPath;
    }

    public VideoExportSession CreateVideoSession(
        int width,
        int height,
        int framerate,
        int bitrateKbps,
        string videoFormat,
        string outputPath)
    {
        EnsureFfmpegReady();

        int fps = Math.Clamp(framerate, 1, 120);
        int bitrate = Math.Clamp(bitrateKbps, 500, 200000);
        string format = NormalizeVideoFormat(videoFormat);
        string finalPath = EnsureOutputExtension(outputPath, format);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? ".");

        List<string> args =
        [
            "-y",
            "-f", "rawvideo",
            "-pixel_format", "rgb24",
            "-video_size", $"{width}x{height}",
            "-framerate", fps.ToString(),
            "-i", "-",
            "-an"
        ];

        if (string.Equals(format, "webm", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(["-c:v", "libvpx-vp9", "-b:v", $"{bitrate}k", "-pix_fmt", "yuv420p"]);
        }
        else
        {
            args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-b:v", $"{bitrate}k"]);
        }

        args.Add(finalPath);

        Process process = CreateFfmpegProcess(args);
        process.Start();

        var writer = new BinaryWriter(process.StandardInput.BaseStream);
        int expectedFrameBytes = checked(width * height * 3);
        return new VideoExportSession(finalPath, process, writer, expectedFrameBytes);
    }

    public static string EncodePpmSequenceToVideo(string framesDirectory, int framerate, int bitrateKbps, string videoFormat, string outputPath)
    {
        EnsureFfmpegReady();

        int fps = Math.Clamp(framerate, 1, 120);
        int bitrate = Math.Clamp(bitrateKbps, 500, 200000);
        string format = NormalizeVideoFormat(videoFormat);
        string finalPath = EnsureOutputExtension(outputPath, format);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? ".");

        string pattern = Path.Combine(framesDirectory, "frame_%06d.ppm");

        List<string> args =
        [
            "-y",
            "-framerate", fps.ToString(),
            "-i", pattern,
            "-an"
        ];

        if (string.Equals(format, "webm", StringComparison.OrdinalIgnoreCase))
            args.AddRange(["-c:v", "libvpx-vp9", "-b:v", $"{bitrate}k", "-pix_fmt", "yuv420p"]);
        else
            args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-b:v", $"{bitrate}k"]);

        args.Add(finalPath);

        using Process process = CreateFfmpegProcess(args);
        process.Start();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed with code {process.ExitCode}: {stderr}");

        return finalPath;
    }

    public string ExportVideo(
        int width,
        int height,
        int framerate,
        int bitrateKbps,
        string videoFormat,
        string outputPath,
        Timeline timeline,
        bool highQuality = false)
    {
        EnsureFfmpegReady();

        int fps = Math.Clamp(framerate, 1, 120);
        int bitrate = Math.Clamp(bitrateKbps, 500, 200000);
        string format = NormalizeVideoFormat(videoFormat);
        string finalPath = EnsureOutputExtension(outputPath, format);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? ".");

        int originalFrame = timeline.CurrentFrame;

        try
        {
            using VideoExportSession session = CreateVideoSession(width, height, fps, bitrate, format, finalPath);
            for (int frame = 0; frame <= timeline.MaxFrames; frame++)
            {
                timeline.SetCurrentFrame(frame);
                if (!_cameraViewport.CaptureCurrentViewRgb((uint)width, (uint)height, highQuality, out byte[] rgbFrame))
                    throw new InvalidOperationException($"Failed to capture frame {frame}.");

                session.AppendFrame(rgbFrame);
            }

            session.Complete();
        }
        finally
        {
            timeline.SetCurrentFrame(originalFrame);
        }

        return finalPath;
    }

    private static void EnsureFfmpegReady()
    {
        FfmpegBootstrap.EnsureFfmpegInstalled();
        string ffmpegPath = FfmpegBootstrap.GetFfmpegExecutablePath();
        if (!File.Exists(ffmpegPath))
            throw new FileNotFoundException("ffmpeg executable was not found after setup.", ffmpegPath);
    }

    private static void RunFfmpegWithRawFrames(IReadOnlyList<string> args, Action<BinaryWriter> writeFrames)
    {
        using Process process = CreateFfmpegProcess(args);

        process.Start();

        using (var stdin = process.StandardInput.BaseStream)
        using (var writer = new BinaryWriter(stdin))
        {
            writeFrames(writer);
        }

        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed with code {process.ExitCode}: {stderr}");
    }

    private static Process CreateFfmpegProcess(IReadOnlyList<string> args)
    {
        string ffmpegPath = FfmpegBootstrap.GetFfmpegExecutablePath();
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = BuildArgumentString(args),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
    }

    private static string BuildArgumentString(IReadOnlyList<string> args)
    {
        return string.Join(" ", args.Select(EscapeArg));
    }

    private static string EscapeArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        if (value.IndexOfAny([' ', '\t', '\n', '\r', '"']) < 0)
            return value;

        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string EnsureOutputExtension(string outputPath, string extensionWithoutDot)
    {
        string ext = "." + extensionWithoutDot.ToLowerInvariant();
        if (string.Equals(Path.GetExtension(outputPath), ext, StringComparison.OrdinalIgnoreCase))
            return outputPath;

        return Path.ChangeExtension(outputPath, ext);
    }

    private static string NormalizeImageFormat(string format)
    {
        string normalized = (format ?? "png").Trim().ToLowerInvariant();
        return normalized switch
        {
            "jpg" => "jpg",
            "jpeg" => "jpg",
            "webp" => "webp",
            _ => "png"
        };
    }

    private static string NormalizeVideoFormat(string format)
    {
        string normalized = (format ?? "mp4").Trim().ToLowerInvariant();
        return normalized == "webm" ? "webm" : "mp4";
    }
}
