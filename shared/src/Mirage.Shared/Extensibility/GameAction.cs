using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>Where the client offers a declared action.</summary>
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
}

/// <summary>
/// Something a game lets the player do, offered by a client that has never heard of it.
///
/// <para><b>This is a declaration, not code.</b> A stock client draws the label where the surface says,
/// and invoking it sends the action's id back with whatever the player was pointing at. The game's rule
/// runs on the server, where every other rule runs. Nothing about the behaviour crosses the wire, so
/// nothing has to be deployed beside the client.</para>
///
/// <para><b>That is the whole trade, and it is what makes it possible at all.</b> A seam that let a game
/// send BEHAVIOUR to a client would be a seam that shipped code to every player, and the client would
/// have to run it. What travels here is a name and a caption; what happens is the server's business.</para>
/// </summary>
public sealed record GameAction
{
    /// <summary>What comes back when the player picks it. Owned by one module, and the name the handler
    /// is asked about — so it is stable in the way a packet command is, not a caption.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    /// <summary>Localization key for what the player reads.</summary>
    [JsonPropertyName("labelKey")] public string LabelKey { get; init; } = string.Empty;

    /// <summary>Where it is offered.</summary>
    [JsonPropertyName("surface")] public ActionSurface Surface { get; init; }

    /// <summary>The heading it sits under, so a game's verbs read as a group rather than scattered
    /// through Core's own menu. Localization key; blank puts them under the game's name.</summary>
    [JsonPropertyName("groupKey")] public string GroupKey { get; init; } = string.Empty;

    /// <summary>Where it sits among the game's other actions on that surface. Lower shows first.</summary>
    [JsonPropertyName("ordinal")] public int Ordinal { get; init; }

    /// <summary>When it is offered at all, as a question about what the player already carries. The
    /// default asks nothing, so a verb that says nothing about this is always offered.
    ///
    /// <para>Read by the client to grey the entry out and by the server to refuse the invoke, through
    /// the same <see cref="ActionCondition.Holds"/>. A shortcut bound to a verb whose condition does not
    /// hold does nothing, for the same reason.</para></summary>
    [JsonPropertyName("when")] public ActionCondition When { get; init; } = ActionCondition.Always;

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
    /// still in the world is a question for <see cref="IWorld"/> at the moment the handler asks.</param>
    /// <param name="at">The square they picked it on. A client names a place it can see; whether the
    /// player is close enough to act on it is the game's question, because how far a game's own verb
    /// reaches is not something Core could know.</param>
    void Invoke(EntityHandle from, string actionId, EntityHandle on, in WorldPlace at);
}
