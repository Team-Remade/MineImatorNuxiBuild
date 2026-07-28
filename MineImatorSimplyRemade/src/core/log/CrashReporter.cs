using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MineImatorSimplyRemade.core.log;

/// <summary>
/// Global crash handling. Hooks unhandled exceptions on the main thread and background
/// threads, writes a timestamped crash report (with recent log context) to the
/// crash-reports folder, forwards the exception to Sentry, and shows a native OS window
/// letting the user know something went wrong and where the report was saved.
/// </summary>
public static class CrashReporter
{
    private const string AppTitle = "Mine Imator Nuxi";

    private static int _reported;

    /// <summary>
    /// Wires up process-wide unhandled exception hooks. Should be called once, early in
    /// startup (after <see cref="Logger.Initialize"/> and Sentry initialization).
    /// </summary>
    public static void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        Exception exception = e.ExceptionObject as Exception
            ?? new Exception($"Non-exception object thrown: {e.ExceptionObject}");

        Report(exception, "AppDomain.UnhandledException");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Unobserved task exceptions do not terminate the process, so log/report them
        // without showing the crash window (which is reserved for fatal errors).
        Logger.Error($"Unobserved task exception: {e.Exception}");
        TryCaptureSentry(e.Exception);
        e.SetObserved();
    }

    /// <summary>
    /// Handles a fatal exception: logs it, sends it to Sentry, writes a crash report file,
    /// and shows a native "something went wrong" window. Safe to call more than once;
    /// only the first call produces a report/dialog to avoid duplicate noise when an
    /// exception is observed by more than one handler.
    /// </summary>
    public static void Report(Exception? exception, string source)
    {
        if (Interlocked.Exchange(ref _reported, 1) != 0)
        {
            Logger.Error($"Additional crash from '{source}' suppressed (already handling a crash): {exception}");
            return;
        }

        exception ??= new Exception("Unknown fatal error (no exception information available).");

        Logger.Error($"Fatal exception from {source}: {exception}");

        TryCaptureSentry(exception);

        string reportPath = WriteCrashReport(exception, source);

        NativeMessageBox.Show($"{AppTitle} - Unexpected Error", BuildUserMessage(exception, reportPath));
    }

    private static void TryCaptureSentry(Exception exception)
    {
        try
        {
            SentrySdk.CaptureException(exception);
            SentrySdk.Flush(TimeSpan.FromSeconds(3));
        }
        catch (Exception sentryEx)
        {
            Logger.Error($"Failed to report exception to Sentry: {sentryEx.Message}");
        }
    }

    private static string WriteCrashReport(Exception exception, string source)
    {
        try
        {
            Directory.CreateDirectory(Logger.CrashReportsDirectory);

            string fileName = $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log";
            string path = Path.Combine(Logger.CrashReportsDirectory, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("Mine Imator Nuxi crash report");
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            sb.AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"App version: {typeof(CrashReporter).Assembly.GetName().Version}");
            sb.AppendLine();
            sb.AppendLine("---- Exception ----");
            sb.AppendLine(exception.ToString());
            sb.AppendLine();
            sb.AppendLine("---- Recent log output ----");
            sb.AppendLine(Logger.ReadLatestLogTail());

            File.WriteAllText(path, sb.ToString());
            Logger.Error($"Crash report written to '{path}'");

            return path;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to write crash report: {ex.Message}");
            return Logger.CrashReportsDirectory;
        }
    }

    private static string BuildUserMessage(Exception exception, string reportPath)
    {
        return
            $"{AppTitle} ran into a problem and needs to close.\n\n" +
            $"A crash report was saved to:\n{reportPath}\n\n" +
            $"Error details: {exception.GetType().Name}: {exception.Message}";
    }
}
