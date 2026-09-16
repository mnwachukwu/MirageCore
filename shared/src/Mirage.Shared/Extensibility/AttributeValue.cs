namespace Mirage.Shared.Extensibility;

/// <summary>What kind of number, flag, or word an <see cref="AttributeValue"/> is carrying.</summary>
public enum AttributeKind : byte
{
    Integer = 0,
    Real = 1,
    Flag = 2,
    Text = 3,

    /// <summary>Several of one of the four above. <see cref="AttributeValue.Of"/> says which, and every
    /// member is that kind — a set cannot hold a number beside a word.</summary>
    Set = 4,
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
    private AttributeValue(AttributeKind kind, long integer, double real, string? text,
                          AttributeKind of = AttributeKind.Integer, AttributeValue[]? set = null)
    {
        Kind = kind;
        _integer = integer;
        _real = real;
        _text = text;
        Of = of;
        _set = set;
    }

    /// <summary>What was written. The only way to tell an Integer from a Real, since every accessor
    /// reads both.</summary>
    public AttributeKind Kind { get; }

    /// <summary>For a <see cref="AttributeKind.Set"/>, what kind its members are. Integer for
    /// everything else, where it means nothing and is never read.</summary>
    public AttributeKind Of { get; }

    private readonly long _integer;
    private readonly double _real;
    private readonly string? _text;
    private readonly AttributeValue[]? _set;

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

    // ── Making a set ──────────────────────────────────────────────────────────
    //
    // 🔴 One factory per member kind, rather than one that takes a kind and a pile of values. A set
    // cannot be built mixed, because there is no way to hand these a value of the wrong kind - the
    // invariant is the signature rather than a check that could be skipped.

    public static AttributeValue From(IEnumerable<long> values) =>
        Gathered(AttributeKind.Integer, values, From);

    public static AttributeValue From(IEnumerable<int> values) =>
        Gathered(AttributeKind.Integer, values, v => From((long)v));

    public static AttributeValue From(IEnumerable<double> values) =>
        Gathered(AttributeKind.Real, values, From);

    public static AttributeValue From(IEnumerable<bool> values) =>
        Gathered(AttributeKind.Flag, values, From);

    public static AttributeValue From(IEnumerable<string> values) =>
        Gathered(AttributeKind.Text, values, v => From(v));

    private static AttributeValue Gathered<T>(AttributeKind of, IEnumerable<T> values,
                                              Func<T, AttributeValue> one)
    {
        ArgumentNullException.ThrowIfNull(values);

        return new AttributeValue(AttributeKind.Set, 0, 0, null, of, [.. values.Select(one)]);
    }

    /// <summary>An empty set of that kind. What a game writes to say "none of them" rather than leaving
    /// the key off, which would read as a question nobody answered.</summary>
    public static AttributeValue EmptySet(AttributeKind of) =>
        new(AttributeKind.Set, 0, 0, null, of, []);

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

    // ── Reading a set ─────────────────────────────────────────────────────────

    /// <summary>How many members it holds. Zero for anything that is not a set, so a caller can ask
    /// this of any value without checking the kind first.</summary>
    public int Count => _set?.Length ?? 0;

    /// <summary>Its members, in the order they were given. Empty for anything that is not a set.</summary>
    public IReadOnlyList<AttributeValue> Members => _set ?? [];

    /// <summary>Whether it holds that whole number. False for anything that is not a set.
    ///
    /// <para>⚠ Compared as a NUMBER rather than by kind, so a set of whole numbers answers the same
    /// question whether the file wrote them 1 or 1.0. A set of words compares as text.</para></summary>
    public bool Has(long value) =>
        Of == AttributeKind.Text
            ? Has(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : _set is { } set && Array.Exists(set, m => m.AsLong() == value);

    /// <summary>Whether it holds that word, compared exactly. False for anything that is not a set.</summary>
    public bool Has(string value) =>
        _set is { } set && Array.Exists(set, m => string.Equals(m.AsText(), value, StringComparison.Ordinal));

    /// <summary>Its members as whole numbers. Empty for anything that is not a set.</summary>
    public IEnumerable<long> AsLongs() => Members.Select(m => m.AsLong());

    /// <summary>Its members as words. Empty for anything that is not a set.</summary>
    public IEnumerable<string> AsTexts() => Members.Select(m => m.AsText());

    // ── Comparing ─────────────────────────────────────────────────────────────
    //
    // ⚠ Written out because a set holds an ARRAY, and the compiler's own equality for a record
    // struct compares that by reference - so two bags built from the same file would differ, and the
    // editor's save comparison decides whether a record is dirty by exactly that test. Every other kind
    // keeps the field-by-field answer it already had.

    public bool Equals(AttributeValue other)
    {
        if (Kind != other.Kind) return false;
        if (Kind != AttributeKind.Set)
            return _integer == other._integer && _real.Equals(other._real)
                   && string.Equals(_text, other._text, StringComparison.Ordinal);

        if (Of != other.Of || Count != other.Count) return false;

        for (int i = 0; i < Count; i++)
        {
            if (!Members[i].Equals(other.Members[i])) return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        if (Kind != AttributeKind.Set) return HashCode.Combine(Kind, _integer, _real, _text);

        var code = new HashCode();
        code.Add(Kind);
        code.Add(Of);
        foreach (AttributeValue member in Members) code.Add(member);

        return code.ToHashCode();
    }

    /// <summary>A set reads as its members between brackets, so a value logged or shown in a form says
    /// what it holds rather than its type name.</summary>
    public override string ToString() =>
        Kind == AttributeKind.Set ? "[" + string.Join(", ", AsTexts()) + "]" : AsText();
}
