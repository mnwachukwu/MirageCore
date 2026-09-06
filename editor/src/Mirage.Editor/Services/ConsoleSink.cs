using Serilog.Core;
using Serilog.Events;

namespace Mirage.Editor.Services;

/// <summary>
/// A Serilog sink that keeps the recent log in memory for the Console window to show.
///
/// <para>It receives whatever the file receives, at whatever level Help &gt; Logging is set to, and holds
/// the last <see cref="Capacity"/> events. The window being closed changes nothing here — by the time
/// somebody opens it, the interesting lines have been written, and a sink that only ran while a window
/// was open would never catch the thing it was opened for.</para>
///
/// <para>The client carries a sink of the same shape for its own console. They are siblings rather than
/// one shared class because sharing would put a Serilog reference into <c>Mirage.Shared</c>, which every
/// project takes, to save forty lines.</para>
///
/// <para><see cref="Emit"/> is called from whichever thread logged — the server connection runs off the
/// UI thread — so the buffer is locked and the event is raised without one. Marshalling to the UI thread
/// is the window's job, not the sink's.</para>
/// </summary>
public sealed class ConsoleSink : ILogEventSink
{
    /// <summary>Events held for the window. Enough to cover a working session at the rate real events
    /// happen, and a few hundred KB at Verbose.</summary>
    public const int Capacity = 1000;

    /// <summary>One rendered event, with the level it came in at so the window can color it.</summary>
    public readonly record struct Line(DateTimeOffset At, LogEventLevel Level, string Text);

    private readonly Lock _gate = new();
    private readonly Queue<Line> _lines = new(Capacity);

    /// <summary>Raised for each event, on the thread that logged it.</summary>
    public event Action<Line>? Written;

    public void Emit(LogEvent logEvent)
    {
        var line = new Line(logEvent.Timestamp, logEvent.Level, Render(logEvent));

        lock (_gate)
        {
            if (_lines.Count >= Capacity) _lines.Dequeue();
            _lines.Enqueue(line);
        }

        try { Written?.Invoke(line); }
        catch { /* a window that throws must never take the editor with it */ }
    }

    /// <summary>Everything held, oldest first. Read once when the window opens.</summary>
    public IReadOnlyList<Line> Snapshot()
    {
        lock (_gate) return [.. _lines];
    }

    /// <summary>Drops everything held. The file is untouched — this clears the view, not the record.</summary>
    public void Clear()
    {
        lock (_gate) _lines.Clear();
    }

    /// <summary>The message with its exception folded in: the window has no second column to put one in,
    /// and an exception with no message is the half that matters.</summary>
    private static string Render(LogEvent e)
    {
        string message = e.RenderMessage();
        return e.Exception is null ? message : $"{message} — {e.Exception.GetType().Name}: {e.Exception.Message}";
    }
}
