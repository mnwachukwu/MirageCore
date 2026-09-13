using Mirage.Shared.Extensibility;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

/// <summary>
/// S→C, once per session before anything else is synced: what the loaded game's attribute keys are
/// called, and which small number stands for each on the wire.
///
/// <para><b>The client learns the numbering rather than agreeing on it.</b> Ordinals are handed out by
/// the server's schema in declaration order, so two servers running different module sets number the
/// same key differently and neither is wrong. A client that assumed a constant would read one game's
/// values as another's.</para>
///
/// <para>A world whose game declares nothing sends an empty list, which is the ordinary case for Core
/// on its own.</para>
/// </summary>
public sealed record AttributeSchemaPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AttributeSchema;

    /// <summary>One row per declared key. Visibility is included because the client uses it to know
    /// which values it may be told about at all — a key it never receives is not a bug to report.</summary>
    [JsonPropertyName("keys")] public IReadOnlyList<Row> Keys { get; init; } = [];

    public readonly record struct Row(
        [property: JsonPropertyName("ord")] int Ordinal,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("vis")] AttributeVisibility Visibility,
        [property: JsonPropertyName("label")] string? LabelKey);
}

/// <summary>
/// S→C: some of one body's attributes changed, here are their new values.
///
/// <para><b>Only what the receiver may see, and only what moved.</b> The server projects a bag against
/// the schema and the viewer's closeness before building this, so an onlooker is never sent an owner's
/// values and no filtering is left to the client. Keys ride as ordinals because this is the packet that
/// repeats — once per body per change — and a name per entry would be most of the payload.</para>
///
/// <para>A value is the natural JSON one, exactly as it is in an authored file: a number, a bool, or a
/// string. There is no kind tag, so a line stays readable to somebody watching the wire.</para>
/// </summary>
public sealed record AttributeSyncPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.AttributeSync;

    /// <summary>Whose attributes these are.</summary>
    [JsonPropertyName("who")] public EntityHandle Who { get; init; }

    /// <summary>The changed entries. Never empty: a sync with nothing in it is not sent.</summary>
    [JsonPropertyName("set")] public IReadOnlyList<Entry> Set { get; init; } = [];

    public readonly record struct Entry(
        [property: JsonPropertyName("ord")] int Ordinal,
        [property: JsonPropertyName("v")] AttributeValue Value);
}

/// <summary>
/// S→C, once per session before any attribute can arrive: which of this game's attributes are drawn as
/// rows over a body's head, and in what color.
///
/// <para><b>Which bars exist is the loaded game's decision</b>, so a client compiled against a fixed set
/// could only ever draw one game's. A world whose game declares none sends an empty list, and nothing
/// is drawn over anyone.</para>
///
/// <para>The values themselves travel as ordinary attribute syncs — there is no separate bar traffic, so
/// a bar cannot disagree with the number it is drawing.</para>
/// </summary>
public sealed record OverheadBarsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.OverheadBars;

    /// <summary>One row per declared bar, already in draw order.</summary>
    [JsonPropertyName("bars")] public IReadOnlyList<Row> Bars { get; init; } = [];

    public readonly record struct Row(
        [property: JsonPropertyName("v")] string ValueKey,
        [property: JsonPropertyName("max")] string MaxKey,
        [property: JsonPropertyName("rgb")] int Rgb);
}

/// <summary>
/// S→C, once per session before any attribute can arrive: what each surface shows about a body, and how.
///
/// <para><b>The rows are declared, not computed.</b> A client resolves these against the values it
/// already holds, so a number moving costs an ordinary attribute sync and no display traffic at all —
/// and a row cannot show something the attribute does not say.</para>
///
/// <para>A world whose game declares none sends an empty list, and every surface draws only what Core
/// itself puts there.</para>
/// </summary>
public sealed record DisplayFieldsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.DisplayFields;

    /// <summary>One row per declared field, already in draw order.</summary>
    [JsonPropertyName("fields")] public IReadOnlyList<Row> Fields { get; init; } = [];

    public readonly record struct Row(
        [property: JsonPropertyName("s")] string Surface,
        [property: JsonPropertyName("v")] string ValueKey,
        [property: JsonPropertyName("max")] string MaxKey,
        [property: JsonPropertyName("label")] string? LabelKey,
        [property: JsonPropertyName("rgb")] int Rgb,
        [property: JsonPropertyName("style")] DisplayStyle Style);
}

/// <summary>
/// S→C, once per session: what this game lets the player do.
///
/// <para><b>A caption and an id, and nothing else.</b> The client offers the label where the surface
/// says and sends the id back when it is picked. No behaviour crosses the wire, which is what lets a
/// stock client offer a verb it has never heard of without anything being deployed beside it.</para>
///
/// <para>A world whose game declares none sends an empty list, and every menu holds only Core's own
/// items.</para>
/// </summary>
public sealed record GameActionsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GameActions;

    /// <summary>One row per declared action, already in the order they are offered.</summary>
    [JsonPropertyName("actions")] public IReadOnlyList<Row> Actions { get; init; } = [];

    /// <summary>One action as the client needs it: what to call it, where to offer it, the panel
    /// picking it opens, and the key that reaches it without the menu. <c>OpensPanel</c> is blank for an
    /// action that only tells the server, and <c>Key</c> for one with no shortcut.</summary>
    public readonly record struct Row(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("label")] string LabelKey,
        [property: JsonPropertyName("surface")] ActionSurface Surface,
        [property: JsonPropertyName("group")] string GroupKey,
        [property: JsonPropertyName("opens")] string OpensPanel,
        [property: JsonPropertyName("key")] string Key);
}

/// <summary>
/// C→S: the player picked one of the game's own actions.
///
/// <para>The square is what the client was pointing at, named the way every other placed thing is. How
/// far a game's verb reaches is the game's question — Core does not know what the verb is, so it cannot
/// know what distance would be reasonable for it.</para>
/// </summary>
public sealed record InvokeActionPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.InvokeAction;

    [JsonPropertyName("action")] public string Action { get; init; } = string.Empty;

    [JsonPropertyName("map")] public int MapNum { get; init; }

    [JsonPropertyName("x")] public int X { get; init; }

    [JsonPropertyName("y")] public int Y { get; init; }
}

/// <summary>
/// S→C, once per session: the screens this game paints.
///
/// <para>A title, the display surface that fills the body, and the verbs under it. Everything a panel
/// SHOWS the client already holds as attributes, and everything it DOES is an action id — so a window a
/// client was never compiled against costs one declaration and no code.</para>
/// </summary>
public sealed record GamePanelsPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.GamePanels;

    [JsonPropertyName("panels")] public IReadOnlyList<GamePanel> Panels { get; init; } = [];
}
