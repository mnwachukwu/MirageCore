using System.Text;

namespace Mirage.Server.Host.Management;

/// <summary>
/// A <see cref="TextWriter"/> that passes everything through to the real stdout and also hands each
/// completed line to subscribers.
///
/// <para>Installed with <see cref="Console.SetOut"/> before Serilog is configured, so it catches BOTH the
/// log pipeline and the direct <c>Console.WriteLine</c> calls the console commands make. A Serilog sink
/// would only ever have seen the first, which would have made <c>/who</c> print nothing to a remote
/// operator.</para>
/// </summary>
public sealed class ConsoleTee(TextWriter inner) : TextWriter
{
    private readonly StringBuilder _pending = new();

    /// <summary>One completed line, without its newline. Raised on whichever thread wrote it, so a
    /// handler must not block — the writer here can be the game thread.</summary>
    public event Action<string>? LineWritten;

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        inner.Write(value);
        Accumulate(value);
    }

    public override void Write(string? value)
    {
        if (value is null) return;
        inner.Write(value);
        foreach (char c in value) Accumulate(c);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        inner.Write(buffer, index, count);
        for (int i = 0; i < count; i++) Accumulate(buffer[index + i]);
    }

    public override void Flush() => inner.Flush();

    // Split on '\n' and drop a preceding '\r', so a line reads the same whichever newline the platform
    // uses. Callers are line-oriented; a partial line waits here for the rest of itself.
    private void Accumulate(char c)
    {
        if (c != '\n') { _pending.Append(c); return; }

        if (_pending.Length > 0 && _pending[^1] == '\r') _pending.Length--;
        string line = _pending.ToString();
        _pending.Clear();
        LineWritten?.Invoke(line);
    }
}
