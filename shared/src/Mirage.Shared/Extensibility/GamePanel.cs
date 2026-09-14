using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>One thing a declared panel offers the player, under whatever it shows.</summary>
public readonly record struct PanelButton(
    [property: JsonPropertyName("label")] string LabelKey,
    [property: JsonPropertyName("action")] string ActionId);

/// <summary>
/// One value a declared panel asks the player FOR, rather than tells them.
///
/// <para><b>Everything a control needs is here, because the client has nothing else to go on.</b> A
/// drop-down carries its own members rather than the id of a set, since choice sets travel to the
/// editor and not to a player's client — an id nothing can resolve is a drop-down with no entries in
/// it.</para>
///
/// <para><see cref="FieldKind.RecordRef"/> never appears. A picker over a family's records needs the
/// records, which a client does not hold.</para>
/// </summary>
public sealed record PanelInput
{
    /// <summary>What the value is called in the message it becomes. The model's own field name.</summary>
    [JsonPropertyName("field")] public string Field { get; init; } = string.Empty;

    /// <summary>Localization key for the caption beside the control.</summary>
    [JsonPropertyName("labelKey")] public string LabelKey { get; init; } = string.Empty;

    /// <summary>What it holds, and therefore which control edits it.</summary>
    [JsonPropertyName("kind")] public FieldKind Kind { get; init; }

    /// <summary>Character limit for <see cref="FieldKind.Text"/>. Zero means unbounded.</summary>
    [JsonPropertyName("maxLength")] public int MaxLength { get; init; }

    /// <summary>Inclusive bounds for a number. Equal values mean unbounded.</summary>
    [JsonPropertyName("min")] public long Min { get; init; }

    /// <inheritdoc cref="Min"/>
    [JsonPropertyName("max")] public long Max { get; init; }

    /// <summary>For <see cref="FieldKind.Choice"/>: the members, in declaration order.</summary>
    [JsonPropertyName("choices")] public IReadOnlyList<string> Choices { get; init; } = [];
}

/// <summary>
/// A screen a game paints, on a client that has never heard of it.
///
/// <para><b>A panel is a title, a surface, and some buttons.</b> Its body is whatever
/// <see cref="DisplayField"/> rows the game declared for <see cref="Surface"/>, read live off the
/// player's own attributes; its buttons carry <see cref="GameAction"/> ids. So both halves of a screen —
/// what it says and what it does — are things the engine already knows how to carry, and neither is
/// code.</para>
///
/// <para><b>What it is not is a layout language.</b> Rows stack, buttons sit under them, and the engine
/// decides the rest. A game wanting columns, a grid, an image or a list of its own is asking for a UI
/// toolkit on the wire, which is a different and much larger thing than this. What is here covers the
/// shape most game screens actually are: a titled window of labeled values, with verbs under it.</para>
/// </summary>
public sealed record GamePanel
{
    /// <summary>What opens it. A <see cref="GameAction.OpensPanel"/> names this.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    /// <summary>Localization key for the title bar.</summary>
    [JsonPropertyName("titleKey")] public string TitleKey { get; init; } = string.Empty;

    /// <summary>The glyph beside it, named from <see cref="GameIcon.Offered"/>. Blank takes
    /// <see cref="GameIcon.Default"/>.
    ///
    /// <para>A NAME crosses the wire and each surface draws its own shape for it: a game cannot ship
    /// geometry to a client it does not control. Every game that named nothing drew the same glyph
    /// before this existed, which made two families in one game indistinguishable from each other and
    /// from one of Core's own sections.</para></summary>
    [JsonPropertyName("icon")] public string Icon { get; init; } = string.Empty;

    /// <summary>The display surface whose rows fill the body. A surface with no fields declared for it
    /// is a panel with only its buttons, which is a perfectly ordinary thing for a panel to be.</summary>
    [JsonPropertyName("surface")] public string Surface { get; init; } = string.Empty;

    /// <summary>What the player can do from here, in order.</summary>
    [JsonPropertyName("buttons")] public IReadOnlyList<PanelButton> Buttons { get; init; } = [];

    /// <summary>
    /// The message this panel composes, or blank for one that only shows things.
    ///
    /// <para>🔴 <b>This is the half a stock client was missing.</b> A client originates a verb — an
    /// action id and the square it was used on — and nothing else, so "the player filled this in and
    /// sent it" had no carrier. A panel that names a message gets <see cref="Inputs"/> to fill and a
    /// send button, and the line it sends reaches the game's own <c>OnMessage</c>.</para>
    ///
    /// <para>Both halves arrive together or neither does: a panel with inputs and no message would
    /// collect values nothing sends, and a message with no inputs would send an empty one.</para>
    /// </summary>
    [JsonPropertyName("asks")] public string Asks { get; init; } = string.Empty;

    /// <summary>What the player fills in, in the order the model declared them. Empty unless
    /// <see cref="Asks"/> names a message.</summary>
    [JsonPropertyName("inputs")] public IReadOnlyList<PanelInput> Inputs { get; init; } = [];

    /// <summary>Localization key for the button that sends <see cref="Asks"/>.</summary>
    [JsonPropertyName("sendLabel")] public string SendLabelKey { get; init; } = string.Empty;

    /// <summary>The key that opens and closes it, or blank for a panel reached only through an action.
    /// Must be one of <see cref="GameKey.Offered"/>.
    ///
    /// <para>Opening is entirely the client's: a panel shows attributes it already holds, so a key press
    /// costs no round trip. Wherever the panel is offered to the player, the key is shown beside its
    /// name — a shortcut nothing displays is a shortcut nobody finds.</para></summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    /// <summary>How big it opens, in the client's reference pixels. Zero takes the engine's default,
    /// which is what a game with no opinion about its own window wants.</summary>
    [JsonPropertyName("w")] public int Width { get; init; }

    /// <inheritdoc cref="Width"/>
    [JsonPropertyName("h")] public int Height { get; init; }
}

/// <summary>
/// Every screen a game paints, by the id that opens it.
/// </summary>
public sealed class GamePanels
{
    /// <summary>A game with no screens of its own. What Core describes by itself.</summary>
    public static readonly GamePanels Empty = new([]);

    private readonly Dictionary<string, GamePanel> _byId;

    public GamePanels(IReadOnlyList<GamePanel> panels)
    {
        ArgumentNullException.ThrowIfNull(panels);
        All = panels;
        _byId = panels.ToDictionary(p => p.Id, StringComparer.Ordinal);
    }

    /// <summary>Every panel, in the order they were declared.</summary>
    public IReadOnlyList<GamePanel> All { get; }

    public int Count => All.Count;

    /// <summary>The panel with this id, or null for one no game declared. Null is an ordinary answer:
    /// an action may name a panel a later version of the game removed.</summary>
    public GamePanel? Find(string? id)
        => id is not null && _byId.TryGetValue(id, out var panel) ? panel : null;
}
