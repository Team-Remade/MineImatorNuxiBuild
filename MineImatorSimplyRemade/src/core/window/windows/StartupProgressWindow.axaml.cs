using System.ComponentModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MineImatorSimplyRemade.core.startup;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.window.windows;

/// <summary>
/// Startup/progress splash window. Ported from the old GLFW+ImGui immediate-mode
/// version: instead of re-drawing everything every frame from
/// <c>StartupProgressState</c> fields, this now binds to a
/// <see cref="core.startup.StartupProgressState"/> that raises
/// <see cref="INotifyPropertyChanged"/> and pushes updates straight to the
/// Avalonia controls whenever a caller mutates <see cref="ProgressState"/>.
/// </summary>
public partial class StartupProgressWindow : Window
{
    private readonly record struct GifFrame(Bitmap Bitmap, int DelayMs);

    public StartupProgressState ProgressState { get; } = new();

    private readonly DispatcherTimer _dotsTimer;
    private readonly DispatcherTimer _gifTimer;
    private readonly DateTime _startTime = DateTime.UtcNow;

    private readonly List<GifFrame> _gifFrames = new();
    private int _gifFrameIndex;

    public StartupProgressWindow()
    {
        InitializeComponent();

        ProgressState.PropertyChanged += OnProgressStatePropertyChanged;
        RefreshAll();

        LoadLoadingGif();

        _dotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _dotsTimer.Tick += (_, _) => UpdateWorkingText();
        _dotsTimer.Start();

        _gifTimer = new DispatcherTimer();
        if (_gifFrames.Count > 1)
        {
            _gifTimer.Interval = TimeSpan.FromMilliseconds(_gifFrames[0].DelayMs);
            _gifTimer.Tick += (_, _) => AdvanceGifFrame();
            _gifTimer.Start();
        }

        UpdateWorkingText();
    }

    private void OnProgressStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Property changes can be raised from background threads (e.g. the FFmpeg
        // bootstrap poll loop or runtime-init progress callback); marshal to the UI
        // thread the same way the old code implicitly did by only touching state
        // from the single-threaded GLFW render loop.
        Dispatcher.UIThread.Post(RefreshAll);
    }

    private void RefreshAll()
    {
        TitleText.Text = ProgressState.Title;
        StepLabelText.Text = ProgressState.StepLabel;
        PhaseText.Text = ProgressState.Phase;
        Progress.Value = Math.Clamp(ProgressState.Progress, 0f, 1f);
        StatusText.Text = ProgressState.Status;
        DetailText.Text = ProgressState.Detail;
        DetailText.IsVisible = ProgressState.HasDetail;
    }

    private void UpdateWorkingText()
    {
        double elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
        int dots = (int)(elapsed * 2.0) % 4;
        WorkingText.Text = "Working" + new string('.', dots);
    }

    private void LoadLoadingGif()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using Stream? stream = assembly.GetManifestResourceStream("MineImatorSimplyRemade.assets.img.loading.gif");
        if (stream == null)
        {
            Console.WriteLine("Embedded loading.gif not found.");
            return;
        }

        List<AnimatedFrameResult> frames = new();
        try
        {
            foreach (AnimatedFrameResult frame in ImageResult.AnimatedGifFramesFromStream(stream, ColorComponents.RedGreenBlueAlpha))
                frames.Add(frame);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to decode loading.gif with stb_image: {ex.Message}");
            return;
        }

        foreach (AnimatedFrameResult frame in frames)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || frame.Data.Length == 0)
                continue;

            var bitmap = new WriteableBitmap(
                new Avalonia.PixelSize(frame.Width, frame.Height),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Unpremul);

            using (var fb = bitmap.Lock())
            {
                System.Runtime.InteropServices.Marshal.Copy(frame.Data, 0, fb.Address, frame.Data.Length);
            }

            int delayMs = frame.DelayInMs > 0 ? frame.DelayInMs : 80;
            _gifFrames.Add(new GifFrame(bitmap, delayMs));
        }

        if (_gifFrames.Count > 0)
            GifImage.Source = _gifFrames[0].Bitmap;
    }

    private void AdvanceGifFrame()
    {
        if (_gifFrames.Count <= 1)
            return;

        _gifFrameIndex = (_gifFrameIndex + 1) % _gifFrames.Count;
        GifImage.Source = _gifFrames[_gifFrameIndex].Bitmap;
        _gifTimer.Interval = TimeSpan.FromMilliseconds(_gifFrames[_gifFrameIndex].DelayMs);
    }
}
