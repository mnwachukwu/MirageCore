namespace Mirage.Shared.Extensibility;

/// <summary>What kind of number, flag, or word an <see cref="AttributeValue"/> is carrying.</summary>
public enum AttributeKind : byte
{
    Integer = 0,
    Real = 1,
    Flag = 2,
    Text = 3,
}

/// <summary>
/// One value in an <see cref="AttributeBag"/> — a number, a flag, or a word that the engine stores,
/// persists, and ships, and never interprets.
///
/// <para><b>Four kinds, not one.</b> A game reaching for a bag entry is as likely to want a species
/// name or an is-shiny flag as a hit-point count, and a single numeric kind would encode the other two
/// as magic numbers. The four are closed and compare by value, which is what the editor's
/// save-packet comparison needs of anything a record holds.</para>
///
/// <para><b>The accessors coerce rather than throw.</b> A bag is authored data, and authored data is
/// edited by hand; a field written <c>45</c> where the game expected <c>45.0</c> is a typo the game
/// should never see. <see cref="AsDouble"/> reads an Integer, <see cref="AsLong"/> reads a whole Real,
/// and both read a Flag as 0 or 1. Only <see cref="Kind"/> tells you what was actually written.</para>
///
/// <para><b>On the wire and on disk it is the natural JSON value</b> — a number, a bool, or a string,
/// with no kind tag beside it. So a world file reads <c>"baseHp": 45</c> rather than
/// <c>"baseHp": { "kind": "Integer", "value": 45 }</c>, so a world file stays legible to the person
/// editing it. Integer and Real therefore share one JSON number, and a value written <c>5.0</c> reads
/// back as Integer 5 — invisible through the coercing accessors, and visible through
/// <see cref="Kind"/>.</para>
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(AttributeValueConverter))]
public readonly record struct AttributeValue
{
    private AttributeValue(AttributeKind kind, long integer, double real, string? text)
    {
        Kind = kind;
        _integer = integer;
        _real = real;
        _text = text;
    }

    /// <summary>What was written. The only way to tell an Integer from a Real, since every accessor
    /// reads both.</summary>
    public AttributeKind Kind { get; }

    private readonly long _integer;
    private readonly double _real;
    private readonly string? _text;

    // ── Making one ────────────────────────────────────────────────────────────

    public static AttributeValue From(long value) => new(AttributeKind.Integer, value, value, null);
    public static AttributeValue From(int value) => From((long)value);
    public static AttributeValue From(double value) => new(AttributeKind.Real, (long)value, value, null);
    public static AttributeValue From(bool value) => new(AttributeKind.Flag, value ? 1 : 0, value ? 1 : 0, null);
    public static AttributeValue From(string? value) => new(AttributeKind.Text, 0, 0, value ?? string.Empty);

    public static implicit operator AttributeValue(long value) => From(value);
    public static implicit operator AttributeValue(int value) => From(value);
    public static implicit operator AttributeValue(double value) => From(value);
    public static implicit operator AttributeValue(bool value) => From(value);
    public static implicit operator AttributeValue(string? value) => From(value);

    // ── Reading one ───────────────────────────────────────────────────────────

    /// <summary>As a whole number. A Real is truncated, a Flag is 0 or 1, and Text parses or yields 0.</summary>
    public long AsLong() => Kind switch
    {
        AttributeKind.Integer or AttributeKind.Flag => _integer,
        AttributeKind.Real => (long)_real,
        _ => long.TryParse(_text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long parsed) ? parsed : 0,
    };

    /// <summary>As a number that may have a fraction. An Integer widens exactly, a Flag is 0 or 1, and
    /// Text parses or yields 0.</summary>
    public double AsDouble() => Kind switch
    {
        AttributeKind.Real => _real,
        AttributeKind.Integer or AttributeKind.Flag => _integer,
        _ => double.TryParse(_text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed) ? parsed : 0,
    };

    /// <summary>As a yes or no. Every numeric kind is false only at zero; Text is true unless it is
    /// empty or spells a negative — "false", "no", "0".</summary>
    public bool AsBool() => Kind switch
    {
        AttributeKind.Flag or AttributeKind.Integer => _integer != 0,
        AttributeKind.Real => _real != 0,
        _ => !string.IsNullOrWhiteSpace(_text)
             && !_text.Equals("false", StringComparison.OrdinalIgnoreCase)
             && !_text.Equals("no", StringComparison.OrdinalIgnoreCase)
             && !_text.Equals("0", StringComparison.Ordinal),
    };

    /// <summary>As text. Numbers format invariantly, so a value that round-trips through here is the
    /// same value on every machine.</summary>
    public string AsText() => Kind switch
    {
        AttributeKind.Text => _text ?? string.Empty,
        AttributeKind.Flag => _integer != 0 ? "true" : "false",
        AttributeKind.Real => _real.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => _integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public override string ToString() => AsText();
}
