using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// One filled row over a body's head: an attribute to read, an attribute to measure it against, and
/// the color to draw it in.
///
/// <para><b>Core knows that bodies have bars; it does not know what they measure.</b> Health, fuel,
/// hunger, a captured pokémon's tameness, the charge left on a spacesuit — which bars exist and what
/// they read is a game's decision, and there is nothing in the engine that could make it. What the
/// engine owns is the part that is the same everywhere: a row is a fraction, the fraction comes from
/// values the body already carries, and a body carrying neither shows nothing.</para>
///
/// <para><b>It reads, it never writes.</b> A bar is a view of an <see cref="AttributeBag"/>, so a game
/// changes what is drawn by changing the values through <see cref="IWorld.SetAttribute"/> — there is no
/// separate bar state to keep in step, and no way for the two to disagree.</para>
/// </summary>
public sealed record OverheadBar
{
    /// <summary>The attribute holding how much there is now.</summary>
    [JsonPropertyName("valueKey")] public string ValueKey { get; init; } = string.Empty;

    /// <summary>The attribute holding how much there can be. A body whose maximum is absent or zero
    /// shows no row for this bar, which is how a game turns one off for a body that has no business
    /// with it.</summary>
    [JsonPropertyName("maxKey")] public string MaxKey { get; init; } = string.Empty;

    /// <summary>The fill color, packed <c>0xRRGGBB</c>.</summary>
    [JsonPropertyName("rgb")] public int Rgb { get; init; }

    /// <summary>Where this sits among the others. Lower draws higher; equal values keep declaration
    /// order, so a game that does not care may leave them all at zero.</summary>
    [JsonPropertyName("ordinal")] public int Ordinal { get; init; }

    /// <summary>How full this bar is for a body carrying <paramref name="bag"/>, from 0 to 1 — or a
    /// negative number when this body has nothing to say about it and the row should not be drawn.
    ///
    /// <para>Out-of-range values are clamped rather than refused: a game that overheals past its own
    /// maximum for a moment has a full bar, not a bar that disappears.</para></summary>
    public float FractionIn(AttributeBag? bag)
    {
        if (bag is null) return Absent;
        if (!bag.TryGet(ValueKey, out var value) || !bag.TryGet(MaxKey, out var max)) return Absent;

        double ceiling = max.AsDouble();
        return ceiling > 0 ? (float)Math.Clamp(value.AsDouble() / ceiling, 0d, 1d) : Absent;
    }

    /// <summary>The fraction of a row that is not there. Negative, so every "is this drawn" test is the
    /// same comparison whether the row is a bar or the engine's own cooldown.</summary>
    public const float Absent = -1f;
}

/// <summary>
/// The bars a world draws over a head, in draw order.
///
/// <para><b>Three at most, and the limit is the space.</b> A row is four pixels over a sprite that is
/// thirty-two tall; a fourth would push the name off the top of a body standing near the ceiling of
/// the viewport, and nothing about a fourth bar is readable at that size anyway. A game with more
/// than three things to show has a panel for them.</para>
/// </summary>
public sealed class OverheadBarSet
{
    /// <summary>A world that draws nothing over a head. What Core describes before a game declares a
    /// bar.</summary>
    public static readonly OverheadBarSet Empty = new([]);

    /// <summary>How many rows a game may declare.</summary>
    public const int Max = 3;

    public OverheadBarSet(IReadOnlyList<OverheadBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        Bars = [.. bars.OrderBy(b => b.Ordinal)];
    }

    /// <summary>Every bar, in the order the rows should be drawn.</summary>
    public IReadOnlyList<OverheadBar> Bars { get; }

    public int Count => Bars.Count;

    /// <summary>The bar at <paramref name="index"/>, or null past the end. Asked by a renderer walking
    /// a fixed three rows against a set that may hold fewer.</summary>
    public OverheadBar? At(int index) => index >= 0 && index < Bars.Count ? Bars[index] : null;
}
