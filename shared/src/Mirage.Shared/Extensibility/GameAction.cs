using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>Where the client offers a declared action.</summary>
/// <summary>What the client does with a verb whose <see cref="GameAction.When"/> does not hold.</summary>
public enum ActionUnmet : byte
{
    /// <summary>Drawn, dim, and not clickable. The player can see the verb exists and that something
    /// they do not have would reach it.</summary>
    Gray = 0,

    /// <summary>Not drawn. What a verb about something a player may never have wants, since a permanent
    /// dim entry is one they read past every time. On the HUD the buttons below it close the gap.</summary>
    Hide = 1,
}

public enum ActionSurface : byte
{
    /// <summary>The right-click menu on a square, under the game's own heading. What a verb that acts on
    /// a PLACE wants — the thing in front of you, the ground you are standing on.</summary>
    Tile = 0,

    /// <summary>The right-click menu on another player, under the game's own heading. Invoking one names
    /// that player, so a verb here knows who it was used on.</summary>
    Player = 1,

    /// <summary>The right-click menu on an NPC. Invoking one names that NPC the way every other placed
    /// body is named — by where it SPAWNS, so a body that has wandered onto the next map is still
    /// itself.</summary>
    Npc = 2,

    /// <summary>A button on the HUD, under Core's own. A verb with no target and no place: opening one of
    /// the game's screens, or telling the server something about nothing in particular.</summary>
    Hud = 3,

    /// <summary>Nowhere on its own. The verb is declared, and the game says where it appears: a button
    /// on one of its own panels, a choice in one of its conversations, or a key it bound.
    ///
    /// <para>🔴 <b>Without this a verb has to be in a menu to exist at all</b>, and a game whose
    /// screens hold its verbs - a guild hall, a training hall, a vault - would have to hang every one of
    /// them off whatever the player happens to be pointing at. What follows is a right-click on a
    /// passing shopkeeper offering to donate to your guild, which is where the verb is reachable rather
    /// than where it belongs.</para></summary>
    None = 4,
}

/// <summary>
/// Something a game lets the player do, offered by a client that has never heard of it.
///
/// <para><b>This is a declaration, not code.</b> A stock client draws the label where the surface says,
/// and invoking it sends the action's id back with whatever the player was pointing at. The game's rule
/// runs on the server, where every other rule runs. Nothing about the behavior crosses the wire, so
/// nothing has to be deployed beside the client.</para>
///
/// <para><b>Without that trade, none of this would be possible.</b> A seam that let a game
/// send BEHAVIOR to a client would be a seam that shipped code to every player, and the client would
/// have to run it. What travels here is a name and a caption; what happens is the server's business.</para>
/// </summary>
public sealed record GameAction
{
    /// <summary>What comes back when the player picks it. Owned by one module, and the name the handler
    /// is asked about — so it is stable in the way a packet command is, not a caption.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    /// <summary>Localization key for what the player reads.</summary>
    [JsonPropertyName("labelKey")] public string LabelKey { get; init; } = string.Empty;

    /// <summary>The glyph beside it, named from <see cref="GameIcon.Offered"/>. Blank draws none.
    ///
    /// <para>A NAME crosses the wire and the client draws its own shape for it: a game cannot ship
    /// geometry to a client it does not control. Blank rather than a default here, unlike a panel or a
    /// family — a menu of verbs all wearing the same placeholder glyph is worse than a menu of
    /// captions.</para></summary>
    [JsonPropertyName("icon")] public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Whether picking this also does what Core's reach key would have done: a shop, a conversation,
    /// or the body's own line.
    ///
    /// <para>🔴 <b>This exists because a game that binds E takes the reach key outright.</b> That is
    /// deliberate — sharing a key between a game's verb and Core's reaching is worse — but without a
    /// way to hand interaction back, binding E would silently cost the world its shops and its
    /// conversations. A game that takes the key offers this on the menu, or on a key of its own.</para>
    ///
    /// <para>Only meaningful for a verb offered on a creature: there is nothing else to reach for.</para>
    /// </summary>
    [JsonPropertyName("interacts")] public bool Interacts { get; init; }

    /// <summary>Where it is offered. <see cref="ActionSurface.None"/> for one the game places itself,
    /// on a panel of its own or on a key.</summary>
    [JsonPropertyName("surface")] public ActionSurface Surface { get; init; }

    /// <summary>
    /// Whether this verb acts on whatever the player has TARGETED, when they did not point at anything
    /// while using it.
    ///
    /// <para>🔴 <b>Targeting is Core's, and a game should not be rebuilding it.</b> Picking a body
    /// out of a crowd is cycling with Tab, clicking one, and the line of sight and footprint arithmetic
    /// that decides which body a pixel belongs to - all of which Core already does, and none of which is
    /// about any particular game. Aiming at YOURSELF is part of the same thing: Ctrl+Tab selects
    /// the caster, so a spell that heals needs no verb of its own and no button that says
    /// "on yourself".</para>
    ///
    /// <para>The selection is read from the SERVER's copy, which is the one the client told it about
    /// when the player made it. So an aimed verb cannot be pointed somewhere the player never
    /// pointed.</para>
    ///
    /// <para>Off by default: a verb about a PLACE - digging, planting, laying claim - would otherwise be
    /// handed a body that has nothing to do with it.</para>
    /// </summary>
    [JsonPropertyName("aimed")] public bool Aimed { get; init; }

    /// <summary>The heading it sits under, so a game's verbs read as a group rather than scattered
    /// through Core's own menu. Localization key; blank puts them under the game's name.</summary>
    [JsonPropertyName("groupKey")] public string GroupKey { get; init; } = string.Empty;

    /// <summary>Where it sits among the game's other actions on that surface. Lower shows first.</summary>
    [JsonPropertyName("ordinal")] public int Ordinal { get; init; }

    /// <summary>Whether the player may put this verb on the action bar.
    ///
    /// <para>Off by default, because most verbs are not worth a slot and nobody reads a menu that
    /// offers "assign to hotkey" on every line of a game’s whole vocabulary. Say it on the few
    /// a player would reach for under pressure.</para>
    ///
    /// <para>⚠ Says nothing unless the game declared a bar. A hotkeyable verb in a world with no
    /// <see cref="HotkeyBar"/> never gets offered for binding: the quiet half of a feature rather
    /// than an error, since the bar has to exist first.</para></summary>
    [JsonPropertyName("hotkeyable")] public bool Hotkeyable { get; init; }

    /// <summary>When it is offered at all, as a question about what the player already carries. The
    /// default asks nothing, so a verb that says nothing about this is always offered.
    ///
    /// <para>Read by the client to withhold the verb and by the server to refuse the invoke, through
    /// the same <see cref="ActionCondition.Holds"/>. <see cref="Unmet"/> says which of the two things the
    /// client does with it. A shortcut bound to a verb whose condition does not hold does nothing either
    /// way, since there is nothing to draw on a keyboard.</para></summary>
    [JsonPropertyName("when")] public ActionCondition When { get; init; } = ActionCondition.Always;

    /// <summary>Gray or gone, while <see cref="When"/> does not hold. Gray by default, which suits
    /// a verb the player could reach today; hidden is for one they may never be able to.</summary>
    [JsonPropertyName("unmet")] public ActionUnmet Unmet { get; init; } = ActionUnmet.Gray;

    /// <summary>A key that invokes this without opening the menu, or blank for one the player has to go
    /// and find. Must be one of <see cref="GameKey.Offered"/>.
    ///
    /// <para>A bound key acts on the square the player is facing, which is the square the menu would
    /// have opened on. So the key and the menu item are the same verb reaching the same place, and a
    /// game declaring both has given the player a shortcut rather than a second feature.</para></summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    /// <summary>A <see cref="GamePanel.Id"/> this opens, or blank for one that only tells the server.
    ///
    /// <para>Opening happens on the client and costs no round trip — the panel's contents are attributes
    /// it already holds. An action that both opens a panel AND names a handler does both, which is how a
    /// screen that needs the server to prepare something gets to say so.</para></summary>
    [JsonPropertyName("opens")] public string OpensPanel { get; init; } = string.Empty;
}

/// <summary>
/// Every action a game offers, grouped by the surface that draws it.
///
/// <para>Indexed once at load: a menu opening asks this, and a menu opens on a click rather than on a
/// frame, but the lookup is the same either way.</para>
/// </summary>
public sealed class GameActions
{
    /// <summary>A game that offers the player nothing of its own. What Core describes by itself, and
    /// then every menu holds only Core's own items.</summary>
    public static readonly GameActions Empty = new([]);

    private readonly Dictionary<ActionSurface, IReadOnlyList<GameAction>> _bySurface;

    public GameActions(IReadOnlyList<GameAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        All = [.. actions.OrderBy(a => a.Ordinal)];
        _bySurface = All.GroupBy(a => a.Surface)
                        .ToDictionary(g => g.Key, g => (IReadOnlyList<GameAction>)[.. g]);
    }

    /// <summary>Every action, in the order they are offered.</summary>
    public IReadOnlyList<GameAction> All { get; }

    public int Count => All.Count;

    /// <summary>What <paramref name="surface"/> offers, in order. Empty for a surface a game declared
    /// nothing for, which is most of them in most games.</summary>
    public IReadOnlyList<GameAction> For(ActionSurface surface)
        => _bySurface.TryGetValue(surface, out var actions) ? actions : [];
}

/// <summary>
/// What a game does when the player picks one of its actions.
///
/// <para>Runs on the game thread, in the middle of the read that delivered it, so a handler does its
/// work and returns. One that throws is logged with its name and the player keeps their connection.</para>
/// </summary>
public interface IActionHandler
{
    /// <summary>What this handler is called, for the log line that names it when it throws.</summary>
    string Name { get; }

    /// <summary>The action ids it owns. Each must also be declared with
    /// <see cref="ICoreBuilder.AddAction"/>, or no client will ever offer something that reaches
    /// here.</summary>
    IReadOnlyCollection<string> Actions { get; }

    /// <summary>The player picked <paramref name="actionId"/>.</summary>
    /// <param name="from">Who picked it.</param>
    /// <param name="actionId">Which of <see cref="Actions"/>.</param>
    /// <param name="on">The body it was used on, or <see cref="EntityHandle.None"/> for a verb offered
    /// somewhere with no target — a square, or the HUD. Named rather than resolved: whether that body is
    /// still in the world is for <see cref="IWorld"/> to answer at the moment the handler asks.</param>
    /// <param name="at">The square they picked it on. A client names a place it can see; whether the
    /// player is close enough to act on it is the game's question, because how far a game's own verb
    /// reaches is not something Core could know.</param>
    /// <param name="picked">The line of the panel's list that was selected, as the id that line carried,
    /// or blank for a verb reached from anywhere else. Core passes it through without reading it - what a
    /// row means is entirely the game's.</param>
    void Invoke(EntityHandle from, string actionId, EntityHandle on, in WorldPlace at, string picked);
}
