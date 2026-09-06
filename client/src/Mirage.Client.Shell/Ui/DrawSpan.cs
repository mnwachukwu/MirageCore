using Mirage.Shared;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// One styled run inside a single visual line of a <see cref="TextArea"/> — a hyperlink, a player name, or
/// a plain recolored substring. The three kinds are merged into one list and drawn in column order, which
/// is why they share a shape rather than being drawn in three passes.
///
/// <para>Each kind uses only some of the fields, so build them through the factories below rather than the
/// constructor. As a seven-element tuple this read
/// <c>spans.Add((c.StartCol, c.StartCol + c.Length, SpanKindColor, "", default, false, c.ColorIndex))</c> —
/// four of the seven values being padding that existed only to satisfy the shape, and the two `int`s at the
/// front transposable without complaint.</para>
/// </summary>
public readonly record struct DrawSpan
{
    public enum SpanKind { Link, Name, Color }

    /// <summary>First column of the run, inclusive.</summary>
    public int Start { get; init; }
    /// <summary>One past the last column of the run.</summary>
    public int End { get; init; }
    public SpanKind Kind { get; init; }
    /// <summary>The URL for a link, the player name for a name, unused for a color run — it is what the
    /// hit-rect carries so a click knows what it hit.</summary>
    public string Payload { get; init; }
    /// <summary>Name runs only: drives the name color alongside <see cref="ShowAsPk"/>.</summary>
    public AdminLevel Access { get; init; }
    /// <summary>Name runs only.</summary>
    public bool ShowAsPk { get; init; }
    /// <summary>Color runs only: an index into the shared game palette.</summary>
    public int ColorIndex { get; init; }

    public static DrawSpan Link(int startCol, int length, string url) =>
        new() { Start = startCol, End = startCol + length, Kind = SpanKind.Link, Payload = url };

    public static DrawSpan Name(int startCol, int length, string name, AdminLevel access, bool showAsPk) =>
        new() { Start = startCol, End = startCol + length, Kind = SpanKind.Name, Payload = name, Access = access, ShowAsPk = showAsPk };

    public static DrawSpan Color(int startCol, int length, int colorIndex) =>
        new() { Start = startCol, End = startCol + length, Kind = SpanKind.Color, Payload = "", ColorIndex = colorIndex };
}
