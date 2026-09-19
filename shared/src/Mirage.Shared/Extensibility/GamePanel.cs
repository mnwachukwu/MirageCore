using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>One thing a declared panel offers the player, under whatever it shows.</summary>
public readonly record struct PanelButton(
    [property: JsonPropertyName("label")] string LabelKey,
    [property: JsonPropertyName("action")] string ActionId);

/// <summary>
/// One line of a panel's list: what it READS as, and what it IS.
///
/// <para>Two attribute keys rather than one, because the caption a player picks by and the thing a verb
/// then acts on are rarely the same. Show "Ironhelm - at war since Tuesday"; retract against the
/// guild it names. A row whose caption reads blank is not drawn, which is how a list
/// as long as the table behind it shows only the part that is filled.</para>
///
/// <para>Both are read off the player's own attributes, live, like everything else a panel shows. A
/// game with nothing to put in an id key can leave it blank and use the caption as the id, which
/// suits a list of plain names.</para>
/// </summary>
public readonly record struct PanelRow(
    [property: JsonPropertyName("label")] string LabelKey,
    [property: JsonPropertyName("id")] string IdKey);

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
/// <para><b>What it is not is a layout language.</b> Values stack, the list sits under them, buttons sit
/// under that, and the engine decides the rest. A game wanting columns, a grid or an image is asking for
/// a UI toolkit on the wire, which is a different and much larger thing than this. What is here covers
/// the shape most game screens actually are: a titled window of labeled values, something to pick from,
/// and verbs under it.</para>
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
    /// The lines the player picks ONE of, in order. Empty for a panel with nothing to choose between.
    ///
    /// <para>🔴 <b>A verb reaching several things needs a way to say which one.</b> Without a list, a
    /// screen about a set of anything - wars to retract, offers to accept, members to promote - has to
    /// ask for a NAME in a box, which means a player reading one off the screen above and typing it back
    /// in. The pick travels with whatever button they press next, so the button says what to do and the
    /// list says what to.</para>
    ///
    /// <para>One list to a panel, for the same reason there is one form: two would need a way to say
    /// which of them a button meant, and a screen wanting that is two screens.</para>
    /// </summary>
    [JsonPropertyName("rows")] public IReadOnlyList<PanelRow> Rows { get; init; } = [];

    /// <summary>The verb a row of this panel’s list goes on the action bar as, carrying that row’s id as
    /// the subject. Blank for a list whose rows are not worth a slot.
    ///
    /// <para>🔴 <b>This is how a game’s own things reach the bar at all.</b> Core lists items itself and
    /// knows nothing about a game’s spellbook, so the panel showing that book is the only place that can
    /// say what its rows are for. A slot then does exactly what pressing that button with that row
    /// picked does — the same verb, the same subject — so the bar is a shortcut rather than a second
    /// feature.</para>
    ///
    /// <para>⚠ The verb still needs <see cref="GameAction.Hotkeyable"/>, which the server checks
    /// when the binding arrives. And the rows’ ids have to BE numbers: a slot carries a number, and a list
    /// built for a person to read may carry captions instead. A row whose id is not a number offers
    /// nothing.</para></summary>
    [JsonPropertyName("hotkeyAction")] public string HotkeyAction { get; init; } = string.Empty;

    /// <summary>The verb fired as soon as a row of this panel's list is highlighted, carrying that
    /// row's id as the subject. Blank for a list nothing needs to know about until a button is pressed.
    ///
    /// <para><b>Without this a list cannot say anything until a button is pressed.</b> A highlight is
    /// client-side, so a screen that wants to describe what you just clicked on has to grow a button
    /// whose only job is to carry the pick over — and the player has to press it to find out what they
    /// selected, which is one press more than the screen appears to need.</para>
    ///
    /// <para>⚠ It fires on the CHANGE, including the first row a freshly opened list settles on, and
    /// not again until the highlight moves. So the verb behind it describes rather than acts: it is
    /// raised by looking, and a game that spends something here spends it on a mouse movement.</para></summary>
    [JsonPropertyName("pickedAction")] public string PickedAction { get; init; } = string.Empty;

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

    /// <summary>
    /// While this holds, the panel is up. Blank for a window the player opens and closes themselves,
    /// which is most of them.
    ///
    /// <para>🔴 <b>How a game puts a readout on the screen without drawing on the world.</b> A score
    /// that has to be visible during a fight, a countdown over a body that cannot act, the state of the
    /// ground being fought over: none of them is a window somebody chose to open, and none should be
    /// reachable by a key or sit on the sidebar the rest of the week.</para>
    ///
    /// <para>On a panel the player opens, this is a gate rather than a schedule: the key and the verb
    /// refuse while it does not hold, and a window already up closes when it stops.</para>
    ///
    /// <para>Asked of the attributes the client already holds, so it opens and closes with no round
    /// trip and cannot disagree with the server about whether it should be showing.</para>
    /// </summary>
    [JsonPropertyName("while")] public ActionCondition While { get; init; } = ActionCondition.Always;

    /// <summary>
    /// Whether the player may dismiss it. False for an ordinary window; true for one the GAME takes
    /// down, by the condition above ceasing to hold.
    ///
    /// <para>⚠ <b>Held requires <see cref="While"/>.</b> A window with no close control and no
    /// condition can never be taken away, leaving the player a rectangle over their game
    /// forever. A panel declaring one without the other is refused by name.</para>
    ///
    /// <para>A held panel still needs a way OUT of whatever it is about, by a button on it rather
    /// than the close control: Respawn on a death panel, Leave on a party panel. Closing the
    /// window and leaving the thing are different acts, and a corner X that did both is how a player
    /// leaves a party by tidying their screen.</para>
    /// </summary>
    [JsonPropertyName("held")] public bool Held { get; init; }

    /// <summary>The key that opens and closes it, or blank for a panel reached only through an action.
    /// Must be one of <see cref="GameKey.Offered"/>.
    ///
    /// <para>Opening is entirely the client's: a panel shows attributes it already holds, so a key press
    /// costs no round trip. Wherever the panel is offered to the player, the key is shown beside its
    /// name, so nobody has to be told it exists.</para></summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    /// <summary>How big it opens, in the client's reference pixels. Zero takes the engine's default,
    /// which suits a game with no opinion about its own window.</summary>
    [JsonPropertyName("w")] public int Width { get; init; }

    /// <inheritdoc cref="Width"/>
    [JsonPropertyName("h")] public int Height { get; init; }

    /// <summary>
    /// How small the player may drag it, in the client's reference pixels. Zero takes the engine's own
    /// floor, which suits a panel with no opinion.
    ///
    /// <para>⚠ A panel is resizable, and a floor stops a resize turning it into a title bar with
    /// nothing under it. The engine's floor is one number for every panel and cannot know that a
    /// form of four boxes needs more height than a list of names — so a screen that has a shape worth
    /// keeping says so here, and the player keeps every size above it.</para>
    /// </summary>
    [JsonPropertyName("minW")] public int MinWidth { get; init; }

    /// <inheritdoc cref="MinWidth"/>
    [JsonPropertyName("minH")] public int MinHeight { get; init; }
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
