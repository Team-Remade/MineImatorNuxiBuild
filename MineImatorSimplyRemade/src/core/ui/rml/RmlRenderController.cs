using System.Globalization;
using System.Net;
using System.Text;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Retained-mode replacement for MainWindow's ImGui-drawn Render Output dialog
/// (see OpenRenderPopup/RenderRenderPopup/RenderRenderProgressSection in the old ImGui MainWindow).
/// This is scaffolding only: it mirrors the settings/progress UI and state shape but is not yet
/// wired to the real render pipeline (RenderExporter lives on MainWindow).</summary>
public sealed class RmlRenderController
{
    public enum RenderMode
    {
        Image,
        Video
    }

    private readonly record struct ResolutionPreset(string Name, int Width, int Height);

    private static readonly ResolutionPreset[] ResolutionPresets =
    [
        new("Avatar 512x512", 512, 512),
        new("VGA 640x480", 640, 480),
        new("720P", 1280, 720),
        new("1080P", 1920, 1080),
        new("1440P", 2560, 1440),
        new("720P Cinematic", 1280, 544),
        new("1080P Cinematic", 1920, 816),
        new("1440P Cinematic", 2560, 1088),
        new("1600P Cinematic", 3200, 1360),
        new("UW4k Cinematic", 3840, 1600),
        new("UW5k Cinematic", 5120, 2160),
        new("Preview 960x400", 960, 400),
        new("Custom", 0, 0)
    ];

    private static readonly int[] FramerateOptions = [12, 24, 25, 30, 48, 50, 60, 120];
    private static readonly int[] BitrateOptionsKbps = [4000, 8000, 12000, 20000, 40000, 80000];

    private readonly Element _overlay;
    private readonly Element _root;
    public bool Visible { get; private set; }

    private RenderMode _mode = RenderMode.Image;
    private int _width = 1920;
    private int _height = 1080;
    private int _framerate = 30;
    private int _bitrateKbps = 12000;
    private bool _highQuality = true;
    private string _imageFormat = "png";
    private string _videoFormat = "mp4";
    private string _preset = "1080P";

    private bool _jobActive;
    private bool _jobFinished;
    private string _jobStatus = "";
    private int _frameCurrent;
    private int _frameTotal = 1;
    private double _jobStartedAtSeconds;

    private string _lastRenderedSignature = string.Empty;

    public RmlRenderController(Element overlay, Element root)
    {
        _overlay = overlay;
        _root = root;
        Refresh();
    }

    public void Show(RenderMode mode)
    {
        Visible = true;
        _mode = mode;
        _overlay.SetProperty("display", "block");
        Refresh(force: true);
    }

    public void Toggle(RenderMode mode)
    {
        if (Visible)
            Hide();
        else
            Show(mode);
    }

    public void Hide()
    {
        Visible = false;
        _overlay.SetProperty("display", "none");
    }

    public void Update()
    {
        if (!Visible)
            return;

        if (_jobActive)
            AdvanceSimulatedJob();

        Refresh();
    }

    private void StartRender()
    {
        if (_jobActive)
            return;

        _jobActive = true;
        _jobFinished = false;
        _frameCurrent = 0;
        _frameTotal = _mode == RenderMode.Video ? Math.Max(1, _framerate * 5) : 1;
        _jobStatus = _mode == RenderMode.Video ? "Rendering frames..." : "Rendering image...";
        _jobStartedAtSeconds = GetNowSeconds();
        Refresh(force: true);
    }

    private void CancelRender()
    {
        _jobActive = false;
        _jobFinished = false;
        _jobStatus = "Render canceled";
        Refresh(force: true);
    }

    private void AdvanceSimulatedJob()
    {
        if (_frameCurrent < _frameTotal)
        {
            _frameCurrent++;
            _jobStatus = _mode == RenderMode.Video
                ? $"Rendering frame {_frameCurrent}/{_frameTotal}..."
                : "Rendering image...";
        }
        else
        {
            _jobActive = false;
            _jobFinished = true;
            _jobStatus = "Render complete";
        }
    }

    private void SelectPreset(string presetName)
    {
        _preset = presetName;
        var preset = ResolutionPresets.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
        if (!string.Equals(presetName, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            _width = preset.Width;
            _height = preset.Height;
        }
        Refresh(force: true);
    }

    private void Refresh(bool force = false)
    {
        double elapsed = Math.Max(0d, GetNowSeconds() - _jobStartedAtSeconds);
        string signature = $"{_mode}|{_width}|{_height}|{_framerate}|{_bitrateKbps}|{_highQuality}|{_imageFormat}|{_videoFormat}|{_preset}|" +
            $"{_jobActive}|{_jobFinished}|{_jobStatus}|{_frameCurrent}|{_frameTotal}|{elapsed:F0}";
        if (!force && signature == _lastRenderedSignature)
            return;
        _lastRenderedSignature = signature;

        bool controlsDisabled = _jobActive;

        var html = new StringBuilder("""
            <div id="render-scroll">
            """);

        html.Append("<p class='render-label'>Output Type</p>");
        AppendButtons(html, "render-mode", ["Image", "Video"], _mode == RenderMode.Video ? "Video" : "Image", controlsDisabled);

        html.Append("<p class='render-label'>Resolution Preset</p>");
        AppendButtons(html, "render-preset", ResolutionPresets.Select(p => p.Name).ToArray(), _preset, controlsDisabled);

        html.Append("<p class='render-label'>Width</p>")
            .Append("<input id='render-width' class='render-dim' value='").Append(_width).Append("'/>")
            .Append("<p class='render-label'>Height</p>")
            .Append("<input id='render-height' class='render-dim' value='").Append(_height).Append("'/>");

        html.Append("<p class='render-label'>Quality</p>");
        AppendButtons(html, "render-highquality", ["Rendered (high quality)", "Unrendered (fast)"],
            _highQuality ? "Rendered (high quality)" : "Unrendered (fast)", controlsDisabled);

        html.Append("<p class='render-label'>File Format</p>");
        if (_mode == RenderMode.Image)
            AppendButtons(html, "render-format", ["png", "jpg", "webp"], _imageFormat, controlsDisabled);
        else
            AppendButtons(html, "render-format", ["mp4", "webm"], _videoFormat, controlsDisabled);

        if (_mode == RenderMode.Video)
        {
            html.Append("<p class='render-label'>Framerate</p>");
            AppendButtons(html, "render-framerate", FramerateOptions.Select(f => $"{f} fps").ToArray(), $"{_framerate} fps", controlsDisabled);

            html.Append("<p class='render-label'>Bitrate</p>");
            AppendButtons(html, "render-bitrate", BitrateOptionsKbps.Select(b => $"{b} kbps").ToArray(), $"{_bitrateKbps} kbps", controlsDisabled);
        }

        if (_jobActive || _jobFinished)
        {
            float progress = _frameTotal <= 0 ? 0f : Math.Clamp(_frameCurrent / (float)_frameTotal, 0f, 1f);
            html.Append("<hr/><p class='render-label'>Progress</p>")
                .Append("<p>").Append(Escape(_jobStatus)).Append("</p>")
                .Append("<div id='render-progress-bar'><div id='render-progress-fill' style='width:")
                .Append((progress * 100f).ToString("F1", CultureInfo.InvariantCulture)).Append("%;'/></div>")
                .Append("<p>").Append((progress * 100f).ToString("F1", CultureInfo.InvariantCulture)).Append("%</p>")
                .Append("<p>Frames: ").Append(_frameCurrent).Append('/').Append(_frameTotal).Append("</p>")
                .Append("<p>Elapsed: ").Append(elapsed.ToString("F1", CultureInfo.InvariantCulture)).Append("s</p>");
        }

        html.Append("</div><div id='render-footer'>");
        html.Append("<button id='render-start'").Append(controlsDisabled ? " style='display:none;'" : "").Append(">Start Render</button>");
        html.Append("<button id='render-abort'").Append(_jobActive ? "" : " style='display:none;'").Append(">Abort</button>");
        html.Append("<button id='render-close'").Append(_jobActive ? " style='display:none;'" : "").Append('>')
            .Append(_jobFinished ? "Close" : "Cancel").Append("</button>");
        html.Append("</div>");

        _root.SetInnerRml(html.ToString());

        BindChoices("render-mode", ["Image", "Video"], value =>
        {
            _mode = value == "Video" ? RenderMode.Video : RenderMode.Image;
            Refresh(force: true);
        });
        BindChoices("render-preset", ResolutionPresets.Select(p => p.Name).ToArray(), SelectPreset);
        BindChoices("render-highquality", ["Rendered (high quality)", "Unrendered (fast)"], value =>
        {
            _highQuality = value == "Rendered (high quality)";
            Refresh(force: true);
        });
        if (_mode == RenderMode.Image)
        {
            BindChoices("render-format", ["png", "jpg", "webp"], value =>
            {
                _imageFormat = value;
                Refresh(force: true);
            });
        }
        else
        {
            BindChoices("render-format", ["mp4", "webm"], value =>
            {
                _videoFormat = value;
                Refresh(force: true);
            });
            BindChoices("render-framerate", FramerateOptions.Select(f => $"{f} fps").ToArray(), value =>
            {
                _framerate = int.Parse(value.Split(' ')[0], CultureInfo.InvariantCulture);
                Refresh(force: true);
            });
            BindChoices("render-bitrate", BitrateOptionsKbps.Select(b => $"{b} kbps").ToArray(), value =>
            {
                _bitrateKbps = int.Parse(value.Split(' ')[0], CultureInfo.InvariantCulture);
                Refresh(force: true);
            });
        }

        if (_root.GetElementById("render-width") is ElementFormControlInput widthInput)
            widthInput.AddEventListener("change", _ =>
            {
                if (int.TryParse(widthInput.GetValue(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    _width = Math.Max(1, value);
                    _preset = "Custom";
                }
                Refresh(force: true);
            });
        if (_root.GetElementById("render-height") is ElementFormControlInput heightInput)
            heightInput.AddEventListener("change", _ =>
            {
                if (int.TryParse(heightInput.GetValue(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    _height = Math.Max(1, value);
                    _preset = "Custom";
                }
                Refresh(force: true);
            });

        _root.GetElementById("render-start")?.AddEventListener("click", _ => StartRender());
        _root.GetElementById("render-abort")?.AddEventListener("click", _ => CancelRender());
        _root.GetElementById("render-close")?.AddEventListener("click", _ =>
        {
            _jobFinished = false;
            Hide();
        });
    }

    private static void AppendButtons(StringBuilder html, string prefix, IReadOnlyList<string> values, string? selected, bool disabled)
    {
        html.Append("<div class='render-choices'>");
        for (int i = 0; i < values.Count; i++)
        {
            html.Append("<button id='").Append(prefix).Append('-').Append(i).Append("' class='")
                .Append(values[i] == selected ? "selected" : "").Append('\'')
                .Append(disabled ? " disabled" : "").Append('>')
                .Append(Escape(values[i])).Append("</button>");
        }
        html.Append("</div>");
    }

    private void BindChoices(string prefix, IReadOnlyList<string> values, Action<string> select)
    {
        for (int i = 0; i < values.Count; i++)
        {
            string value = values[i];
            _root.GetElementById($"{prefix}-{i}")?.AddEventListener("click", _ => select(value));
        }
    }

    private static double GetNowSeconds() => DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;

    private static string Escape(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
