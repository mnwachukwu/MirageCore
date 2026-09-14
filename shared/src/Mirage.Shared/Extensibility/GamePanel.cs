using System.Text.Json.Serialization;

namespace Mirage.Shared.Extensibility;

/// <summary>One thing a declared panel offers the player, under whatever it shows.</summary>
public readonly record struct PanelButton(
    [property: JsonPropertyName("label")] string LabelKey,
    [property: JsonPropertyName("action")] string ActionId);

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

    /// <summary>The display surface whose rows fill the body. A surface with no fields declared for it
    /// is a panel with only its buttons, which is a perfectly ordinary thing for a panel to be.</summary>
    [JsonPropertyName("surface")] public string Surface { get; init; } = string.Empty;

    /// <summary>What the player can do from here, in order.</summary>
    [JsonPropertyName("buttons")] public IReadOnlyList<PanelButton> Buttons { get; init; } = [];

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
