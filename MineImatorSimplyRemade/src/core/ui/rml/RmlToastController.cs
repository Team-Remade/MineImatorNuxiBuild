using System.Diagnostics;
using System.Net;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Retained-mode replacement for MainWindow's ImGui-drawn toast notifications
/// (see ShowToast/RenderToastWindow in the old ImGui MainWindow).</summary>
public sealed class RmlToastController
{
    public enum ToastKind
    {
        Success,
        Error
    }

    private readonly Element _toast;
    private readonly Element _text;
    private string _message = string.Empty;
    private ToastKind _kind = ToastKind.Success;
    private double _expiresAtSeconds;
    private bool _visible;

    public RmlToastController(Element toast, Element text)
    {
        _toast = toast;
        _text = text;
    }

    public void ShowSuccess(string message) => Show(message, ToastKind.Success, 2.4);

    public void ShowError(string message) => Show(message, ToastKind.Error, 4.0);

    public void Show(string message, ToastKind kind, double durationSeconds)
    {
        _message = message;
        _kind = kind;
        _expiresAtSeconds = GetNowSeconds() + durationSeconds;
        Refresh();
    }

    /// <summary>Must be called every frame to hide the toast once it expires.</summary>
    public void Update()
    {
        if (!_visible)
            return;

        if (GetNowSeconds() >= _expiresAtSeconds)
        {
            _message = string.Empty;
            Refresh();
        }
    }

    private void Refresh()
    {
        bool show = !string.IsNullOrWhiteSpace(_message);
        if (show == _visible && !show)
            return;

        _visible = show;
        _toast.SetProperty("display", show ? "block" : "none");

        if (!show)
            return;

        _toast.RemoveClass("success");
        _toast.RemoveClass("error");
        _toast.AddClass(_kind == ToastKind.Error ? "error" : "success");
        _text.SetInnerRml(WebUtility.HtmlEncode(_message));
    }

    private static double GetNowSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}
