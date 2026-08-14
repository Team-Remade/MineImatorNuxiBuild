using System.Net;
using MineImatorSimplyRemade.core.startup;
using MineImatorSimplyRemade.core.ui.rml;
using RmlUiNet;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.window.windows;

public sealed class StartupProgressWindow : Window
{
    public StartupProgressState ProgressState { get; } = new();

    private readonly DateTime _startedAt = DateTime.UtcNow;
    private RmlWindowHost? _rml;
    private ElementDocument? _document;
    private Element? _title;
    private Element? _step;
    private Element? _phase;
    private Element? _progress;
    private Element? _percent;
    private Element? _working;
    private Element? _status;
    private Element? _detail;

    public StartupProgressWindow(int width, int height, string title, Glfw glfw, GL gl = null)
        : base(width, height, title, glfw, gl) { }

    public unsafe void SetupRml()
    {
        _rml = new RmlWindowHost(Glfw, windowHandle, GL, WindowWidth, WindowHeight, "startup");
        _document = _rml.LoadDocument(DocumentRml);
        _title = _document.GetElementById("title");
        _step = _document.GetElementById("step");
        _phase = _document.GetElementById("phase");
        _progress = _document.GetElementById("progress");
        _percent = _document.GetElementById("percent");
        _working = _document.GetElementById("working");
        _status = _document.GetElementById("status");
        _detail = _document.GetElementById("detail");
    }

    public override unsafe void Render()
    {
        if (_rml == null) throw new InvalidOperationException("SetupRml must be called before rendering.");
        Glfw.MakeContextCurrent(windowHandle);
        GL.Viewport(0, 0, (uint)Math.Max(1, WindowWidth), (uint)Math.Max(1, WindowHeight));
        GL.ClearColor(ClearR, ClearG, ClearB, ClearA);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        UpdateDocument();
        _rml.Render(WindowWidth, WindowHeight);
        Glfw.SwapBuffers(windowHandle);
    }

    private void UpdateDocument()
    {
        float value = Math.Clamp(ProgressState.Progress, 0f, 1f);
        string step = ProgressState.TotalSteps > 0
            ? $"Step {Math.Clamp(ProgressState.CurrentStep, 1, ProgressState.TotalSteps)}/{ProgressState.TotalSteps}"
            : "Startup";
        int dots = (int)((DateTime.UtcNow - _startedAt).TotalSeconds * 2) % 4;

        SetText(_title, ProgressState.Title);
        SetText(_step, step);
        SetText(_phase, ProgressState.Phase);
        _progress?.SetAttribute("value", value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
        SetText(_percent, $"{MathF.Round(value * 100f)}%");
        SetText(_working, "Working" + new string('.', dots));
        SetText(_status, ProgressState.Status);
        SetText(_detail, ProgressState.Detail);
        _detail?.SetProperty("display", string.IsNullOrWhiteSpace(ProgressState.Detail) ? "none" : "block");
    }

    private static void SetText(Element? element, string? value) =>
        element?.SetInnerRml(WebUtility.HtmlEncode(value ?? string.Empty));

    public override void Dispose()
    {
        _rml?.Dispose();
        _rml = null;
        base.Dispose();
    }

    private const string DocumentRml = """
        <rml>
        <head>
          <style>
            body { width: 100%; height: 100%; margin: 0; padding: 20px 22px; box-sizing: border-box;
                   background: #1c1c21; color: #e8e8ec; font-family: "Noto Sans"; font-size: 14px; }
            #header { display: flex; flex-direction: row; align-items: center; padding-bottom: 10px;
                      border-bottom: 1px #45454d; }
            #title { flex: 1; color: #ebbd4f; font-size: 20px; font-weight: bold; }
            #step { color: #a8a8b0; }
            #phase { margin-top: 15px; margin-bottom: 10px; font-size: 16px; }
            #bar-row { display: flex; flex-direction: row; align-items: center; }
            progress { flex: 1; height: 16px; color: #d8a83e; background-color: #35353c; }
            #percent { width: 52px; margin-left: 10px; text-align: right; color: #c6c6cc; }
            #working { margin-top: 11px; color: #d5d5da; }
            #status { margin-top: 3px; white-space: normal; }
            #detail { margin-top: 7px; color: #b2bdcc; white-space: normal; }
          </style>
        </head>
        <body>
          <div id="header"><div id="title"></div><div id="step"></div></div>
          <div id="phase"></div>
          <div id="bar-row"><progress id="progress" value="0" max="1"/><div id="percent">0%</div></div>
          <div id="working">Working</div>
          <div id="status"></div>
          <div id="detail"></div>
        </body>
        </rml>
        """;
}
