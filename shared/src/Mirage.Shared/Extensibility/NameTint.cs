using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// One rule for the color of the name over a creature's head: an attribute that selects it, and the
/// color a body carrying that attribute is named in.
///
/// <para><b>Core knows that bodies have names; it does not know what a body IS.</b> A shopkeeper, a
/// guard, a thing that will kill you on sight — which of those a creature is, and which of them is
/// worth a warning color, is a game's decision. Core's own creature record says how a body looks, how
/// it moves, and how far it notices, and none of that answers it: an archer holding its distance and a
/// deer holding its distance move identically.</para>
///
/// <para><b>It reads, it never writes.</b> A tint is a view of an <see cref="AttributeBag"/>, so a body
/// that becomes hostile is renamed by the same sync that made it hostile — there is no separate color
/// to keep in step, and no way for the two to disagree.</para>
///
/// <para>⚠ Player names are not tinted this way. What colors those is access level, observer mode, and
/// the PK flag, which are permissions and engine state rather than anything a game declares.</para>
/// </summary>
public sealed record NameTint
{
    /// <summary>The attribute that selects this color. A body whose value for it reads true — see
    /// <see cref="AttributeValue.AsBool"/> — is named in <see cref="Rgb"/>.</summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    /// <summary>The name color, packed <c>0xRRGGBB</c>.</summary>
    [JsonPropertyName("rgb")] public int Rgb { get; init; }

    /// <summary>Where this sits among the others. Lower is asked first; equal values keep declaration
    /// order.
    ///
    /// <para>⚠ <b>The first match wins, so declaration order decides.</b> A guard that is also hostile
    /// is named as whichever of the two a game asked about first, and a game that wants its guards
    /// picked out of its hostiles orders them here.</para></summary>
    [JsonPropertyName("ordinal")] public int Ordinal { get; init; }
}

/// <summary>
/// How a world colors the names over its creatures: the rules, in the order they are asked, and the
/// color for a body that matches none of them.
/// </summary>
public sealed class NameTintSet
{
    /// <summary>White. What a body is named in before a game says otherwise, and what every body is
    /// named in under a game that declares no rule at all.</summary>
    public const int PlainRgb = 0xFFFFFF;

    /// <summary>A world that colors no name by what a body is. What Core describes on its own.</summary>
    public static readonly NameTintSet Plain = new([], PlainRgb);

    /// <summary>How many rules a game may declare. Eight distinct name colors is already more than a
    /// player tells apart at a glance.</summary>
    public const int Max = 8;

    public NameTintSet(IReadOnlyList<NameTint> tints, int otherwiseRgb)
    {
        ArgumentNullException.ThrowIfNull(tints);
        Tints = [.. tints.OrderBy(t => t.Ordinal)];
        OtherwiseRgb = otherwiseRgb;
    }

    /// <summary>Every rule, in the order they are asked.</summary>
    public IReadOnlyList<NameTint> Tints { get; }

    /// <summary>The color for a body carrying none of the rules' attributes.</summary>
    public int OtherwiseRgb { get; }

    public int Count => Tints.Count;

    /// <summary>The color to name a body carrying <paramref name="bag"/>, packed <c>0xRRGGBB</c>.
    ///
    /// <para>A body with no values at all is named in <see cref="OtherwiseRgb"/> rather than being left
    /// uncolored: a creature a game has said nothing about still has a name to draw.</para></summary>
    public int RgbFor(AttributeBag? bag)
    {
        if (bag is not null)
        {
            foreach (var tint in Tints)
                if (bag.TryGet(tint.Key, out var value) && value.AsBool()) return tint.Rgb;
        }

        return OtherwiseRgb;
    }
}
