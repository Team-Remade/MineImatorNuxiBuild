using System.Text;

namespace MineImatorSimplyRemade.core.log;

/// <summary>
/// A <see cref="TextWriter"/> that forwards everything written through it to an inner writer
/// (e.g. the process's original stdout/stderr) while also invoking a callback once per
/// completed line. Used to transparently mirror <see cref="Console.WriteLine"/> /
/// <see cref="Console.Error"/> output into the log file without requiring any changes to
/// existing call sites throughout the codebase.
/// </summary>
internal sealed class TeeTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly Action<string> _onLine;
    private readonly StringBuilder _buffer = new();

    public TeeTextWriter(TextWriter inner, Action<string> onLine)
    {
        _inner = inner;
        _onLine = onLine;
    }

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(char value)
    {
        _inner.Write(value);

        if (value == '\n')
            FlushBuffer();
        else if (value != '\r')
            _buffer.Append(value);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        _inner.Write(value);

        foreach (char c in value)
        {
            if (c == '\n')
                FlushBuffer();
            else if (c != '\r')
                _buffer.Append(c);
        }
    }

    public override void WriteLine(string? value)
    {
        _inner.WriteLine(value);

        if (!string.IsNullOrEmpty(value))
            _buffer.Append(value);

        FlushBuffer();
    }

    public override void WriteLine()
    {
        _inner.WriteLine();
        FlushBuffer();
    }

    public override void Flush()
    {
        _inner.Flush();
    }

    private void FlushBuffer()
    {
        if (_buffer.Length == 0)
            return;

        string line = _buffer.ToString();
        _buffer.Clear();

        try
        {
            _onLine(line);
        }
        catch
        {
            // Mirroring to the log file must never break console output.
        }
    }
}
