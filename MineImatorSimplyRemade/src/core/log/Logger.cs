namespace MineImatorSimplyRemade.core.log;

/// <summary>
/// File-backed logging for the application. Writes a rolling "latest.log" file (with up to
/// <see cref="MaxPreviousLogs"/> rotated backups) under a "logs" folder next to the app
/// executable (alongside the "data" folder), and transparently mirrors everything written
/// via <see cref="Console.WriteLine"/> / <see cref="Console.Error"/> into that file so
/// existing logging call sites across the codebase don't need to change.
/// </summary>
public static class Logger
{
    private const int MaxPreviousLogs = 5;

    private static readonly Lock WriteLock = new();
    private static StreamWriter? _writer;
    private static TextWriter? _originalOut;
    private static TextWriter? _originalError;
    private static bool _initialized;

    public static string LogDirectory { get; private set; } = string.Empty;
    public static string CrashReportsDirectory { get; private set; } = string.Empty;
    public static string LatestLogPath { get; private set; } = string.Empty;

    /// <summary>
    /// Resolves the directory containing the application executable, the same base used for
    /// the "data" folder. Uses Environment.ProcessPath (same as BlockRegistry/CharacterRegistry)
    /// so that single-file self-contained publishes find logs/crash-reports next to the .exe
    /// rather than in the temp extraction directory that AppContext.BaseDirectory points to.
    /// </summary>
    private static string ExecutableDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>
    /// Sets up the logs/crash-reports folders, rotates previous logs, opens a fresh
    /// latest.log, and redirects Console output so it is also captured to disk.
    /// Safe to call multiple times; only the first call has an effect.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        string baseDir = ExecutableDirectory;
        LogDirectory = Path.Combine(baseDir, "logs");
        CrashReportsDirectory = Path.Combine(baseDir, "crash-reports");
        LatestLogPath = Path.Combine(LogDirectory, "latest.log");

        try
        {
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(CrashReportsDirectory);

            RotateLogs();

            _writer = new StreamWriter(new FileStream(LatestLogPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };

            _originalOut = Console.Out;
            _originalError = Console.Error;

            Console.SetOut(new TeeTextWriter(_originalOut, line => WriteLine("INFO", line)));
            Console.SetError(new TeeTextWriter(_originalError, line => WriteLine("ERROR", line)));

            WriteLine("INFO", "==== Mine Imator Nuxi starting ====");
            WriteLine("INFO", $"App version: {typeof(Logger).Assembly.GetName().Version}");
            WriteLine("INFO", $"OS: {Environment.OSVersion}, .NET: {Environment.Version}, 64-bit process: {Environment.Is64BitProcess}");
        }
        catch (Exception ex)
        {
            // Logging setup must never prevent the app from starting.
            (_originalError ?? Console.Error).WriteLine($"Failed to initialize file logging: {ex.Message}");
        }
    }

    /// <summary>
    /// Shifts previous-1.log..previous-4.log up by one slot (dropping previous-5.log),
    /// then moves the existing latest.log (if any) into previous-1.log.
    /// </summary>
    private static void RotateLogs()
    {
        string PreviousPath(int index) => Path.Combine(LogDirectory, $"previous-{index}.log");

        string oldest = PreviousPath(MaxPreviousLogs);
        if (File.Exists(oldest))
            File.Delete(oldest);

        for (int i = MaxPreviousLogs - 1; i >= 1; i--)
        {
            string source = PreviousPath(i);
            if (File.Exists(source))
                File.Move(source, PreviousPath(i + 1), overwrite: true);
        }

        if (File.Exists(LatestLogPath))
            File.Move(LatestLogPath, PreviousPath(1), overwrite: true);
    }

    public static void WriteLine(string level, string? message)
    {
        if (message == null)
            return;

        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

        lock (WriteLock)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch
            {
                // Never let a logging failure crash the app.
            }
        }
    }

    public static void Debug(string message) => WriteLine("DEBUG", message);
    public static void Info(string message) => WriteLine("INFO", message);
    public static void Warn(string message) => WriteLine("WARN", message);
    public static void Error(string message) => WriteLine("ERROR", message);

    /// <summary>
    /// Returns the last <paramref name="maxLines"/> lines of the current latest.log,
    /// useful for embedding recent context in crash reports.
    /// </summary>
    public static string ReadLatestLogTail(int maxLines = 200)
    {
        try
        {
            lock (WriteLock)
            {
                _writer?.Flush();
            }

            if (!File.Exists(LatestLogPath))
                return string.Empty;

            string[] lines = File.ReadAllLines(LatestLogPath);
            int start = Math.Max(0, lines.Length - maxLines);
            return string.Join(Environment.NewLine, lines[start..]);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Flushes and closes the log file and restores the original Console writers.
    /// </summary>
    public static void Shutdown()
    {
        if (!_initialized)
            return;

        lock (WriteLock)
        {
            try
            {
                WriteLine("INFO", "==== Shutting down ====");
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch
            {
                // Ignore - we're shutting down anyway.
            }
            finally
            {
                _writer = null;
            }
        }

        if (_originalOut != null)
            Console.SetOut(_originalOut);

        if (_originalError != null)
            Console.SetError(_originalError);

        _initialized = false;
    }
}
