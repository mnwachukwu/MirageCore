using Serilog.Core;
using Serilog.Events;

namespace Mirage.Client.Core.Diagnostics;

/// <summary>
/// A Serilog sink that keeps the recent log in memory for the in-game console to draw.
///
/// <para>It receives every event the file does and holds the last <see cref="Capacity"/> of them, so
/// opening the console shows what already happened rather than starting a fresh recording. The console
/// being closed changes nothing here — by the time somebody thinks to look, the interesting lines have
/// been written.</para>
///
/// <para>🔴 <b>These lines are shown on screen.</b> A player can open the console, and anything logged
/// is therefore visible to them and screenshottable. A message that would be fine in a file on their own
/// machine is not automatically fine here: never log a credential, a session token, a server-side
/// formula input, or anything else the client is not otherwise told. What the client already knows and
/// displays is safe; what it merely passes through is not.</para>
///
/// <para>Emit is called from whichever thread logged — packet handling runs off the game thread — while
/// the console reads on the game thread, so the buffer is locked on both sides. Formatting happens here
/// rather than at draw time so the cost lands on the event, not on every frame the console is open.</para>
/// </summary>
public sealed class ConsoleSink : ILogEventSink
{
    /// <summary>Lines held for the console. Enough to cover a session's worth of real events without
    /// being a memory concern at a few hundred bytes each.</summary>
    public const int Capacity = 500;

    /// <summary>One formatted line, with the level it came in at so the console can color it.</summary>
    public readonly record struct Line(DateTimeOffset At, LogEventLevel Level, string Text);

    private readonly object _gate = new();
    private readonly Queue<Line> _lines = new(Capacity);

    /// <summary>Raised for each event, on the thread that logged it. The console appends from this so an
    /// open console updates live rather than re-reading the whole buffer every frame.</summary>
    public event Action<Line>? Written;

    public void Emit(LogEvent logEvent)
    {
        var line = new Line(logEvent.Timestamp, logEvent.Level, Format(logEvent));

        lock (_gate)
        {
            if (_lines.Count >= Capacity) _lines.Dequeue();
            _lines.Enqueue(line);
        }

        try { Written?.Invoke(line); }
        catch { /* a console that throws must never take the client with it */ }
    }

    /// <summary>Everything held, oldest first. Read once when the console opens.</summary>
    public IReadOnlyList<Line> Snapshot()
    {
        lock (_gate) return _lines.ToArray();
    }

    /// <summary>The message with its exception folded in, since the console has no second column to put
    /// one in and an exception with no message is the half that matters.</summary>
    private static string Format(LogEvent e)
    {
        string message = e.RenderMessage();
        return e.Exception is null ? message : $"{message} — {e.Exception.GetType().Name}: {e.Exception.Message}";
    }
}
