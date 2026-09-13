using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>The surfaces Core itself asks. A surface is an open string, so a game may invent its own and
/// render it from its own client code; these are the ones the engine draws without being asked.</summary>
public static class DisplaySurfaces
{
    /// <summary>The sidebar the player reads while walking around.</summary>
    public const string Hud = "hud";
}

/// <summary>
/// One value a game wants shown, named by the attribute it reads rather than by a number it carries.
///
/// <para><b>A declaration, not a reading.</b> The engine resolves this against an entity's
/// <see cref="AttributeBag"/> wherever the named surface is drawn, so a game changes what the player
/// sees by changing the value — there is no display state to keep in step, and nothing to push when a
/// number moves. It is the same bargain the overhead bars make, one shape wider.</para>
///
/// <para><b>The game decides the content; the engine decides the place.</b> Nothing here says where on
/// a surface a row lands, how wide it is, or what it sits beside. A surface takes the rows it has room
/// for, in order, and draws them the way that surface draws things.</para>
/// </summary>
public sealed record DisplayField
{
    /// <summary>Which surface asks for this. See <see cref="DisplaySurfaces"/>.</summary>
    [JsonPropertyName("surface")] public string Surface { get; init; } = DisplaySurfaces.Hud;

    /// <summary>The attribute holding the value. A body carrying nothing for it shows no row, which is
    /// how one declaration serves a world where only some bodies have the thing.</summary>
    [JsonPropertyName("valueKey")] public string ValueKey { get; init; } = string.Empty;

    /// <summary>For <see cref="DisplayStyle.Meter"/>: the attribute holding what full means. Ignored by
    /// every other style.</summary>
    [JsonPropertyName("maxKey")] public string MaxKey { get; init; } = string.Empty;

    /// <summary>Localization key for the caption, or null for a row that shows only its value.</summary>
    [JsonPropertyName("labelKey")] public string? LabelKey { get; init; }

    /// <summary>The color to draw it in, packed <c>0xRRGGBB</c>.</summary>
    [JsonPropertyName("rgb")] public int Rgb { get; init; } = GameColor.Rgb[GameColor.White];

    [JsonPropertyName("style")] public DisplayStyle Style { get; init; }

    /// <summary>Where this sits among the others on its surface. Lower draws first; equal values keep
    /// declaration order.</summary>
    [JsonPropertyName("ordinal")] public int Ordinal { get; init; }

    /// <summary>This field read against one body's values, or null when that body has nothing to say
    /// about it and the row should not be drawn.
    ///
    /// <para>A <see cref="DisplayStyle.Heading"/> carries no value and is therefore always drawn — it is
    /// the one row whose whole job is to say something about the rows under it.</para></summary>
    public DisplayRow? RowIn(AttributeBag? bag)
    {
        if (Style == DisplayStyle.Heading) return DisplayRow.OfHeading(LabelKey ?? string.Empty);
        if (bag is null || !bag.TryGet(ValueKey, out var value)) return null;

        return Style switch
        {
            DisplayStyle.Meter => MeterRow(bag, value),
            DisplayStyle.Badge => BadgeRow(value),
            _ => DisplayRow.OfText(LabelKey, value.AsText(), Rgb),
        };
    }

    private DisplayRow? MeterRow(AttributeBag bag, AttributeValue value)
    {
        // A meter with no ceiling is not an empty bar, it is a bar nobody said the size of. Drawing it
        // would claim the body is at zero, which is a different and wrong statement.
        if (!bag.TryGet(MaxKey, out var max)) return null;
        double ceiling = max.AsDouble();
        return ceiling > 0 ? DisplayRow.OfMeter(LabelKey, value.AsDouble(), ceiling, Rgb) : null;
    }

    /// <summary>A badge states something that is CURRENTLY true, so a flag that is false and an empty
    /// word are both nothing to say rather than a badge reading "false".</summary>
    private DisplayRow? BadgeRow(AttributeValue value)
    {
        if (value.Kind == AttributeKind.Flag)
            return value.AsBool() ? DisplayRow.OfBadge(LabelKey ?? ValueKey, Rgb) : null;

        string text = value.AsText();
        return text.Length > 0 ? DisplayRow.OfBadge(text, Rgb) : null;
    }
}

/// <summary>
/// Everything a world shows about a body, grouped by the surface that asks for it.
///
/// <para>Indexed once at load because every frame that draws a surface asks it the same question.</para>
/// </summary>
public sealed class DisplayFieldSet
{
    /// <summary>A world that shows nothing anywhere. What Core describes before a game declares a
    /// field, and what every surface then renders as empty rather than as a gap.</summary>
    public static readonly DisplayFieldSet Empty = new([]);

    private readonly Dictionary<string, IReadOnlyList<DisplayField>> _bySurface;

    public DisplayFieldSet(IReadOnlyList<DisplayField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Fields = [.. fields.OrderBy(f => f.Ordinal)];
        _bySurface = Fields
            .GroupBy(f => f.Surface, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DisplayField>)[.. g], StringComparer.Ordinal);
    }

    /// <summary>Every field, in draw order, across every surface.</summary>
    public IReadOnlyList<DisplayField> Fields { get; }

    public int Count => Fields.Count;

    /// <summary>What <paramref name="surface"/> shows, in draw order. Empty for a surface no game
    /// declared anything for, which is most of them in most games.</summary>
    public IReadOnlyList<DisplayField> For(string surface)
        => _bySurface.TryGetValue(surface, out var fields) ? fields : [];

    /// <summary>What <paramref name="surface"/> draws for the body carrying <paramref name="bag"/>.
    ///
    /// <para>Fields the body has no value for are left out rather than drawn blank, so the rows a
    /// surface receives are exactly the rows it should draw.</para></summary>
    public DisplayProjection Project(string surface, AttributeBag? bag)
    {
        var fields = For(surface);
        if (fields.Count == 0) return DisplayProjection.Nothing(surface);

        List<DisplayRow>? rows = null;
        foreach (var field in fields)
            if (field.RowIn(bag) is { } row)
                (rows ??= new List<DisplayRow>(fields.Count)).Add(row);

        return rows is null ? DisplayProjection.Nothing(surface) : new DisplayProjection(surface, rows);
    }
}
