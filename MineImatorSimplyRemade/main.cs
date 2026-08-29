using Avalonia;
using MineImatorSimplyRemade.core.log;

public static class main
{
    public const string ApplicationLocalDirectory = "SimplyRemadeNuxi";

    public static readonly string LocalPath =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string ApplicationLocalDirectoryPath { get; } =
        Path.Combine(LocalPath, ApplicationLocalDirectory);

    /// <summary>
    /// Process entry point. Windowing/UI now runs on Avalonia instead of GLFW+ImGui;
    /// all app orchestration (startup splash, main window, camera preview window) that
    /// used to live in this Main loop now lives in <see cref="MineImatorSimplyRemade.App"/>
    /// and its Avalonia windows.
    /// </summary>
    public static int Main(string[] args)
    {
        // Set up file logging (logs\latest.log + up to 5 rotated logs\previous-N.log)
        // as early as possible so startup output is captured too.
        Logger.Initialize();

        SentrySdk.Init(options =>
        {
            // A Sentry Data Source Name (DSN) is required.
            // See https://docs.sentry.io/product/sentry-basics/dsn-explainer/
            options.Dsn = "https://ba6126794a0f2713d069a8f2b7311187@o4511769100419072.ingest.us.sentry.io/4511769112739840";
            options.Debug = false;
            options.AutoSessionTracking = true;
            options.EnableLogs = true;
        });

        // Catch unhandled exceptions on background threads too (e.g. Task continuations),
        // writing a crash report and showing the native "something went wrong" dialog.
        CrashReporter.Initialize();

        // Renderer-migration verification switch: runs the headless Veldrid smoke
        // test (see VeldridSmokeTest) and exits, without touching Avalonia/GLFW/
        // any not-yet-ported panel at all. Not intended for end users.
        int smokeTestIndex = Array.IndexOf(args, "--veldrid-smoke-test");
        if (smokeTestIndex >= 0)
        {
            string outputPath = smokeTestIndex + 1 < args.Length
                ? args[smokeTestIndex + 1]
                : Path.Combine(Path.GetTempPath(), "veldrid-smoke-test.png");
            return MineImatorSimplyRemade.core.render.VeldridSmokeTest.Run(outputPath);
        }

        try
        {
            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            CrashReporter.Report(e, "Main");
            return 1;
        }
        finally
        {
            Logger.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<MineImatorSimplyRemade.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
