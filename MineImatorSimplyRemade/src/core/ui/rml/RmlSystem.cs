using System.Diagnostics;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

public sealed class RmlSystem : SystemInterface
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string _clipboard = string.Empty;

    public override double ElapsedTime => _clock.Elapsed.TotalSeconds;
    public override void SetClipboardText(string text) => _clipboard = text;
    public override string GetClipboardText() => _clipboard;
    public override void ActivateKeyboard(float caretX, float caretY, float lineHeight) { }
    public override void DeactivateKeyboard() { }
    public override bool LogMessage(LogType type, string message)
    {
        if (type is LogType.Error or LogType.Assert or LogType.Warning)
            Console.Error.WriteLine($"[RmlUi:{type}] {message}");
        return true;
    }
}
