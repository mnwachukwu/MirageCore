using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>How a <see cref="DisplayRow"/> is drawn.</summary>
public enum DisplayStyle : byte
{
    /// <summary>A caption and a value, side by side.</summary>
    Text = 0,

    /// <summary>A filled bar. Reads <see cref="DisplayRow.Value"/> against
    /// <see cref="DisplayRow.Max"/>.</summary>
    Meter = 1,

    /// <summary>A small colored tag with no caption — a status, a state word.</summary>
    Badge = 2,

    /// <summary>A heading that separates the rows under it. Carries no value.</summary>
    Heading = 3,
}

/// <summary>
/// One labeled value, ready to be drawn and carrying no opinion about where.
///
/// <para><b>The engine decides the place; the game decides the content.</b> The same row is a line in
/// a heads-up display, a line in a tooltip, a line in an authoring preview, a line in an operator's
/// grid, or a line of text over a head. None of those is named here, which is what lets a game add a
/// value to all of them at once.</para>
/// </summary>
public readonly record struct DisplayRow
{
    /// <summary>Localization key for the caption, or null for a row that shows only its value.</summary>
    [JsonPropertyName("labelKey")] public string? LabelKey { get; init; }

    /// <summary>The value as it should read. Already formatted — the game owns how its own numbers are
    /// written, including how many decimals and whether a thousand has a separator.</summary>
    [JsonPropertyName("text")] public string Text { get; init; }

    /// <summary>For <see cref="DisplayStyle.Meter"/>: how full the bar is.</summary>
    [JsonPropertyName("value")] public double Value { get; init; }

    /// <summary>For <see cref="DisplayStyle.Meter"/>: what full means. A zero or negative
    /// <see cref="Max"/> draws an empty bar rather than dividing by it.</summary>
    [JsonPropertyName("max")] public double Max { get; init; }

    [JsonPropertyName("color")] public int Color { get; init; }

    [JsonPropertyName("style")] public DisplayStyle Style { get; init; }

    /// <summary>How full, from 0 to 1. Zero when <see cref="Max"/> is not positive.</summary>
    [JsonIgnore]
    public double Fill => Max > 0 ? Math.Clamp(Value / Max, 0, 1) : 0;

    public static DisplayRow OfText(string? labelKey, string text, int color = GameColor.White)
        => new() { LabelKey = labelKey, Text = text, Color = color, Style = DisplayStyle.Text };

    public static DisplayRow OfMeter(string? labelKey, double value, double max, int color)
        => new()
        {
            LabelKey = labelKey,
            Text = $"{value:0}/{max:0}",
            Value = value,
            Max = max,
            Color = color,
            Style = DisplayStyle.Meter,
        };

    public static DisplayRow OfBadge(string text, int color)
        => new() { Text = text, Color = color, Style = DisplayStyle.Badge };

    public static DisplayRow OfHeading(string labelKey)
        => new() { LabelKey = labelKey, Text = string.Empty, Style = DisplayStyle.Heading };
}

/// <summary>
/// An ordered set of <see cref="DisplayRow"/> for one subject, named so a surface can ask for the part
/// it has room for.
/// </summary>
/// <param name="Surface">What asked — a heads-up display, a tooltip, an authoring preview. A game
/// returning rows may vary them by this, and returning the same rows for every surface is
/// fine.</param>
/// <param name="Rows">What to draw, in order.</param>
public readonly record struct DisplayProjection(string Surface, IReadOnlyList<DisplayRow> Rows)
{
    /// <summary>Nothing to draw. What a game with no opinion about a subject returns, and what every
    /// surface renders as empty rather than as a gap.</summary>
    public static DisplayProjection Nothing(string surface) => new(surface, Array.Empty<DisplayRow>());

    public bool IsEmpty => Rows.Count == 0;
}
